namespace ChamaLink.Domain;

// Account types for members
public enum AccountType
{
    Savings = 1,
    Loan = 2,
    Fine = 3,
    SocialFund = 4
}

// Ledger transaction types — Added Savings (was missing, caused all savings to be stored as Contribution)
public enum TransactionType
{
    Contribution = 1,
    LoanDisbursement = 2,
    LoanRepayment = 3,
    FineIssue = 4,
    FinePayment = 5,
    ShareOut = 6,
    WelfareDeduction = 7,
    WelfareTopUp = 8,
    EventContribution = 9,
    JoiningFee = 10,
    Savings = 11
}

// Member role in group
public enum GroupRole
{
    Chairperson = 1,
    Treasurer = 2,
    Secretary = 3,
    Member = 4,
    MemberRepresentative = 5
}

// Organization type - what kind of group
public enum OrganizationType
{
    Mkoba = 1,
    Vicoba = 2,
    Saccos = 3,
    SavingsClub = 4,
    Other = 5
}

// Welfare handling mode
public enum WelfareMode
{
    DeductBalance = 1,
    ContributePot = 2
}

// Debt type
public enum DebtType
{
    MissedContribution = 1,
    JoiningFee = 2,
    Other = 3
}

public enum FineStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Waived = 4
}

public enum LoanInterestType
{
    Flat = 1,
    Reducing = 2
}

public enum FineReasonType
{
    LateMonthlyContribution = 1,
    WelfareBalanceBelowMinimum = 2,
    Other = 3,
    LoanOverdue = 4
}

public enum DebtStatus
{
    Outstanding = 1,
    Cleared = 2,
    Waived = 3
}

// How new payments apply to old debts
public enum DebtAllocationStrategy
{
    CurrentMonthFirst = 1,
    OldestDebtFirst = 2,
    ManualAllocation = 3
}

// Shortfall handling strategy — V2: FineImmediately added per Business Rule Engine spec
// Keep existing numbers for backward compat (data already stored as int)
public enum ShortfallStrategy
{
    FineImmediately = 0,
    CreateDebt = 1,
    UseSavingsThenDebt = 2,
    UseSavingsThenFine = 3,
    DebtOnly = 4
}

// Compliance evaluation strategy
public enum ComplianceStrategy
{
    MonthlyTarget = 1,
    CumulativeTarget = 2,
    Custom = 3
}

// Loan approval strategy
public enum LoanStrategy
{
    DirectIssue = 1,
    ApplicationApproval = 2,
    GuarantorRequired = 3
}

public enum WithdrawalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4,
    PartiallyApproved = 5
}

public enum ApprovalMode
{
    TreasurerOnly = 1,
    ChairpersonOnly = 2,
    TreasurerAndChairperson = 3,
    TreasurerAndSecretary = 4,
    CustomApproval = 5
}

public enum EventContributionStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3
}

public enum MemberStatus
{
    New = 1,
    Active = 2,
    Warning = 3,
    EligibleForExpulsion = 4,
    Inactive = 5,
    Suspended = 6,
    Exited = 7,
    Deceased = 8,
    Archived = 9
}

public enum NotificationType
{
    ContributionDue = 1,
    FineIssued = 2,
    EventCreated = 3,
    MemberOverdue = 4,
    WithdrawalPendingApproval = 5,
    WithdrawalDecided = 6,
    LoanIssued = 7,
    General = 8
}
