using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Application.DTOs;

// NEW (Reports Engine gap: Loan Portfolio Report / Member Financial
// Profile loan section). See Loan.cs for why this exists separately from
// the raw LoanDisbursement/LoanRepayment ledger rows.

// SECURITY FIX (audit 1.5/1.6): IssuedByGroupMemberId used to be sent by
// the client, so anyone with a token could claim to be the Treasurer
// when issuing a loan. The actor is now always derived from the
// caller's JWT (see LoanController/LoanService), never accepted here.
//
// SECURITY FIX (audit 8.3: "Interest override haina bounds"): a negative
// or absurdly large InterestRateOverride used to be accepted as-is.
public record IssueLoanDto(
    Guid GroupMemberId,

    [Range(0.01, double.MaxValue, ErrorMessage = "Kiasi cha mkopo lazima kiwe zaidi ya sifuri.")]
    decimal PrincipalAmount,

    // If null, the group's own GroupSettings.LoanInterestRate is used.
    [Range(0, 100, ErrorMessage = "Riba lazima iwe kati ya 0% na 100%.")]
    decimal? InterestRateOverride,

    // If null, the group's own LoanSettings.RepaymentDays default fills
    // it in (see LoanService.IssueLoanAsync).
    DateTime? DueDate,
    string? Purpose
);

public record RecordLoanRepaymentDto(
    [Range(0.01, double.MaxValue, ErrorMessage = "Kiasi cha malipo lazima kiwe zaidi ya sifuri.")]
    decimal Amount,

    string? ReferenceNo
);

public record LoanResponseDto(
    Guid Id,
    Guid GroupId,
    Guid GroupMemberId,
    Guid UserId,
    string? MemberName,
    decimal PrincipalAmount,
    decimal InterestRate,
    decimal InterestAmount,
    decimal TotalPayable,
    decimal AmountRepaid,
    decimal OutstandingBalance,
    string? Purpose,
    DateTime DisbursedAt,
    DateTime DueDate,
    DateTime? RepaidAt,
    string Status,
    bool IsOverdue
);

// Sprint answer to "Loan Portfolio Report": issued/recovered/outstanding
// across the whole group, plus a breakdown by how each loan stands today.
public record LoanPortfolioDto(
    Guid GroupId,
    decimal TotalIssued,
    decimal TotalRecovered,
    decimal TotalOutstanding,
    int ActiveLoansCount,
    int OverdueLoansCount,
    int RepaidLoansCount,
    int DefaultedLoansCount,
    decimal OverdueAmount,
    decimal DefaultedAmount
);
