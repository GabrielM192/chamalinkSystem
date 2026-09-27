namespace ChamaLink.Application.DTOs;

public record GroupSummaryDto(
    Guid GroupId,
    decimal TotalSavings,
    decimal TotalLoansIssued,
    int TotalMembers
);

public record MemberStatementDto(
    Guid MemberId,
    decimal TotalSavings,
    decimal TotalLoansOutstanding,
    List<LedgerResponseDto> Transactions
);