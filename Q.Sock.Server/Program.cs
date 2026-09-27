using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Hubs;
using Q.Sock.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddDbContextFactory<ChatDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("ChatDatabase")));
builder.Services.AddIdentityCore<UserEntity>(options =>
{
	options.User.RequireUniqueEmail = false;
	options.Password.RequiredLength = 8;
})
	.AddRoles<IdentityRole<Guid>>()
	.AddEntityFrameworkStores<ChatDbContext>()
	.AddSignInManager();
builder.Services.AddScoped<JwtTokenService>();

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
			ValidateLifetime = true
		};
		options.Events = new JwtBearerEvents
		{
			OnMessageReceived = context =>
			{
				var token = context.Request.Query["access_token"];
				if (!string.IsNullOrEmpty(token) &&
					context.HttpContext.Request.Path.StartsWithSegments("/chat"))
					context.Token = token;
				return Task.CompletedTask;
			}
		};
	});
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
	dbContext.Database.Migrate();
}

app.MapControllers();
app.MapHub<ChatHub>("/chat");

app.Run("http://localhost:5000");