using ChamaLink.Application.DTOs;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChamaLink.API.Controllers;

// SECURITY FIX (audit Stage 2: "Rate limiting haipo"): Register/Login
// carry no [Authorize] by design (you need them before you have a
// token), which is exactly why they need their own tight rate limit -
// see the "auth" policy in Program.cs.
[EnableRateLimiting("auth")]
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {

        var result = await _authService.RegisterAsync(dto);
        return Ok(result);

    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {

        var result = await _authService.LoginAsync(dto);
        return Ok(result);

    }
}