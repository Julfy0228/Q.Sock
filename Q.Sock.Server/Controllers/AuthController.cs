using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Q.Sock.Server.Entities;
using Q.Sock.Server.Services;
using Q.Sock.Shared.Models.Auth;

namespace Q.Sock.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<UserEntity> userManager,
    SignInManager<UserEntity> signInManager,
    JwtTokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var username = request.UserName.Trim();
        if (string.IsNullOrWhiteSpace(username))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.UserName)] = ["Логин не может быть пустым."]
            }));

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = username.ToUpperInvariant()
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray())));
        }

        return Ok(await tokenService.CreateTokenAsync(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByNameAsync(request.UserName.Trim());
        if (user is null)
            return Unauthorized();

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
            return Unauthorized();

        return Ok(await tokenService.CreateTokenAsync(user));
    }
}
