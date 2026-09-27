namespace ChamaLink.Application.DTOs;

// Sprint 1 gaps: Collection Rate Engine, Defaulter Engine, Group Balance
// Engine, Group Financial Summary Endpoint. Served by AnalyticsService.

public record CollectionRateDto(
    Guid GroupId,
    DateTime Period,
    decimal ExpectedAmount,
    decimal CollectedAmount,
    decimal CollectionRatePercent
);

// BUG FIX (Defaulters Report gap): a leader could see a member owed
// money but not how long they'd been behind - "Peter owes 50k" reads very
// differently from "Peter owes 50k and has for 60 days". DaysLate is the
// age of that member's oldest still-Outstanding Debt (in days), 0 when
// they have none.
public record DefaulterDto(
    Guid UserId,
    string MemberName,
    string PhoneNumber,
    decimal OutstandingDebt,
    decimal OutstandingFine,
    DateTime? LastContributionDate,
    int DaysLate
);

// BUG FIX (Group Balance business-logic gap): a single "CurrentGroupBalance"
// number conflated two different questions - "how much cash does the
// group actually have in hand right now" vs "how much is the group
// worth overall including money out on loan / still owed". For a
// lending chama, an outstanding loan is still the group's asset (it will
// come back with interest), not a loss - so it must not just vanish from
// what the group is worth, but it also is NOT cash sitting in the bank/
// M-Pesa/cash box today. This DTO now reports both views plus the total,
// matching how SACCOs/VICOBA/ROSCA groups actually think about their
// books: Cash Available, what's Outstanding (an asset, not yet cash),
// and Total Assets (the group's real net worth).
public record GroupBalanceDto(
    Guid GroupId,
    decimal TotalContributions,
    decimal CashAvailable,
    decimal OutstandingLoans,
    decimal OutstandingFines,
    decimal OutstandingDebts,
    decimal TotalWelfareHeld,
    decimal TotalWithdrawalsPaid,
    // FIX (A4 - ledger/cash consistency): money that physically left the
    // group's mobile wallet per imported M-Koba statements, but has not
    // yet been promoted into a formal Withdrawal record. These reduce
    // Cash Available immediately - reconciliation only categorises what
    // the payout was for (loan/expense), it does not decide whether cash
    // left. Previously these were invisible to the balance calc, so the
    // group could look ~52% richer than reality and over-lend.
    decimal TotalWalletWithdrawals,
    decimal TotalAssets
);

public record GroupFinancialSummaryDto(
    Guid GroupId,
    string GroupName,
    int TotalMembers,
    int ActiveMembers,
    decimal CashAvailable,
    decimal OutstandingLoans,
    decimal OutstandingFines,
    decimal OutstandingDebts,
    decimal TotalAssets,
    decimal CollectionRatePercent,
    int OpenEventsCount,
    int PendingWithdrawalsCount
);
