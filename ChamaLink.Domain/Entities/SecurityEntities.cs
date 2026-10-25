namespace ChamaLink.Domain.Entities;

/// <summary>
/// SASISHO17 (NEW-2): kubadilisha nafasi ya mwanachama (kupandishwa uongozi, kushushwa, kukabidhi uenyekiti) ni
/// PENDEKEZO + KURA — hakuna mtu mmoja (hata Mwenyekiti) anayebadilisha vyeo peke yake. Idadi ya kura inatoka kwenye
/// sera ya kikundi (TruthGroupSettings.RoleChangeApprovals). Kiongozi mpya anapata nguvu za kuidhinisha baada ya
/// kipindi cha kusubiri (TruthGroupSettings.LeaderCoolingOffDays) — GroupMember.LeaderPowersFrom.
/// </summary>
public class RoleChangeRequest
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }
    public GroupRole FromRole { get; set; }
    public GroupRole ToRole { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int RequiredApprovals { get; set; }
    public int RejectionsToReject { get; set; }
    /// <summary>true = wapiga kura ni viongozi (wenye nguvu kamili); false = kikundi hakina viongozi wengine, wanachama wenye akaunti wanapiga kura.</summary>
    public bool LeadersOnly { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>Siku za kusubiri zilizotumika (nakala ya sera wakati wa idhini — ukaguzi).</summary>
    public int? CoolingOffDaysApplied { get; set; }
}

public enum StatementImportStatus { Pending = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

/// <summary>
/// SASISHO17 (P0-2, P1-7, P1-8): PDF ya M-Koba inaingia kama RASIMU (Pending). Kiongozi WA PILI anaona tofauti (safu mpya,
/// salio la mwisho, mapengo mapya, namba zisizojulikana, migongano) kisha anaidhinisha; ndipo safu zinaingia kwenye
/// MkobaRawTransactions — zote ndani ya transaction MOJA. Faili halisi linahifadhiwa likiwa limesimbwa (forensics).
/// </summary>
public class MkobaStatementImport
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    /// <summary>Ufunguo wa hifadhi ya faili (lililosimbwa) — IEvidenceStorage.</summary>
    public string? StorageKey { get; set; }
    public StatementImportStatus Status { get; set; } = StatementImportStatus.Pending;
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public int RowsInPdf { get; set; }
    public int NewRows { get; set; }
    public int ExistingRows { get; set; }
    public int ConflictingRows { get; set; }
    public int BackdatedRows { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public decimal? LastBalance { get; set; }
    /// <summary>Safu mpya zilizosomwa (JSON) — zinaingizwa tu baada ya idhini.</summary>
    public string RowsJson { get; set; } = "[]";
    /// <summary>Ripoti ya uadilifu iliyoonyeshwa kwa mwidhinishaji (JSON).</summary>
    public string ReportJson { get; set; } = "{}";
}

/// <summary>SASISHO17 (P0-6): kufuta data ya fedha (staging tu, AllowReset=true) kunahitaji viongozi WAWILI.</summary>
public class GroupResetRequest
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public bool KeepLoans { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
}

/// <summary>
/// SASISHO17 (P1-4, NEW-5): majaribio mabaya ya kuingia kwa (akaunti + IP). Kufungiwa kwa IP moja hakumfungii mwenye
/// akaunti anayeingia kutoka mahali pengine (kinga dhidi ya kutumia lockout kama silaha); kikomo cha akaunti nzima
/// (IP zote) kinazuia mashambulizi yanayosambazwa.
/// </summary>
public class LoginFailure
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>SHA-256 ya IP (hatuhifadhi IP wazi).</summary>
    public string IpKey { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockoutEndUtc { get; set; }
}
