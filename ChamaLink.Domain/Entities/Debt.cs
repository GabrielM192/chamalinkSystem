namespace ChamaLink.Domain.Entities;

// A missed-contribution shortfall for one member for one period (Sprint 1
// gap: "Debt Table Haipo"). This is separate from a Fine: the Fine is the
// *penalty* for being late, the Debt is the *missing principal itself*
// (the contribution amount the member still owes the group). A member
// can clear a Debt later by contributing more than that month's target -
// the excess is applied against the oldest open Debt first (see
// DebtService.ClearWithPaymentAsync).
public class Debt
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }
    public Guid UserId { get; set; }

    public decimal Amount { get; set; }
    public decimal AmountCleared { get; set; } = 0m;

    public string Reason { get; set; } = string.Empty;

    // First day of the month this shortfall belongs to.
    public DateTime Period { get; set; }

    public DebtStatus Status { get; set; } = DebtStatus.Outstanding;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClearedAt { get; set; }

    public Group? Group { get; set; }
    public GroupMember? GroupMember { get; set; }
}
