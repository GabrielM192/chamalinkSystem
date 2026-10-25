namespace ChamaLink.Domain.Entities;

/// <summary>
/// Immutable audit trail — every financial action has an audit event.
/// Insert only, never update or delete. Every shilling is traceable.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid? MemberId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string? ActorMembershipNumber { get; set; }

    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? ChangesJson { get; set; }

    public EventSource Source { get; set; } = EventSource.Manual;
    public string? SourceReference { get; set; }
    public string? SourceMetadataJson { get; set; }

    public int? PolicyVersion { get; set; }
    public string? AllocationResultJson { get; set; }

    public string? Reason { get; set; }
    public string? IpAddress { get; set; }

    public bool IsSystemGenerated { get; set; } = false;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    // ---- Mnyororo wa hash (SASISHO13): kila tukio "linafungwa" kwa hash ya lililotangulia ndani ya kikundi ----
    /// <summary>Nafasi kwenye mnyororo wa kikundi (1, 2, 3...). Null = bado halijafungwa.</summary>
    public long? ChainIndex { get; set; }
    public string? PrevHash { get; set; }
    public string? Hash { get; set; }
}

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    PaymentReceived = 10,
    ContributionCharged = 11,
    ContributionPaid = 12,
    LoanIssued = 20,
    LoanRepaymentPaid = 21,
    FineCharged = 30,
    FinePaid = 31,
    JoiningFeeCharged = 40,
    JoiningFeePaid = 41,
    SavingsDeposited = 50,
    SavingsWithdrawn = 51,
    SavingsHeld = 52,
    SavingsReleased = 53,
    WelfareEventCreated = 60,
    WelfareObligationCreated = 61,
    WelfareContributionPaid = 62,
    WelfareDisbursement = 63,
    MemberStatusChanged = 70,
    GroupPolicyCreated = 80,
    GroupPolicyVersioned = 81,
    GovernanceApproved = 90,
    GovernanceRejected = 91,
    ImportStarted = 100,
    ImportCompleted = 101,
    ImportFailed = 102,
    MembershipRequested = 110,
    MemberLinkedToAccount = 111,
    MemberRoleChanged = 112,
    MemberNumberIssued = 113,
    MemberRecordCreated = 114,
    JoinCodeChanged = 115,
    // Mafao na utawala wa watu wengi
    BenefitCaseOpened = 120,
    EvidenceUploaded = 121,
    EvidenceVerified = 122,
    EvidenceRejected = 123,
    VoteCast = 124,
    BenefitApproved = 125,
    BenefitRejected = 126,
    BenefitPaidConfirmed = 127,
    EvidenceAccessed = 128,
    BenefitCaseClosed = 129,
    PhoneLinkApproved = 130,
    PhoneLinkEnded = 131,
    StatusChangeRequested = 132,
    WithdrawalExplained = 133,
    ResetBlocked = 134,
    PaymentInitiated = 135,
    RiskBlocked = 136,
    RelationAdded = 137,
    RelationEndRequested = 138,
    RelationEnded = 139,
    BenefitLedgerPosted = 140,
    /// <summary>SASISHO14: kesi iliyosubiri pesa imekaguliwa upya (AwaitingFunds -> Approved).</summary>
    BenefitFundsRechecked = 141,
    // SASISHO15
    RelationApproved = 142,
    RelationRejected = 143,
    LoanApplicationSubmitted = 144,
    LoanApplicationApproved = 145,
    LoanApplicationRejected = 146,
    LoanApplicationCancelled = 147,
    LoanDisbursementInitiated = 148,
    LoanDisbursementConfirmed = 149,
    LoanDefaultRequested = 150,
    LoanDefaultDecided = 151,
    LoanApplicationExpired = 152,
    SessionsRevoked = 153,
    // SASISHO17
    RoleChangeRequested = 160,
    RoleChangeApproved = 161,
    RoleChangeRejected = 162,
    RoleChangeCancelled = 163,
    LeaderRoleRevokedByStatus = 164,
    StatementImportStaged = 165,
    StatementImportApproved = 166,
    StatementImportRejected = 167,
    StatementImportCancelled = 168,
    ResetRequested = 169,
    ResetApproved = 170,
    StatusChangeApplied = 171,
    // SASISHO20 — msimamizi wa mfumo (kikundi KINAONA kila kitendo kinachokigusa)
    PlatformUnlockedAccount = 180,
    PlatformResetCodeIssued = 181,
    PlatformResetCodeRedeemed = 182,
    PlatformJoinCodeRotated = 183,
    PlatformNoticeSent = 184,
    PlatformBreakGlassApproved = 185,
    PlatformBreakGlassUsed = 186,
    PlatformBreakGlassRevoked = 187,
    // SASISHO21 — sanduku la malipo (kodi ya malipo + madai ya wanachama)
    PaymentReceiptConfirmed = 190,
    PaymentReceiptSecondApproved = 191,
    PaymentReceiptRejected = 192,
    PaymentClaimSubmitted = 193,
    PaymentClaimDecided = 194,
    PaymentAttributionCorrected = 195,
    SmsQueued = 200,
    SmsSent = 201,
    SmsFailed = 202,
    SmsSuppressed = 203,
    // SASISHO21 — rufaa (kupinga uamuzi unaomgusa mwanachama)
    AppealFiled = 210,
    AppealVoted = 211,
    AppealDecided = 212,
    AppealWithdrawn = 213
}
