namespace ChamaLink.Domain.Entities;

// One leader's decision (approve or reject) on a single Withdrawal.
// A Withdrawal can need more than one of these before it moves to
// Approved, depending on the group's GroupSettings.WithdrawalApprovalMode
// (e.g. TreasurerAndChairperson needs one row from each of those roles).
public class WithdrawalApproval
{
    public Guid Id { get; set; }

    public Guid WithdrawalId { get; set; }
    public Withdrawal? Withdrawal { get; set; }

    public Guid GroupMemberId { get; set; }
    public GroupMember? GroupMember { get; set; }

    // Snapshot of the member's role at the moment they voted, so a later
    // role change (e.g. a new Treasurer takes over) never rewrites what
    // actually happened on this withdrawal's history.
    public GroupRole RoleAtDecision { get; set; }

    // true = approve vote, false = reject/veto.
    public bool Approved { get; set; }

    // Mostly used for rejections, but a leader can leave a note on an
    // approval too (e.g. "approved on condition receipts are kept").
    public string? Reason { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}
