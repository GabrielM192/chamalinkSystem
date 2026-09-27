namespace ChamaLink.Domain.Entities;

// Sprint 1 gap: "Duplicate Import Protection Sijaiona" - a per-transaction
// ReferenceNo check already existed, but there was no way to know
// "has this exact statement file already been uploaded for this group?"
// before spending time parsing/processing it again. FileHash is a SHA-256
// of the raw uploaded bytes, so even a re-saved/re-named copy of the same
// PDF is still recognised as the same statement.
public class ImportedStatement
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public Group? Group { get; set; }
}
