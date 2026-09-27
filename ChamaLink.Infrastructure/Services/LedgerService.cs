using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// V2 — Single Allocation Engine path. No direct balance updates.
/// Every contribution goes through FinancialEvent → AllocationEngine → Ledger → Audit.
/// </summary>
public class LedgerService
{
    private readonly ApplicationDbContext _context;
    private readonly AccountResolverService _accountResolver;
    private readonly MemberCounterSyncService _counterSync;
    private readonly AllocationEngine _allocationEngine;
    private readonly BusinessRuleEngine _businessRuleEngine;
    private readonly FinancialPositionService _financialPositionService;
    private readonly ObligationLedgerService _obligationLedgerService;
    private readonly AuditService _auditService;

    public LedgerService(
        ApplicationDbContext context,
        AccountResolverService accountResolver,
        MemberCounterSyncService counterSync,
        AllocationEngine allocationEngine,
        BusinessRuleEngine businessRuleEngine,
        FinancialPositionService financialPositionService,
        ObligationLedgerService obligationLedgerService,
        AuditService auditService)
    {
        _context = context;
        _accountResolver = accountResolver;
        _counterSync = counterSync;
        _allocationEngine = allocationEngine;
        _businessRuleEngine = businessRuleEngine;
        _financialPositionService = financialPositionService;
        _obligationLedgerService = obligationLedgerService;
        _auditService = auditService;
    }

    public async Task<LedgerResponseDto> RecordContributionAsync(RecordContributionDto dto)
    {
        var groupMember = await _accountResolver.GetGroupMemberAsync(dto.GroupId, dto.UserId);
        var account = await _accountResolver.GetOrCreateAccountAsync(groupMember.Id, AccountType.Savings);

        var policy = await _businessRuleEngine.GetActivePolicyAsync(dto.GroupId, DateTime.UtcNow);
        var position = await _financialPositionService.CalculatePositionAsync(groupMember.Id);
        var activeLoans = await _context.Loans.Where(l => l.GroupMemberId == groupMember.Id && l.Status == LoanStatus.Active).ToListAsync();
        var outstandingQueue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(groupMember.Id, policy, DateTime.UtcNow);

        // Oldest-first single engine
        var allocation = _allocationEngine.AllocateOldestFirst(
            paymentAmount: dto.Amount,
            paymentDate: DateTime.UtcNow,
            outstandingQueue: outstandingQueue,
            policy: policy,
            activeLoans: activeLoans,
            year: DateTime.UtcNow.Year,
            month: DateTime.UtcNow.Month);

        var correlationId = allocation.CorrelationId;

        // FinancialEvent for payment received
        var paymentEvent = new FinancialEvent
        {
            Id = Guid.NewGuid(),
            GroupId = dto.GroupId,
            MemberId = groupMember.Id,
            Type = FinancialEventType.PaymentReceived,
            Amount = dto.Amount,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = groupMember.UserId,
            Source = EventSource.Manual,
            SourceReference = dto.ReferenceNo,
            PolicyVersion = policy.Version,
            CorrelationId = correlationId
        };
        _context.FinancialEvents.Add(paymentEvent);

        LedgerEntry? mainEntry = null;

        foreach (var alloc in allocation.Allocations)
        {
            if (alloc.Target == "SavingsCover") continue;

            var (type, desc) = alloc.Target switch
            {
                "Contribution" => (TransactionType.Contribution, dto.Description ?? "Monthly Savings Contribution"),
                "LoanRepayment" => (TransactionType.LoanRepayment, $"Loan repayment {DateTime.UtcNow:MMMM yyyy}"),
                "Fine" => (TransactionType.FinePayment, $"Fine payment {DateTime.UtcNow:MMMM yyyy}"),
                "JoiningFee" => (TransactionType.JoiningFee, $"Joining fee {DateTime.UtcNow:MMMM yyyy}"),
                "Debt" => (TransactionType.Contribution, $"Pay old debt {dto.ReferenceNo}"),
                "Savings" => (TransactionType.Savings, $"Excess savings {DateTime.UtcNow:MMMM yyyy}"),
                _ => (TransactionType.Savings, $"{alloc.Target} {DateTime.UtcNow:MMMM yyyy}")
            };

            var entry = new LedgerEntry
            {
                Id = Guid.NewGuid(),
                GroupId = dto.GroupId,
                UserId = groupMember.UserId,
                AccountId = account.Id,
                Amount = alloc.Amount,
                Type = type,
                ReferenceNo = alloc.Target == "Contribution" ? dto.ReferenceNo : $"{dto.ReferenceNo}-{alloc.Target}",
                Description = desc,
                CreatedAt = DateTime.UtcNow
            };
            _context.LedgerEntries.Add(entry);

            if (alloc.Target == "Contribution" && mainEntry == null)
                mainEntry = entry;

            if (alloc.Target == "Savings")
                groupMember.AdvanceBalance += alloc.Amount;

            // FinancialEvent for allocation
            _context.FinancialEvents.Add(new FinancialEvent
            {
                Id = Guid.NewGuid(),
                GroupId = dto.GroupId,
                MemberId = groupMember.Id,
                Type = alloc.Target switch
                {
                    "Contribution" => FinancialEventType.ContributionPaid,
                    "LoanRepayment" => FinancialEventType.LoanRepaymentPaid,
                    "Fine" => FinancialEventType.FinePaid,
                    "JoiningFee" => FinancialEventType.JoiningFeePaid,
                    "Debt" => FinancialEventType.DebtCleared,
                    "Savings" => FinancialEventType.SavingsDeposited,
                    _ => FinancialEventType.SavingsDeposited
                },
                Amount = alloc.Amount,
                OccurredAt = DateTime.UtcNow,
                CreatedBy = groupMember.UserId,
                Source = EventSource.Manual,
                SourceReference = entry.ReferenceNo,
                PolicyVersion = policy.Version,
                CorrelationId = correlationId
            });
        }

        if (mainEntry == null)
        {
            mainEntry = new LedgerEntry
            {
                Id = Guid.NewGuid(),
                GroupId = dto.GroupId,
                UserId = groupMember.UserId,
                AccountId = account.Id,
                Amount = dto.Amount,
                Type = TransactionType.Contribution,
                ReferenceNo = dto.ReferenceNo,
                Description = dto.Description ?? "Monthly Savings Contribution",
                CreatedAt = DateTime.UtcNow
            };
            _context.LedgerEntries.Add(mainEntry);
        }

        await _context.SaveChangesAsync();

        await _auditService.CreateAsync(
            groupId: dto.GroupId,
            memberId: groupMember.Id,
            actorId: groupMember.UserId,
            actorRole: "Treasurer",
            actorMembershipNumber: groupMember.MemberNumber,
            action: AuditAction.PaymentReceived,
            entityType: "FinancialEvent",
            entityId: paymentEvent.Id,
            before: null,
            after: new { Amount = dto.Amount, Reference = dto.ReferenceNo, Allocation = allocation },
            source: EventSource.Manual,
            sourceReference: dto.ReferenceNo,
            sourceMetadata: new { Amount = dto.Amount, Description = dto.Description },
            policyVersion: policy.Version,
            allocationResult: allocation,
            reason: dto.Description ?? "Manual contribution",
            correlationId: correlationId);

        await _counterSync.ReconcileMemberAsync(groupMember.Id);
        await _context.SaveChangesAsync();

        return new LedgerResponseDto(mainEntry.Id, mainEntry.GroupId, mainEntry.AccountId, mainEntry.Amount, mainEntry.Type.ToString(), mainEntry.ReferenceNo, mainEntry.CreatedAt);
    }

    public async Task<GroupSummaryDto> GetGroupSummaryAsync(Guid groupId)
    {
        var ledger = await _context.LedgerEntries
            .Where(e => e.GroupId == groupId)
            .GroupBy(e => 1)
            .Select(g => new
            {
                TotalSavings = g.Where(e => e.Type == TransactionType.Contribution || e.Type == TransactionType.Savings).Sum(e => (decimal?)e.Amount) ?? 0m,
                TotalLoans = g.Where(e => e.Type == TransactionType.LoanDisbursement).Sum(e => (decimal?)e.Amount) ?? 0m
            })
            .FirstOrDefaultAsync();

        var totalSavings = ledger?.TotalSavings ?? 0m;
        var totalLoans = ledger?.TotalLoans ?? 0m;

        var totalMembers = await _context.GroupMembers.CountAsync(m => m.GroupId == groupId);

        return new GroupSummaryDto(groupId, totalSavings, totalLoans, totalMembers);
    }

    public async Task<MemberStatementDto> GetMemberStatementAsync(Guid groupId, Guid userId)
    {
        var groupMember = await _context.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId)
            ?? throw new NotFoundException("Mwanachama hajapatikana.");

        var accountIds = await _context.Accounts
            .Where(a => a.GroupMemberId == groupMember.Id)
            .Select(a => a.Id)
            .ToListAsync();

        var totals = await _context.LedgerEntries
            .Where(e => accountIds.Contains(e.AccountId))
            .GroupBy(e => 1)
            .Select(g => new
            {
                TotalSavings = g.Where(e => e.Type == TransactionType.Contribution || e.Type == TransactionType.Savings).Sum(e => (decimal?)e.Amount) ?? 0m,
                TotalDisbursed = g.Where(e => e.Type == TransactionType.LoanDisbursement).Sum(e => (decimal?)e.Amount) ?? 0m,
                TotalRepaid = g.Where(e => e.Type == TransactionType.LoanRepayment).Sum(e => (decimal?)e.Amount) ?? 0m
            })
            .FirstOrDefaultAsync();

        var entries = await _context.LedgerEntries
            .Where(e => accountIds.Contains(e.AccountId))
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new LedgerResponseDto(
                e.Id,
                e.GroupId,
                e.AccountId,
                e.Amount,
                e.Type.ToString(),
                e.ReferenceNo,
                e.CreatedAt
            ))
            .ToListAsync();

        var outstandingLoan = (totals?.TotalDisbursed ?? 0m) - (totals?.TotalRepaid ?? 0m);

        return new MemberStatementDto(userId, totals?.TotalSavings ?? 0m, outstandingLoan, entries);
    }
}
