using System.Text.RegularExpressions;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Localization;
using ChamaLink.Infrastructure.Services.Truth;
using ChamaLink.Infrastructure.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Benefits;

// ============================================================================
//  SASISHO14 — MAPENGO 12 YA MAFAO
//   1  Pesa inahakikiwa wakati wa idhini (AwaitingFunds + "kagua pesa tena")
//   2  Mgongano wa maslahi: ndugu wa aliyefungua kesi / aliyeanzisha malipo + aina za uhusiano kwenye sera
//   3  Nafasi: wapiga kura (ApproverRoles), Mhakiki wa Ustahiki (EvidenceVerifierRoles), Wakaguzi (AuditorMemberIds)
//   4  Malipo makubwa: aliyeidhinisha ≠ mweka-malipo
//   5  Mhusika yule yule: DUPLICATE bila kujali uhusiano; kuzuia kati ya wanachama (sera)
//   6  Skrini ya mweka-malipo: namba ya mwanachama + simu
//   7  Deni la mkopo limetenganishwa (DeductLoanArrears, MAFAO-MKOPO-)
//   8  Malipo ya taslimu ya zamani (maker-checker + ushahidi + tarehe ya mwisho)
//   9  Sera ina tarehe ya kuanza (EffectiveFrom) — tukio linatumia sera ya siku yake
//   10 Kiwango kina tarehe ya mwisho; tarehe ya kuhesabu wanachama (HeadcountAsOf)
//   11 Maandishi ya fedha yanaingia kwenye mnyororo wa ukaguzi (BenefitLedgerPosted)
//   12 Ujumbe wa seva unatafsirika (Loc: kiolezo + vigeu)
// ============================================================================
public partial class BenefitService
{
    // ---------------------------------------------------------------- vigeu vya sera
    public static List<GroupRole> DefaultApproverRoles(bool allowMembers)
    {
        var roles = NotificationService.LeaderRoles.ToList();
        if (allowMembers) roles.Add(GroupRole.Member);
        return roles;
    }

    public static IReadOnlyList<MemberRelationType> ParseRelationTypes(string? csv) => (csv ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var i) && Enum.IsDefined(typeof(MemberRelationType), i) ? (MemberRelationType?)i
            : Enum.TryParse<MemberRelationType>(x, out var r) ? r : null)
        .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

    public static IReadOnlyList<Guid> ParseIds(string? csv) => (csv ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();

    /// <summary>Wapiga kura: ApproverRoles; sera ya zamani (tupu) => viongozi (+ wanachama kama AllowMemberApprovers).</summary>
    public static IReadOnlyList<GroupRole> ApproverRolesOf(BenefitGovernance g)
    {
        var r = ParseRoles(g.ApproverRoles);
        return r.Count > 0 ? r : DefaultApproverRoles(g.AllowMemberApprovers);
    }

    /// <summary>Mhakiki wa Ustahiki: EvidenceVerifierRoles; sera ya zamani (tupu) => viongozi wote.</summary>
    public static IReadOnlyList<GroupRole> VerifierRolesOf(BenefitGovernance g)
    {
        var r = ParseRoles(g.EvidenceVerifierRoles);
        return r.Count > 0 ? r : NotificationService.LeaderRoles;
    }

    public static string HeadcountBasisName(HeadcountDateBasis b) => b switch
    {
        HeadcountDateBasis.ClaimDate => "siku dai lilipofunguliwa",
        HeadcountDateBasis.VerificationDate => "siku ya uhakiki",
        _ => "siku ya tukio"
    };

    // ---------------------------------------------------------------- makosa yenye orodha
    /// <summary>Kosa lenye ukaguzi uliofeli — kila ukaguzi unatafsiriwa peke yake kwenye frontend (Details).</summary>
    private static ConflictException FailedChecks(string prefix, IEnumerable<Check> failed)
    {
        var list = failed.ToList();
        var joined = string.Join(" ", list.Select(c => c.Message));
        return new ConflictException(Loc.Of(prefix + " {0}", new[] { joined }, prefix + " " + joined))
        {
            Details = list.Select(c => new LocMessage(c.Tpl ?? c.Message, c.TArgs ?? Array.Empty<string>())).ToList()
        };
    }

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>"{0}: " + kiolezo (vishika-nafasi vinasogezwa mbele kwa 1).</summary>
    private static Loc PrefixLoc(string prefix, string? tpl, string[]? args, string text)
    {
        if (string.IsNullOrEmpty(tpl)) return Loc.Of("{0}: {1}", new[] { prefix, text }, $"{prefix}: {text}");
        var shifted = Placeholder.Replace(tpl, m => "{" + (int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 1) + "}");
        return Loc.Of("{0}: " + shifted, new[] { prefix }.Concat(args ?? Array.Empty<string>()).ToArray(), $"{prefix}: {text}");
    }

    // ---------------------------------------------------------------- wakaguzi
    private async Task<bool> IsAuditorAsync(Guid groupId, Guid memberId, CancellationToken ct)
        => ParseIds((await GetActiveGovernanceAsync(groupId, ct))?.AuditorMemberIds).Contains(memberId);

    /// <summary>Viongozi AU wakaguzi walioteuliwa na sera (kusoma tu: daftari, ulinganisho, hatari, ukaguzi, pesa, uhusiano).</summary>
    public async Task<GroupMember> RequireLeaderOrAuditorAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var m = await RequireMemberAsync(groupId, userId, ct); // SASISHO17: uanachama hai kwa wote (kiongozi na mkaguzi)
        if (Common.MemberAccess.IsEffectiveLeader(m)) return m;
        if (await IsAuditorAsync(groupId, m.Id, ct)) return m;
        throw new ForbiddenException("Kitendo hiki ni cha viongozi au wakaguzi walioteuliwa kwenye sera ya mafao.");
    }

    // ---------------------------------------------------------------- 1. kusubiri pesa
    /// <summary>
    /// Kesi iliyoidhinishwa kwa kura lakini pesa haikutosha (AwaitingFunds): kiongozi yeyote anakagua upya baada ya
    /// kuingiza PDF mpya ya M-Koba. Pesa ikitosha -> Approved (inaendelea kuanzisha malipo). Haitoshi -> kosa (hakuna mabadiliko).
    /// </summary>
    public Task<BenefitCase> RecheckFundsAsync(Guid groupId, Guid caseId, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.CashCommitment, () => RecheckFundsCoreAsync(groupId, caseId, userId, ct), ct); // SASISHO17 (P0-3)

    private async Task<BenefitCase> RecheckFundsCoreAsync(Guid groupId, Guid caseId, Guid userId, CancellationToken ct)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Kesi haipo.");
        if (c.Status != BenefitCaseStatus.AwaitingFunds) throw new ConflictException("Kesi hii haisubiri pesa.");
        var cash = await CashPositionAsync(groupId, c.Id, c.NetAmount, ct);
        if (!cash.Sufficient) throw new ConflictException(CashShortReason(cash));
        var gov = await _context.BenefitGovernances.AsNoTracking().FirstAsync(g => g.Id == c.GovernanceId, ct);
        var before = CaseSnap(c);
        c.Status = BenefitCaseStatus.Approved;
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitFundsRechecked, nameof(BenefitCase), c.Id, before, CaseSnap(c), c.CaseNumber,
            $"Pesa inatosha: {cash.Available:N0} ≥ {c.NetAmount:N0}", ct);
        var yes = await _context.GovernanceVotes.CountAsync(v => v.SubjectType == SubjectBenefitCase && v.SubjectId == c.Id && v.Approve, ct);
        await NotifyApprovedAsync(c, gov, await MemberName(c.BeneficiaryGroupMemberId, ct), yes, ct);
        await _context.SaveChangesAsync(ct);
        return c;
    }

    // ---------------------------------------------------------------- 8. taslimu ya zamani
    /// <summary>Urefu wa juu wa maandishi ya taslimu (lazima ulingane na ApplicationDbContext) — si sheria ya fedha.</summary>
    private const int CashTextLength = 200, CashReasonLength = 500;

    private async Task<BenefitCase> RecordHistoricalCashAsync(Guid groupId, HistoricalCaseDto dto, GroupMember ben, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ExceptionReason)) throw new ValidationException("Eleza kwa nini malipo haya yalikuwa ya taslimu (nje ya M-Koba).");
        if (string.IsNullOrWhiteSpace(dto.CashRecipientName)) throw new ValidationException("Andika jina la aliyepokea pesa taslimu.");
        if (dto.CashPaidAt is null) throw new ValidationException("Weka tarehe pesa taslimu ilipotolewa.");
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(dto.CashRecipientPhone))
        {
            try { phone = MkobaPlatform.NormalizePhone(dto.CashRecipientPhone); }
            catch (ArgumentException) { throw new ValidationException($"Namba ya simu '{dto.CashRecipientPhone}' si sahihi."); }
        }
        var paidAt = dto.CashPaidAt.Value.Date;
        var cutoff = await _context.TruthGroupSettings.AsNoTracking().Where(s => s.GroupId == groupId).Select(s => s.ManualEntryCutoff).FirstOrDefaultAsync(ct);
        if (cutoff is null)
            throw new ConflictException("Kikundi hakijaweka tarehe ya mwisho ya kumbukumbu za mkononi (Ukweli → Mipangilio). Malipo ya taslimu ya zamani hayawezi kurekodiwa bila hiyo.");
        if (paidAt > cutoff.Value.Date)
            throw new ConflictException($"Malipo ya taslimu ya {paidAt:dd/MM/yyyy} ni baada ya tarehe ya mwisho ya kumbukumbu za mkononi ({cutoff.Value:dd/MM/yyyy}) — baada ya hapo M-Koba ndiyo ukweli. Kwa malipo ya sasa kwa namba nyingine tumia kesi ya kawaida (thibitisha kwa rejea ya M-Koba ukieleza mpokeaji).");
        var controlledFrom = await _context.BenefitGovernances.AsNoTracking().Where(g => g.GroupId == groupId && g.Status == ProposalStatus.Approved && g.DecidedAt != null)
            .OrderBy(g => g.DecidedAt).Select(g => g.DecidedAt).FirstOrDefaultAsync(ct);
        if (controlledFrom is { } cf && paidAt >= EatDate(cf))
            throw new ConflictException($"Malipo ya {paidAt:dd/MM/yyyy} ni baada ya mfumo kuanza kusimamia mafao ({EatDate(cf):dd/MM/yyyy}) — fungua kesi ya kawaida.");
        if (dto.EventDate.Date > paidAt) throw new ValidationException("Tarehe ya tukio haiwezi kuwa baada ya malipo.");
        var gross = dto.PerMemberAmount * dto.Headcount;
        var net = gross - dto.JoinFeeDeducted - dto.ArrearsDeducted - dto.LoanArrearsDeducted;
        if (net < 0) throw new ValidationException($"Makato ({dto.JoinFeeDeducted + dto.ArrearsDeducted + dto.LoanArrearsDeducted:N0}) ni zaidi ya jumla ({gross:N0}).");

        var checks = new List<Check>
        {
            new("HISTORICAL_CASH", true, $"Kesi ya kihistoria ya TASLIMU: {net:N0} kwa {dto.CashRecipientName.Trim()} tarehe {paidAt:dd/MM/yyyy} (kabla ya tarehe ya mwisho {cutoff.Value:dd/MM/yyyy}). Inahitaji ushahidi uliohakikiwa na kiongozi wa pili.")
        };
        var rate = await ActiveRateAsync(groupId, dto.Category, dto.Relationship, dto.EventDate, ct);
        if (rate is not null && rate.PerMemberAmount != dto.PerMemberAmount)
            checks.Add(new("RATE_DIFFERS", true, $"Tahadhari: kiwango kilichoidhinishwa siku hiyo ni {rate.PerMemberAmount:N0}, kesi hii ina {dto.PerMemberAmount:N0}."));
        if (dto.JoinFeeDeducted > 0)
        {
            var truthThen = await _truth.GetMemberTruthAsync(ben.Id, EndOfDay(paidAt).AddSeconds(-1), ct);
            if (dto.JoinFeeDeducted > truthThen.Statement.JoinFeeOutstanding)
                throw new ConflictException($"Kianzio kilichokatwa ({dto.JoinFeeDeducted:N0}) ni zaidi ya Kianzio alichokuwa anadaiwa siku hiyo ({truthThen.Statement.JoinFeeOutstanding:N0}).");
        }
        static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
        var c = new BenefitCase
        {
            Id = Guid.NewGuid(), GroupId = groupId, CaseNumber = await NextCaseNumberAsync(groupId, dto.EventDate.Year, ct), IsHistorical = true,
            Category = dto.Category, Relationship = dto.Relationship, BeneficiaryGroupMemberId = ben.Id, SubjectName = dto.SubjectName.Trim(),
            EventDate = DateOnlyUtc(dto.EventDate), Description = dto.Description?.Trim(), Status = BenefitCaseStatus.HistoricalPendingConfirmation,
            BenefitRateId = rate?.Id, PerMemberAmount = dto.PerMemberAmount, Headcount = dto.Headcount, GrossAmount = gross,
            JoinFeeDeducted = dto.JoinFeeDeducted, ArrearsDeducted = dto.ArrearsDeducted, LoanArrearsDeducted = dto.LoanArrearsDeducted, NetAmount = net,
            HeadcountRuleUsed = "Kihistoria — idadi imetajwa na viongozi (orodha ya majina haijulikani)",
            EligibilityJson = System.Text.Json.JsonSerializer.Serialize(checks, Json), PaidAt = DateOnlyUtc(paidAt), // Npgsql: timestamptz inahitaji Kind=Utc (tarehe ya ukutani, kama muda wa M-Koba)
            PaymentChannel = BenefitPaymentChannel.Cash, CashRecipientName = Cut(dto.CashRecipientName.Trim(), CashTextLength), CashRecipientPhone = phone,
            ExceptionReason = Cut(dto.ExceptionReason.Trim(), CashReasonLength), SnapshotAt = DateTime.UtcNow, CreatedByUserId = userId
        };
        _context.BenefitCases.Add(c);
        await Audit(groupId, ben.Id, userId, AuditAction.BenefitCaseOpened, nameof(BenefitCase), c.Id, null, CaseSnap(c), CashRef(c.CaseNumber),
            "Kesi ya kihistoria ya taslimu (inasubiri ushahidi + kiongozi wa pili)", ct);
        await _notify.NotifyLeadersAsync(groupId, "BenefitCase", $"Thibitisha kesi ya zamani (taslimu) {c.CaseNumber}",
            $"{CategoryName(c.Category)} ya {await MemberName(ben.Id, ct)}: {net:N0} taslimu kwa {c.CashRecipientName} ({paidAt:dd/MM/yyyy}). Pakia risiti; kiongozi mwingine ahakiki na athibitishe.",
            $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        return c;
    }

    /// <summary>Taslimu: angalau ushahidi 1 uliohakikiwa (na asiye aliyeupakia); anayethibitisha si aliyepakia wala aliyehakiki ushahidi.</summary>
    private async Task RequireCashEvidenceAsync(BenefitCase c, Guid userId, CancellationToken ct)
    {
        var ev = await _context.BenefitEvidence.AsNoTracking().Where(e => e.BenefitCaseId == c.Id).ToListAsync(ct);
        if (!ev.Any(e => e.Status == EvidenceStatus.Verified && e.VerifiedByUserId != e.UploadedByUserId))
            throw new ConflictException("Malipo ya taslimu yanahitaji ushahidi (mf. risiti) uliohakikiwa na kiongozi asiye aliyeupakia, kabla ya kuthibitishwa.");
        if (ev.Any(e => e.UploadedByUserId == userId))
            throw new ForbiddenException("Uliyepakia ushahidi wa malipo haya HUWEZI kuyathibitisha — kiongozi mwingine.");
        if (ev.Any(e => e.Status == EvidenceStatus.Verified && e.VerifiedByUserId == userId))
            throw new ForbiddenException("Uliyehakiki ushahidi wa malipo haya HUWEZI pia kuyathibitisha — mtu wa tatu athibitishe (mhakiki ≠ mthibitishaji).");
    }
}
