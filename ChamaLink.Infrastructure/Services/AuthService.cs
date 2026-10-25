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

    /// <summary>Jibu la usajili — DAIMA ni lile lile (akaunti mpya au email iliyopo), ili mtu asiweze kujua nani yuko kwenye mfumo.</summary>
    public const string GenericRegisterMessage = "Ombi la usajili limepokelewa. Kama taarifa ni sahihi, ingia kwa email na neno lako la siri.";

    public sealed record SessionResult(AuthResponseDto Auth, string RefreshToken, DateTime RefreshExpiresAt);

    /// <summary>
    /// SASISHO15 (P2): usajili haurudishi token wala haufichui kama email ipo (user enumeration).
    /// Email iliyopo → hakuna kinachobadilika, lakini jibu na muda wa kazi (BCrypt) ni sawa na akaunti mpya.
    /// Mtumiaji anaingia kupitia /login baada ya usajili.
    /// </summary>
    public async Task<string> RegisterAsync(RegisterDto dto)
    {
        string phone;
        try { phone = ChamaLink.Infrastructure.Services.Truth.PhoneNumbers.Normalize(dto.PhoneNumber); }
        catch (ArgumentException ex) { throw new ValidationException(ex.Message); }

        var email = dto.Email.Trim();
        var weak = PasswordPolicy.Validate(dto.Password, email, phone,
            PasswordPolicy.MinLength(int.TryParse(_config["Auth:MinPasswordLength"], out var mpl) ? mpl : null));
        if (weak is { } w) throw new ValidationException(w);
        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password); // kila mara (muda sawa)
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            return GenericRegisterMessage;

        _context.Users.Add(new User { Id = Guid.NewGuid(), FullName = dto.FullName.Trim(), Email = email, PhoneNumber = phone, PasswordHash = hash });
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException) { /* mbio za email ile ile (unique index) — jibu ni lile lile */ }
        return GenericRegisterMessage;
    }

    // Hash ya kubuni: email isiyojulikana inapitia BCrypt.Verify ile ile ili muda wa jibu usifichue kama akaunti ipo.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    private int MaxLoginFailures => int.TryParse(_config["Auth:MaxLoginFailures"], out var v) && v > 0 ? v : 5;
    private TimeSpan LockoutDuration => TimeSpan.FromMinutes(int.TryParse(_config["Auth:LockoutMinutes"], out var v) && v > 0 ? v : 15);
    public int AccessTokenMinutes => int.TryParse(_config["Jwt:AccessTokenMinutes"], out var v) && v > 0 ? v : 15;
    private int RefreshTokenDays => int.TryParse(_config["Jwt:RefreshTokenDays"], out var v) && v > 0 ? v : 14;

    /// <summary>
    /// Kuingia. Kufungiwa kunahifadhiwa kwenye DB (User.FailedLoginCount/LockoutEndUtc) — kunadumu app ikizimwa na
    /// kunashirikiwa na instances zote. Access token ni ya muda mfupi (dakika); kikao kinaendelezwa na refresh token
    /// iliyo kwenye HttpOnly cookie (JavaScript haiioni — XSS haiwezi kuiba kikao).
    /// </summary>
    public async Task<SessionResult> LoginAsync(LoginDto dto, string? ip = null)
    {
        var email = dto.Email.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            BCrypt.Net.BCrypt.Verify(dto.Password, DummyHash);
            throw BadCredentials();
        }
        var now = DateTime.UtcNow;
        var ipKey = IpKey(ip);

        // SASISHO17 (P1-4/NEW-5) — kufungia kusikoweza kupitwa kwa maombi ya wakati mmoja:
        //  (0) kufuli zilizopo zinaangaliwa kwanza (jaribio dhidi ya kufuli halihesabiwi kwenye akaunti → mshambuliaji wa IP moja
        //      hawezi kumfungia mwenye akaunti aliye mahali pengine);
        //  (1) jaribio "linahifadhiwa" KABLA ya kukagua neno la siri kwa UPDATE ... RETURNING ya atomiki (akaunti+IP, kisha akaunti nzima);
        //      maombi yanayozidi kikomo yanakataliwa BILA kukagua neno la siri. Hivyo makisio yanayokaguliwa ≤ kikomo hata kwa
        //      maombi 1000 ya wakati mmoja;
        //  (2) jibu ni LILE LILE kwa neno baya / akaunti iliyofungwa / email isiyojulikana (hakuna kufichua kama akaunti ipo).
        if (await IsLockedAsync(user.Id, ipKey, now))
        {
            BCrypt.Net.BCrypt.Verify(dto.Password, DummyHash);
            throw BadCredentials();
        }
        var pair = await ReservePairAttemptAsync(user.Id, ipKey, now);
        var account = pair.Locked || pair.Value > MaxLoginFailures ? new AttemptRow() : await ReserveAccountAttemptAsync(user.Id, now);
        if (pair.Locked || pair.Value > MaxLoginFailures || account.Locked || account.Value > AccountWideMaxFailures)
        {
            BCrypt.Net.BCrypt.Verify(dto.Password, DummyHash);
            throw BadCredentials();
        }
        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            await LockIfReachedAsync(user.Id, ipKey, pair.Value >= MaxLoginFailures, account.Value >= AccountWideMaxFailures, now);
            throw BadCredentials();
        }
        await ClearAttemptsAsync(user, ipKey);
        var (raw, rt) = NewRefreshToken(user, Guid.NewGuid(), ip);
        _context.RefreshTokens.Add(rt);
        await _context.SaveChangesAsync();
        return new SessionResult(Response(user), raw, rt.ExpiresAt);
    }

    private int AccountWideMaxFailures => int.TryParse(_config["Auth:AccountWideMaxFailures"], out var v) && v > 0 ? v : 4 * MaxLoginFailures;

    /// <summary>IP haihifadhiwi wazi: SHA-256 (ikiwa na chumvi ya server kama ipo).</summary>
    /// <summary>Safu ya matokeo ya UPDATE/INSERT ... RETURNING (idadi ya majaribio + kama kufuli iko hai).</summary>
    public sealed class AttemptRow { public int Value { get; set; } public bool Locked { get; set; } }

    private async Task<bool> IsLockedAsync(Guid userId, string ipKey, DateTime now)
    {
        var accountLocked = await _context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.LockoutEndUtc != null && u.LockoutEndUtc > now);
        return accountLocked || await _context.LoginFailures.AsNoTracking()
            .AnyAsync(f => f.UserId == userId && f.IpKey == ipKey && f.LockoutEndUtc != null && f.LockoutEndUtc > now);
    }

    private async Task<AttemptRow> ReservePairAttemptAsync(Guid userId, string ipKey, DateTime now)
    {
        var windowFrom = now - LockoutDuration;
        if (_context.Database.IsRelational())
        {
            var id = Guid.NewGuid();
            // Dirisha limeisha NA hakuna kufuli hai → hesabu inaanza upya. Vinginevyo +1 (pia wakati wa kufuli: haipunguzi kufuli).
            var rows = await _context.Database.SqlQuery<AttemptRow>($"""
                INSERT INTO "LoginFailures" ("Id","UserId","IpKey","Count","WindowStartUtc","LockoutEndUtc")
                VALUES ({id}, {userId}, {ipKey}, 1, {now}, NULL)
                ON CONFLICT ("UserId","IpKey") DO UPDATE SET
                  "Count" = CASE WHEN "LoginFailures"."WindowStartUtc" < {windowFrom}
                                  AND ("LoginFailures"."LockoutEndUtc" IS NULL OR "LoginFailures"."LockoutEndUtc" <= {now})
                                 THEN 1 ELSE "LoginFailures"."Count" + 1 END,
                  "WindowStartUtc" = CASE WHEN "LoginFailures"."WindowStartUtc" < {windowFrom}
                                  AND ("LoginFailures"."LockoutEndUtc" IS NULL OR "LoginFailures"."LockoutEndUtc" <= {now})
                                 THEN {now} ELSE "LoginFailures"."WindowStartUtc" END,
                  "LockoutEndUtc" = CASE WHEN "LoginFailures"."LockoutEndUtc" <= {now} THEN NULL ELSE "LoginFailures"."LockoutEndUtc" END
                RETURNING "Count" AS "Value", ("LockoutEndUtc" IS NOT NULL AND "LockoutEndUtc" > {now}) AS "Locked"
                """).ToListAsync();
            return rows[0];
        }
        // InMemory (majaribio ya unit): mantiki ile ile bila atomiki ya DB
        var f = await _context.LoginFailures.FirstOrDefaultAsync(x => x.UserId == userId && x.IpKey == ipKey);
        if (f is null) { f = new LoginFailure { Id = Guid.NewGuid(), UserId = userId, IpKey = ipKey, WindowStartUtc = now }; _context.LoginFailures.Add(f); }
        if (f.LockoutEndUtc is { } le && le <= now) f.LockoutEndUtc = null;
        if (f.WindowStartUtc < windowFrom && f.LockoutEndUtc is null) { f.Count = 0; f.WindowStartUtc = now; }
        f.Count++;
        await _context.SaveChangesAsync();
        return new AttemptRow { Value = f.Count, Locked = f.LockoutEndUtc is { } l && l > now };
    }

    private async Task<AttemptRow> ReserveAccountAttemptAsync(Guid userId, DateTime now)
    {
        if (_context.Database.IsRelational())
        {
            var rows = await _context.Database.SqlQuery<AttemptRow>($"""
                UPDATE "Users" SET
                  "FailedLoginCount" = CASE WHEN "LockoutEndUtc" IS NOT NULL AND "LockoutEndUtc" <= {now} THEN 1 ELSE "FailedLoginCount" + 1 END,
                  "LockoutEndUtc" = CASE WHEN "LockoutEndUtc" <= {now} THEN NULL ELSE "LockoutEndUtc" END
                WHERE "Id" = {userId}
                RETURNING "FailedLoginCount" AS "Value", ("LockoutEndUtc" IS NOT NULL AND "LockoutEndUtc" > {now}) AS "Locked"
                """).ToListAsync();
            return rows[0];
        }
        var u = await _context.Users.FirstAsync(x => x.Id == userId);
        if (u.LockoutEndUtc is { } le && le <= now) { u.LockoutEndUtc = null; u.FailedLoginCount = 0; }
        u.FailedLoginCount++;
        await _context.SaveChangesAsync();
        return new AttemptRow { Value = u.FailedLoginCount, Locked = u.LockoutEndUtc is { } l && l > now };
    }

    private async Task LockIfReachedAsync(Guid userId, string ipKey, bool lockPair, bool lockAccount, DateTime now)
    {
        if (!lockPair && !lockAccount) return;
        var until = now.Add(LockoutDuration);
        if (_context.Database.IsRelational())
        {
            if (lockPair)
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "LoginFailures" SET "LockoutEndUtc" = {until}, "WindowStartUtc" = {now}
                    WHERE "UserId" = {userId} AND "IpKey" = {ipKey} AND ("LockoutEndUtc" IS NULL OR "LockoutEndUtc" <= {now})
                    """);
            if (lockAccount)
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Users" SET "LockoutEndUtc" = {until} WHERE "Id" = {userId} AND ("LockoutEndUtc" IS NULL OR "LockoutEndUtc" <= {now})
                    """);
            return;
        }
        if (lockPair)
        {
            var f = await _context.LoginFailures.FirstAsync(x => x.UserId == userId && x.IpKey == ipKey);
            f.LockoutEndUtc = until; f.WindowStartUtc = now;
        }
        if (lockAccount) (await _context.Users.FirstAsync(x => x.Id == userId)).LockoutEndUtc = until;
        await _context.SaveChangesAsync();
    }

    private async Task ClearAttemptsAsync(User user, string ipKey)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "LoginFailures" WHERE "UserId" = {user.Id} AND "IpKey" = {ipKey}""");
            await _context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Users" SET "FailedLoginCount" = 0, "LockoutEndUtc" = NULL WHERE "Id" = {user.Id}""");
            user.FailedLoginCount = 0; user.LockoutEndUtc = null;
            _context.Entry(user).Property(x => x.FailedLoginCount).IsModified = false;
            _context.Entry(user).Property(x => x.LockoutEndUtc).IsModified = false;
            return;
        }
        var f = await _context.LoginFailures.FirstOrDefaultAsync(x => x.UserId == user.Id && x.IpKey == ipKey);
        if (f is not null) _context.LoginFailures.Remove(f);
        user.FailedLoginCount = 0; user.LockoutEndUtc = null;
        await _context.SaveChangesAsync();
    }

    private string IpKey(string? ip)
    {
        var salt = _config["Auth:IpHashSalt"] ?? string.Empty;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(salt + "|" + (ip ?? "unknown"))));
    }

    /// <summary>
    /// Kuzungusha refresh token: kila token inatumika MARA MOJA. Token iliyokwisha tumika ikitumwa tena (mtu ameiba nakala),
    /// familia nzima ya kikao inafutwa — mwizi na mwenye akaunti wote wanalazimika kuingia upya.
    /// SASISHO17 (P1-3): kuzungusha ni ATOMIKI (UPDATE … WHERE RevokedAt IS NULL) — maombi mawili ya wakati mmoja yenye
    /// token ile ile hayawezi wote kupata token mpya; la pili linaonekana kama matumizi ya pili (familia inafutwa).
    /// </summary>
    public async Task<SessionResult> RefreshAsync(string? rawToken, string? ip = null)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new UnauthorizedException("Kikao kimeisha. Ingia tena.");
        var hash = Hash(rawToken);
        var rt = await _context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash) ?? throw new UnauthorizedException("Kikao kimeisha. Ingia tena.");
        if (rt.RevokedAt is not null) await ReuseDetectedAsync(rt);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == rt.UserId);
        if (rt.ExpiresAt <= DateTime.UtcNow || user is null || user.TokenVersion != rt.TokenVersion)
            throw new UnauthorizedException("Kikao kimeisha. Ingia tena.");
        var (raw, next) = NewRefreshToken(user, rt.FamilyId, ip);
        var now = DateTime.UtcNow;
        if (_context.Database.IsRelational())
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var claimed = await _context.RefreshTokens.Where(t => t.Id == rt.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.ReplacedById, next.Id));
            if (claimed == 0) { await tx.RollbackAsync(); await ReuseDetectedAsync(rt); }
            _context.RefreshTokens.Add(next);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        else
        {
            var tracked = await _context.RefreshTokens.FirstAsync(t => t.Id == rt.Id);
            if (tracked.RevokedAt is not null) await ReuseDetectedAsync(rt);
            tracked.RevokedAt = now; tracked.ReplacedById = next.Id;
            _context.RefreshTokens.Add(next);
            await _context.SaveChangesAsync();
        }
        return new SessionResult(Response(user), raw, next.ExpiresAt);
    }

    private async Task ReuseDetectedAsync(RefreshToken rt)
    {
        await RevokeFamilyAsync(rt.UserId, rt.FamilyId);
        throw new UnauthorizedException("Kikao hiki kimeshatumika mahali pengine — kwa usalama vikao vyote vya kifaa hiki vimefungwa. Ingia tena.");
    }

    public async Task LogoutAsync(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Hash(rawToken);
        var rt = await _context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (rt is not null) await RevokeFamilyAsync(rt.UserId, rt.FamilyId);
    }

    /// <summary>Kufuta vikao VYOTE vya mtumiaji (mf. simu imeibiwa): access tokens za zamani zinakataliwa papo hapo (TokenVersion).</summary>
    public async Task RevokeAllSessionsAsync(Guid userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId) ?? throw new NotFoundException("Mtumiaji hayupo.");
        user.TokenVersion++;
        foreach (var t in await _context.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync()) t.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task RevokeFamilyAsync(Guid userId, Guid familyId)
    {
        foreach (var t in await _context.RefreshTokens.Where(t => t.UserId == userId && t.FamilyId == familyId && t.RevokedAt == null).ToListAsync())
            t.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private (string Raw, RefreshToken Entity) NewRefreshToken(User user, Guid familyId, string? ip)
    {
        var raw = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (raw, new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Hash(raw), FamilyId = familyId, ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays),
            TokenVersion = user.TokenVersion, CreatedByIp = ip is { Length: > 64 } ? ip[..64] : ip
        });
    }

    private static string Hash(string raw) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private AuthResponseDto Response(User user) => new(GenerateJwtToken(user), user.Id, user.FullName, user.Email, AccessTokenMinutes * 60);

    // NOTE: ValidationException -> HTTP 400 (si 401): client.ts inajaribu refresh kwa 401 — kwenye ukurasa wa login hilo lingeleta mzunguko.
    // SASISHO17 (NEW-5): ujumbe MMOJA kwa neno baya / akaunti iliyofungwa / email isiyojulikana.
    public const string BadCredentialsMessage = "Email au neno la siri sio sahihi, au akaunti imefungwa kwa muda baada ya majaribio mengi. Jaribu tena baadaye.";
    private static ValidationException BadCredentials() => new(BadCredentialsMessage);

    /// <summary>Claim ya toleo la vikao: ikibadilika (RevokeAllSessions), access tokens za zamani zinakataliwa.</summary>
    public const string TokenVersionClaim = "tv";

    /// <summary>
    /// SASISHO20: token ya kawaida, au token yenye madai ya ziada (mf. msimamizi wa mfumo: pa/pal)
    /// na muda wake maalum. Hii haibadilishi tabia ya token za wanachama — extraClaims = null na
    /// minutes = null zinatoa token ile ile ya kawaida.
    /// </summary>
    public string GenerateJwtToken(User user, IEnumerable<Claim>? extraClaims = null, int? minutes = null)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(TokenVersionClaim, user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (extraClaims is not null) claims.AddRange(extraClaims);

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
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(minutes ?? AccessTokenMinutes),
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}