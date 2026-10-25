using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Domain.Entities;

/// <summary>Chanzo cha risiti ya malipo.</summary>
public enum PaymentReceiptSource
{
    /// <summary>Mstari wa statement ya M-Koba (PDF iliyoidhinishwa).</summary>
    Statement = 1,
    /// <summary>Madai ya mwanachama ("nmelipa") — hayathibitishi kitu peke yake.</summary>
    Claim = 2
}

/// <summary>
/// Hali ya risiti. Kanuni: pesa HAIMLIPI mtu mpaka hali iwe <see cref="Confirmed"/>
/// (na kwa kiasi kikubwa, mpaka kiongozi wa pili aidhinishe).
/// </summary>
/// <summary>Aina ya risiti: mchango wa tukio, ada ya kujiunga, au nyingine.</summary>
public enum PaymentKind
{
    Contribution = 1,
    /// <summary>Ada ya kujiunga — pesa inakuja KABLA mtu kuwa mwanachama.</summary>
    Joining = 2,
    Other = 3
}

public enum PaymentReceiptStatus
{
    /// <summary>Pesa imeonekana lakini haijulikani ni ya nani.</summary>
    Unmatched = 1,
    /// <summary>Mfumo unapendekeza mwanachama (kwa kodi au kwa namba + kiasi) — inasubiri mtu.</summary>
    Suggested = 2,
    /// <summary>Imethibitishwa na kiongozi (au viongozi wawili kwa kiasi kikubwa). Pesa inaingia kwa mwanachama.</summary>
    Confirmed = 3,
    /// <summary>Imekataliwa — imebaki kwenye sanduku kwa rekodi.</summary>
    Rejected = 4,
    /// <summary>Imekwisha kutumika kabla ya toleo hili (namba inajulikana) — rekodi tu.</summary>
    AutoMatched = 5
}

/// <summary>
/// SASISHO21 — SANDUKU LA MALIPO.
///
/// Risiti SI pesa: ni MSTARI WA KAZI unaoelekeza kwenye mstari mmoja wa
/// statement (MkobaRawTransaction). Pesa yenyewe inaishi sehemu moja tu —
/// kwenye Injini ya Ukweli. Risiti inasema tu: "mstari huu wa statement
/// unamilikiwa na mwanachama X, kwa sababu Y, aliyethibitisha Z".
///
/// Kwa hiyo hakuna "ledger ya pili": kuthibitisha risiti kunabadilisha
/// Mmiliki (AttributedGroupMemberId) wa mstari ule ule wa statement.
/// </summary>
public class PaymentReceipt
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public PaymentReceiptSource Source { get; set; } = PaymentReceiptSource.Statement;

    /// <summary>Mstari wa statement unaohusika (Statement source pekee).</summary>
    public Guid? MkobaTransactionId { get; set; }
    /// <summary>Namba ya muamala kwenye statement / kwenye SMS ya mwanachama (kwa kupinga mara mbili).</summary>
    [MaxLength(80)] public string? Reference { get; set; }

    public string? Phone { get; set; }
    public string? PayerName { get; set; }
    public decimal Amount { get; set; }
    public DateTime OccurredAt { get; set; }
    [MaxLength(400)] public string? Description { get; set; }

    /// <summary>Kodi ya malipo iliyoonekana kwenye maelezo (mf. "J3-E07-0147"), kama ipo.</summary>
    [MaxLength(20)] public string? PaymentCodeSeen { get; set; }

    public PaymentReceiptStatus Status { get; set; } = PaymentReceiptStatus.Unmatched;

    /// <summary>Mwanachama anayependekezwa na mfumo.</summary>
    public Guid? SuggestedGroupMemberId { get; set; }
    /// <summary>Kanuni iliyotumika: Code | PhoneExact | PhonePartial | Claim | None.</summary>
    [MaxLength(20)] public string? MatchRule { get; set; }
    /// <summary>Wajibu unaolengwa (kama kodi ilielekeza kwenye tukio mahususi).</summary>
    public Guid? SuggestedObligationId { get; set; }

    /// <summary>Mwanachama aliyethibitishwa (baada ya kuthibitisha).</summary>
    public Guid? ConfirmedGroupMemberId { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>Kiongozi wa pili kwa kiasi kikubwa (4-eyes).</summary>
    public Guid? SecondApproverUserId { get; set; }
    public DateTime? SecondApprovedAt { get; set; }
    [MaxLength(500)] public string? Note { get; set; }

    /// <summary>Kwa madai: mwanachama aliyeomba na maelezo yake.</summary>
    public Guid? ClaimedByGroupMemberId { get; set; }

    // ---- SASISHO21 (Sehemu ya 2): malipo ya kujiunga yanakuja KABLA ya uanachama ----
    public PaymentKind Kind { get; set; } = PaymentKind.Contribution;
    /// <summary>Ombi la kujiunga ambalo pesa hii inalipia (Kind = Joining pekee).</summary>
    public Guid? MembershipRequestId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// SASISHO21 — "NIMELIPA": mwanachama anadai amelipa, akiweka namba ya muamala.
///
/// Dai HALIBADILISHI deni. Linaingia kwenye sanduku la hazina kama kitu
/// "kinachohitaji uchunguzi" hadi treasurer alinganishe na statement
/// na kiongozi wa pili (kama sera inasema) athibitishe.
/// </summary>
public class PaymentClaim
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(80)] public string Reference { get; set; } = string.Empty;
    /// <summary>Kodi ya malipo (kama mwanachama aliandika) au ya wajibu anaodai analipa.</summary>
    [MaxLength(20)] public string? PaymentCode { get; set; }
    [MaxLength(500)] public string? Note { get; set; }

    public PaymentClaimStatus Status { get; set; } = PaymentClaimStatus.Pending;
    public Guid? ReceiptId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    [MaxLength(500)] public string? DecisionNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum PaymentClaimStatus
{
    /// <summary>Inasubiri uchunguzi — si deni, si malipo.</summary>
    Pending = 1,
    /// <summary>Imeunganishwa na mstari wa statement (au imethibitishwa na viongozi wawili).</summary>
    Verified = 2,
    Rejected = 3,
    /// <summary>Mwanachama ameifuta mwenyewe kabla ya uamuzi.</summary>
    Withdrawn = 4
}
