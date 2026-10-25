using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// SASISHO17 (P0-6): kufuta data ya fedha ya kikundi kunahitaji VIZUIZI VITATU kwa pamoja:
///   1. Server: Truth:AllowReset=true (msimamizi wa server; ProductionGuard inakataa kuanza nje ya Development ikiwa imewashwa).
///   2. Kiongozi A (Mwenyekiti/Mtunza-hazina) anaomba, akiandika jina kamili la kikundi.
///   3. Kiongozi B (tofauti, mwenye nguvu kamili) anaidhinisha ndani ya Truth:ResetRequestTtlHours, akiandika jina tena.
/// Wanachama wote wanaarifiwa ombi linapotolewa na linapotekelezwa. Kumbukumbu za ukaguzi hazifutwi kamwe.
/// </summary>
public class ResetRequestService
{
    public const string AllowResetKey = "Truth:AllowReset";
    private const int DefaultTtlHours = 24;
    private static readonly GroupRole[] ResetRoles = { GroupRole.Chairperson, GroupRole.Treasurer };

    private readonly ApplicationDbContext _context;
    private readonly TruthSetupService _setup;
    private readonly NotificationService _notify;
    private readonly IConfiguration _config;

    public ResetRequestService(ApplicationDbContext context, TruthSetupService setup, NotificationService notify, IConfiguration config)
    {
        _context = context; _setup = setup; _notify = notify; _config = config;
    }

    private bool Allowed => bool.TryParse(_config[AllowResetKey], out var b) && b;
    private int TtlHours => int.TryParse(_config["Truth:ResetRequestTtlHours"], out var h) && h is > 0 and <= 168 ? h : DefaultTtlHours;

    /// <summary>Ujumbe wa jumla — hautaji jina la swichi ya server (si ramani kwa mshambuliaji).</summary>
    public const string DisabledMessage = "Kufuta data ya fedha kumezimwa kwenye server hii. Wasiliana na msimamizi wa server. Jaribio hili limerekodiwa.";

    private async Task BlockedAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var actor = await MemberAccess.FindAsync(_context, groupId, userId, ct);
        await AuditTrail.AddAsync(_context, groupId, actor?.Id, userId, AuditAction.ResetBlocked, nameof(Group), groupId, null,
            new { blocked = true }, null, "Jaribio la kufuta data ya fedha limezuiwa na server", ct);
        await _context.SaveChangesAsync(ct);
        throw new ForbiddenException(DisabledMessage);
    }

    private static void EnsureConfirm(Group g, string? confirm)
    {
        if (!string.Equals(confirm?.Trim(), g.Name.Trim(), StringComparison.Ordinal))
            throw new ValidationException("Kuthibitisha, andika jina kamili la kikundi.");
    }

    public async Task<GroupResetRequest> RequestAsync(Guid groupId, bool keepLoans, string? confirm, Guid userId, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct);
        MemberAccess.EnsureRole(actor, ResetRoles, "Kufuta data ya fedha kunaombwa/kuidhinishwa na Mwenyekiti au Mtunza-hazina.");
        if (!Allowed) await BlockedAsync(groupId, userId, ct);
        var g = await _context.Groups.AsNoTracking().FirstAsync(x => x.Id == groupId, ct);
        EnsureConfirm(g, confirm);
        return await DbTx.SerializedAsync(_context, groupId, DbTx.Decision, async () =>
        {
            var now = DateTime.UtcNow;
            if (await _context.GroupResetRequests.AnyAsync(r => r.GroupId == groupId && r.Status == ProposalStatus.Pending && r.ExpiresAt > now, ct))
                throw new ConflictException("Tayari kuna ombi la kufuta data linalosubiri idhini.");
            var r = new GroupResetRequest
            {
                Id = Guid.NewGuid(), GroupId = groupId, KeepLoans = keepLoans, RequestedByUserId = userId, RequestedAt = now, ExpiresAt = now.AddHours(TtlHours)
            };
            _context.GroupResetRequests.Add(r);
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, AuditAction.ResetRequested, nameof(GroupResetRequest), r.Id, null,
                new { keepLoans, r.ExpiresAt }, null, "Ombi la kufuta data ya fedha (linasubiri kiongozi wa pili)", ct);
            await _notify.NotifyAllMembersAsync(groupId, "ResetRequested", $"Ombi la kufuta data ya fedha",
                $"Kiongozi ameomba kufuta data ya fedha ya kikundi (M-Koba, malipo ya mkono, marekebisho). Linahitaji kiongozi wa pili kabla ya {r.ExpiresAt:dd/MM/yyyy HH:mm} UTC. Kama hukubaliani, wasiliana na viongozi mara moja.",
                "/mipangilio", ct);
            await _context.SaveChangesAsync(ct);
            return r;
        }, ct);
    }

    public async Task<TruthSetupService.ResetReport> ApproveAsync(Guid groupId, Guid requestId, string? confirm, Guid userId, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct);
        MemberAccess.EnsureRole(actor, ResetRoles, "Kufuta data ya fedha kunaombwa/kuidhinishwa na Mwenyekiti au Mtunza-hazina.");
        if (!Allowed) await BlockedAsync(groupId, userId, ct);
        var g = await _context.Groups.AsNoTracking().FirstAsync(x => x.Id == groupId, ct);
        EnsureConfirm(g, confirm);
        return await DbTx.SerializedAsync(_context, groupId, DbTx.Decision, async () =>
        {
            var r = await _context.GroupResetRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct)
                    ?? throw new NotFoundException("Ombi halipo.");
            if (r.Status != ProposalStatus.Pending) throw new ConflictException("Ombi hili tayari limeshughulikiwa.");
            if (r.ExpiresAt <= DateTime.UtcNow) throw new ConflictException("Ombi hili limeisha muda. Omba upya.");
            if (r.RequestedByUserId == userId) throw new ForbiddenException("Aliyeomba HAWEZI kuidhinisha mwenyewe — kiongozi wa pili anahitajika.");
            r.Status = ProposalStatus.Approved; r.DecidedByUserId = userId; r.DecidedAt = DateTime.UtcNow;
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, AuditAction.ResetApproved, nameof(GroupResetRequest), r.Id, null,
                new { r.KeepLoans, requestedBy = r.RequestedByUserId }, null, "Kufuta data ya fedha kumeidhinishwa na kiongozi wa pili", ct);
            await _context.SaveChangesAsync(ct);
            var report = await _setup.ResetGroupFinancialDataAsync(groupId, false, r.KeepLoans, userId, ct);
            await _notify.NotifyAllMembersAsync(groupId, "ResetDone", $"Data ya fedha imefutwa",
                $"Data ya fedha ya kikundi imefutwa kwa idhini ya viongozi wawili. Kumbukumbu za ukaguzi zinabaki.", "/mipangilio", ct);
            await _context.SaveChangesAsync(ct);
            return report;
        }, ct);
    }

    public async Task CancelAsync(Guid groupId, Guid requestId, Guid userId, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct);
        await DbTx.SerializedAsync(_context, groupId, DbTx.Decision, async () =>
        {
            var r = await _context.GroupResetRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct)
                    ?? throw new NotFoundException("Ombi halipo.");
            if (r.Status != ProposalStatus.Pending) throw new ConflictException("Ombi hili tayari limeshughulikiwa.");
            // Kiongozi yeyote mwenye nguvu anaweza KUKATAA (kuzuia ni salama); aliyeomba anaweza kughairi.
            r.Status = ProposalStatus.Rejected; r.DecidedByUserId = userId; r.DecidedAt = DateTime.UtcNow;
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, AuditAction.GovernanceRejected, nameof(GroupResetRequest), r.Id, null,
                new { rejected = true, byRequester = r.RequestedByUserId == userId }, null, "Ombi la kufuta data limekataliwa/ghairiwa", ct);
            await _context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<List<object>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, tracking: false);
        var now = DateTime.UtcNow;
        var list = await _context.GroupResetRequests.AsNoTracking().Where(r => r.GroupId == groupId).OrderByDescending(r => r.RequestedAt).Take(20).ToListAsync(ct);
        return list.Select(r => (object)new
        {
            r.Id, r.KeepLoans, status = r.Status == ProposalStatus.Pending && r.ExpiresAt <= now ? "Expired" : r.Status.ToString(),
            r.RequestedAt, r.ExpiresAt, r.DecidedAt, mine = r.RequestedByUserId == userId,
            canApprove = r.Status == ProposalStatus.Pending && r.ExpiresAt > now && r.RequestedByUserId != userId
        }).ToList();
    }
}
