namespace ChamaLink.Domain.Entities;

/// <summary>
/// SASISHO18 — wajibu wa mwanachama mmoja kwa tukio (kesi) moja.
///
/// HUU ni "kitabu cha deni" cha vikundi vinavyochangishana kila tukio.
/// Msingi wake ni kanuni moja:
///
///     RequiredAmount  = kiasi alichotakiwa kuchangia (snapshot ya sera siku ya tukio)
///     DeductedAmount  = kilichokatwa kwenye pochi (kinaweza kuwa kidogo — floor)
///     Outstanding     = RequiredAmount − DeductedAmount  (deni linalobaki)
///
/// Deni linakamilishwa (sweep) kila amana mpya inapoingia, kuanzia la zamani.
/// Hakuna kuandika mara mbili: mchango unakatwa MARA MOJA tu kwa kila kesi.
/// </summary>
public enum EventObligationStatus
{
    /// <summary>Haijakamilika — bado kuna Outstanding.</summary>
    Pending = 1,
    /// <summary>Imekatwa yote kwenye pochi (Outstanding = 0).</summary>
    Settled = 2,
    /// <summary>Viongozi wamesamehe deni (na sababu imeandikwa).</summary>
    Waived = 3,
    /// <summary>Mwanachama hakuhusika na tukio hili (mf. hakuwepo siku ya tukio).</summary>
    Exempted = 4
}

public class EventObligation
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }

    /// <summary>Kesi ya mafao inayowakilisha tukio (BenefitCase.Id).</summary>
    public Guid BenefitCaseId { get; set; }

    public Guid GroupMemberId { get; set; }

    /// <summary>Kiasi kilichotakiwa (snapshot ya PerMemberAmount ya kesi — haibadiliki baadaye).</summary>
    public decimal RequiredAmount { get; set; }

    /// <summary>Kiasi kilichokatwa kwenye pochi hadi sasa (mara nyingi RequiredAmount ukiwa juu ya floor).</summary>
    public decimal DeductedAmount { get; set; }

    public EventObligationStatus Status { get; set; } = EventObligationStatus.Pending;

    /// <summary>Tarehe ambayo deni linatakiwa liwe limekamilika (toka sera: Event.TopUpWindowDays).</summary>
    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAt { get; set; }

    public Guid? WaivedByUserId { get; set; }
    public string? WaiveReason { get; set; }

    /// <summary>Faini ya kuchelewa ilishatozwa? (kinga dhidi ya faini mara mbili).</summary>
    public bool OverdueFineIssued { get; set; }

    /// <summary>
    /// SASISHO21 — kodi ya malipo ya wajibu huu (mf. "J3-E07-0147").
    /// Mwanachama anaiandika kwenye maelezo ya malipo; mfumo unaitumia
    /// kulinganisha statement. Null kwa wajibu wa zamani (kabla ya SASISHO21) —
    /// hujazwa na BackfillPaymentCodesAsync.
    /// </summary>
    public string? PaymentCode { get; set; }

    /// <summary>Deni linalobaki (haijahifadhiwa — inahesabiwa).</summary>
    public decimal OutstandingAmount => Status == EventObligationStatus.Settled || Status == EventObligationStatus.Waived
        ? 0m
        : Math.Max(0m, RequiredAmount - DeductedAmount);

    public Group? Group { get; set; }
    public GroupMember? GroupMember { get; set; }
}
