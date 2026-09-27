namespace ChamaLink.Domain.Entities;

public enum MembershipRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecisionNote { get; set; }

    public Group? Group { get; set; }
    public User? User { get; set; }
}
