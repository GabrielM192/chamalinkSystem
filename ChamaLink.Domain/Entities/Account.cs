namespace ChamaLink.Domain.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupMemberId { get; set; }
    public AccountType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public GroupMember GroupMember { get; set; } = null!;
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}