namespace ChamaLink.Domain.Entities;

public class GroupEvent
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public decimal TargetAmountPerMember { get; set; }
    public decimal AmountDeducted { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsResolved { get; set; } = false;

    public DateTime EventDate { get; set; }
    public DateTime DeadlineDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // NEW (Sprint 2 gap: Event Beneficiary Tracking Haijakamilika).
    // Who this event's benefit is for. When the beneficiary is a member
    // of the group, BeneficiaryGroupMemberId is set; when it's a relative
    // or someone outside the group (e.g. a member's parent), only
    // BeneficiaryName (free text) is used - same reasoning as
    // Withdrawal.BeneficiaryName.
    public Guid? BeneficiaryGroupMemberId { get; set; }
    public string? BeneficiaryName { get; set; }

    // Navigation Properties
    public Group? Group { get; set; }
    public GroupMember? BeneficiaryGroupMember { get; set; }
}