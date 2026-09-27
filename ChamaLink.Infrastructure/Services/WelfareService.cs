using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Welfare Engine — lifecycle: Event → Obligations → Collection → Disbursement → Closed
/// </summary>
public class WelfareService
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _ruleEngine;
    private readonly AuditService _auditService;

    public WelfareService(ApplicationDbContext context, BusinessRuleEngine ruleEngine, AuditService auditService)
    {
        _context = context;
        _ruleEngine = ruleEngine;
        _auditService = auditService;
    }

    public async Task<WelfareEvent> CreateEventAsync(Guid groupId, string title, string description, WelfareType type, Guid? beneficiaryMemberId, string beneficiaryName, decimal requiredPerMember, WelfareCollectionMethod method, Guid createdBy, string reason)
    {
        var policy = await _ruleEngine.GetActivePolicyAsync(groupId, DateTime.UtcNow);

        var welfareEvent = new WelfareEvent
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Title = title,
            Description = description,
            Type = type,
            BeneficiaryMemberId = beneficiaryMemberId,
            BeneficiaryName = beneficiaryName,
            RequiredContributionPerMember = requiredPerMember,
            CollectionMethod = method,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            Status = WelfareEventStatus.Draft,
            PolicyVersion = policy.Version
        };

        _context.WelfareEvents.Add(welfareEvent);
        await _context.SaveChangesAsync();

        await _auditService.CreateAsync(
            groupId, beneficiaryMemberId, createdBy, "Secretary", null,
            AuditAction.WelfareEventCreated, "WelfareEvent", welfareEvent.Id,
            null, welfareEvent, EventSource.Manual, null, null,
            policy.Version, null, reason, Guid.NewGuid());

        return welfareEvent;
    }

    public async Task<WelfareEvent> OpenEventAsync(Guid welfareEventId, Guid approvedBy, string reason)
    {
        var welfareEvent = await _context.WelfareEvents.FirstOrDefaultAsync(w => w.Id == welfareEventId);
        if (welfareEvent == null) throw new InvalidOperationException("Welfare event not found");
        if (welfareEvent.Status != WelfareEventStatus.Draft) throw new InvalidOperationException("Only draft events can be opened");

        var policy = await _ruleEngine.GetActivePolicyAsync(welfareEvent.GroupId, DateTime.UtcNow);
        var members = await _context.GroupMembers.Where(m => m.GroupId == welfareEvent.GroupId && m.Status == MemberStatus.Active).ToListAsync();

        decimal totalExpected = 0;
        foreach (var member in members)
        {
            // Beneficiary exempted?
            if (policy.BeneficiaryExempted && welfareEvent.BeneficiaryMemberId.HasValue && member.Id == welfareEvent.BeneficiaryMemberId.Value)
            {
                var exempted = new WelfareObligation
                {
                    Id = Guid.NewGuid(),
                    WelfareEventId = welfareEventId,
                    MemberId = member.Id,
                    RequiredAmount = welfareEvent.RequiredContributionPerMember,
                    PaidAmount = 0,
                    Status = ObligationStatus.Exempted,
                    CreatedAt = DateTime.UtcNow
                };
                _context.WelfareObligations.Add(exempted);
                continue;
            }

            var obligation = new WelfareObligation
            {
                Id = Guid.NewGuid(),
                WelfareEventId = welfareEventId,
                MemberId = member.Id,
                RequiredAmount = welfareEvent.RequiredContributionPerMember,
                PaidAmount = 0,
                Status = ObligationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                DueDate = welfareEvent.DeadlineDate
            };
            _context.WelfareObligations.Add(obligation);
            totalExpected += welfareEvent.RequiredContributionPerMember;
        }

        welfareEvent.TotalExpected = totalExpected;
        welfareEvent.Status = WelfareEventStatus.Open;
        await _context.SaveChangesAsync();

        await _auditService.CreateAsync(
            welfareEvent.GroupId, welfareEvent.BeneficiaryMemberId, approvedBy, "Chairperson", null,
            AuditAction.GovernanceApproved, "WelfareEvent", welfareEvent.Id,
            new { Status = "Draft" }, new { Status = "Open", TotalExpected = totalExpected },
            EventSource.Manual, null, null, policy.Version, null, reason, Guid.NewGuid());

        // Auto-collect if DeductFromSavings
        if (welfareEvent.CollectionMethod == WelfareCollectionMethod.DeductFromSavings)
        {
            await CollectAllAsync(welfareEventId, approvedBy);
        }

        return welfareEvent;
    }

    public async Task CollectAllAsync(Guid welfareEventId, Guid actorId)
    {
        var welfareEvent = await _context.WelfareEvents.FirstOrDefaultAsync(w => w.Id == welfareEventId);
        if (welfareEvent == null) throw new InvalidOperationException("Welfare event not found");

        var obligations = await _context.WelfareObligations
            .Where(o => o.WelfareEventId == welfareEventId && o.Status == ObligationStatus.Pending)
            .ToListAsync();

        welfareEvent.Status = WelfareEventStatus.Collecting;

        foreach (var obligation in obligations)
        {
            var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == obligation.MemberId);
            if (member == null) continue;

            // Create financial event
            var financialEvent = new FinancialEvent
            {
                Id = Guid.NewGuid(),
                GroupId = welfareEvent.GroupId,
                MemberId = obligation.MemberId,
                Type = FinancialEventType.WelfareContributionPaid,
                Amount = obligation.RequiredAmount,
                OccurredAt = DateTime.UtcNow,
                CreatedBy = actorId,
                Source = EventSource.System,
                PolicyVersion = welfareEvent.PolicyVersion,
                CorrelationId = Guid.NewGuid()
            };
            _context.FinancialEvents.Add(financialEvent);

            // Ledger entry — deduct from savings
            var savingsAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.GroupMemberId == obligation.MemberId && a.Type == AccountType.Savings);
            if (savingsAccount != null)
            {
                _context.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = welfareEvent.GroupId,
                    UserId = member.UserId,
                    AccountId = savingsAccount.Id,
                    Amount = -obligation.RequiredAmount,
                    Type = TransactionType.WelfareDeduction,
                    ReferenceNo = $"WELFARE-{welfareEvent.Id}-{member.MemberNumber}",
                    Description = $"Welfare contribution {welfareEvent.Title}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            var contribution = new WelfareContribution
            {
                Id = Guid.NewGuid(),
                WelfareObligationId = obligation.Id,
                WelfareEventId = welfareEventId,
                MemberId = obligation.MemberId,
                Amount = obligation.RequiredAmount,
                PaidAt = DateTime.UtcNow,
                PaidBy = actorId,
                Source = "SavingsDeduction",
                FinancialEventId = financialEvent.Id
            };
            _context.WelfareContributions.Add(contribution);

            obligation.PaidAmount = obligation.RequiredAmount;
            obligation.Status = ObligationStatus.Paid;
            welfareEvent.TotalCollected += obligation.RequiredAmount;
        }

        if (welfareEvent.TotalCollected >= welfareEvent.TotalExpected * (welfareEvent.GroupId != Guid.Empty ? 0.9m : 0.9m))
        {
            welfareEvent.Status = WelfareEventStatus.Collected;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<WelfareDisbursement> DisburseAsync(Guid welfareEventId, decimal amount, string method, string reference, Guid disbursedBy, Guid approvedBy, string reason)
    {
        var welfareEvent = await _context.WelfareEvents.FirstOrDefaultAsync(w => w.Id == welfareEventId);
        if (welfareEvent == null) throw new InvalidOperationException("Welfare event not found");
        if (welfareEvent.Status != WelfareEventStatus.Collected && welfareEvent.Status != WelfareEventStatus.Collecting)
            throw new InvalidOperationException("Event must be collected before disbursement");

        var financialEvent = new FinancialEvent
        {
            Id = Guid.NewGuid(),
            GroupId = welfareEvent.GroupId,
            MemberId = welfareEvent.BeneficiaryMemberId,
            Type = FinancialEventType.WelfareDisbursement,
            Amount = amount,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = disbursedBy,
            Source = EventSource.Manual,
            PolicyVersion = welfareEvent.PolicyVersion,
            CorrelationId = Guid.NewGuid()
        };
        _context.FinancialEvents.Add(financialEvent);

        var disbursement = new WelfareDisbursement
        {
            Id = Guid.NewGuid(),
            WelfareEventId = welfareEventId,
            BeneficiaryMemberId = welfareEvent.BeneficiaryMemberId,
            BeneficiaryName = welfareEvent.BeneficiaryName,
            Amount = amount,
            DisbursedAt = DateTime.UtcNow,
            DisbursedBy = disbursedBy,
            Method = method,
            Reference = reference,
            FinancialEventId = financialEvent.Id,
            Status = "Disbursed",
            ApprovedBy = approvedBy
        };
        _context.WelfareDisbursements.Add(disbursement);

        welfareEvent.TotalDisbursed += amount;
        welfareEvent.Status = WelfareEventStatus.Disbursing;

        if (welfareEvent.TotalDisbursed >= welfareEvent.TotalCollected)
            welfareEvent.Status = WelfareEventStatus.Closed;

        await _context.SaveChangesAsync();

        await _auditService.CreateAsync(
            welfareEvent.GroupId, welfareEvent.BeneficiaryMemberId, disbursedBy, "Treasurer", null,
            AuditAction.WelfareDisbursement, "WelfareDisbursement", disbursement.Id,
            null, disbursement, EventSource.Manual, reference, null,
            welfareEvent.PolicyVersion, null, reason, financialEvent.CorrelationId ?? Guid.NewGuid());

        return disbursement;
    }

    public async Task<WelfareEvent> GetEventAsync(Guid id)
    {
        return await _context.WelfareEvents
            .Include(w => w.Obligations)
            .FirstOrDefaultAsync(w => w.Id == id) ?? throw new InvalidOperationException("Not found");
    }
}
