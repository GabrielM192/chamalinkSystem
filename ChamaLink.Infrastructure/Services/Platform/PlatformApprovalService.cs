using System.Text.Json;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Platform;

/// <summary>
/// SASISHO20 — maamuzi ya watu WAWILI (4-eyes) ya jukwaa.
///
/// Hatua zinazohitaji msimamizi wa pili:
///   • BreakGlass        — kuona taarifa kamili za kikundi (wanachama mmoja mmoja) kwa saa 24
///   • GrantAdmin        — kumpa mtu nafasi ya msimamizi
///   • ChangeAdminLevel  — kubadilisha ngazi ya msimamizi
///   • DeactivateAdmin   — kuondoa nafasi ya msimamizi (+ kufunga vikao vyake)
///
/// Kanuni ngumu:
///   1. Aliyeomba HAWEZI kuidhinisha ombi lake.
///   2. Msimamizi anayeondolewa hawezi kuidhinisha kuondolewa kwake.
///   3. Break-glass inaisha kiotomatiki (saa 24, config `Platform:BreakGlassHours`).
///   4. Kila kitu kinaandikwa — na kikundi kinaona break-glass iliyogusa data yake.
/// </summary>
public class PlatformApprovalService
{
    public static readonly PlatformApprovalAction[] AdminActions =
        { PlatformApprovalAction.GrantAdmin, PlatformApprovalAction.ChangeAdminLevel, PlatformApprovalAction.DeactivateAdmin,
          PlatformApprovalAction.ResetMfa };

    private readonly ApplicationDbContext _context;
    private readonly PlatformAuditService _audit;
    private readonly IConfiguration _config;

    public PlatformApprovalService(ApplicationDbContext context, PlatformAuditService audit, IConfiguration config)
    {
        _context = context; _audit = audit; _config = config;
    }

    private int BreakGlassHours => int.TryParse(_config["Platform:BreakGlassHours"], out var v) && v > 0 ? v : 24;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record ApprovalRow(Guid Id, string Action, string Status, Guid? GroupId, string? GroupName,
        Guid? TargetPlatformAdminId, string? TargetName, Guid? TargetUserId, string? TargetUserName,
        string? RequestedBy, Guid RequestedByPlatformAdminId, string? DecidedBy, string? Reason, string? DecisionNote,
        string? PayloadJson, DateTime CreatedAt, DateTime? DecidedAt, DateTime? ExpiresAt,
        bool CanDecide, bool CanCancel, bool IsMine);

    // ------------------------------------------------------------------
    // Kuomba
    // ------------------------------------------------------------------
    public async Task<PlatformApproval> RequestBreakGlassAsync(Guid actorUserId, Guid groupId, string reason, CancellationToken ct = default)
    {
        var admin = await Require(actorUserId, PlatformAdminLevel.SuperAdmin, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 12)
            throw new ValidationException("Eleza sababu ya break-glass kwa uwazi (angalau herufi 12) — sababu hii itaonekana kwa kikundi.");
        if (!await _context.Groups.AnyAsync(g => g.Id == groupId, ct)) throw new NotFoundException("Kikundi hakipo.");
        var pending = await _context.PlatformApprovals.AnyAsync(a => a.Action == PlatformApprovalAction.BreakGlass
            && a.GroupId == groupId && a.RequestedByPlatformAdminId == admin.Id && a.Status == PlatformApprovalStatus.Pending, ct);
        if (pending) throw new ConflictException("Tayari una ombi la break-glass linalosubiri kwa kikundi hiki.");

        return await CreateAsync(admin, PlatformApprovalAction.BreakGlass, groupId: groupId, reason: reason, payload: null, ct: ct);
    }

    public async Task<PlatformApproval> RequestAdminAsync(Guid actorUserId, PlatformApprovalAction action, Guid? targetUserId,
        Guid? targetAdminId, PlatformAdminLevel? level, string reason, CancellationToken ct = default)
    {
        var admin = await Require(actorUserId, PlatformAdminLevel.SuperAdmin, ct);
        if (!AdminActions.Contains(action)) throw new ValidationException("Aina ya ombi si ya wasimamizi.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Eleza sababu ya ombi (angalau herufi 5).");

        switch (action)
        {
            case PlatformApprovalAction.GrantAdmin:
            {
                if (targetUserId is null) throw new ValidationException("Chagua mtumiaji anayepata nafasi ya msimamizi.");
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
                           ?? throw new NotFoundException("Mtumiaji hakupatikana.");
                if (await _context.PlatformAdmins.AnyAsync(a => a.UserId == targetUserId && a.IsActive, ct))
                    throw new ConflictException($"{user.FullName} tayari ni msimamizi hai.");
                if (level is null) throw new ValidationException("Chagua ngazi (Support / Oversight / SuperAdmin).");
                break;
            }
            case PlatformApprovalAction.ChangeAdminLevel:
            {
                if (targetAdminId is null || level is null) throw new ValidationException("Chagua msimamizi na ngazi mpya.");
                var target = await _context.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetAdminId && a.IsActive, ct)
                             ?? throw new NotFoundException("Msimamizi hakupatikana.");
                if (target.UserId == admin.UserId) throw new ValidationException("Huwezi kubadilisha ngazi yako mwenyewe kwa njia hii.");
                break;
            }
            case PlatformApprovalAction.ResetMfa:
            {
                if (targetAdminId is null) throw new ValidationException("Chagua msimamizi anayehitaji MFA mpya.");
                var target = await _context.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetAdminId && a.IsActive, ct)
                             ?? throw new NotFoundException("Msimamizi hakupatikana.");
                if (target.UserId == admin.UserId)
                    throw new ValidationException("Huwezi kuwasha upya MFA yako mwenyewe — hiyo ingevunja kinga ya MFA.");
                break;
            }
            case PlatformApprovalAction.DeactivateAdmin:
            {
                if (targetAdminId is null) throw new ValidationException("Chagua msimamizi wa kuondolewa.");
                var target = await _context.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetAdminId && a.IsActive, ct)
                             ?? throw new NotFoundException("Msimamizi hakupatikana.");
                if (target.UserId == admin.UserId) throw new ValidationException("Huwezi kujiondoa mwenyewe; msimamizi mwingine afanye hivyo.");
                break;
            }
        }

        return await CreateAsync(admin, action, reason: reason, targetUserId: targetUserId, targetAdminId: targetAdminId,
            payload: level is null ? null : new { level = level.Value.ToString() }, ct: ct);
    }

    private async Task<PlatformApproval> CreateAsync(PlatformAdmin admin, PlatformApprovalAction action, Guid? groupId = null,
        Guid? targetUserId = null, Guid? targetAdminId = null, object? payload = null, string? reason = null, CancellationToken ct = default)
    {
        var approval = new PlatformApproval
        {
            Id = Guid.NewGuid(), Action = action, Status = PlatformApprovalStatus.Pending,
            RequestedByPlatformAdminId = admin.Id, GroupId = groupId,
            TargetUserId = targetUserId, TargetPlatformAdminId = targetAdminId,
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, Json),
            Reason = (reason ?? string.Empty).Trim()
        };
        _context.PlatformApprovals.Add(approval);
        await _audit.WriteAsync(admin.Id, admin.UserId, "approval_requested", groupId, approval.Id, approval.Reason,
            new { action = action.ToString() }, ct: ct);
        await _context.SaveChangesAsync(ct);
        return approval;
    }

    // ------------------------------------------------------------------
    // Kuamua
    // ------------------------------------------------------------------
    public async Task<PlatformApproval> DecideAsync(Guid actorUserId, Guid approvalId, bool approve, string? note, CancellationToken ct = default)
    {
        var decider = await Require(actorUserId,
            // Break-glass: Oversight anaruhusiwa kuidhinisha. Mambo ya wasimamizi: SuperAdmin pekee.
            PlatformAdminLevel.Oversight, ct);

        var a = await _context.PlatformApprovals.FirstOrDefaultAsync(x => x.Id == approvalId, ct)
                ?? throw new NotFoundException("Ombi halipo.");
        if (a.Status != PlatformApprovalStatus.Pending) throw new ConflictException($"Ombi tayari ni {a.Status}.");
        if (a.RequestedByPlatformAdminId == decider.Id)
            throw new ForbiddenException("Huwezi kuidhinisha ombi lako mwenyewe — msimamizi MWINGINE anahitajika.");

        if (AdminActions.Contains(a.Action) && decider.Level < PlatformAdminLevel.SuperAdmin)
            throw new ForbiddenException("Mabadiliko ya wasimamizi yanahitaji msimamizi mkuu (Super Admin) wa pili.");
        if (a.Action == PlatformApprovalAction.DeactivateAdmin && a.TargetPlatformAdminId == decider.Id)
            throw new ForbiddenException("Huwezi kuidhinisha kuondolewa kwako mwenyewe.");
        if (!approve && string.IsNullOrWhiteSpace(note))
            throw new ValidationException("Kukataa kunahitaji sababu.");

        if (!approve)
        {
            a.Status = PlatformApprovalStatus.Rejected;
            a.DecidedByPlatformAdminId = decider.Id;
            a.DecidedAt = DateTime.UtcNow;
            a.DecisionNote = Trim(note, 500);
            await _audit.WriteAsync(decider.Id, decider.UserId, "approval_rejected", a.GroupId, a.Id, a.Reason,
                new { action = a.Action.ToString(), note }, ct: ct);
            await _context.SaveChangesAsync(ct);
            return a;
        }

        a.Status = PlatformApprovalStatus.Approved;
        a.DecidedByPlatformAdminId = decider.Id;
        a.DecidedAt = DateTime.UtcNow;
        a.DecisionNote = Trim(note, 500);

        switch (a.Action)
        {
            case PlatformApprovalAction.BreakGlass:
                a.ExpiresAt = DateTime.UtcNow.AddHours(BreakGlassHours);
                await _audit.WriteAsync(decider.Id, decider.UserId, "break_glass_approved", a.GroupId, a.Id, a.Reason,
                    new { expiresAt = a.ExpiresAt }, ct: ct);
                if (a.GroupId is { } gid)
                    await PlatformAuditService.WriteGroupVisibleAsync(_context, gid, decider.UserId,
                        AuditAction.PlatformBreakGlassApproved, a.Id,
                        new { msimamizi = decider.UserId, inaisha = a.ExpiresAt },
                        $"Ufikiaji wa msimamizi wa mfumo (break-glass) umeidhinishwa. Sababu: {a.Reason}", ct);
                break;

            case PlatformApprovalAction.GrantAdmin:
                await ApplyGrantAsync(a, ct);
                break;

            case PlatformApprovalAction.ChangeAdminLevel:
                await ApplyLevelChangeAsync(a, ct);
                break;

            case PlatformApprovalAction.DeactivateAdmin:
                await ApplyDeactivateAsync(a, ct);
                break;

            case PlatformApprovalAction.ResetMfa:
                await ApplyMfaResetAsync(a, ct);
                break;
        }

        await _context.SaveChangesAsync(ct);
        return a;
    }

    private async Task ApplyGrantAsync(PlatformApproval a, CancellationToken ct)
    {
        var level = ParseLevel(a.PayloadJson) ?? PlatformAdminLevel.Support;
        var targetUserId = a.TargetUserId ?? throw new ConflictException("Ombi halina mtumiaji lengwa.");
        var existing = await _context.PlatformAdmins.FirstOrDefaultAsync(x => x.UserId == targetUserId, ct);
        if (existing is null)
            _context.PlatformAdmins.Add(new PlatformAdmin
            {
                Id = Guid.NewGuid(), UserId = targetUserId, Level = level, IsActive = true,
                CreatedByPlatformAdminId = a.RequestedByPlatformAdminId
            });
        else
        {
            existing.IsActive = true;
            existing.Level = level;
            existing.DeactivatedAt = null;
        }
        await _audit.WriteAsync(a.DecidedByPlatformAdminId, targetUserId, "admin_granted", subjectId: targetUserId,
            detail: new { level = level.ToString() }, ct: ct);
    }

    private async Task ApplyLevelChangeAsync(PlatformApproval a, CancellationToken ct)
    {
        var level = ParseLevel(a.PayloadJson) ?? throw new ConflictException("Ombi halina ngazi mpya.");
        var target = await _context.PlatformAdmins.FirstOrDefaultAsync(x => x.Id == a.TargetPlatformAdminId, ct)
                     ?? throw new NotFoundException("Msimamizi hakupatikana.");
        var before = target.Level;
        target.Level = level;
        // Ngazi ikishuka, vikao vya zamani vinafutwa (token ina `pal` ya ngazi ya zamani).
        await BumpTokenVersionAsync(target.UserId, ct);
        await _audit.WriteAsync(a.DecidedByPlatformAdminId, target.UserId, "admin_level_changed",
            subjectId: target.Id, detail: new { before = before.ToString(), after = level.ToString() }, ct: ct);
    }

    private async Task ApplyDeactivateAsync(PlatformApproval a, CancellationToken ct)
    {
        var target = await _context.PlatformAdmins.FirstOrDefaultAsync(x => x.Id == a.TargetPlatformAdminId, ct)
                     ?? throw new NotFoundException("Msimamizi hakupatikana.");
        var active = await _context.PlatformAdmins.CountAsync(x => x.IsActive, ct);
        if (active <= 1) throw new ConflictException("Huwezi kuondoa msimamizi wa mwisho — mfumo utabaki bila msimamizi.");
        target.IsActive = false;
        target.DeactivatedAt = DateTime.UtcNow;
        await BumpTokenVersionAsync(target.UserId, ct);
        await _audit.WriteAsync(a.DecidedByPlatformAdminId, target.UserId, "admin_deactivated",
            subjectId: target.Id, ct: ct);
    }

    /// <summary>
    /// MFA mpya: siri ya sasa inafutwa KABISA (si kuwekwa pembeni), kikao kimefungwa,
    /// na msimamizi atalazimika kuskani QR mpya akiingia mara ijayo. Hii ni njia ya
    /// pekee ya kukabiliana na simu iliyopotea — bila kuipunguza kinga kwa mtu yeyote.
    /// </summary>
    private async Task ApplyMfaResetAsync(PlatformApproval a, CancellationToken ct)
    {
        var target = await _context.PlatformAdmins.FirstOrDefaultAsync(x => x.Id == a.TargetPlatformAdminId, ct)
                     ?? throw new NotFoundException("Msimamizi hakupatikana.");
        target.TotpSecretProtected = null;
        target.PendingTotpSecretProtected = null;
        target.MfaEnabledAt = null;
        target.LastMfaVerifiedAt = null;
        await BumpTokenVersionAsync(target.UserId, ct);
        await _audit.WriteAsync(a.DecidedByPlatformAdminId, target.UserId, "admin_mfa_reset",
            subjectId: target.Id, reason: a.Reason, ct: ct);
    }

    private async Task BumpTokenVersionAsync(Guid userId, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null) user.TokenVersion++;
    }

    private static PlatformAdminLevel? ParseLevel(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("level", out var l) && Enum.TryParse<PlatformAdminLevel>(l.GetString(), true, out var parsed))
                return parsed;
        }
        catch (JsonException) { }
        return null;
    }

    // ------------------------------------------------------------------
    // Break-glass: je, msimamizi huyu ana ufikiaji halali sasa?
    // ------------------------------------------------------------------
    public async Task<bool> HasBreakGlassAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var admin = await _context.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct);
        if (admin is null) return false;
        var now = DateTime.UtcNow;
        return await _context.PlatformApprovals.AsNoTracking().AnyAsync(a =>
            a.Action == PlatformApprovalAction.BreakGlass && a.GroupId == groupId &&
            a.RequestedByPlatformAdminId == admin.Id &&
            a.Status == PlatformApprovalStatus.Approved && a.ExpiresAt != null && a.ExpiresAt > now, ct);
    }

    /// <summary>Muhuri wa matumizi: unaandikwa mara moja kila dakika 10 kwa kila msimamizi/kikundi.</summary>
    public async Task MarkBreakGlassUsedAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var admin = await _context.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (admin is null) return;
        var since = DateTime.UtcNow.AddMinutes(-10);
        if (await _context.PlatformAuditEvents.AnyAsync(a => a.PlatformAdminId == admin.Id && a.GroupId == groupId
                && a.Action == "break_glass_used" && a.At >= since, ct)) return;
        await _audit.WriteAsync(admin.Id, userId, "break_glass_used", groupId, detail: new { note = "taarifa kamili zimefunguliwa" }, ct: ct);
        await PlatformAuditService.WriteGroupVisibleAsync(_context, groupId, userId, AuditAction.PlatformBreakGlassUsed,
            groupId, new { msimamizi = userId }, "Msimamizi wa mfumo amefungua taarifa kamili za kikundi (break-glass).", ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> ExpireStaleAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _context.PlatformApprovals
            .Where(a => a.Status == PlatformApprovalStatus.Approved && a.ExpiresAt != null && a.ExpiresAt <= now)
            .ToListAsync(ct);
        foreach (var a in rows)
        {
            a.Status = PlatformApprovalStatus.Expired;
            await _audit.WriteAsync(a.DecidedByPlatformAdminId, null, "approval_expired", a.GroupId, a.Id, a.Reason, ct: ct);
        }
        if (rows.Count > 0) await _context.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<PlatformApproval> CancelAsync(Guid actorUserId, Guid approvalId, CancellationToken ct = default)
    {
        var admin = await Require(actorUserId, PlatformAdminLevel.Support, ct);
        var a = await _context.PlatformApprovals.FirstOrDefaultAsync(x => x.Id == approvalId, ct)
                ?? throw new NotFoundException("Ombi halipo.");
        if (a.RequestedByPlatformAdminId != admin.Id) throw new ForbiddenException("Unaweza kughairi ombi lako mwenyewe pekee.");
        if (a.Status != PlatformApprovalStatus.Pending) throw new ConflictException($"Ombi tayari ni {a.Status}.");
        a.Status = PlatformApprovalStatus.Cancelled;
        a.DecidedAt = DateTime.UtcNow;
        await _audit.WriteAsync(admin.Id, admin.UserId, "approval_cancelled", a.GroupId, a.Id, ct: ct);
        await _context.SaveChangesAsync(ct);
        return a;
    }

    // ------------------------------------------------------------------
    // Orodha
    // ------------------------------------------------------------------
    public async Task<List<ApprovalRow>> ListAsync(Guid actorUserId, bool pendingOnly = false, CancellationToken ct = default)
    {
        var me = await Require(actorUserId, PlatformAdminLevel.Support, ct);
        await ExpireStaleAsync(ct);

        var q = _context.PlatformApprovals.AsNoTracking();
        if (pendingOnly) q = q.Where(a => a.Status == PlatformApprovalStatus.Pending);
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);

        var adminIds = rows.SelectMany(r => new[] { r.RequestedByPlatformAdminId, r.DecidedByPlatformAdminId ?? Guid.Empty })
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var admins = await _context.PlatformAdmins.AsNoTracking().Include(a => a.User)
            .Where(a => adminIds.Contains(a.Id)).Select(a => new { a.Id, a.Level, Name = a.User!.FullName }).ToListAsync(ct);
        var groupIds = rows.Where(r => r.GroupId != null).Select(r => r.GroupId!.Value).Distinct().ToList();
        var groups = await _context.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).Select(g => new { g.Id, g.Name }).ToListAsync(ct);
        var userIds = rows.Where(r => r.TargetUserId != null).Select(r => r.TargetUserId!.Value).Distinct().ToList();
        var users = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        var targetAdminIds = rows.Where(r => r.TargetPlatformAdminId != null).Select(r => r.TargetPlatformAdminId!.Value).Distinct().ToList();
        var targetAdmins = await _context.PlatformAdmins.AsNoTracking().Include(a => a.User)
            .Where(a => targetAdminIds.Contains(a.Id)).Select(a => new { a.Id, Name = a.User!.FullName }).ToListAsync(ct);

        return rows.Select(a => new ApprovalRow(
            a.Id, a.Action.ToString(), a.Status.ToString(), a.GroupId,
            a.GroupId is { } g ? groups.FirstOrDefault(x => x.Id == g)?.Name : null,
            a.TargetPlatformAdminId,
            a.TargetPlatformAdminId is { } ta ? targetAdmins.FirstOrDefault(x => x.Id == ta)?.Name : null,
            a.TargetUserId,
            a.TargetUserId is { } tu ? users.FirstOrDefault(x => x.Id == tu)?.FullName : null,
            admins.FirstOrDefault(x => x.Id == a.RequestedByPlatformAdminId)?.Name,
            a.RequestedByPlatformAdminId,
            a.DecidedByPlatformAdminId is { } db ? admins.FirstOrDefault(x => x.Id == db)?.Name : null,
            a.Reason, a.DecisionNote, a.PayloadJson, a.CreatedAt, a.DecidedAt, a.ExpiresAt,
            CanDecide: a.Status == PlatformApprovalStatus.Pending && a.RequestedByPlatformAdminId != me.Id
                       && (!AdminActions.Contains(a.Action) || me.Level == PlatformAdminLevel.SuperAdmin)
                       && !(a.Action == PlatformApprovalAction.DeactivateAdmin && a.TargetPlatformAdminId == me.Id),
            CanCancel: a.Status == PlatformApprovalStatus.Pending && a.RequestedByPlatformAdminId == me.Id,
            IsMine: a.RequestedByPlatformAdminId == me.Id)).ToList();
    }

    private async Task<PlatformAdmin> Require(Guid userId, PlatformAdminLevel min, CancellationToken ct)
    {
        var admin = await _context.PlatformAdmins.FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct)
                    ?? throw new ForbiddenException("Huna nafasi ya msimamizi wa mfumo.");
        if (admin.Level < min) throw new ForbiddenException("Ngazi yako ya usimamizi hairuhusu kitendo hiki.");
        if (admin.MfaEnabledAt is null) throw new ForbiddenException("MFA haijawashwa kwenye akaunti yako ya usimamizi.");
        return admin;
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..(max - 1)] + "…";
}
