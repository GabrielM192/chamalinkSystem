namespace ChamaLink.Domain.Exceptions;

/// <summary>
/// Thrown when GroupPolicy is not configured for a group.
/// No silent fallback to Ukonga numbers (10000m/50000m) - error must be visible.
/// </summary>
public class PolicyNotConfiguredException : InvalidOperationException
{
    public Guid GroupId { get; }

    public PolicyNotConfiguredException(Guid groupId, string message, Exception? inner = null)
        : base(message, inner)
    {
        GroupId = groupId;
    }

    public PolicyNotConfiguredException(Guid groupId)
        : base($"GroupPolicy not configured for group {groupId}. Please configure policy before calculating financial position.")
    {
        GroupId = groupId;
    }
}
