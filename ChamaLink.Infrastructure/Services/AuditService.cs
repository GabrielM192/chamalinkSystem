using ChamaLink.Domain.Entities;
using System.Text.Json;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Audit Trail Service — every financial operation creates AuditEvent automatically.
/// Immutable, insert-only, every shilling traceable.
/// </summary>
public class AuditService
{
    private readonly ApplicationDbContext _context;

    public AuditService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AuditEvent> CreateAsync(
        Guid groupId,
        Guid? memberId,
        Guid actorId,
        string actorRole,
        string? actorMembershipNumber,
        AuditAction action,
        string entityType,
        Guid entityId,
        object? before,
        object after,
        EventSource source,
        string? sourceReference,
        object? sourceMetadata,
        int? policyVersion,
        object? allocationResult,
        string? reason,
        Guid? correlationId,
        bool isSystemGenerated = false)
    {
        var audit = new AuditEvent
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            MemberId = memberId,
            ActorId = actorId,
            ActorRole = actorRole,
            ActorMembershipNumber = actorMembershipNumber,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = before != null ? JsonSerializer.Serialize(before) : null,
            AfterJson = JsonSerializer.Serialize(after),
            ChangesJson = before != null ? JsonSerializer.Serialize(new { Before = before, After = after }) : null,
            Source = source,
            SourceReference = sourceReference,
            SourceMetadataJson = sourceMetadata != null ? JsonSerializer.Serialize(sourceMetadata) : null,
            PolicyVersion = policyVersion,
            AllocationResultJson = allocationResult != null ? JsonSerializer.Serialize(allocationResult) : null,
            Reason = reason,
            Timestamp = DateTime.UtcNow,
            EffectiveDate = DateTime.UtcNow,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            IsSystemGenerated = isSystemGenerated
        };

        _context.AuditEvents.Add(audit);
        await _context.SaveChangesAsync();
        return audit;
    }

    public async Task<AuditEvent> CreatePaymentReceivedAsync(
        Guid groupId,
        Guid memberId,
        Guid actorId,
        string actorRole,
        decimal amount,
        DateTime receivedAt,
        EventSource source,
        string sourceReference,
        int policyVersion,
        object allocationResult,
        Guid correlationId)
    {
        return await CreateAsync(
            groupId: groupId,
            memberId: memberId,
            actorId: actorId,
            actorRole: actorRole,
            actorMembershipNumber: null,
            action: AuditAction.PaymentReceived,
            entityType: "FinancialEvent",
            entityId: Guid.NewGuid(),
            before: null,
            after: new { Amount = amount, ReceivedAt = receivedAt, MemberId = memberId },
            source: source,
            sourceReference: sourceReference,
            sourceMetadata: new { ReceivedAt = receivedAt, Amount = amount },
            policyVersion: policyVersion,
            allocationResult: allocationResult,
            reason: $"Payment received {amount:N0} for {receivedAt:MMMM yyyy}",
            correlationId: correlationId
        );
    }

    public async Task<List<AuditEvent>> GetByCorrelationIdAsync(Guid correlationId)
    {
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            _context.AuditEvents.Where(a => a.CorrelationId == correlationId).OrderBy(a => a.Timestamp));
    }

    public async Task<List<AuditEvent>> GetByMemberAsync(Guid memberId, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.AuditEvents.Where(a => a.MemberId == memberId);
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Timestamp <= to.Value);
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query.OrderByDescending(a => a.Timestamp));
    }

    public async Task<List<AuditEvent>> GetByGroupAsync(Guid groupId, AuditAction? action = null, DateTime? from = null)
    {
        var query = _context.AuditEvents.Where(a => a.GroupId == groupId);
        if (action.HasValue) query = query.Where(a => a.Action == action.Value);
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query.OrderByDescending(a => a.Timestamp).Take(100));
    }
}
