namespace ChamaLink.Domain.Entities;

public enum WelfareType
{
    Death = 1,
    Hospitalization = 2,
    Marriage = 3,
    Birth = 4,
    Disaster = 5,
    Emergency = 6,
    Other = 7
}

public enum WelfareEventStatus
{
    Draft = 1,
    Open = 2,
    Collecting = 3,
    Collected = 4,
    Disbursing = 5,
    Closed = 6,
    Cancelled = 7
}

public enum WelfareCollectionMethod
{
    DeductFromSavings = 1,
    ManualContribution = 2,
    AutoDeduct = 3
}

public enum ObligationStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Waived = 4,
    Exempted = 5
}

public class WelfareEvent
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WelfareType Type { get; set; } = WelfareType.Other;
    public Guid? BeneficiaryMemberId { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public decimal RequiredContributionPerMember { get; set; }
    public decimal TotalExpected { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalDisbursed { get; set; }
    public WelfareEventStatus Status { get; set; } = WelfareEventStatus.Draft;
    public WelfareCollectionMethod CollectionMethod { get; set; } = WelfareCollectionMethod.DeductFromSavings;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeadlineDate { get; set; }
    public int PolicyVersion { get; set; } = 1;

    public Group? Group { get; set; }
    public GroupMember? BeneficiaryMember { get; set; }
    public ICollection<WelfareObligation> Obligations { get; set; } = new List<WelfareObligation>();
}

public class WelfareObligation
{
    public Guid Id { get; set; }
    public Guid WelfareEventId { get; set; }
    public Guid MemberId { get; set; }
    public decimal RequiredAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount => RequiredAmount - PaidAmount;
    public ObligationStatus Status { get; set; } = ObligationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public Guid? WaivedBy { get; set; }
    public string? WaivedReason { get; set; }

    public WelfareEvent? WelfareEvent { get; set; }
    public GroupMember? Member { get; set; }
}

public class WelfareContribution
{
    public Guid Id { get; set; }
    public Guid WelfareObligationId { get; set; }
    public Guid WelfareEventId { get; set; }
    public Guid MemberId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public Guid PaidBy { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid FinancialEventId { get; set; }
    public Guid? AuditTrailId { get; set; }

    public WelfareObligation? Obligation { get; set; }
    public FinancialEvent? FinancialEvent { get; set; }
}

public class WelfareDisbursement
{
    public Guid Id { get; set; }
    public Guid WelfareEventId { get; set; }
    public Guid? BeneficiaryMemberId { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime DisbursedAt { get; set; } = DateTime.UtcNow;
    public Guid DisbursedBy { get; set; }
    public string Method { get; set; } = "Cash";
    public string? Reference { get; set; }
    public Guid FinancialEventId { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? ApprovedBy { get; set; }
    public Guid? AuditTrailId { get; set; }

    public WelfareEvent? WelfareEvent { get; set; }
    public FinancialEvent? FinancialEvent { get; set; }
}
