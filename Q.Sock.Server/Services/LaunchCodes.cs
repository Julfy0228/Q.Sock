using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Data;

namespace Q.Sock.Server.Services;

public sealed record LaunchPayload(Guid UserId, Guid? ChatId, Guid? CallId);

public static class LaunchCodes
{
    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    public static async Task<string> Create(ChatDbContext db, LaunchPayload payload)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.LaunchNonces.Add(
            new()
            {
                Id = "code:" + Hash(code),
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(60),
                Payload = JsonSerializer.Serialize(payload),
            }
        );
        await db.SaveChangesAsync();
        return code;
    }

    public static async Task<LaunchPayload> Consume(ChatDbContext db, string code)
    {
        var id = "code:" + Hash(code);
        var row =
            await db
                .LaunchNonces.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.ExpiresAtUtc > DateTime.UtcNow)
            ?? throw new ApiException(401, "Ссылка входа просрочена или использована.");
        if (await db.LaunchNonces.Where(x => x.Id == id).ExecuteDeleteAsync() != 1)
            throw new ApiException(401, "Ссылка уже использована.");
        return JsonSerializer.Deserialize<LaunchPayload>(row.Payload)!;
    }
}
