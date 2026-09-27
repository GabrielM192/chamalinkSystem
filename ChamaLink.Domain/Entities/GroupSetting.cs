namespace ChamaLink.Domain.Entities;

// Group configuration - 5 modules stored as owned types on same table
public class GroupSettings
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FinancialSettings Financial { get; set; } = new();
    public ContributionSettings Contribution { get; set; } = new();
    public EventSettings Event { get; set; } = new();
    public LoanSettings Loan { get; set; } = new();
    public GovernanceSettings Governance { get; set; } = new();
    public Group? Group { get; set; }
}
