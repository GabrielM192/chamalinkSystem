using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// Mahali PAMOJA pa kuandika kumbukumbu za ukaguzi (AuditEvents) kwa huduma za Injini ya Ukweli na uanachama.
/// Inaongeza kumbukumbu kwenye DbContext tu: mwitaji anaihifadhi kwa SaveChanges ILE ILE ya kitendo,
/// kwa hiyo kitendo hakiwezi kuhifadhiwa bila kumbukumbu yake. AuditEvents hazifutwi kamwe.
/// </summary>
public static class AuditTrail
{
    public static async Task AddAsync(ApplicationDbContext context, Guid groupId, Guid? memberId, Guid actorUserId, AuditAction action,
        string entityType, Guid entityId, object? before, object after, string? sourceReference, string? reason, CancellationToken ct)
    {
        var actor = await context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.UserId == actorUserId)
            .Select(m => new { m.Role, m.MemberNumber }).FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        context.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), GroupId = groupId, MemberId = memberId,
            ActorId = actorUserId, ActorRole = actor?.Role.ToString() ?? "HajulikaniKwenyeKikundi", ActorMembershipNumber = actor?.MemberNumber,
            Action = action, EntityType = entityType, EntityId = entityId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(after),
            ChangesJson = before is null ? null : JsonSerializer.Serialize(new { Before = before, After = after }),
            Source = EventSource.Manual, SourceReference = sourceReference, Reason = reason,
            Timestamp = now, EffectiveDate = now, CorrelationId = entityId, IsSystemGenerated = false
        });
    }
}
