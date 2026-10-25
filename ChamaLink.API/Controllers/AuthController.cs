using ChamaLink.API.Extensions;
using ChamaLink.Application.DTOs;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChamaLink.API.Controllers;

// Register/Login/Refresh hazina [Authorize] kwa asili (unazihitaji kabla ya kuwa na token) — kwa hiyo zina kikomo
// chake kikali ("auth" policy kwenye Program.cs).
//
// SASISHO15 (P2): vikao.
//  - Access token (JWT) ni ya muda mfupi (Jwt:AccessTokenMinutes, chaguo-msingi 15) na inakaa kwenye KUMBUKUMBU ya
//    browser tu (si localStorage).
//  - Refresh token iko kwenye cookie ya HttpOnly + SameSite=Strict + Path=/api/auth: JavaScript haiioni (XSS haiwezi
//    kuiba kikao), na browser haiitumi kwa njia nyingine yoyote. Inazungushwa kila matumizi; ikitumika mara ya pili,
//    familia nzima ya kikao inafungwa (kugundua wizi).
[EnableRateLimiting("auth")]
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    public const string RefreshCookie = "chamalink_rt";
    private readonly AuthService _authService;

    public AuthController(AuthService authService) => _authService = authService;

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Njia ya cookie ni case-sensitive kwenye browser: frontend inaita /api/auth/... kwa herufi ndogo.
    // Kama API iko nyuma ya kiambishi (mf. /chamalink/api), weka Auth:RefreshCookiePath ipasavyo.
    private string CookiePath => HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Auth:RefreshCookiePath"] is { Length: > 0 } p ? p : "/api/auth";

    private void SetCookie(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token, new CookieOptions
    {
        HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Path = CookiePath, Expires = expires, IsEssential = true
    });

    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, new CookieOptions
    {
        HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Path = CookiePath
    });

    /// <summary>Jibu ni DAIMA 202 na ujumbe ule ule (hakuna token, hakuna "email ipo") — mtumiaji anaingia kupitia /login.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        => Accepted(new { message = await _authService.RegisterAsync(dto) });

    // =================================================================
    // SASISHO20 — code ya mara moja ya kureset neno la siri
    //
    // Hii ni njia pekee ya kurejesha akaunti bila msimamizi kuona/kuweka neno la siri:
    // msimamizi wa mfumo anatoa code (kwa simu), mwenye akaunti anajiwekea neno jipya hapa.
    // Rate limit ni ile ile ya auth; code inaisha baada ya dakika 30 na matumizi moja.
    // =================================================================
    public sealed record ResetWithCodeDto(string Email, string Code, string NewPassword);

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-with-code")]
    public async Task<IActionResult> ResetWithCode([FromBody] ResetWithCodeDto dto, [FromServices] ChamaLink.Infrastructure.Services.Platform.PlatformSupportService support)
    {
        await support.RedeemResetCodeAsync(dto.Email ?? string.Empty, dto.Code ?? string.Empty, dto.NewPassword ?? string.Empty, Ip);
        return Ok(new { ujumbe = "Neno la siri limewekwa. Ingia kwa neno jipya; vikao vyote vya zamani vimefungwa." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var r = await _authService.LoginAsync(dto, Ip);
        SetCookie(r.RefreshToken, r.RefreshExpiresAt);
        return Ok(r.Auth);
    }

    [EnableRateLimiting("session")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            var r = await _authService.RefreshAsync(Request.Cookies[RefreshCookie], Ip);
            SetCookie(r.RefreshToken, r.RefreshExpiresAt);
            return Ok(r.Auth);
        }
        catch (ChamaLink.Domain.Exceptions.UnauthorizedException)
        {
            ClearCookie();
            throw;
        }
    }

    [EnableRateLimiting("session")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync(Request.Cookies[RefreshCookie]);
        ClearCookie();
        return NoContent();
    }

    /// <summary>Funga vikao VYOTE vya akaunti yangu (vifaa vyote) — mf. simu imepotea.</summary>
    [Authorize]
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        await _authService.RevokeAllSessionsAsync(User.GetUserId());
        ClearCookie();
        return NoContent();
    }
}
