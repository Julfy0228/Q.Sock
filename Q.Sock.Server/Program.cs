using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;

using Q.Sock.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddDbContextFactory<ChatDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("ChatDatabase")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
	dbContext.Database.EnsureCreated();
}

app.MapHub<ChatHub>("/chat");

app.Run("http://localhost:5000");