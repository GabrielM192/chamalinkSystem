namespace ChamaLink.Domain.Entities;

/// <summary>
/// SASISHO15 (AWAMU L): ombi la mkopo. Mtiririko pekee wa kuzaa mkopo:
/// Mwanachama anaomba → mfumo unakagua sifa (sera ya kikundi) → viongozi huru wanapiga kura →
/// Mtunza-hazina anaanzisha utoaji → mtu MWINGINE anathibitisha kwa rejea ya utoaji wa M-Koba → Loan inazaliwa.
/// Kiasi, riba na ratiba vinatoka kwenye sera (vimegandishwa kwenye ombi); hakuna anayeweza kuvibadilisha.
/// </summary>
public class LoanApplication
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public Guid BorrowerGroupMemberId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public decimal Amount { get; set; }
    public string? Purpose { get; set; }
    // Snapshot ya sera siku ya ombi
    public decimal InterestPercent { get; set; }
    public decimal InterestAmount { get; set; }
    public int RepaymentMonths { get; set; }
    public int RequiredApprovals { get; set; }
    public string? EligibilityJson { get; set; }
    public LoanApplicationStatus Status { get; set; } = LoanApplicationStatus.Voting;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ApprovalExpiresAt { get; set; }
    public Guid? DisbursementInitiatedByUserId { get; set; }
    public DateTime? DisbursementInitiatedAt { get; set; }
    public string? MkobaReference { get; set; }
    public DateTime? DisbursedAt { get; set; }
    public Guid? DisbursementConfirmedByUserId { get; set; }
    public DateTime? DisbursementConfirmedAt { get; set; }
    public string? RecipientNote { get; set; }
    public Guid? LoanId { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }

    public decimal TotalRepayable => Amount + InterestAmount;
}

public enum LoanApplicationStatus
{
    Voting = 1,
    Approved = 2,
    DisbursementInitiated = 3,
    Disbursed = 4,
    Rejected = 5,
    Cancelled = 6,
    Expired = 7
}

/// <summary>
/// SASISHO15: refresh token (HttpOnly cookie). Inahifadhiwa kama HASH tu; inazungushwa kila matumizi
/// (rotation) na matumizi ya pili ya token iliyokwisha tumika yanaua familia yote (kuibiwa kumegunduliwa).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: kamwe isiende kwa mteja
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public int TokenVersion { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: kamwe isiende kwa mteja
    public string? CreatedByIp { get; set; }
}
