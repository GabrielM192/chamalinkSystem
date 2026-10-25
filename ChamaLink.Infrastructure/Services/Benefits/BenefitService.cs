using ChamaLink.Infrastructure.Services.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Localization;
using ChamaLink.Domain.Ledger;
using ChamaLink.Infrastructure.Services.Events;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Benefits;

/// <summary>
/// MAFAO (msiba/sherehe) — mtiririko kamili, bila mtu mmoja kuweza kutoa pesa:
///   1. Sera + viwango: kiongozi anapendekeza, kiongozi WA PILI anaidhinisha (hakuna default).
///   2. Kesi: kiongozi anafungua -> mfumo unakagua ustahiki (sababu zinaandikwa).
///   3. Ushahidi: cheti (PDF/JPG/PNG), SHA-256, namba ya cheti; kimoja hakitumiki mara mbili.
///      Anayehakiki ≠ aliyepakia ≠ mnufaika ≠ aliyefungua kesi.
///   4. Hesabu inafungwa (snapshot): kiwango × idadi (sheria ya sera) − makato = kiasi halisi.
///   5. Kura: angalau N (sera), mnufaika/aliyefungua/aliyepakia/aliyehakiki hawapigi kura.
///   6. Arifa kwa kila mwanachama (mgao wake) — wanachama wote ni walinzi.
///   7. Malipo: yanathibitishwa TU kwa muamala wa M-Koba wenye kiasi sawa kabisa.
///      Kila utoaji wa M-Koba usio na kesi/maelezo yaliyoidhinishwa = tahadhari nyekundu.
/// </summary>
public partial class BenefitService
{
    public const string SubjectBenefitCase = "BenefitCase";

    private readonly ApplicationDbContext _context;
    private readonly TruthLedgerService _truth;
    private readonly NotificationService _notify;
    private readonly IEvidenceStorage _storage;
    private readonly IConfiguration _config;
    // SASISHO18: kwa vikundi vya "kila tukio", idhini ya kesi ndiyo inayotengeneza
    // wajibu wa kila mwanachama na kukata mchango kwenye pochi yake.
    private readonly EventWalletService _eventWallet;

    public BenefitService(ApplicationDbContext context, TruthLedgerService truth, NotificationService notify,
        IEvidenceStorage storage, IConfiguration config, EventWalletService eventWallet)
    {
        _context = context;
        _truth = truth;
        _notify = notify;
        _storage = storage;
        _config = config;
        _eventWallet = eventWallet;
    }

    /// <summary>Kikomo cha usalama cha siku kwenye sera (miaka 10) — kinazuia makosa ya kuandika, si sheria ya kikundi.</summary>
    private const int MaxPolicyDays = 10 * 365;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Task Audit(Guid groupId, Guid? memberId, Guid actor, AuditAction action, string type, Guid id, object? before, object after, string? reference, string? reason, CancellationToken ct)
        => AuditTrail.AddAsync(_context, groupId, memberId, actor, action, type, id, before, after, reference, reason, ct);

    // SASISHO17 (P0-1): ruhusa zote zinapitia Common.MemberAccess (uanachama hai + nguvu za kiongozi zinazotumika)
    private Task<GroupMember> RequireLeaderAsync(Guid groupId, Guid userId, CancellationToken ct)
        => Common.MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct);

    private Task<GroupMember> RequireMemberAsync(Guid groupId, Guid userId, CancellationToken ct)
        => Common.MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);

    private static DateTime EatDate(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MkobaPlatform.ResolveTimeZone()).Date;
    private static DateTime EndOfDay(DateTime d) => DateTime.SpecifyKind(d.Date.Add(TruthPolicy.EndOfDay), DateTimeKind.Unspecified);
    private static DateTime DateOnlyUtc(DateTime d) => DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);

    // =====================================================================
    // 1. SERA YA UTAWALA (maker-checker, matoleo)
    // =====================================================================
    public sealed class GovernanceDto
    {
        public int? RequiredApprovals { get; set; }
        public int? MinLeaderApprovals { get; set; }
        public bool? AllowMemberApprovers { get; set; }
        public int? RejectionsToReject { get; set; }
        public int? VotingDays { get; set; }
        public BenefitHeadcountRule? HeadcountRule { get; set; }
        public int? HeadcountPaidWithinDays { get; set; }
        public bool? ExcludeBeneficiaryFromHeadcount { get; set; }
        public BenefitFundingSource? FundingSource { get; set; }
        public bool? DeductJoinFee { get; set; }
        public bool? DeductArrears { get; set; }
        public bool? RequireNoArrearsForEligibility { get; set; }
        public int? MinMembershipDays { get; set; }
        public int? ClaimWindowDays { get; set; }
        public int? PaymentConfirmationDays { get; set; }
        public int? StatusChangeApprovals { get; set; }
        public int? ExpulsionVotePercent { get; set; }
        // SASISHO13
        public bool? RequireJoinFeePaid { get; set; }
        /// <summary>0 = hakuna sharti; null = rithi toleo lililopo.</summary>
        public int? CooldownDays { get; set; }
        /// <summary>0 = bila kikomo; null = rithi.</summary>
        public decimal? MaxBenefitAmountPerPeriod { get; set; }
        public int? MaxBenefitPeriodDays { get; set; }
        public bool? RequireCashAvailability { get; set; }
        public List<GroupRole>? PaymentInitiatorRoles { get; set; }
        public List<GroupRole>? PaymentConfirmerRoles { get; set; }
        public bool? ApproverCannotInitiatePayment { get; set; }
        public bool? ExcludeRelatedParties { get; set; }
        /// <summary>0 = hakuna tahadhari; null = rithi.</summary>
        public int? FrequentClaimsFlagCount { get; set; }
        public int? RecentPolicyChangeFlagDays { get; set; }
        // SASISHO14
        public bool? CashCheckAtApproval { get; set; }
        public List<MemberRelationType>? ConflictRelationTypes { get; set; }
        public List<GroupRole>? ApproverRoles { get; set; }
        public List<GroupRole>? EvidenceVerifierRoles { get; set; }
        public List<Guid>? AuditorMemberIds { get; set; }
        /// <summary>0 = imezimwa; null = rithi.</summary>
        public decimal? HighValueThreshold { get; set; }
        public bool? BlockSameSubjectAcrossMembers { get; set; }
        public bool? DeductLoanArrears { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public HeadcountDateBasis? HeadcountAsOf { get; set; }
        public string? Note { get; set; }
    }

    public static IReadOnlyList<GroupRole> ParseRoles(string? csv) => (csv ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var i) && Enum.IsDefined(typeof(GroupRole), i) ? (GroupRole?)i : Enum.TryParse<GroupRole>(x, out var r) ? r : null)
        .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

    private static string RolesCsv(IEnumerable<GroupRole> roles) => string.Join(",", roles.Distinct().OrderBy(r => r).Select(r => ((int)r).ToString(CultureInfo.InvariantCulture)));

    /// <summary>0 => null (hakuna sharti), null => rithi thamani ya toleo lililopo.</summary>
    private static int? OptionalDays(int? v, int? cur) => v is null ? cur : v == 0 ? null : v;

    /// <summary>Sera inayotumika LEO (EAT) — iliyoidhinishwa ya karibuni yenye EffectiveFrom ≤ leo.</summary>
    public async Task<BenefitGovernance?> GetActiveGovernanceAsync(Guid groupId, CancellationToken ct = default)
        => await GovernanceForDateAsync(groupId, TruthLedgerService.NowEat().Date, ct);

    /// <summary>
    /// SASISHO14: sera iliyokuwa hai TAREHE hiyo (EffectiveFrom ≤ tarehe; ikilingana, iliyoamuliwa mwisho).
    /// Tukio la kabla ya sera ya kwanza linatumia sera ya kwanza (ndiyo iliyoweka utaratibu wa mfumo).
    /// </summary>
    public async Task<BenefitGovernance?> GovernanceForDateAsync(Guid groupId, DateTime date, CancellationToken ct = default)
    {
        var d = DateOnlyUtc(date);
        var q = _context.BenefitGovernances.AsNoTracking().Where(g => g.GroupId == groupId && g.Status == ProposalStatus.Approved);
        return await q.Where(g => g.EffectiveFrom <= d).OrderByDescending(g => g.EffectiveFrom).ThenByDescending(g => g.DecidedAt).FirstOrDefaultAsync(ct)
               ?? await q.OrderBy(g => g.EffectiveFrom).ThenBy(g => g.DecidedAt).FirstOrDefaultAsync(ct);
    }

    public async Task<BenefitGovernance> RequireGovernanceAsync(Guid groupId, CancellationToken ct = default)
        => await RequireGovernanceForDateAsync(groupId, TruthLedgerService.NowEat().Date, ct);

    public async Task<BenefitGovernance> RequireGovernanceForDateAsync(Guid groupId, DateTime date, CancellationToken ct = default)
        => await GovernanceForDateAsync(groupId, date, ct)
           ?? throw new PolicyNotConfiguredException(groupId,
               "Kikundi hakijaweka sera ya mafao na idhini (idadi ya kura, sheria ya idadi ya wanachama, makato n.k.). Kiongozi apendekeze kwenye Mafao → Sera, na kiongozi wa pili aidhinishe.");

    public async Task<BenefitGovernance> ProposeGovernanceAsync(Guid groupId, GovernanceDto dto, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        if (await _context.BenefitGovernances.AnyAsync(g => g.GroupId == groupId && g.Status == ProposalStatus.Pending, ct))
            throw new ConflictException("Kuna pendekezo la sera linalosubiri idhini. Liamuliwe kwanza (au aliyeliomba alighairi).");
        var cur = await GetActiveGovernanceAsync(groupId, ct);

        var missing = new List<Loc>();
        T Need<T>(T? v, T? fallback, Loc name) where T : struct
        {
            if (v is { } x) return x;
            if (fallback is { } f) return f;
            missing.Add(name);
            return default;
        }
        var g = new BenefitGovernance
        {
            Id = Guid.NewGuid(), GroupId = groupId, Status = ProposalStatus.Pending, RequestedByUserId = userId, Note = dto.Note?.Trim(),
            RequiredApprovals = Need(dto.RequiredApprovals, cur?.RequiredApprovals, "idadi ya kura za kuidhinisha (RequiredApprovals)"),
            MinLeaderApprovals = Need(dto.MinLeaderApprovals, cur?.MinLeaderApprovals, "kura za chini za viongozi (MinLeaderApprovals; 0 kama hakuna)"),
            AllowMemberApprovers = Need(dto.AllowMemberApprovers, cur?.AllowMemberApprovers, "wanachama wa kawaida wanapiga kura? (AllowMemberApprovers)"),
            RejectionsToReject = Need(dto.RejectionsToReject, cur?.RejectionsToReject, "kura za HAPANA zinazokataa kesi (RejectionsToReject)"),
            VotingDays = Need(dto.VotingDays, cur?.VotingDays, "siku za kupiga kura (VotingDays)"),
            HeadcountRule = Need(dto.HeadcountRule, cur?.HeadcountRule, "nani anahesabiwa kwenye idadi (HeadcountRule)"),
            HeadcountPaidWithinDays = dto.HeadcountPaidWithinDays ?? cur?.HeadcountPaidWithinDays,
            ExcludeBeneficiaryFromHeadcount = Need(dto.ExcludeBeneficiaryFromHeadcount, cur?.ExcludeBeneficiaryFromHeadcount, "mnufaika anaondolewa kwenye idadi? (ExcludeBeneficiaryFromHeadcount)"),
            FundingSource = Need(dto.FundingSource, cur?.FundingSource, "chanzo cha pesa (FundingSource)"),
            DeductJoinFee = Need(dto.DeductJoinFee, cur?.DeductJoinFee, "kata Kianzio kilichobaki? (DeductJoinFee)"),
            DeductArrears = Need(dto.DeductArrears, cur?.DeductArrears, "kata deni la michango? (DeductArrears)"),
            RequireNoArrearsForEligibility = Need(dto.RequireNoArrearsForEligibility, cur?.RequireNoArrearsForEligibility, "mnufaika asiwe na deni? (RequireNoArrearsForEligibility)"),
            MinMembershipDays = Need(dto.MinMembershipDays, cur?.MinMembershipDays, "siku za chini za uanachama (MinMembershipDays; 0 kama hakuna)"),
            ClaimWindowDays = Need(dto.ClaimWindowDays, cur?.ClaimWindowDays, "siku za kuwasilisha ombi baada ya tukio (ClaimWindowDays)"),
            PaymentConfirmationDays = Need(dto.PaymentConfirmationDays, cur?.PaymentConfirmationDays, "siku za malipo kuonekana M-Koba (PaymentConfirmationDays)"),
            StatusChangeApprovals = Need(dto.StatusChangeApprovals, cur?.StatusChangeApprovals, "idhini za viongozi kubadilisha hali ya mwanachama (StatusChangeApprovals)"),
            ExpulsionVotePercent = Need(dto.ExpulsionVotePercent, cur?.ExpulsionVotePercent, "asilimia ya kura kumfukuza mwanachama (ExpulsionVotePercent)"),
        };
        g.RequireJoinFeePaid = Need(dto.RequireJoinFeePaid, cur?.RequireJoinFeePaid, "Kianzio kiwe kimelipwa kikamilifu ili kustahili? (RequireJoinFeePaid)");
        g.RequireCashAvailability = Need(dto.RequireCashAvailability, cur?.RequireCashAvailability, "hakiki pesa ipo M-Koba kabla ya kuanzisha malipo? (RequireCashAvailability)");
        g.ApproverCannotInitiatePayment = Need(dto.ApproverCannotInitiatePayment, cur?.ApproverCannotInitiatePayment, "aliyeidhinisha kwa kura asianzishe malipo? (ApproverCannotInitiatePayment)");
        g.ExcludeRelatedParties = Need(dto.ExcludeRelatedParties, cur?.ExcludeRelatedParties, "ndugu wa mnufaika wasishiriki kuidhinisha/kulipa? (ExcludeRelatedParties)");
        var initRoles = dto.PaymentInitiatorRoles is not null ? dto.PaymentInitiatorRoles.Distinct().ToList() : ParseRoles(cur?.PaymentInitiatorRoles).ToList();
        var confRoles = dto.PaymentConfirmerRoles is not null ? dto.PaymentConfirmerRoles.Distinct().ToList() : ParseRoles(cur?.PaymentConfirmerRoles).ToList();
        if (initRoles.Count == 0) missing.Add("nani ANAANZISHA malipo (PaymentInitiatorRoles, mf. Mtunza-hazina)");
        if (confRoles.Count == 0) missing.Add("nani ANATHIBITISHA malipo kwa rejea ya M-Koba (PaymentConfirmerRoles)");
        g.PaymentInitiatorRoles = RolesCsv(initRoles);
        g.PaymentConfirmerRoles = RolesCsv(confRoles);
        g.CooldownDays = OptionalDays(dto.CooldownDays, cur?.CooldownDays);
        g.MaxBenefitAmountPerPeriod = dto.MaxBenefitAmountPerPeriod is null ? cur?.MaxBenefitAmountPerPeriod : dto.MaxBenefitAmountPerPeriod == 0 ? null : dto.MaxBenefitAmountPerPeriod;
        g.MaxBenefitPeriodDays = g.MaxBenefitAmountPerPeriod is null ? null : OptionalDays(dto.MaxBenefitPeriodDays, cur?.MaxBenefitPeriodDays);
        g.FrequentClaimsFlagCount = OptionalDays(dto.FrequentClaimsFlagCount, cur?.FrequentClaimsFlagCount);
        g.RecentPolicyChangeFlagDays = OptionalDays(dto.RecentPolicyChangeFlagDays, cur?.RecentPolicyChangeFlagDays);
        // ---- SASISHO14 ----
        var today = TruthLedgerService.NowEat().Date;
        var anyApproved = await _context.BenefitGovernances.AnyAsync(x => x.GroupId == groupId && x.Status == ProposalStatus.Approved, ct);
        g.EffectiveFrom = DateOnlyUtc(dto.EffectiveFrom ?? today);
        g.HeadcountAsOf = dto.HeadcountAsOf ?? cur?.HeadcountAsOf ?? HeadcountDateBasis.EventDate;
        g.CashCheckAtApproval = dto.CashCheckAtApproval ?? cur?.CashCheckAtApproval ?? false;
        g.BlockSameSubjectAcrossMembers = dto.BlockSameSubjectAcrossMembers ?? cur?.BlockSameSubjectAcrossMembers ?? false;
        // Deni la mkopo lilikuwa sehemu ya DeductArrears kabla ya SASISHO14 — chaguo-msingi linafuata DeductArrears
        g.DeductLoanArrears = dto.DeductLoanArrears ?? cur?.DeductLoanArrears ?? g.DeductArrears;
        g.HighValueThreshold = dto.HighValueThreshold is null ? cur?.HighValueThreshold : dto.HighValueThreshold == 0 ? null : dto.HighValueThreshold;
        var approverRoles = dto.ApproverRoles is not null ? dto.ApproverRoles.Distinct().ToList()
            : dto.AllowMemberApprovers is not null || cur is null || string.IsNullOrEmpty(cur.ApproverRoles) ? DefaultApproverRoles(g.AllowMemberApprovers)
            : ParseRoles(cur.ApproverRoles).ToList();
        g.ApproverRoles = RolesCsv(approverRoles);
        g.AllowMemberApprovers = approverRoles.Contains(GroupRole.Member);
        var verifierRoles = dto.EvidenceVerifierRoles is not null ? dto.EvidenceVerifierRoles.Distinct().ToList()
            : cur is not null && !string.IsNullOrEmpty(cur.EvidenceVerifierRoles) ? ParseRoles(cur.EvidenceVerifierRoles).ToList() : NotificationService.LeaderRoles.ToList();
        g.EvidenceVerifierRoles = RolesCsv(verifierRoles);
        var relTypes = dto.ConflictRelationTypes is not null ? dto.ConflictRelationTypes.Distinct().ToList()
            : cur is not null && !string.IsNullOrEmpty(cur.ConflictRelationTypes) ? ParseRelationTypes(cur.ConflictRelationTypes).ToList() : Enum.GetValues<MemberRelationType>().ToList();
        g.ConflictRelationTypes = string.Join(",", relTypes.OrderBy(x => x).Select(x => ((int)x).ToString(CultureInfo.InvariantCulture)));
        var auditors = dto.AuditorMemberIds is not null ? dto.AuditorMemberIds.Distinct().ToList() : ParseIds(cur?.AuditorMemberIds).ToList();
        g.AuditorMemberIds = string.Join(",", auditors);
        if (missing.Count > 0)
            throw new ValidationException(Loc.Of("Sera haijakamilika. Weka: {0}.", new[] { string.Join("; ", missing.Select(m => m.Text)) },
                "Sera haijakamilika. Weka: " + string.Join("; ", missing.Select(m => m.Text)) + ".")) { Details = missing.Select(m => m.ToMessage()).ToList() };

        var errs = new List<Loc>();
        if (g.RequiredApprovals < 2) errs.Add("Kura za kuidhinisha ziwe angalau 2 (mtu mmoja hawezi kutoa pesa).");
        if (g.MinLeaderApprovals < 0 || g.MinLeaderApprovals > g.RequiredApprovals) errs.Add("Kura za viongozi ziwe kati ya 0 na idadi ya kura zote.");
        if (!g.AllowMemberApprovers && g.MinLeaderApprovals < g.RequiredApprovals && g.MinLeaderApprovals != 0)
            errs.Add("Wanachama wa kawaida hawapigi kura: kwa hiyo kura zote ni za viongozi (MinLeaderApprovals iwe 0 au sawa na RequiredApprovals).");
        if (g.RejectionsToReject < 1) errs.Add("Kura za HAPANA ziwe angalau 1.");
        if (g.VotingDays is < 1 or > 90) errs.Add("Siku za kura 1..90.");
        if (g.HeadcountRule == BenefitHeadcountRule.MembersPaidWithinDays && g.HeadcountPaidWithinDays is not (>= 1 and <= 366))
            errs.Add("Sheria 'waliolipa ndani ya siku N' inahitaji N (1..366).");
        if (g.HeadcountRule != BenefitHeadcountRule.MembersPaidWithinDays) g.HeadcountPaidWithinDays = null;
        if (g.FundingSource == BenefitFundingSource.NewCollection)
            errs.Add("Mchango mpya kwa kila tukio (harambee) bado haujatekelezwa kwenye Injini ya Ukweli — chagua 'Mfuko wa kikundi'.");
        if (g.MinMembershipDays < 0 || g.MinMembershipDays > MaxPolicyDays) errs.Add($"Siku za uanachama 0..{MaxPolicyDays}.");
        if (g.ClaimWindowDays < 1 || g.ClaimWindowDays > MaxPolicyDays) errs.Add($"Siku za kuwasilisha ombi 1..{MaxPolicyDays}.");
        if (g.PaymentConfirmationDays is < 1 or > 90) errs.Add("Siku za malipo kuonekana M-Koba 1..90.");
        if (g.StatusChangeApprovals < 1) errs.Add("Kubadilisha hali ya mwanachama kunahitaji angalau kiongozi 1 zaidi ya aliyeomba.");
        if (g.ExpulsionVotePercent is < 50 or > 100) errs.Add("Asilimia ya kufukuza iwe 50..100 (Katiba: 75).");
        if (initRoles.Concat(confRoles).Any(r => !NotificationService.LeaderRoles.Contains(r)))
            errs.Add("Kuanzisha na kuthibitisha malipo ni kazi za viongozi (Mwenyekiti, Mtunza-hazina, Katibu).");
        if (g.RequireJoinFeePaid && g.DeductJoinFee)
            errs.Add("Chagua moja: 'Kianzio kiwe kimelipwa ili kustahili' AU 'kata Kianzio kilichobaki kwenye mafao' — si vyote viwili.");
        if (g.CooldownDays is < 1 or > MaxPolicyDays) errs.Add($"Siku za kusubiri kati ya madai 1..{MaxPolicyDays} (0 = hakuna sharti).");
        if (g.MaxBenefitAmountPerPeriod is < 0) errs.Add("Kiasi cha juu cha mafao hakiwezi kuwa hasi.");
        if (g.MaxBenefitAmountPerPeriod is not null && g.MaxBenefitPeriodDays is not (>= 1 and <= MaxPolicyDays))
            errs.Add($"Kiasi cha juu cha mafao kinahitaji kipindi (siku 1..{MaxPolicyDays}), mf. 365.");
        if (g.FrequentClaimsFlagCount is < 1) errs.Add("Tahadhari ya madai mengi iwe angalau 1 (0 = hakuna).");
        if (g.RecentPolicyChangeFlagDays is < 1 or > MaxPolicyDays) errs.Add($"Siku za tahadhari ya sera iliyobadilika 1..{MaxPolicyDays} (0 = hakuna).");
        if (anyApproved && g.EffectiveFrom < DateOnlyUtc(today))
            errs.Add($"Sera mpya haiwezi kuanza tarehe iliyopita ({g.EffectiveFrom:dd/MM/yyyy}) — sera za zamani hazibadilishwi. Chagua leo ({today:dd/MM/yyyy}) au baadaye.");
        if (approverRoles.Count == 0) errs.Add("Chagua angalau nafasi moja inayopiga kura ya kuidhinisha (ApproverRoles).");
        if (g.MinLeaderApprovals > 0 && !approverRoles.Any(r => NotificationService.LeaderRoles.Contains(r)))
            errs.Add("Sera inataka kura za viongozi lakini hakuna nafasi ya kiongozi kwenye wapiga kura.");
        if (verifierRoles.Count == 0) errs.Add("Chagua angalau nafasi moja ya Mhakiki wa Ustahiki (EvidenceVerifierRoles).");
        if (verifierRoles.Any(r => !NotificationService.LeaderRoles.Contains(r)))
            errs.Add("Mhakiki wa ushahidi lazima awe kiongozi (vyeti ni siri — vinaonwa na viongozi na mnufaika tu).");
        if (g.ExcludeRelatedParties && relTypes.Count == 0)
            errs.Add("Mgongano wa maslahi umewashwa: chagua angalau aina moja ya uhusiano (ConflictRelationTypes).");
        if (relTypes.Any(t => !Enum.IsDefined(t))) errs.Add("Aina ya uhusiano si sahihi.");
        if (g.HighValueThreshold is <= 0) errs.Add("Kiasi cha 'malipo makubwa' kiwe zaidi ya 0 (0 = imezimwa).");
        if (!Enum.IsDefined(g.HeadcountAsOf)) errs.Add("Tarehe ya kuhesabu wanachama si sahihi.");
        if (g.CashCheckAtApproval && !g.RequireCashAvailability)
            errs.Add("Kuhakiki pesa wakati wa kuidhinisha kunahitaji 'hakiki pesa ipo M-Koba' (RequireCashAvailability) iwashwe.");
        if (auditors.Count > 0)
        {
            var okIds = await _context.GroupMembers.Where(m => m.GroupId == groupId && auditors.Contains(m.Id) && NotificationService.InGroupStatuses.Contains(m.Status))
                .Select(m => m.Id).ToListAsync(ct);
            if (okIds.Count != auditors.Count) errs.Add("Kila mkaguzi lazima awe mwanachama aliye kikundini.");
        }
        if (errs.Count > 0)
            throw new ValidationException(Loc.Of("{0}", new[] { string.Join(" ", errs.Select(e => e.Text)) }, string.Join(" ", errs.Select(e => e.Text))))
                { Details = errs.Select(e => e.ToMessage()).ToList() };
        g.Version = (await _context.BenefitGovernances.Where(x => x.GroupId == groupId).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;

        _context.BenefitGovernances.Add(g);
        await Audit(groupId, null, userId, AuditAction.GroupPolicyCreated, nameof(BenefitGovernance), g.Id, cur is null ? null : GovSnap(cur), GovSnap(g), null,
            "Pendekezo la sera ya mafao (linasubiri kiongozi wa pili)", ct);
        await _notify.NotifyLeadersAsync(groupId, "PolicyProposal", "Pendekezo la sera ya mafao", "Kiongozi amependekeza sera mpya ya mafao/idhini. Ihakiki na uidhinishe au ukatae.", "/mafao?tab=sera", ct);
        await _context.SaveChangesAsync(ct);
        return g;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<BenefitGovernance> DecideGovernanceAsync(Guid groupId, Guid proposalId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideGovernanceCoreAsync(groupId, proposalId, approve, note, userId, ct), ct);

    private async Task<BenefitGovernance> DecideGovernanceCoreAsync(Guid groupId, Guid proposalId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        var g = await _context.BenefitGovernances.FirstOrDefaultAsync(x => x.Id == proposalId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Pendekezo halipo.");
        if (g.Status != ProposalStatus.Pending) throw new ConflictException($"Pendekezo tayari ni {g.Status}.");
        if (g.RequestedByUserId == userId) throw new ForbiddenException("Aliyependekeza HAWEZI kuidhinisha mwenyewe (maker-checker).");
        // SASISHO14: pendekezo lililoidhinishwa kwa kuchelewa haliwezi kuanza nyuma (sera za zamani hazibadiliki)
        var todayUtc = DateOnlyUtc(TruthLedgerService.NowEat().Date);
        if (approve && g.EffectiveFrom < todayUtc && await _context.BenefitGovernances.AnyAsync(x => x.GroupId == groupId && x.Status == ProposalStatus.Approved, ct))
            g.EffectiveFrom = todayUtc;
        g.Status = approve ? ProposalStatus.Approved : ProposalStatus.Rejected;
        g.DecidedByUserId = userId;
        g.DecidedAt = DateTime.UtcNow;
        await Audit(groupId, null, userId, approve ? AuditAction.GroupPolicyVersioned : AuditAction.GovernanceRejected, nameof(BenefitGovernance), g.Id,
            null, GovSnap(g), null, note, ct);
        await _context.SaveChangesAsync(ct);
        return g;
    }

    public async Task CancelGovernanceProposalAsync(Guid groupId, Guid proposalId, Guid userId, CancellationToken ct = default)
    {
        var g = await _context.BenefitGovernances.FirstOrDefaultAsync(x => x.Id == proposalId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Pendekezo halipo.");
        if (g.Status != ProposalStatus.Pending || g.RequestedByUserId != userId) throw new ForbiddenException("Unaweza kughairi pendekezo lako linalosubiri tu.");
        g.Status = ProposalStatus.Rejected; g.DecidedByUserId = userId; g.DecidedAt = DateTime.UtcNow;
        await Audit(groupId, null, userId, AuditAction.GovernanceRejected, nameof(BenefitGovernance), g.Id, null, GovSnap(g), null, "Limeghairiwa na aliyelipendekeza", ct);
        await _context.SaveChangesAsync(ct);
    }

    private static object GovSnap(BenefitGovernance g) => new
    {
        g.RequiredApprovals, g.MinLeaderApprovals, g.AllowMemberApprovers, g.RejectionsToReject, g.VotingDays,
        HeadcountRule = g.HeadcountRule.ToString(), g.HeadcountPaidWithinDays, g.ExcludeBeneficiaryFromHeadcount,
        FundingSource = g.FundingSource.ToString(), g.DeductJoinFee, g.DeductArrears, g.RequireNoArrearsForEligibility,
        g.MinMembershipDays, g.ClaimWindowDays, g.PaymentConfirmationDays, g.StatusChangeApprovals, g.ExpulsionVotePercent,
        g.Version, g.RequireJoinFeePaid, g.CooldownDays, g.MaxBenefitAmountPerPeriod, g.MaxBenefitPeriodDays, g.RequireCashAvailability,
        g.PaymentInitiatorRoles, g.PaymentConfirmerRoles, g.ApproverCannotInitiatePayment, g.ExcludeRelatedParties,
        g.FrequentClaimsFlagCount, g.RecentPolicyChangeFlagDays,
        g.CashCheckAtApproval, g.ConflictRelationTypes, g.ApproverRoles, g.EvidenceVerifierRoles, g.AuditorMemberIds, g.HighValueThreshold,
        g.BlockSameSubjectAcrossMembers, g.DeductLoanArrears, g.EffectiveFrom, HeadcountAsOf = g.HeadcountAsOf.ToString(),
        Status = g.Status.ToString()
    };

    public static string DescribeHeadcount(BenefitGovernance g) => HeadcountRuleText(g) + HeadcountSuffix(g);
    public static string HeadcountSuffix(BenefitGovernance g) => g.ExcludeBeneficiaryFromHeadcount ? " (bila mnufaika)" : " (pamoja na mnufaika)";
    public static string HeadcountRuleText(BenefitGovernance g) => g.HeadcountRule switch
    {
        BenefitHeadcountRule.AllActiveMembers => "Wanachama wote walio kikundini siku ya tukio",
        BenefitHeadcountRule.MembersWithNoArrears => "Wanachama walio kikundini wasio na deni la michango siku ya tukio",
        BenefitHeadcountRule.MembersPaidWithinDays => $"Wanachama walio kikundini waliolipa ndani ya siku {g.HeadcountPaidWithinDays} kabla ya tukio",
        _ => g.HeadcountRule.ToString()
    };

    // =====================================================================
    // 2. VIWANGO VYA MAFAO (kwa kila mwanachama; maker-checker; matoleo kwa tarehe)
    // =====================================================================
    public sealed class RateDto
    {
        public BenefitCategory Category { get; set; }
        public BenefitRelationship Relationship { get; set; }
        public decimal? PerMemberAmount { get; set; }
        public List<EvidenceDocumentType>? AcceptedEvidence { get; set; }
        public int? MaxClaimsPerMember { get; set; }
        public bool IsDisabled { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        /// <summary>SASISHO14: tarehe ya mwisho (pamoja nayo). null = hakina mwisho.</summary>
        public DateTime? EffectiveTo { get; set; }
        public string? Note { get; set; }
    }

    public async Task<BenefitRate> ProposeRateAsync(Guid groupId, RateDto dto, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        if (!Enum.IsDefined(dto.Category) || !Enum.IsDefined(dto.Relationship)) throw new ValidationException("Aina ya tukio au uhusiano si sahihi.");
        if (dto.EffectiveFrom is null) throw new ValidationException("Weka tarehe ya kuanza kutumika (EffectiveFrom).");
        if (dto.EffectiveTo is { } eto && eto.Date < dto.EffectiveFrom.Value.Date)
            throw new ValidationException($"Tarehe ya mwisho ({eto:dd/MM/yyyy}) haiwezi kuwa kabla ya tarehe ya kuanza ({dto.EffectiveFrom.Value:dd/MM/yyyy}).");
        if (!dto.IsDisabled)
        {
            if (dto.PerMemberAmount is not > 0) throw new ValidationException("Kiwango kwa kila mwanachama kiwe zaidi ya 0.");
            if (dto.PerMemberAmount % 1 != 0) throw new ValidationException("Kiwango kiwe namba kamili.");
            if (dto.AcceptedEvidence is not { Count: > 0 } || dto.AcceptedEvidence.Any(e => !Enum.IsDefined(e)))
                throw new ValidationException("Chagua angalau aina moja ya ushahidi unaokubalika (mf. cheti cha kifo, kibali cha mazishi).");
            if (dto.MaxClaimsPerMember is < 1) throw new ValidationException("Kikomo cha madai kiwe angalau 1 (au acha wazi = bila kikomo).");
        }
        if (await _context.BenefitRates.AnyAsync(r => r.GroupId == groupId && r.Status == ProposalStatus.Pending
                                                      && r.Category == dto.Category && r.Relationship == dto.Relationship, ct))
            throw new ConflictException("Tayari kuna pendekezo la kiwango hiki linalosubiri idhini.");
        var r = new BenefitRate
        {
            Id = Guid.NewGuid(), GroupId = groupId, Category = dto.Category, Relationship = dto.Relationship,
            PerMemberAmount = dto.IsDisabled ? 0 : dto.PerMemberAmount!.Value,
            AcceptedEvidence = dto.IsDisabled ? string.Empty : string.Join(",", dto.AcceptedEvidence!.Distinct()),
            MaxClaimsPerMember = dto.IsDisabled ? null : dto.MaxClaimsPerMember, IsDisabled = dto.IsDisabled,
            EffectiveFrom = DateOnlyUtc(dto.EffectiveFrom.Value), EffectiveTo = dto.EffectiveTo is { } to ? DateOnlyUtc(to) : null, Status = ProposalStatus.Pending, RequestedByUserId = userId, Note = dto.Note?.Trim()
        };
        _context.BenefitRates.Add(r);
        await Audit(groupId, null, userId, AuditAction.GroupPolicyCreated, nameof(BenefitRate), r.Id, null, RateSnap(r), null, "Pendekezo la kiwango cha mafao", ct);
        await _notify.NotifyLeadersAsync(groupId, "PolicyProposal", "Pendekezo la kiwango cha mafao",
            $"{CategoryName(r.Category)} ({RelationshipName(r.Relationship)}): {(r.IsDisabled ? "hakuna mafao" : $"{r.PerMemberAmount:N0} kwa kila mwanachama")} kuanzia {r.EffectiveFrom:dd/MM/yyyy}. Ihakiki.",
            "/mafao?tab=sera", ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<BenefitRate> DecideRateAsync(Guid groupId, Guid rateId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideRateCoreAsync(groupId, rateId, approve, note, userId, ct), ct);

    private async Task<BenefitRate> DecideRateCoreAsync(Guid groupId, Guid rateId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        var r = await _context.BenefitRates.FirstOrDefaultAsync(x => x.Id == rateId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Pendekezo halipo.");
        if (r.Status != ProposalStatus.Pending) throw new ConflictException($"Pendekezo tayari ni {r.Status}.");
        if (r.RequestedByUserId == userId) throw new ForbiddenException("Aliyependekeza HAWEZI kuidhinisha mwenyewe (maker-checker).");
        r.Status = approve ? ProposalStatus.Approved : ProposalStatus.Rejected;
        r.DecidedByUserId = userId; r.DecidedAt = DateTime.UtcNow;
        await Audit(groupId, null, userId, approve ? AuditAction.GroupPolicyVersioned : AuditAction.GovernanceRejected, nameof(BenefitRate), r.Id, null, RateSnap(r), null, note, ct);
        await _context.SaveChangesAsync(ct);
        return r;
    }

    public async Task CancelRateProposalAsync(Guid groupId, Guid rateId, Guid userId, CancellationToken ct = default)
    {
        var r = await _context.BenefitRates.FirstOrDefaultAsync(x => x.Id == rateId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Pendekezo halipo.");
        if (r.Status != ProposalStatus.Pending || r.RequestedByUserId != userId) throw new ForbiddenException("Unaweza kughairi pendekezo lako linalosubiri tu.");
        r.Status = ProposalStatus.Rejected; r.DecidedByUserId = userId; r.DecidedAt = DateTime.UtcNow;
        await Audit(groupId, null, userId, AuditAction.GovernanceRejected, nameof(BenefitRate), r.Id, null, RateSnap(r), null, "Limeghairiwa na aliyelipendekeza", ct);
        await _context.SaveChangesAsync(ct);
    }

    private static object RateSnap(BenefitRate r) => new
    {
        Category = r.Category.ToString(), Relationship = r.Relationship.ToString(), r.PerMemberAmount, r.AcceptedEvidence,
        r.MaxClaimsPerMember, r.IsDisabled, r.EffectiveFrom, r.EffectiveTo, Status = r.Status.ToString()
    };

    public async Task<BenefitRate?> ActiveRateAsync(Guid groupId, BenefitCategory cat, BenefitRelationship rel, DateTime date, CancellationToken ct)
    {
        var r = await _context.BenefitRates.AsNoTracking()
            .Where(x => x.GroupId == groupId && x.Status == ProposalStatus.Approved && x.Category == cat && x.Relationship == rel && x.EffectiveFrom <= DateOnlyUtc(date)
                        && (x.EffectiveTo == null || x.EffectiveTo >= DateOnlyUtc(date)))
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
        return r is null || r.IsDisabled ? null : r;
    }

    public static IReadOnlyList<EvidenceDocumentType> ParseEvidence(string csv) => csv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => Enum.TryParse<EvidenceDocumentType>(x, out var e) ? (EvidenceDocumentType?)e : null)
        .Where(x => x is not null).Select(x => x!.Value).ToList();

    // =====================================================================
    // 3. USTAHIKI + HESABU
    // =====================================================================
    /// <summary>Tpl/Args (SASISHO14) = kiolezo kinachotafsirika; Message = Kiswahili kamili (fallback).</summary>
    public sealed record Check
    {
        [System.Text.Json.Serialization.JsonConstructor]
        public Check(string code, bool passed, string message, string? tpl = null, string[]? tArgs = null)
        {
            Code = code; Passed = passed; Message = message; Tpl = tpl; TArgs = tArgs;
        }
        public Check(string code, bool passed, Loc message) : this(code, passed, message.Text, message.Template, message.Args.ToArray()) { }
        public string Code { get; init; }
        public bool Passed { get; init; }
        public string Message { get; init; }
        public string? Tpl { get; init; }
        public string[]? TArgs { get; init; }
    }

    public sealed record Assessment(
        List<Check> Checks, BenefitRate? Rate, BenefitGovernance Governance,
        decimal PerMemberAmount, int Headcount, List<Guid> CountedMemberIds,
        decimal Gross, decimal JoinFeeDeducted, decimal ArrearsDeducted, decimal Net, string HeadcountRule, decimal LoanArrearsDeducted = 0m,
        DateTime? HeadcountDate = null)
    {
        public bool Eligible => Checks.All(c => c.Passed);
    }

    public sealed class OpenCaseDto
    {
        public BenefitCategory Category { get; set; }
        public BenefitRelationship Relationship { get; set; }
        public Guid BeneficiaryMemberId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string? Description { get; set; }
    }

    private static string Norm(string s) => string.Join(' ', s.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <param name="claimDate">Tarehe dai lilipofunguliwa (kwa HeadcountAsOf = ClaimDate). null = leo.</param>
    public async Task<Assessment> AssessAsync(Guid groupId, OpenCaseDto dto, Guid? excludeCaseId = null, CancellationToken ct = default, DateTime? claimDate = null)
    {
        // SASISHO14: sera iliyokuwa hai SIKU YA TUKIO (si ya leo)
        var gov = await RequireGovernanceForDateAsync(groupId, dto.EventDate.Date, ct);
        if (!Enum.IsDefined(dto.Category) || !Enum.IsDefined(dto.Relationship)) throw new ValidationException("Aina ya tukio au uhusiano si sahihi.");
        if (string.IsNullOrWhiteSpace(dto.SubjectName)) throw new ValidationException("Andika jina la mhusika wa tukio (mf. jina la marehemu / mtoto / mwenza).");
        var beneficiary = await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.BeneficiaryMemberId && m.GroupId == groupId, ct)
            ?? throw new NotFoundException("Mnufaika si mwanachama wa kikundi hiki.");

        var checks = new List<Check>();
        var eventDate = dto.EventDate.Date;
        var today = TruthLedgerService.NowEat().Date;
        checks.Add(new("EVENT_DATE", eventDate <= today, eventDate <= today ? Loc.L($"Tarehe ya tukio {eventDate:dd/MM/yyyy}.") : Loc.L("Tarehe ya tukio haiwezi kuwa ya baadaye.")));
        var days = (today - eventDate).Days;
        checks.Add(new("CLAIM_WINDOW", days <= gov.ClaimWindowDays,
            days <= gov.ClaimWindowDays ? Loc.L($"Ombi ndani ya muda (siku {days} kati ya {gov.ClaimWindowDays}).")
                                        : Loc.L($"Ombi limechelewa: siku {days} tangu tukio; sera inaruhusu siku {gov.ClaimWindowDays}.")));

        var rate = await ActiveRateAsync(groupId, dto.Category, dto.Relationship, eventDate, ct);
        checks.Add(new("RATE", rate is not null, rate is not null
            ? Loc.L($"Kiwango: {rate.PerMemberAmount:N0} kwa kila mwanachama ({CategoryName(dto.Category)}, {RelationshipName(dto.Relationship)}).")
            : Loc.L($"Hakuna kiwango kilichoidhinishwa cha {CategoryName(dto.Category)} ({RelationshipName(dto.Relationship)}) kwa tarehe hiyo. Weka kwenye Mafao → Sera.")));

        bool inGroup = NotificationService.InGroupStatuses.Contains(beneficiary.Status)
                       || (beneficiary.Status == MemberStatus.Deceased && dto.Category == BenefitCategory.Msiba && dto.Relationship == BenefitRelationship.Self);
        checks.Add(new("BENEFICIARY_STATUS", inGroup, inGroup ? Loc.L("Mnufaika yuko kikundini.") : Loc.L($"Mnufaika hali yake ni {beneficiary.Status} — hastahili mafao.")));

        // Ukweli wa wanachama WOTE siku ya tukio (mara moja) — kwa ustahiki na idadi
        var asOf = EndOfDay(eventDate);
        var all = await _truth.GetGroupTruthAsync(groupId, asOf, ct);
        var me = all.FirstOrDefault(t => t.MemberId == beneficiary.Id);
        if (me is not null)
        {
            var need = eventDate.AddDays(-gov.MinMembershipDays);
            var okJoin = me.EffectiveJoin.Date <= need;
            checks.Add(new("MIN_MEMBERSHIP", okJoin, gov.MinMembershipDays == 0 ? "Hakuna sharti la muda wa uanachama."
                : okJoin ? Loc.L($"Uanachama tangu {me.EffectiveJoin:dd/MM/yyyy} (sharti: siku {gov.MinMembershipDays}).")
                         : Loc.L($"Uanachama tangu {me.EffectiveJoin:dd/MM/yyyy} — bado hajafikisha siku {gov.MinMembershipDays}.")));
            if (gov.RequireNoArrearsForEligibility)
            {
                var arrears = me.Statement.Arrears;
                checks.Add(new("NO_ARREARS", arrears == 0, arrears == 0 ? Loc.L("Hakuwa na deni la michango siku ya tukio (Kanuni 5(d)).")
                    : Loc.L($"Alikuwa na deni la michango {arrears:N0} siku ya tukio ({string.Join(", ", me.Statement.MonthsInArrears.Select(p => p.Label))}). Kanuni 5(d).")));
            }
            if (gov.RequireJoinFeePaid)
            {
                var jf = me.Statement.JoinFeeOutstanding;
                checks.Add(new("JOIN_FEE_PAID", jf == 0, jf == 0 ? Loc.L("Kianzio kilikuwa kimelipwa kikamilifu siku ya tukio.")
                    : Loc.L($"Kianzio kilichobaki siku ya tukio: {jf:N0}. Sera inataka Kianzio kilipwe kikamilifu kwanza.")));
            }
        }

        var sameCategory = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.BeneficiaryGroupMemberId == beneficiary.Id && c.Id != excludeCaseId
                        && c.Status != BenefitCaseStatus.Rejected && c.Status != BenefitCaseStatus.Cancelled
                        && c.Category == dto.Category)
            .ToListAsync(ct);
        var existing = sameCategory.Where(c => c.Relationship == dto.Relationship).ToList();
        if (rate?.MaxClaimsPerMember is { } max)
            checks.Add(new("MAX_CLAIMS", existing.Count < max, existing.Count < max ? Loc.L($"Madai {existing.Count} kati ya {max} yanayoruhusiwa.")
                : Loc.L($"Tayari amepata madai {existing.Count} ya aina hii (kikomo {max}).")));
        // SASISHO14: mnufaika yule yule + aina ile ile + mhusika yule yule = tukio lile lile, HATA uhusiano ukiandikwa tofauti
        var dup = sameCategory.FirstOrDefault(c => Norm(c.SubjectName) == Norm(dto.SubjectName));
        checks.Add(new("DUPLICATE", dup is null, dup is null ? Loc.L("Hakuna dai lingine la tukio hili.") : Loc.L($"Tukio hili tayari lina kesi {dup.CaseNumber}.")));
        await AddAntiProfitChecksAsync(checks, groupId, gov, beneficiary.Id, dto, excludeCaseId, ct);

        // ---- Idadi ya wanaohesabiwa (sheria ya sera) ----
        // SASISHO14: tarehe ya kuhesabu inatoka kwenye sera (tukio / dai / uhakiki). Ustahiki unabaki siku ya tukio.
        var headDate = gov.HeadcountAsOf switch
        {
            HeadcountDateBasis.ClaimDate => (claimDate ?? today).Date,
            HeadcountDateBasis.VerificationDate => today,
            _ => eventDate
        };
        if (headDate > today) headDate = today;
        if (headDate < eventDate) headDate = eventDate;
        if (headDate != eventDate)
        {
            asOf = EndOfDay(headDate);
            all = await _truth.GetGroupTruthAsync(groupId, asOf, ct);
        }
        var counted = new List<Guid>();
        var memberRows = await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).Select(m => new { m.Id, m.Status }).ToListAsync(ct);
        foreach (var t in all)
        {
            var row = memberRows.First(r => r.Id == t.MemberId);
            var active = NotificationService.InGroupStatuses.Contains(row.Status) || (t.ObligationsEndAt is { } end && end > asOf);
            if (!active || t.EffectiveJoin > asOf) continue;
            if (gov.ExcludeBeneficiaryFromHeadcount && t.MemberId == beneficiary.Id) continue;
            var ok = gov.HeadcountRule switch
            {
                BenefitHeadcountRule.AllActiveMembers => true,
                BenefitHeadcountRule.MembersWithNoArrears => t.Statement.Arrears == 0,
                BenefitHeadcountRule.MembersPaidWithinDays => t.Statement.Allocations.Any(a =>
                    a.Timestamp <= asOf && a.Timestamp >= headDate.AddDays(-gov.HeadcountPaidWithinDays!.Value)
                    && a.Amount > 0 && a.Reference != "AKIBA" && !a.Reference.StartsWith("WAIVER-", StringComparison.Ordinal)
                    && !a.Reference.StartsWith("MAFAO-", StringComparison.Ordinal)),
                _ => false
            };
            if (ok) counted.Add(t.MemberId);
        }
        checks.Add(new("HEADCOUNT", counted.Count > 0, $"Wanaohesabiwa: {counted.Count} — {HeadcountRuleText(gov)}{HeadcountSuffix(gov)}. Tarehe ya kuhesabu: {headDate:dd/MM/yyyy} ({HeadcountBasisName(gov.HeadcountAsOf)})."));

        var per = rate?.PerMemberAmount ?? 0m;
        var gross = per * counted.Count;
        // Makato yanatumia deni la SASA la mnufaika (siku ya kulipa), si la siku ya tukio
        var nowTruth = await _truth.GetMemberTruthAsync(beneficiary.Id, null, ct);
        // SASISHO14: Kianzio -> deni la michango -> deni la mkopo (kila moja swichi yake ya sera)
        var ded = WelfareDeductionRule.ComputeSplit(gross, nowTruth.Statement, gov.DeductJoinFee, gov.DeductArrears, gov.DeductLoanArrears);
        await AddPeriodLimitCheckAsync(checks, groupId, gov, beneficiary.Id, eventDate, gross, excludeCaseId, ct);
        return new Assessment(checks, rate, gov, per, counted.Count, counted, gross, ded.JoinFeeDeducted, ded.ArrearsDeducted, ded.NetPayout,
            DescribeHeadcount(gov) + $" — {HeadcountBasisName(gov.HeadcountAsOf)} {headDate:dd/MM/yyyy}", ded.LoanArrearsDeducted, headDate);
    }

    private async Task<string> NextCaseNumberAsync(Guid groupId, int year, CancellationToken ct)
    {
        var code = await _context.Groups.Where(g => g.Id == groupId).Select(g => g.ShortCode).FirstOrDefaultAsync(ct);
        var prefix = $"{(string.IsNullOrWhiteSpace(code) ? "GRP" : code)}-MF-{year}-";
        var nums = await _context.BenefitCases.Where(c => c.GroupId == groupId && c.CaseNumber.StartsWith(prefix)).Select(c => c.CaseNumber).ToListAsync(ct);
        var max = nums.Select(n => int.TryParse(n[prefix.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:000}";
    }

    // =====================================================================
    // 4. KESI
    // =====================================================================
    /// <summary>
    /// Kiongozi anafungua kesi ya mwanachama yeyote; mwanachama wa kawaida anaomba kwa AJILI YAKE MWENYEWE tu
    /// (msiba/sherehe yake). Hatua zinazofuata ni zile zile: ushahidi → uhakiki wa kiongozi → kura → M-Koba.
    /// </summary>
    public async Task<GroupMember> RequireCanOpenForAsync(Guid groupId, Guid beneficiaryMemberId, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireMemberAsync(groupId, userId, ct); // SASISHO17: hai tu (viongozi pia)
        if (Common.MemberAccess.IsEffectiveLeader(actor)) return actor;
        if (actor.Id != beneficiaryMemberId)
            throw new ForbiddenException("Mwanachama anaweza kuomba mafao kwa ajili yake mwenyewe tu. Kwa mwanachama mwingine, mwombe kiongozi.");
        if (!NotificationService.InGroupStatuses.Contains(actor.Status))
            throw new ForbiddenException("Hali yako ya uanachama hairuhusu kuomba mafao. Wasiliana na viongozi.");
        return actor;
    }

    public async Task<BenefitCase> OpenCaseAsync(Guid groupId, OpenCaseDto dto, Guid userId, CancellationToken ct = default)
    {
        var opener = await RequireCanOpenForAsync(groupId, dto.BeneficiaryMemberId, userId, ct);
        var byMember = !Common.MemberAccess.IsEffectiveLeader(opener);
        // SASISHO15: kufuli ya kikundi — maombi mawili ya wakati mmoja hayawezi kupita ukaguzi wa DUPLICATE/COOLDOWN/MAX
        // pamoja, wala kupata namba ya kesi inayofanana. Ukaguzi + kuhifadhi viko ndani ya transaction moja.
        await using var tx = await DbTx.BeginAsync(_context, ct);
        await DbTx.LockAsync(_context, groupId, DbTx.BenefitCaseOpen, ct);
        var a = await AssessAsync(groupId, dto, null, ct);
        if (!a.Eligible) throw FailedChecks("Kesi haiwezi kufunguliwa:", a.Checks.Where(c => !c.Passed));

        var c = new BenefitCase
        {
            Id = Guid.NewGuid(), GroupId = groupId, CaseNumber = await NextCaseNumberAsync(groupId, dto.EventDate.Year, ct),
            Category = dto.Category, Relationship = dto.Relationship, BeneficiaryGroupMemberId = dto.BeneficiaryMemberId,
            SubjectName = dto.SubjectName.Trim(), EventDate = DateOnlyUtc(dto.EventDate), Description = dto.Description?.Trim(),
            Status = BenefitCaseStatus.AwaitingEvidence, CreatedByUserId = userId,
            BenefitRateId = a.Rate?.Id, GovernanceId = a.Governance.Id, PolicyVersion = a.Governance.Version,
            PerMemberAmount = a.PerMemberAmount, Headcount = a.Headcount, GrossAmount = a.Gross,
            JoinFeeDeducted = a.JoinFeeDeducted, ArrearsDeducted = a.ArrearsDeducted, LoanArrearsDeducted = a.LoanArrearsDeducted, NetAmount = a.Net,
            HeadcountRuleUsed = a.HeadcountRule, EligibilityJson = JsonSerializer.Serialize(a.Checks, Json)
        };
        _context.BenefitCases.Add(c);
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitCaseOpened, nameof(BenefitCase), c.Id, null, CaseSnap(c), c.CaseNumber,
            $"{CategoryName(c.Category)}: {c.SubjectName}", ct);
        var who = await MemberName(c.BeneficiaryGroupMemberId, ct);
        await _notify.NotifyLeadersAsync(groupId, "BenefitCase", byMember ? Loc.L($"Ombi jipya la mafao {c.CaseNumber}") : Loc.L($"Kesi mpya {c.CaseNumber}"),
            byMember
                ? Loc.L($"{who} ameomba {CategoryName(c.Category)} ({RelationshipName(c.Relationship)}): {c.SubjectName}. Hakiki cheti chake.")
                : Loc.L($"{CategoryName(c.Category)} ({RelationshipName(c.Relationship)}) ya {who}: {c.SubjectName}. Pakia na uhakiki ushahidi."), $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        await DbTx.CommitAsync(tx, ct);
        return c;
    }

    public async Task<BenefitEvidence> UploadEvidenceAsync(Guid groupId, Guid caseId, EvidenceDocumentType type, string documentNumber, string fileName,
        byte[] content, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireMemberAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.Include(x => x.Evidence).FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Kesi haipo.");
        var isLeader = Common.MemberAccess.IsEffectiveLeader(actor);
        if (!isLeader && actor.Id != c.BeneficiaryGroupMemberId) throw new ForbiddenException("Ushahidi unapakiwa na viongozi au mnufaika mwenyewe.");
        if (c.Status is BenefitCaseStatus.Rejected or BenefitCaseStatus.Cancelled) throw new ConflictException("Kesi imefungwa.");
        if (!c.IsHistorical && c.Status is not (BenefitCaseStatus.AwaitingEvidence or BenefitCaseStatus.AwaitingVerification))
            throw new ConflictException("Ushahidi unapakiwa kabla ya kura kuanza.");
        // Snapshot lock: kesi ya kihistoria ikishathibitishwa (Paid) haipokei ushahidi mpya
        if (c.IsHistorical && c.Status != BenefitCaseStatus.HistoricalPendingConfirmation)
            throw new ConflictException("Kesi ya kihistoria imeshathibitishwa — haipokei ushahidi mpya.");
        if (!Enum.IsDefined(type)) throw new ValidationException("Aina ya ushahidi si sahihi.");
        if (string.IsNullOrWhiteSpace(documentNumber)) throw new ValidationException("Andika namba ya cheti/kibali/barua (kama ilivyo kwenye hati).");
        if (content.Length == 0) throw new ValidationException("Faili ni tupu.");
        var max = EvidenceFileRules.MaxBytes(_config);
        if (content.Length > max) throw new ValidationException($"Faili ni kubwa mno ({content.Length / 1024:N0} KB). Kikomo ni {max / 1024:N0} KB.");
        var kind = EvidenceFileRules.Detect(content);
        var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var docNo = Norm(documentNumber);

        var sameFile = await _context.BenefitEvidence.AsNoTracking().Where(e => e.GroupId == groupId && e.Sha256 == sha && e.Status != EvidenceStatus.Rejected)
            .Select(e => e.BenefitCaseId).FirstOrDefaultAsync(ct);
        if (sameFile != Guid.Empty)
            await RiskBlockedAsync(groupId, c, userId, "DUPLICATE_FILE", $"Faili hili tayari limetumika kwenye kesi {await CaseNumber(sameFile, ct)}. Cheti kimoja hakiwezi kudai mafao mara mbili.", ct);
        if (sameFile != Guid.Empty)
            throw new ConflictException($"Faili hili tayari limetumika kwenye kesi {await CaseNumber(sameFile, ct)}. Cheti kimoja hakiwezi kudai mafao mara mbili.");
        var sameDoc = await _context.BenefitEvidence.AsNoTracking().Where(e => e.GroupId == groupId && e.DocumentType == type && e.DocumentNumber == docNo && e.Status != EvidenceStatus.Rejected)
            .Select(e => e.BenefitCaseId).FirstOrDefaultAsync(ct);
        if (sameDoc != Guid.Empty)
            await RiskBlockedAsync(groupId, c, userId, "DUPLICATE_DOCUMENT", $"{EvidenceName(type)} namba {docNo} tayari imetumika kwenye kesi {await CaseNumber(sameDoc, ct)}.", ct);
        if (sameDoc != Guid.Empty)
            throw new ConflictException($"{EvidenceName(type)} namba {docNo} tayari imetumika kwenye kesi {await CaseNumber(sameDoc, ct)}.");

        var key = await _storage.SaveAsync(groupId, sha, kind.Extension, content, ct);
        var e = new BenefitEvidence
        {
            Id = Guid.NewGuid(), GroupId = groupId, BenefitCaseId = c.Id, DocumentType = type, DocumentNumber = docNo,
            FileName = Path.GetFileName(fileName).Length > 200 ? Path.GetFileName(fileName)[..200] : Path.GetFileName(fileName),
            ContentType = kind.ContentType, SizeBytes = content.Length, Sha256 = sha, StorageKey = key, UploadedByUserId = userId
        };
        _context.BenefitEvidence.Add(e);
        if (c.Status == BenefitCaseStatus.AwaitingEvidence) c.Status = BenefitCaseStatus.AwaitingVerification;
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.EvidenceUploaded, nameof(BenefitEvidence), e.Id, null,
            new { e.DocumentType, e.DocumentNumber, e.Sha256, e.SizeBytes, e.ContentType }, c.CaseNumber, null, ct);
        await _notify.NotifyLeadersAsync(groupId, "Evidence", $"Ushahidi mpya: {c.CaseNumber}",
            $"{EvidenceName(type)} namba {docNo} imepakiwa. Kiongozi mwingine (si aliyepakia) aihakiki.", $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        return e;
    }

    public async Task<BenefitCase> VerifyEvidenceAsync(Guid groupId, Guid evidenceId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => (await VerifyEvidenceWithWarningAsync(groupId, evidenceId, approve, note, userId, ct)).Case;

    /// <summary>
    /// SASISHO15 (F-L2): kama cheti kilichohakikiwa SI aina inayokubalika kwa kiwango cha kesi (mf. cheti cha ndoa kwenye msiba),
    /// uhakiki unahifadhiwa lakini kesi haiendi kwenye kura — na mhakiki anaambiwa wazi kwa nini, badala ya kesi kukaa kimya.
    /// </summary>
    public async Task<(BenefitCase Case, Loc? Warning)> VerifyEvidenceWithWarningAsync(Guid groupId, Guid evidenceId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(groupId, userId, ct);
        var e = await _context.BenefitEvidence.FirstOrDefaultAsync(x => x.Id == evidenceId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Ushahidi haupo.");
        var c = await _context.BenefitCases.Include(x => x.Evidence).FirstAsync(x => x.Id == e.BenefitCaseId, ct);
        if (e.Status != EvidenceStatus.Uploaded) throw new ConflictException($"Ushahidi huu tayari ni {e.Status}.");
        if (e.UploadedByUserId == userId) throw new ForbiddenException("Aliyepakia ushahidi HAWEZI kuuhakiki.");
        if (c.CreatedByUserId == userId) throw new ForbiddenException("Aliyefungua kesi HAWEZI kuhakiki ushahidi wake — kiongozi mwingine.");
        if (actor.Id == c.BeneficiaryGroupMemberId) throw new ForbiddenException("Mnufaika hawezi kuhakiki ushahidi wa kesi yake.");
        var vGov = await CaseGovernanceAsync(c, ct);
        if (vGov is not null && !VerifierRolesOf(vGov).Contains(Common.MemberAccess.EffectiveRole(actor)))
            throw new ForbiddenException($"Sera (toleo {vGov.Version}): ushahidi unahakikiwa na Mhakiki wa Ustahiki ({string.Join(", ", VerifierRolesOf(vGov).Select(RoleName))}) tu.");
        if (await IsConflictedAsync(c, actor.Id, ct))
            throw new ForbiddenException("Wewe ni ndugu/mhusika wa karibu wa mnufaika au wa aliyefungua kesi (mgongano wa maslahi) — kiongozi mwingine ahakiki ushahidi.");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new ValidationException("Eleza kwa nini ushahidi umekataliwa.");

        e.Status = approve ? EvidenceStatus.Verified : EvidenceStatus.Rejected;
        e.VerifiedByUserId = userId; e.VerifiedAt = DateTime.UtcNow; e.ReviewNote = note?.Trim();
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, approve ? AuditAction.EvidenceVerified : AuditAction.EvidenceRejected, nameof(BenefitEvidence), e.Id,
            null, new { e.DocumentType, e.DocumentNumber, e.Sha256, Status = e.Status.ToString() }, c.CaseNumber, note, ct);

        Loc? warning = null;
        if (!c.IsHistorical && c.Status == BenefitCaseStatus.AwaitingVerification)
        {
            var accepted = c.BenefitRateId is { } rid
                ? ParseEvidence((await _context.BenefitRates.AsNoTracking().FirstAsync(r => r.Id == rid, ct)).AcceptedEvidence)
                : Array.Empty<EvidenceDocumentType>();
            var hasAccepted = c.Evidence.Any(x => x.Status == EvidenceStatus.Verified && accepted.Contains(x.DocumentType));
            if (approve && !accepted.Contains(e.DocumentType))
            {
                var list = string.Join(", ", accepted.Select(EvidenceName));
                warning = Loc.L($"Umehakiki {EvidenceName(e.DocumentType)}, lakini kiwango cha kesi {c.CaseNumber} kinakubali: {list}. Kesi haitaenda kwenye kura mpaka hati inayokubalika ipakiwe na kuhakikiwa.");
                await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.RiskBlocked, nameof(BenefitEvidence), e.Id, null,
                    new { Code = "EVIDENCE_TYPE_NOT_ACCEPTED", e.DocumentType, Accepted = accepted }, c.CaseNumber, warning.Value.Text, ct);
            }
            if (hasAccepted) await StartVotingAsync(c, ct);
            else if (c.Evidence.All(x => x.Status == EvidenceStatus.Rejected)) c.Status = BenefitCaseStatus.AwaitingEvidence;
        }
        await _context.SaveChangesAsync(ct);
        return (c, warning);
    }

    private async Task<List<GroupMember>> EligibleVotersAsync(BenefitCase c, BenefitGovernance gov, CancellationToken ct)
    {
        // SASISHO15 (F-M2): soma ushahidi KAMA ULIVYO SASA (pamoja na uhakiki ambao bado haujahifadhiwa kwenye ombi hili).
        // Zamani AsNoTracking ilisoma DB pekee, hivyo mhakiki wa sasa alihesabiwa kama mpiga kura — kesi ikaingia Voting
        // bila wapiga kura wa kutosha na kukwama milele.
        var tracked = await _context.BenefitEvidence.Where(e => e.BenefitCaseId == c.Id).ToListAsync(ct);
        var excludedUsers = tracked.Concat(_context.BenefitEvidence.Local.Where(e => e.BenefitCaseId == c.Id))
            .Distinct().Select(e => new { e.UploadedByUserId, e.VerifiedByUserId }).ToList();
        var ex = excludedUsers.Select(x => x.UploadedByUserId)
            .Concat(excludedUsers.Where(x => x.VerifiedByUserId != null).Select(x => x.VerifiedByUserId!.Value))
            .Append(c.CreatedByUserId).ToHashSet();
        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == c.GroupId && NotificationService.InGroupStatuses.Contains(m.Status) && m.Id != c.BeneficiaryGroupMemberId)
            .ToListAsync(ct);
        var related = await ConflictedMemberIdsAsync(c, gov, includeInitiator: false, ct);
        var roles = ApproverRolesOf(gov);
        return members.Where(m => !ex.Contains(m.UserId) && !related.Contains(m.Id) && roles.Contains(Common.MemberAccess.EffectiveRole(m))).ToList();
    }

    private async Task StartVotingAsync(BenefitCase c, CancellationToken ct)
    {
        // Hesabu inafanywa upya na KUFUNGWA sasa (ushahidi umethibitishwa) — haibadiliki tena baada ya hapa
        var a = await AssessAsync(c.GroupId, new OpenCaseDto
        {
            Category = c.Category, Relationship = c.Relationship, BeneficiaryMemberId = c.BeneficiaryGroupMemberId,
            SubjectName = c.SubjectName, EventDate = c.EventDate, Description = c.Description
        }, c.Id, ct, EatDate(c.CreatedAt));
        // Muda wa kuwasilisha ulihakikiwa kesi ilipofunguliwa; hauhesabiwi tena hapa
        var failed = a.Checks.Where(x => !x.Passed && x.Code != "CLAIM_WINDOW").ToList();
        if (failed.Count > 0) throw FailedChecks("Ushahidi umethibitishwa lakini kura haziwezi kuanza:", failed);
        var voters = await EligibleVotersAsync(c, a.Governance, ct);
        var leaders = voters.Count(v => Common.MemberAccess.IsEffectiveLeader(v));
        if (voters.Count < a.Governance.RequiredApprovals || leaders < a.Governance.MinLeaderApprovals)
            throw new ConflictException($"Hakuna wapiga kura wa kutosha: wanaoruhusiwa {voters.Count} (viongozi {leaders}); sera inahitaji kura {a.Governance.RequiredApprovals} (za viongozi angalau {a.Governance.MinLeaderApprovals}). Aliyefungua kesi, aliyepakia/aliyehakiki ushahidi, mnufaika na ndugu zake (na wa aliyefungua) waliosajiliwa hawapigi kura.");

        c.GovernanceId = a.Governance.Id; c.BenefitRateId = a.Rate?.Id; c.PolicyVersion = a.Governance.Version;
        c.PerMemberAmount = a.PerMemberAmount; c.Headcount = a.Headcount; c.GrossAmount = a.Gross;
        c.JoinFeeDeducted = a.JoinFeeDeducted; c.ArrearsDeducted = a.ArrearsDeducted; c.LoanArrearsDeducted = a.LoanArrearsDeducted; c.NetAmount = a.Net;
        c.HeadcountRuleUsed = a.HeadcountRule; c.CountedMemberIdsJson = JsonSerializer.Serialize(a.CountedMemberIds, Json);
        c.EligibilityJson = JsonSerializer.Serialize(a.Checks, Json);
        c.SnapshotAt = DateTime.UtcNow;
        c.VotingDeadline = DateTime.UtcNow.AddDays(a.Governance.VotingDays);
        c.Status = BenefitCaseStatus.Voting;

        var who = await MemberName(c.BeneficiaryGroupMemberId, ct);
        Loc title = $"{CategoryName(c.Category)}: {c.SubjectName}"; /* Loc (si string) ili kichwa kitafsiriwe */
        var ded = c.GrossAmount - c.NetAmount;
        var cat = CategoryName(c.Category); var rel = RelationshipName(c.Relationship);
        await _notify.NotifyAsync(c.GroupId, a.CountedMemberIds, "BenefitShare", title, ded > 0
                ? Loc.L($"{cat} ({rel}) ya {who}. Mgao wa kila mwanachama: {c.PerMemberAmount:N0} × {c.Headcount} = {c.GrossAmount:N0}; makato {ded:N0}; atalipwa {c.NetAmount:N0}. Mgao wako: {c.PerMemberAmount:N0} kutoka mfuko wa kikundi.")
                : Loc.L($"{cat} ({rel}) ya {who}. Mgao wa kila mwanachama: {c.PerMemberAmount:N0} × {c.Headcount} = {c.GrossAmount:N0}. Mgao wako: {c.PerMemberAmount:N0} kutoka mfuko wa kikundi."),
            $"/mafao?kesi={c.Id}", ct);
        await _notify.NotifyAsync(c.GroupId, voters.Select(v => v.Id), "VoteNeeded", $"Kura inahitajika: {c.CaseNumber}", ded > 0
                ? Loc.L($"{cat} ({rel}) ya {who}. Mgao wa kila mwanachama: {c.PerMemberAmount:N0} × {c.Headcount} = {c.GrossAmount:N0}; makato {ded:N0}; atalipwa {c.NetAmount:N0}. Piga kura kabla ya {c.VotingDeadline:dd/MM/yyyy}.")
                : Loc.L($"{cat} ({rel}) ya {who}. Mgao wa kila mwanachama: {c.PerMemberAmount:N0} × {c.Headcount} = {c.GrossAmount:N0}. Piga kura kabla ya {c.VotingDeadline:dd/MM/yyyy}."),
            $"/mafao?kesi={c.Id}", ct);
    }

    public sealed record VoteResult(BenefitCase Case, int Approvals, int Rejections, int Required);

    /// <summary>SASISHO17 (P0-3): kura zinapangwa mstari (Decision) na idhini inayoshikilia pesa inapita kufuli ya pesa (CashCommitment).</summary>
    public Task<VoteResult> VoteAsync(Guid groupId, Guid caseId, bool approve, string? reason, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, new[] { DbTx.Decision, DbTx.CashCommitment }, () => VoteCoreAsync(groupId, caseId, approve, reason, userId, ct), ct);

    private async Task<VoteResult> VoteCoreAsync(Guid groupId, Guid caseId, bool approve, string? reason, Guid userId, CancellationToken ct)
    {
        var voter = await RequireMemberAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Kesi haipo.");
        if (c.Status != BenefitCaseStatus.Voting) throw new ConflictException("Kesi hii haiko kwenye kura.");
        if (c.VotingDeadline is { } dl && DateTime.UtcNow > dl) throw new ConflictException("Muda wa kura umeisha. Viongozi waighairi kesi na kuifungua upya ikihitajika.");
        var gov = await _context.BenefitGovernances.AsNoTracking().FirstAsync(g => g.Id == c.GovernanceId, ct);
        var voters = await EligibleVotersAsync(c, gov, ct);
        if (voters.All(v => v.Id != voter.Id))
            throw new ForbiddenException("Huruhusiwi kupiga kura kwenye kesi hii (mnufaika au ndugu yake, aliyefungua kesi, aliyepakia/aliyehakiki ushahidi, au sera inaruhusu viongozi tu).");
        if (await _context.GovernanceVotes.AnyAsync(v => v.SubjectType == SubjectBenefitCase && v.SubjectId == c.Id && v.GroupMemberId == voter.Id, ct))
            throw new ConflictException("Tayari umepiga kura kwenye kesi hii.");
        if (!approve && string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Kura ya HAPANA inahitaji sababu.");

        var vote = new GovernanceVote
        {
            Id = Guid.NewGuid(), GroupId = groupId, SubjectType = SubjectBenefitCase, SubjectId = c.Id, GroupMemberId = voter.Id, UserId = userId,
            Approve = approve, Reason = reason?.Trim(), RoleAtVote = Common.MemberAccess.EffectiveRole(voter)
        };
        _context.GovernanceVotes.Add(vote);
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.VoteCast, nameof(GovernanceVote), vote.Id, null,
            new { vote.Approve, vote.Reason, Role = vote.RoleAtVote.ToString() }, c.CaseNumber, reason, ct);

        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && v.SubjectId == c.Id).ToListAsync(ct);
        votes.Add(vote);
        var yes = votes.Count(v => v.Approve);
        var no = votes.Count(v => !v.Approve);
        var leaderYes = votes.Count(v => v.Approve && NotificationService.LeaderRoles.Contains(v.RoleAtVote));
        var who = await MemberName(c.BeneficiaryGroupMemberId, ct);

        if (no >= gov.RejectionsToReject)
        {
            c.Status = BenefitCaseStatus.Rejected; c.ClosedAt = DateTime.UtcNow; c.ClosedReason = "Imekataliwa kwa kura: " + string.Join("; ", votes.Where(v => !v.Approve).Select(v => v.Reason));
            await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitRejected, nameof(BenefitCase), c.Id, null, CaseSnap(c), c.CaseNumber, c.ClosedReason, ct);
            await _notify.NotifyAllMembersAsync(groupId, "BenefitRejected", $"{c.CaseNumber} imekataliwa", $"{CategoryName(c.Category)} ya {who} ({c.SubjectName}) imekataliwa kwa kura. {c.ClosedReason}", $"/mafao?kesi={c.Id}", ct);
        }
        else if (yes >= gov.RequiredApprovals && leaderYes >= gov.MinLeaderApprovals)
        {
            c.Status = BenefitCaseStatus.Approved; c.ApprovedAt = DateTime.UtcNow;

            // SASISHO18 (vikundi vya "kila tukio"): tukio lililoidhinishwa linatengeneza
            // wajibu wa kila mwanachama na kukata mchango kwenye pochi yake — hadi floor.
            // Kwa vikundi vya mwezi hii inarudi 0 bila kubadilisha kitu chochote.
            // Idempotent: UNIQUE(BenefitCaseId, GroupMemberId).
            await _eventWallet.PostObligationsAsync(groupId, c, userId, ct);

            // SASISHO14: pesa haitoshi siku ya idhini -> AwaitingFunds (haiwezi kulipwa mpaka salio la M-Koba litoshe)
            CashPosition? fundCheck = null;
            if (c.NetAmount > 0 && gov.CashCheckAtApproval && gov.RequireCashAvailability)
            {
                fundCheck = await CashPositionAsync(groupId, c.Id, c.NetAmount, ct);
                if (!fundCheck.Sufficient) c.Status = BenefitCaseStatus.AwaitingFunds;
            }
            await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitApproved, nameof(BenefitCase), c.Id, null, CaseSnap(c), c.CaseNumber,
                $"Kura {yes} za NDIYO (viongozi {leaderYes})" + (c.Status == BenefitCaseStatus.AwaitingFunds ? " — inasubiri pesa" : ""), ct);
            if (c.Status == BenefitCaseStatus.AwaitingFunds)
            {
                await _notify.NotifyAllMembersAsync(groupId, "BenefitAwaitingFunds", $"{c.CaseNumber} imeidhinishwa — inasubiri pesa",
                    $"{CategoryName(c.Category)} ya {who} ({c.SubjectName}) imeidhinishwa kwa kura {yes}, lakini pesa ya M-Koba haitoshi {c.NetAmount:N0}. Italipwa salio likitosha.", $"/mafao?kesi={c.Id}", ct);
                await _notify.NotifyLeadersAsync(groupId, "AwaitingFunds", $"Pesa haitoshi: {c.CaseNumber}",
                    fundCheck!.Available is null
                        ? Loc.L($"Kesi imeidhinishwa ({c.NetAmount:N0}) lakini hakuna salio la M-Koba lililoingizwa. Ingiza PDF ya karibuni kisha 'Kagua pesa tena'.")
                        : fundCheck.Stale
                            ? Loc.L($"Kesi imeidhinishwa ({c.NetAmount:N0}) lakini taarifa ya M-Koba ni ya zamani au sera ya umri wake haijawekwa. Ingiza PDF ya karibuni kisha 'Kagua pesa tena'.")
                            : Loc.L($"Kesi imeidhinishwa ({c.NetAmount:N0}) lakini inayopatikana ni {fundCheck.Available:N0}. Pesa ikiingia, ingiza PDF kisha 'Kagua pesa tena'."),
                    $"/mafao?kesi={c.Id}", ct);
            }
            else if (c.NetAmount == 0)
            {
                // Yote yamekatwa (Kianzio/deni): hakuna pesa inayotoka M-Koba — makato yanaingia Injini ya Ukweli sasa
                c.Status = BenefitCaseStatus.Paid; c.PaidAt = DateTime.SpecifyKind(TruthLedgerService.NowEat(), DateTimeKind.Utc) /* saa za EAT kama M-Koba; Npgsql: Kind=Utc */; c.PaidConfirmedAt = DateTime.UtcNow; c.PaidConfirmedByUserId = userId;
                await PostLedgerAsync(c, userId, ct);
                await _notify.NotifyAllMembersAsync(groupId, "BenefitPaid", $"{c.CaseNumber} imekamilika",
                    $"{CategoryName(c.Category)} ya {who}: {c.GrossAmount:N0} yote yamekatwa kulipa madeni yake (Kianzio {c.JoinFeeDeducted:N0}, deni la michango {c.ArrearsDeducted:N0}, deni la mkopo {c.LoanArrearsDeducted ?? 0:N0}). Hakuna pesa iliyotoka M-Koba.", $"/mafao?kesi={c.Id}", ct);
            }
            else await NotifyApprovedAsync(c, gov, who, yes, ct);
        }
        await _context.SaveChangesAsync(ct);
        return new VoteResult(c, yes, no, gov.RequiredApprovals);
    }

    private async Task NotifyApprovedAsync(BenefitCase c, BenefitGovernance gov, string who, int yes, CancellationToken ct)
    {
        await _notify.NotifyAllMembersAsync(c.GroupId, "BenefitApproved", $"{c.CaseNumber} imeidhinishwa",
            $"{CategoryName(c.Category)} ya {who} ({c.SubjectName}): atalipwa {c.NetAmount:N0} kupitia M-Koba. Kura {yes} za NDIYO.", $"/mafao?kesi={c.Id}", ct);
        await _notify.NotifyLeadersAsync(c.GroupId, "PayNow", $"Anzisha malipo {c.CaseNumber}",
            $"Kesi imeidhinishwa: {c.NetAmount:N0} kwa {who}. Hakiki pesa ipo, kisha ANZISHA malipo kwenye mfumo; mthibitishaji mwingine atathibitisha kwa reference ya M-Koba. Isipoonekana M-Koba ndani ya siku {gov.PaymentConfirmationDays}, wanachama wote wataona tahadhari.",
            $"/mafao?kesi={c.Id}", ct, ParseRoles(gov.PaymentInitiatorRoles).ToArray());
    }

    /// <summary>
    /// HATUA YA 2 YA MALIPO. Yanathibitishwa kwa muamala wa M-Koba (Withdraw) wenye kiasi SAWA KABISA na kiasi halisi,
    /// uliofanyika baada ya idhini, ambao haujaeleza kesi nyingine — na na mtu TOFAUTI na aliyeanzisha malipo
    /// (mwenye nafasi iliyoruhusiwa na sera). M-Koba haiwezi kughushiwa — huu ndio ushahidi wa mwisho.
    /// </summary>
    public async Task<BenefitCase> ConfirmPaymentAsync(Guid groupId, Guid caseId, string mkobaReference, Guid userId, CancellationToken ct = default,
        bool acknowledgeOtherRecipient = false, string? note = null)
    {
        var actor = await RequireLeaderAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Kesi haipo.");
        if (c.Status == BenefitCaseStatus.Approved)
            await RiskBlockedAsync(groupId, c, userId, "CONFIRM_BEFORE_INITIATION",
                "Malipo hayawezi kuthibitishwa kabla hayajaanzishwa na mweka-malipo (mf. Mtunza-hazina) aliyehakiki pesa ipo.", ct);
        if (c.Status != BenefitCaseStatus.PaymentInitiated) throw new ConflictException("Kesi lazima iwe imeidhinishwa kwa kura NA malipo yameanzishwa kwanza.");
        var gov = await _context.BenefitGovernances.AsNoTracking().FirstAsync(g => g.Id == c.GovernanceId, ct);
        if (actor.Id == c.BeneficiaryGroupMemberId) throw new ForbiddenException("Mnufaika hawezi kuthibitisha malipo yake mwenyewe.");
        if (c.PaymentInitiatedByUserId == userId)
            await RiskBlockedAsync(groupId, c, userId, "CONFIRMER_IS_INITIATOR", "Aliyeanzisha malipo HAWEZI kuyathibitisha — mthibitishaji lazima awe mtu mwingine.", ct);
        var roles = ParseRoles(gov.PaymentConfirmerRoles);
        if (!roles.Contains(Common.MemberAccess.EffectiveRole(actor)))
            throw new ForbiddenException($"Sera (toleo {gov.Version}) inaruhusu kuthibitisha malipo kwa: {string.Join(", ", roles.Select(RoleName))} tu.");
        if (await IsConflictedAsync(c, actor.Id, ct, includeInitiator: true))
            throw new ForbiddenException("Wewe ni ndugu wa mnufaika, wa aliyefungua kesi au wa aliyeanzisha malipo (mgongano wa maslahi) — kiongozi mwingine athibitishe malipo.");
        var row = await RequireFreeWithdrawalAsync(groupId, mkobaReference, c.Id, ct);
        if (row.Amount != c.NetAmount)
            await RiskBlockedAsync(groupId, c, userId, "PAYMENT_AMOUNT_MISMATCH",
                $"Kiasi cha M-Koba ({row.Amount:N0}) si sawa na kiasi kilichoidhinishwa ({c.NetAmount:N0}). Malipo hayawezi kuthibitishwa — tofauti yoyote lazima ielezwe na viongozi.", ct);
        var approvedEat = EatDate(c.ApprovedAt!.Value);
        if (row.Timestamp.Date < approvedEat)
            await RiskBlockedAsync(groupId, c, userId, "PAYMENT_BEFORE_APPROVAL",
                $"Muamala wa M-Koba ({row.Timestamp:dd/MM/yyyy}) ulifanyika KABLA ya idhini ({approvedEat:dd/MM/yyyy}). Pesa haipaswi kutoka kabla ya kura — kama ni tukio la zamani, lirekodi kama kesi ya kihistoria.", ct);
        var benPhones = await BeneficiaryPhonesAsync(groupId, c.BeneficiaryGroupMemberId, row.Timestamp, ct);
        var toOther = !benPhones.Contains(PhoneKey(row.Phone));
        if (toOther && (!acknowledgeOtherRecipient || string.IsNullOrWhiteSpace(note)))
            throw new ConflictException($"Muamala {row.Reference} ulienda kwa namba {row.Phone} ({row.PayerName}) ambayo SI namba iliyoidhinishwa ya mnufaika. Kama ni sahihi (mf. familia ya marehemu), thibitisha tena ukieleza kwa nini — itaonekana kama tahadhari kwenye ukaguzi.");
        // SASISHO15: hali ya kesi + daftari la mafao + ukaguzi + arifa vinahifadhiwa pamoja au havihifadhiwi kabisa.
        // Ombi la pili la wakati mmoja linagonga xmin ya kesi → DbUpdateConcurrencyException → 409 (halipati "imefaulu").
        await using var tx = await DbTx.BeginAsync(_context, ct);
        var before = CaseSnap(c);
        c.MkobaReference = row.Reference; c.PaidAt = row.Timestamp; c.PaidConfirmedByUserId = userId; c.PaidConfirmedAt = DateTime.UtcNow;
        c.Status = BenefitCaseStatus.Paid;
        await PostLedgerAsync(c, userId, ct);
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitPaidConfirmed, nameof(BenefitCase), c.Id, before, CaseSnap(c), row.Reference,
            toOther ? Loc.L($"Namba ya mpokeaji {row.Phone} si ya mnufaika: {note!.Trim()}") : note?.Trim(), ct);
        var who = await MemberName(c.BeneficiaryGroupMemberId, ct);
        await _notify.NotifyAllMembersAsync(groupId, "BenefitPaid", $"{c.CaseNumber} imelipwa",
            $"{who} amelipwa {c.NetAmount:N0} ({CategoryName(c.Category)}: {c.SubjectName}). M-Koba ref {row.Reference}, {row.Timestamp:dd/MM/yyyy HH:mm}.", $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        await DbTx.CommitAsync(tx, ct);
        return c;
    }

    private async Task<MkobaRawTransaction> RequireFreeWithdrawalAsync(Guid groupId, string mkobaReference, Guid? forCaseId, CancellationToken ct)
    {
        var reference = (mkobaReference ?? string.Empty).Trim().ToUpperInvariant();
        if (reference.Length == 0) throw new ValidationException("Weka reference ya M-Koba ya utoaji (mf. DFNO319K1X).");
        var row = await _context.MkobaRawTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.GroupId == groupId && t.Reference == reference, ct)
            ?? throw new NotFoundException($"Reference {reference} haipo kwenye PDF za M-Koba zilizoingizwa. Ingiza PDF yenye muamala huo kwanza.");
        if (!row.IsWithdrawal) throw new ConflictException($"{reference} ni pesa iliyoingia (Deposit), si utoaji.");
        var usedBy = await _context.BenefitCases.AsNoTracking().Where(x => x.GroupId == groupId && x.MkobaReference == reference && x.Id != forCaseId)
            .Select(x => x.CaseNumber).FirstOrDefaultAsync(ct);
        if (usedBy is not null) throw new ConflictException($"Utoaji {reference} tayari unaeleza kesi {usedBy}.");
        var loanUse = await WithdrawalRegistry.UsedByOtherAsync(_context, groupId, reference, forCaseId, ct);
        if (loanUse is { Kind: "Loan" }) throw new ConflictException($"Utoaji {reference} tayari unaeleza mkopo {loanUse.Label}.");
        if (await _context.MkobaWithdrawalExplanations.AnyAsync(x => x.GroupId == groupId && x.MkobaReference == reference && x.Status != ProposalStatus.Rejected, ct))
            throw new ConflictException($"Utoaji {reference} tayari umeelezwa kama matumizi mengine (si mafao).");
        return row;
    }

    public async Task<BenefitCase> CancelCaseAsync(Guid groupId, Guid caseId, string reason, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireMemberAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Kesi haipo.");
        if (!Common.MemberAccess.IsEffectiveLeader(actor))
        {
            // Mwanachama anaweza kughairi OMBI LAKE tu, kabla kura hazijaanza
            if (c.CreatedByUserId != userId) throw new ForbiddenException("Unaweza kughairi ombi lako mwenyewe tu.");
            if (c.Status is not (BenefitCaseStatus.AwaitingEvidence or BenefitCaseStatus.AwaitingVerification))
                throw new ConflictException("Kura zimeshaanza — ombi haliwezi kughairiwa na mwombaji. Wasiliana na viongozi.");
        }
        if (c.Status is BenefitCaseStatus.Paid or BenefitCaseStatus.Rejected or BenefitCaseStatus.Cancelled) throw new ConflictException("Kesi tayari imefungwa.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Eleza sababu ya kughairi kesi.");
        var before = CaseSnap(c);
        c.Status = BenefitCaseStatus.Cancelled; c.ClosedReason = reason.Trim(); c.ClosedByUserId = userId; c.ClosedAt = DateTime.UtcNow;
        c.MkobaReference = null; // reference inaachiwa (inaweza kuelezwa upya); kumbukumbu inabaki kwenye audit
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.BenefitCaseClosed, nameof(BenefitCase), c.Id, before, CaseSnap(c), c.CaseNumber, reason, ct);
        await _notify.NotifyAllMembersAsync(groupId, "BenefitCancelled", $"{c.CaseNumber} imeghairiwa", $"{CategoryName(c.Category)}: {c.SubjectName}. Sababu: {c.ClosedReason}", $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        return c;
    }

    // ---- Kesi za zamani (pesa ilishatoka M-Koba kabla ya mfumo) ----
    public sealed class HistoricalCaseDto
    {
        public BenefitCategory Category { get; set; }
        public BenefitRelationship Relationship { get; set; }
        public Guid BeneficiaryMemberId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string? Description { get; set; }
        public string MkobaReference { get; set; } = string.Empty;
        public decimal PerMemberAmount { get; set; }
        public int Headcount { get; set; }
        public decimal JoinFeeDeducted { get; set; }
        public decimal ArrearsDeducted { get; set; }
        // SASISHO14
        public decimal LoanArrearsDeducted { get; set; }
        /// <summary>Mkoba (chaguo-msingi) au Cash (taslimu ya zamani, kabla ya tarehe ya mwisho ya kumbukumbu za mkononi).</summary>
        public BenefitPaymentChannel PaymentChannel { get; set; } = BenefitPaymentChannel.Mkoba;
        public DateTime? CashPaidAt { get; set; }
        public string? CashRecipientName { get; set; }
        public string? CashRecipientPhone { get; set; }
        public string? ExceptionReason { get; set; }
    }

    public async Task<BenefitCase> RecordHistoricalCaseAsync(Guid groupId, HistoricalCaseDto dto, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        if (!Enum.IsDefined(dto.Category) || !Enum.IsDefined(dto.Relationship)) throw new ValidationException("Aina ya tukio au uhusiano si sahihi.");
        if (string.IsNullOrWhiteSpace(dto.SubjectName)) throw new ValidationException("Andika jina la mhusika wa tukio.");
        var ben = await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.BeneficiaryMemberId && m.GroupId == groupId, ct)
            ?? throw new NotFoundException("Mnufaika si mwanachama wa kikundi hiki.");
        if (dto.PerMemberAmount <= 0 || dto.Headcount <= 0) throw new ValidationException("Weka kiwango kwa kila mwanachama na idadi ya wanachama waliohesabiwa.");
        if (dto.JoinFeeDeducted < 0 || dto.ArrearsDeducted < 0 || dto.LoanArrearsDeducted < 0) throw new ValidationException("Makato hayawezi kuwa hasi.");
        if (!Enum.IsDefined(dto.PaymentChannel)) throw new ValidationException("Njia ya malipo si sahihi.");
        if (dto.PaymentChannel == BenefitPaymentChannel.Cash) return await RecordHistoricalCashAsync(groupId, dto, ben, userId, ct);
        var row = await RequireFreeWithdrawalAsync(groupId, dto.MkobaReference, null, ct);
        // Njia ya kihistoria ni ya malipo yaliyotoka KABLA mfumo haujaanza kusimamia mafao (sera ya kwanza kuidhinishwa).
        // Baada ya hapo kila utoaji lazima upite kesi → ushahidi → kura → kuanzisha → kuthibitisha.
        var controlledFrom = await _context.BenefitGovernances.AsNoTracking().Where(g => g.GroupId == groupId && g.Status == ProposalStatus.Approved && g.DecidedAt != null)
            .OrderBy(g => g.DecidedAt).Select(g => g.DecidedAt).FirstOrDefaultAsync(ct);
        if (controlledFrom is { } cf && row.Timestamp.Date >= EatDate(cf))
            throw new ConflictException($"Utoaji {row.Reference} ({row.Timestamp:dd/MM/yyyy}) ulifanyika baada ya mfumo kuanza kusimamia mafao ({EatDate(cf):dd/MM/yyyy}). Hauwezi kurekodiwa kama kesi ya kihistoria — fungua kesi ya kawaida, au ueleze utoaji huu kwenye Mafao → M-Koba (utaonekana kwenye ukaguzi wa hatari).");
        var gross = dto.PerMemberAmount * dto.Headcount;
        var net = gross - dto.JoinFeeDeducted - dto.ArrearsDeducted - dto.LoanArrearsDeducted;
        if (net != row.Amount)
            throw new ConflictException($"Hesabu hailingani na M-Koba: {dto.PerMemberAmount:N0} × {dto.Headcount} = {gross:N0} − makato {dto.JoinFeeDeducted + dto.ArrearsDeducted + dto.LoanArrearsDeducted:N0} = {net:N0}, lakini utoaji {row.Reference} ni {row.Amount:N0}. Rekebisha idadi/kiwango/makato mpaka ilingane kabisa.");
        if (dto.EventDate.Date > row.Timestamp.Date) throw new ValidationException("Tarehe ya tukio haiwezi kuwa baada ya malipo.");

        var checks = new List<Check> { new("HISTORICAL", true, $"Kesi ya kihistoria: malipo {row.Reference} ya {row.Timestamp:dd/MM/yyyy} ({row.Amount:N0}).") };
        var rate = await ActiveRateAsync(groupId, dto.Category, dto.Relationship, dto.EventDate, ct);
        if (rate is not null && rate.PerMemberAmount != dto.PerMemberAmount)
            checks.Add(new("RATE_DIFFERS", true, $"Tahadhari: kiwango kilichoidhinishwa siku hiyo ni {rate.PerMemberAmount:N0}, kesi hii ina {dto.PerMemberAmount:N0}."));
        if (dto.JoinFeeDeducted > 0)
        {
            var truthNow = await _truth.GetMemberTruthAsync(ben.Id, EndOfDay(row.Timestamp.Date).AddSeconds(-1), ct);
            if (dto.JoinFeeDeducted > truthNow.Statement.JoinFeeOutstanding)
                throw new ConflictException($"Kianzio kilichokatwa ({dto.JoinFeeDeducted:N0}) ni zaidi ya Kianzio alichokuwa anadaiwa siku hiyo ({truthNow.Statement.JoinFeeOutstanding:N0}).");
        }

        var c = new BenefitCase
        {
            Id = Guid.NewGuid(), GroupId = groupId, CaseNumber = await NextCaseNumberAsync(groupId, dto.EventDate.Year, ct), IsHistorical = true,
            Category = dto.Category, Relationship = dto.Relationship, BeneficiaryGroupMemberId = ben.Id, SubjectName = dto.SubjectName.Trim(),
            EventDate = DateOnlyUtc(dto.EventDate), Description = dto.Description?.Trim(), Status = BenefitCaseStatus.HistoricalPendingConfirmation,
            BenefitRateId = rate?.Id, PerMemberAmount = dto.PerMemberAmount, Headcount = dto.Headcount, GrossAmount = gross,
            JoinFeeDeducted = dto.JoinFeeDeducted, ArrearsDeducted = dto.ArrearsDeducted, LoanArrearsDeducted = dto.LoanArrearsDeducted, NetAmount = net,
            HeadcountRuleUsed = "Kihistoria — idadi imetajwa na viongozi (orodha ya majina haijulikani)",
            EligibilityJson = JsonSerializer.Serialize(checks, Json), MkobaReference = row.Reference, PaidAt = row.Timestamp,
            SnapshotAt = DateTime.UtcNow, CreatedByUserId = userId
        };
        _context.BenefitCases.Add(c);
        await Audit(groupId, ben.Id, userId, AuditAction.BenefitCaseOpened, nameof(BenefitCase), c.Id, null, CaseSnap(c), row.Reference, "Kesi ya kihistoria (inasubiri kiongozi wa pili)", ct);
        await _notify.NotifyLeadersAsync(groupId, "BenefitCase", $"Thibitisha kesi ya zamani {c.CaseNumber}",
            $"{CategoryName(c.Category)} ya {await MemberName(ben.Id, ct)}: utoaji {row.Reference} ({row.Amount:N0}). Kiongozi mwingine athibitishe.", $"/mafao?kesi={c.Id}", ct);
        await _context.SaveChangesAsync(ct);
        return c;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<BenefitCase> DecideHistoricalAsync(Guid groupId, Guid caseId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideHistoricalCoreAsync(groupId, caseId, approve, note, userId, ct), ct);

    private async Task<BenefitCase> DecideHistoricalCoreAsync(Guid groupId, Guid caseId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(groupId, userId, ct);
        var c = await _context.BenefitCases.FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Kesi haipo.");
        if (c.Status != BenefitCaseStatus.HistoricalPendingConfirmation) throw new ConflictException("Kesi hii haisubiri uthibitisho.");
        if (c.CreatedByUserId == userId) throw new ForbiddenException("Aliyerekodi kesi HAWEZI kuithibitisha mwenyewe (maker-checker).");
        if (actor.Id == c.BeneficiaryGroupMemberId) throw new ForbiddenException("Mnufaika hawezi kuthibitisha kesi yake.");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new ValidationException("Eleza kwa nini unakataa.");
        if (approve && c.PaymentChannel == BenefitPaymentChannel.Cash) await RequireCashEvidenceAsync(c, userId, ct);
        var before = CaseSnap(c);
        if (approve)
        {
            c.Status = BenefitCaseStatus.Paid; c.ApprovedAt = DateTime.UtcNow; c.PaidConfirmedByUserId = userId; c.PaidConfirmedAt = DateTime.UtcNow;
            await PostLedgerAsync(c, userId, ct);
        }
        else
        {
            c.Status = BenefitCaseStatus.Rejected; c.ClosedReason = note!.Trim(); c.ClosedByUserId = userId; c.ClosedAt = DateTime.UtcNow; c.MkobaReference = null;
        }
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, approve ? AuditAction.BenefitPaidConfirmed : AuditAction.BenefitRejected, nameof(BenefitCase), c.Id,
            before, CaseSnap(c), c.MkobaReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return c;
    }

    private static object CaseSnap(BenefitCase c) => new
    {
        c.CaseNumber, Category = c.Category.ToString(), Relationship = c.Relationship.ToString(), c.BeneficiaryGroupMemberId, c.SubjectName, c.EventDate,
        Status = c.Status.ToString(), c.IsHistorical, c.PerMemberAmount, c.Headcount, c.GrossAmount, c.JoinFeeDeducted, c.ArrearsDeducted, c.LoanArrearsDeducted, c.NetAmount,
        PaymentChannel = c.PaymentChannel.ToString(), c.CashRecipientName, c.CashRecipientPhone, c.ExceptionReason,
        c.MkobaReference, c.PaidAt, c.ApprovedAt, c.ClosedReason, c.PolicyVersion, c.PaymentInitiatedByUserId, c.PaymentInitiatedAt, c.PaymentInitiatedToPhone,
        c.CashAvailableAtInitiation, c.PaidConfirmedByUserId
    };

    public async Task<(BenefitEvidence Evidence, byte[] Content)> ReadEvidenceAsync(Guid groupId, Guid evidenceId, Guid userId, CancellationToken ct = default)
    {
        var actor = await RequireMemberAsync(groupId, userId, ct);
        var e = await _context.BenefitEvidence.AsNoTracking().FirstOrDefaultAsync(x => x.Id == evidenceId && x.GroupId == groupId, ct) ?? throw new NotFoundException("Ushahidi haupo.");
        var c = await _context.BenefitCases.AsNoTracking().FirstAsync(x => x.Id == e.BenefitCaseId, ct);
        // Usiri: faili linaonwa na viongozi (wahakiki) na mnufaika tu; wanachama wanaona "imethibitishwa ✔"
        if (!Common.MemberAccess.IsEffectiveLeader(actor) && actor.Id != c.BeneficiaryGroupMemberId)
            throw new ForbiddenException("Vyeti vinaonwa na viongozi na mnufaika tu. Wewe unaona hali yake (imethibitishwa au la).");
        var bytes = await _storage.ReadAsync(e.StorageKey, ct);
        // Uadilifu: faili lililohifadhiwa lazima liwe lile lile lililopakiwa (SHA-256 inalinganishwa kila linaposomwa)
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, e.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.RiskBlocked, nameof(BenefitEvidence), e.Id, null,
                new { Code = "EVIDENCE_TAMPERED", Expected = e.Sha256, Actual = actual }, c.CaseNumber, "Faili la ushahidi limebadilika baada ya kupakiwa", ct);
            await _context.SaveChangesAsync(ct);
            throw new ConflictException($"Faili la ushahidi {e.DocumentNumber} limebadilika tangu lilipopakiwa (SHA-256 hailingani). Tukio limeandikwa kwenye ukaguzi — wajulishe viongozi.");
        }
        await Audit(groupId, c.BeneficiaryGroupMemberId, userId, AuditAction.EvidenceAccessed, nameof(BenefitEvidence), e.Id, null, new { e.DocumentType, e.DocumentNumber }, c.CaseNumber, null, ct);
        await _context.SaveChangesAsync(ct);
        return (e, bytes);
    }

    // =====================================================================
    // 5. UTOAJI WA M-KOBA: maelezo (si mafao) + ulinganisho
    // =====================================================================
    public async Task<MkobaWithdrawalExplanation> ExplainWithdrawalAsync(Guid groupId, string mkobaReference, WithdrawalExplanationKind kind, string description, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        if (!Enum.IsDefined(kind)) throw new ValidationException("Aina ya matumizi si sahihi.");
        if (string.IsNullOrWhiteSpace(description)) throw new ValidationException("Eleza pesa hii ilitumika kwa nini (na uamuzi wa kikao/idhini ulipo).");
        var row = await RequireFreeWithdrawalAsync(groupId, mkobaReference, null, ct);
        var x = new MkobaWithdrawalExplanation
        {
            Id = Guid.NewGuid(), GroupId = groupId, MkobaReference = row.Reference, Kind = kind, Description = description.Trim(), RequestedByUserId = userId
        };
        _context.MkobaWithdrawalExplanations.Add(x);
        await Audit(groupId, null, userId, AuditAction.WithdrawalExplained, nameof(MkobaWithdrawalExplanation), x.Id, null,
            new { x.MkobaReference, Kind = x.Kind.ToString(), x.Description, row.Amount }, row.Reference, "Maelezo ya utoaji (yanasubiri kiongozi wa pili)", ct);
        await _notify.NotifyLeadersAsync(groupId, "WithdrawalExplanation", $"Thibitisha maelezo ya utoaji {row.Reference}",
            $"{row.Amount:N0} ({row.Timestamp:dd/MM/yyyy}): {x.Description}", "/mafao?tab=mkoba", ct);
        await _context.SaveChangesAsync(ct);
        return x;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<MkobaWithdrawalExplanation> DecideExplanationAsync(Guid groupId, Guid id, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideExplanationCoreAsync(groupId, id, approve, note, userId, ct), ct);

    private async Task<MkobaWithdrawalExplanation> DecideExplanationCoreAsync(Guid groupId, Guid id, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(groupId, userId, ct);
        var x = await _context.MkobaWithdrawalExplanations.FirstOrDefaultAsync(e => e.Id == id && e.GroupId == groupId, ct) ?? throw new NotFoundException("Maelezo hayapo.");
        if (x.Status != ProposalStatus.Pending) throw new ConflictException($"Tayari ni {x.Status}.");
        if (x.RequestedByUserId == userId) throw new ForbiddenException("Aliyeandika maelezo HAWEZI kuyathibitisha (maker-checker).");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new ValidationException("Eleza kwa nini unakataa.");
        x.Status = approve ? ProposalStatus.Approved : ProposalStatus.Rejected; x.DecidedByUserId = userId; x.DecidedAt = DateTime.UtcNow; x.DecisionNote = note?.Trim();
        await Audit(groupId, null, userId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected, nameof(MkobaWithdrawalExplanation), x.Id, null,
            new { x.MkobaReference, Status = x.Status.ToString() }, x.MkobaReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return x;
    }

    public sealed record WithdrawalRow(string Reference, DateTime Timestamp, decimal Amount, string Phone, string PayerName, decimal? BalanceAfter,
        string State, string Hali, Guid? CaseId, string? CaseNumber, Guid? ExplanationId, string? ExplanationKind, string? ExplanationText,
        bool CanDecideExplanation);

    public async Task<List<WithdrawalRow>> WithdrawalsAsync(Guid groupId, Guid viewerUserId, CancellationToken ct = default)
    {
        var rows = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId && t.IsWithdrawal).OrderByDescending(t => t.Timestamp).ToListAsync(ct);
        var cases = await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId && c.MkobaReference != null).ToListAsync(ct);
        var ex = await _context.MkobaWithdrawalExplanations.AsNoTracking().Where(e => e.GroupId == groupId && e.Status != ProposalStatus.Rejected).ToListAsync(ct);
        var loanUses = (await WithdrawalRegistry.UsesAsync(_context, groupId, ct)).Where(u => u.Kind == "Loan").ToList();
        // SASISHO16 (faragha): kiasi, tarehe na maelezo ni wazi kwa wote; NAMBA KAMILI YA SIMU ya mpokeaji inaonekana kwa
        // viongozi/wakaguzi na kwa mwenye namba tu — wanachama wengine wanaona namba iliyofichwa (mf. 25574•••••429).
        var viewer = await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == viewerUserId, ct);
        var fullPhones = viewer is not null && (Common.MemberAccess.IsEffectiveLeader(viewer)
                         || (NotificationService.InGroupStatuses.Contains(viewer.Status) && await IsAuditorAsync(groupId, viewer.Id, ct)));
        var ownPhones = viewer is null ? new HashSet<string>() : (await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupMemberId == viewer.Id).Select(p => p.Phone).ToListAsync(ct)).Select(PhoneMask.Digits).ToHashSet();
        string ShowPhone(string ph) => fullPhones || ownPhones.Contains(PhoneMask.Digits(ph)) ? ph : PhoneMask.Mask(ph);
        return rows.Select(r =>
        {
            var c = cases.FirstOrDefault(x => x.MkobaReference == r.Reference);
            var e = ex.FirstOrDefault(x => x.MkobaReference == r.Reference);
            var lu = loanUses.FirstOrDefault(x => x.Reference == r.Reference);
            var (state, hali) = lu is not null && c is null
                ? lu.Final ? ("Explained", $"Mkopo {lu.Label}") : ("Pending", $"Mkopo {lu.Label} — inasubiri uthibitisho")
                : c is not null
                ? c.Status == BenefitCaseStatus.Paid ? ("Explained", $"Mafao {c.CaseNumber}") : ("Pending", $"Mafao {c.CaseNumber} — inasubiri uthibitisho")
                : e is not null
                    ? e.Status == ProposalStatus.Approved ? ("Explained", $"{KindName(e.Kind)}: {e.Description}") : ("Pending", $"{KindName(e.Kind)} — inasubiri kiongozi wa pili")
                    : ("Unexplained", "HAIJAELEZWA — pesa imetoka bila kesi ya mafao wala maelezo yaliyoidhinishwa");
            return new WithdrawalRow(r.Reference, r.Timestamp, r.Amount, ShowPhone(r.Phone), r.PayerName, r.Balance, state, hali, c?.Id, c?.CaseNumber,
                e?.Id, e?.Kind.ToString(), e?.Description, e is { Status: ProposalStatus.Pending } && e.RequestedByUserId != viewerUserId);
        }).ToList();
    }

    /// <summary>Message = Kiswahili (fallback). Args = vigeu kwa frontend kutafsiri kwa Code (mf. EN).</summary>
    public sealed record AlertItem(string Level, string Code, string Message, string? Link, Dictionary<string, string>? Args = null, string? Tpl = null, string[]? TArgs = null)
    {
        public AlertItem(string level, string code, Loc message, string? link, Dictionary<string, string>? args = null)
            : this(level, code, message.Text, link, args, message.Template, message.Args.ToArray()) { }
    }
    private static Dictionary<string, string> A(params (string k, object v)[] xs) => xs.ToDictionary(x => x.k, x => Convert.ToString(x.v, System.Globalization.CultureInfo.InvariantCulture) ?? "");

    /// <summary>Tahadhari za uwazi — zinaonekana kwa KILA mwanachama (hazina taarifa za vyeti).</summary>
    public async Task<List<AlertItem>> AlertsAsync(Guid groupId, CancellationToken ct = default)
    {
        var list = new List<AlertItem>();
        var gov = await GetActiveGovernanceAsync(groupId, ct);
        var rows = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId && t.IsWithdrawal).ToListAsync(ct);
        var explainedRefs = (await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId && c.MkobaReference != null && c.Status == BenefitCaseStatus.Paid)
                .Select(c => c.MkobaReference!).ToListAsync(ct))
            .Concat(await _context.MkobaWithdrawalExplanations.AsNoTracking().Where(e => e.GroupId == groupId && e.Status == ProposalStatus.Approved)
                .Select(e => e.MkobaReference).ToListAsync(ct))
            .Concat((await WithdrawalRegistry.UsesAsync(_context, groupId, ct)).Where(u => u.Kind == "Loan" && u.Final).Select(u => u.Reference)).ToHashSet();
        var unexplained = rows.Where(r => !explainedRefs.Contains(r.Reference)).ToList();
        if (unexplained.Count > 0)
            list.Add(new("danger", "UNEXPLAINED_WITHDRAWALS",
                $"Utoaji {unexplained.Count} wa M-Koba wa jumla {unexplained.Sum(r => r.Amount):N0} haujaelezwa na kesi ya mafao wala maelezo yaliyoidhinishwa: " +
                string.Join(", ", unexplained.OrderBy(r => r.Timestamp).Select(r => $"{r.Reference} ({r.Amount:N0}, {r.Timestamp:dd/MM/yyyy})")) + ".", "/mafao?tab=mkoba",
                A(("count", unexplained.Count), ("total", unexplained.Sum(r => r.Amount)),
                  ("list", string.Join(", ", unexplained.OrderBy(r => r.Timestamp).Select(r => $"{r.Reference} ({r.Amount:N0}, {r.Timestamp:dd/MM/yyyy})"))))));

        var open = await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId &&
            (c.Status == BenefitCaseStatus.Approved || c.Status == BenefitCaseStatus.PaymentInitiated || c.Status == BenefitCaseStatus.AwaitingFunds || c.Status == BenefitCaseStatus.Voting || c.Status == BenefitCaseStatus.AwaitingVerification || c.Status == BenefitCaseStatus.HistoricalPendingConfirmation))
            .ToListAsync(ct);
        foreach (var c in open.Where(c => c.Status is BenefitCaseStatus.Approved or BenefitCaseStatus.PaymentInitiated && gov is not null && c.ApprovedAt!.Value.AddDays(gov.PaymentConfirmationDays) < DateTime.UtcNow))
            list.Add(new("warn", "PAYMENT_NOT_CONFIRMED", $"{c.CaseNumber}: imeidhinishwa {c.ApprovedAt:dd/MM/yyyy} ({c.NetAmount:N0}) lakini malipo hayajaonekana M-Koba ndani ya siku {gov!.PaymentConfirmationDays}.", $"/mafao?kesi={c.Id}",
                A(("case", c.CaseNumber), ("date", c.ApprovedAt!.Value.ToString("dd/MM/yyyy")), ("amount", c.NetAmount), ("days", gov!.PaymentConfirmationDays))));
        foreach (var c in open.Where(c => c.Status == BenefitCaseStatus.AwaitingFunds))
            list.Add(new("warn", "AWAITING_FUNDS", $"{c.CaseNumber}: imeidhinishwa lakini inasubiri pesa ({c.NetAmount:N0}) — salio la M-Koba halitoshi.", $"/mafao?kesi={c.Id}",
                A(("case", c.CaseNumber), ("amount", c.NetAmount))));
        foreach (var c in open.Where(c => c.Status == BenefitCaseStatus.Voting && c.VotingDeadline < DateTime.UtcNow))
            list.Add(new("warn", "VOTING_EXPIRED", $"{c.CaseNumber}: muda wa kura umeisha bila uamuzi.", $"/mafao?kesi={c.Id}", A(("case", c.CaseNumber))));
        var waitingEvidence = open.Count(c => c.Status == BenefitCaseStatus.AwaitingVerification);
        if (waitingEvidence > 0) list.Add(new("info", "EVIDENCE_WAITING", $"Kesi {waitingEvidence} zinasubiri ushahidi kuhakikiwa.", "/mafao", A(("count", waitingEvidence))));
        var hist = open.Count(c => c.Status == BenefitCaseStatus.HistoricalPendingConfirmation);
        if (hist > 0) list.Add(new("info", "HISTORICAL_WAITING", $"Kesi {hist} za zamani zinasubiri kiongozi wa pili kuthibitisha.", "/mafao", A(("count", hist))));
        var voting = open.Count(c => c.Status == BenefitCaseStatus.Voting && c.VotingDeadline >= DateTime.UtcNow);
        if (voting > 0) list.Add(new("info", "VOTING_OPEN", $"Kesi {voting} ziko kwenye kura.", "/mafao", A(("count", voting))));

        var pendingPhones = await _context.MemberPhones.CountAsync(p => p.GroupId == groupId && (p.Status == TruthApprovalStatus.Pending || p.PendingValidTo != null), ct);
        if (pendingPhones > 0) list.Add(new("info", "PHONES_WAITING", $"Maombi {pendingPhones} ya namba za simu yanasubiri kiongozi wa pili.", "/ukweli/simu", A(("count", pendingPhones))));
        var pendingPolicy = await _context.BenefitGovernances.CountAsync(g => g.GroupId == groupId && g.Status == ProposalStatus.Pending, ct)
                            + await _context.BenefitRates.CountAsync(r => r.GroupId == groupId && r.Status == ProposalStatus.Pending, ct);
        if (pendingPolicy > 0) list.Add(new("info", "POLICY_WAITING", $"Mapendekezo {pendingPolicy} ya sera/viwango vya mafao yanasubiri idhini.", "/mafao?tab=sera", A(("count", pendingPolicy))));
        var pendingStatus = await _context.MemberStatusChangeRequests.CountAsync(r => r.GroupId == groupId && r.Status == ProposalStatus.Pending, ct);
        if (pendingStatus > 0) list.Add(new("info", "STATUS_WAITING", $"Maombi {pendingStatus} ya kubadilisha hali ya wanachama yanasubiri kura.", "/wanachama/kura", A(("count", pendingStatus))));
        if (gov is null) list.Add(new("warn", "NO_GOVERNANCE", "Sera ya mafao na idhini haijawekwa — mafao hayawezi kushughulikiwa.", "/mafao?tab=sera"));
        return list;
    }

    // =====================================================================
    // 6. DAFTARI LA MAFAO (viongozi) + CSV
    // =====================================================================
    public sealed record LedgerRow(Guid CaseId, string CaseNumber, DateTime? PaidAt, string MemberNumber, string Phones, string FullName,
        string Category, string Relationship, string SubjectName, DateTime EventDate, decimal PerMemberAmount, int Headcount, decimal Gross,
        decimal JoinFeeDeducted, decimal ArrearsDeducted, decimal Net, string Approvers, string? MkobaReference, string Status, bool IsHistorical,
        decimal LoanArrearsDeducted = 0m, string PaymentChannel = nameof(BenefitPaymentChannel.Mkoba));

    public async Task<List<LedgerRow>> LedgerAsync(Guid groupId, CancellationToken ct = default)
    {
        var cases = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && (c.Status == BenefitCaseStatus.Paid || c.Status == BenefitCaseStatus.Approved || c.Status == BenefitCaseStatus.PaymentInitiated
                        || c.Status == BenefitCaseStatus.AwaitingFunds || c.Status == BenefitCaseStatus.HistoricalPendingConfirmation))
            .OrderBy(c => c.PaidAt ?? c.ApprovedAt ?? c.CreatedAt).ToListAsync(ct);
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        var phones = await _context.MemberPhones.AsNoTracking().Where(p => p.GroupId == groupId && p.Status == TruthApprovalStatus.Approved).ToListAsync(ct);
        var ids = cases.Select(c => c.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && ids.Contains(v.SubjectId) && v.Approve).ToListAsync(ct);
        string Name(Guid memberId) => members.FirstOrDefault(m => m.Id == memberId)?.User?.FullName ?? "?";
        string UserName(Guid? uid) => uid is null ? "" : members.FirstOrDefault(m => m.UserId == uid)?.User?.FullName ?? "?";
        return cases.Select(c =>
        {
            var m = members.FirstOrDefault(x => x.Id == c.BeneficiaryGroupMemberId);
            var ph = string.Join(" / ", phones.Where(p => p.GroupMemberId == c.BeneficiaryGroupMemberId).Select(p => p.Phone).Distinct());
            var approvers = c.IsHistorical
                ? $"{UserName(c.CreatedByUserId)} (alirekodi), {UserName(c.PaidConfirmedByUserId)} (alithibitisha)"
                : string.Join(", ", votes.Where(v => v.SubjectId == c.Id).OrderBy(v => v.At).Select(v => Name(v.GroupMemberId)));
            return new LedgerRow(c.Id, c.CaseNumber, c.PaidAt, m?.MemberNumber ?? "", ph, m?.User?.FullName ?? "?", CategoryName(c.Category), RelationshipName(c.Relationship),
                c.SubjectName, c.EventDate, c.PerMemberAmount, c.Headcount, c.GrossAmount, c.JoinFeeDeducted, c.ArrearsDeducted, c.NetAmount, approvers,
                c.MkobaReference ?? (c.PaymentChannel == BenefitPaymentChannel.Cash ? CashRef(c.CaseNumber) : null), c.Status.ToString(), c.IsHistorical,
                c.LoanArrearsDeducted ?? 0m, c.PaymentChannel.ToString());
        }).ToList();
    }

    /// <summary>
    /// SASISHO16 (CSV/formula injection): maandishi yanayoanza na = + - @ TAB au CR yangetekelezwa kama fomula na
    /// Excel/LibreOffice (mf. jina la mhusika "=HYPERLINK(...)"). Yanatanguliwa na ' ili yasomeke kama maandishi tu.
    /// </summary>
    public static string CsvSafe(string? s)
    {
        var v = s ?? string.Empty;
        return v.Length > 0 && "=+-@\t\r".Contains(v[0]) ? "'" + v : v;
    }

    public async Task<byte[]> LedgerCsvAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await LedgerAsync(groupId, ct);
        var sb = new StringBuilder();
        sb.AppendLine("Kesi,Tarehe ya malipo,Namba ya mwanachama,Simu,Jina,Aina,Uhusiano,Mhusika,Tarehe ya tukio,Kiwango kwa mwanachama,Idadi,Jumla,Kianzio kilichokatwa,Deni la michango lililokatwa,Deni la mkopo lililokatwa,Kiasi halisi,Walioidhinisha,Ref M-Koba / taslimu,Njia,Hali");
        static string Q(string? s) => "\"" + CsvSafe(s).Replace("\"", "\"\"") + "\"";
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Q(r.CaseNumber), Q(r.PaidAt?.ToString("yyyy-MM-dd HH:mm")), Q(r.MemberNumber), Q(r.Phones), Q(r.FullName), Q(r.Category), Q(r.Relationship),
                Q(r.SubjectName), Q(r.EventDate.ToString("yyyy-MM-dd")), r.PerMemberAmount.ToString("0", CultureInfo.InvariantCulture), r.Headcount,
                r.Gross.ToString("0", CultureInfo.InvariantCulture), r.JoinFeeDeducted.ToString("0", CultureInfo.InvariantCulture),
                r.ArrearsDeducted.ToString("0", CultureInfo.InvariantCulture), r.LoanArrearsDeducted.ToString("0", CultureInfo.InvariantCulture),
                r.Net.ToString("0", CultureInfo.InvariantCulture), Q(r.Approvers), Q(r.MkobaReference), Q(r.PaymentChannel), Q(r.Status)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(); // BOM => Excel inasoma herufi sawa
    }

    // =====================================================================
    // 7. MWANACHAMA: mafao aliyochangia / aliyopokea / kura zinazomsubiri
    // =====================================================================
    public async Task<object> MyBenefitsAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var cases = await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId && c.Status != BenefitCaseStatus.Cancelled && c.Status != BenefitCaseStatus.Rejected)
            .OrderByDescending(c => c.EventDate).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToDictionaryAsync(m => m.Id, m => m.User!.FullName, ct);
        var contributed = cases.Where(c => c.Status is BenefitCaseStatus.Paid or BenefitCaseStatus.Approved or BenefitCaseStatus.PaymentInitiated or BenefitCaseStatus.AwaitingFunds or BenefitCaseStatus.Voting)
            .Select(c =>
            {
                var ids = c.CountedMemberIdsJson is null ? null : JsonSerializer.Deserialize<List<Guid>>(c.CountedMemberIdsJson, Json);
                return new { c, counted = ids?.Contains(me.Id), known = ids is not null };
            })
            .Where(x => x.counted != false)
            .Select(x => new
            {
                caseId = x.c.Id, x.c.CaseNumber, category = CategoryName(x.c.Category), relationship = RelationshipName(x.c.Relationship),
                beneficiary = names.GetValueOrDefault(x.c.BeneficiaryGroupMemberId), x.c.SubjectName, x.c.EventDate, x.c.PaidAt,
                share = x.known ? x.c.PerMemberAmount : (decimal?)null, status = x.c.Status.ToString(),
                note = x.known ? null : "Kesi ya zamani — orodha ya waliohesabiwa haijulikani"
            }).ToList();
        var received = cases.Where(c => c.BeneficiaryGroupMemberId == me.Id)
            .Select(c => new { caseId = c.Id, c.CaseNumber, category = CategoryName(c.Category), c.SubjectName, c.EventDate, c.GrossAmount, c.JoinFeeDeducted, c.ArrearsDeducted, loanArrearsDeducted = c.LoanArrearsDeducted ?? 0m, c.NetAmount, c.PaidAt, status = c.Status.ToString() })
            .ToList();
        var voting = new List<object>();
        foreach (var c in cases.Where(c => c.Status == BenefitCaseStatus.Voting && c.VotingDeadline >= DateTime.UtcNow))
        {
            var gov = await _context.BenefitGovernances.AsNoTracking().FirstAsync(g => g.Id == c.GovernanceId, ct);
            var voters = await EligibleVotersAsync(c, gov, ct);
            if (voters.Any(v => v.Id == me.Id) && !await _context.GovernanceVotes.AnyAsync(v => v.SubjectType == SubjectBenefitCase && v.SubjectId == c.Id && v.GroupMemberId == me.Id, ct))
                voting.Add(new { caseId = c.Id, c.CaseNumber, category = CategoryName(c.Category), c.SubjectName, beneficiary = names.GetValueOrDefault(c.BeneficiaryGroupMemberId), c.VotingDeadline });
        }
        return new
        {
            memberId = me.Id, canRequest = NotificationService.InGroupStatuses.Contains(me.Status),
            contributed, totalContributed = contributed.Where(x => x.share is not null && x.status == nameof(BenefitCaseStatus.Paid)).Sum(x => x.share!.Value),
            received, totalReceived = received.Where(x => x.status == nameof(BenefitCaseStatus.Paid)).Sum(x => x.NetAmount),
            votesNeeded = voting
        };
    }

    // =====================================================================
    // 8. ORODHA + UNDANI WA KESI
    // =====================================================================
    public async Task<List<object>> ListCasesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await RequireMemberAsync(groupId, userId, ct);
        var cases = await _context.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToDictionaryAsync(m => m.Id, m => m.User!.FullName, ct);
        var ids = cases.Select(c => c.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && ids.Contains(v.SubjectId)).ToListAsync(ct);
        return cases.Select(c => (object)new
        {
            c.Id, c.CaseNumber, category = c.Category.ToString(), relationship = c.Relationship.ToString(), beneficiaryId = c.BeneficiaryGroupMemberId,
            beneficiaryName = names.GetValueOrDefault(c.BeneficiaryGroupMemberId), c.SubjectName, c.EventDate, status = c.Status.ToString(), c.IsHistorical,
            c.PerMemberAmount, c.Headcount, c.GrossAmount, c.NetAmount, c.MkobaReference, c.CreatedAt, c.VotingDeadline,
            approvals = votes.Count(v => v.SubjectId == c.Id && v.Approve), rejections = votes.Count(v => v.SubjectId == c.Id && !v.Approve)
        }).ToList();
    }

    public async Task<object> CaseDetailAsync(Guid groupId, Guid caseId, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var isLeader = Common.MemberAccess.IsEffectiveLeader(me);
        var c = await _context.BenefitCases.AsNoTracking().Include(x => x.Evidence).FirstOrDefaultAsync(x => x.Id == caseId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Kesi haipo.");
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        string UserName(Guid? uid) => uid is null ? "" : members.FirstOrDefault(m => m.UserId == uid)?.User?.FullName ?? "?";
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == SubjectBenefitCase && v.SubjectId == c.Id).OrderBy(v => v.At).ToListAsync(ct);
        var gov = c.GovernanceId is { } gid ? await _context.BenefitGovernances.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) : null;
        var rate = c.BenefitRateId is { } rid ? await _context.BenefitRates.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct) : null;
        bool canVote = false; int eligibleVoters = 0;
        if (c.Status == BenefitCaseStatus.Voting && gov is not null)
        {
            var voters = await EligibleVotersAsync(c, gov, ct);
            eligibleVoters = voters.Count;
            canVote = c.VotingDeadline >= DateTime.UtcNow && voters.Any(v => v.Id == me.Id) && votes.All(v => v.GroupMemberId != me.Id);
        }
        var candidates = new List<object>();
        if (c.Status == BenefitCaseStatus.PaymentInitiated && isLeader)
        {
            var approvedEat = EatDate(c.ApprovedAt!.Value);
            var used = await _context.BenefitCases.AsNoTracking().Where(x => x.GroupId == groupId && x.MkobaReference != null).Select(x => x.MkobaReference!).ToListAsync(ct);
            var explained = (await WithdrawalRegistry.UsesAsync(_context, groupId, ct)).Where(u => u.Kind != "Benefit").Select(u => u.Reference).ToList();
            candidates = (await _context.MkobaRawTransactions.AsNoTracking()
                    .Where(t => t.GroupId == groupId && t.IsWithdrawal && t.Amount == c.NetAmount).ToListAsync(ct))
                .Where(t => t.Timestamp.Date >= approvedEat && !used.Contains(t.Reference) && !explained.Contains(t.Reference))
                .Select(t => (object)new { t.Reference, t.Timestamp, t.Amount, t.PayerName, t.Phone }).ToList();
        }
        var counted = c.CountedMemberIdsJson is null ? null : JsonSerializer.Deserialize<List<Guid>>(c.CountedMemberIdsJson, Json);
        var conflicted = await IsConflictedAsync(c, me.Id, ct, includeInitiator: c.Status == BenefitCaseStatus.PaymentInitiated);
        // SASISHO14: wapiga kura na viongozi wanaona pesa ipo kabla ya kuamua; mweka-malipo anaona namba/simu ya mnufaika
        var cashView = (canVote || isLeader) && c.NetAmount > 0 && c.Status is BenefitCaseStatus.Voting or BenefitCaseStatus.AwaitingFunds
            ? await CashPositionAsync(groupId, c.Id, c.NetAmount, ct) : null;
        var ben = members.FirstOrDefault(m => m.Id == c.BeneficiaryGroupMemberId);
        var benPhone = isLeader
            ? (await Common.MemberPhonesAt.ListAsync(_context, groupId, c.BeneficiaryGroupMemberId, TruthLedgerService.NowEat(), ct)).FirstOrDefault()
            : null;
        var iAmAuditor = gov is not null && ParseIds(gov.AuditorMemberIds).Contains(me.Id)
                         || ParseIds((await GetActiveGovernanceAsync(groupId, ct))?.AuditorMemberIds).Contains(me.Id);
        var verifierRoles = gov is null ? NotificationService.LeaderRoles : VerifierRolesOf(gov);
        string? histBlocked = null;
        if (c.Status == BenefitCaseStatus.HistoricalPendingConfirmation && c.PaymentChannel == BenefitPaymentChannel.Cash)
        {
            if (!c.Evidence.Any(e => e.Status == EvidenceStatus.Verified && e.VerifiedByUserId != e.UploadedByUserId))
                histBlocked = "Malipo ya taslimu yanahitaji ushahidi (mf. risiti) uliohakikiwa na kiongozi asiye aliyeupakia, kabla ya kuthibitishwa.";
            else if (c.Evidence.Any(e => e.UploadedByUserId == userId))
                histBlocked = "Uliyepakia ushahidi wa malipo haya HUWEZI kuyathibitisha — kiongozi mwingine.";
            else if (c.Evidence.Any(e => e.Status == EvidenceStatus.Verified && e.VerifiedByUserId == userId))
                histBlocked = "Uliyehakiki ushahidi wa malipo haya HUWEZI pia kuyathibitisha — mtu wa tatu athibitishe (mhakiki ≠ mthibitishaji).";
        }
        var pay = isLeader ? await PaymentPermissionsAsync(c, gov, me, votes, conflicted, ct) : new PaymentPermissions(false, null, false, null, null);
        var ledgerCheck = isLeader || iAmAuditor || me.Id == c.BeneficiaryGroupMemberId ? await ReconcileCaseAsync(c, ct) : null;
        return new
        {
            c.Id, c.CaseNumber, category = c.Category.ToString(), relationship = c.Relationship.ToString(), beneficiaryId = c.BeneficiaryGroupMemberId,
            beneficiaryName = ben?.User?.FullName, c.SubjectName, c.EventDate, c.Description,
            beneficiaryMemberNumber = isLeader ? ben?.MemberNumber : null, beneficiaryPhone = benPhone,
            status = c.Status.ToString(), c.IsHistorical, c.PerMemberAmount, c.Headcount, c.GrossAmount, c.JoinFeeDeducted, c.ArrearsDeducted,
            loanArrearsDeducted = c.LoanArrearsDeducted ?? 0m, legacyArrears = c.LoanArrearsDeducted is null, c.NetAmount,
            paymentChannel = c.PaymentChannel.ToString(), c.CashRecipientName, cashRecipientPhone = isLeader ? c.CashRecipientPhone : null, c.ExceptionReason,
            cashForVoters = cashView, canRecheckFunds = isLeader && c.Status == BenefitCaseStatus.AwaitingFunds, iAmAuditor,
            headcountRule = c.HeadcountRuleUsed, frozen = c.SnapshotAt is not null, c.SnapshotAt,
            countedMembers = counted?.Select(id => members.FirstOrDefault(m => m.Id == id)?.User?.FullName ?? "?").OrderBy(n => n).ToList(),
            eligibility = c.EligibilityJson is null ? null : JsonSerializer.Deserialize<List<Check>>(c.EligibilityJson, Json),
            acceptedEvidence = rate is null ? new List<string>() : ParseEvidence(rate.AcceptedEvidence).Select(e => e.ToString()).ToList(),
            c.VotingDeadline, c.ApprovedAt, c.MkobaReference, c.PaidAt, paidConfirmedBy = UserName(c.PaidConfirmedByUserId),
            createdBy = UserName(c.CreatedByUserId), c.CreatedAt, c.ClosedReason, closedBy = UserName(c.ClosedByUserId),
            required = gov?.RequiredApprovals, minLeaders = gov?.MinLeaderApprovals, rejectionsToReject = gov?.RejectionsToReject, eligibleVoters,
            evidence = c.Evidence.OrderBy(e => e.UploadedAt).Select(e => new
            {
                e.Id, documentType = e.DocumentType.ToString(), e.DocumentNumber, e.FileName, e.ContentType, e.SizeBytes, e.Sha256, status = e.Status.ToString(),
                uploadedBy = UserName(e.UploadedByUserId), e.UploadedAt, verifiedBy = UserName(e.VerifiedByUserId), e.VerifiedAt, e.ReviewNote,
                canView = isLeader || me.Id == c.BeneficiaryGroupMemberId,
                canVerify = isLeader && verifierRoles.Contains(Common.MemberAccess.EffectiveRole(me)) && e.Status == EvidenceStatus.Uploaded && e.UploadedByUserId != userId && c.CreatedByUserId != userId && me.Id != c.BeneficiaryGroupMemberId && !conflicted
            }),
            votes = votes.Select(v => new { name = members.FirstOrDefault(m => m.Id == v.GroupMemberId)?.User?.FullName, role = v.RoleAtVote.ToString(), v.Approve, v.Reason, v.At }),
            approvals = votes.Count(v => v.Approve), rejections = votes.Count(v => !v.Approve),
            leaderApprovals = votes.Count(v => v.Approve && NotificationService.LeaderRoles.Contains(v.RoleAtVote)),
            canVote,
            canUpload = (isLeader || me.Id == c.BeneficiaryGroupMemberId) && (c.IsHistorical ? c.Status == BenefitCaseStatus.HistoricalPendingConfirmation
                                                                                           : c.Status is BenefitCaseStatus.AwaitingEvidence or BenefitCaseStatus.AwaitingVerification),
            canConfirmPayment = pay.CanConfirm, confirmBlockedReason = pay.ConfirmReason, confirmBlockedReasonLoc = pay.ConfirmReasonLoc,
            canInitiatePayment = pay.CanInitiate, initiateBlockedReason = pay.InitiateReason, initiateBlockedReasonLoc = pay.InitiateReasonLoc, cash = pay.Cash,
            deductionDrift = pay.Drift is { } dd ? new { dd.Frozen, dd.Current, dd.Excess, message = dd.Message, messageLoc = dd.MessageLoc } : null,
            c.PolicyVersion, paymentInitiatedBy = UserName(c.PaymentInitiatedByUserId), c.PaymentInitiatedAt,
            paymentInitiatedToPhone = isLeader || me.Id == c.BeneficiaryGroupMemberId ? c.PaymentInitiatedToPhone : null,
            c.CashAvailableAtInitiation, iAmConflicted = conflicted, ledger = ledgerCheck,
            canConfirmHistorical = isLeader && c.Status == BenefitCaseStatus.HistoricalPendingConfirmation && c.CreatedByUserId != userId && me.Id != c.BeneficiaryGroupMemberId
                                   && histBlocked is null,
            confirmHistoricalBlockedReason = histBlocked,
            canCancel = isLeader
                ? c.Status is not (BenefitCaseStatus.Paid or BenefitCaseStatus.Rejected or BenefitCaseStatus.Cancelled)
                : c.CreatedByUserId == userId && c.Status is BenefitCaseStatus.AwaitingEvidence or BenefitCaseStatus.AwaitingVerification,
            paymentCandidates = candidates
        };
    }

    public async Task<object> PolicyViewAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var viewer = await RequireMemberAsync(groupId, userId, ct);
        var govs = await _context.BenefitGovernances.AsNoTracking().Where(g => g.GroupId == groupId).OrderByDescending(g => g.RequestedAt).ToListAsync(ct);
        var rates = await _context.BenefitRates.AsNoTracking().Where(r => r.GroupId == groupId).OrderBy(r => r.Category).ThenBy(r => r.Relationship).ThenByDescending(r => r.EffectiveFrom).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        string UserName(Guid? uid) => uid is null ? "" : names.FirstOrDefault(m => m.UserId == uid)?.User?.FullName ?? "?";
        var today = TruthLedgerService.NowEat().Date;
        var active = await GetActiveGovernanceAsync(groupId, ct);
        var todayUtc = DateOnlyUtc(today);
        object G(BenefitGovernance g) => new
        {
            g.Id, status = g.Status.ToString(), g.RequiredApprovals, g.MinLeaderApprovals, g.AllowMemberApprovers, g.RejectionsToReject, g.VotingDays,
            headcountRule = g.HeadcountRule.ToString(), g.HeadcountPaidWithinDays, g.ExcludeBeneficiaryFromHeadcount, fundingSource = g.FundingSource.ToString(),
            g.DeductJoinFee, g.DeductArrears, g.RequireNoArrearsForEligibility, g.MinMembershipDays, g.ClaimWindowDays, g.PaymentConfirmationDays,
            g.StatusChangeApprovals, g.ExpulsionVotePercent, g.Note, requestedBy = UserName(g.RequestedByUserId), g.RequestedAt,
            decidedBy = UserName(g.DecidedByUserId), g.DecidedAt, canDecide = g.Status == ProposalStatus.Pending && g.RequestedByUserId != userId,
            canCancel = g.Status == ProposalStatus.Pending && g.RequestedByUserId == userId, description = DescribeHeadcount(g),
            g.Version, g.RequireJoinFeePaid, g.CooldownDays, g.MaxBenefitAmountPerPeriod, g.MaxBenefitPeriodDays, g.RequireCashAvailability,
            paymentInitiatorRoles = ParseRoles(g.PaymentInitiatorRoles).Select(r => r.ToString()),
            paymentConfirmerRoles = ParseRoles(g.PaymentConfirmerRoles).Select(r => r.ToString()),
            g.ApproverCannotInitiatePayment, g.ExcludeRelatedParties, g.FrequentClaimsFlagCount, g.RecentPolicyChangeFlagDays,
            // SASISHO14
            g.CashCheckAtApproval, conflictRelationTypes = ParseRelationTypes(g.ConflictRelationTypes).Select(t => t.ToString()),
            approverRoles = ApproverRolesOf(g).Select(r => r.ToString()), evidenceVerifierRoles = VerifierRolesOf(g).Select(r => r.ToString()),
            auditors = ParseIds(g.AuditorMemberIds).Select(id => new { id, name = names.FirstOrDefault(m => m.Id == id)?.User?.FullName ?? names.FirstOrDefault(m => m.Id == id)?.MemberNumber ?? "?" }),
            g.HighValueThreshold, g.BlockSameSubjectAcrossMembers, g.DeductLoanArrears, g.EffectiveFrom, headcountAsOf = g.HeadcountAsOf.ToString(),
            isUpcoming = g.Status == ProposalStatus.Approved && g.EffectiveFrom > todayUtc,
            capacityWarnings = CapacityWarnings(g, names).Select(w => new { message = w.Text, tpl = w.Template, args = w.Args })
        };
        return new
        {
            active = active is null ? null : G(active),
            upcoming = govs.Where(g => g.Status == ProposalStatus.Approved && g.EffectiveFrom > todayUtc).OrderBy(g => g.EffectiveFrom).Select(G),
            iAmAuditor = active is not null && ParseIds(active.AuditorMemberIds).Contains(viewer.Id),
            iAmLeader = Common.MemberAccess.IsEffectiveLeader(viewer),
            pending = govs.Where(g => g.Status == ProposalStatus.Pending).Select(G),
            history = govs.Where(g => g.Status != ProposalStatus.Pending).Take(10).Select(G),
            rates = rates.Select(r => new
            {
                r.Id, category = r.Category.ToString(), relationship = r.Relationship.ToString(), r.PerMemberAmount,
                acceptedEvidence = ParseEvidence(r.AcceptedEvidence).Select(e => e.ToString()), r.MaxClaimsPerMember, r.IsDisabled, r.EffectiveFrom, r.EffectiveTo,
                status = r.Status.ToString(), r.Note, requestedBy = UserName(r.RequestedByUserId), decidedBy = UserName(r.DecidedByUserId),
                isCurrent = r.Status == ProposalStatus.Approved && r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today) && rates
                    .Where(x => x.Status == ProposalStatus.Approved && x.Category == r.Category && x.Relationship == r.Relationship && x.EffectiveFrom <= today
                                && (x.EffectiveTo == null || x.EffectiveTo >= today))
                    .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.DecidedAt).First().Id == r.Id,
                isExpired = r.Status == ProposalStatus.Approved && r.EffectiveTo is { } rto && rto < today,
                canDecide = r.Status == ProposalStatus.Pending && r.RequestedByUserId != userId,
                canCancel = r.Status == ProposalStatus.Pending && r.RequestedByUserId == userId
            })
        };
    }

    /// <summary>
    /// SASISHO15 (F-M3): je, kikundi kina viongozi WA KUTOSHA kutimiza sera hii? Katika hali mbaya kabisa aliyefungua kesi na
    /// Mhakiki ni viongozi (hawapigi kura) na mnufaika naye ni kiongozi. Kama waliobaki ni wachache kuliko kura za viongozi
    /// zinazohitajika, kesi kama hizo zitakataliwa mwanzoni ("hakuna wapiga kura wa kutosha") — tahadhari inaonyeshwa KABLA.
    /// </summary>
    private static List<Loc> CapacityWarnings(BenefitGovernance g, List<GroupMember> members)
    {
        var w = new List<Loc>();
        var inGroup = members.Where(m => NotificationService.InGroupStatuses.Contains(m.Status)).ToList();
        var approverRoles = ApproverRolesOf(g);
        var leaderApprovers = inGroup.Count(m => Common.MemberAccess.IsEffectiveLeader(m) && approverRoles.Contains(Common.MemberAccess.EffectiveRole(m)));
        var worst = Math.Max(0, leaderApprovers - 3);
        if (g.MinLeaderApprovals > 0 && worst < g.MinLeaderApprovals)
            w.Add(Loc.L($"Viongozi wanaoweza kupiga kura ni {leaderApprovers}. Kesi ikifunguliwa na kiongozi, ushahidi ukahakikiwa na kiongozi mwingine na mnufaika naye ni kiongozi, wanabaki {worst} — chini ya kura {g.MinLeaderApprovals} za viongozi zinazohitajika. Teua manaibu au punguza kura za viongozi."));
        var verifiers = inGroup.Count(m => VerifierRolesOf(g).Contains(m.Role));
        if (verifiers < 2)
            w.Add(Loc.L($"Kuna Mhakiki {verifiers} tu. Akiwa ndiye aliyefungua kesi, mnufaika au ndugu yake, hakuna atakayeweza kuhakiki ushahidi."));
        var initiators = inGroup.Where(m => ParseRoles(g.PaymentInitiatorRoles).Contains(m.Role)).Select(m => m.Id).ToHashSet();
        var confirmers = inGroup.Where(m => ParseRoles(g.PaymentConfirmerRoles).Contains(m.Role)).Select(m => m.Id).ToHashSet();
        if (initiators.Count == 0 || confirmers.Count == 0 || initiators.Union(confirmers).Count() < 2)
            w.Add(Loc.L($"Malipo yanahitaji watu wawili tofauti (anayeanzisha na anayethibitisha), lakini nafasi zilizochaguliwa zina watu {initiators.Union(confirmers).Count()} tu."));
        return w;
    }

    // ---- majina ----
    private async Task<string> MemberName(Guid memberId, CancellationToken ct)
        => await _context.GroupMembers.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.User != null ? m.User.FullName : m.MemberNumber).FirstOrDefaultAsync(ct) ?? "?";
    private async Task<string> CaseNumber(Guid caseId, CancellationToken ct)
        => await _context.BenefitCases.AsNoTracking().Where(c => c.Id == caseId).Select(c => c.CaseNumber).FirstOrDefaultAsync(ct) ?? "?";

    public static string CategoryName(BenefitCategory c) => c switch { BenefitCategory.Msiba => "Msiba", BenefitCategory.Sherehe => "Sherehe", _ => c.ToString() };
    public static string RelationshipName(BenefitRelationship r) => r switch
    {
        BenefitRelationship.Self => "mwanachama mwenyewe", BenefitRelationship.Spouse => "mwenza", BenefitRelationship.Child => "mtoto",
        BenefitRelationship.Parent => "mzazi", _ => r.ToString()
    };
    public static string EvidenceName(EvidenceDocumentType e) => e switch
    {
        EvidenceDocumentType.DeathCertificate => "Cheti cha kifo", EvidenceDocumentType.BurialPermit => "Kibali cha mazishi",
        EvidenceDocumentType.LocalGovernmentLetter => "Barua ya serikali ya mtaa", EvidenceDocumentType.MarriageCertificate => "Cheti cha ndoa",
        EvidenceDocumentType.BirthCertificate => "Cheti cha kuzaliwa", EvidenceDocumentType.HospitalLetter => "Barua ya hospitali", _ => "Hati nyingine"
    };
    public static string KindName(WithdrawalExplanationKind k) => k switch
    {
        WithdrawalExplanationKind.LoanDisbursement => "Mkopo", WithdrawalExplanationKind.GroupExpense => "Gharama ya kikundi",
        WithdrawalExplanationKind.BankTransfer => "Uhamisho benki", WithdrawalExplanationKind.Refund => "Marejesho", _ => "Mengineyo"
    };
}
