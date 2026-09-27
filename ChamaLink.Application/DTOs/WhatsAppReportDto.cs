namespace ChamaLink.Domain.DTOs
{
    public class WhatsAppReportDto
    {
        public Guid GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public string FormattedMessage { get; set; } = string.Empty;
    }
}