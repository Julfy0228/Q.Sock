using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Q.Sock.Server.Data;
using Q.Sock.Server.Services;

namespace Q.Sock.Server.Controllers;

public sealed class LaunchExchange
{
    public string Code { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "Браузер";
}

[ApiController, Route("api/auth")]
public sealed class LaunchController(ChatDbContext db, AccessService access, JwtTokenService tokens)
    : ControllerBase
{
    [Authorize, HttpPost("bridge")]
    public async Task<object> Bridge([FromQuery] Guid? callId = null)
    {
        if (callId != null)
        {
            var c =
                await db.Calls.FindAsync(callId)
                ?? throw new ApiException(404, "Звонок не найден.");
            await access.Chat(c.ChatId, User.UserId());
        }
        return new { code = await LaunchCodes.Create(db, new(User.UserId(), null, callId)) };
    }

    [HttpPost("launch")]
    public async Task<object> Exchange(LaunchExchange r)
    {
        if (
            r.Code.Length != 64
            || r.DeviceId.Length is < 1 or > 128
            || r.DeviceName.Length is < 1 or > 128
        )
            throw new ApiException(400, "Некорректный запрос.");
        var p = await LaunchCodes.Consume(db, r.Code);
        var user = await access.User(p.UserId);
        if (p.ChatId != null)
            await access.Chat(p.ChatId.Value, p.UserId);
        return new
        {
            auth = await tokens.CreateSessionAsync(user, r.DeviceId, r.DeviceName),
            p.ChatId,
            p.CallId,
        };
    }
}
