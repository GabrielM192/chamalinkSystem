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
    JoinCodeChanged = 115
}
