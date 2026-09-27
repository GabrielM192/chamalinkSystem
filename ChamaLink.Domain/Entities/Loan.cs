using System.ComponentModel.DataAnnotations.Schema;

namespace ChamaLink.Domain.Entities;

// NEW (Reports Engine gap: Loan Portfolio Report / Member Financial
// Profile loan section). Before this, a loan only existed as raw
// LoanDisbursement/LoanRepayment LedgerEntry rows against a member's
// aggregate AccountType.Loan account - there was no way to know how many
// distinct loans a member had, what each one's own interest rate or due
// date was, or which ones were overdue. This is the group's own record
// of a single loan, the same relationship WithdrawalApproval has to
// Withdrawal / EventContribution has to GroupEvent: the ledger rows are
// still written (so existing balance/statement views keep working), but
// this is what answers "whose loan is this, how much interest, when is
// it due, is it overdue".
public class Loan
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }

    public Guid GroupMemberId { get; set; }
    public GroupMember? GroupMember { get; set; }
    public Guid UserId { get; set; }

    public decimal PrincipalAmount { get; set; }

    // Snapshot of the interest rate actually applied to this loan (either
    // the group's own GroupSettings.LoanInterestRate at disbursement time,
    // or an explicit override) - kept on the loan itself so a later
    // change to the group's rate never rewrites what this member actually
    // agreed to.
    public decimal InterestRate { get; set; }
    public decimal InterestAmount { get; set; }

    // What has actually been paid back so far, across one or more
    // repayments (FIFO against TotalPayable, same shape as
    // Fine/Debt clearing elsewhere in the app).
    public decimal AmountRepaid { get; set; } = 0m;

    // Principal + Interest = the full amount this loan expects back.
    [NotMapped]
    public decimal TotalPayable => PrincipalAmount + InterestAmount;

    [NotMapped]
    public decimal OutstandingBalance => TotalPayable - AmountRepaid;

    public string? Purpose { get; set; }

    public DateTime DisbursedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; }
    public DateTime? RepaidAt { get; set; }

    // Active covers both "on time" and "overdue" - IsOverdue below is
    // what actually distinguishes them, computed rather than stored, so
    // it is always correct as of "now" without needing a background job
    // to keep flipping a stored status. Defaulted is a deliberate leader
    // decision (see LoanService.MarkDefaultedAsync), not automatic - a
    // group's own committee decides when a loan is genuinely written off
    // as unrecoverable, the same way Fine/Debt can be Waived by a person,
    // not a timer.
    public LoanStatus Status { get; set; } = LoanStatus.Active;

    [NotMapped]
    public bool IsOverdue => Status == LoanStatus.Active && DueDate < DateTime.UtcNow;

    public Guid IssuedByGroupMemberId { get; set; }
    public GroupMember? IssuedByGroupMember { get; set; }

    // NEW (Loan Engine V2, item 3/3: LatePenaltyAmount wiring). Guards
    // against LoanPenaltyBackgroundService charging the same loan more
    // than once - LatePenaltyAmount is a single flat fee for becoming
    // overdue (like MarkDefaultedAsync, a one-time state change), not a
    // per-day rate, so once it has fired for this loan it must never
    // fire again for as long as the loan stays overdue. Cleared back to
    // false only if the loan is ever reactivated (it is not today), so
    // in practice it is a one-way flag set exactly once per loan.
    public bool LatePenaltyCharged { get; set; } = false;

    public Group? Group { get; set; }
}

public enum LoanStatus
{
    Active = 1,
    Repaid = 2,
    Defaulted = 3
}
