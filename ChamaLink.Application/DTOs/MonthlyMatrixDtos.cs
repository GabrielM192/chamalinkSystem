namespace ChamaLink.Application.DTOs;

/// <summary>One month cell for one member in monthly matrix.</summary>
public class MatrixMonthCellDto
{
    public int Month { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Contribution { get; set; }
    public decimal FinePaid { get; set; }
    public decimal LoanRepayment { get; set; } // Added 2026-09-19 Awamu 1
    public bool Missed { get; set; }
    public bool HadFine { get; set; }
    public decimal ToJoiningFee { get; set; }
    public decimal ToDebt { get; set; }
    public decimal ToSavings { get; set; }
}

/// <summary>One row: one member with all months.</summary>
public class MatrixMemberRowDto
{
    public Guid GroupMemberId { get; set; }
    public Guid UserId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsNonActive { get; set; }
    public List<MatrixMonthCellDto> Months { get; set; } = new();
    public decimal JoiningFeePaid { get; set; }
    public decimal JoiningFeeTarget { get; set; }
    public decimal JoiningFeeDebt { get; set; }
    public decimal EventTotal { get; set; }
    public decimal GrandTotal { get; set; }
}

/// <summary>Full monthly matrix report.</summary>
public class MonthlyMatrixDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int Year { get; set; }
    public List<int> Months { get; set; } = new();
    public List<MatrixMemberRowDto> Members { get; set; } = new();
    public List<decimal> MonthTotals { get; set; } = new();
    public decimal TotalJoiningFees { get; set; }
    public decimal TotalEvents { get; set; }
    public decimal TotalLoanRepayments { get; set; } // Added Awamu 1
    public decimal CurrentSavings { get; set; }
    public decimal GrandTotal { get; set; }
}
