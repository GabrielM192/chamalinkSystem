namespace ChamaLink.Domain.Entities;

// Tracks, per member, how much of a GroupEvent's TargetAmountPerMember
// they have paid so far (Sprint 2 gap: "Event Contribution Tracking
// Haipo"). One row is created for every member the moment an event is
// triggered (Expected = TargetAmountPerMember), then kept up to date as
// LedgerEntry.EventContribution rows are posted (manually, or via the
// M-Koba import waterfall's Step 2). This is what answers "who has
// contributed to this Msiba and who hasn't" directly, instead of
// re-summing the ledger every time.
public class EventContribution
{
    public Guid Id { get; set; }
    public Guid GroupEventId { get; set; }
    public Guid GroupMemberId { get; set; }
    public Guid UserId { get; set; }

    public decimal ExpectedAmount { get; set; }
    public decimal PaidAmount { get; set; } = 0m;

    public EventContributionStatus Status { get; set; } = EventContributionStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPaidAt { get; set; }

    public GroupEvent? GroupEvent { get; set; }
    public GroupMember? GroupMember { get; set; }
}
