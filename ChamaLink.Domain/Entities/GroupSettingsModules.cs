namespace ChamaLink.Domain.Entities;

// Product configuration layer - each module governs one business area
// Stored as owned types on GroupSettings table

public enum JoinFeeMode
{
    Optional = 1,
    ParallelWithContributions = 2,
    MustCompleteBeforeContributing = 3
}

public enum JoinFeeCaptureMode
{
    AutomaticOnly = 1,
    AutomaticAndManual = 2,
    ManualOnly = 3
}

public class JoinFeeSettings
{
    public decimal RequiredAmount { get; set; } // hakuna default: kiasi kinawekwa na viongozi
    public JoinFeeMode Mode { get; set; } = JoinFeeMode.ParallelWithContributions;
    public JoinFeeCaptureMode CaptureMode { get; set; } = JoinFeeCaptureMode.AutomaticAndManual;
    public decimal? ApprovalThreshold { get; set; } // e.g., manual entries above this need approval
}

public class FinancialSettings
{
    public decimal JoiningFee { get; set; } = 0m;
    public decimal MinimumReserveBalance { get; set; } = 0m;
    public JoinFeeSettings JoinFeeConfig { get; set; } = new(); //
}

public class ContributionSettings
{
    public decimal MonthlyContribution { get; set; } = 0m;
    public int DueDateDay { get; set; } // hakuna default: thamani inatoka kwa viongozi
    public int GracePeriodDays { get; set; } // hakuna default: thamani inatoka kwa viongozi
    public decimal LateFine { get; set; } = 0m;
    public decimal MinimumShortfallForFine { get; set; } = 0m;
    public int MaxConsecutiveMissedMonths { get; set; } // hakuna default: thamani inatoka kwa viongozi
    public DebtAllocationStrategy DebtAllocationStrategy { get; set; } = DebtAllocationStrategy.OldestDebtFirst; // Was CurrentMonthFirst, now OldestDebtFirst for
    public ShortfallStrategy ShortfallStrategy { get; set; } = ShortfallStrategy.CreateDebt;
    public ComplianceStrategy ComplianceStrategy { get; set; } = ComplianceStrategy.MonthlyTarget;
}

public class EventSettings
{
    public bool EventsEnabled { get; set; } = true;
    public WelfareMode WelfareMode { get; set; } = WelfareMode.DeductBalance;
    public decimal WelfareFineAmount { get; set; } // hakuna default: 0 = hakuna faini ya ustawi
}

public class LoanSettings
{
    public bool Enabled { get; set; } = true;
    public decimal InterestRate { get; set; } = 0m;
    public LoanInterestType InterestType { get; set; } = LoanInterestType.Flat;
    public int RepaymentDays { get; set; } // hakuna default: thamani inatoka kwa viongozi
    public decimal LatePenaltyAmount { get; set; } = 0m;
    public decimal? MaxLoanMultiplier { get; set; }
    public LoanStrategy LoanStrategy { get; set; } = LoanStrategy.DirectIssue;
    public bool GuarantorRequired { get; set; } = false;
    public int MinGuarantors { get; set; } = 0;
}

public class ApprovalPolicy
{
    public ApprovalMode Mode { get; set; } = ApprovalMode.TreasurerOnly;
    public string? CustomRoles { get; set; }
    public int? CustomRequiredApprovals { get; set; }
}

public class GovernanceSettings
{
    public ApprovalPolicy WithdrawalApproval { get; set; } = new();
    public ApprovalPolicy LoanApproval { get; set; } = new()
    {
        Mode = ApprovalMode.CustomApproval,
        CustomRoles = "Treasurer,Chairperson",
        CustomRequiredApprovals = 1
    };
    public ApprovalPolicy EventApproval { get; set; } = new()
    {
        Mode = ApprovalMode.CustomApproval,
        CustomRoles = "Treasurer,Chairperson",
        CustomRequiredApprovals = 1
    };
    public ApprovalPolicy ImportApproval { get; set; } = new()
    {
        Mode = ApprovalMode.CustomApproval,
        CustomRoles = "Treasurer,Chairperson",
        CustomRequiredApprovals = 1
    };
}
