using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Q.Sock.Server.Entities;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Server.Services;

public sealed class JwtTokenService(
    IConfiguration configuration,
    UserManager<UserEntity> userManager)
{
    public async Task<AuthResponse> CreateTokenAsync(UserEntity user)
    {
        var settings = configuration.GetSection("Jwt");
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.GetValue("AccessTokenMinutes", 30));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(await userManager.GetClaimsAsync(user));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings["Issuer"],
            audience: settings["Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new AuthResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAtUtc = expiresAt
        };
    }
}
