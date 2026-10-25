using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Domain.Entities;

/// <summary>Hali ya ujumbe wa SMS kwenye mnyororo.</summary>
public enum SmsStatus
{
    /// <summary>Umepangwa; haujatumwa (labda unasubiri saa za utulivu au salio).</summary>
    Queued = 1,
    /// <summary>Umetumwa kwa mtoa-huduma; tunasubiri mtandao.</summary>
    Sent = 2,
    /// <summary>Mtandao umethibitisha kuwa umefika kwenye simu.</summary>
    Delivered = 3,
    /// <summary>Umeshindikana (mara 3) — namba si sahihi au mtandao umekataa.</summary>
    Failed = 4,
    /// <summary>Haukutumwa kwa makusudi (salio halitoshi, saa za utulivu, kikomo cha tukio).</summary>
    Suppressed = 5
}

/// <summary>
/// SASISHO21 (Sehemu ya 2) — SMS YA NO-REPLY.
///
/// Mnyororo kamili: ujumbe → mtoa-huduma → mtandao → simu → kumbukumbu ya uwasilishaji.
/// Kila hatua ina hali yake, na kila ujumbe una gharama ("credits").
///
/// Kanuni:
///   • Hakuna jina la marehemu, hakuna kiasi cha mtu mwingine, hakuna namba za wengine.
///   • Ujumbe unasema "kikundi", kamwe "kila kikundi".
///   • Salio la SMS ni CREDITS (hazina), si bajeti ya mwezi.
///   • Nje ya saa za utulivu, ujumbe unasubiri — haukataliwi.
/// </summary>
public class SmsMessage
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>Mwanachama anayelengwa (null = namba ya nje, mf. muombaji wa kujiunga).</summary>
    public Guid? GroupMemberId { get; set; }

    [MaxLength(20)] public string Phone { get; set; } = string.Empty;
    /// <summary>Kiolezo kilichotumika (mf. "EventAnnouncement").</summary>
    [MaxLength(40)] public string TemplateKey { get; set; } = string.Empty;
    [MaxLength(360)] public string Body { get; set; } = string.Empty;

    public SmsStatus Status { get; set; } = SmsStatus.Queued;
    /// <summary>Hautumwi kabla ya saa hii (saa za utulivu).</summary>
    public DateTime? SendAfter { get; set; }

    public int Attempts { get; set; }
    [MaxLength(80)] public string? ProviderReference { get; set; }
    [MaxLength(40)] public string? ProviderStatus { get; set; }
    [MaxLength(300)] public string? FailureReason { get; set; }
    [MaxLength(200)] public string? SuppressedReason { get; set; }

    /// <summary>Gharama kwa vitengo vya SMS (kawaida 1; ujumbe mrefu unaweza kuwa 2).</summary>
    public int Credits { get; set; } = 1;

    /// <summary>Kitu kilichosababisha ujumbe (EventObligation / BenefitCase / PaymentReceipt).</summary>
    [MaxLength(40)] public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

/// <summary>
/// Salio la SMS la kikundi (credits zilizonunuliwa). Si bajeti ya mwezi:
/// kikundi kinanunua credits, na kila SMS inapunguza moja.
/// </summary>
public class SmsAccount
{
    public Guid GroupId { get; set; }
    public int Credits { get; set; }
    /// <summary>Jumla ya credits zilizowahi kuongezwa (kwa ripoti).</summary>
    public int TotalPurchased { get; set; }
    /// <summary>Jumla ya SMS zilizotumwa (kwa ripoti).</summary>
    public int TotalSent { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
