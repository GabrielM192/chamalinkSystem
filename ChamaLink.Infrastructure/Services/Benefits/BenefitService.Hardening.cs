using System.Globalization;
using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Localization;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

using ChamaLink.Infrastructure.Services.Common;

namespace ChamaLink.Infrastructure.Services.Benefits;

// ============================================================================
//  SASISHO13 — ULINZI WA MAFAO (Benefits Security & Governance Hardening)
//
//   B2  Ustahiki: Kianzio kimelipwa, muda wa kusubiri (cooldown), kiasi cha juu kwa kipindi,
//       mhusika yule yule kudaiwa na wanachama wawili (tahadhari).
//   B2  Mgongano wa maslahi: ndugu waliosajiliwa (MemberRelation) hawapigi kura / hawahakiki / hawalipi.
//   B5  Pesa ipo? Salio la mwisho la M-Koba (ukweli) − kesi zilizoidhinishwa hazijalipwa. Kuanzisha
//       malipo (Mhazini) ≠ kuthibitisha malipo (mtu mwingine, rejea ya M-Koba).
//   B6  Kila kesi iliyolipwa inaandika maandishi ya fedha: Approved = JoinFeeOffset + ArrearsOffset + CashPaid,
//       na yanalinganishwa na M-Koba na Injini ya Ukweli.
//   B7  Majaribio yaliyozuiwa yanaandikwa (RiskBlocked) + dashibodi ya hatari + mnyororo wa hash.
//   B8  Daftari la uwazi kwa KILA mwanachama (bila simu, bila vyeti).
// ============================================================================
public partial class BenefitService
{
    private const int DaysPerYear = 365;

    public static string RoleName(GroupRole r) => r switch
    {
        GroupRole.Chairperson => "Mwenyekiti", GroupRole.Treasurer => "Mtunza-hazina", GroupRole.Secretary => "Katibu", _ => "Mwanachama"
    };

    public static string RelationName(MemberRelationType t) => t switch
    {
        MemberRelationType.Spouse => "mwenza", MemberRelationType.Parent => "mzazi", MemberRelationType.Child => "mtoto",
        MemberRelationType.Sibling => "ndugu (kaka/dada)", MemberRelationType.Household => "kaya moja", MemberRelationType.BusinessPartner => "mbia wa biashara",
        _ => "uhusiano mwingine"
    };

    /// <summary>Tarakimu 9 za mwisho za namba (255712... = 0712... = 712...).</summary>
    private static string PhoneKey(string? phone)
    {
        var d = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length > 9 ? d[^9..] : d;
    }

    /// <summary>SASISHO17 (P0-5): namba zilizokuwa za mnufaika SIKU YA MUAMALA (atEat) tu — namba iliyofungwa haikubaliwi.</summary>
    private async Task<HashSet<string>> BeneficiaryPhonesAsync(Guid groupId, Guid memberId, DateTime atEat, CancellationToken ct)
        => (await MemberPhonesAt.ListAsync(_context, groupId, memberId, atEat, ct)).Select(PhoneKey).ToHashSet();

    /// <summary>Andika jaribio lililozuiwa kwenye ukaguzi (linahifadhiwa hata kama kitendo kinakataliwa), kisha kataa.</summary>
    private async Task RiskBlockedAsync(Guid groupId, BenefitCase? c, Guid userId, string code, Loc message, CancellationToken ct, bool forbidden = false)
    {
        await Audit(groupId, c?.BeneficiaryGroupMemberId, userId, AuditAction.RiskBlocked, nameof(BenefitCase), c?.Id ?? Guid.Empty, null,
            new { Code = code, Case = c?.CaseNumber, Tpl = message.Template, TArgs = message.Args }, c?.CaseNumber, message.Text, ct);
        await _context.SaveChangesAsync(ct);
        if (forbidden || code is "CONFIRMER_IS_INITIATOR") throw new ForbiddenException(message);
        throw new ConflictException(message);
    }

    // =====================================================================
    // MGONGANO WA MASLAHI (ndugu)
    // =====================================================================
    private async Task<HashSet<Guid>> RelatedMemberIdsAsync(Guid groupId, IReadOnlyCollection<Guid> memberIds, IReadOnlyCollection<MemberRelationType>? types, CancellationToken ct)
    {
        if (memberIds.Count == 0) return new HashSet<Guid>();
        var rows = await _context.MemberRelations.AsNoTracking()
            .Where(r => r.GroupId == groupId && (r.Status == MemberRelationStatus.Active || r.Status == MemberRelationStatus.EndPending) && (memberIds.Contains(r.MemberAId) || memberIds.Contains(r.MemberBId)))
            .Select(r => new { r.MemberAId, r.MemberBId, r.Type }).ToListAsync(ct);
        return rows.Where(r => types is null || types.Count == 0 || types.Contains(r.Type))
            .SelectMany(r => new[] { memberIds.Contains(r.MemberAId) ? r.MemberBId : Guid.Empty, memberIds.Contains(r.MemberBId) ? r.MemberAId : Guid.Empty })
            .Where(id => id != Guid.Empty && !memberIds.Contains(id)).ToHashSet();
    }

    private async Task<BenefitGovernance?> CaseGovernanceAsync(BenefitCase c, CancellationToken ct)
        => c.GovernanceId is { } gid
            ? await _context.BenefitGovernances.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct)
            : await GovernanceForDateAsync(c.GroupId, c.EventDate, ct);

    /// <summary>
    /// SASISHO14: wanachama wenye mgongano wa maslahi kwenye kesi — ndugu (kwa aina za sera) wa MNUFAIKA na wa ALIYEFUNGUA kesi;
    /// kwa uthibitisho wa malipo pia ndugu wa ALIYEANZISHA malipo.
    /// </summary>
    private async Task<HashSet<Guid>> ConflictedMemberIdsAsync(BenefitCase c, BenefitGovernance? gov, bool includeInitiator, CancellationToken ct)
    {
        if (gov is not { ExcludeRelatedParties: true }) return new HashSet<Guid>();
        var parties = new List<Guid> { c.BeneficiaryGroupMemberId };
        var userIds = new List<Guid> { c.CreatedByUserId };
        if (includeInitiator && c.PaymentInitiatedByUserId is { } iu) userIds.Add(iu);
        parties.AddRange(await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == c.GroupId && userIds.Contains(m.UserId)).Select(m => m.Id).ToListAsync(ct));
        var distinct = parties.Distinct().ToList();
        var related = await RelatedMemberIdsAsync(c.GroupId, distinct, ParseRelationTypes(gov.ConflictRelationTypes), ct);
        return related;
    }

    private async Task<bool> IsConflictedAsync(BenefitCase c, Guid memberId, CancellationToken ct, bool includeInitiator = false)
        => (await ConflictedMemberIdsAsync(c, await CaseGovernanceAsync(c, ct), includeInitiator, ct)).Contains(memberId);

    public sealed class RelationDto
    {
        public Guid MemberAId { get; set; }
        public Guid MemberBId { get; set; }
        public MemberRelationType Type { get; set; }
        public string? Note { get; set; }
    }

    public async Task<List<object>> RelationsAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var viewer = await RequireLeaderOrAuditorAsync(groupId, userId, ct);
        var viewerIsLeader = Common.MemberAccess.IsEffectiveLeader(viewer);
        var rows = await _context.MemberRelations.AsNoTracking().Where(r => r.GroupId == groupId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        string Name(Guid id) => members.FirstOrDefault(m => m.Id == id)?.User?.FullName ?? members.FirstOrDefault(m => m.Id == id)?.MemberNumber ?? "?";
        string UserName(Guid? uid) => uid is null ? "" : members.FirstOrDefault(m => m.UserId == uid)?.User?.FullName ?? "?";
        var me = members.First(m => m.UserId == userId);
        return rows.Select(r => (object)new
        {
            r.Id, r.MemberAId, memberAName = Name(r.MemberAId), r.MemberBId, memberBName = Name(r.MemberBId),
            type = r.Type.ToString(), typeName = RelationName(r.Type), r.Note, status = r.Status.ToString(),
            createdBy = UserName(r.CreatedByUserId), r.CreatedAt, endRequestedBy = UserName(r.EndRequestedByUserId), r.EndRequestedAt, r.EndReason,
            endedBy = UserName(r.EndedByUserId), r.EndedAt,
            canRequestEnd = viewerIsLeader && r.Status == MemberRelationStatus.Active,
            canDecideEnd = viewerIsLeader && r.Status == MemberRelationStatus.EndPending && r.EndRequestedByUserId != userId && me.Id != r.MemberAId && me.Id != r.MemberBId,
            approvedBy = UserName(r.ApprovedByUserId), r.ApprovedAt, r.DecisionNote,
            canDecideAdd = viewerIsLeader && r.Status == MemberRelationStatus.Pending && r.CreatedByUserId != userId && me.Id != r.MemberAId && me.Id != r.MemberBId,
            canCancelAdd = r.Status == MemberRelationStatus.Pending && r.CreatedByUserId == userId
        }).ToList();
    }

    /// <summary>
    /// SASISHO15: kuongeza uhusiano ni OMBI (Pending) mpaka kiongozi wa pili aidhinishe. Uhusiano unaosubiri hauzuii kura,
    /// hivyo kiongozi mmoja hawezi kuwasajili wenzake wote kama "ndugu" na kukwamisha kila kesi (denial-of-governance).
    /// </summary>
    public async Task<MemberRelation> AddRelationAsync(Guid groupId, RelationDto dto, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        if (!Enum.IsDefined(dto.Type)) throw new ValidationException("Aina ya uhusiano si sahihi.");
        if (dto.MemberAId == dto.MemberBId) throw new ValidationException("Chagua wanachama wawili tofauti.");
        var ids = new[] { dto.MemberAId, dto.MemberBId };
        if (await _context.GroupMembers.CountAsync(m => m.GroupId == groupId && ids.Contains(m.Id), ct) != 2)
            throw new NotFoundException("Wanachama wote wawili lazima wawe wa kikundi hiki.");
        if (await _context.MemberRelations.AnyAsync(r => r.GroupId == groupId && r.Status != MemberRelationStatus.Ended && r.Status != MemberRelationStatus.Rejected
                && ((r.MemberAId == dto.MemberAId && r.MemberBId == dto.MemberBId) || (r.MemberAId == dto.MemberBId && r.MemberBId == dto.MemberAId)), ct))
            throw new ConflictException("Uhusiano kati ya wanachama hawa tayari umesajiliwa.");
        var r = new MemberRelation
        {
            Id = Guid.NewGuid(), GroupId = groupId, MemberAId = dto.MemberAId, MemberBId = dto.MemberBId, Type = dto.Type,
            Note = dto.Note?.Trim(), CreatedByUserId = userId, Status = MemberRelationStatus.Pending
        };
        _context.MemberRelations.Add(r);
        await Audit(groupId, r.MemberAId, userId, AuditAction.RelationAdded, nameof(MemberRelation), r.Id, null,
            new { r.MemberAId, r.MemberBId, Type = r.Type.ToString(), r.Note, Status = r.Status.ToString() }, null, "Ombi la kusajili uhusiano (linasubiri kiongozi wa pili)", ct);
        await _notify.NotifyLeadersAsync(groupId, "Relation", "Ombi la kusajili uhusiano", "Kiongozi ameomba kusajili uhusiano wa wanachama (mgongano wa maslahi). Kiongozi mwingine aamue.", "/mafao?tab=sera", ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>Kiongozi wa pili (si aliyeomba, si mmoja wa wahusika) anaidhinisha au kukataa uhusiano mpya.</summary>
    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<MemberRelation> DecideRelationAsync(Guid groupId, Guid relationId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideRelationCoreAsync(groupId, relationId, approve, note, userId, ct), ct);

    private async Task<MemberRelation> DecideRelationCoreAsync(Guid groupId, Guid relationId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(groupId, userId, ct);
        var r = await _context.MemberRelations.FirstOrDefaultAsync(x => x.Id == relationId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Uhusiano haupo.");
        if (r.Status != MemberRelationStatus.Pending) throw new ConflictException("Uhusiano huu hausubiri uamuzi.");
        var cancelling = r.CreatedByUserId == userId;
        if (cancelling && approve) throw new ForbiddenException("Aliyeomba kusajili uhusiano HAWEZI kuuidhinisha mwenyewe (maker-checker). Unaweza kulighairi tu.");
        if (!cancelling && (actor.Id == r.MemberAId || actor.Id == r.MemberBId)) throw new ForbiddenException("Huwezi kuamua kuhusu uhusiano wako mwenyewe.");
        r.Status = approve ? MemberRelationStatus.Active : MemberRelationStatus.Rejected;
        r.ApprovedByUserId = userId; r.ApprovedAt = DateTime.UtcNow; r.DecisionNote = cancelling ? "Limeghairiwa na aliyeliomba" : note?.Trim();
        await Audit(groupId, r.MemberAId, userId, approve ? AuditAction.RelationApproved : AuditAction.RelationRejected, nameof(MemberRelation), r.Id, null,
            new { Status = r.Status.ToString() }, null, r.DecisionNote, ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>Kuondoa uhusiano kunapunguza ulinzi — kunahitaji kiongozi wa pili (maker-checker).</summary>
    public async Task<MemberRelation> RequestEndRelationAsync(Guid groupId, Guid relationId, string reason, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        var r = await _context.MemberRelations.FirstOrDefaultAsync(x => x.Id == relationId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Uhusiano haupo.");
        if (r.Status != MemberRelationStatus.Active) throw new ConflictException("Uhusiano huu hauko hai au tayari una ombi la kuondolewa.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Eleza kwa nini uhusiano huu uondolewe.");
        r.Status = MemberRelationStatus.EndPending; r.EndRequestedByUserId = userId; r.EndRequestedAt = DateTime.UtcNow; r.EndReason = reason.Trim();
        await Audit(groupId, r.MemberAId, userId, AuditAction.RelationEndRequested, nameof(MemberRelation), r.Id, null, new { Status = r.Status.ToString(), r.EndReason }, null, reason, ct);
        await _notify.NotifyLeadersAsync(groupId, "Relation", "Ombi la kuondoa uhusiano", "Kiongozi ameomba kuondoa uhusiano wa wanachama (mgongano wa maslahi). Kiongozi mwingine aamue.", "/mafao?tab=sera", ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<MemberRelation> DecideEndRelationAsync(Guid groupId, Guid relationId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideEndRelationCoreAsync(groupId, relationId, approve, note, userId, ct), ct);

    private async Task<MemberRelation> DecideEndRelationCoreAsync(Guid groupId, Guid relationId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(groupId, userId, ct);
        var r = await _context.MemberRelations.FirstOrDefaultAsync(x => x.Id == relationId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Uhusiano haupo.");
        if (r.Status != MemberRelationStatus.EndPending) throw new ConflictException("Hakuna ombi la kuondoa uhusiano huu.");
        if (r.EndRequestedByUserId == userId) throw new ForbiddenException("Aliyeomba kuondoa uhusiano HAWEZI kuamua mwenyewe (maker-checker).");
        if (actor.Id == r.MemberAId || actor.Id == r.MemberBId) throw new ForbiddenException("Huwezi kuamua kuhusu uhusiano wako mwenyewe.");
        if (approve) { r.Status = MemberRelationStatus.Ended; r.EndedByUserId = userId; r.EndedAt = DateTime.UtcNow; }
        else { r.Status = MemberRelationStatus.Active; r.EndRequestedByUserId = null; r.EndRequestedAt = null; r.EndReason = null; }
        await Audit(groupId, r.MemberAId, userId, AuditAction.RelationEnded, nameof(MemberRelation), r.Id, null, new { Status = r.Status.ToString(), Approved = approve }, null, note, ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    // =====================================================================
    // USTAHIKI WA ZIADA (kinga dhidi ya faida isiyo halali)
    // =====================================================================
    private async Task AddAntiProfitChecksAsync(List<Check> checks, Guid groupId, BenefitGovernance gov, Guid beneficiaryId, OpenCaseDto dto,
        Guid? excludeCaseId, CancellationToken ct)
    {
        var eventDate = dto.EventDate.Date;
        if (gov.CooldownDays is { } cd)
        {
            var prior = await _context.BenefitCases.AsNoTracking()
                .Where(c => c.GroupId == groupId && c.BeneficiaryGroupMemberId == beneficiaryId && c.Id != excludeCaseId && c.Category == dto.Category
                            && c.Status != BenefitCaseStatus.Rejected && c.Status != BenefitCaseStatus.Cancelled)
                .Select(c => new { c.CaseNumber, c.EventDate }).ToListAsync(ct);
            var near = prior.Select(p => new { p.CaseNumber, p.EventDate, Gap = Math.Abs((p.EventDate.Date - eventDate).Days) })
                .Where(p => p.Gap < cd).OrderBy(p => p.Gap).FirstOrDefault();
            checks.Add(new("COOLDOWN", near is null, near is null
                ? Loc.L($"Hakuna dai lingine la {CategoryName(dto.Category)} ndani ya siku {cd}.")
                : Loc.L($"Dai la {CategoryName(dto.Category)} {near.CaseNumber} ({near.EventDate:dd/MM/yyyy}) liko ndani ya siku {near.Gap}; sera inataka angalau siku {cd} kati ya madai.")));
        }
        var subject = Norm(dto.SubjectName);
        var others = (await _context.BenefitCases.AsNoTracking()
                .Where(c => c.GroupId == groupId && c.BeneficiaryGroupMemberId != beneficiaryId && c.Id != excludeCaseId && c.Category == dto.Category
                            && c.Status != BenefitCaseStatus.Rejected && c.Status != BenefitCaseStatus.Cancelled)
                .Select(c => new { c.CaseNumber, c.SubjectName }).ToListAsync(ct))
            .Where(c => Norm(c.SubjectName) == subject).Select(c => c.CaseNumber).ToList();
        if (others.Count > 0)
            checks.Add(gov.BlockSameSubjectAcrossMembers
                ? new("SUBJECT_OTHER_MEMBER", false,
                    $"Mhusika '{dto.SubjectName.Trim()}' tayari anadaiwa na mwanachama mwingine ({string.Join(", ", others)}). Sera inazuia tukio moja kudaiwa mara mbili — kama ni watu tofauti, andika jina kamili linalowatofautisha.")
                : new("SUBJECT_OTHER_MEMBER", true,
                    $"Tahadhari: mhusika '{dto.SubjectName.Trim()}' tayari anadaiwa na mwanachama mwingine ({string.Join(", ", others)}). Wahakiki wathibitishe si tukio lile lile."));
    }

    private async Task AddPeriodLimitCheckAsync(List<Check> checks, Guid groupId, BenefitGovernance gov, Guid beneficiaryId, DateTime eventDate, decimal gross,
        Guid? excludeCaseId, CancellationToken ct)
    {
        if (gov.MaxBenefitAmountPerPeriod is not { } max || gov.MaxBenefitPeriodDays is not { } days) return;
        var from = DateOnlyUtc(eventDate.AddDays(-days + 1));
        var to = DateOnlyUtc(eventDate);
        var already = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.BeneficiaryGroupMemberId == beneficiaryId && c.Id != excludeCaseId
                        && c.Status != BenefitCaseStatus.Rejected && c.Status != BenefitCaseStatus.Cancelled && c.EventDate >= from && c.EventDate <= to)
            .SumAsync(c => (decimal?)c.GrossAmount, ct) ?? 0m;
        var ok = already + gross <= max;
        checks.Add(new("MAX_AMOUNT_PERIOD", ok, ok
            ? Loc.L($"Jumla ya mafao ndani ya siku {days}: {already + gross:N0} kati ya kikomo {max:N0}.")
            : Loc.L($"Kikomo cha mafao ni {max:N0} ndani ya siku {days}; tayari {already:N0} + dai hili {gross:N0} = {already + gross:N0}.")));
    }

    // =====================================================================
    // B5: PESA IPO? + KUANZISHA MALIPO
    // =====================================================================
    /// <summary>
    /// SASISHO17 (P0-4): StatementAgeDays = siku tangu muamala wa mwisho wa PDF ya M-Koba (EAT). Taarifa ikiwa ya zamani kuliko
    /// TruthGroupSettings.CashStatementMaxAgeDays (au sera haijawekwa) → Stale=true na Sufficient=false (fail-closed):
    /// salio la miezi iliyopita halithibitishi kwamba pesa ipo leo.
    /// </summary>
    public sealed record CashPosition(decimal? MkobaBalance, DateTime? AsOf, string? AsOfReference, decimal Reserved, List<string> ReservedCases,
        decimal? Available, decimal Needed, bool Sufficient, int? StatementAgeDays = null, int? MaxAgeDays = null, bool Stale = false);

    /// <summary>Sababu moja (ya lugha mbili) kwa nini pesa "haitoshi" — inatumika na mafao NA mikopo (chanzo kimoja).</summary>
    public static Loc CashShortReason(CashPosition cash)
    {
        if (cash.Available is null) return Loc.L($"Hakuna salio la M-Koba lililoingizwa — ingiza PDF ya karibuni kwanza.");
        if (cash.MaxAgeDays is null)
            return Loc.L($"Sera ya umri wa taarifa ya M-Koba haijawekwa (Mipangilio → umri wa juu wa taarifa). Malipo yanazuiwa mpaka iwekwe.");
        if (cash.Stale)
            return Loc.L($"Taarifa ya M-Koba ni ya zamani: muamala wa mwisho {cash.AsOf:dd/MM/yyyy} (siku {cash.StatementAgeDays}); sera inaruhusu siku {cash.MaxAgeDays}. Ingiza PDF ya karibuni kwanza.");
        return Loc.L($"Pesa haitoshi: salio la M-Koba {cash.MkobaBalance:N0} ({cash.AsOf:dd/MM/yyyy}) − kesi nyingine ambazo hazijalipwa {cash.Reserved:N0} = {cash.Available:N0}; inahitajika {cash.Needed:N0}.");
    }

    /// <summary>
    /// Pesa inayopatikana = salio la MWISHO la M-Koba (kutoka PDF zilizoingizwa — ukweli, si Group.Balance)
    /// − kiasi cha kesi nyingine zilizoidhinishwa/zilizoanzishwa ambazo hazijalipwa.
    /// </summary>
    public async Task<CashPosition> CashPositionAsync(Guid groupId, Guid? forCaseId, decimal needed, CancellationToken ct = default, Guid? excludeLoanApplicationId = null)
    {
        var last = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId && t.Balance != null)
            .OrderByDescending(t => t.Timestamp).ThenByDescending(t => t.RowOrder).Select(t => new { t.Balance, t.Timestamp, t.Reference }).FirstOrDefaultAsync(ct);
        var reserved = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Id != forCaseId && (c.Status == BenefitCaseStatus.Approved || c.Status == BenefitCaseStatus.PaymentInitiated) && c.NetAmount > 0)
            .Select(c => new { c.CaseNumber, c.NetAmount }).ToListAsync(ct);
        // SASISHO15: mikopo iliyoidhinishwa/iliyoanzishwa ambayo bado haijatolewa nayo inashikilia pesa
        var loanRes = await _context.LoanApplications.AsNoTracking()
            .Where(a => a.GroupId == groupId && a.Id != excludeLoanApplicationId
                        && (a.Status == LoanApplicationStatus.Approved || a.Status == LoanApplicationStatus.DisbursementInitiated)
                        && (a.ApprovalExpiresAt == null || a.ApprovalExpiresAt >= DateTime.UtcNow || a.Status == LoanApplicationStatus.DisbursementInitiated))
            .Select(a => new { CaseNumber = a.ApplicationNumber, NetAmount = a.Amount }).ToListAsync(ct);
        reserved = reserved.Concat(loanRes).ToList();
        var res = reserved.Sum(r => r.NetAmount);
        decimal? avail = last?.Balance is { } b ? b - res : null;
        var maxAge = await _context.TruthGroupSettings.AsNoTracking().Where(s => s.GroupId == groupId).Select(s => s.CashStatementMaxAgeDays).FirstOrDefaultAsync(ct);
        // Timestamp ya M-Koba ni saa za EAT (imeandikwa +00) — inalinganishwa na tarehe ya leo EAT
        int? age = last is null ? null : Math.Max(0, (TruthLedgerService.NowEat().Date - last.Timestamp.Date).Days);
        var stale = maxAge is null || age is null || age > maxAge;
        return new CashPosition(last?.Balance, last?.Timestamp, last?.Reference, res, reserved.Select(r => $"{r.CaseNumber} ({r.NetAmount:N0})").ToList(),
            avail, needed, !stale && avail is { } a && a >= needed, age, maxAge, stale);
    }

    public sealed record PaymentPermissions(bool CanInitiate, string? InitiateReason, bool CanConfirm, string? ConfirmReason, CashPosition? Cash,
        LocMessage? InitiateReasonLoc = null, LocMessage? ConfirmReasonLoc = null, bool CashBlocked = false, DeductionDrift? Drift = null);

    /// <summary>
    /// SASISHO16: makato ya kesi yanafungwa kura zinapoanza (snapshot ya ukaguzi — hayabadiliki). Mnufaika akilipa
    /// deni lake BAADA ya hapo, makato yaliyofungwa yanazidi deni la sasa. Hatubadilishi kesi iliyoidhinishwa kimya;
    /// badala yake anayeanzisha/anayethibitisha malipo anaonyeshwa ziada hiyo na inakokwenda (kwenye Injini ya Ukweli
    /// ziada inaingia mfuatano wa kawaida: deni lijalo → Kianzio → akiba; haipotei).
    /// </summary>
    public sealed record DeductionDrift(decimal Frozen, decimal Current, decimal Excess, string Message, LocMessage MessageLoc);

    private async Task<DeductionDrift?> DeductionDriftAsync(BenefitCase c, BenefitGovernance gov, CancellationToken ct)
    {
        var frozen = c.JoinFeeDeducted + c.ArrearsDeducted + (c.LoanArrearsDeducted ?? 0m);
        if (frozen <= 0) return null;
        var now = (await _truth.GetMemberTruthAsync(c.BeneficiaryGroupMemberId, null, ct)).Statement;
        var cur = ChamaLink.Domain.Ledger.WelfareDeductionRule.ComputeSplit(c.GrossAmount, now, gov.DeductJoinFee, gov.DeductArrears, gov.DeductLoanArrears);
        return ComputeDrift(c, cur.JoinFeeDeducted + cur.ArrearsDeducted + cur.LoanArrearsDeducted);
    }

    /// <summary>Hesabu safi (bila DB): ziada ya makato yaliyofungwa juu ya deni la sasa; null kama hakuna ziada.</summary>
    public static DeductionDrift? ComputeDrift(BenefitCase c, decimal current)
    {
        var frozen = c.JoinFeeDeducted + c.ArrearsDeducted + (c.LoanArrearsDeducted ?? 0m);
        if (frozen <= 0 || current >= frozen) return null;
        var excess = frozen - current;
        Loc msg = $"Makato yaliyofungwa kura zilipoanza ni {frozen:N0}, lakini deni la sasa la mnufaika ni {current:N0} (amelipa sehemu baada ya hapo). Ziada ya {excess:N0} haipotei: itaingia kwenye hesabu yake (deni lijalo, Kianzio au akiba). Kiasi cha kulipwa kinabaki {c.NetAmount:N0} kama kilivyoidhinishwa.";
        return new DeductionDrift(frozen, current, excess, msg.Text, msg.ToMessage());
    }

    private async Task<PaymentPermissions> PaymentPermissionsAsync(BenefitCase c, BenefitGovernance? gov, GroupMember me, List<GovernanceVote> votes, bool conflicted, CancellationToken ct)
    {
        if (gov is null || c.Status is not (BenefitCaseStatus.Approved or BenefitCaseStatus.PaymentInitiated))
            return new(false, null, false, null, null);
        var cash = await CashPositionAsync(c.GroupId, c.Id, c.NetAmount, ct);
        Loc? initReason = null, confReason = null;
        var cashBlocked = false;
        if (c.Status == BenefitCaseStatus.Approved)
        {
            var roles = ParseRoles(gov.PaymentInitiatorRoles);
            var highValue = gov.HighValueThreshold is { } hv && c.NetAmount >= hv;
            if (!roles.Contains(Common.MemberAccess.EffectiveRole(me))) initReason = Loc.L($"Sera (toleo {gov.Version}): malipo yanaanzishwa na {string.Join(", ", roles.Select(RoleName))}.");
            else if (me.Id == c.BeneficiaryGroupMemberId) initReason = "Mnufaika hawezi kuanzisha malipo yake.";
            else if (conflicted) initReason = "Wewe ni ndugu wa mnufaika au wa aliyefungua kesi (mgongano wa maslahi).";
            else if (gov.ApproverCannotInitiatePayment && votes.Any(v => v.Approve && v.GroupMemberId == me.Id))
                initReason = "Uliidhinisha kesi hii kwa kura — sera inataka mweka-malipo awe mtu mwingine.";
            else if (highValue && votes.Any(v => v.Approve && v.GroupMemberId == me.Id))
                initReason = Loc.L($"Malipo makubwa ({c.NetAmount:N0} ≥ {gov.HighValueThreshold:N0}): aliyeidhinisha kwa kura hawezi kuanzisha malipo — mtu mwingine aanzishe.");
            else if (gov.RequireCashAvailability && !cash.Sufficient)
            {
                cashBlocked = true;
                initReason = CashShortReason(cash);
            }
        }
        else
        {
            var roles = ParseRoles(gov.PaymentConfirmerRoles);
            if (!roles.Contains(Common.MemberAccess.EffectiveRole(me))) confReason = Loc.L($"Sera (toleo {gov.Version}): malipo yanathibitishwa na {string.Join(", ", roles.Select(RoleName))}.");
            else if (me.Id == c.BeneficiaryGroupMemberId) confReason = "Mnufaika hawezi kuthibitisha malipo yake.";
            else if (c.PaymentInitiatedByUserId == me.UserId) confReason = "Wewe ndiye uliyeanzisha malipo — mthibitishaji lazima awe mtu mwingine.";
            else if (conflicted) confReason = "Wewe ni ndugu wa mnufaika, wa aliyefungua kesi au wa aliyeanzisha malipo (mgongano wa maslahi).";
        }
        var drift = await DeductionDriftAsync(c, gov, ct);
        return new(c.Status == BenefitCaseStatus.Approved && initReason is null, initReason?.Text,
                   c.Status == BenefitCaseStatus.PaymentInitiated && confReason is null, confReason?.Text, cash,
                   initReason?.ToMessage(), confReason?.ToMessage(), cashBlocked, drift);
    }

    /// <summary>
    /// HATUA YA 1 YA MALIPO: mweka-malipo (nafasi kutoka sera, mf. Mtunza-hazina) anathibitisha pesa ipo M-Koba
    /// na anaelekeza malipo kwa namba iliyoidhinishwa ya mnufaika. Kesi inakuwa PaymentInitiated.
    /// </summary>
    public async Task<BenefitCase> InitiatePaymentAsync(Guid groupId, Guid caseId, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Kesi haipo.");
        if (c.Status != BenefitCaseStatus.Approved) throw new ConflictException("Malipo yanaanzishwa kwa kesi iliyoidhinishwa kwa kura tu.");
        var gov = await _context.BenefitGovernances.AsNoTracking().FirstAsync(g => g.Id == c.GovernanceId, ct);
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && v.SubjectId == c.Id).ToListAsync(ct);
        var perm = await PaymentPermissionsAsync(c, gov, actor, votes, await IsConflictedAsync(c, actor.Id, ct), ct);
        if (!perm.CanInitiate)
        {
            var reason = perm.InitiateReasonLoc is { } rl ? Loc.Of(rl.Tpl, rl.Args, perm.InitiateReason ?? rl.Tpl) : (Loc)"Huruhusiwi kuanzisha malipo haya.";
            if (perm.CashBlocked && perm.Cash is { Available: not null }) await RiskBlockedAsync(groupId, c, userId, "CASH_INSUFFICIENT", reason, ct);
            throw new ForbiddenException(reason);
        }
        var phones = await MemberPhonesAt.ListAsync(_context, groupId, c.BeneficiaryGroupMemberId, TruthLedgerService.NowEat(), ct); // SASISHO17 (P0-5)
        if (phones.Count == 0 && string.IsNullOrWhiteSpace(note))
            throw new ValidationException("Mnufaika hana namba iliyoidhinishwa. Eleza pesa itapokelewa na nani (mf. familia ya marehemu) — itaonekana kwenye ukaguzi.");
        // SASISHO17 (P0-3): ahadi ya pesa inapangwa mstari mmoja kwa kikundi (mafao + mikopo). Ndani ya kufuli: hali ya kesi
        // na pesa zinasomwa UPYA — maombi mawili ya wakati mmoja hayawezi kutumia salio lile lile.
        await using var tx = await DbTx.BeginAsync(_context, ct);
        await DbTx.LockAsync(_context, groupId, DbTx.CashCommitment, ct);
        if (_context.Database.IsRelational()) await _context.Entry(c).ReloadAsync(ct);
        if (c.Status != BenefitCaseStatus.Approved) throw new ConflictException("Malipo yanaanzishwa kwa kesi iliyoidhinishwa kwa kura tu.");
        if (gov.RequireCashAvailability)
        {
            var fresh = await CashPositionAsync(groupId, c.Id, c.NetAmount, ct);
            if (!fresh.Sufficient) throw new ConflictException(CashShortReason(fresh));
            perm = perm with { Cash = fresh };
        }
        var before = CaseSnap(c);
        c.Status = BenefitCaseStatus.PaymentInitiated;
        c.PaymentInitiatedByUserId = userId; c.PaymentInitiatedAt = DateTime.UtcNow;
        c.PaymentInitiatedToPhone = phones.FirstOrDefault();
        c.CashAvailableAtInitiation = perm.Cash?.Available;
        // SASISHO16: ziada ya makato (kama ipo) inaandikwa kwenye ukaguzi pamoja na maelezo ya mweka-malipo
        var auditNote = perm.Drift is { } dr
            ? string.Join(" | ", new[] { note?.Trim(), dr.Message }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : note?.Trim();
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.PaymentInitiated, nameof(BenefitCase), c.Id, before, CaseSnap(c), c.CaseNumber,
            auditNote, ct);
        var who = await MemberName(c.BeneficiaryGroupMemberId, ct);
        await _notify.NotifyLeadersAsync(groupId, "ConfirmPayment", $"Thibitisha malipo {c.CaseNumber}",
            c.PaymentInitiatedToPhone is null
                ? Loc.L($"{RoleName(actor.Role)} ameanzisha malipo ya {c.NetAmount:N0} kwa {who}. Baada ya kuingiza PDF ya M-Koba, thibitisha kwa reference ya utoaji (mtu tofauti na aliyeanzisha).")
                : Loc.L($"{RoleName(actor.Role)} ameanzisha malipo ya {c.NetAmount:N0} kwa {who} ({c.PaymentInitiatedToPhone}). Baada ya kuingiza PDF ya M-Koba, thibitisha kwa reference ya utoaji (mtu tofauti na aliyeanzisha)."),
            $"/mafao?kesi={c.Id}", ct,
            ParseRoles(gov.PaymentConfirmerRoles).ToArray());
        await _notify.NotifyAsync(groupId, new[] { c.BeneficiaryGroupMemberId }, "PaymentInitiated", $"Malipo yameanzishwa: {c.CaseNumber}",
            $"Malipo ya {c.NetAmount:N0} yameanzishwa. Utaarifiwa yakithibitishwa kwenye M-Koba.", $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        await DbTx.CommitAsync(tx, ct);
        return c;
    }

    // =====================================================================
    // B6: MAANDISHI YA FEDHA YA MAFAO + ULINGANISHO
    // =====================================================================
    public static string JoinFeeOffsetRef(string caseNumber) => $"MAFAO-JF-{caseNumber}";
    public static string ArrearsOffsetRef(string caseNumber) => $"MAFAO-DENI-{caseNumber}";
    public static string LoanArrearsOffsetRef(string caseNumber) => $"MAFAO-MKOPO-{caseNumber}";
    /// <summary>Rejea ya malipo ya taslimu (kesi za zamani nje ya M-Koba).</summary>
    public static string CashRef(string caseNumber) => $"CASH-{caseNumber}";

    /// <summary>
    /// Andika maandishi ya fedha ya kesi iliyolipwa (mara moja tu; index ya kipekee (kesi, aina) inazuia marudio)
    /// + tukio la ukaguzi (BenefitLedgerPosted) ili kila andiko liwe kwenye mnyororo wa hash.
    /// </summary>
    private async Task PostLedgerAsync(BenefitCase c, Guid userId, CancellationToken ct)
    {
        var loan = c.LoanArrearsDeducted ?? 0m;
        if (c.GrossAmount != c.JoinFeeDeducted + c.ArrearsDeducted + loan + c.NetAmount)
            throw new ConflictException($"Hesabu ya kesi {c.CaseNumber} haijakaa sawa: {c.GrossAmount:N0} ≠ Kianzio {c.JoinFeeDeducted:N0} + deni la michango {c.ArrearsDeducted:N0} + deni la mkopo {loan:N0} + pesa {c.NetAmount:N0}.");
        if (_context.BenefitLedgerEntries.Local.Any(e => e.BenefitCaseId == c.Id)
            || await _context.BenefitLedgerEntries.AnyAsync(e => e.BenefitCaseId == c.Id, ct)) return;
        var posted = new List<object>();
        void Add(BenefitLedgerKind k, decimal amount, string? reference)
        {
            _context.BenefitLedgerEntries.Add(new BenefitLedgerEntry
            {
                Id = Guid.NewGuid(), GroupId = c.GroupId, BenefitCaseId = c.Id, BeneficiaryGroupMemberId = c.BeneficiaryGroupMemberId,
                Kind = k, Amount = amount, Reference = reference, PostedByUserId = userId
            });
            posted.Add(new { Kind = k.ToString(), Amount = amount, Reference = reference });
        }
        Add(BenefitLedgerKind.Approved, c.GrossAmount, c.CaseNumber);
        if (c.JoinFeeDeducted > 0) Add(BenefitLedgerKind.JoinFeeOffset, c.JoinFeeDeducted, JoinFeeOffsetRef(c.CaseNumber));
        if (c.ArrearsDeducted > 0) Add(BenefitLedgerKind.ArrearsOffset, c.ArrearsDeducted, ArrearsOffsetRef(c.CaseNumber));
        if (loan > 0) Add(BenefitLedgerKind.LoanArrearsOffset, loan, LoanArrearsOffsetRef(c.CaseNumber));
        Add(BenefitLedgerKind.CashPaid, c.NetAmount, c.MkobaReference ?? (c.PaymentChannel == BenefitPaymentChannel.Cash ? CashRef(c.CaseNumber) : null));
        await Audit(c.GroupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitLedgerPosted, nameof(BenefitCase), c.Id, null,
            new { c.CaseNumber, Entries = posted }, c.MkobaReference ?? c.CaseNumber, "Maandishi ya fedha ya mafao yameandikwa", ct);
    }

    public sealed record LedgerEntryView(string Kind, decimal Amount, string? Reference, DateTime PostedAt);
    public sealed record CaseReconciliation(bool Posted, List<LedgerEntryView> Entries, List<Check> Checks, bool Ok);

    private static CaseReconciliation Reconcile(BenefitCase c, List<BenefitLedgerEntry> entries, MkobaRawTransaction? row, ContributionTruthStatementView? truth,
        IReadOnlyCollection<BenefitEvidence>? evidence = null)
    {
        if (c.Status != BenefitCaseStatus.Paid) return new(false, new(), new(), true);
        var checks = new List<Check>();
        decimal Sum(BenefitLedgerKind k) => entries.Where(e => e.Kind == k).Sum(e => e.Amount);
        var gross = Sum(BenefitLedgerKind.Approved); var jf = Sum(BenefitLedgerKind.JoinFeeOffset);
        var arr = Sum(BenefitLedgerKind.ArrearsOffset); var cash = Sum(BenefitLedgerKind.CashPaid);
        var loan = Sum(BenefitLedgerKind.LoanArrearsOffset); var caseLoan = c.LoanArrearsDeducted ?? 0m;
        checks.Add(new("LEDGER_POSTED", entries.Count > 0, entries.Count > 0 ? "Maandishi ya fedha yameandikwa." : "Kesi imelipwa lakini maandishi ya fedha HAYAJAANDIKWA."));
        if (entries.Count > 0)
        {
            var inv = gross == jf + arr + loan + cash;
            checks.Add(new("INVARIANT", inv, inv ? Loc.L($"{gross:N0} = Kianzio {jf:N0} + deni la michango {arr:N0} + deni la mkopo {loan:N0} + pesa {cash:N0}.")
                : Loc.L($"Hesabu haikai: {gross:N0} ≠ Kianzio {jf:N0} + deni la michango {arr:N0} + deni la mkopo {loan:N0} + pesa {cash:N0}.")));
            var same = gross == c.GrossAmount && jf == c.JoinFeeDeducted && arr == c.ArrearsDeducted && loan == caseLoan && cash == c.NetAmount;
            checks.Add(new("MATCHES_SNAPSHOT", same, same ? "Maandishi yanalingana na hesabu iliyofungwa ya kesi." : "Maandishi HAYALINGANI na hesabu iliyofungwa ya kesi."));
        }
        if (c.PaymentChannel == BenefitPaymentChannel.Cash)
        {
            // SASISHO14: taslimu ya zamani — hakuna M-Koba; ushahidi uliohakikiwa na mtu asiye aliyeupakia ndio kinga
            var verified = (evidence ?? Array.Empty<BenefitEvidence>()).Where(e => e.Status == EvidenceStatus.Verified && e.VerifiedByUserId != e.UploadedByUserId).ToList();
            var ok = verified.Count > 0;
            checks.Add(new("CASH_EVIDENCE", ok, ok
                ? Loc.L($"Malipo ya taslimu ({c.NetAmount:N0}) kwa {c.CashRecipientName}: ushahidi {verified.Count} uliohakikiwa ({string.Join(", ", verified.Select(e => e.DocumentNumber))}).")
                : Loc.L($"Malipo ya taslimu ({c.NetAmount:N0}) HAYANA ushahidi uliohakikiwa na mtu asiye aliyeupakia.")));
        }
        else if (c.NetAmount > 0)
        {
            var ok = row is { IsWithdrawal: true } && row.Amount == c.NetAmount;
            checks.Add(new("MKOBA_CASH", ok, ok ? Loc.L($"M-Koba {row!.Reference}: {row.Amount:N0} ({row.Timestamp:dd/MM/yyyy}).")
                : row is null ? Loc.L($"Rejea ya M-Koba {c.MkobaReference} haipo kwenye PDF zilizoingizwa.") : Loc.L($"M-Koba {row.Reference} ni {row.Amount:N0}, si {c.NetAmount:N0}.")));
        }
        if (truth is not null)
        {
            if (c.JoinFeeDeducted > 0)
            {
                var got = truth.AllocatedFor(JoinFeeOffsetRef(c.CaseNumber));
                checks.Add(new("TRUTH_JOIN_FEE", got == c.JoinFeeDeducted, got == c.JoinFeeDeducted
                    ? Loc.L($"Injini ya Ukweli: Kianzio {got:N0} kimelipwa kwa makato ({JoinFeeOffsetRef(c.CaseNumber)}).")
                    : Loc.L($"Injini ya Ukweli inaonyesha {got:N0} kwa {JoinFeeOffsetRef(c.CaseNumber)}, si {c.JoinFeeDeducted:N0}.")));
            }
            if (c.ArrearsDeducted > 0)
            {
                var got = truth.AllocatedFor(ArrearsOffsetRef(c.CaseNumber));
                checks.Add(new("TRUTH_ARREARS", got == c.ArrearsDeducted, got == c.ArrearsDeducted
                    ? Loc.L($"Injini ya Ukweli: deni {got:N0} limelipwa kwa makato ({ArrearsOffsetRef(c.CaseNumber)}).")
                    : Loc.L($"Injini ya Ukweli inaonyesha {got:N0} kwa {ArrearsOffsetRef(c.CaseNumber)}, si {c.ArrearsDeducted:N0}.")));
            }
            if (caseLoan > 0)
            {
                var got = truth.AllocatedFor(LoanArrearsOffsetRef(c.CaseNumber));
                checks.Add(new("TRUTH_LOAN_ARREARS", got == caseLoan, got == caseLoan
                    ? Loc.L($"Injini ya Ukweli: deni la mkopo {got:N0} limelipwa kwa makato ({LoanArrearsOffsetRef(c.CaseNumber)}).")
                    : Loc.L($"Injini ya Ukweli inaonyesha {got:N0} kwa {LoanArrearsOffsetRef(c.CaseNumber)}, si {caseLoan:N0}.")));
            }
        }
        return new(entries.Count > 0, entries.OrderBy(e => e.Kind).Select(e => new LedgerEntryView(e.Kind.ToString(), e.Amount, e.Reference, e.PostedAt)).ToList(),
            checks, checks.All(x => x.Passed));
    }

    /// <summary>Sehemu ndogo ya taarifa ya Injini ya Ukweli inayohitajika kwa ulinganisho.</summary>
    public sealed class ContributionTruthStatementView
    {
        private readonly Dictionary<string, decimal> _byRef;
        public ContributionTruthStatementView(IEnumerable<(string Reference, decimal Amount)> lines)
            => _byRef = lines.GroupBy(l => l.Reference).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        public decimal AllocatedFor(string reference) => _byRef.TryGetValue(reference, out var v) ? v : 0m;
    }

    private async Task<CaseReconciliation> ReconcileCaseAsync(BenefitCase c, CancellationToken ct)
    {
        if (c.Status != BenefitCaseStatus.Paid) return new(false, new(), new(), true);
        var entries = await _context.BenefitLedgerEntries.AsNoTracking().Where(e => e.BenefitCaseId == c.Id).ToListAsync(ct);
        var row = c.MkobaReference is null ? null
            : await _context.MkobaRawTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.GroupId == c.GroupId && t.Reference == c.MkobaReference, ct);
        ContributionTruthStatementView? view = null;
        if (c.JoinFeeDeducted + c.ArrearsDeducted + (c.LoanArrearsDeducted ?? 0m) > 0)
        {
            var t = await _truth.GetMemberTruthAsync(c.BeneficiaryGroupMemberId, null, ct);
            view = new(t.Statement.Allocations.Select(a => (a.Reference, a.Amount)));
        }
        var evidence = c.PaymentChannel == BenefitPaymentChannel.Cash
            ? await _context.BenefitEvidence.AsNoTracking().Where(e => e.BenefitCaseId == c.Id).ToListAsync(ct) : null;
        return Reconcile(c, entries, row, view, evidence);
    }

    public async Task<object> ReconciliationAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderOrAuditorAsync(groupId, userId, ct);
        var cases = await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId && c.Status == BenefitCaseStatus.Paid).OrderBy(c => c.PaidAt).ToListAsync(ct);
        var ids = cases.Select(c => c.Id).ToList();
        var entries = await _context.BenefitLedgerEntries.AsNoTracking().Where(e => ids.Contains(e.BenefitCaseId)).ToListAsync(ct);
        var refs = cases.Where(c => c.MkobaReference != null).Select(c => c.MkobaReference!).ToList();
        var rows = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId && refs.Contains(t.Reference)).ToListAsync(ct);
        var needTruth = cases.Any(c => c.JoinFeeDeducted + c.ArrearsDeducted + (c.LoanArrearsDeducted ?? 0m) > 0);
        var cashIds = cases.Where(c => c.PaymentChannel == BenefitPaymentChannel.Cash).Select(c => c.Id).ToList();
        var cashEvidence = await _context.BenefitEvidence.AsNoTracking().Where(e => cashIds.Contains(e.BenefitCaseId)).ToListAsync(ct);
        var truth = needTruth ? await _truth.GetGroupTruthAsync(groupId, null, ct) : null;
        var list = cases.Select(c =>
        {
            var t = truth?.FirstOrDefault(x => x.MemberId == c.BeneficiaryGroupMemberId);
            var r = Reconcile(c, entries.Where(e => e.BenefitCaseId == c.Id).ToList(), rows.FirstOrDefault(x => x.Reference == c.MkobaReference),
                t is null ? null : new ContributionTruthStatementView(t.Statement.Allocations.Select(a => (a.Reference, a.Amount))),
                cashEvidence.Where(e => e.BenefitCaseId == c.Id).ToList());
            return new { c.Id, c.CaseNumber, c.GrossAmount, c.JoinFeeDeducted, c.ArrearsDeducted, loanArrearsDeducted = c.LoanArrearsDeducted ?? 0m, c.NetAmount,
                c.MkobaReference, paymentChannel = c.PaymentChannel.ToString(), r.Ok, r.Checks, r.Entries };
        }).ToList();
        return new
        {
            ok = list.All(x => x.Ok), cases = list.Count, failing = list.Count(x => !x.Ok),
            totals = new
            {
                gross = entries.Where(e => e.Kind == BenefitLedgerKind.Approved).Sum(e => e.Amount),
                joinFeeOffset = entries.Where(e => e.Kind == BenefitLedgerKind.JoinFeeOffset).Sum(e => e.Amount),
                arrearsOffset = entries.Where(e => e.Kind == BenefitLedgerKind.ArrearsOffset).Sum(e => e.Amount),
                loanArrearsOffset = entries.Where(e => e.Kind == BenefitLedgerKind.LoanArrearsOffset).Sum(e => e.Amount),
                cashPaid = entries.Where(e => e.Kind == BenefitLedgerKind.CashPaid).Sum(e => e.Amount)
            },
            rows = list
        };
    }

    // =====================================================================
    // B8: DAFTARI LA UWAZI (kila mwanachama) — bila simu, bila vyeti
    // =====================================================================
    public async Task<object> TransparencyAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await RequireMemberAsync(groupId, userId, ct);
        var cases = await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId).OrderByDescending(c => c.EventDate).ThenByDescending(c => c.CreatedAt).ToListAsync(ct);
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        var ids = cases.Select(c => c.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && ids.Contains(v.SubjectId)).ToListAsync(ct);
        var rows = cases.Select(c =>
        {
            var m = members.FirstOrDefault(x => x.Id == c.BeneficiaryGroupMemberId);
            return new
            {
                c.Id, c.CaseNumber, category = c.Category.ToString(), relationship = c.Relationship.ToString(), beneficiaryName = m?.User?.FullName ?? m?.MemberNumber ?? "?",
                memberNumber = m?.MemberNumber, c.SubjectName, c.EventDate, status = c.Status.ToString(), c.IsHistorical, c.PolicyVersion,
                c.PerMemberAmount, c.Headcount, c.GrossAmount, c.JoinFeeDeducted, c.ArrearsDeducted, loanArrearsDeducted = c.LoanArrearsDeducted ?? 0m, c.NetAmount,
                paymentChannel = c.PaymentChannel.ToString(), c.MkobaReference, c.PaidAt, c.ApprovedAt, approvals = votes.Count(v => v.SubjectId == c.Id && v.Approve), rejections = votes.Count(v => v.SubjectId == c.Id && !v.Approve),
                c.ClosedReason
            };
        }).ToList();
        var paid = cases.Where(c => c.Status == BenefitCaseStatus.Paid).ToList();
        return new
        {
            totals = new
            {
                paidCases = paid.Count, gross = paid.Sum(c => c.GrossAmount), joinFeeOffset = paid.Sum(c => c.JoinFeeDeducted),
                arrearsOffset = paid.Sum(c => c.ArrearsDeducted), loanArrearsOffset = paid.Sum(c => c.LoanArrearsDeducted ?? 0m), cashPaid = paid.Sum(c => c.NetAmount),
                awaitingPayment = cases.Where(c => c.Status is BenefitCaseStatus.Approved or BenefitCaseStatus.PaymentInitiated).Sum(c => c.NetAmount),
                awaitingFunds = cases.Where(c => c.Status == BenefitCaseStatus.AwaitingFunds).Sum(c => c.NetAmount)
            },
            rows
        };
    }

    // =====================================================================
    // B7: DASHIBODI YA HATARI (viongozi)
    // =====================================================================
    public sealed record RiskFlag(string Severity, string Code, string Message, Guid? CaseId, string? CaseNumber, DateTime? At, Dictionary<string, string>? Args = null,
        string? Tpl = null, string[]? TArgs = null)
    {
        public RiskFlag(string severity, string code, Loc message, Guid? caseId, string? caseNumber, DateTime? at, Dictionary<string, string>? args = null)
            : this(severity, code, message.Text, caseId, caseNumber, at, args, message.Template, message.Args.ToArray()) { }
    }

    public async Task<object> RiskFlagsAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderOrAuditorAsync(groupId, userId, ct);
        var sealedNow = await AuditChain.SealAsync(_context, groupId, ct);
        var chain = await AuditChain.VerifyAsync(_context, groupId, ct);
        var flags = new List<RiskFlag>();
        if (!chain.Ok) flags.Add(new("danger", "AUDIT_CHAIN_BROKEN", chain.ToLoc(), null, null, null, A(("index", chain.BrokenAtIndex ?? 0))));

        var gov = await GetActiveGovernanceAsync(groupId, ct);
        var cases = await _context.BenefitCases.AsNoTracking().Include(c => c.Evidence).Where(c => c.GroupId == groupId).ToListAsync(ct);
        var live = cases.Where(c => c.Status is not (BenefitCaseStatus.Rejected or BenefitCaseStatus.Cancelled)).ToList();
        var ids = cases.Select(c => c.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && ids.Contains(v.SubjectId)).ToListAsync(ct);
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        string Name(Guid id) => members.FirstOrDefault(m => m.Id == id)?.User?.FullName ?? "?";
        var relations = await _context.MemberRelations.AsNoTracking().Where(r => r.GroupId == groupId && (r.Status == MemberRelationStatus.Active || r.Status == MemberRelationStatus.EndPending)).ToListAsync(ct);
        bool Related(Guid a, Guid b) => relations.Any(r => (r.MemberAId == a && r.MemberBId == b) || (r.MemberAId == b && r.MemberBId == a));
        var phones = await _context.MemberPhones.AsNoTracking().Where(p => p.GroupId == groupId && p.Status == TruthApprovalStatus.Approved)
            .Select(p => new { p.GroupMemberId, p.Phone }).ToListAsync(ct);
        var refs = cases.Where(c => c.MkobaReference != null).Select(c => c.MkobaReference!).ToList();
        var rows = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId && refs.Contains(t.Reference)).ToListAsync(ct);

        // 1. Majaribio yaliyozuiwa (cheti kurudiwa, malipo kabla ya idhini, kiasi tofauti, mthibitishaji = mweka-malipo, pesa haitoshi, faili kubadilika)
        var blocked = await _context.AuditEvents.AsNoTracking().Where(a => a.GroupId == groupId && a.Action == AuditAction.RiskBlocked)
            .OrderByDescending(a => a.Timestamp).Take(RiskBlockedDisplayLimit).ToListAsync(ct);
        foreach (var b in blocked)
        {
            string code = "BLOCKED"; string? tpl = null; string[]? targs = null;
            try
            {
                var root = JsonDocument.Parse(b.AfterJson ?? "{}").RootElement;
                if (root.TryGetProperty("Code", out var p)) code = p.GetString() ?? code;
                if (root.TryGetProperty("Tpl", out var tp) && tp.ValueKind == JsonValueKind.String) tpl = tp.GetString();
                if (root.TryGetProperty("TArgs", out var ta) && ta.ValueKind == JsonValueKind.Array) targs = ta.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            }
            catch (JsonException) { }
            var actor = members.FirstOrDefault(m => m.UserId == b.ActorId)?.User?.FullName ?? "?";
            flags.Add(new(code is "EVIDENCE_TAMPERED" ? "danger" : "warn", "BLOCKED_" + code, PrefixLoc(actor, tpl ?? b.Reason, targs, b.Reason ?? ""), b.EntityId == Guid.Empty ? null : b.EntityId,
                b.SourceReference, b.Timestamp, A(("actor", actor), ("reason", b.Reason ?? ""))));
        }

        // 2. Mhusika yule yule anadaiwa na wanachama tofauti
        foreach (var g in live.GroupBy(c => (c.Category, Subject: Norm(c.SubjectName))).Where(g => g.Select(c => c.BeneficiaryGroupMemberId).Distinct().Count() > 1))
            flags.Add(new("warn", "SUBJECT_MULTI_CLAIM", $"{CategoryName(g.Key.Category)}: '{g.First().SubjectName}' anadaiwa na wanachama {g.Select(c => c.BeneficiaryGroupMemberId).Distinct().Count()}: {string.Join(", ", g.Select(c => $"{c.CaseNumber} ({Name(c.BeneficiaryGroupMemberId)})"))}", g.First().Id, g.First().CaseNumber, null,
                A(("subject", g.First().SubjectName), ("cases", string.Join(", ", g.Select(c => c.CaseNumber))))));

        // 3. Madai mengi
        if (gov?.FrequentClaimsFlagCount is { } freq)
        {
            var since = DateOnlyUtc(TruthLedgerService.NowEat().Date.AddDays(-DaysPerYear));
            foreach (var g in live.Where(c => c.EventDate >= since).GroupBy(c => c.BeneficiaryGroupMemberId).Where(g => g.Count() >= freq))
                flags.Add(new("warn", "FREQUENT_CLAIMS", $"{Name(g.Key)} ana madai {g.Count()} ndani ya siku {DaysPerYear} (tahadhari ≥ {freq}): {string.Join(", ", g.Select(c => c.CaseNumber))}.",
                    g.First().Id, g.First().CaseNumber, null, A(("name", Name(g.Key)), ("count", g.Count()), ("limit", freq))));
        }

        foreach (var c in cases)
        {
            var yes = votes.Where(v => v.SubjectId == c.Id && v.Approve).ToList();
            // 4. Aliyeidhinisha ni mnufaika / aliyefungua / ndugu
            foreach (var v in yes)
            {
                if (v.GroupMemberId == c.BeneficiaryGroupMemberId || v.UserId == c.CreatedByUserId)
                    flags.Add(new("danger", "APPROVER_IS_PARTY", $"{c.CaseNumber}: {Name(v.GroupMemberId)} aliidhinisha kesi ambayo ni mnufaika/aliyeifungua.", c.Id, c.CaseNumber, v.At));
                else if (Related(v.GroupMemberId, c.BeneficiaryGroupMemberId))
                    flags.Add(new("danger", "APPROVER_RELATED", $"{c.CaseNumber}: {Name(v.GroupMemberId)} (ndugu wa mnufaika) aliidhinisha kesi hii.", c.Id, c.CaseNumber, v.At,
                        A(("case", c.CaseNumber), ("name", Name(v.GroupMemberId)))));
            }
            // 5. Aliyehakiki = aliyepakia
            foreach (var e in c.Evidence.Where(e => e.VerifiedByUserId != null && e.VerifiedByUserId == e.UploadedByUserId))
                flags.Add(new("danger", "VERIFIER_IS_UPLOADER", $"{c.CaseNumber}: ushahidi {e.DocumentNumber} ulihakikiwa na aliyeupakia.", c.Id, c.CaseNumber, e.VerifiedAt));
            if (c.Status == BenefitCaseStatus.Paid && !c.IsHistorical)
            {
                // 6. Mthibitishaji = mweka-malipo
                if (c.PaymentInitiatedByUserId is not null && c.PaymentInitiatedByUserId == c.PaidConfirmedByUserId)
                    flags.Add(new("danger", "CONFIRMER_IS_INITIATOR", $"{c.CaseNumber}: malipo yalianzishwa na kuthibitishwa na mtu mmoja.", c.Id, c.CaseNumber, c.PaidConfirmedAt));
                // 7. Malipo kabla ya idhini
                if (c.ApprovedAt is { } ap && c.PaidAt is { } pa && c.NetAmount > 0 && pa.Date < EatDate(ap))
                    flags.Add(new("danger", "PAYMENT_BEFORE_APPROVAL", $"{c.CaseNumber}: pesa ilitoka {pa:dd/MM/yyyy} kabla ya idhini {EatDate(ap):dd/MM/yyyy}.", c.Id, c.CaseNumber, pa));
            }
            if (c.Status == BenefitCaseStatus.Paid && c.MkobaReference is { } rf && rows.FirstOrDefault(r => r.Reference == rf) is { } row)
            {
                // 8. Malipo > kilichoidhinishwa
                if (row.Amount != c.NetAmount)
                    flags.Add(new("danger", "PAYMENT_AMOUNT_MISMATCH", $"{c.CaseNumber}: M-Koba {rf} ni {row.Amount:N0}, kilichoidhinishwa {c.NetAmount:N0}.", c.Id, c.CaseNumber, row.Timestamp));
                // 9. Malipo kwa namba isiyo ya mnufaika
                var benPhones = phones.Where(p => p.GroupMemberId == c.BeneficiaryGroupMemberId).Select(p => PhoneKey(p.Phone)).ToHashSet();
                if (!benPhones.Contains(PhoneKey(row.Phone)))
                {
                    var owner = phones.FirstOrDefault(p => PhoneKey(p.Phone) == PhoneKey(row.Phone));
                    flags.Add(new(owner is null ? "warn" : "danger", "PAYMENT_TO_OTHER_PHONE",
                        owner is null
                            ? Loc.L($"{c.CaseNumber}: pesa ({row.Amount:N0}) ilienda {row.Phone} ({row.PayerName}) — namba haijasajiliwa kwa mwanachama yeyote.")
                            : Loc.L($"{c.CaseNumber}: pesa ({row.Amount:N0}) ilienda {row.Phone} ({row.PayerName}) — namba ya {Name(owner.GroupMemberId)}, si ya mnufaika {Name(c.BeneficiaryGroupMemberId)}."),
                        c.Id, c.CaseNumber, row.Timestamp, A(("case", c.CaseNumber), ("phone", row.Phone))));
                }
            }
            // 10. Kesi iliyoghairiwa baada ya malipo kuanzishwa
            if (c.Status == BenefitCaseStatus.Cancelled && c.PaymentInitiatedAt is not null)
                flags.Add(new("warn", "CANCELLED_AFTER_INITIATION", $"{c.CaseNumber}: iliahirishwa baada ya malipo kuanzishwa ({c.ClosedReason}). Hakikisha pesa haikutoka.", c.Id, c.CaseNumber, c.ClosedAt));
        }

        // 11. Sera/kiwango kilibadilika muda mfupi kabla ya dai
        if (gov?.RecentPolicyChangeFlagDays is { } pd)
        {
            var govs = await _context.BenefitGovernances.AsNoTracking().Where(g => g.GroupId == groupId && g.Status == ProposalStatus.Approved).ToListAsync(ct);
            var rates = await _context.BenefitRates.AsNoTracking().Where(r => r.GroupId == groupId && r.Status == ProposalStatus.Approved).ToListAsync(ct);
            foreach (var c in live.Where(c => !c.IsHistorical))
            {
                var changes = govs.Where(g => g.Version > 1 && g.DecidedAt <= c.CreatedAt && g.DecidedAt >= c.CreatedAt.AddDays(-pd)).Select(g => $"sera toleo {g.Version} ({g.DecidedAt:dd/MM/yyyy})")
                    .Concat(rates.Where(r => r.Category == c.Category && r.Relationship == c.Relationship && r.DecidedAt <= c.CreatedAt && r.DecidedAt >= c.CreatedAt.AddDays(-pd)
                                             && rates.Any(o => o.Id != r.Id && o.Category == r.Category && o.Relationship == r.Relationship && o.DecidedAt < r.DecidedAt))
                        .Select(r => $"kiwango {r.PerMemberAmount:N0} ({r.DecidedAt:dd/MM/yyyy})")).ToList();
                if (changes.Count > 0)
                    flags.Add(new("warn", "POLICY_CHANGED_BEFORE_CLAIM", $"{c.CaseNumber}: imefunguliwa ndani ya siku {pd} baada ya mabadiliko: {string.Join(", ", changes)}.",
                        c.Id, c.CaseNumber, c.CreatedAt, A(("case", c.CaseNumber), ("days", pd), ("changes", string.Join(", ", changes)))));
            }
        }

        // 12. Pesa haitoshi kwa kesi zote zinazosubiri malipo
        var waiting = cases.Where(c => c.Status is BenefitCaseStatus.Approved or BenefitCaseStatus.PaymentInitiated).ToList();
        foreach (var c in cases.Where(c => c.Status == BenefitCaseStatus.AwaitingFunds))
            flags.Add(new("warn", "AWAITING_FUNDS", $"{c.CaseNumber}: imeidhinishwa lakini inasubiri pesa ({c.NetAmount:N0}) — salio la M-Koba halitoshi.", c.Id, c.CaseNumber, c.ApprovedAt));
        if (waiting.Count > 0)
        {
            var cash = await CashPositionAsync(groupId, null, 0m, ct);
            if (cash.Available is { } av && av < 0 || cash.Available is null)
                flags.Add(new("danger", "CASH_INSUFFICIENT", cash.Available is null
                        ? Loc.L($"Kesi {waiting.Count} zinasubiri malipo ({waiting.Sum(c => c.NetAmount):N0}) lakini hakuna salio la M-Koba lililoingizwa.")
                        : Loc.L($"Salio la M-Koba {cash.MkobaBalance:N0} ({cash.AsOf:dd/MM/yyyy}) halitoshi kesi zinazosubiri malipo ({cash.Reserved:N0})."),
                    null, null, cash.AsOf, A(("balance", cash.MkobaBalance ?? 0), ("reserved", cash.Reserved))));
        }

        // 13. Kumbukumbu za mkononi baada ya tarehe ya mwisho (Injini inazikataa — lakini zilijaribiwa)
        var cutoff = await _context.TruthGroupSettings.AsNoTracking().Where(s => s.GroupId == groupId).Select(s => s.ManualEntryCutoff).FirstOrDefaultAsync(ct);
        var lateManual = await _context.FinancialEvents.AsNoTracking()
            .Where(e => e.GroupId == groupId && e.Source == EventSource.Manual && (cutoff == null || e.OccurredAt > cutoff.Value.AddDays(1)))
            .CountAsync(ct);
        if (lateManual > 0)
            flags.Add(new("warn", "MANUAL_AFTER_CUTOFF", cutoff is null
                ? Loc.L($"Kumbukumbu {lateManual} za mkononi zipo lakini kikundi hakijaweka tarehe ya mwisho ya kumbukumbu za mkononi (zinakataliwa na Injini ya Ukweli).")
                : Loc.L($"Kumbukumbu {lateManual} za mkononi zina tarehe baada ya {cutoff:dd/MM/yyyy} (tarehe ya mwisho) — Injini ya Ukweli inazikataa; M-Koba ndiyo ukweli."),
                null, null, null, A(("count", lateManual))));

        // 14. Utoaji usioelezwa + malipo yaliyochelewa (kutoka tahadhari za uwazi)
        foreach (var a in (await AlertsAsync(groupId, ct)).Where(a => a.Code is "UNEXPLAINED_WITHDRAWALS" or "PAYMENT_NOT_CONFIRMED"))
            flags.Add(new(a.Level, a.Code, a.Message, null, null, null, a.Args, a.Tpl, a.TArgs));

        // 15. Ulinganisho wa maandishi ya fedha (B6)
        var rec = await ReconciliationAsync(groupId, userId, ct);
        var recJson = JsonSerializer.SerializeToElement(rec, Json);
        foreach (var r in recJson.GetProperty("rows").EnumerateArray().Where(r => !r.GetProperty("ok").GetBoolean()))
        {
            var failedChecks = r.GetProperty("checks").EnumerateArray().Where(x => !x.GetProperty("passed").GetBoolean()).ToList();
            var failed = string.Join(" ", failedChecks.Select(x => x.GetProperty("message").GetString()));
            var first = failedChecks.FirstOrDefault();
            string? ftpl = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("tpl", out var ft) && ft.ValueKind == JsonValueKind.String ? ft.GetString() : null;
            string[]? fargs = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("tArgs", out var fa) && fa.ValueKind == JsonValueKind.Array
                ? fa.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : null;
            var caseNo = r.GetProperty("caseNumber").GetString() ?? "?";
            flags.Add(new("danger", "LEDGER_MISMATCH", failedChecks.Count == 1 ? PrefixLoc(caseNo, ftpl, fargs, failed) : Loc.Of("{0}: {1}", new[] { caseNo, failed }, $"{caseNo}: {failed}"),
                r.GetProperty("id").GetGuid(), caseNo, null));
        }

        var order = new Dictionary<string, int> { ["danger"] = 0, ["warn"] = 1, ["info"] = 2 };
        return new
        {
            generatedAt = DateTime.UtcNow,
            audit = new { chain.Ok, chain.Sealed, chain.Unsealed, chain.Message, chain.Tpl, chain.TArgs, sealedNow },
            counts = new { danger = flags.Count(f => f.Severity == "danger"), warn = flags.Count(f => f.Severity == "warn"), info = flags.Count(f => f.Severity == "info") },
            flags = flags.OrderBy(f => order.GetValueOrDefault(f.Severity, 3)).ThenByDescending(f => f.At).ToList()
        };
    }

    /// <summary>Idadi ya juu ya majaribio yaliyozuiwa yanayoonyeshwa (ya karibuni) — si sheria ya fedha.</summary>
    private const int RiskBlockedDisplayLimit = 100;

    public async Task<AuditChain.VerifyResult> VerifyAuditChainAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderOrAuditorAsync(groupId, userId, ct);
        await AuditChain.SealAsync(_context, groupId, ct);
        return await AuditChain.VerifyAsync(_context, groupId, ct);
    }
}
