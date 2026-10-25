using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Platform;

/// <summary>
/// SASISHO20 — kuingia kwa msimamizi wa mfumo.
///
/// Mzunguko:
///   1. Neno la siri (akaunti ile ile ya <see cref="User"/>) → uthibitisho wa kwanza.
///   2. MFA (TOTP): msimamizi mpya anajisajili kwa siri mpya (QR/mkono), kisha code
///      ya tarakimu 6 kila kuingia. Hakuna token kamili bila MFA.
///   3. Token ya jukwaa: JWT yenye madai `pa=1` na `pal=&lt;ngazi&gt;`. Token za kikundi
///      hazina madai haya — hivyo kiongozi hawezi kufikia API ya jukwaa kwa bahati.
///
/// Usalama:
///   • Mtu asiye msimamizi anapata jibu LILE LILE (haufichui kama akaunti ni ya msimamizi).
///   • Kufuli ya akaunti ni ile ile ya AuthService (Users.FailedLoginCount/LockoutEndUtc).
///   • Siri ya TOTP haisomiwi wazi: imesimbwa (AES-GCM) na AAD = "platform-totp:{id}".
/// </summary>
public class PlatformAuthService
{
    public const string PlatformClaim = "pa";
    public const string PlatformLevelClaim = "pal";
    public const string PlatformIdClaim = "pid";
    /// <summary>Token ya hatua ya kwanza (bado haijathibitishwa MFA) — inatumika tu kwenye /mfa/*.</summary>
    public const string MfaStageClaim = "pmfa";
    public const string EnrollStageClaim = "penroll";

    private const string BadCredentialsMessage = "Barua pepe au neno la siri si sahihi, au akaunti imefungwa kwa muda. Jaribu tena baadaye.";

    private readonly ApplicationDbContext _context;
    private readonly AuthService _auth;
    private readonly PlatformAuditService _audit;
    private readonly IConfiguration _config;

    public PlatformAuthService(ApplicationDbContext context, AuthService auth, PlatformAuditService audit, IConfiguration config)
    {
        _context = context; _auth = auth; _audit = audit; _config = config;
    }

    private int MaxFailures => int.TryParse(_config["Auth:MaxLoginFailures"], out var v) && v > 0 ? v : 5;
    private TimeSpan Lockout => TimeSpan.FromMinutes(int.TryParse(_config["Auth:LockoutMinutes"], out var v) && v > 0 ? v : 15);
    private int SessionMinutes => int.TryParse(_config["Platform:SessionMinutes"], out var v) && v > 0 ? v : 60;
    private int ChallengeMinutes => int.TryParse(_config["Platform:ChallengeMinutes"], out var v) && v > 0 ? v : 5;
    private const int MaxMfaFailuresPerWindow = 6;

    public sealed record LoginStage(string Stage, string? Token, string FullName, string Level, DateTime? ExpiresAt);

    // ------------------------------------------------------------------
    // 1. Neno la siri
    // ------------------------------------------------------------------
    public async Task<LoginStage> LoginAsync(string email, string password, string? ip, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLower(), ct);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHashFor(password));
            throw new ValidationException(BadCredentialsMessage);
        }
        var now = DateTime.UtcNow;
        if (user.LockoutEndUtc is { } until && until > now)
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHashFor(password));
            throw new ValidationException(BadCredentialsMessage);
        }
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailures) user.LockoutEndUtc = now.Add(Lockout);
            await _context.SaveChangesAsync(ct);
            await _audit.WriteAsync(null, user.Id, "login_failed", ip: ip, ct: ct);
            await _context.SaveChangesAsync(ct);
            throw new ValidationException(BadCredentialsMessage);
        }
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;

        var admin = await _context.PlatformAdmins.FirstOrDefaultAsync(a => a.UserId == user.Id && a.IsActive, ct);
        if (admin is null)
        {
            // Mtu asiye msimamizi: jibu ni lile lile. Ushahidi unaandikwa ili mmiliki wa mfumo
            // aone kama mtu anajaribu kuingia kwenye mlango wa msimamizi.
            await _context.SaveChangesAsync(ct);
            await _audit.WriteAsync(null, user.Id, "login_not_admin", ip: ip, ct: ct);
            await _context.SaveChangesAsync(ct);
            throw new ValidationException(BadCredentialsMessage);
        }

        await _context.SaveChangesAsync(ct);

        if (string.IsNullOrEmpty(admin.TotpSecretProtected))
        {
            var enroll = ChallengeToken(user, EnrollStageClaim);
            await _audit.WriteAsync(admin.Id, user.Id, "login_enrollment_required", ip: ip, ct: ct);
            await _context.SaveChangesAsync(ct);
            return new LoginStage("EnrollmentRequired", enroll, user.FullName, admin.Level.ToString(), now.AddMinutes(ChallengeMinutes));
        }

        var challenge = ChallengeToken(user, MfaStageClaim);
        await _audit.WriteAsync(admin.Id, user.Id, "login_password_ok", ip: ip, ct: ct);
        await _context.SaveChangesAsync(ct);
        return new LoginStage("MfaRequired", challenge, user.FullName, admin.Level.ToString(), now.AddMinutes(ChallengeMinutes));
    }

    private static string DummyHashFor(string password)
        => string.IsNullOrEmpty(password) ? Dummy : Dummy;
    private static readonly string Dummy = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    private string ChallengeToken(User user, string stageClaim)
        => _auth.GenerateJwtToken(user, new[]
        {
            new Claim(stageClaim, "1"),
            new Claim("pcv", user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        }, ChallengeMinutes);

    // ------------------------------------------------------------------
    // 2. Kujisajili MFA
    // ------------------------------------------------------------------
    public sealed record Enrollment(string Secret, string Uri, string ManualHint);

    /// <summary>Hutoa siri mpya ya TOTP. Haithibitishiwi hadi mtumiaji aandike code sahihi.</summary>
    public async Task<Enrollment> EnrollAsync(Guid userId, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(userId, ct);
        if (!string.IsNullOrEmpty(admin.TotpSecretProtected))
            throw new ConflictException("MFA imekwisha washwa kwenye akaunti hii. Kama umeipoteza simu, msimamizi mwingine aandikishe upya.");

        var secret = TotpService.NewSecret();
        admin.PendingTotpSecretProtected = ProtectSecret(admin.Id, secret);
        await _context.SaveChangesAsync(ct);
        await _audit.WriteAsync(admin.Id, userId, "mfa_enroll_started", ct: ct);
        await _context.SaveChangesAsync(ct);

        var account = (await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct)).Email;
        return new Enrollment(secret, TotpService.OtpAuthUri("ChamaLink", account, secret),
            string.Join(" ", secret.Chunk(4).Select(c => new string(c))));
    }

    public async Task<LoginStage> ConfirmEnrollmentAsync(Guid userId, string code, string? ip, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(userId, ct);
        if (string.IsNullOrEmpty(admin.PendingTotpSecretProtected))
            throw new ConflictException("Hakuna usajili wa MFA unaosubiri. Ingia kwanza kwa neno la siri.");

        var secret = UnprotectSecret(admin.Id, admin.PendingTotpSecretProtected);
        if (!TotpService.Verify(secret, code))
        {
            await _audit.WriteAsync(admin.Id, userId, "mfa_enroll_failed", ip: ip, ct: ct);
            await _context.SaveChangesAsync(ct);
            throw new ValidationException("Code si sahihi. Hakikisha saa ya simu iko sawa, kisha jaribu tena.");
        }

        admin.TotpSecretProtected = admin.PendingTotpSecretProtected;
        admin.PendingTotpSecretProtected = null;
        admin.MfaEnabledAt = DateTime.UtcNow;
        admin.LastMfaVerifiedAt = DateTime.UtcNow;
        await _audit.WriteAsync(admin.Id, userId, "mfa_enrolled", ip: ip, ct: ct);
        await _context.SaveChangesAsync(ct);
        return await ReadyAsync(admin, ip, ct);
    }

    // ------------------------------------------------------------------
    // 3. Kuthibitisha MFA kila kuingia
    // ------------------------------------------------------------------
    public async Task<LoginStage> VerifyMfaAsync(Guid userId, string code, string? ip, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(userId, ct);
        if (string.IsNullOrEmpty(admin.TotpSecretProtected))
            throw new ConflictException("MFA haijawashwa kwenye akaunti hii.");

        var since = DateTime.UtcNow.AddMinutes(-5);
        var recentFailures = await _context.PlatformAuditEvents.CountAsync(
            a => a.PlatformAdminId == admin.Id && a.Action == "mfa_failed" && a.At >= since, ct);
        if (recentFailures >= MaxMfaFailuresPerWindow)
            throw new ForbiddenException("Majaribio mengi ya code. Subiri dakika 5 kisha ujaribu tena.");

        var secret = UnprotectSecret(admin.Id, admin.TotpSecretProtected);
        if (!TotpService.Verify(secret, code))
        {
            await _audit.WriteAsync(admin.Id, userId, "mfa_failed", ip: ip, ct: ct);
            await _context.SaveChangesAsync(ct);
            throw new ValidationException("Code si sahihi au imeisha muda. Code mpya inabadilika kila sekunde 30.");
        }

        admin.LastMfaVerifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await ReadyAsync(admin, ip, ct);
    }

    private async Task<LoginStage> ReadyAsync(PlatformAdmin admin, string? ip, CancellationToken ct)
    {
        var user = await _context.Users.FirstAsync(u => u.Id == admin.UserId, ct);
        var token = _auth.GenerateJwtToken(user, new[]
        {
            new Claim(PlatformClaim, "1"),
            new Claim(PlatformLevelClaim, admin.Level.ToString()),
            new Claim(PlatformIdClaim, admin.Id.ToString())
        }, SessionMinutes);
        await _audit.WriteAsync(admin.Id, user.Id, "login_ok", ip: ip, detail: new { level = admin.Level.ToString() }, ct: ct);
        await _context.SaveChangesAsync(ct);
        return new LoginStage("Ready", token, user.FullName, admin.Level.ToString(), DateTime.UtcNow.AddMinutes(SessionMinutes));
    }

    // ------------------------------------------------------------------
    // 4. Usaidizi: msimamizi aliye hai + ngazi
    // ------------------------------------------------------------------
    public async Task<PlatformAdmin> RequireAdminAsync(Guid userId, CancellationToken ct = default)
        => await _context.PlatformAdmins.Include(a => a.User).FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct)
           ?? throw new ForbiddenException("Huna nafasi ya msimamizi wa mfumo.");

    public async Task<PlatformAdmin> RequireLevelAsync(Guid userId, PlatformAdminLevel min, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(userId, ct);
        if (admin.Level < min)
            throw new ForbiddenException(min == PlatformAdminLevel.SuperAdmin
                ? "Kitendo hiki ni cha msimamizi mkuu (Super Admin)."
                : "Ngazi yako ya usimamizi hairuhusu kitendo hiki.");
        return admin;
    }

    /// <summary>Msimamizi LAAZIMA awe amethibitisha MFA katika kikao hiki (token ya jukwaa).</summary>
    public async Task<PlatformAdmin> RequireVerifiedAsync(Guid userId, CancellationToken ct = default)
    {
        var admin = await RequireAdminAsync(userId, ct);
        if (admin.MfaEnabledAt is null)
            throw new ForbiddenException("MFA haijawashwa kwenye akaunti yako ya usimamizi.");
        return admin;
    }

    // ------------------------------------------------------------------
    // 5. Siri za TOTP (AES-GCM; AAD inaunganisha siri na msimamizi husika)
    // ------------------------------------------------------------------
    private string ProtectSecret(Guid adminId, string secret)
        => Convert.ToBase64String(EvidenceCrypto.Protect(_config, $"platform-totp:{adminId:N}", Encoding.UTF8.GetBytes(secret)));

    private string UnprotectSecret(Guid adminId, string stored)
    {
        var raw = EvidenceCrypto.Unprotect(_config, $"platform-totp:{adminId:N}", Convert.FromBase64String(stored));
        return Encoding.UTF8.GetString(raw);
    }

    // ------------------------------------------------------------------
    // 6. Bootstrap: msimamizi wa kwanza (console pekee)
    // ------------------------------------------------------------------
    /// <summary>
    /// Huundwa kwa amri ya console tu (<c>--seed-platform-admin</c>) — hakuna neno la siri
    /// lililoandikwa kwenye code, na hakuna endpoint ya umma inayounda msimamizi wa kwanza.
    /// Mtu mwenye ufikiaji wa console ya server ana uwezo wa kila kitu kwa hiyo hii si njia ya nyuma.
    /// </summary>
    public async Task<PlatformAdmin> SeedFirstAdminAsync(string email, string password, string fullName, CancellationToken ct = default)
    {
        email = email.Trim();
        if (await _context.PlatformAdmins.AnyAsync(ct))
            throw new ConflictException("Tayari kuna msimamizi wa mfumo. Ongeza wengine kupitia dashibodi (idhini ya mwenzako inahitajika).");

        var weak = PasswordPolicy.Validate(password, email, "0000000000", PasswordPolicy.MinLength(12));
        if (weak is { } w) throw new ValidationException(w);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);
        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), FullName = fullName.Trim(), Email = email, PhoneNumber = string.Empty, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) };
            _context.Users.Add(user);
        }
        else user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);

        var admin = new PlatformAdmin { Id = Guid.NewGuid(), UserId = user.Id, Level = PlatformAdminLevel.SuperAdmin, IsActive = true };
        _context.PlatformAdmins.Add(admin);
        await _audit.WriteAsync(admin.Id, user.Id, "admin_seeded", detail: new { level = "SuperAdmin", via = "console" }, ct: ct);
        await _context.SaveChangesAsync(ct);
        return admin;
    }
}
