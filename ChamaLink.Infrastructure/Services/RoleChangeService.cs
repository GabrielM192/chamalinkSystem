using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Localization;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// SASISHO17 (NEW-2): vyeo vya kikundi vinabadilishwa kwa PENDEKEZO + KURA tu.
///
/// Kwa nini: maker-checker yote (malipo ya mkono, namba za simu, kura za mafao, uthibitisho wa malipo, PDF ya M-Koba)
/// inategemea dhana kwamba "viongozi wawili ni watu wawili". Kama mtu mmoja angeweza kumpa cheo akaunti yake ya pili,
/// dhana hiyo inavunjika (ukaguzi wa SASISHO16: Sybil). Kwa hiyo:
///   1. Hakuna mtu anayejipa cheo; hakuna mtu mmoja anayempa mwingine cheo (hata Mwenyekiti).
///   2. Wapiga kura = viongozi wenye nguvu kamili (si aliyependekeza, si mhusika). Kama hawatoshi (kikundi kipya),
///      wanachama hai wenye akaunti — ambao akaunti zao zimeunganishwa kabla ya kipindi cha kusubiri — wanapiga kura.
///   3. Idadi ya kura = TruthGroupSettings.RoleChangeApprovals (sera). Bila sera: 1 (kiwango cha chini cha watu wawili).
///   4. Kiongozi mpya anapata nguvu za kuidhinisha baada ya TruthGroupSettings.LeaderCoolingOffDays (GroupMember.LeaderPowersFrom).
///   5. Kila pendekezo na kila uamuzi unatangazwa kwa WANACHAMA WOTE (uwazi) na unaingia kwenye ukaguzi.
///   6. Kujiuzulu (kushuka kwenda Mwanachama mwenyewe) hakuhitaji kura — kunapunguza ruhusa tu.
/// </summary>
public class RoleChangeService
{
    public const string SubjectRoleChange = "RoleChange";
    public static readonly GroupRole[] AssignableRoles =
        { GroupRole.Chairperson, GroupRole.Treasurer, GroupRole.Secretary, GroupRole.MemberRepresentative, GroupRole.Member };

    private readonly ApplicationDbContext _context;
    private readonly NotificationService _notify;

    public RoleChangeService(ApplicationDbContext context, NotificationService notify)
    {
        _context = context;
        _notify = notify;
    }

    public sealed record ProposeResult(Guid? RequestId, string Status, bool Applied, int RequiredApprovals, int EligibleVoters, string Ujumbe);

    private async Task<(int Required, int CoolingDays)> PolicyAsync(Guid groupId, CancellationToken ct)
    {
        var s = await _context.TruthGroupSettings.AsNoTracking().Where(x => x.GroupId == groupId)
            .Select(x => new { x.RoleChangeApprovals, x.LeaderCoolingOffDays }).FirstOrDefaultAsync(ct);
        return (Math.Max(1, s?.RoleChangeApprovals ?? 1), Math.Max(0, s?.LeaderCoolingOffDays ?? 0));
    }

    /// <summary>Wapiga kura: viongozi wenye nguvu kamili; wasipotosha, wanachama hai wenye akaunti zilizokomaa.</summary>
    private async Task<List<GroupMember>> EligibleVotersAsync(Guid groupId, Guid targetMemberId, Guid proposerUserId, bool leadersOnly, int coolingDays, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var members = (await _context.GroupMembers.AsNoTracking().Include(m => m.User)
                .Where(m => m.GroupId == groupId && MemberAccess.ActiveStatuses.Contains(m.Status) && m.Id != targetMemberId && m.UserId != proposerUserId)
                .ToListAsync(ct))
            .Where(m => MembershipService.HasLogin(m.User)).ToList();
        if (leadersOnly) return members.Where(m => MemberAccess.IsEffectiveLeader(m, now)).ToList();
        var matureFrom = now.AddDays(-coolingDays);
        return members.Where(m => m.AccountLinkedAt is null || m.AccountLinkedAt <= matureFrom).ToList();
    }

    public async Task<ProposeResult> ProposeAsync(Guid groupId, Guid memberId, GroupRole toRole, string? reason, Guid userId, CancellationToken ct = default)
        => await DbTx.SerializedAsync(_context, groupId, DbTx.RoleChange, () => ProposeCoreAsync(groupId, memberId, toRole, reason, userId, ct), ct);

    private async Task<ProposeResult> ProposeCoreAsync(Guid groupId, Guid memberId, GroupRole toRole, string? reason, Guid userId, CancellationToken ct)
    {
        if (!AssignableRoles.Contains(toRole)) throw new ValidationException("Nafasi hiyo haipo.");
        var proposer = await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);
        var target = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
                     ?? throw new NotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        if (target.Role == toRole) throw new ConflictException("Mwanachama tayari ana nafasi hiyo.");
        var name = target.User?.FullName ?? target.MemberNumber;

        // Kujiuzulu: kupunguza ruhusa zako mwenyewe — hakuna kura (hakuna faida ya kudanganya)
        if (target.Id == proposer.Id)
        {
            if (toRole != GroupRole.Member) throw new ForbiddenException("Huwezi kujipendekeza kwa nafasi. Mwanachama mwingine akupendekeze, kisha kura ziamue.");
            var old = target.Role;
            target.Role = GroupRole.Member; target.LeaderPowersFrom = null;
            await AuditTrail.AddAsync(_context, groupId, target.Id, userId, AuditAction.MemberRoleChanged, nameof(GroupMember), target.Id,
                new { Role = old.ToString() }, new { Role = GroupRole.Member.ToString() }, null, "Amejiuzulu nafasi ya uongozi", ct);
            await _notify.NotifyAllMembersAsync(groupId, "RoleChanged", $"{name} amejiuzulu",
                $"{name} amejiuzulu nafasi ya {MembershipService.RoleSw(old)}. Teueni mbadala kwa pendekezo na kura.", "/wanachama", ct);
            await _context.SaveChangesAsync(ct);
            return new ProposeResult(null, "Applied", true, 0, 0, "Umejiuzulu nafasi ya uongozi. Wanachama wote wamearifiwa.");
        }

        // Nani anaweza kupendekeza: kiongozi mwenye nguvu kamili, au Mwakilishi wa wanachama; kama kikundi hakina kiongozi
        // yeyote mwenye nguvu (mf. wote wamesimamishwa), mwanachama yeyote hai mwenye akaunti.
        var anyLeader = (await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId && MemberAccess.ActiveStatuses.Contains(m.Status)
                            && MemberAccess.LeaderRoles.Contains(m.Role)).ToListAsync(ct))
                        .Any(m => MemberAccess.IsEffectiveLeader(m));
        var proposerRole = MemberAccess.EffectiveRole(proposer);
        if (anyLeader && !MemberAccess.LeaderRoles.Contains(proposerRole) && proposerRole != GroupRole.MemberRepresentative)
            throw new ForbiddenException("Pendekezo la nafasi linaanzishwa na kiongozi au Mwakilishi wa wanachama.");

        if (!MemberAccess.IsActive(target)) throw new ConflictException("Mwanachama asiye hai (amesimamishwa/ametoka) hawezi kupewa nafasi.");
        if (toRole != GroupRole.Member && !MembershipService.HasLogin(target.User))
            throw new ValidationException($"{name} hana akaunti ya kuingia. Nafasi ya uongozi inahitaji akaunti — kwanza ajisajili na kuunganishwa.");
        if (await _context.RoleChangeRequests.AnyAsync(x => x.GroupMemberId == target.Id && x.Status == ProposalStatus.Pending, ct))
            throw new ConflictException("Tayari kuna pendekezo la nafasi ya mwanachama huyu linalosubiri kura.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Eleza sababu ya pendekezo (mf. uamuzi wa mkutano wa tarehe fulani).");

        var (required, cooling) = await PolicyAsync(groupId, ct);
        var leaders = await EligibleVotersAsync(groupId, target.Id, userId, leadersOnly: true, cooling, ct);
        var leadersOnly = leaders.Count >= required;
        var voters = leadersOnly ? leaders : await EligibleVotersAsync(groupId, target.Id, userId, leadersOnly: false, cooling, ct);
        if (voters.Count < required)
            throw new ConflictException($"Hakuna wapiga kura wa kutosha: wanaoruhusiwa {voters.Count}, wanaohitajika {required}. " +
                                        $"Aliyependekeza na mhusika hawapigi kura; wanachama wenye akaunti mpya wanasubiri kipindi cha sera.");

        var r = new RoleChangeRequest
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = target.Id, FromRole = target.Role, ToRole = toRole,
            Reason = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim(), RequiredApprovals = required,
            RejectionsToReject = voters.Count - required + 1, LeadersOnly = leadersOnly, RequestedByUserId = userId
        };
        _context.RoleChangeRequests.Add(r);
        await AuditTrail.AddAsync(_context, groupId, target.Id, userId, AuditAction.RoleChangeRequested, nameof(RoleChangeRequest), r.Id, null,
            new { From = r.FromRole.ToString(), To = r.ToRole.ToString(), r.RequiredApprovals, r.LeadersOnly, voters = voters.Count }, null, r.Reason, ct);
        await _notify.NotifyAsync(groupId, voters.Select(v => v.Id), "VoteNeeded", $"Kura: nafasi ya {name}",
            $"Pendekezo: {name} kuwa {MembershipService.RoleSw(toRole)}. Sababu: {r.Reason}. Kura {required} za NDIYO zinahitajika.", "/wanachama", ct);
        await _notify.NotifyAllMembersAsync(groupId, "RoleProposed", $"Pendekezo la nafasi: {name}",
            $"{name} amependekezwa kuwa {MembershipService.RoleSw(toRole)}. Kama hukubaliani, wajulishe viongozi au Mwakilishi wa wanachama.",
            "/wanachama", ct, voters.Select(v => v.Id).ToArray());
        await _context.SaveChangesAsync(ct);
        return new ProposeResult(r.Id, "Pending", false, required, voters.Count,
            $"Pendekezo limehifadhiwa. Linahitaji kura {required} za NDIYO (wanaoruhusiwa {voters.Count}).");
    }

    public Task<RoleChangeRequest> VoteAsync(Guid groupId, Guid requestId, bool approve, string? reason, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.RoleChange, () => VoteCoreAsync(groupId, requestId, approve, reason, userId, ct), ct);

    private async Task<RoleChangeRequest> VoteCoreAsync(Guid groupId, Guid requestId, bool approve, string? reason, Guid userId, CancellationToken ct)
    {
        var voter = await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);
        var r = await _context.RoleChangeRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Pendekezo halipo.");
        if (r.Status != ProposalStatus.Pending) throw new ConflictException($"Pendekezo tayari ni {r.Status}.");
        var (_, cooling) = await PolicyAsync(groupId, ct);
        var voters = await EligibleVotersAsync(groupId, r.GroupMemberId, r.RequestedByUserId, r.LeadersOnly, cooling, ct);
        if (voters.All(v => v.Id != voter.Id))
            throw new ForbiddenException("Huruhusiwi kupiga kura kwenye pendekezo hili (aliyependekeza, mhusika, kiongozi aliye kwenye kipindi cha kusubiri, au akaunti mpya).");
        if (await _context.GovernanceVotes.AnyAsync(v => v.SubjectType == SubjectRoleChange && v.SubjectId == r.Id && v.GroupMemberId == voter.Id, ct))
            throw new ConflictException("Tayari umepiga kura.");
        if (!approve && string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Kura ya HAPANA inahitaji sababu.");

        var vote = new GovernanceVote
        {
            Id = Guid.NewGuid(), GroupId = groupId, SubjectType = SubjectRoleChange, SubjectId = r.Id, GroupMemberId = voter.Id, UserId = userId,
            Approve = approve, Reason = reason?.Trim(), RoleAtVote = MemberAccess.EffectiveRole(voter)
        };
        _context.GovernanceVotes.Add(vote);
        await AuditTrail.AddAsync(_context, groupId, r.GroupMemberId, userId, AuditAction.VoteCast, nameof(GovernanceVote), vote.Id, null,
            new { vote.Approve, vote.Reason, Subject = SubjectRoleChange }, null, reason, ct);

        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectRoleChange && v.SubjectId == r.Id).ToListAsync(ct);
        votes.Add(vote);
        var yes = votes.Count(v => v.Approve);
        var no = votes.Count(v => !v.Approve);
        var target = await _context.GroupMembers.Include(m => m.User).FirstAsync(m => m.Id == r.GroupMemberId, ct);
        var name = target.User?.FullName ?? target.MemberNumber;

        if (yes >= r.RequiredApprovals)
        {
            if (!MemberAccess.IsActive(target) || target.Role != r.FromRole)
                throw new ConflictException("Hali au nafasi ya mhusika imebadilika tangu pendekezo. Pendekezo jipya linahitajika.");
            r.Status = ProposalStatus.Approved; r.DecidedAt = DateTime.UtcNow; r.CoolingOffDaysApplied = cooling;
            if (r.ToRole == GroupRole.Chairperson)
            {
                // Mwenyekiti ni mmoja: aliyepo anakuwa Mwanachama (kukabidhi kwa kura)
                foreach (var c in await _context.GroupMembers.Where(m => m.GroupId == groupId && m.Role == GroupRole.Chairperson && m.Id != target.Id).ToListAsync(ct))
                {
                    c.Role = GroupRole.Member; c.LeaderPowersFrom = null;
                    await AuditTrail.AddAsync(_context, groupId, c.Id, userId, AuditAction.MemberRoleChanged, nameof(GroupMember), c.Id,
                        new { Role = GroupRole.Chairperson.ToString() }, new { Role = GroupRole.Member.ToString() }, null, "Uenyekiti umekabidhiwa kwa kura", ct);
                }
            }
            target.Role = r.ToRole;
            target.LeaderPowersFrom = MemberAccess.LeaderRoles.Contains(r.ToRole) && cooling > 0 ? DateTime.UtcNow.AddDays(cooling) : null;
            await AuditTrail.AddAsync(_context, groupId, target.Id, userId, AuditAction.RoleChangeApproved, nameof(RoleChangeRequest), r.Id,
                new { Role = r.FromRole.ToString() }, new { Role = r.ToRole.ToString(), yes, no, target.LeaderPowersFrom }, null, r.Reason, ct);
            await _notify.NotifyAllMembersAsync(groupId, "RoleChanged", $"{name}: {MembershipService.RoleSw(r.ToRole)}",
                target.LeaderPowersFrom is { } from
                    ? $"Kwa kura {yes}, {name} sasa ni {MembershipService.RoleSw(r.ToRole)}. Nguvu za kuidhinisha zinaanza {from:dd/MM/yyyy}."
                    : $"Kwa kura {yes}, {name} sasa ni {MembershipService.RoleSw(r.ToRole)}.", "/wanachama", ct);
        }
        else if (no >= r.RejectionsToReject)
        {
            r.Status = ProposalStatus.Rejected; r.DecidedAt = DateTime.UtcNow; r.DecisionNote = "Kura za HAPANA zimefanya idhini isiwezekane";
            await AuditTrail.AddAsync(_context, groupId, target.Id, userId, AuditAction.RoleChangeRejected, nameof(RoleChangeRequest), r.Id, null,
                new { yes, no }, null, r.DecisionNote, ct);
            await _notify.NotifyAllMembersAsync(groupId, "RoleRejected", $"Pendekezo la nafasi limekataliwa",
                $"Pendekezo la {name} kuwa {MembershipService.RoleSw(r.ToRole)} limekataliwa kwa kura.", "/wanachama", ct);
        }
        await _context.SaveChangesAsync(ct);
        return r;
    }

    public async Task CancelAsync(Guid groupId, Guid requestId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);
        var r = await _context.RoleChangeRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Pendekezo halipo.");
        if (r.Status != ProposalStatus.Pending || r.RequestedByUserId != userId) throw new ForbiddenException("Unaweza kughairi pendekezo lako linalosubiri tu.");
        r.Status = ProposalStatus.Rejected; r.DecidedAt = DateTime.UtcNow; r.DecisionNote = "Limeghairiwa na aliyelipendekeza";
        await AuditTrail.AddAsync(_context, groupId, r.GroupMemberId, userId, AuditAction.RoleChangeCancelled, nameof(RoleChangeRequest), r.Id, null,
            new { cancelled = true }, null, r.DecisionNote, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<object>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct, tracking: false);
        var reqs = await _context.RoleChangeRequests.AsNoTracking().Where(r => r.GroupId == groupId).OrderByDescending(r => r.RequestedAt).Take(100).ToListAsync(ct);
        var ids = reqs.Select(r => r.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectRoleChange && ids.Contains(v.SubjectId)).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.UserId, Name = m.User != null ? m.User.FullName : m.MemberNumber }).ToListAsync(ct);
        var (_, cooling) = await PolicyAsync(groupId, ct);
        var result = new List<object>();
        foreach (var r in reqs)
        {
            var mine = votes.Where(v => v.SubjectId == r.Id).ToList();
            var canVote = false;
            if (r.Status == ProposalStatus.Pending && mine.All(v => v.GroupMemberId != me.Id))
                canVote = (await EligibleVotersAsync(groupId, r.GroupMemberId, r.RequestedByUserId, r.LeadersOnly, cooling, ct)).Any(v => v.Id == me.Id);
            result.Add(new
            {
                r.Id, memberId = r.GroupMemberId, memberName = names.FirstOrDefault(n => n.Id == r.GroupMemberId)?.Name,
                fromRole = r.FromRole.ToString(), toRole = r.ToRole.ToString(), r.Reason, status = r.Status.ToString(),
                r.RequiredApprovals, r.LeadersOnly, r.RequestedAt, r.DecidedAt, r.DecisionNote,
                requestedBy = names.FirstOrDefault(n => n.UserId == r.RequestedByUserId)?.Name,
                yes = mine.Count(v => v.Approve), no = mine.Count(v => !v.Approve),
                canVote, canCancel = r.Status == ProposalStatus.Pending && r.RequestedByUserId == userId
            });
        }
        return result;
    }
}
