using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Localization;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Benefits;

/// <summary>
/// Kubadilisha hali ya mwanachama (kufukuza, kujitoa, kufariki, kusimamishwa, kurejeshwa) — HAKUNA kiongozi mmoja
/// anayeweza kufanya peke yake:
///   • Kufukuza (Katiba 12(3)): kura za NDIYO ≥ asilimia ya sera (mf. 75%) ya wanachama WOTE walio kikundini
///     (mhusika hahesabiwi wala hapigi kura).
///   • Mengine: viongozi N (sera) mbali na aliyeomba na mhusika.
/// Ikiidhinishwa: GroupMember.Status inabadilika + MemberStatusHistory (EffectiveFrom) inaandikwa — Injini ya Ukweli
/// inasimamisha madeni mapya ya mwanachama aliyetoka kuanzia tarehe hiyo.
/// </summary>
public class MemberStatusService
{
    public const string SubjectStatusChange = "MemberStatusChange";

    private readonly ApplicationDbContext _context;
    private readonly BenefitService _benefits;
    private readonly NotificationService _notify;

    public MemberStatusService(ApplicationDbContext context, BenefitService benefits, NotificationService notify)
    {
        _context = context;
        _benefits = benefits;
        _notify = notify;
    }

    public sealed class RequestDto
    {
        public Guid MemberId { get; set; }
        public MemberStatus ToStatus { get; set; }
        public bool IsExpulsion { get; set; }
        public DateTime EffectiveDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? EvidenceNote { get; set; }
    }

    private static readonly MemberStatus[] Settable =
        { MemberStatus.Active, MemberStatus.Suspended, MemberStatus.Inactive, MemberStatus.Exited, MemberStatus.Deceased, MemberStatus.Archived };

    private async Task<List<GroupMember>> EligibleVotersAsync(MemberStatusChangeRequest r, CancellationToken ct)
    {
        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == r.GroupId && NotificationService.InGroupStatuses.Contains(m.Status) && m.Id != r.GroupMemberId).ToListAsync(ct);
        return r.LeadersOnly
            ? members.Where(m => Common.MemberAccess.IsEffectiveLeader(m) && m.UserId != r.RequestedByUserId).ToList()
            : members;
    }

    /// <param name="minApprovals">
    /// SASISHO19: kiwanja cha chini cha idhini (mf. kuondolewa kwa deni la tukio = viongozi ZAIDI ya wawili).
    /// null = tumia sera ya mafao pekee (tabia ya zamani).
    /// </param>
    public async Task<MemberStatusChangeRequest> RequestAsync(Guid groupId, RequestDto dto, Guid userId, CancellationToken ct = default, int? minApprovals = null)
    {
        var actor = await Common.MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, "Ombi la kubadilisha hali linaanzishwa na kiongozi.");
        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == dto.MemberId && m.GroupId == groupId, ct)
            ?? throw new NotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        if (member.Id == actor.Id) throw new ForbiddenException("Huwezi kuomba kubadilisha hali yako mwenyewe kwa njia hii.");
        if (!Settable.Contains(dto.ToStatus)) throw new ValidationException("Hali hiyo haiwezi kuwekwa kwa ombi.");
        if (member.Status == dto.ToStatus) throw new ConflictException($"Mwanachama tayari yuko kwenye hali {dto.ToStatus}.");
        if (dto.IsExpulsion && dto.ToStatus != MemberStatus.Exited) throw new ValidationException("Kufukuzwa kunamaanisha hali 'Exited'.");
        if (string.IsNullOrWhiteSpace(dto.Reason)) throw new ValidationException("Eleza sababu (na kifungu cha Katiba kama kipo).");
        if (dto.EffectiveDate.Date < member.JoinedAt.Date) throw new ValidationException("Tarehe ya kuanza haiwezi kuwa kabla ya kujiunga.");
        if (dto.EffectiveDate.Date > TruthLedgerService.NowEat().Date.AddDays(31)) throw new ValidationException("Tarehe ya kuanza iko mbali mno mbele.");
        if (await _context.MemberStatusChangeRequests.AnyAsync(r => r.GroupId == groupId && r.GroupMemberId == member.Id && r.Status == ProposalStatus.Pending, ct))
            throw new ConflictException("Tayari kuna ombi la hali ya mwanachama huyu linalosubiri kura.");
        // SASISHO19: kuondolewa kwa deni la tukio (minApprovals) HAKUHITAJI sera ya mafao —
        // kinachotakiwa ni viongozi wa kikundi. Kufukuza (IsExpulsion) bado kunahitaji sera,
        // kwa sababu asilimia ya kura za wanachama wote inatoka kwenye sera hiyo.
        var gov = await _benefits.GetActiveGovernanceAsync(groupId, ct);
        if (gov is null && (dto.IsExpulsion || minApprovals is null))
            throw new PolicyNotConfiguredException(groupId,
                "Kikundi hakijaweka sera ya mafao na idhini (idadi ya kura, sheria ya idadi ya wanachama, makato n.k.). Kiongozi apendekeze kwenye Mafao → Sera, na kiongozi wa pili aidhinishe.");

        var r = new MemberStatusChangeRequest
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = member.Id, FromStatus = member.Status, ToStatus = dto.ToStatus,
            IsExpulsion = dto.IsExpulsion, EffectiveDate = DateTime.SpecifyKind(dto.EffectiveDate.Date, DateTimeKind.Utc),
            Reason = dto.Reason.Trim(), EvidenceNote = dto.EvidenceNote?.Trim(), GovernanceId = gov?.Id, RequestedByUserId = userId,
            LeadersOnly = !dto.IsExpulsion
        };
        var voters = await EligibleVotersAsync(r, ct);
        r.RequiredApprovals = dto.IsExpulsion
            ? (int)Math.Ceiling((gov?.ExpulsionVotePercent ?? 100m) / 100m * voters.Count)
            : gov?.StatusChangeApprovals ?? 0;
        // SASISHO19: sera ya kikundi haiwezi kupunguza kiwango cha chini kinachotakiwa na kanuni
        // (mf. kuondolewa kwa deni la tukio: viongozi ZAIDI ya wawili).
        if (minApprovals is { } floor && floor > r.RequiredApprovals) r.RequiredApprovals = floor;
        if (r.RequiredApprovals < 1 || voters.Count < r.RequiredApprovals)
            throw new ConflictException($"Hakuna wapiga kura wa kutosha: wanaoruhusiwa {voters.Count}, wanaohitajika {r.RequiredApprovals}. " +
                                        "Ongezeni viongozi (wasio aliyeomba wala mhusika) kisha rudia.");
        // Kura za HAPANA zinazofanya idhini isiwezekane tena
        r.RejectionsToReject = voters.Count - r.RequiredApprovals + 1;

        _context.MemberStatusChangeRequests.Add(r);
        var name = member.User?.FullName ?? member.MemberNumber;
        await AuditTrail.AddAsync(_context, groupId, member.Id, userId, AuditAction.StatusChangeRequested, nameof(MemberStatusChangeRequest), r.Id, null,
            new { From = r.FromStatus.ToString(), To = r.ToStatus.ToString(), r.IsExpulsion, r.EffectiveDate, r.RequiredApprovals, r.LeadersOnly }, null, r.Reason, ct);
        var body = r.IsExpulsion
            ? Loc.L($"Pendekezo la KUMFUKUZA {name} kuanzia {r.EffectiveDate:dd/MM/yyyy}. Sababu: {r.Reason}. Kura {r.RequiredApprovals} za NDIYO zinahitajika.")
            : Loc.L($"Pendekezo: {name} kuwa {StatusName(r.ToStatus)} kuanzia {r.EffectiveDate:dd/MM/yyyy}. Sababu: {r.Reason}. Kura {r.RequiredApprovals} za NDIYO zinahitajika.");
        await _notify.NotifyAsync(groupId, voters.Select(v => v.Id), "VoteNeeded", r.IsExpulsion ? "Kura: kufukuza mwanachama" : "Kura: hali ya mwanachama", body, "/wanachama/kura", ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>SASISHO17: kura zinapangwa mstari (DbTx.Decision) — kura mbili za wakati mmoja haziwezi kutekeleza mara mbili.</summary>
    public Task<MemberStatusChangeRequest> VoteAsync(Guid groupId, Guid requestId, bool approve, string? reason, Guid userId, CancellationToken ct = default)
        => Common.DbTx.SerializedAsync(_context, groupId, Common.DbTx.Decision, () => VoteCoreAsync(groupId, requestId, approve, reason, userId, ct), ct);

    private async Task<MemberStatusChangeRequest> VoteCoreAsync(Guid groupId, Guid requestId, bool approve, string? reason, Guid userId, CancellationToken ct)
    {
        var voter = await Common.MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);
        var r = await _context.MemberStatusChangeRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Ombi halipo.");
        if (r.Status != ProposalStatus.Pending) throw new ConflictException($"Ombi tayari ni {r.Status}.");
        var voters = await EligibleVotersAsync(r, ct);
        if (voters.All(v => v.Id != voter.Id))
            throw new ForbiddenException(r.LeadersOnly ? "Kura hii ni ya viongozi (si aliyeomba wala mhusika)." : "Mhusika hapigi kura kwenye ombi lake.");
        if (await _context.GovernanceVotes.AnyAsync(v => v.SubjectType == SubjectStatusChange && v.SubjectId == r.Id && v.GroupMemberId == voter.Id, ct))
            throw new ConflictException("Tayari umepiga kura.");
        if (!approve && string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Kura ya HAPANA inahitaji sababu.");

        var vote = new GovernanceVote
        {
            Id = Guid.NewGuid(), GroupId = groupId, SubjectType = SubjectStatusChange, SubjectId = r.Id, GroupMemberId = voter.Id, UserId = userId,
            Approve = approve, Reason = reason?.Trim(), RoleAtVote = Common.MemberAccess.EffectiveRole(voter)
        };
        _context.GovernanceVotes.Add(vote);
        await AuditTrail.AddAsync(_context, groupId, r.GroupMemberId, userId, AuditAction.VoteCast, nameof(GovernanceVote), vote.Id, null,
            new { vote.Approve, vote.Reason, Subject = SubjectStatusChange }, null, reason, ct);

        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectStatusChange && v.SubjectId == r.Id).ToListAsync(ct);
        votes.Add(vote);
        var yes = votes.Count(v => v.Approve);
        var no = votes.Count(v => !v.Approve);
        var member = await _context.GroupMembers.Include(m => m.User).FirstAsync(m => m.Id == r.GroupMemberId, ct);
        var name = member.User?.FullName ?? member.MemberNumber;

        if (yes >= r.RequiredApprovals)
        {
            if (member.Status != r.FromStatus)
                throw new ConflictException($"Hali ya mwanachama imebadilika tangu ombi ({r.FromStatus} → {member.Status}). Ombi jipya linahitajika.");
            r.Status = ProposalStatus.Approved; r.DecidedAt = DateTime.UtcNow;
            // SASISHO17: EffectiveDate HALISI — hali inabadilika tarehe yake ikifika (si siku ya kura). Mpaka hapo mwanachama
            // ana haki zake zote. StatusSchedulerBackgroundService inatekeleza maombi yaliyofikia tarehe.
            if (r.EffectiveDate.Date <= TruthLedgerService.NowEat().Date)
                await ApplyAsync(r, member, userId, yes, no, ct);
            else
            {
                await AuditTrail.AddAsync(_context, groupId, member.Id, userId, AuditAction.MemberStatusChanged, nameof(MemberStatusChangeRequest), r.Id,
                    new { Status = r.FromStatus.ToString() }, new { Scheduled = r.ToStatus.ToString(), r.EffectiveDate }, null, $"Kura {yes} za NDIYO (itaanza {r.EffectiveDate:dd/MM/yyyy}): {r.Reason}", ct);
                await _notify.NotifyAllMembersAsync(groupId, "StatusChanged", $"{name}: {StatusName(r.ToStatus)}",
                    Loc.L($"Imeidhinishwa kwa kura {yes}: {name} atakuwa {StatusName(r.ToStatus)} kuanzia {r.EffectiveDate:dd/MM/yyyy}. Mpaka tarehe hiyo haki zake zinaendelea."), "/wanachama", ct);
            }
        }
        else if (no >= r.RejectionsToReject)
        {
            r.Status = ProposalStatus.Rejected; r.DecidedAt = DateTime.UtcNow;
            await AuditTrail.AddAsync(_context, groupId, member.Id, userId, AuditAction.GovernanceRejected, nameof(MemberStatusChangeRequest), r.Id, null,
                new { yes, no }, null, "Kura hazitoshi tena", ct);
        }
        await _context.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>
    /// SASISHO17: tekeleza ombi lililoidhinishwa (EffectiveDate imefika). Hali ikitoka kwenye hali hai na mhusika ni kiongozi,
    /// NAFASI YAKE INAONDOLEWA (NEW-4) — hakuna "Mtunza-hazina aliyefukuzwa" anayeendelea kuidhinisha. Kurejeshwa kwa hali
    /// hairudishi cheo: kiongozi mpya anateuliwa kwa kura (RoleChangeService).
    /// </summary>
    private async Task ApplyAsync(MemberStatusChangeRequest r, GroupMember member, Guid actorUserId, int? yes, int? no, CancellationToken ct)
    {
        var name = member.User?.FullName ?? member.MemberNumber;
        r.AppliedAt = DateTime.UtcNow;
        if (member.Status != r.FromStatus)
        {
            await AuditTrail.AddAsync(_context, r.GroupId, member.Id, actorUserId, AuditAction.StatusChangeApplied, nameof(MemberStatusChangeRequest), r.Id,
                null, new { applied = false, current = member.Status.ToString() }, null, "Haikutekelezwa: hali ilibadilika kabla ya tarehe ya kuanza", ct);
            return;
        }
        member.Status = r.ToStatus;
        _context.MemberStatusHistories.Add(new MemberStatusHistory
        {
            Id = Guid.NewGuid(), MemberId = member.Id, FromStatus = r.FromStatus, ToStatus = r.ToStatus, Reason = r.Reason,
            ChangedBy = actorUserId, ChangedAt = DateTime.UtcNow, EffectiveFrom = r.EffectiveDate,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { requestId = r.Id, yes, no, r.RequiredApprovals, r.IsExpulsion })
        });
        await AuditTrail.AddAsync(_context, r.GroupId, member.Id, actorUserId, AuditAction.MemberStatusChanged, nameof(GroupMember), member.Id,
            new { Status = r.FromStatus.ToString() }, new { Status = r.ToStatus.ToString(), r.EffectiveDate }, null, $"Kura za NDIYO: {r.Reason}", ct);
        await _notify.NotifyAllMembersAsync(r.GroupId, "StatusChanged", $"{name}: {StatusName(r.ToStatus)}",
            r.IsExpulsion ? Loc.L($"{name} amefukuzwa kuanzia {r.EffectiveDate:dd/MM/yyyy}.") : Loc.L($"Hali ya {name} imekuwa {StatusName(r.ToStatus)} kuanzia {r.EffectiveDate:dd/MM/yyyy}."), "/wanachama", ct);

        if (!Common.MemberAccess.IsActive(r.ToStatus))
        {
            if (Common.MemberAccess.LeaderRoles.Contains(member.Role))
            {
                var oldRole = member.Role;
                member.Role = GroupRole.Member; member.LeaderPowersFrom = null;
                await AuditTrail.AddAsync(_context, r.GroupId, member.Id, actorUserId, AuditAction.LeaderRoleRevokedByStatus, nameof(GroupMember), member.Id,
                    new { Role = oldRole.ToString() }, new { Role = GroupRole.Member.ToString(), Status = r.ToStatus.ToString() }, null,
                    "Nafasi ya uongozi imeondolewa kwa sababu uanachama si hai tena", ct);
                await _notify.NotifyAllMembersAsync(r.GroupId, "RoleVacant", Loc.L($"Nafasi ya uongozi iko wazi"),
                    Loc.L($"{name} si kiongozi tena (hali: {StatusName(r.ToStatus)}). Teueni kiongozi mpya kwa pendekezo na kura (Wanachama → Nafasi)."), "/wanachama", ct);
            }
            // Maombi ya cheo yanayomhusu yanafungwa (hayawezi kuidhinishwa kwa asiye hai)
            foreach (var rc in await _context.RoleChangeRequests.Where(x => x.GroupMemberId == member.Id && x.Status == ProposalStatus.Pending).ToListAsync(ct))
            { rc.Status = ProposalStatus.Rejected; rc.DecidedAt = DateTime.UtcNow; rc.DecisionNote = "Mhusika si mwanachama hai tena"; }
        }
    }

    /// <summary>
    /// SASISHO17 (K): maombi yaliyoidhinishwa yenye tarehe ya baadaye yanatekelezwa siku yake ikifika.
    /// Inaitwa na StatusSchedulerBackgroundService (wakati wa kuanza + kila Jobs:StatusSchedulerMinutes).
    /// Kila kikundi kinashughulikiwa chini ya kufuli ya maamuzi (DbTx.Decision) — haiwezi kugongana na kura.
    /// </summary>
    public async Task<int> ApplyDueAsync(CancellationToken ct = default)
    {
        var todayUtc = DateTime.SpecifyKind(TruthLedgerService.NowEat().Date, DateTimeKind.Utc);
        var groups = await _context.MemberStatusChangeRequests.AsNoTracking()
            .Where(r => r.Status == ProposalStatus.Approved && r.AppliedAt == null && r.EffectiveDate <= todayUtc)
            .Select(r => r.GroupId).Distinct().ToListAsync(ct);
        var total = 0;
        foreach (var gid in groups)
        {
            total += await Common.DbTx.SerializedAsync(_context, gid, Common.DbTx.Decision, async () =>
            {
                var due = await _context.MemberStatusChangeRequests
                    .Where(r => r.GroupId == gid && r.Status == ProposalStatus.Approved && r.AppliedAt == null && r.EffectiveDate <= todayUtc)
                    .OrderBy(r => r.EffectiveDate).ThenBy(r => r.DecidedAt).Take(500).ToListAsync(ct);
                foreach (var r in due)
                {
                    var member = await _context.GroupMembers.Include(m => m.User).FirstAsync(m => m.Id == r.GroupMemberId, ct);
                    await ApplyAsync(r, member, r.RequestedByUserId, null, null, ct);
                }
                if (due.Count > 0) await _context.SaveChangesAsync(ct);
                return due.Count;
            }, ct);
        }
        return total;
    }

    public async Task CancelAsync(Guid groupId, Guid requestId, Guid userId, CancellationToken ct = default)
    {
        var r = await _context.MemberStatusChangeRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Ombi halipo.");
        if (r.Status != ProposalStatus.Pending || r.RequestedByUserId != userId) throw new ForbiddenException("Unaweza kughairi ombi lako linalosubiri tu.");
        r.Status = ProposalStatus.Rejected; r.DecidedAt = DateTime.UtcNow;
        await AuditTrail.AddAsync(_context, groupId, r.GroupMemberId, userId, AuditAction.GovernanceRejected, nameof(MemberStatusChangeRequest), r.Id, null,
            new { cancelled = true }, null, "Limeghairiwa na aliyeliomba", ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<object>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await Common.MemberAccess.RequireActiveAsync(_context, groupId, userId, ct, tracking: false);
        var reqs = await _context.MemberStatusChangeRequests.AsNoTracking().Where(r => r.GroupId == groupId).OrderByDescending(r => r.RequestedAt).Take(100).ToListAsync(ct);
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        var ids = reqs.Select(r => r.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectStatusChange && ids.Contains(v.SubjectId)).ToListAsync(ct);
        var result = new List<object>();
        foreach (var r in reqs)
        {
            var mine = votes.Where(v => v.SubjectId == r.Id).ToList();
            var canVote = r.Status == ProposalStatus.Pending && mine.All(v => v.GroupMemberId != me.Id)
                          && (await EligibleVotersAsync(r, ct)).Any(v => v.Id == me.Id);
            result.Add(new
            {
                r.Id, memberId = r.GroupMemberId, memberName = members.FirstOrDefault(m => m.Id == r.GroupMemberId)?.User?.FullName,
                fromStatus = r.FromStatus.ToString(), toStatus = r.ToStatus.ToString(), r.IsExpulsion, r.EffectiveDate, r.Reason, r.EvidenceNote,
                r.RequiredApprovals, r.RejectionsToReject, r.LeadersOnly, status = r.Status.ToString(), r.RequestedAt, r.DecidedAt,
                requestedBy = members.FirstOrDefault(m => m.UserId == r.RequestedByUserId)?.User?.FullName,
                approvals = mine.Count(v => v.Approve), rejections = mine.Count(v => !v.Approve),
                votes = mine.Select(v => new { name = members.FirstOrDefault(m => m.Id == v.GroupMemberId)?.User?.FullName, v.Approve, v.Reason, v.At }),
                canVote, canCancel = r.Status == ProposalStatus.Pending && r.RequestedByUserId == userId
            });
        }
        return result;
    }

    public static string StatusName(MemberStatus s) => s switch
    {
        MemberStatus.Active => "Hai", MemberStatus.Suspended => "Amesimamishwa", MemberStatus.Inactive => "Hayuko hai",
        MemberStatus.Exited => "Ametoka", MemberStatus.Deceased => "Marehemu", MemberStatus.Archived => "Kumbukumbu", _ => s.ToString()
    };
}
