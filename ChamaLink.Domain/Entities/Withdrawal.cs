namespace ChamaLink.Domain.Entities;

// A structured, audited record of money leaving the group's external
// wallet/account for a specific purpose (e.g. paying out a funeral
// benefit, a wedding contribution, a group expense). This is the
// "Withdrawal Management" record from the ChamaLink v1 spec - distinct
// from WalletWithdrawal:
//
//   WalletWithdrawal = a raw "Withdraw / Transfer fund" row noticed
//                       during an M-Koba statement import. It only knows
//                       WHO triggered it on M-Koba and HOW MUCH - it does
//                       not know WHY or who authorised it.
//
//   Withdrawal        = the group's own record of an approved
//                        expenditure: what it was for, who the money
//                        went to, who approved it (normally the
//                        Treasurer), and who recorded it. A leader can
//                        create this directly (manual entry), or link it
//                        to a WalletWithdrawal once they work out what
//                        that unexplained M-Koba withdrawal was for.
public class Withdrawal
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;
    public decimal Amount { get; set; }

    // Why the money was taken out, e.g. "Funeral Support", "Wedding Gift".
    public string Purpose { get; set; } = string.Empty;

    // Who the withdrawal was for / who received the benefit. Kept as free
    // text (not a GroupMember FK) because a beneficiary is sometimes a
    // relative of a member, not a member themselves (e.g. paying a
    // hospital or a funeral home directly).
    public string BeneficiaryName { get; set; } = string.Empty;

    // NEW (Reports Engine gap: Member Financial Profile "Benefits
    // Received" was summing TransactionType.ShareOut - the annual
    // dividend, a completely different concept from a welfare/event
    // payout. There was no way to tell which member a Withdrawal
    // actually benefited at all. Set directly when the beneficiary is a
    // group member (mirrors GroupEvent.BeneficiaryGroupMemberId); left
    // null when the money went to someone outside the group (only
    // BeneficiaryName is meaningful then). AnalyticsService now sums Paid
    // withdrawals against this field instead of ShareOut.
    public Guid? BeneficiaryGroupMemberId { get; set; }
    public GroupMember? BeneficiaryGroupMember { get; set; }

    // Optional: which event this payout was for (e.g. the msiba/funeral
    // event that raised the money being paid out here). Lets a leader
    // trace "this withdrawal settled that event" without guessing from
    // the Purpose text.
    public Guid? GroupEventId { get; set; }
    public GroupEvent? GroupEvent { get; set; }

    // NEW (Sprint 2 gap: Withdrawal Approval Workflow Haipo). A withdrawal
    // starts life as Pending the moment it is recorded/requested, and only
    // moves to Approved/Rejected once a leader (normally the Treasurer or
    // Chairperson) makes a decision, then to Paid once the money has
    // actually left the group's hands. See WithdrawalService.
    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Pending;
    public DateTime? DecisionAt { get; set; }
    public string? RejectionReason { get; set; }

    // Who authorised this withdrawal - normally the Treasurer. Null until
    // a decision (approve/reject) has actually been made.
    public Guid? ApprovedByGroupMemberId { get; set; }
    public GroupMember? ApprovedByGroupMember { get; set; }

    // Who recorded this entry into ChamaLink (may be the same person as
    // ApprovedBy, or the Secretary recording on the Treasurer's behalf).
    public Guid RecordedByGroupMemberId { get; set; }
    public GroupMember? RecordedByGroupMember { get; set; }

    // Optional external reference (bank/mobile-money transaction ID) for
    // matching against a statement later.
    public string? ReferenceNo { get; set; }

    // Optional extra context beyond Purpose (e.g. "Approved at the
    // 2026-09-01 committee meeting, see minutes").
    public string? Notes { get; set; }

    // If this withdrawal explains a specific M-Koba wallet withdrawal
    // that was auto-imported, link it here so the two records reconcile
    // instead of double-counting the same money leaving the group.
    public Guid? WalletWithdrawalId { get; set; }
    public WalletWithdrawal? WalletWithdrawal { get; set; }

    // NEW (Withdrawal Governance gap): every individual approve/reject
    // vote toward this withdrawal's decision. See WithdrawalApproval.
    public List<WithdrawalApproval> Approvals { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Group? Group { get; set; }
}
