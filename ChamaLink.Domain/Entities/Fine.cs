namespace ChamaLink.Domain.Entities;

// A single fine charged to one member (Sprint 1 gap: "Fine Entity Haipo").
// This is the structured record that ReportsController's Fine Report and
// a member's Financial Profile read from. It is deliberately separate
// from LedgerEntry.FineIssue/FinePayment (which remain the record of the
// actual money movement, kept so old ledger-based views such as the
// M-Koba import waterfall stay consistent) - FineService is the only
// place that should create/update a Fine, and it keeps both in sync.
public class Fine
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }
    public Guid UserId { get; set; }

    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; } = 0m;

    public FineReasonType ReasonType { get; set; }
    public string Reason { get; set; } = string.Empty;

    // Optional link back to the GroupEvent that caused this fine (e.g. a
    // welfare-balance-below-minimum fine). Null for monthly-contribution
    // late fines.
    public Guid? GroupEventId { get; set; }

    // Which contribution period (first day of the month) this fine
    // belongs to, when it comes from a missed monthly contribution.
    // Null for fines not tied to a specific month (e.g. welfare fines).
    public DateTime? Period { get; set; }

    public FineStatus Status { get; set; } = FineStatus.Pending;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }

    public Group? Group { get; set; }
    public GroupMember? GroupMember { get; set; }
    public GroupEvent? GroupEvent { get; set; }
}
