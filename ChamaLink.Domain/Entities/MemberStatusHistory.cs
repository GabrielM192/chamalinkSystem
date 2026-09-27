namespace ChamaLink.Domain.Entities;

public class MemberStatusHistory
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public MemberStatus FromStatus { get; set; }
    public MemberStatus ToStatus { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public string? MetadataJson { get; set; }

    public GroupMember? Member { get; set; }
}
