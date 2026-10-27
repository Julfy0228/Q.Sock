using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Hubs;
using Q.Sock.Server.Modules;
using Q.Sock.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
if (builder.Configuration["InstanceConfiguration"] is { Length: > 0 } instanceConfiguration)
    builder.Configuration.AddJsonFile(Path.GetFullPath(instanceConfiguration), optional: false);
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);

var stateDirectory = builder.Configuration["State:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "state");
Directory.CreateDirectory(stateDirectory);
builder
    .Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(stateDirectory, "keys")));
var configuredKey = builder.Configuration["Jwt:Key"];
if (
    string.IsNullOrWhiteSpace(configuredKey)
    || configuredKey.StartsWith("development-only", StringComparison.Ordinal)
)
{
    var keyPath = Path.Combine(stateDirectory, "jwt.key");
    if (!File.Exists(keyPath))
        File.WriteAllText(keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
    builder.Configuration["Jwt:Key"] = File.ReadAllText(keyPath).Trim();
}

builder.Services.AddControllers();
builder.Services.AddSignalR(options => options.AddFilter<ActiveSessionHubFilter>());
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<ExternalAuthentication>();
builder.Services.AddScoped<MessengerService>();
builder.Services.AddScoped<CallService>();
builder.Services.AddSingleton<ClientPluginCatalog>();
builder.Services.AddSingleton<CallPresence>();
builder.Services.AddHostedService<CallPolicyReconciler>();
builder.Services.AddHttpClient();
builder.Services.AddMessengerModules(builder.Configuration);
builder.Services.AddRateLimiter(options =>
    options.AddPolicy(
        "auth",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }
            )
    )
);
builder.Services.AddDbContextFactory<ChatDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChatDatabase"))
);
builder
    .Services.AddIdentityCore<UserEntity>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        options.Password.RequiredLength = 4;
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ChatDbContext>()
    .AddSignInManager();
builder.Services.AddScoped<JwtTokenService>();

var jwt = builder.Configuration.GetSection("Jwt");
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ValidateLifetime = true,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sessionIdClaim = context.Principal?.FindFirstValue("sid");
                if (!Guid.TryParse(sessionIdClaim, out var sessionId))
                {
                    context.Fail("Token does not identify a session.");
                    return;
                }

                var factory = context.HttpContext.RequestServices.GetRequiredService<
                    IDbContextFactory<ChatDbContext>
                >();
                await using var db = await factory.CreateDbContextAsync();
                var uid = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var active =
                    Guid.TryParse(uid, out var userId)
                    && await db.Users.AnyAsync(u => u.Id == userId && !u.IsSuspended)
                    && await db.DeviceSessions.AnyAsync(session =>
                        session.Id == sessionId
                        && session.RevokedAtUtc == null
                        && session.UserId == userId
                        && session.ExpiresAtUtc > DateTime.UtcNow
                    );
                if (!active)
                    context.Fail("Session is no longer active.");
            },
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (
                    !string.IsNullOrEmpty(token)
                    && context.HttpContext.Request.Path.StartsWithSegments("/chat")
                )
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.Use(
    async (context, next) =>
    {
        try
        {
            await next();
        }
        catch (ApiException e)
        {
            context.Response.StatusCode = e.Status;
            await context.Response.WriteAsJsonAsync(new { error = e.Message });
        }
    }
);
app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        await next();
    }
);
app.UseDefaultFiles();
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".pdb"] = "application/octet-stream";
contentTypes.Mappings[".dat"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    dbContext.Database.Migrate();
    await DatabaseBootstrap.Initialize(scope.ServiceProvider);
    foreach (var module in scope.ServiceProvider.GetRequiredService<ModuleRegistry>().Modules)
        await module.Initialize(scope.ServiceProvider);
}

app.MapControllers();
app.MapHub<ChatHub>("/chat");
app.MapMessengerModules();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet(
    "/app/",
    (HttpContext context) => Results.Redirect("/avalonia/index.html" + context.Request.QueryString)
);
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
