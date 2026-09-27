using System.ComponentModel.DataAnnotations;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain;

namespace ChamaLink.Application.DTOs;

// BUG FIX: this used to have no Type field at all, so GroupService never
// had anything to assign and every group silently defaulted to
// MonthlySavings, even when the leader intended an EventBased/Hybrid
// group. Now the caller must say which kind of group this is.
//
// NEW (ChamaLink v1 scope): GracePeriodDays, JoiningFee and
// MinimumReserveBalance existed as GroupSettings columns but nothing
// ever let a leader actually set them at creation time - they silently
// stayed at their C# default forever, the same bug pattern as the old
// MonthlyContributionAmount/LatePenaltyAmount duplicate-field issue.
// WithdrawalApprovalMode/CustomApprovalRoles/CustomRequiredApprovals are
// the Withdrawal Governance settings - optional, default to TreasurerOnly.
//
// SECURITY FIX (audit 2.1/2.2: "DTOs hazina validation rules" /
// "Negative amounts zinaweza kupenya"): every money/day field below used
// to accept negative or zero values with nothing stopping it (e.g. a
// negative LateFine would silently pay members instead of fining them).
public record CreateGroupDto(
    [Required(ErrorMessage = "Jina la kikundi linahitajika.")]
    [StringLength(200, MinimumLength = 2)]
    string Name,

    [StringLength(1000)]
    string Description,

    GroupType Type,

    [Range(0.01, double.MaxValue, ErrorMessage = "Mchango wa kila mwezi lazima uwe zaidi ya sifuri.")]
    decimal MonthlyContribution,

    [Range(0, double.MaxValue, ErrorMessage = "Faini haiwezi kuwa hasi.")]
    decimal LateFine,

    [Range(0, 100, ErrorMessage = "Riba lazima iwe kati ya 0% na 100%.")]
    decimal LoanInterestRate,

    [Range(0, 90, ErrorMessage = "Siku za neema lazima ziwe kati ya 0 na 90.")]
    // Hakuna default: siku za neema ni kanuni ya kikundi (Ukonga = 0). Ikikosekana, ombi linakataliwa.
    int? GracePeriodDays = null,

    [Range(0, double.MaxValue, ErrorMessage = "Ada ya kujiunga haiwezi kuwa hasi.")]
    decimal JoiningFee = 0m,

    [Range(0, double.MaxValue, ErrorMessage = "Salio la chini la akiba haliwezi kuwa hasi.")]
    decimal MinimumReserveBalance = 0m,

    ApprovalMode WithdrawalApprovalMode = ApprovalMode.TreasurerOnly,
    string? CustomApprovalRoles = null,

    [Range(1, int.MaxValue, ErrorMessage = "Idadi ya approvals lazima iwe angalau 1.")]
    int? CustomRequiredApprovals = null,

    // NEW (Fine threshold / "grace amount" gap): shortfall below this
    // still becomes a Debt but does not trigger a Fine. Defaults to 0
    // (no tolerance), same as the old behaviour.
    [Range(0, double.MaxValue, ErrorMessage = "Kiwango cha msamaha wa faini hakiwezi kuwa hasi.")]
    decimal MinimumShortfallForFine = 0m,

    // NEW: lets the group decide whether an event's WelfareDeduction
    // subtracts from a member's own welfare balance, or adds to it as a
    // mandatory contribution. See WelfareMode in Enums.cs.
    WelfareMode WelfareMode = WelfareMode.DeductBalance
);

public record AddMemberDto(
    Guid UserId,

    [Required(ErrorMessage = "Namba ya mwanachama inahitajika.")]
    [StringLength(50)]
    string MemberNumber,

    [Required(ErrorMessage = "Role inahitajika.")]
    string Role // Admin, Treasurer, Secretary, Member
);

// NEW: there was previously no way to change a group's settings after
// creation at all - every GroupSettings field (contribution amount,
// grace period, joining fee, minimum reserve balance, withdrawal
// approval rule) was permanently frozen at whatever CreateGroupAsync
// happened to set. All fields are optional so a caller can update just
// the ones that changed (e.g. only the withdrawal approval rule)
// without having to resend the whole settings object.
public record UpdateGroupSettingsDto(
    [Range(0.01, double.MaxValue, ErrorMessage = "Mchango wa kila mwezi lazima uwe zaidi ya sifuri.")]
    decimal? MonthlyContribution = null,

    [Range(0, double.MaxValue, ErrorMessage = "Faini haiwezi kuwa hasi.")]
    decimal? LateFine = null,

    [Range(0, 100, ErrorMessage = "Riba lazima iwe kati ya 0% na 100%.")]
    decimal? LoanInterestRate = null,

    [Range(1, 28, ErrorMessage = "Siku ya malipo lazima iwe kati ya 1 na 28.")]
    int? DueDateDay = null,

    [Range(0, 90, ErrorMessage = "Siku za neema lazima ziwe kati ya 0 na 90.")]
    int? GracePeriodDays = null,

    [Range(0, double.MaxValue, ErrorMessage = "Ada ya kujiunga haiwezi kuwa hasi.")]
    decimal? JoiningFee = null,

    [Range(0, double.MaxValue, ErrorMessage = "Salio la chini la akiba haliwezi kuwa hasi.")]
    decimal? MinimumReserveBalance = null,

    ApprovalMode? WithdrawalApprovalMode = null,
    string? CustomApprovalRoles = null,

    [Range(1, int.MaxValue, ErrorMessage = "Idadi ya approvals lazima iwe angalau 1.")]
    int? CustomRequiredApprovals = null,

    [Range(0, double.MaxValue, ErrorMessage = "Kiwango cha msamaha wa faini hakiwezi kuwa hasi.")]
    decimal? MinimumShortfallForFine = null,

    WelfareMode? WelfareMode = null,

    // NEW (Ukonga Rules Specification v1.2, sehemu 3/4b - ARCH-001).
    [Range(1, 24, ErrorMessage = "Idadi ya miezi mfululizo lazima iwe kati ya 1 na 24.")]
    int? MaxConsecutiveMissedMonths = null,

    // Validated/parsed as ChamaLink.Domain.DebtAllocationStrategy in
    // GroupService.UpdateSettingsAsync (CurrentMonthFirst / OldestDebtFirst
    // / ManualAllocation) - kept as a plain string here, like every other
    // enum this DTO already sends back over the API, rather than binding
    // straight to the enum type (this project has no global
    // JsonStringEnumConverter, so an enum-typed request property would
    // expect the numeric value, not the name).
    string? DebtAllocationStrategy = null
);

public record GroupSettingsResponseDto(
    Guid GroupId,
    decimal MonthlyContribution,
    decimal LateFine,
    decimal LoanInterestRate,
    int DueDateDay,
    int GracePeriodDays,
    decimal JoiningFee,
    decimal MinimumReserveBalance,
    string WithdrawalApprovalMode,
    string? CustomApprovalRoles,
    int? CustomRequiredApprovals,
    decimal MinimumShortfallForFine,
    string WelfareMode,
    // NEW (Ukonga Rules Specification v1.2, sehemu 3/4b - ARCH-001):
    // these existed in GroupSettings since Phase 1 but were never exposed
    // via the API, so a Chairperson had no way to see or change them
    // without going into the database directly.
    int MaxConsecutiveMissedMonths,
    string DebtAllocationStrategy
);

public record GroupResponseDto(
    Guid Id,
    string Name,
    string Description,
    string Code,
    DateTime CreatedAt
);

// NEW (frontend foundation gap): after logging in, a user has no way to
// discover which group(s) they belong to and in what role - there was
// no endpoint for it anywhere in the API. This is exactly what a
// post-login "choose your group" screen needs, and nothing more (no
// financial figures here - Member Financial Profile already owns that).
public record MyGroupSummaryDto(
    Guid GroupId,
    string GroupName,
    string GroupCode,
    string Role,
    string GroupType,
    string MemberStatus
);