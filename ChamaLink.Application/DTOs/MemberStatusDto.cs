namespace ChamaLink.Application.DTOs
{
    public class MemberStatusDto
    {
        public Guid UserId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public decimal MonthlyContributionTarget { get; set; }
        public decimal TotalMonthlyPaidThisMonth { get; set; }
        public bool HasPaidCurrentMonth { get; set; }
        public decimal PendingFineAmount { get; set; }
        public decimal AdvanceBalance { get; set; }
    }
}