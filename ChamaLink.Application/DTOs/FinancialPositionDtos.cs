namespace ChamaLink.Application.DTOs;

public class MemberFinancialPositionDto
{
    public Guid MemberId { get; set; }
    public string MembershipNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime AsOf { get; set; }

    // Savings — with Held vs Available
    public decimal SavingsBalance { get; set; }
    public decimal HeldSavings { get; set; }
    public decimal AvailableSavings { get; set; }

    // Contribution
    public decimal TotalExpectedContributions { get; set; }
    public decimal TotalPaidContributions { get; set; }
    public decimal ContributionDebt { get; set; }
    public decimal ContributionCompliancePercent { get; set; }

    // Joining Fee
    public decimal JoiningFeeTarget { get; set; }
    public decimal JoiningFeePaid { get; set; }
    public decimal JoiningFeeBalance { get; set; }

    // Loan
    public decimal TotalLoansIssued { get; set; }
    public decimal TotalLoansRepaid { get; set; }
    public decimal OutstandingLoan { get; set; }
    public decimal OutstandingLoanInterest { get; set; }
    public DateTime? NextLoanDueDate { get; set; }
    public int? DaysPastDue { get; set; }
    public string LoanRiskStatus { get; set; } = "Normal";

    // Fine
    public decimal TotalFinesCharged { get; set; }
    public decimal TotalFinesPaid { get; set; }
    public decimal OutstandingFine { get; set; }

    // Welfare
    public decimal TotalWelfareObligations { get; set; }
    public decimal TotalWelfareContributions { get; set; }
    public decimal WelfareBalance { get; set; }
    public decimal TotalWelfareBenefitsReceived { get; set; }

    // Net
    public decimal NetPosition { get; set; }

    public DateTime CalculatedAt { get; set; }
}

public class AllocationResultDto
{
    public decimal PaymentReceived { get; set; }
    public List<AllocationItemDto> Allocations { get; set; } = new();
    public decimal Unallocated { get; set; }
    public string Status { get; set; } = "Paid";
    public decimal TargetForMonth { get; set; }
    public decimal PaidForMonth { get; set; }
    public decimal SavingsAdded { get; set; }
    public decimal DebtCleared { get; set; }
    public decimal FinePaid { get; set; }
    public decimal JoiningFeePaid { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
}

public class AllocationItemDto
{
    public string Target { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int PolicyVersion { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public Guid? LoanId { get; set; }
    public string? Note { get; set; }
}
