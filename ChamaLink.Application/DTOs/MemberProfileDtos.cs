namespace ChamaLink.Application.DTOs;

// NEW (Reports Engine gap: Member Financial Profile). Previously this
// endpoint returned an untyped anonymous object with only Savings/Debt/
// Fine - no group/role context, no contribution compliance %, no real
// Loan section (there was no Loan entity yet), no event contribution
// history, and no Financial Score. This is the member's "financial
// passport" a leader actually opens to decide something (approve another
// loan? chase a debt? nothing to worry about?).
public record MemberFinancialProfileDto(
    Guid UserId,
    Guid GroupMemberId,
    string MemberName,
    string PhoneNumber,
    Guid GroupId,
    string GroupName,
    string Role,
    string Status,
    DateTime JoinedAt,

    decimal TotalContributions,
    decimal ExpectedContributions,
    decimal ContributionCompliancePercent,

    decimal OutstandingDebt,
    decimal OverdueDebt,

    // NEW (Member Financial Profile completion, "Financial Obligations"
    // section): sum of Debt.AmountCleared across every Debt this member
    // has ever had, regardless of current status - the counterpart to
    // OutstandingDebt above. Deliberately kept separate from
    // FinesPaid/TotalPenaltiesPaid below: a Debt is the missed
    // contribution principal itself (an obligation), not a punitive
    // fee, so clearing one is not "paying a penalty" - only a Fine is.
    decimal TotalDebtCleared,

    decimal TotalFinesIssued,
    decimal FinesPaid,
    decimal OutstandingFines,

    // NEW (Member Financial Profile completion, "Welfare" line): sum of
    // WelfareTopUp ledger entries on the member's own SocialFund
    // account - what they have actually paid INTO the welfare fund,
    // distinct from BenefitsReceived below (what they've been PAID OUT
    // of the group via Withdrawal).
    decimal WelfareContributions,

    decimal ActiveLoansOutstanding,
    int ActiveLoansCount,
    List<LoanResponseDto> Loans,

    // NEW (Member Financial Profile completion, "Loans" section): these
    // three summarize the Loans list above so a leader (or later, a
    // report) doesn't have to re-total it themselves every time.
    decimal TotalBorrowed,
    decimal TotalRepaid,
    int OverdueLoansCount,

    List<MemberEventContributionDto> Events,

    decimal BenefitsReceived,

    // NEW (Member Financial Profile completion, "Governance" section):
    // how many WithdrawalApproval decisions (approve or reject) this
    // member has personally cast as a leader. Attendance Score is
    // intentionally not included here yet - there is no attendance
    // data anywhere in the system to compute it from.
    int ApprovalsParticipated,

    // A/B/C/D - see AnalyticsService.ComputeFinancialScore for exactly
    // what each grade means. Meant as a quick at-a-glance signal for a
    // leader, not a credit-bureau-grade algorithm - the underlying
    // numbers above are what actually matters and are always shown
    // alongside it.
    string FinancialScore,

    // NEW (Member Financial Profile completion, "Loans" section, Phase
    // 1 of loan risk): deliberately a fact-based label, not a
    // High/Medium/Low judgement call - see
    // AnalyticsService.ComputeLoanRiskCategory. A numeric 0-100 risk
    // SCORE (weighing severity/amount/frequency, not just yes/no) is
    // planned as Phase 2, once Loan Portfolio/Reports Engine exist to
    // make use of it.
    //   GoodStanding        - never late, nothing overdue right now
    //   PreviousDelinquency - no current issue, but was late before
    //                         (LatePenaltyCharged on a past loan)
    //   CurrentOverdue      - has an Active loan overdue right now
    string LoanRiskCategory
);

public record MemberEventContributionDto(
    Guid GroupEventId,
    string EventTitle,
    decimal ExpectedAmount,
    decimal PaidAmount,
    string Status
);

// NEW (Reports Engine gap: Members Report). The one-screen table a
// Chairperson/Secretary opens to see every member's standing at once,
// instead of opening each Member Financial Profile one at a time.
public record MemberReportRowDto(
    Guid UserId,
    string MemberName,
    string Status,
    decimal SavingsBalance,
    decimal OutstandingDebt,
    decimal OutstandingFine,
    decimal OutstandingLoan
);
