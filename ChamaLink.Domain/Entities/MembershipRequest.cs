namespace ChamaLink.Domain.Entities;

public enum MembershipRequestStatus
{
    /// <summary>Inasubiri kiongozi (kikundi kisichotaka malipo kabla ya uanachama).</summary>
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    /// <summary>SASISHO21: ombi linasubiri MALIPO ya kujiunga kabla kiongozi aamue.</summary>
    AwaitingPayment = 5,
    /// <summary>SASISHO21: malipo yamethibitishwa — sasa linasubiri kiongozi kuidhinisha uanachama.</summary>
    Paid = 6,
    /// <summary>SASISHO21: muda wa malipo umepita bila kulipwa (si deni — ombi limeisha).</summary>
    Expired = 7
}

/// <summary>
/// Ombi la mtu mwenye akaunti (User) kujiunga na kikundi kwa kodi ya kujiunga.
///
/// Mwanachama (GroupMember) ni rekodi ya FEDHA; akaunti (User) ni ya KUINGIA. Wanachama walioingizwa
/// na viongozi/import hawana akaunti. Mtu akijisajili na namba ya simu inayolingana na mwanachama
/// asiye na akaunti, mfumo UNAPENDEKEZA tu (SuggestedMemberId). Kiongozi ndiye anayethibitisha kuunganisha
/// (LinkedMemberId) wakati wa kuidhinisha. Hakuna kuunganisha kiotomatiki kwa jina wala kwa simu.
/// </summary>
public class MembershipRequest
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public GroupRole RequestedRole { get; set; } = GroupRole.Member;
    public MembershipRequestStatus Status { get; set; } = MembershipRequestStatus.Pending;
    public string? Note { get; set; }

    /// <summary>Mwanachama asiye na akaunti ambaye namba yake ya simu inalingana (pendekezo tu).</summary>
    public Guid? SuggestedMemberId { get; set; }
    /// <summary>Mwanachama aliyeunganishwa na akaunti hii baada ya kiongozi kuthibitisha.</summary>
    public Guid? LinkedMemberId { get; set; }

    // ---- SASISHO21: LANGO LA KUJIUNGA (lipa kwanza, kisha uwe mwanachama) ----
    //
    // Kikundi chenye JoiningPaymentRequired = true hakimfanyi mtu mwanachama
    // hadi malipo yake yathibitishwe. Malipo hayo yana kodi yake (J3-JOIN-0007)
    // ili yaweze kulinganishwa na statement ya M-Koba kama malipo mengine.
    //
    // Ada inagawanywa kama sera ya kikundi inavyosema:
    //   JoiningSavingsPortion  → AKIBA ya mwanachama (inabaki kuwa yake)
    //   kilichobaki            → KIANZIO (hakiingii akiba)
    /// <summary>Kodi ya malipo ya ombi, mf. "J3-JOIN-0007".</summary>
    public string? PaymentCode { get; set; }
    /// <summary>Kiasi kinachotakiwa (nakala ya sera wakati wa ombi — haibadiliki baadaye).</summary>
    public decimal? PaymentAmount { get; set; }
    /// <summary>Tarehe ya mwisho ya malipo (CreatedAt + siku za sera). Null = hakuna muda.</summary>
    public DateTime? PaymentDeadline { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public Guid? PaymentReceiptId { get; set; }
    /// <summary>Namba ya muamala iliyothibitishwa (kwa mwanachama kuiona).</summary>
    public string? PaymentReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecisionNote { get; set; }

    public Group? Group { get; set; }
    public User? User { get; set; }
}
