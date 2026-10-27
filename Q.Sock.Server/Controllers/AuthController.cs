using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Server.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(
    UserManager<UserEntity> userManager,
    SignInManager<UserEntity> signInManager,
    JwtTokenService tokenService,
    AccessService access,
    ExternalAuthentication authentication
) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (!authentication.AllowsLocalRegistration || !(await access.Policy()).AllowRegistration)
            return StatusCode(403, new { error = "Регистрация отключена администратором." });
        var username = request.UserName.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return ValidationProblem(
                new ValidationProblemDetails(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.UserName)] = ["Логин не может быть пустым."],
                    }
                )
            );

        if (!HasValidDevice(request.DeviceId, request.DeviceName))
            return BadRequest("Не переданы корректные данные устройства.");

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant(),
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(
                new ValidationProblemDetails(
                    result
                        .Errors.GroupBy(error => error.Code)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(error => error.Description).ToArray()
                        )
                )
            );
        }

        return Ok(
            await tokenService.CreateSessionAsync(user, request.DeviceId, request.DeviceName)
        );
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        if (!HasValidDevice(request.DeviceId, request.DeviceName))
            return BadRequest("Не переданы корректные данные устройства.");

        if (authentication.Active is { } provider)
        {
            var external = await provider.Authenticate(request.UserName, request.Password);
            return external is null
                ? Unauthorized()
                : Ok(
                    await tokenService.CreateSessionAsync(
                        external,
                        request.DeviceId,
                        request.DeviceName
                    )
                );
        }
        var user = await userManager.FindByNameAsync(request.UserName.Trim());
        if (user is null || user.IsSuspended)
            return Unauthorized();

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true
        );
        if (!signInResult.Succeeded)
            return Unauthorized();

        return Ok(
            await tokenService.CreateSessionAsync(user, request.DeviceId, request.DeviceName)
        );
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        var response = await tokenService.RefreshSessionAsync(request);
        return response is null ? Unauthorized() : Ok(response);
    }

    private static bool HasValidDevice(string deviceId, string deviceName) =>
        !string.IsNullOrWhiteSpace(deviceId)
        && deviceId.Length <= 128
        && !string.IsNullOrWhiteSpace(deviceName)
        && deviceName.Length <= 128;
}
