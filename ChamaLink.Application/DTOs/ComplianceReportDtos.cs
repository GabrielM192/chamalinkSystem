namespace ChamaLink.Application.DTOs;

// Ukonga Rules Specification v1.2, sehemu 5/7/8 (Phase 5 - Reports). One
// row per member, from their LATEST ComplianceSnapshot (Phase 4). This is
// exactly the table shape sehemu 7 shows: Member | Missed(Total) |
// Consecutive | Fines Owed | Contribution Debt | Status. "Defaulters",
// "Warnings" and "NonActive list" (sehemu 8) are all just filtered views
// over this same table (by Status or by ConsecutiveMissedMonths), not
// three separate pieces of business logic computing three separate
// answers - the frontend filters what it needs from one call.
public record ComplianceSummaryRowDto(
    Guid GroupMemberId,
    Guid UserId,
    string MemberName,
    string PhoneNumber,
    DateTime SnapshotMonth,
    int TotalMissedMonths,
    int ConsecutiveMissedMonths,
    decimal OutstandingFineAmount,
    decimal OutstandingContributionDebt,
    string Status
);

// One month of a member's compliance history (sehemu 6/8: "Compliance
// Trends - mwenendo wa mwezi kwa mwezi").
public record ComplianceTrendPointDto(
    DateTime Month,
    decimal ExpectedContribution,
    decimal PaidContribution,
    decimal FineIssuedAmount,
    decimal FinePaidAmount,
    decimal OutstandingFineAmount,
    int TotalMissedMonths,
    int ConsecutiveMissedMonths,
    decimal OutstandingContributionDebt,
    string Status
);

// A single member's full trend, oldest month first.
public record ComplianceTrendDto(
    Guid GroupMemberId,
    string MemberName,
    List<ComplianceTrendPointDto> Trend
);
