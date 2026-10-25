using System.ComponentModel.DataAnnotations;
namespace ChamaLink.Domain.Entities;

// ============================================================================
//  Entities za "Injini ya Ukweli".
//  KANUNI: data GHAFI tu inahifadhiwa. Mgawanyo (mchango/faini/kianzio/akiba)
//  HAUHIFADHIWI — unahesabiwa upya kila mara na ContributionTruthEngine.
// ============================================================================

public enum TruthApprovalStatus { Pending = 1, Approved = 2, Rejected = 3 }

/// <summary>
/// Mipangilio ya Injini ya Ukweli kwa kila kikundi (meza yake yenyewe — haitegemei GroupPolicies/GroupSettings).
/// Viongozi wanaiweka kupitia PUT /api/truth/{groupId}/settings.
/// Hakuna default ya kiasi cha pesa wala siku: mara ya kwanza thamani zote za fedha LAZIMA ziwekwe
/// (TruthSetupService.UpdateSettingsAsync inakataa zikikosekana).
/// </summary>
public class TruthGroupSettings
{
    public Guid GroupId { get; set; }
    public decimal MonthlyContribution { get; set; }
    public decimal LateFine { get; set; }
    /// <summary>
    /// Faini kama ASILIMIA ya mchango wa mwezi (Katiba Kanuni 13(3)/16(9): "faini 50%"). Ikiwa na thamani,
    /// inachukua nafasi ya LateFine (kiasi maalum) — mchango ukibadilika, faini inafuata yenyewe.
    /// </summary>
    public decimal? LateFinePercent { get; set; }
    public decimal JoiningFee { get; set; }
    /// <summary>Mchango wa mwezi M unatakiwa kufika tarehe hii ya mwezi M+1.</summary>
    public int DueDateDay { get; set; }
    /// <summary>Siku za neema baada ya DueDateDay (0 = faini kuanzia siku inayofuata).</summary>
    public int GracePeriodDays { get; set; }
    public bool FineOnPartialPayment { get; set; }
    /// <summary>Mwanachama asipolipa mwezi kufikia tarehe ya mwisho, akiba yake (ziada ya zamani) itumike kulipa? Uamuzi wa viongozi.</summary>
    public bool AutoDeductSavings { get; set; }
    /// <summary>Mwezi wa kwanza wa michango ya kikundi (siku ya 1).</summary>
    public DateTime ContributionStartDate { get; set; }
    /// <summary>Waliojiunga kufikia tarehe hii = waanzilishi (Kianzio kwa awamu). Null = wote ni waanzilishi.</summary>
    public DateTime? FoundingMembersCutoff { get; set; }
    /// <summary>Tarehe ya mwisho ya kumbukumbu za mkononi. Null = manual hairuhusiwi.</summary>
    public DateTime? ManualEntryCutoff { get; set; }
    public decimal LoanLatePenaltyAmount { get; set; }

    // ---- SASISHO15: Sera ya mikopo (inatoka kwa viongozi; hakuna thamani ndani ya msimbo) ----
    /// <summary>Mikopo imefunguliwa? Chaguo-msingi: HAPANA mpaka viongozi waweke sera kamili.</summary>
    public bool LoansEnabled { get; set; }
    /// <summary>Riba ya flat kwa mkopo mzima (%), mf. 10 = 10%.</summary>
    public decimal? LoanInterestPercent { get; set; }
    /// <summary>Idadi ya miezi ya kurejesha (mafungu sawa kila mwezi).</summary>
    public int? LoanRepaymentMonths { get; set; }
    /// <summary>Kikomo cha mkopo mmoja (null = hakuna kikomo cha kiasi).</summary>
    public decimal? LoanMaxAmount { get; set; }
    /// <summary>Kikomo kama mara N ya michango aliyolipa mwanachama (Injini ya Ukweli). Null = hakitumiki.</summary>
    public decimal? LoanMaxMultipleOfContributions { get; set; }
    /// <summary>Miezi ya chini ya uanachama kabla ya kukopa. Null = hakuna sharti.</summary>
    public int? LoanMinMembershipMonths { get; set; }
    /// <summary>Mwanachama mwenye malimbikizo/Kianzio kinachodaiwa anaweza kukopa? Chaguo-msingi: hapana.</summary>
    public bool LoanAllowWithArrears { get; set; }
    /// <summary>Mwanachama anaweza kuwa na mkopo zaidi ya mmoja unaodaiwa? Chaguo-msingi: hapana.</summary>
    public bool LoanAllowMultipleActive { get; set; }
    /// <summary>Salio la M-Koba lisishuke chini ya kiasi hiki baada ya kutoa mkopo (0 = hakuna akiba ya lazima).</summary>
    public decimal LoanMinCashReserve { get; set; }
    /// <summary>Kura za viongozi huru zinazohitajika kuidhinisha ombi (mkopaji na aliyeomba hawapigi kura).</summary>
    public int? LoanRequiredApprovals { get; set; }
    /// <summary>Siku ambazo ombi lililoidhinishwa linasubiri utoaji kabla ya kuisha.</summary>
    public int? LoanApprovalValidDays { get; set; }

    // ---- SASISHO17: utawala wa vyeo na uhalisia wa salio (sera ya kikundi; hakuna thamani ndani ya msimbo) ----
    /// <summary>Kura za NDIYO zinazohitajika kubadilisha nafasi ya uongozi. Null = 1 (kiwango cha chini: mtu mwingine mmoja).</summary>
    public int? RoleChangeApprovals { get; set; }
    /// <summary>Siku ambazo kiongozi mpya anasubiri kabla ya kuidhinisha/kupiga kura kama kiongozi. Null = 0.</summary>
    public int? LeaderCoolingOffDays { get; set; }
    /// <summary>Umri wa juu (siku) wa taarifa ya M-Koba inayotumika kuidhinisha malipo/mikopo. Null = haijawekwa → ukaguzi wa pesa unakataa.</summary>
    public int? CashStatementMaxAgeDays { get; set; }

    // ---- SASISHO18: sera ya vikundi vinavyochangishana KILA TUKIO ----
    //
    // Mfumo wa pochi: kila mwanachama ana salio ndani ya kikundi. Tukio
    // linapoidhinishwa, mchango unakatwa kwenye salio — lakini salio
    // HALIRUHUSIWI kushuka chini ya MemberMinimumBalance. Kinachobaki
    // kinakuwa deni (EventObligations) kinachokamilishwa kwa amana zijazo.
    //
    // Zote zinaanzia 0/false: hakuna thamani ya fedha iliyoandikwa kwenye
    // msimbo — viongozi wanaziweka kwenye Mipangilio.

    /// <summary>Kiwango cha chini (floor) cha salio la mwanachama. 0 = hakuna floor.</summary>
    public decimal MemberMinimumBalance { get; set; }

    /// <summary>Siku ambazo mwanachama anapewa kujaza salio baada ya tukio. 0 = hakuna muda maalum.</summary>
    public int EventTopUpWindowDays { get; set; }

    /// <summary>Faini ya deni la tukio linapochelewa. 0 = hakuna faini.</summary>
    public decimal OverdueEventFineAmount { get; set; }

    /// <summary>Kikomo cha makato ya matukio kwa mwanachama kwa mwezi. 0 = hakuna kikomo.</summary>
    public decimal MaxEventDeductionPerMemberPerMonth { get; set; }

    /// <summary>Idadi ya juu ya matukio yanayoidhinishwa kwa mwezi. 0 = hakuna kikomo.</summary>
    public int MaxEventsPerMonth { get; set; }

    /// <summary>true = mnufaika naye anachangia tukio lake mwenyewe.</summary>
    public bool BeneficiaryContributes { get; set; }

    // ---- SASISHO19 (kanuni za J3): ucheleweshaji -> faini -> hatari ya kuondolewa ----

    /// <summary>
    /// Siku za neema BAADA ya faini kutolewa. Deni likibaki bila kulipwa baada ya muda huu,
    /// mwanachama anaingia kwenye orodha ya "hatarini kuondolewa" (inaonekana kwa wanachama WOTE).
    /// 0 = hakuna muda wa nyongeza.
    /// </summary>
    public int EventFineGraceDays { get; set; }

    /// <summary>
    /// Idadi ya chini ya viongozi wanayohitaji KUIDHINISHA ili mwanachama aondolewe.
    /// 0 = tumia sera ya mafao (BenefitGovernance.StatusChangeApprovals), lakini isipungue 3
    /// (kanuni: "viongozi ZAIDI ya wawili").
    /// </summary>
    public int RemovalApprovalsRequired { get; set; }

    /// <summary>
    /// Kizidishi cha faini kwa mwanachama anayerudi baada ya kuondolewa (mf. 2 = faini x 2).
    /// 0 = hakuna ada ya kurudi.
    /// </summary>
    public decimal ReinstatementFineMultiplier { get; set; }

    /// <summary>true = anayerudi analipa tena ada ya kujiunga (kiingilio) kabla ya kurejeshwa.</summary>
    public bool ReinstatementRequiresJoiningFee { get; set; }

    // ---- SASISHO21: sanduku la malipo, lango la kujiunga, na SMS ----

    /// <summary>
    /// Kiasi kinachohitaji MKONO WA PILI kabla ya kuthibitisha malipo yasiyotambulika.
    /// 0 = kila kiasi kinahitaji mkono mmoja tu (tabia ya zamani).
    /// </summary>
    public decimal PaymentSecondApprovalAmount { get; set; }

    /// <summary>Amani za mwanachama zinaonyeshwa kwenye sanduku kwa ajili ya kuhakiki? (chaguo-msingi: ndiyo)</summary>
    public bool ClaimsEnabled { get; set; } = true;

    /// <summary>true = mtu anakuwa mwanachama TU baada ya malipo ya kujiunga kuthibitishwa (lango).</summary>
    public bool JoiningPaymentRequired { get; set; }

    /// <summary>Sehemu ya ada ya kujiunga inayoingia kwenye AKIBA (iliyobaki ni kianzio). 0 = yote kianzio.</summary>
    public decimal JoiningSavingsPortion { get; set; }

    /// <summary>Siku ambazo ombi la kujiunga linasubiri malipo kabla ya kuisha. 0 = haliondolewi.</summary>
    public int JoiningPaymentWindowDays { get; set; }

    // ---- SMS ----
    /// <summary>true = SMS zinaweza kutumwa kwa kikundi hiki.</summary>
    public bool SmsEnabled { get; set; }
    /// <summary>Salio la SMS (credits) linatosha chini ya kiwango hiki → onyo kwa viongozi. 0 = hakuna onyo.</summary>
    public int SmsLowCreditThreshold { get; set; }
    /// <summary>Saa ya kuanza kutuma SMS (EAT). 0 = hakuna kizuizi.</summary>
    public int SmsQuietFromHour { get; set; }
    /// <summary>Saa ya kuacha kutuma SMS (EAT). 24 = mwisho wa siku.</summary>
    public int SmsQuietToHour { get; set; } = 24;
    /// <summary>SMS za juu ya mwanachama kwa tukio (0 = hakuna kikomo).</summary>
    public int SmsMaxPerMemberPerEvent { get; set; }

    /// <summary>
    /// Kikomo cha ujumbe mmoja kwa herufi kabla ya kugawanywa (ujumbe mrefu = SMS 2+).
    /// 0 = tumia 160 (kawaida ya GSM-7).
    /// </summary>
    public int SmsMaxCharsPerMessage { get; set; }

    // ------------------------------------------------------------------
    // SASISHO21 — RUFA (APPEAL)
    // ------------------------------------------------------------------

    /// <summary>Rufaa zinaruhusiwa? Zikizimwa, mwanachama hawezi kuwasilisha rufaa mpya.</summary>
    public bool AppealsEnabled { get; set; } = true;

    /// <summary>Siku za kuwasilisha rufaa tangu uamuzi. 0 = hakuna kikomo cha muda.</summary>
    public int AppealWindowDays { get; set; }

    /// <summary>Viongozi wanaohitajika kuamua rufaa. Kamwe chini ya 2 (mmoja haamui rufaa).</summary>
    public int AppealApprovalsRequired { get; set; } = 2;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
}

/// <summary>Mstari mmoja wa statement ya M-Koba, kama ulivyo kwenye PDF. Haubadilishwi baada ya kuingizwa.</summary>
public class MkobaRawTransaction
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>Reference ya M-Koba (ya kipekee ndani ya kikundi).</summary>
    public string Reference { get; set; } = string.Empty;
    /// <summary>Saa za ukutani za EAT kama zilivyo kwenye PDF.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Namba ya simu iliyosafishwa: 255XXXXXXXXX.</summary>
    public string Phone { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public bool IsWithdrawal { get; set; }
    public string Description { get; set; } = string.Empty;
    public int RowOrder { get; set; }
    public string StatementFileHash { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public Guid ImportedByUserId { get; set; }
    /// <summary>SASISHO17: rasimu ya import iliyoidhinishwa iliyoleta safu hii.</summary>
    public Guid? ImportId { get; set; }

    // ---- SASISHO21: umiliki uliothibitishwa (sanduku la malipo) ----
    //
    // Kawaida pesa inamilikiwa kwa NAMBA ya simu. Lakini mwanachama anaweza kulipa
    // kwa namba ya mtu mwingine (mke, mfanyakazi, duka la M-Pesa) akiweka kodi yake
    // ya malipo. Kiongozi akithibitisha, mstari huu unamilikiwa na mwanachama HUYU
    // na HAUMILIKIWI tena kwa namba — umiliki mmoja tu, hakuna kuhesabu mara mbili.
    public Guid? AttributedGroupMemberId { get; set; }
    public Guid? AttributedByUserId { get; set; }
    public DateTime? AttributedAt { get; set; }
    [MaxLength(300)] public string? AttributionReason { get; set; }
}

/// <summary>
/// Namba ya simu ya mwanachama (inawekwa na kiongozi). Malipo ya M-Koba yanaunganishwa
/// na mwanachama kwa NAMBA YA SIMU pekee — si kwa jina.
/// </summary>
public class MemberPhone
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }
    public string Phone { get; set; } = string.Empty;
    /// <summary>Jina lililoonekana kwenye M-Koba kwa namba hii (kwa kiongozi kuhakiki).</summary>
    public string? MkobaNameSeen { get; set; }
    public bool MatchedInMkoba { get; set; }
    public Guid AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }

    // ---- Maker-checker + muda wa umiliki ----
    // Kuunganisha namba = kuamua pesa zake ni za nani, kwa hiyo kunahitaji kiongozi WA PILI (Pending -> Approved).
    // Namba haifutwi: inafungwa kwa tarehe (ValidTo). Malipo ya baada ya ValidTo hayahesabiwi kwa mwanachama huyu
    // tena — yanaonekana kama "namba isiyotambulika" ili viongozi wayaamue.
    public TruthApprovalStatus Status { get; set; } = TruthApprovalStatus.Pending;
    /// <summary>Malipo ya kuanzia tarehe hii (EAT) ndiyo ya mwanachama huyu. Null = tangu mwanzo.</summary>
    public DateTime? ValidFrom { get; set; }
    /// <summary>Malipo hadi tarehe hii (EAT, mwisho wa siku). Null = bado inatumika.</summary>
    public DateTime? ValidTo { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Ombi la kufunga namba (linasubiri kiongozi wa pili).</summary>
    public DateTime? PendingValidTo { get; set; }
    public string? PendingEndReason { get; set; }
    public Guid? EndRequestedByUserId { get; set; }
    public DateTime? EndRequestedAt { get; set; }

    /// <summary>Je, malipo ya saa hii (EAT) yanahesabiwa kwa mwanachama huyu?</summary>
    public bool Covers(DateTime atEat) =>
        Status == TruthApprovalStatus.Approved
        && (ValidFrom is null || atEat >= ValidFrom.Value.Date)
        && (ValidTo is null || atEat < ValidTo.Value.Date.AddDays(1));
}

/// <summary>
/// Pengo kwenye mnyororo wa balance wa M-Koba: pesa iliingia (balance inathibitisha)
/// lakini mstari wake haupo kwenye PDF. Mwenyewe anathibitishwa na viongozi wawili.
/// </summary>
public class MkobaStatementGap
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string? AfterReference { get; set; }
    public string BeforeReference { get; set; } = string.Empty;
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public decimal Amount { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    // Utatuzi (maker-checker)
    public Guid? AssignedGroupMemberId { get; set; }
    /// <summary>Ushahidi: mf. reference ya SMS ya M-Koba ya mwanachama, au statement kamili ya Vodacom.</summary>
    public string? Evidence { get; set; }
    public DateTime? AssumedTimestamp { get; set; }
    public TruthApprovalStatus? ResolutionStatus { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTime? RequestedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public enum TruthAdjustmentKind { FineWaiver = 1, GraceExtension = 2 }

/// <summary>Msamaha wa faini / nyongeza ya muda kwa mwezi fulani. GroupMemberId = null => kwa wanachama wote.</summary>
public class TruthAdjustmentRecord
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid? GroupMemberId { get; set; }
    public TruthAdjustmentKind Kind { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int ExtraDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public TruthApprovalStatus Status { get; set; } = TruthApprovalStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
}
