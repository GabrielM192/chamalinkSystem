using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Application.DTOs;

// BUG FIX: this used to have no TargetAmountPerMember at all, so
// GroupEvent.TargetAmountPerMember always stayed 0 and the M-Koba import
// waterfall's "Event Contribution" step could never find any pending
// amount to allocate money to. Also adds beneficiary tracking (Sprint 2
// gap) and an optional free-text Description.
//
// SECURITY FIX (audit 7.1: "EventService haina input validation ya
// kutosha"): AmountDeducted/TargetAmountPerMember/DeadlineDays used to
// accept negative or zero values with nothing stopping it.
public record TriggerEventDto(
    Guid GroupId,

    [Required(ErrorMessage = "Kichwa cha tukio kinahitajika.")]
    [StringLength(200)]
    string Title,

    [StringLength(1000)]
    string? Description,

    [Range(0.01, double.MaxValue, ErrorMessage = "Kiasi cha kukata lazima kiwe zaidi ya sifuri.")]
    decimal AmountDeducted,

    [Range(0, double.MaxValue, ErrorMessage = "Lengo la mchango haliwezi kuwa hasi.")]
    decimal TargetAmountPerMember,

    Guid? BeneficiaryGroupMemberId,
    string? BeneficiaryName,

    [Range(1, 365, ErrorMessage = "Siku za mwisho lazima ziwe kati ya 1 na 365.")]
    int DeadlineDays = 7
);

public record EventResponseDto(
    Guid EventId,
    Guid GroupId,
    string Title,
    decimal AmountDeducted,
    decimal TargetAmountPerMember,
    DateTime EventDate,
    DateTime DeadlineDate,
    int MembersAffectedCount
);