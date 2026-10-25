namespace ChamaLink.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: kamwe isiende kwa mteja
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>SASISHO15: kufungiwa kwa majaribio mabaya kunahifadhiwa kwenye DB (si RAM) — kunafanya kazi kwa seva nyingi.</summary>
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: kamwe isiende kwa mteja
    public int FailedLoginCount { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: kamwe isiende kwa mteja
    public DateTime? LockoutEndUtc { get; set; }
    /// <summary>Ikibadilika, refresh token zote za zamani zinakufa (mf. baada ya kubadilisha nenosiri au kuondolewa uongozi).</summary>
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: kamwe isiende kwa mteja
    public int TokenVersion { get; set; }

    public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
}