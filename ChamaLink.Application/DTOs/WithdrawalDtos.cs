using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Application.DTOs;

// Withdrawal Governance gap: these DTOs now carry approval-progress info
// (how many approvals are needed, how many have been given so far, and
// who has voted) so a client can show "1/2 approvals" instead of just a
// flat Pending/Approved/Rejected status.

// SECURITY FIX (audit 1.6 / IDOR): RecordedByGroupMemberId and
// DecidedByGroupMemberId used to be sent by the client, so anyone with a
// valid token could claim to be a different member (e.g. impersonate the
// Treasurer) simply by sending that member's ID. The actor is now always
// derived server-side from the caller's JWT (see WithdrawalController),
// never accepted as a request field.

public record CreateWithdrawalDto(
    Guid GroupId,

    [Range(0.01, double.MaxValue, ErrorMessage = "Kiasi cha kutoa lazima kiwe zaidi ya sifuri.")]
    decimal Amount,

    [Required(ErrorMessage = "Sababu ya kutoa fedha inahitajika.")]
    [StringLength(500)]
    string Purpose,

    [Required(ErrorMessage = "Jina la mpokeaji linahitajika.")]
    [StringLength(200)]
    string BeneficiaryName,

    string? ReferenceNo,
    string? Notes,
    // NEW: set when the beneficiary is an actual group member, so this
    // withdrawal counts toward their Member Financial Profile "Benefits
    // Received". Leave null when the money went to someone outside the
    // group (BeneficiaryName alone is used then).
    Guid? BeneficiaryGroupMemberId = null,
    // NEW: optionally ties this payout to the event that raised the
    // money for it (e.g. a msiba event). If set and
    // BeneficiaryGroupMemberId is not explicitly given, the event's own
    // beneficiary is used automatically.
    Guid? GroupEventId = null
);

// approve = true records an approval vote, false records a rejection/veto.
public record DecideWithdrawalDto(
    bool Approve,
    string? Reason
);

public record WithdrawalApprovalDto(
    Guid GroupMemberId,
    string Role,
    bool Approved,
    string? Reason,
    DateTime DecidedAt
);

public record WithdrawalResponseDto(
    Guid Id,
    Guid GroupId,
    decimal Amount,
    string Purpose,
    string BeneficiaryName,
    Guid? BeneficiaryGroupMemberId,
    Guid? GroupEventId,
    string Status,
    Guid? ApprovedByGroupMemberId,
    Guid RecordedByGroupMemberId,
    string? ReferenceNo,
    string? Notes,
    string? RejectionReason,
    DateTime Date,
    DateTime? DecisionAt,
    int RequiredApprovals,
    int ApprovalsReceived,
    List<string> AllowedApproverRoles,
    List<WithdrawalApprovalDto> Decisions
);

// Lets a "new withdrawal" screen show the group's current governance
// rule up front, before anyone has voted on anything.
public record WithdrawalApprovalRuleDto(
    List<string> AllowedRoles,
    int RequiredApprovals
);
