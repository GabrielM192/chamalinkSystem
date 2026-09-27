namespace ChamaLink.Domain.Entities;

public class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    
    /// <summary>Namba ya uanachama. Muundo rasmi {ShortCode}-{Mwaka}-{Namba}, mf. UMK-2026-001.</summary>
    public string MemberNumber { get; set; } = string.Empty;
    /// <summary>Ikiwa na thamani, MemberNumber ni ya muundo rasmi na HAIBADILIKI kamwe.</summary>
    public DateTime? MemberNumberIssuedAt { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member; // Changed string to GroupRole
    public MemberStatus Status { get; set; } = MemberStatus.New;
    public int TotalContributionsCount { get; set; } = 0;
    public decimal AdvanceBalance { get; set; } = 0m;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public Group? Group { get; set; }
    public User? User { get; set; }
}