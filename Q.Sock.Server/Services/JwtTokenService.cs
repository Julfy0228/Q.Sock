using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Q.Sock.Server.Data;
using Q.Sock.Server.Entities;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Server.Services;

public sealed class JwtTokenService(
    IConfiguration configuration,
    UserManager<UserEntity> userManager,
    ChatDbContext dbContext
)
{
    public async Task<AuthResponse> CreateSessionAsync(
        UserEntity user,
        string deviceId,
        string deviceName
    )
    {
        var now = DateTime.UtcNow;
        var refreshToken = CreateRefreshToken();
        var session = new DeviceSessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = deviceId,
            DeviceName = deviceName,
            RefreshTokenHash = HashRefreshToken(refreshToken),
            CreatedAtUtc = now,
            LastActivityAtUtc = now,
            ExpiresAtUtc = now.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 14)),
        };

        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return await CreateAuthResponseAsync(user, session, refreshToken);
    }

    public async Task<AuthResponse?> RefreshSessionAsync(RefreshRequest request)
    {
        if (request.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(request.RefreshToken))
            return null;

        var now = DateTime.UtcNow;
        var currentHash = HashRefreshToken(request.RefreshToken);
        var session = await dbContext
            .DeviceSessions.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == request.SessionId
                && item.RefreshTokenHash == currentHash
                && item.RevokedAtUtc == null
                && item.ExpiresAtUtc > now
            );
        if (session is null)
            return null;

        var nextRefreshToken = CreateRefreshToken();
        var nextHash = HashRefreshToken(nextRefreshToken);
        var nextExpiresAt = now.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 14));
        var updated = await dbContext
            .DeviceSessions.Where(item =>
                item.Id == session.Id
                && item.RefreshTokenHash == currentHash
                && item.RevokedAtUtc == null
                && item.ExpiresAtUtc > now
            )
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(item => item.RefreshTokenHash, nextHash)
                    .SetProperty(item => item.LastActivityAtUtc, now)
                    .SetProperty(item => item.ExpiresAtUtc, nextExpiresAt)
            );
        if (updated == 0)
            return null;

        var user = await userManager.FindByIdAsync(session.UserId.ToString());
        if (user is null || user.IsSuspended)
            return null;

        session.RefreshTokenHash = nextHash;
        session.LastActivityAtUtc = now;
        session.ExpiresAtUtc = nextExpiresAt;
        return await CreateAuthResponseAsync(user, session, nextRefreshToken);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(
        UserEntity user,
        DeviceSessionEntity session,
        string refreshToken
    )
    {
        var settings = configuration.GetSection("Jwt");
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.GetValue("AccessTokenMinutes", 15));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("sid", session.Id.ToString()),
        };

        claims.AddRange(await userManager.GetClaimsAsync(user));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings["Issuer"],
            audience: settings["Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return new AuthResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = refreshToken,
            SessionId = session.Id,
            ExpiresAtUtc = expiresAt,
        };
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CreateRefreshToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
}
