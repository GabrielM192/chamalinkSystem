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
