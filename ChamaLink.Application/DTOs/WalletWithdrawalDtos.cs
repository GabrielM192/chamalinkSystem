using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Application.DTOs;

public record WalletWithdrawalDto(
    Guid Id,
    Guid GroupId,
    string ReferenceNo,
    string MemberName,
    string PhoneNumber,
    decimal Amount,
    DateTime TransactionDate,
    bool IsReconciled,
    string? ReconciliationNote
);

// Sent by the treasurer once they know what a withdrawal was for.
public record ReconcileWithdrawalDto(
    [Required(ErrorMessage = "Maelezo ya reconciliation yanahitajika.")]
    [StringLength(500)]
    string Note
);
