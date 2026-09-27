namespace ChamaLink.Domain.Entities;

public class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid AccountId { get; set; }
    
    // Ongeza hii hapa kuunganisha mwanachama binafsi na muamala
    public Guid? UserId { get; set; } 

    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string? ReferenceNo { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public Group? Group { get; set; }
    public Account? Account { get; set; }
    public User? User { get; set; }
}