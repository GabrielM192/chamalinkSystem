namespace ChamaLink.Domain.Entities;

// Stores a "Withdraw / Transfer fund" row found in an M-Koba statement.
// This money left the group's mobile wallet (e.g. treasurer paying out a
// loan in cash), so it is NOT a member's own contribution and is never
// written as a LedgerEntry against any member's Savings/Fine/SocialFund
// account. Instead it lives here so the treasurer can see it, and later
// mark it as explained/reconciled (e.g. "this was loan payout to Juma").
public class WalletWithdrawal
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }

    // Straight from the statement - kept even though it isn't tied to a
    // specific member's account, so the treasurer can identify the row.
    public string ReferenceNo { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }

    // The treasurer can mark this as explained once they know what the
    // withdrawal was for (e.g. a loan disbursement, a group expense).
    public bool IsReconciled { get; set; } = false;
    public string? ReconciliationNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Group? Group { get; set; }
}
