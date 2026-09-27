namespace ChamaLink.Domain.Entities;

public enum GroupType
{
    MonthlySavings,
    EventBased,
    Hybrid
}

public class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    /// <summary>Kifupi cha kikundi kwenye namba za uanachama (mf. "UMK" → UMK-2026-001). Hakibadiliki baada ya namba ya kwanza kutolewa.</summary>
    public string? ShortCode { get; set; }
    /// <summary>Kodi ambayo Mwenyekiti anawapa watu ili waombe kujiunga. Inaweza kubadilishwa (ikivuja).</summary>
    public string? JoinCode { get; set; }
    public GroupType Type { get; set; } = GroupType.MonthlySavings;
    public OrganizationType OrganizationType { get; set; } = OrganizationType.Mkoba;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public GroupSettings? Settings { get; set; }
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}
