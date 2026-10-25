using ChamaLink.Domain.Exceptions;
using System.Security.Cryptography;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Ledger;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

using ChamaLink.Infrastructure.Services.Common;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// UPANDE WA KUANDIKA wa Injini ya Ukweli — kila kitu kinachobadilisha "ukweli" kiko hapa,
/// na kila kinachotoka kwa binadamu (manual, msamaha, pengo) kina maker-checker:
/// aliyeomba HAWEZI kuidhinisha ombi lake mwenyewe.
/// </summary>
public class TruthSetupService
{
    private readonly ApplicationDbContext _context;
    private readonly IMKobaParserService _parser;
    private readonly TruthLedgerService _truth;
    public TruthSetupService(ApplicationDbContext context, IMKobaParserService parser, TruthLedgerService truth)
    {
        _context = context;
        _parser = parser;
        _truth = truth;
    }

    // =====================================================================
    // 0. KUMBUKUMBU ZA UKAGUZI (AuditEvents)
    // Kila kitendo kinachobadilisha ukweli wa fedha kinaandika AuditEvent ndani ya SaveChanges ILE ILE
    // ya kitendo chenyewe: kama kitendo kimehifadhiwa, kumbukumbu yake imehifadhiwa — hakuna moja bila jingine.
    // AuditEvents HAZIFUTWI kamwe (hata na "futa data za majaribio").
    // =====================================================================
    private Task AddAuditAsync(Guid groupId, Guid? memberId, Guid actorUserId, AuditAction action,
        string entityType, Guid entityId, object? before, object after, string? sourceReference, string? reason, CancellationToken ct)
        => AuditTrail.AddAsync(_context, groupId, memberId, actorUserId, action, entityType, entityId, before, after, sourceReference, reason, ct);

    private static object Snap(TruthGroupSettings t) => new
    {
        t.MonthlyContribution, t.LateFine, t.JoiningFee, t.DueDateDay, t.GracePeriodDays, t.FineOnPartialPayment,
        t.AutoDeductSavings, t.ContributionStartDate, t.FoundingMembersCutoff, t.ManualEntryCutoff, t.LoanLatePenaltyAmount, t.LateFinePercent,
        t.LoansEnabled, t.LoanInterestPercent, t.LoanRepaymentMonths, t.LoanMaxAmount, t.LoanMaxMultipleOfContributions, t.LoanMinMembershipMonths,
        t.LoanAllowWithArrears, t.LoanAllowMultipleActive, t.LoanMinCashReserve, t.LoanRequiredApprovals, t.LoanApprovalValidDays,
        t.RoleChangeApprovals, t.LeaderCoolingOffDays, t.CashStatementMaxAgeDays,
        t.MemberMinimumBalance, t.EventTopUpWindowDays, t.OverdueEventFineAmount, t.MaxEventDeductionPerMemberPerMonth,
        t.MaxEventsPerMonth, t.BeneficiaryContributes,
        t.EventFineGraceDays, t.RemovalApprovalsRequired, t.ReinstatementFineMultiplier, t.ReinstatementRequiresJoiningFee,
        t.PaymentSecondApprovalAmount, t.ClaimsEnabled, t.JoiningPaymentRequired, t.JoiningSavingsPortion,
        t.JoiningPaymentWindowDays, t.SmsEnabled, t.SmsLowCreditThreshold, t.SmsQuietFromHour, t.SmsQuietToHour,
        t.SmsMaxPerMemberPerEvent, t.SmsMaxCharsPerMessage, t.AppealsEnabled, t.AppealWindowDays, t.AppealApprovalsRequired
    };
    private static object Snap(FinancialEvent e) => new
    {
        e.Type, e.Amount, e.OccurredAt, Status = e.Status.ToString(), e.Reason, EvidenceType = e.EvidenceType?.ToString(),
        e.EvidenceReference, e.CreatedBy, e.ApprovedByGroupMemberId, e.ApprovedAt, e.ApprovalNote
    };
    private static object Snap(TruthAdjustmentRecord a) => new
    {
        Kind = a.Kind.ToString(), a.GroupMemberId, a.Year, a.Month, a.ExtraDays, a.Reason, Status = a.Status.ToString(),
        a.RequestedByUserId, a.DecidedByUserId, a.DecidedAt
    };
    private static object Snap(MkobaStatementGap g) => new
    {
        g.AfterReference, g.BeforeReference, g.Amount, g.AssignedGroupMemberId, g.Evidence, g.AssumedTimestamp,
        Status = g.ResolutionStatus?.ToString(), g.RequestedByUserId, g.ApprovedByUserId, g.ApprovedAt
    };

    // =====================================================================
    // 1. MIPANGILIO YA KIKUNDI (hakuna hardcode)
    // =====================================================================
    public sealed class SettingsDto
    {
        public DateTime? ContributionStartDate { get; set; }
        public int? DueDateDay { get; set; }
        public int? GracePeriodDays { get; set; }
        public bool? FineOnPartialPayment { get; set; }
        public bool? AutoDeductSavings { get; set; }
        public DateTime? ManualEntryCutoff { get; set; }
        public DateTime? FoundingMembersCutoff { get; set; }
        public decimal? MonthlyContribution { get; set; }
        public decimal? LateFine { get; set; }
        /// <summary>"Fixed" (kiasi kamili = LateFine) au "Percent" (asilimia ya mchango = LateFinePercent, Katiba 13: 50%).</summary>
        public string? LateFineMode { get; set; }
        public decimal? LateFinePercent { get; set; }
        public decimal? JoiningFee { get; set; }
        public decimal? LoanLatePenaltyAmount { get; set; }
        // SASISHO15 — sera ya mikopo (AWAMU L). null = haibadiliki. Kwa vikomo viwili, 0 = ondoa kikomo hicho.
        public bool? LoansEnabled { get; set; }
        public decimal? LoanInterestPercent { get; set; }
        public int? LoanRepaymentMonths { get; set; }
        public decimal? LoanMaxAmount { get; set; }
        public decimal? LoanMaxMultipleOfContributions { get; set; }
        public int? LoanMinMembershipMonths { get; set; }
        public bool? LoanAllowWithArrears { get; set; }
        public bool? LoanAllowMultipleActive { get; set; }
        public decimal? LoanMinCashReserve { get; set; }
        public int? LoanRequiredApprovals { get; set; }
        public int? LoanApprovalValidDays { get; set; }
        // SASISHO17: utawala na usalama wa pesa
        /// <summary>Kura za NDIYO zinazohitajika kubadilisha cheo (1..10). Bila sera: 1.</summary>
        public int? RoleChangeApprovals { get; set; }
        /// <summary>Siku kabla kiongozi mpya hajapata nguvu za kuidhinisha (0..90). Bila sera: 0.</summary>
        public int? LeaderCoolingOffDays { get; set; }
        /// <summary>Umri wa juu (siku) wa taarifa ya M-Koba kabla ya kuidhinisha malipo ya pesa (1..120). Bila sera: malipo yanazuiwa.</summary>
        public int? CashStatementMaxAgeDays { get; set; }

        // SASISHO18 — sera ya vikundi vinavyochangishana KILA TUKIO (pochi + floor).
        /// <summary>Kiwango cha chini (floor) cha salio la mwanachama. 0 = hakuna floor.</summary>
        public decimal? MemberMinimumBalance { get; set; }
        /// <summary>Siku za kujaza salio baada ya tukio (0..365). 0 = hakuna muda maalum.</summary>
        public int? EventTopUpWindowDays { get; set; }
        /// <summary>Faini ya deni la tukio linapochelewa. 0 = hakuna faini.</summary>
        public decimal? OverdueEventFineAmount { get; set; }
        /// <summary>Kikomo cha makato ya matukio kwa mwanachama kwa mwezi. 0 = hakuna kikomo.</summary>
        public decimal? MaxEventDeductionPerMemberPerMonth { get; set; }
        /// <summary>Idadi ya juu ya matukio yanayoidhinishwa kwa mwezi (0..100). 0 = hakuna kikomo.</summary>
        public int? MaxEventsPerMonth { get; set; }
        /// <summary>true = mnufaika naye anachangia tukio lake mwenyewe.</summary>
        public bool? BeneficiaryContributes { get; set; }

        // SASISHO19 — kanuni za J3: faini -> neema -> hatari ya kuondolewa -> kurudi
        /// <summary>Siku za neema baada ya faini kabla ya kuingia kwenye orodha ya "hatarini" (0..90).</summary>
        public int? EventFineGraceDays { get; set; }
        /// <summary>Idadi ya chini ya viongozi wanaoidhinisha kuondolewa (0..20). Kamwe chini ya 3.</summary>
        public int? RemovalApprovalsRequired { get; set; }
        /// <summary>Kizidishi cha faini kwa anayerudi (mf. 2 = faini x 2). 0 = hakuna ada ya kurudi.</summary>
        public decimal? ReinstatementFineMultiplier { get; set; }
        /// <summary>true = anayerudi analipa tena ada ya kujiunga.</summary>
        public bool? ReinstatementRequiresJoiningFee { get; set; }

        // SASISHO21 — sanduku la malipo, lango la kujiunga, SMS
        /// <summary>Kiasi kinachohitaji kiongozi wa PILI kuthibitisha malipo yasiyotambulika. 0 = mkono mmoja.</summary>
        public decimal? PaymentSecondApprovalAmount { get; set; }
        /// <summary>Madai ya wanachama ("nimelipa") yanaruhusiwa?</summary>
        public bool? ClaimsEnabled { get; set; }
        /// <summary>true = mtu anakuwa mwanachama tu baada ya malipo ya kujiunga kuthibitishwa.</summary>
        public bool? JoiningPaymentRequired { get; set; }
        /// <summary>Sehemu ya ada ya kujiunga inayoingia akiba (iliyobaki = kianzio). 0 = yote kianzio.</summary>
        public decimal? JoiningSavingsPortion { get; set; }
        /// <summary>Siku za ombi la kujiunga kusubiri malipo (0 = haliondolewi).</summary>
        public int? JoiningPaymentWindowDays { get; set; }
        public bool? SmsEnabled { get; set; }
        public int? SmsLowCreditThreshold { get; set; }
        public int? SmsQuietFromHour { get; set; }
        public int? SmsQuietToHour { get; set; }
        public int? SmsMaxPerMemberPerEvent { get; set; }
        /// <summary>Herufi kwa SMS moja (0 = 160 za kawaida ya GSM-7). Ujumbe mrefu = SMS zaidi.</summary>
        public int? SmsMaxCharsPerMessage { get; set; }
        // SASISHO21 — rufaa
        public bool? AppealsEnabled { get; set; }
        /// <summary>Siku za kuwasilisha rufaa tangu uamuzi (0 = hakuna kikomo).</summary>
        public int? AppealWindowDays { get; set; }
        /// <summary>Viongozi wanaohitajika kuamua rufaa (kamwe chini ya 2).</summary>
        public int? AppealApprovalsRequired { get; set; }
    }

    public sealed record SettingsProposalResult(bool Applied, Guid? ProposalId, string Ujumbe);

    /// <summary>
    /// Kubadilisha kanuni za fedha. Mipangilio ya KWANZA inaanza kutumika moja kwa moja (kikundi kipya kiweze kuanza);
    /// kila badiliko baada ya hapo linakuwa PENDEKEZO linalosubiri kiongozi wa pili, kwa sababu linabadilisha deni la kila mtu.
    /// </summary>
    public async Task<SettingsProposalResult> ProposeSettingsAsync(Guid groupId, SettingsDto dto, Guid userId, string? note, CancellationToken ct = default)
    {
        if (!await _context.TruthGroupSettings.AnyAsync(s => s.GroupId == groupId, ct))
        {
            await UpdateSettingsAsync(groupId, dto, userId, ct);
            return new SettingsProposalResult(true, null, "Mipangilio ya kwanza imehifadhiwa. Mabadiliko yajayo yatahitaji idhini ya kiongozi wa pili.");
        }
        if (await _context.TruthSettingsProposals.AnyAsync(p => p.GroupId == groupId && p.Status == ProposalStatus.Pending, ct))
            throw new ConflictException("Kuna pendekezo la kubadilisha kanuni linalosubiri idhini. Liamuliwe (au aliyeliomba alighairi) kwanza.");
        ValidateSettings(dto); // kosa lionekane sasa, si kiongozi wa pili akiidhinisha
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var current = await _context.TruthGroupSettings.AsNoTracking().FirstAsync(x => x.GroupId == groupId, ct);
        if (!WouldChange(current, dto)) throw new ArgumentException("Hakuna badiliko lililopendekezwa — kanuni zote ni sawa na zinazotumika sasa.");
        // SASISHO15: sera ya mikopo ikifunguliwa, ukamilifu unakaguliwa SASA (kanuni za sasa + pendekezo), si kiongozi wa pili akiidhinisha
        var merged = MergeLoanFields(current, dto);
        if (merged.LoansEnabled && Services.Loans.LoanApplicationService.MissingPolicy(merged) is { Count: > 0 } pmiss)
            throw new ArgumentException("Kufungua mikopo kunahitaji sera kamili: " + string.Join("; ", pmiss) + ".");
        var p = new TruthSettingsProposal { Id = Guid.NewGuid(), GroupId = groupId, PayloadJson = json, RequestedByUserId = userId, Note = note?.Trim() };
        _context.TruthSettingsProposals.Add(p);
        await AddAuditAsync(groupId, null, userId, AuditAction.GroupPolicyCreated, nameof(TruthSettingsProposal), p.Id, null, dto, null,
            "Pendekezo la kubadilisha kanuni za fedha (linasubiri kiongozi wa pili)", ct);
        await _context.SaveChangesAsync(ct);
        return new SettingsProposalResult(false, p.Id, "Pendekezo limehifadhiwa. Litaanza kutumika kiongozi WA PILI akiidhinisha.");
    }

    /// <summary>Nakala ya kanuni za sasa ikiwa na sehemu za mikopo za pendekezo (kwa ukaguzi wa ukamilifu tu; haihifadhiwi).</summary>
    private static TruthGroupSettings MergeLoanFields(TruthGroupSettings cur, SettingsDto d) => new()
    {
        LoansEnabled = d.LoansEnabled ?? cur.LoansEnabled,
        LoanInterestPercent = d.LoanInterestPercent ?? cur.LoanInterestPercent,
        LoanRepaymentMonths = d.LoanRepaymentMonths ?? cur.LoanRepaymentMonths,
        LoanRequiredApprovals = d.LoanRequiredApprovals ?? cur.LoanRequiredApprovals,
        LoanApprovalValidDays = d.LoanApprovalValidDays ?? cur.LoanApprovalValidDays,
        LoanMaxAmount = d.LoanMaxAmount is { } a ? (a == 0 ? null : a) : cur.LoanMaxAmount,
        LoanMaxMultipleOfContributions = d.LoanMaxMultipleOfContributions is { } x ? (x == 0 ? null : x) : cur.LoanMaxMultipleOfContributions
    };

    /// <summary>Uhakiki wa thamani (bila kubadilisha kitu) — sheria zilezile za UpdateSettingsAsync.</summary>
    private static void ValidateSettings(SettingsDto d)
    {
        if (d.DueDateDay is < 1 or > 28) throw new ArgumentException("DueDateDay 1..28");
        if (d.GracePeriodDays is < 0 or > 25) throw new ArgumentException("GracePeriodDays 0..25");
        if (d.MonthlyContribution is <= 0) throw new ArgumentException("MonthlyContribution > 0");
        if (d.LateFine is < 0) throw new ArgumentException("LateFine >= 0");
        if (d.JoiningFee is < 0) throw new ArgumentException("JoiningFee >= 0");
        if (d.LoanLatePenaltyAmount is < 0) throw new ArgumentException("LoanLatePenaltyAmount >= 0");
        var mode = d.LateFineMode?.Trim();
        if (mode is not null && !string.Equals(mode, "Percent", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "Fixed", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LateFineMode iwe 'Fixed' au 'Percent'.");
        if (string.Equals(mode, "Fixed", StringComparison.OrdinalIgnoreCase) && d.LateFinePercent is not null)
            throw new ArgumentException("Chagua njia moja ya faini: kiasi kamili AU asilimia, si zote.");
        if (d.LateFinePercent is <= 0 or > 100) throw new ArgumentException("Asilimia ya faini iwe zaidi ya 0 na isizidi 100.");
        if (d.LoanInterestPercent is < 0 or > 100) throw new ArgumentException("Riba ya mkopo iwe kati ya 0 na 100 (asilimia).");
        if (d.LoanRepaymentMonths is < 1 or > 60) throw new ArgumentException("Miezi ya kurejesha mkopo iwe 1..60.");
        if (d.LoanMaxAmount is < 0) throw new ArgumentException("Kikomo cha mkopo kisiwe hasi (0 = ondoa kikomo).");
        if (d.LoanMaxMultipleOfContributions is < 0) throw new ArgumentException("Kikomo kwa michango kisiwe hasi (0 = ondoa kikomo).");
        if (d.LoanMinMembershipMonths is < 0) throw new ArgumentException("Miezi ya uanachama kabla ya kukopa isiwe hasi.");
        if (d.LoanMinCashReserve is < 0) throw new ArgumentException("Akiba ya lazima isiwe hasi.");
        if (d.LoanRequiredApprovals is < 2) throw new ArgumentException("Mkopo unahitaji kura za viongozi angalau 2 (mtu mmoja hawezi kutoa pesa).");
        if (d.LoanApprovalValidDays is < 1 or > 90) throw new ArgumentException("Siku za idhini ya mkopo ziwe 1..90.");
        if (d.RoleChangeApprovals is < 1 or > 10) throw new ArgumentException("Kura za kubadilisha cheo ziwe 1..10.");
        if (d.LeaderCoolingOffDays is < 0 or > 90) throw new ArgumentException("Siku za kusubiri za kiongozi mpya ziwe 0..90.");
        if (d.CashStatementMaxAgeDays is < 1 or > 120) throw new ArgumentException("Umri wa juu wa taarifa ya M-Koba uwe siku 1..120.");
        // SASISHO18 — sera ya matukio
        if (d.MemberMinimumBalance is < 0) throw new ArgumentException("Kiwango cha chini cha salio (floor) kisiwe hasi.");
        if (d.EventTopUpWindowDays is < 0 or > 365) throw new ArgumentException("Siku za kujaza salio ziwe 0..365.");
        if (d.OverdueEventFineAmount is < 0) throw new ArgumentException("Faini ya deni la tukio isiwe hasi.");
        if (d.MaxEventDeductionPerMemberPerMonth is < 0) throw new ArgumentException("Kikomo cha makato kwa mwezi kisiwe hasi.");
        if (d.MaxEventsPerMonth is < 0 or > 100) throw new ArgumentException("Idadi ya juu ya matukio kwa mwezi iwe 0..100.");
        // SASISHO19 — kanuni za J3
        if (d.EventFineGraceDays is < 0 or > 90) throw new ArgumentException("Siku za neema baada ya faini ziwe 0..90.");
        if (d.RemovalApprovalsRequired is < 0 or > 20) throw new ArgumentException("Idadi ya viongozi wanaoidhinisha kuondolewa iwe 0..20 (0 = tumia sera, kamwe chini ya 3).");
        if (d.ReinstatementFineMultiplier is < 0 or > 10) throw new ArgumentException("Kizidishi cha faini ya kurudi kiwe 0..10.");
        if (d.ReinstatementFineMultiplier is > 0 && d.ReinstatementFineMultiplier < 1) throw new ArgumentException("Kizidishi cha faini ya kurudi kiwe 0 (hakuna) au kuanzia 1.");
        // SASISHO21 — sanduku la malipo + lango + SMS
        if (d.PaymentSecondApprovalAmount is < 0) throw new ArgumentException("Kikomo cha mkono wa pili kisiwe hasi (0 = hakuna).");
        if (d.JoiningSavingsPortion is < 0) throw new ArgumentException("Sehemu ya akiba ya kujiunga isiwe hasi.");
        if (d.JoiningPaymentWindowDays is < 0 or > 90) throw new ArgumentException("Siku za malipo ya kujiunga ziwe 0..90.");
        if (d.SmsLowCreditThreshold is < 0) throw new ArgumentException("Kiwango cha onyo la salio la SMS kisiwe hasi.");
        if (d.SmsQuietFromHour is < 0 or > 23) throw new ArgumentException("Saa ya kuanza SMS iwe 0..23.");
        if (d.SmsQuietToHour is < 1 or > 24) throw new ArgumentException("Saa ya kuacha SMS iwe 1..24.");
        if ((d.SmsQuietFromHour is { } qf && d.SmsQuietToHour is { } qt && qf >= qt))
            throw new ArgumentException("Saa ya kuanza SMS lazima iwe kabla ya saa ya kuacha.");
        if (d.SmsMaxPerMemberPerEvent is < 0 or > 20) throw new ArgumentException("SMS kwa mwanachama kwa tukio ziwe 0..20.");
        if (d.SmsMaxCharsPerMessage is < 0 or > 480) throw new ArgumentException("Herufi kwa SMS moja ziwe 0..480 (0 = 160).");
        if (d.AppealWindowDays is < 0 or > 365) throw new ArgumentException("Siku za rufaa ziwe 0..365.");
        // Kanuni: rufaa HAIRUHUSIWI kuamuliwa na kiongozi mmoja.
        if (d.AppealsEnabled == true && d.AppealApprovalsRequired is { } ap && ap < 2)
            throw new ArgumentException("Rufaa inahitaji viongozi angalau 2 — mmoja hawezi kuamua.");
    }

    /// <summary>Je, pendekezo linabadilisha angalau kanuni moja? (fomu inatuma sehemu zote, hata zisizobadilika)</summary>
    private static bool WouldChange(TruthGroupSettings cur, SettingsDto d)
    {
        static bool Diff<T>(T? v, T now) where T : struct => v is { } x && !EqualityComparer<T>.Default.Equals(x, now);
        var mode = d.LateFineMode?.Trim() ?? (d.LateFinePercent is not null ? "Percent" : null);
        var curMode = cur.LateFinePercent is null ? "Fixed" : "Percent";
        return (d.ContributionStartDate is { } s && (s.Year != cur.ContributionStartDate.Year || s.Month != cur.ContributionStartDate.Month))
               || Diff(d.DueDateDay, cur.DueDateDay) || Diff(d.GracePeriodDays, cur.GracePeriodDays)
               || Diff(d.FineOnPartialPayment, cur.FineOnPartialPayment) || Diff(d.AutoDeductSavings, cur.AutoDeductSavings)
               || (d.ManualEntryCutoff is { } mc && mc != cur.ManualEntryCutoff)
               || (d.FoundingMembersCutoff is { } fc && fc != cur.FoundingMembersCutoff)
               || Diff(d.MonthlyContribution, cur.MonthlyContribution) || Diff(d.LateFine, cur.LateFine)
               || (mode is not null && !string.Equals(mode, curMode, StringComparison.OrdinalIgnoreCase))
               || (d.LateFinePercent is { } p && p != cur.LateFinePercent)
               || Diff(d.JoiningFee, cur.JoiningFee) || Diff(d.LoanLatePenaltyAmount, cur.LoanLatePenaltyAmount)
               || Diff(d.LoansEnabled, cur.LoansEnabled) || (d.LoanInterestPercent is { } li && li != cur.LoanInterestPercent)
               || (d.LoanRepaymentMonths is { } lr && lr != cur.LoanRepaymentMonths)
               || (d.LoanMaxAmount is { } lm && (lm == 0 ? cur.LoanMaxAmount is not null : lm != cur.LoanMaxAmount))
               || (d.LoanMaxMultipleOfContributions is { } lx && (lx == 0 ? cur.LoanMaxMultipleOfContributions is not null : lx != cur.LoanMaxMultipleOfContributions))
               || (d.LoanMinMembershipMonths is { } lmm && lmm != cur.LoanMinMembershipMonths)
               || Diff(d.LoanAllowWithArrears, cur.LoanAllowWithArrears) || Diff(d.LoanAllowMultipleActive, cur.LoanAllowMultipleActive)
               || Diff(d.LoanMinCashReserve, cur.LoanMinCashReserve)
               || (d.LoanRequiredApprovals is { } lra && lra != cur.LoanRequiredApprovals)
               || (d.LoanApprovalValidDays is { } lvd && lvd != cur.LoanApprovalValidDays)
               || Diff(d.MemberMinimumBalance, cur.MemberMinimumBalance)
               || Diff(d.EventTopUpWindowDays, cur.EventTopUpWindowDays)
               || Diff(d.OverdueEventFineAmount, cur.OverdueEventFineAmount)
               || Diff(d.MaxEventDeductionPerMemberPerMonth, cur.MaxEventDeductionPerMemberPerMonth)
               || Diff(d.MaxEventsPerMonth, cur.MaxEventsPerMonth)
               || Diff(d.BeneficiaryContributes, cur.BeneficiaryContributes)
               || Diff(d.EventFineGraceDays, cur.EventFineGraceDays)
               || Diff(d.RemovalApprovalsRequired, cur.RemovalApprovalsRequired)
               || Diff(d.ReinstatementFineMultiplier, cur.ReinstatementFineMultiplier)
               || Diff(d.ReinstatementRequiresJoiningFee, cur.ReinstatementRequiresJoiningFee)
               || (d.RoleChangeApprovals is { } rca && rca != cur.RoleChangeApprovals)
               || (d.LeaderCoolingOffDays is { } lco && lco != cur.LeaderCoolingOffDays)
               || (d.CashStatementMaxAgeDays is { } csa && csa != cur.CashStatementMaxAgeDays)
               || Diff(d.PaymentSecondApprovalAmount, cur.PaymentSecondApprovalAmount)
               || Diff(d.ClaimsEnabled, cur.ClaimsEnabled)
               || Diff(d.JoiningPaymentRequired, cur.JoiningPaymentRequired)
               || Diff(d.JoiningSavingsPortion, cur.JoiningSavingsPortion)
               || Diff(d.JoiningPaymentWindowDays, cur.JoiningPaymentWindowDays)
               || Diff(d.SmsEnabled, cur.SmsEnabled)
               || Diff(d.SmsLowCreditThreshold, cur.SmsLowCreditThreshold)
               || Diff(d.SmsQuietFromHour, cur.SmsQuietFromHour)
               || Diff(d.SmsQuietToHour, cur.SmsQuietToHour)
               || Diff(d.SmsMaxPerMemberPerEvent, cur.SmsMaxPerMemberPerEvent)
               || Diff(d.SmsMaxCharsPerMessage, cur.SmsMaxCharsPerMessage)
               || Diff(d.AppealsEnabled, cur.AppealsEnabled)
               || Diff(d.AppealWindowDays, cur.AppealWindowDays)
               || Diff(d.AppealApprovalsRequired, cur.AppealApprovalsRequired);
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<TruthSettingsProposal> DecideSettingsAsync(Guid groupId, Guid proposalId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideSettingsCoreAsync(groupId, proposalId, approve, note, userId, ct), ct);

    private async Task<TruthSettingsProposal> DecideSettingsCoreAsync(Guid groupId, Guid proposalId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        var p = await _context.TruthSettingsProposals.FirstOrDefaultAsync(x => x.Id == proposalId && x.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Pendekezo halipo.");
        if (p.Status != ProposalStatus.Pending) throw new ConflictException($"Pendekezo tayari ni {p.Status}.");
        var cancelling = p.RequestedByUserId == userId;
        if (cancelling && approve) throw new ForbiddenException("Aliyependekeza HAWEZI kuidhinisha mwenyewe (maker-checker). Unaweza kulighairi tu.");
        if (approve)
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<SettingsDto>(p.PayloadJson) ?? new SettingsDto();
            await UpdateSettingsAsync(groupId, dto, userId, ct); // inathibitisha + audit ya kabla/baada
        }
        p.Status = approve ? ProposalStatus.Approved : ProposalStatus.Rejected;
        p.DecidedByUserId = userId; p.DecidedAt = DateTime.UtcNow; p.DecisionNote = cancelling ? "Limeghairiwa na aliyelipendekeza" : note?.Trim();
        await AddAuditAsync(groupId, null, userId, approve ? AuditAction.GroupPolicyVersioned : AuditAction.GovernanceRejected, nameof(TruthSettingsProposal), p.Id,
            null, new { Status = p.Status.ToString() }, null, p.DecisionNote, ct);
        await _context.SaveChangesAsync(ct);
        return p;
    }

    public async Task<List<object>> SettingsProposalsAsync(Guid groupId, Guid viewerUserId, CancellationToken ct = default)
    {
        var list = await _context.TruthSettingsProposals.AsNoTracking().Where(p => p.GroupId == groupId).OrderByDescending(p => p.RequestedAt).Take(20).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId)
            .GroupBy(m => m.UserId).ToDictionaryAsync(g => g.Key, g => g.First().User!.FullName, ct);
        return list.Select(p => (object)new
        {
            p.Id, status = p.Status.ToString(), changes = System.Text.Json.JsonSerializer.Deserialize<SettingsDto>(p.PayloadJson), p.Note,
            requestedBy = names.GetValueOrDefault(p.RequestedByUserId), p.RequestedAt, decidedBy = p.DecidedByUserId is { } d ? names.GetValueOrDefault(d) : null,
            p.DecidedAt, p.DecisionNote,
            canDecide = p.Status == ProposalStatus.Pending && p.RequestedByUserId != viewerUserId,
            canCancel = p.Status == ProposalStatus.Pending && p.RequestedByUserId == viewerUserId
        }).ToList();
    }

    public async Task<TruthGroupSettings> UpdateSettingsAsync(Guid groupId, SettingsDto dto, Guid userId, CancellationToken ct = default)
    {
        var groupType = await _context.Groups.AsNoTracking().Where(g => g.Id == groupId)
            .Select(g => (GroupType?)g.Type).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Kikundi hakipo.");
        // SASISHO18: kikundi cha "kila tukio" hakina mchango wa mwezi — hakilazimishwi kuuweka.
        var isEventBased = groupType == GroupType.EventBased;

        var tracked = await _context.TruthGroupSettings.FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        var before = tracked is null ? null : Snap(tracked);
        if (tracked is null)
        {
            // Mara ya kwanza: KILA sheria ya fedha lazima itoke kwa viongozi — hakuna default ya kiasi/siku.
            var missing = new List<string>();
            // Mwezi wa kumbukumbu na ada ya kujiunga ni vya aina ZOTE mbili za kikundi.
            if (dto.ContributionStartDate is null) missing.Add("mwezi wa kwanza wa kumbukumbu (ContributionStartDate)");
            if (dto.JoiningFee is null) missing.Add("Kianzio (JoiningFee; 0 kama hakuna)");
            // SASISHO21: sheria za MWEZI zinahitajika tu kwa kikundi cha "kila mwezi".
            // Kikundi cha "kila tukio" kinatumia pochi, floor na faini ya tukio — si mchango wa mwezi.
            if (!isEventBased)
            {
                if (dto.MonthlyContribution is null) missing.Add("mchango wa mwezi (MonthlyContribution)");
                if (dto.LateFine is null) missing.Add("faini ya kuchelewa (LateFine; 0 kama hakuna)");
                if (dto.DueDateDay is null) missing.Add("tarehe ya mwisho ya mwezi unaofuata (DueDateDay)");
                if (dto.GracePeriodDays is null) missing.Add("siku za neema (GracePeriodDays; 0 kama hakuna)");
                if (dto.FineOnPartialPayment is null) missing.Add("faini kwa malipo pungufu ndiyo/hapana (FineOnPartialPayment)");
                if (dto.AutoDeductSavings is null) missing.Add("akiba ilipe mwezi usiolipwa ndiyo/hapana (AutoDeductSavings)");
            }
            if (missing.Count > 0)
                throw new ValidationException($"Mipangilio ya kwanza haijakamilika. Weka: {string.Join("; ", missing)}.");   // SASISHO17: kiolezo kinachotafsirika
            tracked = new TruthGroupSettings { GroupId = groupId };
            _context.TruthGroupSettings.Add(tracked);
        }

        if (dto.DueDateDay is { } dd) { if (dd is < 1 or > 28) throw new ArgumentException("DueDateDay 1..28"); tracked.DueDateDay = dd; }
        if (dto.GracePeriodDays is { } g) { if (g is < 0 or > 25) throw new ArgumentException("GracePeriodDays 0..25"); tracked.GracePeriodDays = g; }
        if (dto.ContributionStartDate is { } s) tracked.ContributionStartDate = DateTime.SpecifyKind(new DateTime(s.Year, s.Month, 1), DateTimeKind.Utc);
        if (dto.FineOnPartialPayment is { } f) tracked.FineOnPartialPayment = f;
        if (dto.AutoDeductSavings is { } ads) tracked.AutoDeductSavings = ads;
        if (dto.ManualEntryCutoff is { } mc) tracked.ManualEntryCutoff = DateTime.SpecifyKind(mc, DateTimeKind.Utc);
        tracked.UpdatedAt = DateTime.UtcNow;
        tracked.UpdatedByUserId = userId;
        if (dto.FoundingMembersCutoff is { } fc) tracked.FoundingMembersCutoff = DateTime.SpecifyKind(fc, DateTimeKind.Utc);
        if (dto.MonthlyContribution is { } m)
        {
            if (m < 0 || (!isEventBased && m <= 0)) throw new ArgumentException("MonthlyContribution > 0");
            tracked.MonthlyContribution = m;
        }
        if (dto.LateFine is { } lf) { if (lf < 0) throw new ArgumentException("LateFine >= 0"); tracked.LateFine = lf; }
        var mode = dto.LateFineMode?.Trim();
        if (mode is null && dto.LateFinePercent is not null) mode = "Percent";
        if (mode is not null)
        {
            if (string.Equals(mode, "Percent", StringComparison.OrdinalIgnoreCase))
            {
                var pct = dto.LateFinePercent ?? tracked.LateFinePercent
                          ?? throw new ArgumentException("Faini kwa asilimia inahitaji asilimia (LateFinePercent), mf. 50 = nusu ya mchango.");
                if (pct is <= 0 or > 100) throw new ArgumentException("Asilimia ya faini iwe zaidi ya 0 na isizidi 100.");
                tracked.LateFinePercent = decimal.Round(pct, 2);
            }
            else if (string.Equals(mode, "Fixed", StringComparison.OrdinalIgnoreCase))
            {
                if (dto.LateFinePercent is not null) throw new ArgumentException("Chagua njia moja ya faini: kiasi kamili AU asilimia, si zote.");
                tracked.LateFinePercent = null;
            }
            else throw new ArgumentException("LateFineMode iwe 'Fixed' au 'Percent'.");
        }
        if (dto.JoiningFee is { } jf) { if (jf < 0) throw new ArgumentException("JoiningFee >= 0"); tracked.JoiningFee = jf; }
        if (dto.LoanLatePenaltyAmount is { } lp) { if (lp < 0) throw new ArgumentException("LoanLatePenaltyAmount >= 0"); tracked.LoanLatePenaltyAmount = lp; }
        ValidateSettings(new SettingsDto
        {
            LoanInterestPercent = dto.LoanInterestPercent, LoanRepaymentMonths = dto.LoanRepaymentMonths, LoanMaxAmount = dto.LoanMaxAmount,
            LoanMaxMultipleOfContributions = dto.LoanMaxMultipleOfContributions, LoanMinMembershipMonths = dto.LoanMinMembershipMonths,
            LoanMinCashReserve = dto.LoanMinCashReserve, LoanRequiredApprovals = dto.LoanRequiredApprovals, LoanApprovalValidDays = dto.LoanApprovalValidDays,
            RoleChangeApprovals = dto.RoleChangeApprovals, LeaderCoolingOffDays = dto.LeaderCoolingOffDays, CashStatementMaxAgeDays = dto.CashStatementMaxAgeDays,
            // SASISHO18: kikundi cha "kila tukio" kinaweza kuwa na mchango wa mwezi 0.
            MonthlyContribution = isEventBased ? null : dto.MonthlyContribution
        });
        if (dto.LoansEnabled is { } le) tracked.LoansEnabled = le;
        if (dto.LoanInterestPercent is { } lip) tracked.LoanInterestPercent = decimal.Round(lip, 2);
        if (dto.LoanRepaymentMonths is { } lrm) tracked.LoanRepaymentMonths = lrm;
        if (dto.LoanMaxAmount is { } lma) tracked.LoanMaxAmount = lma == 0 ? null : lma;
        if (dto.LoanMaxMultipleOfContributions is { } lmx) tracked.LoanMaxMultipleOfContributions = lmx == 0 ? null : lmx;
        if (dto.LoanMinMembershipMonths is { } lmn) tracked.LoanMinMembershipMonths = lmn;
        if (dto.LoanAllowWithArrears is { } law) tracked.LoanAllowWithArrears = law;
        if (dto.LoanAllowMultipleActive is { } lam) tracked.LoanAllowMultipleActive = lam;
        if (dto.LoanMinCashReserve is { } lcr) tracked.LoanMinCashReserve = lcr;
        if (dto.LoanRequiredApprovals is { } lra2) tracked.LoanRequiredApprovals = lra2;
        if (dto.LoanApprovalValidDays is { } lvd2) tracked.LoanApprovalValidDays = lvd2;
        if (dto.RoleChangeApprovals is { } rca2) tracked.RoleChangeApprovals = rca2;
        if (dto.LeaderCoolingOffDays is { } lco2) tracked.LeaderCoolingOffDays = lco2;
        if (dto.CashStatementMaxAgeDays is { } csa2) tracked.CashStatementMaxAgeDays = csa2;
        // SASISHO18 — pochi + floor (vikundi vya "kila tukio")
        if (dto.MemberMinimumBalance is { } mmb) { if (mmb < 0) throw new ArgumentException("MemberMinimumBalance >= 0"); tracked.MemberMinimumBalance = mmb; }
        if (dto.EventTopUpWindowDays is { } etw) { if (etw is < 0 or > 365) throw new ArgumentException("EventTopUpWindowDays 0..365"); tracked.EventTopUpWindowDays = etw; }
        if (dto.OverdueEventFineAmount is { } oef) { if (oef < 0) throw new ArgumentException("OverdueEventFineAmount >= 0"); tracked.OverdueEventFineAmount = oef; }
        if (dto.MaxEventDeductionPerMemberPerMonth is { } mdp) { if (mdp < 0) throw new ArgumentException("MaxEventDeductionPerMemberPerMonth >= 0"); tracked.MaxEventDeductionPerMemberPerMonth = mdp; }
        if (dto.MaxEventsPerMonth is { } mep) { if (mep is < 0 or > 100) throw new ArgumentException("MaxEventsPerMonth 0..100"); tracked.MaxEventsPerMonth = mep; }
        if (dto.BeneficiaryContributes is { } bc) tracked.BeneficiaryContributes = bc;
        // SASISHO19 — kanuni za J3 (faini -> neema -> kuondolewa -> kurudi)
        if (dto.EventFineGraceDays is { } egd) { if (egd is < 0 or > 90) throw new ArgumentException("EventFineGraceDays 0..90"); tracked.EventFineGraceDays = egd; }
        if (dto.RemovalApprovalsRequired is { } rar) { if (rar is < 0 or > 20) throw new ArgumentException("RemovalApprovalsRequired 0..20"); tracked.RemovalApprovalsRequired = rar; }
        if (dto.ReinstatementFineMultiplier is { } rfm) { if (rfm < 0) throw new ArgumentException("ReinstatementFineMultiplier >= 0"); tracked.ReinstatementFineMultiplier = rfm; }
        if (dto.ReinstatementRequiresJoiningFee is { } rjf) tracked.ReinstatementRequiresJoiningFee = rjf;
        // SASISHO21 — sanduku la malipo, lango la kujiunga, SMS (sera ya kikundi, si namba ndani ya msimbo)
        if (dto.PaymentSecondApprovalAmount is { } psa) { if (psa < 0) throw new ArgumentException("PaymentSecondApprovalAmount >= 0"); tracked.PaymentSecondApprovalAmount = psa; }
        if (dto.ClaimsEnabled is { } clm) tracked.ClaimsEnabled = clm;
        if (dto.JoiningPaymentRequired is { } jpr) tracked.JoiningPaymentRequired = jpr;
        if (dto.JoiningSavingsPortion is { } jsp) { if (jsp < 0) throw new ArgumentException("JoiningSavingsPortion >= 0"); tracked.JoiningSavingsPortion = jsp; }
        if (dto.JoiningPaymentWindowDays is { } jpw) { if (jpw is < 0 or > 90) throw new ArgumentException("JoiningPaymentWindowDays 0..90"); tracked.JoiningPaymentWindowDays = jpw; }
        if (dto.SmsEnabled is { } sme) tracked.SmsEnabled = sme;
        if (dto.SmsLowCreditThreshold is { } slc) { if (slc < 0) throw new ArgumentException("SmsLowCreditThreshold >= 0"); tracked.SmsLowCreditThreshold = slc; }
        if (dto.SmsQuietFromHour is { } sqf) tracked.SmsQuietFromHour = sqf;
        if (dto.SmsQuietToHour is { } sqt) tracked.SmsQuietToHour = sqt;
        if (dto.SmsMaxPerMemberPerEvent is { } smx) { if (smx is < 0 or > 20) throw new ArgumentException("SmsMaxPerMemberPerEvent 0..20"); tracked.SmsMaxPerMemberPerEvent = smx; }
        if (dto.SmsMaxCharsPerMessage is { } smc) { if (smc is < 0 or > 480) throw new ArgumentException("SmsMaxCharsPerMessage 0..480"); tracked.SmsMaxCharsPerMessage = smc; }
        if (dto.AppealsEnabled is { } apen) tracked.AppealsEnabled = apen;
        if (dto.AppealWindowDays is { } apw) { if (apw is < 0 or > 365) throw new ArgumentException("AppealWindowDays 0..365"); tracked.AppealWindowDays = apw; }
        if (dto.AppealApprovalsRequired is { } apr) { if (apr is < 2 or > 20) throw new ArgumentException("AppealApprovalsRequired 2..20"); tracked.AppealApprovalsRequired = apr; }
        if (tracked.LoansEnabled && Services.Loans.LoanApplicationService.MissingPolicy(tracked) is { Count: > 0 } lmiss)
            throw new ArgumentException("Kufungua mikopo kunahitaji sera kamili: " + string.Join("; ", lmiss) + ".");

        await AddAuditAsync(groupId, null, userId, before is null ? AuditAction.GroupPolicyCreated : AuditAction.GroupPolicyVersioned,
            nameof(TruthGroupSettings), groupId, before, Snap(tracked), null, "Kanuni za kikundi", ct);
        await _context.SaveChangesAsync(ct);
        return tracked;
    }

    // =====================================================================
    // 2. KUFUTA DATA ZA MAJARIBIO (kikundi kimoja tu; dryRun kwanza)
    // =====================================================================
    public sealed record ResetReport(bool DryRun, Dictionary<string, int> Rows);

    public async Task<ResetReport> ResetGroupFinancialDataAsync(Guid groupId, bool dryRun, bool keepLoans, Guid actorUserId, CancellationToken ct = default)
    {
        var memberIds = await _context.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.Id).ToListAsync(ct);
        var withdrawalIds = await _context.Withdrawals.Where(w => w.GroupId == groupId).Select(w => w.Id).ToListAsync(ct);
        var welfareIds = await _context.WelfareEvents.Where(w => w.GroupId == groupId).Select(w => w.Id).ToListAsync(ct);
        var groupEventIds = await _context.GroupEvents.Where(e => e.GroupId == groupId).Select(e => e.Id).ToListAsync(ct);

        // Mpangilio unaheshimu foreign keys (watoto kwanza)
        var plan = new List<(string Name, Func<Task<int>> Run)>
        {
            ("WithdrawalApprovals", () => Del(_context.WithdrawalApprovals.Where(x => withdrawalIds.Contains(x.WithdrawalId)))),
            ("Withdrawals",         () => Del(_context.Withdrawals.Where(x => x.GroupId == groupId))),
            ("WalletWithdrawals",   () => Del(_context.WalletWithdrawals.Where(x => x.GroupId == groupId))),
            ("WelfareContributions",() => Del(_context.WelfareContributions.Where(x => welfareIds.Contains(x.WelfareEventId)))),
            ("WelfareDisbursements",() => Del(_context.WelfareDisbursements.Where(x => welfareIds.Contains(x.WelfareEventId)))),
            ("WelfareObligations",  () => Del(_context.WelfareObligations.Where(x => welfareIds.Contains(x.WelfareEventId)))),
            ("WelfareEvents",       () => Del(_context.WelfareEvents.Where(x => x.GroupId == groupId))),
            ("EventContributions",  () => Del(_context.EventContributions.Where(x => groupEventIds.Contains(x.GroupEventId)))),
            ("Fines",               () => Del(_context.Fines.Where(x => x.GroupId == groupId))),
            ("GroupEvents",         () => Del(_context.GroupEvents.Where(x => x.GroupId == groupId))),
            ("ComplianceSnapshots", () => Del(_context.ComplianceSnapshots.Where(x => x.GroupId == groupId))),
            ("Debts",               () => Del(_context.Debts.Where(x => x.GroupId == groupId))),
            ("LedgerEntries",       () => Del(_context.LedgerEntries.Where(x => x.GroupId == groupId))),
            ("FinancialEvents",     () => Del(_context.FinancialEvents.Where(x => x.GroupId == groupId))),
            ("ImportedStatements",  () => Del(_context.ImportedStatements.Where(x => x.GroupId == groupId))),
            ("MemberStatusHistories",() => Del(_context.MemberStatusHistories.Where(x => memberIds.Contains(x.MemberId)))),
            ("MkobaRawTransactions",() => Del(_context.MkobaRawTransactions.Where(x => x.GroupId == groupId))),
            ("MkobaStatementImports",() => Del(_context.MkobaStatementImports.Where(x => x.GroupId == groupId))), // SASISHO17: rasimu za PDF
            ("MkobaStatementGaps",  () => Del(_context.MkobaStatementGaps.Where(x => x.GroupId == groupId))),
            ("TruthAdjustments",    () => Del(_context.TruthAdjustments.Where(x => x.GroupId == groupId))),
        };
        if (!keepLoans) plan.Add(("LoanApplications", () => Del(_context.LoanApplications.Where(x => x.GroupId == groupId))));
        if (!keepLoans) plan.Add(("Loans", () => Del(_context.Loans.Where(x => x.GroupId == groupId))));
        // HAZIGUSWI: Groups, GroupMembers, Users, GroupPolicies, GroupSettings, MemberPhones, AuditEvents

        var counts = new Dictionary<string, int>();
        if (dryRun)
        {
            counts["WithdrawalApprovals"] = await _context.WithdrawalApprovals.CountAsync(x => withdrawalIds.Contains(x.WithdrawalId), ct);
            counts["Withdrawals"] = withdrawalIds.Count;
            counts["WalletWithdrawals"] = await _context.WalletWithdrawals.CountAsync(x => x.GroupId == groupId, ct);
            counts["WelfareEvents"] = welfareIds.Count;
            counts["GroupEvents"] = groupEventIds.Count;
            counts["Fines"] = await _context.Fines.CountAsync(x => x.GroupId == groupId, ct);
            counts["ComplianceSnapshots"] = await _context.ComplianceSnapshots.CountAsync(x => x.GroupId == groupId, ct);
            counts["Debts"] = await _context.Debts.CountAsync(x => x.GroupId == groupId, ct);
            counts["LedgerEntries"] = await _context.LedgerEntries.CountAsync(x => x.GroupId == groupId, ct);
            counts["FinancialEvents"] = await _context.FinancialEvents.CountAsync(x => x.GroupId == groupId, ct);
            counts["ImportedStatements"] = await _context.ImportedStatements.CountAsync(x => x.GroupId == groupId, ct);
            counts["MkobaRawTransactions"] = await _context.MkobaRawTransactions.CountAsync(x => x.GroupId == groupId, ct);
            counts["MkobaStatementImports"] = await _context.MkobaStatementImports.CountAsync(x => x.GroupId == groupId, ct);
            counts["MkobaStatementGaps"] = await _context.MkobaStatementGaps.CountAsync(x => x.GroupId == groupId, ct);
            counts["TruthAdjustments"] = await _context.TruthAdjustments.CountAsync(x => x.GroupId == groupId, ct);
            if (!keepLoans) counts["LoanApplications"] = await _context.LoanApplications.CountAsync(x => x.GroupId == groupId, ct);
            if (!keepLoans) counts["Loans"] = await _context.Loans.CountAsync(x => x.GroupId == groupId, ct);
            return new ResetReport(true, counts);
        }

        // SASISHO17: inaweza kuitwa ndani ya transaction ya idhini (ResetRequestService) — DbTx.BeginAsync haianzishi ya pili
        await using var tx = await Common.DbTx.BeginAsync(_context, ct);
        foreach (var (name, run) in plan) counts[name] = await run();

        // Counters za zamani za wanachama zirudi sifuri
        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
        foreach (var m in members) { m.TotalContributionsCount = 0; m.AdvanceBalance = 0m; }
        await AddAuditAsync(groupId, null, actorUserId, AuditAction.Deleted, "GroupFinancialData", groupId, null,
            new { keepLoans, rowsDeleted = counts }, null, "Futa data za fedha za majaribio", ct);
        await _context.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new ResetReport(false, counts);

        async Task<int> Del<T>(IQueryable<T> q) where T : class
        {
            var rows = await q.ToListAsync(ct);
            _context.RemoveRange(rows);
            await _context.SaveChangesAsync(ct);
            return rows.Count;
        }
    }

    // =====================================================================
    // 3. IMPORT YA PDF YA M-KOBA — data ghafi tu + ukaguzi wa uadilifu
    // =====================================================================
    public sealed record ImportReport(
        string FileHash, int RowsInPdf, int Inserted, int AlreadyExisted,
        List<string> UnreadableRows, List<object> Gaps,
        decimal DepositsInPdf, decimal WithdrawalsInPdf,
        decimal? DeclaredContributionTotal, decimal? DeclaredGroupBalance, decimal? LastBalanceInPdf,
        List<object> UnknownPhones, List<string> Warnings, List<ChamaLink.Domain.Localization.LocMessage>? WarningsLoc = null);

    // SASISHO17 (P0-2): ImportMkobaStatementAsync (kiongozi MMOJA kuingiza PDF moja kwa moja) IMEONDOLEWA.
    // PDF inapita StatementImportService: rasimu → kiongozi wa pili → transaction moja.

    /// <summary>Mapengo ya mnyororo wa balance juu ya data YOTE ya kikundi (inaitwa baada ya idhini ya PDF).</summary>
    public async Task<List<MkobaStatementGap>> RecomputeGapsAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId)
            .Select(t => new { t.Reference, t.Timestamp, t.Amount, t.IsWithdrawal, t.Balance, t.RowOrder }).ToListAsync(ct);
        var gaps = StatementIntegrity.FindGaps(rows.Select(r =>
            new StatementIntegrity.Row(r.Reference, r.Timestamp, r.Amount, r.IsWithdrawal, r.Balance, r.RowOrder)));

        var existing = await _context.MkobaStatementGaps.Where(g => g.GroupId == groupId).ToListAsync(ct);
        var current = new List<MkobaStatementGap>();
        foreach (var g in gaps)
        {
            var e = existing.FirstOrDefault(x => x.BeforeReference == g.BeforeReference);
            if (e is null)
            {
                e = new MkobaStatementGap
                {
                    Id = Guid.NewGuid(), GroupId = groupId, AfterReference = g.AfterReference, BeforeReference = g.BeforeReference,
                    WindowStart = DateTime.SpecifyKind(g.WindowStart, DateTimeKind.Utc), WindowEnd = DateTime.SpecifyKind(g.WindowEnd, DateTimeKind.Utc),
                    Amount = g.Amount
                };
                _context.MkobaStatementGaps.Add(e);
            }
            current.Add(e);
        }
        // Pengo lililojazwa na PDF mpya (mstari uliokosekana sasa upo) linaondolewa kama halijaidhinishwa
        foreach (var stale in existing.Where(x => gaps.All(g => g.BeforeReference != x.BeforeReference)
                                                  && x.ResolutionStatus != TruthApprovalStatus.Approved))
            _context.MkobaStatementGaps.Remove(stale);
        return current;
    }

    // =====================================================================
    // 4. NAMBA ZA SIMU (kiongozi anaweka; mfumo unahakiki dhidi ya M-Koba)
    // =====================================================================
    public sealed record PhoneResult(MemberPhone Phone, bool MatchedInMkoba, string? MkobaNameSeen, int DepositsFound, decimal DepositsTotal, string Ujumbe);

    private static object Snap(MemberPhone p) => new
    {
        p.Phone, p.GroupMemberId, Status = p.Status.ToString(), p.ValidFrom, p.ValidTo, p.PendingValidTo, p.PendingEndReason,
        p.AddedByUserId, p.ApprovedByUserId, p.EndRequestedByUserId, p.Note
    };

    private static bool Overlaps(DateTime? aFrom, DateTime? aTo, DateTime? bFrom, DateTime? bTo)
    {
        var a0 = aFrom?.Date ?? DateTime.MinValue; var a1 = aTo?.Date ?? DateTime.MaxValue.Date;
        var b0 = bFrom?.Date ?? DateTime.MinValue; var b1 = bTo?.Date ?? DateTime.MaxValue.Date;
        return a0 <= b1 && b0 <= a1;
    }

    private async Task EnsureNoOverlapAsync(Guid groupId, string phone, Guid? exceptId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var others = await _context.MemberPhones
            .Where(p => p.GroupId == groupId && p.Phone == phone && p.Id != exceptId
                        && (p.Status == TruthApprovalStatus.Pending || p.Status == TruthApprovalStatus.Approved))
            .ToListAsync(ct);
        var clash = others.FirstOrDefault(o => Overlaps(o.ValidFrom, o.ValidTo, from, to));
        if (clash is not null)
            throw new ConflictException(
                $"Namba {phone} tayari ni ya mwanachama mwingine (au inasubiri idhini) kwa kipindi kinachopishana" +
                $" ({clash.ValidFrom:yyyy-MM-dd}..{clash.ValidTo:yyyy-MM-dd}). Kwanza ifunge kwa tarehe (kiongozi wa pili akiidhinisha), " +
                $"kisha iunganishe kwa mwingine kuanzia siku inayofuata. Namba moja haiwezi kuwa ya watu wawili kwa wakati mmoja.");
    }

    /// <summary>
    /// Ombi la kuunganisha namba na mwanachama. HALIHESABIWI mpaka kiongozi WA PILI aidhinishe (DecidePhoneAsync),
    /// kwa sababu kuunganisha namba = kuamua pesa zote za namba hiyo ni za nani.
    /// </summary>
    /// <param name="claimedByUserId">
    /// Mwanachama aliyejisajili mwenyewe na namba hii (ombi la kujiunga). Yeye ndiye "maker" na kiongozi anayeidhinisha
    /// ombi (<paramref name="userId"/>) ndiye "checker" — kwa hiyo namba MPYA (isiyo na malipo M-Koba) inaidhinishwa hapo hapo.
    /// Namba yenye malipo tayari inasubiri kiongozi mwingine (pesa ya mtu mwingine isichukuliwe kwa usajili).
    /// </param>
    public async Task<PhoneResult> AddPhoneAsync(Guid groupId, Guid memberId, string rawPhone, Guid userId, string? note, CancellationToken ct = default,
        DateTime? validFrom = null, DateTime? validTo = null, Guid? claimedByUserId = null)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        if (member.UserId == userId) throw new ForbiddenException(MakerIsSubjectMessage); // SASISHO17 (NEW-3)
        var phone = PhoneNumbers.Normalize(rawPhone);
        if (validFrom is { } vf && validTo is { } vt && vt.Date < vf.Date) throw new ArgumentException("Tarehe ya mwisho iko kabla ya tarehe ya kuanza.");

        var mine = await _context.MemberPhones.FirstOrDefaultAsync(p => p.GroupId == groupId && p.Phone == phone && p.GroupMemberId == memberId
            && (p.Status == TruthApprovalStatus.Pending || p.Status == TruthApprovalStatus.Approved) && p.ValidTo == null, ct);
        if (mine is not null)
            throw new ConflictException(mine.Status == TruthApprovalStatus.Pending
                ? $"Namba {phone} tayari inasubiri idhini kwa mwanachama huyu."
                : $"Namba {phone} tayari imewekwa kwa mwanachama huyu.");
        await EnsureNoOverlapAsync(groupId, phone, null, validFrom, validTo, ct);

        var seen = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId && t.Phone == phone && !t.IsWithdrawal).ToListAsync(ct);
        var mp = new MemberPhone
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = member.Id, Phone = phone,
            MatchedInMkoba = seen.Count > 0, MkobaNameSeen = seen.Select(s => s.PayerName).FirstOrDefault(),
            AddedByUserId = claimedByUserId ?? userId, Note = note, Status = TruthApprovalStatus.Pending,
            ValidFrom = validFrom is { } f ? DateTime.SpecifyKind(f.Date, DateTimeKind.Utc) : null,
            ValidTo = validTo is { } t2 ? DateTime.SpecifyKind(t2.Date, DateTimeKind.Utc) : null
        };
        var autoApproved = claimedByUserId is { } cu && cu != userId && seen.Count == 0;
        if (autoApproved) { mp.Status = TruthApprovalStatus.Approved; mp.ApprovedByUserId = userId; mp.ApprovedAt = DateTime.UtcNow; }
        _context.MemberPhones.Add(mp);
        await AddAuditAsync(groupId, member.Id, userId, AuditAction.Created, nameof(MemberPhone), mp.Id, null, Snap(mp), mp.Phone,
            autoApproved ? "Namba ya usajili ya mwanachama mwenyewe, imeidhinishwa na kiongozi (haina malipo ya zamani M-Koba)"
                         : "Ombi la kuunganisha namba ya simu (linasubiri kiongozi wa pili)", ct);
        await _context.SaveChangesAsync(ct);

        var covered = seen.Where(s => mp.ValidFrom is null || s.Timestamp.Date >= mp.ValidFrom.Value.Date)
                          .Where(s => mp.ValidTo is null || s.Timestamp.Date <= mp.ValidTo.Value.Date).ToList();
        var msg = (seen.Count > 0
            ? $"✅ Imelingana na M-Koba: jina la M-Koba ni '{mp.MkobaNameSeen}', malipo {covered.Count} ya jumla {covered.Sum(s => s.Amount):N0} ndani ya kipindi ulichoweka. "
            : "⚠️ Namba hii HAIJAONEKANA kwenye statement yoyote ya M-Koba iliyoingizwa (au mwanachama bado hajalipa kwa namba hii). ")
            + (autoApproved ? "Imeidhinishwa." : "Ombi limehifadhiwa — kiongozi WA PILI aidhinishe ndipo malipo yahesabiwe.");
        return new PhoneResult(mp, mp.MatchedInMkoba, mp.MkobaNameSeen, covered.Count, covered.Sum(s => s.Amount), msg);
    }

    /// <summary>Kiongozi wa pili (si aliyeomba) anaidhinisha au kukataa kuunganishwa kwa namba.</summary>
    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<MemberPhone> DecidePhoneAsync(Guid groupId, Guid phoneId, bool approve, Guid deciderUserId, string? note, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecidePhoneCoreAsync(groupId, phoneId, approve, deciderUserId, note, ct), ct);

    private async Task<MemberPhone> DecidePhoneCoreAsync(Guid groupId, Guid phoneId, bool approve, Guid deciderUserId, string? note, CancellationToken ct = default)
    {
        var mp = await _context.MemberPhones.FirstOrDefaultAsync(p => p.Id == phoneId && p.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Namba haipo.");
        if (mp.Status != TruthApprovalStatus.Pending) throw new ConflictException($"Ombi hili tayari ni {mp.Status}.");
        if (mp.AddedByUserId == deciderUserId) throw new ForbiddenException("Aliyeomba kuunganisha namba HAWEZI kuidhinisha mwenyewe (maker-checker).");
        await EnsureSubjectIsNotDeciderAsync(groupId, mp.GroupMemberId, deciderUserId, ct);
        var before = Snap(mp);
        if (approve) await EnsureNoOverlapAsync(groupId, mp.Phone, mp.Id, mp.ValidFrom, mp.ValidTo, ct);
        mp.Status = approve ? TruthApprovalStatus.Approved : TruthApprovalStatus.Rejected;
        mp.ApprovedByUserId = deciderUserId;
        mp.ApprovedAt = DateTime.UtcNow;
        await AddAuditAsync(groupId, mp.GroupMemberId, deciderUserId, approve ? AuditAction.PhoneLinkApproved : AuditAction.GovernanceRejected,
            nameof(MemberPhone), mp.Id, before, Snap(mp), mp.Phone, note, ct);
        await _context.SaveChangesAsync(ct);
        return mp;
    }

    /// <summary>
    /// Ombi la KUFUNGA namba kuanzia tarehe fulani (badala ya kuifuta). Malipo ya kabla ya tarehe hiyo yanabaki kwa
    /// mwanachama huyu; ya baada yake hayahesabiwi kwake. Linahitaji kiongozi wa pili (DecideEndPhoneAsync).
    /// </summary>
    public async Task<MemberPhone> RequestEndPhoneAsync(Guid groupId, Guid phoneId, DateTime validToEat, string reason, Guid userId, CancellationToken ct = default)
    {
        var mp = await _context.MemberPhones.FirstOrDefaultAsync(p => p.Id == phoneId && p.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Namba haipo.");
        await EnsureMakerIsNotSubjectAsync(groupId, mp.GroupMemberId, userId, ct); // SASISHO17 (NEW-3)
        if (mp.Status != TruthApprovalStatus.Approved) throw new ConflictException("Namba inayoweza kufungwa ni iliyoidhinishwa tu. Ombi linalosubiri linaweza kughairiwa.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Eleza sababu ya kufunga namba (mf. 'Fauzia si mwanachama; Abdulla analipa kwa namba yake tangu Julai').");
        if (mp.PendingValidTo is not null) throw new ConflictException("Tayari kuna ombi la kufunga namba hii linalosubiri idhini.");
        var to = validToEat.Date;
        if (mp.ValidFrom is { } vf && to < vf.Date) throw new ArgumentException("Tarehe ya kufunga iko kabla ya tarehe namba ilipoanza kutumika.");
        if (mp.ValidTo is { } cur && to >= cur.Date) throw new ConflictException($"Namba tayari imefungwa kuanzia {cur:yyyy-MM-dd}.");
        var before = Snap(mp);
        mp.PendingValidTo = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        mp.PendingEndReason = reason.Trim();
        mp.EndRequestedByUserId = userId;
        mp.EndRequestedAt = DateTime.UtcNow;
        await AddAuditAsync(groupId, mp.GroupMemberId, userId, AuditAction.Updated, nameof(MemberPhone), mp.Id, before, Snap(mp), mp.Phone,
            "Ombi la kufunga namba: " + reason.Trim(), ct);
        await _context.SaveChangesAsync(ct);
        return mp;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<MemberPhone> DecideEndPhoneAsync(Guid groupId, Guid phoneId, bool approve, Guid deciderUserId, string? note, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideEndPhoneCoreAsync(groupId, phoneId, approve, deciderUserId, note, ct), ct);

    private async Task<MemberPhone> DecideEndPhoneCoreAsync(Guid groupId, Guid phoneId, bool approve, Guid deciderUserId, string? note, CancellationToken ct = default)
    {
        var mp = await _context.MemberPhones.FirstOrDefaultAsync(p => p.Id == phoneId && p.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Namba haipo.");
        if (mp.PendingValidTo is null) throw new ConflictException("Hakuna ombi la kufunga namba hii.");
        if (mp.EndRequestedByUserId == deciderUserId) throw new ForbiddenException("Aliyeomba kufunga namba HAWEZI kuidhinisha mwenyewe (maker-checker).");
        await EnsureSubjectIsNotDeciderAsync(groupId, mp.GroupMemberId, deciderUserId, ct);
        var before = Snap(mp);
        if (approve) mp.ValidTo = mp.PendingValidTo;
        var reason = mp.PendingEndReason;
        mp.PendingValidTo = null; mp.PendingEndReason = null; mp.EndRequestedByUserId = null; mp.EndRequestedAt = null;
        await AddAuditAsync(groupId, mp.GroupMemberId, deciderUserId, approve ? AuditAction.PhoneLinkEnded : AuditAction.GovernanceRejected,
            nameof(MemberPhone), mp.Id, before, Snap(mp), mp.Phone, note ?? reason, ct);
        await _context.SaveChangesAsync(ct);
        return mp;
    }

    /// <summary>
    /// Kufuta kunaruhusiwa TU kwa ombi ambalo halijawahi kuidhinishwa, na na aliyeliomba (kughairi ombi lake).
    /// Namba iliyoidhinishwa haifutwi kamwe — inafungwa kwa tarehe (RequestEndPhoneAsync) ili historia ibaki.
    /// </summary>
    public async Task RemovePhoneAsync(Guid groupId, Guid phoneId, Guid actorUserId, CancellationToken ct = default)
    {
        var mp = await _context.MemberPhones.FirstOrDefaultAsync(p => p.Id == phoneId && p.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Namba haipo.");
        if (mp.Status == TruthApprovalStatus.Approved)
            throw new ConflictException("Namba iliyoidhinishwa haifutwi — ifunge kwa tarehe (kiongozi wa pili ataidhinisha). Malipo ya zamani yanabaki kwa mwenyewe.");
        if (mp.Status == TruthApprovalStatus.Pending && mp.AddedByUserId != actorUserId)
            throw new ForbiddenException("Ombi linalosubiri linaweza kughairiwa na aliyeliomba tu. Wewe unaweza kulikataa.");
        _context.MemberPhones.Remove(mp);
        await AddAuditAsync(groupId, mp.GroupMemberId, actorUserId, AuditAction.Deleted, nameof(MemberPhone), mp.Id, Snap(mp), new { removed = true }, mp.Phone,
            mp.Status == TruthApprovalStatus.Pending ? "Ombi la kuunganisha namba limeghairiwa na aliyeliomba" : "Ombi lililokataliwa limeondolewa", ct);
        await _context.SaveChangesAsync(ct);
    }

    public const string MakerIsSubjectMessage = "Huwezi kuanzisha jambo linalohusu pesa au namba zako mwenyewe. Kiongozi mwingine aanzishe, na wa tatu aidhinishe.";

    /// <summary>
    /// SASISHO17 (NEW-3): ANAYEANZISHA (maker) hawezi kuwa mhusika. Kabla ya hapa ni ANAYEIDHINISHA tu aliyezuiwa, kwa hiyo
    /// kiongozi A angeweza kujiandikia malipo ya mkono/msamaha wa faini/namba yake, na kiongozi B (rafiki yake) kuidhinisha.
    /// Sasa watu wawili WASIOHUSIKA wanahitajika.
    /// </summary>
    private async Task EnsureMakerIsNotSubjectAsync(Guid groupId, Guid subjectMemberId, Guid makerUserId, CancellationToken ct)
    {
        var subjectUser = await _context.GroupMembers.Where(m => m.Id == subjectMemberId && m.GroupId == groupId).Select(m => m.UserId).FirstOrDefaultAsync(ct);
        if (subjectUser == makerUserId) throw new ForbiddenException(MakerIsSubjectMessage);
    }

    /// <summary>Mhusika wa uamuzi (mwenye namba) hawezi kuidhinisha jambo linalomhusu yeye mwenyewe.</summary>
    private async Task EnsureSubjectIsNotDeciderAsync(Guid groupId, Guid subjectMemberId, Guid deciderUserId, CancellationToken ct)
    {
        var subjectUser = await _context.GroupMembers.Where(m => m.Id == subjectMemberId && m.GroupId == groupId).Select(m => m.UserId).FirstOrDefaultAsync(ct);
        if (subjectUser == deciderUserId) throw new ForbiddenException("Huwezi kuidhinisha jambo linalohusu namba/pesa zako mwenyewe.");
    }

    /// <summary>Orodha ya namba zote zilizoonekana M-Koba + viungo vyake (vilivyoidhinishwa, vinavyosubiri, vilivyofungwa).</summary>
    public async Task<List<object>> PhoneOverviewAsync(Guid groupId, Guid? viewerUserId = null, CancellationToken ct = default)
    {
        var mapped = await _context.MemberPhones.Where(p => p.GroupId == groupId).ToListAsync(ct);
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        var seen = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId && !t.IsWithdrawal)
            .GroupBy(t => t.Phone).Select(g => new { Phone = g.Key, Name = g.Max(x => x.PayerName), Count = g.Count(), Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var userNames = members.GroupBy(m => m.UserId).ToDictionary(g => g.Key, g => g.First().User?.FullName ?? g.First().MemberNumber);
        var today = TruthLedgerService.NowEat();
        var phones = seen.Select(s => s.Phone).Union(mapped.Select(m => m.Phone)).OrderBy(p => p);
        return phones.Select(p =>
        {
            var s = seen.FirstOrDefault(x => x.Phone == p);
            var links = mapped.Where(x => x.Phone == p && x.Status != TruthApprovalStatus.Rejected).OrderByDescending(x => x.AddedAt).ToList();
            // Kiungo "cha sasa": kilichoidhinishwa kinachofunika leo, au kinachosubiri; vinginevyo cha karibuni zaidi
            var m = links.FirstOrDefault(x => x.Status == TruthApprovalStatus.Pending)
                    ?? links.FirstOrDefault(x => x.Covers(today))
                    ?? links.FirstOrDefault();
            var mem = m is null ? null : members.FirstOrDefault(x => x.Id == m.GroupMemberId);
            var ended = m is { Status: TruthApprovalStatus.Approved, ValidTo: not null } && !m.Covers(today);
            var state = m is null ? "Unmapped"
                : m.Status == TruthApprovalStatus.Pending ? "Pending"
                : ended ? "Ended"
                : s is null ? "NotSeen" : "Linked";
            return (object)new
            {
                phone = p, mkobaName = s?.Name, deposits = s?.Count ?? 0, total = s?.Total ?? 0m,
                phoneId = m?.Id, memberId = mem?.Id, memberName = mem?.User?.FullName,
                state,
                hali = state switch
                {
                    "Unmapped" => "HAIJAUNGANISHWA — pesa hii haijulikani ni ya nani",
                    "Pending" => "Inasubiri idhini ya kiongozi wa pili — malipo bado hayahesabiwi",
                    "Ended" => $"Imefungwa tangu {m!.ValidTo:yyyy-MM-dd} — malipo mapya hayahesabiwi kwa mtu yeyote",
                    "NotSeen" => "Imewekwa, haijaonekana M-Koba",
                    _ => "Imeunganishwa"
                },
                validFrom = m?.ValidFrom, validTo = m?.ValidTo,
                requestedByName = m is null ? null : userNames.GetValueOrDefault(m.AddedByUserId),
                canApprove = m is { Status: TruthApprovalStatus.Pending } && viewerUserId is { } v1 && m.AddedByUserId != v1 && mem?.UserId != v1,
                canCancel = m is { Status: TruthApprovalStatus.Pending } && viewerUserId is { } v2 && m.AddedByUserId == v2,
                pendingEnd = m?.PendingValidTo is null ? null : new
                {
                    validTo = m.PendingValidTo, reason = m.PendingEndReason,
                    requestedByName = m.EndRequestedByUserId is { } eu ? userNames.GetValueOrDefault(eu) : null,
                    canApprove = viewerUserId is { } v3 && m.EndRequestedByUserId != v3 && mem?.UserId != v3
                },
                history = links.Select(l => new
                {
                    l.Id, memberName = members.FirstOrDefault(x => x.Id == l.GroupMemberId)?.User?.FullName,
                    status = l.Status.ToString(), l.ValidFrom, l.ValidTo
                })
            };
        }).ToList();
    }

    // =====================================================================
    // 5. MANUAL (pesa ya mkononi / Kianzio) — maker-checker
    // =====================================================================
    /// <summary>
    /// Ukaguzi wa ombi la manual — UNATUMIKA na hakikisho (preview) NA uwasilishaji, ili
    /// kiongozi aone makosa yale yale kabla ya kubofya "Wasilisha".
    /// </summary>
    public async Task<List<string>> ValidateManualPaymentAsync(Guid groupId, Guid memberId, decimal amount, DateTime occurredAtEat,
        bool isJoinFee, CancellationToken ct = default)
    {
        var problems = new List<string>();
        if (amount <= 0) problems.Add("Kiasi lazima kiwe zaidi ya 0.");
        if (amount % 1 != 0) problems.Add("Kiasi kiwe namba kamili (bila senti).");
        if (!await _context.GroupMembers.AnyAsync(m => m.Id == memberId && m.GroupId == groupId, ct))
        {
            problems.Add("Mwanachama hayupo kwenye kikundi hiki.");
            return problems;
        }
        if (occurredAtEat > TruthLedgerService.NowEat().AddMinutes(5))
            problems.Add("Tarehe ya malipo haiwezi kuwa ya baadaye.");

        var policy = TruthLedgerService.ToTruthPolicy(await _truth.GetSettingsAsync(groupId, ct));
        if (policy.ManualEntryCutoff is null)
            problems.Add("Kikundi hakijaweka tarehe ya mwisho ya kumbukumbu za mkononi (ManualEntryCutoff). Iweke kwenye Mipangilio kwanza.");
        else if (occurredAtEat > policy.ManualEntryCutoff.Value)
            problems.Add($"Tarehe {occurredAtEat:dd/MM/yyyy} iko baada ya tarehe ya mwisho ya kumbukumbu za mkononi ({policy.ManualEntryCutoff:dd/MM/yyyy}). Baada ya hapo M-Koba pekee ndiyo ukweli — mwanachama alipe kupitia M-Koba.");
        if (occurredAtEat < new DateTime(policy.GroupFirstPeriod.Year, policy.GroupFirstPeriod.Month, 1).AddMonths(-3))
            problems.Add("Tarehe ni ya kabla ya kikundi kuanza michango.");

        if (isJoinFee && amount > 0)
        {
            var now = await _truth.GetMemberTruthAsync(memberId, null, ct);
            var pendingJf = await _context.FinancialEvents.Where(e => e.MemberId == memberId && e.Type == FinancialEventType.JoiningFeePaid
                                                                     && e.Source == EventSource.Manual && e.Status == FinancialEventStatus.Pending)
                .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
            var room = now.Statement.JoinFeeOutstanding - pendingJf;
            if (amount > room)
                problems.Add(room <= 0
                    ? $"Hakuna Kianzio kinachodaiwa (kilichobaki {now.Statement.JoinFeeOutstanding:N0}, kinachosubiri idhini {pendingJf:N0})."
                    : $"Kianzio kilichobaki ni {now.Statement.JoinFeeOutstanding:N0} (na {pendingJf:N0} kinasubiri idhini) — unaweza kuingiza hadi {room:N0} tu.");
        }
        return problems;
    }

    public sealed record MoneySummary(decimal JoinFeePaid, decimal JoinFeeOutstanding, decimal Arrears, decimal Savings,
        decimal TotalDebtIncludingJoinFee, decimal LoanOutstanding, IReadOnlyList<string> MonthsInArrears);
    public sealed record MonthChange(string Month, string StatusBefore, string StatusAfter, decimal OutstandingBefore, decimal OutstandingAfter);
    public sealed record PreviewResult(bool Valid, List<string> Problems, MoneySummary Before, MoneySummary After,
        List<AllocationLine> NewAllocations, List<MonthChange> ChangedMonths, int PendingRequestsForMember);

    /// <summary>
    /// HAKIKISHO: "Nikiingiza malipo haya, nini kitatokea?" — hakuna kinachohifadhiwa.
    /// Inacheza upya historia yote ya mwanachama pamoja na malipo ya kufikirika na kulinganisha kabla/baada.
    /// </summary>
    public async Task<PreviewResult> PreviewManualPaymentAsync(Guid groupId, Guid memberId, decimal amount, DateTime occurredAtEat,
        bool isJoinFee, CancellationToken ct = default)
    {
        var problems = await ValidateManualPaymentAsync(groupId, memberId, amount, occurredAtEat, isJoinFee, ct);
        if (problems.Contains("Mwanachama hayupo kwenye kikundi hiki.")) throw new KeyNotFoundException(problems[0]);

        var before = (await _truth.GetMemberTruthAsync(memberId, null, ct)).Statement;
        var reference = "HAKIKISHO";
        var hypo = amount > 0
            ? new List<TruthPayment> { new(reference, DateTime.SpecifyKind(occurredAtEat, DateTimeKind.Unspecified), amount,
                PaymentSource.ManualHistorical, isJoinFee ? PaymentPurpose.JoinFee : PaymentPurpose.Auto, IsApproved: true) }
            : new List<TruthPayment>();
        // Cutoff inakaguliwa na ValidateManualPaymentAsync; hapa tunaonyesha mgawanyo hata kama kuna tatizo
        var after = (await _truth.GetMemberTruthAsync(memberId, null, ct, hypo)).Statement;

        var changes = new List<MonthChange>();
        foreach (var a in after.Periods)
        {
            var b = before.Periods.FirstOrDefault(x => x.Period == a.Period);
            if (b is null || b.Status != a.Status || b.Outstanding != a.Outstanding)
                changes.Add(new MonthChange(a.Period.Label, b?.Status.ToString() ?? "-", a.Status.ToString(), b?.Outstanding ?? 0, a.Outstanding));
        }
        var pending = await _context.FinancialEvents.CountAsync(e => e.MemberId == memberId && e.Source == EventSource.Manual
                                                                     && e.Status == FinancialEventStatus.Pending, ct);
        var rejectedHypo = after.Rejected.FirstOrDefault(r => r.Payment.Reference == reference);
        if (rejectedHypo is not null && problems.Count == 0) problems.Add(rejectedHypo.Reason); // sababu ya injini, kama ukaguzi haukuiona

        return new PreviewResult(problems.Count == 0, problems, Summ(before), Summ(after),
            after.Allocations.Where(x => x.Reference == reference).ToList(), changes, pending);

        static MoneySummary Summ(TruthStatement s) => new(s.JoinFeePaid, s.JoinFeeOutstanding, s.Arrears, s.Savings,
            s.TotalDebtIncludingJoinFee, s.LoanOutstanding, s.MonthsInArrears.Select(m => m.Label).ToList());
    }

    public const int EvidenceReferenceMaxLength = 200;

    public async Task<FinancialEvent> RequestManualPaymentAsync(Guid groupId, Guid memberId, decimal amount, DateTime occurredAtEat,
        bool isJoinFee, string reason, EvidenceType? evidenceType, string? evidenceReference, Guid requestedByUserId, CancellationToken ct = default)
    {
        // Malipo ya nje ya M-Koba ndiyo hatari zaidi kiukaguzi: yanahitaji maelezo + ushahidi unaoweza kufuatiliwa
        // + kiongozi wa pili kuidhinisha (DecideManualPaymentAsync). Bila hivyo hayahifadhiwi kabisa.
        await EnsureMakerIsNotSubjectAsync(groupId, memberId, requestedByUserId, ct); // SASISHO17 (NEW-3)
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Eleza malipo haya (mf. 'Kianzio kwa mtunza-hazina').");
        if (evidenceType is null || !Enum.IsDefined(evidenceType.Value))
            throw new ArgumentException("Chagua aina ya ushahidi (risiti, daftari la fedha, kumbukumbu za kikao, tamko la kiongozi, stakabadhi, au nyingine).");
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("Andika ushahidi ulipo (mf. 'Risiti Na. 12' au 'Daftari la fedha uk. 4') ili mkaguzi aweze kuufuatilia.");
        if (evidenceReference.Trim().Length > EvidenceReferenceMaxLength)
            throw new ArgumentException($"Maelezo ya ushahidi yasizidi herufi {EvidenceReferenceMaxLength}.");
        var problems = await ValidateManualPaymentAsync(groupId, memberId, amount, occurredAtEat, isJoinFee, ct);
        if (problems.Contains("Mwanachama hayupo kwenye kikundi hiki.")) throw new KeyNotFoundException(problems[0]);
        if (amount <= 0) throw new ArgumentException(problems[0]);
        if (problems.Count > 0) throw new ConflictException(string.Join(" ", problems));
        var member = await _context.GroupMembers.FirstAsync(m => m.Id == memberId && m.GroupId == groupId, ct);

        var fe = new FinancialEvent
        {
            Id = Guid.NewGuid(), GroupId = groupId, MemberId = member.Id,
            Type = isJoinFee ? FinancialEventType.JoiningFeePaid : FinancialEventType.PaymentReceived,
            Amount = amount, OccurredAt = DateTime.SpecifyKind(occurredAtEat, DateTimeKind.Utc),
            CreatedBy = requestedByUserId, Source = EventSource.Manual,
            SourceReference = $"MANUAL-{Guid.NewGuid():N}"[..20],
            Status = FinancialEventStatus.Pending, Reason = reason.Trim(),
            EvidenceType = evidenceType, EvidenceReference = evidenceReference.Trim()
        };
        _context.FinancialEvents.Add(fe);
        await AddAuditAsync(groupId, member.Id, requestedByUserId, AuditAction.Created, nameof(FinancialEvent), fe.Id, null, Snap(fe),
            fe.SourceReference, fe.Reason, ct);
        await _context.SaveChangesAsync(ct);
        return fe;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<FinancialEvent> DecideManualPaymentAsync(Guid groupId, Guid eventId, bool approve, Guid deciderUserId, Guid deciderMemberId, string? note, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideManualPaymentCoreAsync(groupId, eventId, approve, deciderUserId, deciderMemberId, note, ct), ct);

    private async Task<FinancialEvent> DecideManualPaymentCoreAsync(Guid groupId, Guid eventId, bool approve, Guid deciderUserId, Guid deciderMemberId, string? note, CancellationToken ct = default)
    {
        var fe = await _context.FinancialEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.GroupId == groupId && e.Source == EventSource.Manual, ct)
            ?? throw new KeyNotFoundException("Ombi halipo.");
        if (fe.Status != FinancialEventStatus.Pending) throw new ConflictException($"Ombi tayari ni {fe.Status}.");
        if (fe.CreatedBy == deciderUserId) throw new ForbiddenException("Aliyeingiza ombi HAWEZI kuliidhinisha mwenyewe (maker-checker).");
        var before = Snap(fe);
        fe.Status = approve ? FinancialEventStatus.Approved : FinancialEventStatus.Rejected;
        fe.ApprovedByGroupMemberId = deciderMemberId;
        fe.ApprovedAt = DateTime.UtcNow;
        fe.ApprovalNote = note;
        await AddAuditAsync(groupId, fe.MemberId, deciderUserId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected,
            nameof(FinancialEvent), fe.Id, before, Snap(fe), fe.SourceReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return fe;
    }

    // =====================================================================
    // 6. MSAMAHA WA FAINI / NYONGEZA YA MUDA — maker-checker
    // =====================================================================
    public async Task<TruthAdjustmentRecord> RequestAdjustmentAsync(Guid groupId, Guid? memberId, TruthAdjustmentKind kind,
        int year, int month, int extraDays, string reason, Guid userId, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new ArgumentException("Mwezi 1..12");
        if (memberId is { } adjMember) await EnsureMakerIsNotSubjectAsync(groupId, adjMember, userId, ct); // SASISHO17 (NEW-3)
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Sababu inahitajika");
        if (kind == TruthAdjustmentKind.GraceExtension && extraDays is < 1 or > 25) throw new ArgumentException("ExtraDays 1..25");
        if (kind == TruthAdjustmentKind.FineWaiver && memberId is null)
            throw new ArgumentException("Msamaha wa faini ni wa mwanachama mmoja. Kwa kikundi kizima tumia GraceExtension.");
        if (memberId is { } mid && !await _context.GroupMembers.AnyAsync(m => m.Id == mid && m.GroupId == groupId, ct))
            throw new KeyNotFoundException("Mwanachama hayupo kwenye kikundi hiki.");

        var rec = new TruthAdjustmentRecord
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = memberId, Kind = kind, Year = year, Month = month,
            ExtraDays = kind == TruthAdjustmentKind.GraceExtension ? extraDays : 0, Reason = reason, RequestedByUserId = userId
        };
        _context.TruthAdjustments.Add(rec);
        await AddAuditAsync(groupId, memberId, userId, AuditAction.Created, nameof(TruthAdjustmentRecord), rec.Id, null, Snap(rec), null, reason, ct);
        await _context.SaveChangesAsync(ct);
        return rec;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<TruthAdjustmentRecord> DecideAdjustmentAsync(Guid groupId, Guid id, bool approve, Guid deciderUserId, string? note = null, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideAdjustmentCoreAsync(groupId, id, approve, deciderUserId, note, ct), ct);

    private async Task<TruthAdjustmentRecord> DecideAdjustmentCoreAsync(Guid groupId, Guid id, bool approve, Guid deciderUserId, string? note = null, CancellationToken ct = default)
    {
        var rec = await _context.TruthAdjustments.FirstOrDefaultAsync(a => a.Id == id && a.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Ombi halipo.");
        if (rec.Status != TruthApprovalStatus.Pending) throw new ConflictException($"Ombi tayari ni {rec.Status}.");
        if (rec.RequestedByUserId == deciderUserId) throw new ForbiddenException("Aliyeomba HAWEZI kuidhinisha mwenyewe (maker-checker).");
        var before = Snap(rec);
        rec.Status = approve ? TruthApprovalStatus.Approved : TruthApprovalStatus.Rejected;
        rec.DecidedByUserId = deciderUserId;
        rec.DecidedAt = DateTime.UtcNow;
        await AddAuditAsync(groupId, rec.GroupMemberId, deciderUserId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected,
            nameof(TruthAdjustmentRecord), rec.Id, before, Snap(rec), null, note ?? rec.Reason, ct);
        await _context.SaveChangesAsync(ct);
        return rec;
    }

    // =====================================================================
    // 7. PENGO LA PDF — kumthibitisha mwenyewe (maker-checker)
    // =====================================================================
    public async Task<MkobaStatementGap> RequestGapResolutionAsync(Guid groupId, Guid gapId, Guid memberId, string evidence, Guid userId, CancellationToken ct = default)
    {
        var gap = await _context.MkobaStatementGaps.FirstOrDefaultAsync(g => g.Id == gapId && g.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Pengo halipo.");
        await EnsureMakerIsNotSubjectAsync(groupId, memberId, userId, ct); // SASISHO17 (NEW-3)
        if (gap.Amount <= 0) throw new ConflictException("Pengo hili ni pesa iliyotoka — haliwezi kupewa mwanachama kama malipo.");
        if (gap.ResolutionStatus == TruthApprovalStatus.Approved) throw new ConflictException("Pengo tayari limethibitishwa.");
        if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("Ushahidi unahitajika (mf. SMS ya M-Koba yenye reference, au statement kamili ya Vodacom).");
        if (!await _context.GroupMembers.AnyAsync(m => m.Id == memberId && m.GroupId == groupId, ct))
            throw new KeyNotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        var before = Snap(gap);
        gap.AssignedGroupMemberId = memberId;
        gap.Evidence = evidence;
        gap.AssumedTimestamp = gap.WindowEnd; // tarehe ya kihafidhina: mwisho wa dirisha la pengo
        gap.ResolutionStatus = TruthApprovalStatus.Pending;
        gap.RequestedByUserId = userId;
        gap.RequestedAt = DateTime.UtcNow;
        await AddAuditAsync(groupId, memberId, userId, AuditAction.Updated, nameof(MkobaStatementGap), gap.Id, before, Snap(gap),
            gap.BeforeReference, evidence, ct);
        await _context.SaveChangesAsync(ct);
        return gap;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<MkobaStatementGap> DecideGapResolutionAsync(Guid groupId, Guid gapId, bool approve, Guid deciderUserId, string? note = null, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideGapResolutionCoreAsync(groupId, gapId, approve, deciderUserId, note, ct), ct);

    private async Task<MkobaStatementGap> DecideGapResolutionCoreAsync(Guid groupId, Guid gapId, bool approve, Guid deciderUserId, string? note = null, CancellationToken ct = default)
    {
        var gap = await _context.MkobaStatementGaps.FirstOrDefaultAsync(g => g.Id == gapId && g.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Pengo halipo.");
        if (gap.ResolutionStatus != TruthApprovalStatus.Pending) throw new ConflictException("Hakuna ombi linalosubiri kwa pengo hili.");
        if (gap.RequestedByUserId == deciderUserId) throw new ForbiddenException("Aliyeomba HAWEZI kuidhinisha mwenyewe (maker-checker).");
        var before = Snap(gap);   // muhimu: kukataa kunafuta ushahidi kwenye pengo — kumbukumbu hii ndiyo inabaki nao
        var assigned = gap.AssignedGroupMemberId;
        if (approve)
        {
            gap.ResolutionStatus = TruthApprovalStatus.Approved;
            gap.ApprovedByUserId = deciderUserId;
            gap.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            gap.ResolutionStatus = null; gap.AssignedGroupMemberId = null; gap.Evidence = null; gap.AssumedTimestamp = null;
            gap.RequestedByUserId = null; gap.RequestedAt = null;
        }
        await AddAuditAsync(groupId, assigned, deciderUserId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected,
            nameof(MkobaStatementGap), gap.Id, before, Snap(gap), gap.BeforeReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return gap;
    }
}
