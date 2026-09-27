using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChamaLink.Application.DTOs;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

public class AuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _config;

    public AuthService(ApplicationDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        // SECURITY NOTE (ukaguzi 2026-09-15, M-6: user enumeration).
        // Ujumbe huu unamwambia mtu yeyote (bila akaunti) kama email fulani
        // ipo kwenye mfumo - na /api/Auth/register hauna [Authorize], kwa
        // hiyo ni njia ya bure ya kupata orodha ya wanachama kwa kujaribu
        // email moja moja. Rate limit ya 5/dakika inapunguza kasi tu,
        // haizuii kabisa.
        //
        // SULUHU KAMILI inahitaji uthibitisho wa barua pepe (email
        // verification): usajili unafanikiwa daima, kisha mtu anapewa link
        // ya kuthibitisha - hivyo hakuna ujumbe unaovuja chochote. Hiyo ni
        // kazi ya ziada (inahitaji email service), imeandikwa kwenye ripoti.
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            throw new ConflictException("Email tayari imeshasajiliwa.");

        // Namba ya simu inahifadhiwa kwa mfumo mmoja (255XXXXXXXXX) ili ilinganishwe na M-Koba
        // na na wanachama waliopo (pendekezo la kuunganisha akaunti — kiongozi ndiye anathibitisha).
        string phone;
        try { phone = ChamaLink.Infrastructure.Services.Truth.PhoneNumbers.Normalize(dto.PhoneNumber); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim(),
            PhoneNumber = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user);
        return new AuthResponseDto(token, user.Id, user.FullName, user.Email);
    }

    // NEW (ukaguzi 2026-09-15, M-5): rate limit ya 5/dakika kwa IP
    // (Program.cs, policy "auth") inalinda dhidi ya mtu mmoja anayejaribu
    // kwa kasi - lakini HAILINDI dhidi ya mtu anayelenga akaunti MOJA
    // polepole, wala dhidi ya washambulizi wengi wanaotoka IP tofauti.
    //
    // Kizuizi hiki ni cha ngazi ya pili: baada ya majaribio 5 yasiyofanikiwa
    // kwa email moja, akaunti hiyo imefungwa dakika 15 - bila kujali IP.
    //
    // ONYO LA UWAJIBU: hii imehifadhiwa kwenye RAM, kwa hiyo
    //   (a) inapotea app ikizimwa,
    //   (b) HAISHIRIKIWI kati ya instances nyingi (ukitumia Azure App
    //       Service / K8s na replicas > 1, kila instance ina hesabu yake).
    // Kwa uzalishaji halisi hii inapaswa kuwa kwenye Redis au DB.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Failures, DateTime LockedUntil)>
        LoginAttempts = new();

    private const int MaxLoginFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var emailKey = dto.Email.Trim().ToLowerInvariant();

        // 1. Je akaunti imefungwa?
        if (LoginAttempts.TryGetValue(emailKey, out var attempt))
        {
            if (attempt.LockedUntil > DateTime.UtcNow)
            {
                var minutesLeft = (int)Math.Ceiling((attempt.LockedUntil - DateTime.UtcNow).TotalMinutes);
                throw new ValidationException(
                    $"Akaunti hii imefungwa kwa muda kwa sababu ya majaribio mengi yasiyofanikiwa. Jaribu tena baada ya dakika {minutesLeft}.");
            }
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email)
            ?? throw BadCredentials(dto);

        // Rekodi za wanachama zisizo na akaunti (zilizoingizwa na viongozi/import) hazina neno la siri — haziingii kamwe.
        if (string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw BadCredentials(dto);

        // 2. Kufanikiwa - futa rekodi ya majaribio mabaya.
        LoginAttempts.TryRemove(emailKey, out _);

        var token = GenerateJwtToken(user);
        return new AuthResponseDto(token, user.Id, user.FullName, user.Email);
    }

    // Kumbuka majaribio mabaya, na ufunge akaunti ikiwa yamezidi kiwango.
    private static ValidationException BadCredentials(LoginDto dto)
    {
        var emailKey = dto.Email.Trim().ToLowerInvariant();

        var updated = LoginAttempts.AddOrUpdate(
            emailKey,
            _ => (1, DateTime.MinValue),
            (_, prev) => (prev.Failures + 1, prev.LockedUntil));

        if (updated.Failures >= MaxLoginFailures)
        {
            LoginAttempts[emailKey] = (updated.Failures, DateTime.UtcNow.Add(LockoutDuration));
        }

        // NOTE (ukaguzi 2026-09-15): hii ni ValidationException -> HTTP 400,
        // si 401. Kisemantiki 401 ingekuwa sahihi zaidi, LAKINI
        // chamalink-web-new/src/api/client.ts (mstari 27) humrudisha
        // mtumiaji kwenye login screen kwa 401 yoyote - na kufanya hivyo
        // kutoka kwenye ukurasa wa login wenyewe kungeanza loop. 400
        // inaruhusu LoginPage kuonyesha "email au neno la siri si sahihi"
        // kama ilivyokuwa. Ikiwa utabadilisha client.ts, badilisha hii pia.
        return new ValidationException("Email au neno la siri sio sahihi.");
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // CHANGE (ukaguzi 2026-09-15, M-5): muda wa token ulikuwa
        // umewekwa ngumu kuwa siku 7. Sasa unasomwa kutoka configuration
        // (Jwt:ExpiryDays) na chaguo-msingi ni siku 1.
        //
        // Kwa nini: token inahifadhiwa kwenye localStorage ya browser
        // (chamalink-web-new/src/auth/AuthContext.tsx), yaani inaweza
        // kuibwa kwa XSS, na mfumo huu HAUNA njia ya kuifuta (revocation)
        // wala refresh token. Kwa hiyo muda mrefu = dirisha refu la
        // uvamizi. Punguza hadi 1 (au chini) mpaka refresh tokens
        // zitakapokuwepo - angalia ripoti, M-5.
        // (Hakuna GetValue<> hapa kwa sababu project hii hairejelei
        //  Microsoft.Extensions.Configuration.Binder - int.TryParse hapa
        //  chini inafanya kazi ile ile bila mtegemeo wa ziada.)
        var expiryDays = int.TryParse(_config["Jwt:ExpiryDays"], out var parsedDays) && parsedDays > 0
            ? parsedDays
            : 1;

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(expiryDays),
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}