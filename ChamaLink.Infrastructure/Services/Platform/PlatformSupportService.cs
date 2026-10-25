using System.Security.Cryptography;
using System.Text;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Localization;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Platform;

/// <summary>
/// SASISHO20 — huduma za msaada. Hizi ni "kutatua matatizo", si "kuingia kwenye fedha":
///
///   • Kufungua akaunti iliyofungwa (lockout) — hakuandiki fedha.
///   • Kutoa code ya mara moja ya kureset neno la siri — msimamizi HAONI neno jipya.
///   • Kubadilisha join code ya kikundi (ikivuja) — kiongozi wa kikundi anaarifiwa.
///   • Kutuma notisi rasmi kwa viongozi ("sera haijakamilika", "taarifa ni ya zamani").
///
/// Kila kitendo: (1) kinaandikwa kwenye kumbukumbu ya jukwaa, (2) kinachoigusа kikundi
/// kinaandikwa PIA kwenye kumbukumbu ya kikundi ili wanachama waone.
/// </summary>
public class PlatformSupportService
{
    private const string CodeAlphabet = "ACDEFGHJKLMNPQRTUVWXY34679"; // hakuna 0/O/1/I/8/B/S/Z — code isomwe kwa simu
    private const int CodeLength = 10;

    private readonly ApplicationDbContext _context;
    private readonly PlatformAuditService _audit;
    private readonly NotificationService _notify;
    private readonly IConfiguration _config;

    public PlatformSupportService(ApplicationDbContext context, PlatformAuditService audit, NotificationService notify, IConfiguration config)
    {
        _context = context; _audit = audit; _notify = notify; _config = config;
    }

    private int CodeMinutes => int.TryParse(_config["Platform:ResetCodeMinutes"], out var v) && v > 0 ? v : 30;
    private const int MaxCodeAttempts = 5;

    // ------------------------------------------------------------------
    // 1. Kutafuta mtu (kwa barua pepe au namba ya simu) — msaada wa kwanza
    // ------------------------------------------------------------------
    public sealed record PersonRow(Guid UserId, string FullName, string? Email, string? Phone,
        bool Locked, DateTime? LockedUntil, bool HasAccount, List<MembershipRow> Groups);

    public sealed record MembershipRow(Guid GroupId, string GroupName, string? MemberNumber, string Role, string Status, bool IsLeader);

    public async Task<List<PersonRow>> FindPeopleAsync(string query, CancellationToken ct = default)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length < 3) throw new ValidationException("Andika angalau herufi 3 (jina, barua pepe au namba ya simu).");

        var phone = TryNormalizePhone(q);
        var users = await _context.Users.AsNoTracking()
            .Where(u => u.Email.ToLower().Contains(q.ToLower())
                        || (phone != null && u.PhoneNumber == phone)
                        || u.FullName.ToLower().Contains(q.ToLower()))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)   // Take bila OrderBy = matokeo yasiyotabirika (onyo la EF)
            .Take(25).ToListAsync(ct);

        // Mtu anaweza kuwa "rekodi ya fedha" tu (hana akaunti) — tafuta kwa namba ya simu ya M-Koba pia.
        var memberIds = new List<Guid>();
        if (phone != null)
        {
            memberIds = await _context.MemberPhones.AsNoTracking()
                .Where(p => p.Phone == phone && p.Status == TruthApprovalStatus.Approved)
                .Select(p => p.GroupMemberId).Distinct().OrderBy(x => x).Take(25).ToListAsync(ct);
            var linkedUserIds = await _context.GroupMembers.AsNoTracking()
                .Where(m => memberIds.Contains(m.Id)).Select(m => m.UserId).Distinct().ToListAsync(ct);
            foreach (var uid in linkedUserIds.Where(u => users.All(x => x.Id != u)))
            {
                var u = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid, ct);
                if (u is not null) users.Add(u);
            }
        }

        var result = new List<PersonRow>();
        var now = DateTime.UtcNow;
        // Kufuli ya kuingia kwa kikundi inaishi mahali PAWILI: Users.LockoutEndUtc (uingia wa
        // msimamizi) na LoginFailures kwa IP (uingia wa kawaida). Msaada lazima uone ZOTE mbili —
        // vinginevyo msimamizi anasema "haijafungwa" wakati kiongozi hawezi kuingia.
        var userIds = users.Select(u => u.Id).ToList();
        var lockedIps = await _context.LoginFailures.AsNoTracking()
            .Where(f => userIds.Contains(f.UserId) && f.LockoutEndUtc != null && f.LockoutEndUtc > now)
            .GroupBy(f => f.UserId).Select(g => new { UserId = g.Key, Until = g.Max(x => x.LockoutEndUtc) })
            .ToListAsync(ct);
        foreach (var u in users.Take(25))
        {
            var ms = await _context.GroupMembers.AsNoTracking()
                .Where(m => m.UserId == u.Id)
                .Select(m => new { m.GroupId, m.MemberNumber, m.Role, m.Status, GroupName = m.Group != null ? m.Group.Name : "" })
                .ToListAsync(ct);
            var userLock = u.LockoutEndUtc is { } le && le > now ? u.LockoutEndUtc : null;
            var ipLock = lockedIps.FirstOrDefault(x => x.UserId == u.Id)?.Until;
            var lockUntil = userLock ?? ipLock;
            result.Add(new PersonRow(u.Id, u.FullName, Mask(u.Email), MaskPhone(u.PhoneNumber),
                lockUntil is not null, lockUntil,
                !string.IsNullOrEmpty(u.PasswordHash),
                ms.Select(m => new MembershipRow(m.GroupId, m.GroupName, m.MemberNumber, m.Role.ToString(), m.Status.ToString(),
                    MemberAccessIsLeader(m.Role, m.Status))).ToList()));
        }
        return result;
    }

    private static bool MemberAccessIsLeader(GroupRole role, MemberStatus status)
        => Common.MemberAccess.ActiveStatuses.Contains(status)
           && (role == GroupRole.Chairperson || role == GroupRole.Treasurer || role == GroupRole.Secretary);

    // ------------------------------------------------------------------
    // 2. Kufungua akaunti iliyofungwa
    // ------------------------------------------------------------------
    public async Task<int> UnlockAsync(Guid actorUserId, Guid targetUserId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Eleza sababu ya kufungua akaunti (angalau herufi 5).");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
                   ?? throw new NotFoundException("Mtumiaji hakupatikana.");

        user.LockoutEndUtc = null;
        user.FailedLoginCount = 0;
        // SASISHO20: InMemory (majaribio) haina ExecuteDelete — njia mbili, matokeo ni sawa.
        int cleared;
        if (_context.Database.IsRelational())
            cleared = await _context.LoginFailures.Where(f => f.UserId == targetUserId).ExecuteDeleteAsync(ct);
        else
        {
            var rowsToClear = await _context.LoginFailures.Where(f => f.UserId == targetUserId).ToListAsync(ct);
            _context.LoginFailures.RemoveRange(rowsToClear);
            cleared = rowsToClear.Count;
        }

        var admin = await RequireAdmin(actorUserId, ct);
        await _audit.WriteAsync(admin.Id, actorUserId, "unlock_account", subjectId: targetUserId, reason: reason.Trim(), ct: ct);
        await NotifyGroupsOfUserAsync(targetUserId, AuditAction.PlatformUnlockedAccount, targetUserId,
            $"Msimamizi wa mfumo amefungua akaunti iliyokuwa imefungwa kwa sababu ya majaribio mengi ya kuingia. Sababu: {reason.Trim()}", ct);
        await _context.SaveChangesAsync(ct);
        return cleared;
    }

    // ------------------------------------------------------------------
    // 3. Code ya mara moja ya kureset neno la siri
    // ------------------------------------------------------------------
    public sealed record IssuedCode(string Code, DateTime ExpiresAt, string PersonName, string Email);

    public async Task<IssuedCode> IssueResetCodeAsync(Guid actorUserId, Guid targetUserId, Guid? groupId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Eleza sababu (mf. \"Kiongozi amepiga simu — amesahau neno la siri\").");
        var admin = await RequireAdmin(actorUserId, ct);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
                   ?? throw new NotFoundException("Mtumiaji hakupatikana.");
        if (string.IsNullOrEmpty(user.Email))
            throw new ConflictException("Mtumiaji hana barua pepe — hawezi kuingia kwa neno la siri.");

        // Code za zamani zisizotumika zinafutwa (code moja hai kwa wakati mmoja).
        await RevokeLiveCodesAsync(targetUserId, ct);

        var code = RandomCode();
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var row = new PlatformPasswordResetCode
        {
            Id = Guid.NewGuid(), UserId = targetUserId, GroupId = groupId, Salt = salt,
            CodeHash = Hash(code, salt), ExpiresAt = DateTime.UtcNow.AddMinutes(CodeMinutes),
            IssuedByPlatformAdminId = admin.Id
        };
        _context.PlatformPasswordResetCodes.Add(row);

        await _audit.WriteAsync(admin.Id, actorUserId, "reset_code_issued", groupId, targetUserId, reason.Trim(),
            new { expiresAt = row.ExpiresAt }, ct: ct);
        if (groupId is { } gid)
            await PlatformAuditService.WriteGroupVisibleAsync(_context, gid, actorUserId, AuditAction.PlatformResetCodeIssued,
                targetUserId, new { mtu = user.FullName, inaisha = row.ExpiresAt },
                $"Msimamizi wa mfumo amempa {user.FullName} code ya mara moja ya kureset neno la siri. Sababu: {reason.Trim()}", ct);

        await _context.SaveChangesAsync(ct);
        return new IssuedCode(code, row.ExpiresAt, user.FullName, user.Email);
    }

    /// <summary>
    /// Kiongozi anatumia code (hiari: bila kuingia). Neno jipya HALIWEKWI na msimamizi —
    /// mwenye akaunti mwenyewe analiweka. Vikao vyote vya zamani vinafungwa.
    /// </summary>
    public async Task RedeemResetCodeAsync(string email, string code, string newPassword, string? ip, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLower(), ct);
        // Jibu la jumla: hakuna kufichua kama barua pepe ipo.
        var generic = new ValidationException("Code si sahihi au imeisha muda. Omba code mpya kwa msimamizi wa mfumo.");

        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(code, BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")));
            throw generic;
        }

        var normalized = (code ?? string.Empty).Replace(" ", "").Replace("-", "").ToUpperInvariant();
        var rows = await _context.PlatformPasswordResetCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null && c.RevokedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(c => c.CreatedAt).Take(3).ToListAsync(ct);

        var match = rows.FirstOrDefault(c => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(normalized, c.Salt)), Encoding.ASCII.GetBytes(c.CodeHash)));

        if (match is null)
        {
            foreach (var c in rows) c.Attempts++;
            // Code iliyofikisha kikomo CHA majaribio inafutwa MARA MOJA na kuhifadhiwa kabla ya
            // kurusha kosa — vinginevyo jaribio la 6 kwa code SAHIHI lingepita (imeonekana kwenye jaribio).
            if (rows.Any(c => c.Attempts >= MaxCodeAttempts))
                foreach (var c in rows) c.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            throw generic;
        }

        var weak = PasswordPolicy.Validate(newPassword, user.Email, user.PhoneNumber,
            PasswordPolicy.MinLength(int.TryParse(_config["Auth:MinPasswordLength"], out var mpl) ? mpl : null));
        if (weak is { } w) throw new ValidationException(w);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.TokenVersion++;          // vikao vyote vya zamani vinafungwa
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        match.UsedAt = DateTime.UtcNow;

        await _audit.WriteAsync(match.IssuedByPlatformAdminId, user.Id, "reset_code_redeemed", match.GroupId, user.Id, ip: ip, ct: ct);
        if (match.GroupId is { } gid)
            await PlatformAuditService.WriteGroupVisibleAsync(_context, gid, user.Id, AuditAction.PlatformResetCodeRedeemed,
                user.Id, new { mtu = user.FullName }, "Kiongozi ametumia code ya msimamizi na ameweka neno jipya la siri. Vikao vyote vya zamani vimefungwa.", ct);

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Hufuta code zote hai za mtumiaji (InMemory na PostgreSQL kwa njia moja).</summary>
    private async Task RevokeLiveCodesAsync(Guid userId, CancellationToken ct)
    {
        var live = await _context.PlatformPasswordResetCodes
            .Where(c => c.UserId == userId && c.UsedAt == null && c.RevokedAt == null).ToListAsync(ct);
        foreach (var c in live) c.RevokedAt = DateTime.UtcNow;
    }

    private static string RandomCode()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    private static string Hash(string code, string salt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + ":" + code))).ToLowerInvariant();

    // ------------------------------------------------------------------
    // 4. Join code
    // ------------------------------------------------------------------
    public async Task<string> RotateJoinCodeAsync(Guid actorUserId, Guid groupId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Eleza sababu ya kubadilisha join code (mf. \"code imevuja kwenye group la WhatsApp\").");
        var admin = await RequireAdmin(actorUserId, ct);
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct) ?? throw new NotFoundException("Kikundi hakipo.");

        var code = $"{group.ShortCode ?? "CHM"}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
        group.JoinCode = code;

        await _audit.WriteAsync(admin.Id, actorUserId, "rotate_join_code", groupId, groupId, reason.Trim(), ct: ct);
        await PlatformAuditService.WriteGroupVisibleAsync(_context, groupId, actorUserId, AuditAction.PlatformJoinCodeRotated,
            groupId, new { joinCode = code }, $"Msimamizi wa mfumo amebadilisha join code ya kikundi. Sababu: {reason.Trim()}", ct);
        await _notify.NotifyLeadersAsync(groupId, "PlatformSupport",
            "Join code imebadilishwa",
            Loc.L($"Msimamizi wa mfumo amebadilisha join code ya kikundi. Code mpya: {code}. Sababu: {reason.Trim()}"),
            "/wanachama", ct);
        await _context.SaveChangesAsync(ct);
        return code;
    }

    // ------------------------------------------------------------------
    // 5. Notisi rasmi kwa viongozi
    // ------------------------------------------------------------------
    public async Task NotifyLeadersAsync(Guid actorUserId, Guid groupId, string title, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 3) throw new ValidationException("Kichwa cha notisi kinahitajika.");
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length < 10) throw new ValidationException("Maelezo ya notisi yanahitajika (angalau herufi 10).");
        var admin = await RequireAdmin(actorUserId, ct);
        if (!await _context.Groups.AnyAsync(g => g.Id == groupId, ct)) throw new NotFoundException("Kikundi hakipo.");

        await _notify.NotifyLeadersAsync(groupId, "PlatformSupport", title.Trim(), Loc.L(body.Trim()), "/", ct);
        await _audit.WriteAsync(admin.Id, actorUserId, "notice_sent", groupId, detail: new { title = title.Trim() }, ct: ct);
        await PlatformAuditService.WriteGroupVisibleAsync(_context, groupId, actorUserId, AuditAction.PlatformNoticeSent,
            groupId, new { title = title.Trim() }, $"Msimamizi wa mfumo ametuma notisi: {title.Trim()}", ct);
        await _context.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------
    private async Task<PlatformAdmin> RequireAdmin(Guid userId, CancellationToken ct)
        => await _context.PlatformAdmins.FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive && a.MfaEnabledAt != null, ct)
           ?? throw new ForbiddenException("Huna nafasi ya msimamizi wa mfumo.");

    private async Task NotifyGroupsOfUserAsync(Guid userId, AuditAction action, Guid subjectId, string reason, CancellationToken ct)
    {
        var groups = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId).Select(m => m.GroupId).Distinct().Take(20).ToListAsync(ct);
        foreach (var g in groups)
            await PlatformAuditService.WriteGroupVisibleAsync(_context, g, userId, action, subjectId, new { }, reason, ct);
    }

    private static string? TryNormalizePhone(string s)
    {
        try
        {
            var n = PhoneNumbers.Normalize(s);
            return n.Length >= 10 ? n : null;
        }
        catch (ArgumentException) { return null; }
    }

    private static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        return email[0] + new string('•', Math.Min(4, at - 1)) + email[(at - 1)..];
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        return phone.Length <= 6 ? phone : phone[..4] + new string('•', phone.Length - 6) + phone[^2..];
    }
}
