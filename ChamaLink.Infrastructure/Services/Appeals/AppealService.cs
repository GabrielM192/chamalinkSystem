using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Appeals;

/// <summary>
/// SASISHO21 — RUfAA.
///
/// Mwanachama anapinga uamuzi unaomgusa. Rufaa inaenda kwa VIONGOZI WAWILI
/// (idadi inatoka kwenye sera, kamwe chini ya 2) na mwombaji hapigani kura yake.
///
/// Rufaa haizidishi wala haipunguzi pesa yoyote. Inabadilisha UAMUZI tu:
///   • StatusChange → ombi la kubadilisha hali linafutwa (kama linasubiri)
///                    au hali ya mwanachama inarudishwa kama ilivyokuwa;
///   • Claim        → dai lililokataliwa linafunguliwa tena kwa hazina;
///   • Fine / Other → uamuzi unaandikwa; hazina inachukua hatua kwa zana zake
///                    (hakuna faini inayofutwa kimya kimya).
/// </summary>
public class AppealService
{
    private readonly ApplicationDbContext _context;

    public AppealService(ApplicationDbContext context) => _context = context;

    /// <summary>Hali ambazo mtu bado anaweza kuwasilisha rufaa (pamoja na aliyekwisha toka —
    /// ndiyo hasa anayepinga kufukuzwa).</summary>
    private static readonly MemberStatus[] CanAppeal =
    {
        MemberStatus.New, MemberStatus.Active, MemberStatus.Warning, MemberStatus.EligibleForExpulsion,
        MemberStatus.Inactive, MemberStatus.Suspended, MemberStatus.Exited
    };

    public sealed record AppealRow(
        Guid Id, Guid GroupMemberId, string MemberNumber, string MemberName, string Kind, string Status,
        string SubjectSummary, string Reason, int RequiredApprovals, int Approvals, int Rejections,
        DateTime FiledAt, DateTime? DecidedAt, string? DecisionNote, string? EffectSummary,
        bool Mine, bool CanVote, bool CanWithdraw, string? MyVote);

    public sealed record FileResult(Guid AppealId, string Status, int RequiredApprovals, string Ujumbe);

    // ==================================================================
    // 1. KUWEKA RUfAA
    // ==================================================================

    public async Task<FileResult> FileAsync(Guid groupId, Guid userId, AppealKind kind, Guid? subjectId,
        string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new ValidationException("Eleza sababu ya rufaa kwa ufasaha (angalau herufi 10).");

        var me = await _context.GroupMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct)
            ?? throw new ForbiddenException("Wewe si mwanachama wa kikundi hiki.");

        if (!CanAppeal.Contains(me.Status))
            throw new ForbiddenException("Hali yako ya uanachama hairuhusu kuwasilisha rufaa.");

        var s = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct)
            ?? throw new PolicyNotConfiguredException(groupId, "Kikundi hakijaweka kanuni — rufaa haiwezi kupimwa bila sera.");

        if (!s.AppealsEnabled)
            throw new ForbiddenException("Rufaa zimezimwa kwa kikundi hiki.");

        var required = Math.Max(2, s.AppealApprovalsRequired);

        (Guid? subject, string summary, DateTime occurred) = await ResolveSubjectAsync(groupId, me.Id, kind, subjectId, ct);

        // Muda wa kuwasilisha rufaa (siku) — 0 = hakuna kikomo.
        if (s.AppealWindowDays > 0 && occurred.AddDays(s.AppealWindowDays) < DateTime.UtcNow)
            throw new ConflictException(
                $"Muda wa kuwasilisha rufaa umepita ({s.AppealWindowDays} siku tangu uamuzi wa {occurred:dd/MM/yyyy}).");

        if (subject is not null && await _context.MemberAppeals.AnyAsync(
                a => a.GroupId == groupId && a.GroupMemberId == me.Id && a.SubjectId == subject && a.Status == AppealStatus.Pending, ct))
            throw new ConflictException("Tayari una rufaa inayosubiri kuhusu jambo hili.");

        var appeal = new MemberAppeal
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = me.Id, Kind = kind,
            SubjectId = subject, SubjectSummary = Trim(summary, 300),
            Reason = reason.Trim(), Status = AppealStatus.Pending,
            RequiredApprovals = required, DecisionWindowDays = s.AppealWindowDays,
            AppellantUserId = userId, FiledAt = DateTime.UtcNow
        };
        _context.MemberAppeals.Add(appeal);

        await AuditTrail.AddAsync(_context, groupId, me.Id, userId, AuditAction.AppealFiled,
            nameof(MemberAppeal), appeal.Id, null,
            new { kind = kind.ToString(), appeal.SubjectId, required, summary = appeal.SubjectSummary },
            appeal.SubjectId?.ToString(), appeal.Reason, ct);
        await _context.SaveChangesAsync(ct);

        var leaders = await LeaderMemberIdsAsync(groupId, me.Id, ct);
        if (leaders.Count > 0)
        {
            var notify = new Benefits.NotificationService(_context);
            await notify.NotifyAsync(groupId, leaders, "VoteNeeded", "Rufaa mpya",
                $"Rufaa ya {me.User?.FullName ?? me.MemberNumber}: {appeal.SubjectSummary}. Viongozi {required} wanahitajika kuamua.",
                "/rufaa", ct);
            await _context.SaveChangesAsync(ct);
        }

        return new FileResult(appeal.Id, appeal.Status.ToString(), required,
            $"Rufaa imewasilishwa. Viongozi {required} wanahitajika kuamua — mmoja hawezi. Hii haibadilishi deni lolote bado.");
    }

    /// <summary>Hakikisha kile kinachopingwa ni cha mwombaji mwenyewe, na kirekodi kifupi chake.</summary>
    private async Task<(Guid? Subject, string Summary, DateTime Occurred)> ResolveSubjectAsync(
        Guid groupId, Guid memberId, AppealKind kind, Guid? subjectId, CancellationToken ct)
    {
        if (kind == AppealKind.Other)
        {
            var last = await _context.MemberAppeals.AsNoTracking()
                .Where(a => a.GroupId == groupId && a.GroupMemberId == memberId)
                .OrderByDescending(a => a.FiledAt).Select(a => a.FiledAt).FirstOrDefaultAsync(ct);
            return (null, "Jambo lingine (lilielezwa kwenye sababu)", last == default ? DateTime.UtcNow : last);
        }

        if (subjectId is null || subjectId == Guid.Empty)
            throw new ValidationException("Chagua uamuzi unaopinga.");

        switch (kind)
        {
            case AppealKind.StatusChange:
            {
                var r = await _context.MemberStatusChangeRequests.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == subjectId && x.GroupId == groupId, ct)
                    ?? throw new NotFoundException("Ombi la kubadilisha hali halipo.");
                if (r.GroupMemberId != memberId)
                    throw new ForbiddenException("Unaweza kupinga uamuzi unaokuhusu wewe pekee.");
                return (r.Id, $"Badiliko la hali: {r.FromStatus} → {r.ToStatus}" +
                              (r.IsExpulsion ? " (kufukuzwa)" : ""), r.RequestedAt);
            }
            case AppealKind.Claim:
            {
                var c = await _context.PaymentClaims.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == subjectId && x.GroupId == groupId, ct)
                    ?? throw new NotFoundException("Dai halipo.");
                if (c.GroupMemberId != memberId)
                    throw new ForbiddenException("Unaweza kupinga dai lako mwenyewe pekee.");
                if (c.Status != PaymentClaimStatus.Rejected)
                    throw new ConflictException("Dai hili halijakataliwa — hakuna la kupinga.");
                return (c.Id, $"Dai la {c.Amount:N0} ({c.Reference}) lilikataliwa", c.DecidedAt ?? c.CreatedAt);
            }
            case AppealKind.Fine:
            {
                var o = await _context.EventObligations.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == subjectId && x.GroupId == groupId, ct)
                    ?? throw new NotFoundException("Wajibu huo haupo.");
                if (o.GroupMemberId != memberId)
                    throw new ForbiddenException("Unaweza kupinga wajibu wako mwenyewe pekee.");
                return (o.Id, $"Wajibu wa tukio: {o.RequiredAmount:N0} (deni lililobaki {o.OutstandingAmount:N0})", o.CreatedAt);
            }
            default:
                throw new ValidationException("Aina ya rufaa haijulikani.");
        }
    }

    // ==================================================================
    // 2. ORODHA
    // ==================================================================

    public async Task<List<AppealRow>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        // Hapa tunaruhusu hali YOYOTE (pamoja na Exited): aliyefukuzwa ndiye anayepinga zaidi.
        var me = await MemberAccess.FindAsync(_context, groupId, userId, ct, tracking: false)
            ?? throw new ForbiddenException("Haujawahi kuwa mwanachama wa kikundi hiki.");
        var leader = MemberAccess.IsEffectiveLeader(me);

        var q = _context.MemberAppeals.AsNoTracking().Where(a => a.GroupId == groupId);
        if (!leader) q = q.Where(a => a.GroupMemberId == me.Id);
        var appeals = await q.OrderByDescending(a => a.FiledAt).Take(200).ToListAsync(ct);

        var ids = appeals.Select(a => a.Id).ToList();
        var votes = await _context.MemberAppealVotes.AsNoTracking()
            .Where(v => ids.Contains(v.AppealId)).ToListAsync(ct);

        var memberIds = appeals.Select(a => a.GroupMemberId).Distinct().ToList();
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User)
            .Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber, Name = m.User != null ? m.User.FullName : m.MemberNumber })
            .ToDictionaryAsync(x => x.Id, x => x, ct);

        return appeals.Select(a =>
        {
            var av = votes.Where(v => v.AppealId == a.Id).ToList();
            var approvals = av.Count(v => v.Approve);
            var rejections = av.Count(v => !v.Approve);
            var mine = a.GroupMemberId == me.Id;
            var myVote = av.FirstOrDefault(v => v.UserId == userId);
            members.TryGetValue(a.GroupMemberId, out var m);
            return new AppealRow(
                a.Id, a.GroupMemberId, m?.MemberNumber ?? "", m?.Name ?? "",
                a.Kind.ToString(), a.Status.ToString(), a.SubjectSummary, a.Reason,
                a.RequiredApprovals, approvals, rejections,
                a.FiledAt, a.DecidedAt, a.DecisionNote, a.EffectSummary,
                mine,
                // Kiongozi hawezi kupiga kura kwenye rufaa yake, na hapigani mara mbili.
                leader && !mine && a.Status == AppealStatus.Pending && myVote is null,
                mine && a.Status == AppealStatus.Pending,
                myVote is null ? null : myVote.Approve ? "Approve" : "Reject");
        }).ToList();
    }

    // ==================================================================
    // 3. KUPIGA KURA (viongozi)
    // ==================================================================

    /// <summary>
    /// Kila kiongozi anapiga kura MOJA. Kura zinahesabiwa upya kila mara:
    ///   • NDIYO ikifika RequiredApprovals → rufaa inakubaliwa, uamuzi unabadilishwa;
    ///   • HAPANA ikifika RequiredApprovals → rufaa inakataliwa, uamuzi unasimama.
    /// Aliyeomba hapigani kura yake mwenyewe; mmoja hawezi kuamua.
    /// </summary>
    public async Task<AppealRow?> VoteAsync(Guid groupId, Guid appealId, bool approve, string? note,
        Guid userId, CancellationToken ct = default)
    {
        var leader = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct,
            "Kura za rufaa ni za viongozi.");

        var appeal = await _context.MemberAppeals.FirstOrDefaultAsync(a => a.Id == appealId && a.GroupId == groupId, ct)
            ?? throw new NotFoundException("Rufaa haipo.");
        if (appeal.GroupMemberId == leader.Id)
            throw new ForbiddenException("Huwezi kupiga kura kwenye rufaa yako mwenyewe.");
        if (appeal.Status != AppealStatus.Pending)
            throw new ConflictException("Rufaa hii tayari imeamuliwa.");

        if (await _context.MemberAppealVotes.AnyAsync(v => v.AppealId == appealId && v.UserId == userId, ct))
            throw new ConflictException("Tayari umepiga kura kwenye rufaa hii.");

        _context.MemberAppealVotes.Add(new MemberAppealVote
        {
            Id = Guid.NewGuid(), GroupId = groupId, AppealId = appealId, UserId = userId,
            Approve = approve, Note = Trim(note, 500)
        });
        await AuditTrail.AddAsync(_context, groupId, appeal.GroupMemberId, userId, AuditAction.AppealVoted,
            nameof(MemberAppeal), appeal.Id, null, new { approve, note = Trim(note, 200) }, null, null, ct);
        await _context.SaveChangesAsync(ct);

        var votes = await _context.MemberAppealVotes.AsNoTracking().Where(v => v.AppealId == appealId).ToListAsync(ct);
        var approvals = votes.Count(v => v.Approve);
        var rejections = votes.Count(v => !v.Approve);

        if (approvals >= appeal.RequiredApprovals)
            await DecideAsync(appeal, true, note, userId, votes.Count, ct);
        else if (rejections >= appeal.RequiredApprovals)
            await DecideAsync(appeal, false, note, userId, votes.Count, ct);

        return (await ListAsync(groupId, userId, ct)).FirstOrDefault(r => r.Id == appealId);
    }

    private async Task DecideAsync(MemberAppeal appeal, bool grant, string? note, Guid userId, int voteCount, CancellationToken ct)
    {
        appeal.Status = grant ? AppealStatus.Granted : AppealStatus.Dismissed;
        appeal.DecidedAt = DateTime.UtcNow;
        appeal.DecidedByUserId = userId;
        appeal.DecisionNote = Trim(note, 1000);

        string effect;
        if (!grant)
        {
            effect = "Uamuzi uliopingwa unasimama. Hakuna kilichobadilika.";
        }
        else
        {
            effect = appeal.Kind switch
            {
                AppealKind.StatusChange => await ReverseStatusChangeAsync(appeal, ct),
                AppealKind.Claim => await ReopenClaimAsync(appeal, ct),
                AppealKind.Fine => "Uamuzi umeandikwa. Faini haifutwi hapa — hazina inatoa msamaha kwa rekodi yake.",
                _ => "Rufaa imekubaliwa. Viongozi wanachukua hatua inayofaa kwa rekodi."
            };
        }

        appeal.EffectSummary = Trim(effect, 500);

        await AuditTrail.AddAsync(_context, appeal.GroupId, appeal.GroupMemberId, userId, AuditAction.AppealDecided,
            nameof(MemberAppeal), appeal.Id,
            new { status = AppealStatus.Pending.ToString() },
            new { status = appeal.Status.ToString(), grant, votes = voteCount, effect },
            appeal.SubjectId?.ToString(), appeal.DecisionNote, ct);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Rufaa ya kubadilisha hali: ombi linasubiri → linafutwa; hali ishatumika → inarudishwa.</summary>
    private async Task<string> ReverseStatusChangeAsync(MemberAppeal a, CancellationToken ct)
    {
        if (a.SubjectId is not { } id) return "Hakuna ombi la hali lililounganishwa.";
        var r = await _context.MemberStatusChangeRequests.FirstOrDefaultAsync(x => x.Id == id && x.GroupId == a.GroupId, ct);
        if (r is null) return "Ombi la hali halipo tena — hakuna kilichobadilika.";

        if (r.Status == ProposalStatus.Pending)
        {
            r.Status = ProposalStatus.Rejected;
            r.DecidedAt = DateTime.UtcNow;
            return $"Ombi la {r.FromStatus} → {r.ToStatus} limefutwa kabla ya kutumika. Hali ya mwanachama haijabadilika.";
        }

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == r.GroupMemberId && m.GroupId == a.GroupId, ct);
        if (member is null) return "Mwanachama hayupo.";
        if (member.Status != r.ToStatus) return $"Hali ya mwanachama ni {member.Status} — haiendi sambamba na uamuzi wa {r.ToStatus}; hakuna kilichobadilishwa.";

        var before = member.Status;
        member.Status = r.FromStatus;
        await AuditTrail.AddAsync(_context, a.GroupId, member.Id, a.AppellantUserId, AuditAction.StatusChangeApplied,
            nameof(GroupMember), member.Id, new { Status = before.ToString() }, new { Status = member.Status.ToString(), via = "Appeal" },
            a.Id.ToString(), Prefix("Kurudishwa kwa rufaa iliyokubaliwa. ", a.DecisionNote), ct);
        return $"Hali ya mwanachama imerudishwa kutoka {before} kwenda {member.Status}.";
    }

    /// <summary>Rufaa ya dai: dai lililokataliwa linafunguliwa tena — pesa haibadiliki bado.</summary>
    private async Task<string> ReopenClaimAsync(MemberAppeal a, CancellationToken ct)
    {
        if (a.SubjectId is not { } id) return "Hakuna dai lililounganishwa.";
        var c = await _context.PaymentClaims.FirstOrDefaultAsync(x => x.Id == id && x.GroupId == a.GroupId, ct);
        if (c is null) return "Dai halipo tena.";
        if (c.Status != PaymentClaimStatus.Rejected) return $"Dai liko {c.Status} — hakuna la kufungua.";

        c.Status = PaymentClaimStatus.Pending;
        c.DecidedAt = null;
        c.DecidedByUserId = null;
        c.DecisionNote = Prefix("Imefunguliwa upya kwa rufaa. ", a.DecisionNote);
        return "Dai limefunguliwa upya — hazina italilinganisha na taarifa ya M-Koba. Pesa haijaongezwa wala haijapunguzwa.";
    }

    // ==================================================================
    // 4. KUFUTA (mwombaji)
    // ==================================================================

    public async Task WithdrawAsync(Guid groupId, Guid appealId, Guid userId, CancellationToken ct = default)
    {
        var a = await _context.MemberAppeals.FirstOrDefaultAsync(x => x.Id == appealId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Rufaa haipo.");
        if (a.AppellantUserId != userId) throw new ForbiddenException("Unaweza kufuta rufaa yako pekee.");
        if (a.Status != AppealStatus.Pending) throw new ConflictException("Rufaa hii tayari imeamuliwa.");

        a.Status = AppealStatus.Withdrawn;
        a.DecidedAt = DateTime.UtcNow;
        a.DecisionNote = "Imefutwa na mwombaji.";
        await AuditTrail.AddAsync(_context, groupId, a.GroupMemberId, userId, AuditAction.AppealWithdrawn,
            nameof(MemberAppeal), a.Id, null, new { status = "Withdrawn" }, null, null, ct);
        await _context.SaveChangesAsync(ct);
    }

    // ==================================================================
    private async Task<List<Guid>> LeaderMemberIdsAsync(Guid groupId, Guid excludeMemberId, CancellationToken ct)
        => await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Id != excludeMemberId
                        && (m.Role == GroupRole.Chairperson || m.Role == GroupRole.Secretary || m.Role == GroupRole.Treasurer)
                        && m.Status == MemberStatus.Active)
            .Select(m => m.Id).ToListAsync(ct);

    private static string Prefix(string p, string? tail) => tail is null ? p.Trim() : (p + tail).Trim();

    private static string Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
