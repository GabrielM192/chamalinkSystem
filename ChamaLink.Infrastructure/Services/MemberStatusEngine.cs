using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Member Status Engine — rule-driven transitions, not manual edit.
/// Status is derived from financial behavior + governance.
/// </summary>
public class MemberStatusEngine
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _ruleEngine;

    public MemberStatusEngine(ApplicationDbContext context, BusinessRuleEngine ruleEngine)
    {
        _context = context;
        _ruleEngine = ruleEngine;
    }

    public async Task<string> CalculateStatusAsync(Guid memberId, DateTime asOf)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) return "Active";

        var current = member.Status.ToString();
        if (current == "Exited" || current == "Deceased" || current == "Archived")
            return current;

        var policy = await _ruleEngine.GetActivePolicyAsync(member.GroupId, asOf);
        int consecutiveMissed = await CalculateConsecutiveMissedMonthsAsync(memberId, asOf, policy);

        return _ruleEngine.CalculateMemberStatus(consecutiveMissed, policy, current);
    }

    public async Task<MemberStatusHistory> TransitionAsync(Guid memberId, string toStatus, string reason, Guid changedBy)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) throw new InvalidOperationException("Member not found");

        var fromStatus = member.Status.ToString();
        var history = new MemberStatusHistory
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            FromStatus = Enum.Parse<MemberStatus>(fromStatus),
            ToStatus = Enum.Parse<MemberStatus>(toStatus),
            Reason = reason,
            ChangedBy = changedBy,
            ChangedAt = DateTime.UtcNow,
            EffectiveFrom = DateTime.UtcNow
        };

        if (Enum.TryParse<MemberStatus>(toStatus, out var newStatus))
        {
            member.Status = newStatus;
        }

        _context.MemberStatusHistories.Add(history);
        await _context.SaveChangesAsync();

        var audit = new AuditEvent
        {
            Id = Guid.NewGuid(),
            GroupId = member.GroupId,
            MemberId = memberId,
            ActorId = changedBy,
            Action = AuditAction.MemberStatusChanged,
            EntityType = "GroupMember",
            EntityId = memberId,
            BeforeJson = System.Text.Json.JsonSerializer.Serialize(new { Status = fromStatus }),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { Status = toStatus }),
            ChangesJson = System.Text.Json.JsonSerializer.Serialize(new { Status = new { From = fromStatus, To = toStatus } }),
            Reason = reason,
            Timestamp = DateTime.UtcNow,
            EffectiveDate = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid()
        };
        _context.AuditEvents.Add(audit);
        await _context.SaveChangesAsync();

        return history;
    }

    public async Task AutoTransitionAllAsync(Guid groupId, DateTime asOf)
    {
        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync();
        var policy = await _ruleEngine.GetActivePolicyAsync(groupId, asOf);

        foreach (var member in members)
        {
            var current = member.Status.ToString();
            if (current == "Exited" || current == "Deceased" || current == "Archived") continue;

            int consecutiveMissed = await CalculateConsecutiveMissedMonthsAsync(member.Id, asOf, policy);
            string nextStatus = _ruleEngine.CalculateMemberStatus(consecutiveMissed, policy, current);

            if (nextStatus != current)
            {
                await TransitionAsync(member.Id, nextStatus, $"Auto: Missed {consecutiveMissed} consecutive months (Max={policy.MaxConsecutiveMissedMonths})", Guid.Empty);
            }
        }
    }

    private async Task<int> CalculateConsecutiveMissedMonthsAsync(Guid memberId, DateTime asOf, GroupPolicy policy)
    {
        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) return 0;

        var ledger = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.Type == TransactionType.Contribution)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        int consecutive = 0;
        var current = new DateTime(asOf.Year, asOf.Month, 1);

        for (int i = 0; i < policy.MaxConsecutiveMissedMonths + 2; i++)
        {
            var monthStart = current.AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1);

            bool hasContribution = ledger.Any(l => l.CreatedAt >= monthStart && l.CreatedAt < monthEnd && l.Amount >= policy.MonthlyContribution * 0.9m);

            if (hasContribution) break;
            consecutive++;
        }

        return consecutive;
    }
}
