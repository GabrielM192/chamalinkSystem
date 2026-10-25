using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Domain.Entities;

/// <summary>
/// SASISHO21 — RUfAA (APPEAL).
///
/// Mwanachama ana haki ya kupinga uamuzi unaomgusa: kufukuzwa/kubadilishwa hali,
/// kukataliwa dai la malipo, au faini. Rufaa HAIFUTI deni yenyewe — inafungua
/// mchakato wa viongozi wawili (si mmoja) kuamua.
///
/// Kanuni tatu:
///   1. Rufaa haibadilishi fedha. Inabadilisha UAMUZI tu (hali ya mwanachama au
///      hali ya dai). Pesa inabadilishwa na hazina kwa njia yake — kwa rekodi.
///   2. Viongozi Wawili wanahitajika: mmoja hawezi kuidhinisha wala kukataa rufaa.
///   3. Mwombaji haruhusiwi kupiga kura kwenye rufaa yake mwenyewe.
///
/// Muda wa kuwasilisha rufaa unatoka kwenye sera (AppealWindowDays), si msimbo.
/// </summary>
public class MemberAppeal
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }

    public AppealKind Kind { get; set; }

    /// <summary>Kitu kinachopingwa: MemberStatusChangeRequest, PaymentClaim, EventObligation... (hiari kwa "Other").</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Maelezo mafupi ya kile kinachopingwa — yanaonekana kwenye orodha bila kuunganisha meza nyingine.</summary>
    [MaxLength(300)] public string SubjectSummary { get; set; } = string.Empty;

    [MaxLength(1500)] public string Reason { get; set; } = string.Empty;

    public AppealStatus Status { get; set; } = AppealStatus.Pending;

    /// <summary>Viongozi wanaohitajika kuamua (snapshot ya sera wakati wa kuwasilisha).</summary>
    public int RequiredApprovals { get; set; }

    /// <summary>Siku alizonazo muombaji kabla rufaa kufungwa kama haijapigiwa kura (snapshot ya sera).</summary>
    public int DecisionWindowDays { get; set; }

    public Guid AppellantUserId { get; set; }
    public DateTime FiledAt { get; set; } = DateTime.UtcNow;

    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }

    [MaxLength(1000)] public string? DecisionNote { get; set; }

    /// <summary>Kama uamuzi ulibadilisha kitu (hali ya mwanachama / dai), hapa kunahifadhiwa kilichotokea.</summary>
    [MaxLength(500)] public string? EffectSummary { get; set; }
}

/// <summary>Kura moja ya kiongozi. Mtu mmoja = kura moja (unique index).</summary>
public class MemberAppealVote
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid AppealId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>true = kubali rufaa (uamuzi ubadilishwe). false = kataa (uamuzi ubaki).</summary>
    public bool Approve { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AppealKind
{
    /// <summary>Kubadilishwa hali / kufukuzwa (MemberStatusChangeRequest).</summary>
    StatusChange = 1,
    /// <summary>Dai la malipo lililokataliwa (PaymentClaim).</summary>
    Claim = 2,
    /// <summary>Faini au wajibu wa tukio.</summary>
    Fine = 3,
    /// <summary>Jambo lingine lolote — linahitaji hatua ya hazina, hakibadilishi chochote yenyewe.</summary>
    Other = 4
}

public enum AppealStatus
{
    Pending = 1,
    /// <summary>Rufaa imekubaliwa — uamuzi uliopingwa umebadilishwa.</summary>
    Granted = 2,
    /// <summary>Rufaa imekataliwa — uamuzi unasimama.</summary>
    Dismissed = 3,
    /// <summary>Mwombaji ameifuta mwenyewe.</summary>
    Withdrawn = 4
}
