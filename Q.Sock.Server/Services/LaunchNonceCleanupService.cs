using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;

namespace Q.Sock.Server.Services;

public sealed class LaunchNonceCleanupService(
    IServiceScopeFactory scopes,
    ILogger<LaunchNonceCleanupService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
                await db
                    .LaunchNonces.Where(nonce => nonce.ExpiresAtUtc < DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Не удалось удалить истёкшие коды перехода.");
            }
        }
    }
}
