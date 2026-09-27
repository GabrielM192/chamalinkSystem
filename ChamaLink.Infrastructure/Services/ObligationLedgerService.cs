using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Obligation item - single source of truth for what is due per month.
/// DueDate = next month + DueDateDay (e.g. Jan contrib due Feb 5).
/// Contribution + Fine are paired per month for oldest-first allocation.
/// </summary>
public class ObligationItem
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public DateTime DueDate { get; set; }
    public bool IsFirstMonth { get; set; }

    public decimal ContributionDue { get; set; }
    public decimal ContributionPaid { get; set; }
    public decimal ContributionOutstanding => Math.Max(0, ContributionDue - ContributionPaid);

    public decimal FineDue { get; set; }
    public decimal FinePaid { get; set; }
    public decimal FineOutstanding => Math.Max(0, FineDue - FinePaid);

    public decimal JoinFeeDue { get; set; }
    public decimal JoinFeePaid { get; set; }
    public decimal JoinFeeOutstanding => Math.Max(0, JoinFeeDue - JoinFeePaid);

    public decimal TotalDue => ContributionDue + FineDue + JoinFeeDue;
    public decimal TotalPaid => ContributionPaid + FinePaid + JoinFeePaid;
    public decimal TotalOutstanding => Math.Max(0, TotalDue - TotalPaid);

    public bool IsOverdue { get; set; }
    public bool IsFullyPaid => TotalOutstanding <= 0.01m;
    public bool IsContributionFullyPaid => ContributionOutstanding <= 0.01m;
}

/// <summary>
/// ObligationLedgerService - SINGLE SOURCE for all obligation logic.
/// Replaces 4 conflicting engines with one queue ordered by DueDate ASC.
/// </summary>
public class ObligationLedgerService
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _ruleEngine;

    public ObligationLedgerService(ApplicationDbContext context, BusinessRuleEngine ruleEngine)
    {
        _context = context;
        _ruleEngine = ruleEngine;
    }

    private async Task<List<ObligationItem>> BuildOutstandingQueueWithPolicyAsync(
        Guid memberId,
        GroupPolicy policy,
        DateTime asOfDate)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var queue = await GenerateRawQueueWithPolicyAsync(memberId, policy, asOfDate);
        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId &&
                        l.UserId == member.UserId &&
                        l.CreatedAt <= asOfDate)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        // Fine eligibility is based on the balance at the grace deadline,
        // not on the final balance at the report date.
        foreach (var obligation in queue)
        {
            if (!obligation.IsOverdue) continue;

            var graceDeadline = obligation.DueDate.AddDays(policy.GracePeriodDays);
            var paidByGrace = ledgerEntries
                .Where(l => l.Type == TransactionType.Contribution &&
                            l.CreatedAt <= graceDeadline)
                .Sum(l => l.Amount);
            var priorContributionDue = queue
                .Where(q => q.DueDate < obligation.DueDate)
                .Sum(q => q.ContributionDue);
            var availableForObligation = Math.Max(0m, paidByGrace - priorContributionDue);

            if (availableForObligation < obligation.ContributionDue - 0.01m)
                obligation.FineDue = policy.LateFine;
        }

        // Ledger entries are already allocation results. Replay each
        // category into its own bucket; Savings must not pay contributions.
        foreach (var entry in ledgerEntries)
        {
            switch (entry.Type)
            {
                case TransactionType.Contribution:
                    ApplyToQueue(queue, entry.Amount,
                        q => q.ContributionOutstanding,
                        (q, amount) => q.ContributionPaid += amount);
                    break;
                case TransactionType.FinePayment:
                    ApplyToQueue(queue, entry.Amount,
                        q => q.FineOutstanding,
                        (q, amount) => q.FinePaid += amount);
                    break;
                case TransactionType.JoiningFee:
                    ApplyToQueue(queue, entry.Amount,
                        q => q.JoinFeeOutstanding,
                        (q, amount) => q.JoinFeePaid += amount);
                    break;
            }
        }

        return queue.OrderBy(q => q.DueDate).ToList();
    }

    private static void ApplyToQueue(
        List<ObligationItem> queue,
        decimal amount,
        Func<ObligationItem, decimal> outstanding,
        Action<ObligationItem, decimal> apply)
    {
        var remaining = amount;
        foreach (var obligation in queue.OrderBy(q => q.DueDate))
        {
            if (remaining <= 0) break;
            var allocation = Math.Min(remaining, outstanding(obligation));
            if (allocation <= 0) continue;
            apply(obligation, allocation);
            remaining -= allocation;
        }
    }

    public async Task<DateTime> GetEffectiveJoinDateAsync(Guid memberId)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var earliestLedger = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync();

        DateTime effective = member.JoinedAt;
        if (effective.Kind == DateTimeKind.Unspecified)
            effective = DateTime.SpecifyKind(effective, DateTimeKind.Utc);

        if (earliestLedger != null && earliestLedger.CreatedAt < effective)
            effective = new DateTime(earliestLedger.CreatedAt.Year, earliestLedger.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return effective;
    }

    public int GetDueDateDay(GroupPolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        if (policy.DueDateDay is < 1 or > 28)
            throw new InvalidOperationException($"DueDateDay {policy.DueDateDay} is invalid for policy {policy.Version}.");
        return policy.DueDateDay;
    }

    /// <summary>
    /// New method requires GroupPolicy as mandatory parameter - no hidden fetch with fallback.
    /// All financial numbers come from policy only.
    /// </summary>
    public async Task<List<ObligationItem>> GenerateRawQueueWithPolicyAsync(Guid memberId, GroupPolicy policy, DateTime asOfDate)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        if (policy.MonthlyContribution <= 0) throw new InvalidOperationException($"MonthlyContribution {policy.MonthlyContribution} invalid for group {policy.GroupId}");

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var dueDateDay = GetDueDateDay(policy);
        var effectiveJoin = await GetEffectiveJoinDateAsync(memberId);

        var queue = new List<ObligationItem>();
        var current = new DateTime(effectiveJoin.Year, effectiveJoin.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(asOfDate.Year, asOfDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        int monthIndex = 0;
        while (current <= end)
        {
            monthIndex++;
            bool isFirst = monthIndex == 1;
            var nextMonth = current.AddMonths(1);
            int day = Math.Min(dueDateDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            var dueDate = new DateTime(nextMonth.Year, nextMonth.Month, day, 23, 59, 59, DateTimeKind.Utc);
            bool isOverdue = asOfDate > dueDate.AddDays(policy.GracePeriodDays);

            queue.Add(new ObligationItem
            {
                Year = current.Year,
                Month = current.Month,
                MonthName = current.ToString("MMM yyyy"),
                DueDate = dueDate,
                IsFirstMonth = isFirst,
                ContributionDue = policy.MonthlyContribution,
                ContributionPaid = 0,
                FineDue = 0,
                FinePaid = 0,
                JoinFeeDue = isFirst ? policy.JoiningFee : 0m,
                JoinFeePaid = 0,
                IsOverdue = isOverdue
            });

            current = current.AddMonths(1);
        }

        return queue;
    }

    /// <summary>
    /// Generate raw obligation queue from effective join date to asOf (inclusive).
    /// DueDate = next month + DueDateDay (e.g. Jan due Feb 5).
    /// </summary>
    public async Task<List<ObligationItem>> GenerateRawQueueAsync(Guid memberId, DateTime? asOf = null)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);
        else if (asOfDate.Kind == DateTimeKind.Local)
            asOfDate = asOfDate.ToUniversalTime();

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var policy = await _ruleEngine.GetActivePolicyAsync(member.GroupId, asOfDate);
        var dueDateDay = GetDueDateDay(policy);
        var effectiveJoin = await GetEffectiveJoinDateAsync(memberId);

        var queue = new List<ObligationItem>();
        var current = new DateTime(effectiveJoin.Year, effectiveJoin.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(asOfDate.Year, asOfDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        int monthIndex = 0;
        while (current <= end)
        {
            monthIndex++;
            bool isFirst = monthIndex == 1;

            // DueDate = next month + DueDateDay (spec: Jan due Feb 5)
            var nextMonth = current.AddMonths(1);
            int day = Math.Min(dueDateDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            var dueDate = new DateTime(nextMonth.Year, nextMonth.Month, day, 23, 59, 59, DateTimeKind.Utc);

            bool isOverdue = asOfDate > dueDate.AddDays(policy.GracePeriodDays);

            queue.Add(new ObligationItem
            {
                Year = current.Year,
                Month = current.Month,
                MonthName = current.ToString("MMM yyyy"),
                DueDate = dueDate,
                IsFirstMonth = isFirst,
                ContributionDue = policy.MonthlyContribution,
                ContributionPaid = 0,
                FineDue = 0, // will be set after checking overdue + unpaid
                FinePaid = 0,
                JoinFeeDue = isFirst ? policy.JoiningFee : 0m,
                JoinFeePaid = 0,
                IsOverdue = isOverdue
            });

            current = current.AddMonths(1);
        }

        return queue;
    }

    /// <summary>
    /// Get outstanding queue WITH mandatory GroupPolicy - no hidden fetch.
    /// All financial numbers from policy only.
    /// </summary>
    public async Task<List<ObligationItem>> GetOutstandingQueueWithPolicyAsync(Guid memberId, GroupPolicy policy, DateTime asOfDate)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        return await BuildOutstandingQueueWithPolicyAsync(memberId, policy, asOfDate);

#if false
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var rawQueue = await GenerateRawQueueWithPolicyAsync(memberId, policy, asOfDate);

        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.CreatedAt <= asOfDate)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        // Same allocation logic as before but using provided policy
        foreach (var ob in rawQueue)
        {
            ob.ContributionPaid = 0;
            ob.JoinFeePaid = 0;
            ob.FinePaid = 0;
        }

        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;
            foreach (var ob in rawQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        foreach (var ob in rawQueue)
        {
            if (ob.IsOverdue && ob.ContributionOutstanding > 0.01m)
                ob.FineDue = policy.LateFine;
        }

        foreach (var ob in rawQueue)
        {
            ob.ContributionPaid = 0;
            ob.JoinFeePaid = 0;
            ob.FinePaid = 0;
        }

        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;
            foreach (var ob in rawQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.FineOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.FineOutstanding);
                    ob.FinePaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        foreach (var ob in rawQueue)
        {
            if (!ob.IsOverdue) continue;
            var graceDeadline = ob.DueDate.AddDays(policy.GracePeriodDays);
            var paymentsBeforeGrace = ledgerEntries.Where(l => l.CreatedAt <= graceDeadline).Sum(l => l.Amount);
            var neededBefore = rawQueue.Where(o => o.DueDate < ob.DueDate).Sum(o => o.ContributionDue + o.JoinFeeDue);
            var availableForThis = paymentsBeforeGrace - neededBefore;
            if (availableForThis < ob.ContributionDue - 0.01m)
                ob.FineDue = policy.LateFine;
            else if (ob.FinePaid == 0)
                ob.FineDue = 0;
        }

        foreach (var ob in rawQueue)
        {
            ob.ContributionPaid = 0;
            ob.FinePaid = 0;
            ob.JoinFeePaid = 0;
        }

        foreach (var entry in ledgerEntries.OrderBy(e => e.CreatedAt))
        {
            decimal remaining = entry.Amount;
            foreach (var ob in rawQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.FineOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.FineOutstanding);
                    ob.FinePaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        return rawQueue.OrderBy(o => o.DueDate).ToList();
#endif
    }

    /// <summary>
    /// Get outstanding queue after applying all ledger entries oldest-first.
    /// This is the SINGLE SOURCE used by FinancialPosition, AllocationEngine, ObligationEngine.
    /// </summary>
    public async Task<List<ObligationItem>> GetOutstandingQueueAsync(Guid memberId, DateTime? asOf = null)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var policy = await _ruleEngine.GetActivePolicyAsync(member.GroupId, asOfDate);
        return await BuildOutstandingQueueWithPolicyAsync(memberId, policy, asOfDate);

#if false
        var rawQueue = await GenerateRawQueueAsync(memberId, asOfDate);

        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.CreatedAt <= asOfDate)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        // First pass: allocate ledger to contributions and joining fees oldest-first
        decimal totalLedger = ledgerEntries.Sum(l => l.Amount);
        // We need to simulate allocation: join fee first? Spec says Overdue Contrib+Fine paired -> Current -> Join -> Savings
        // But for outstanding calc, we do oldest-first for contrib+join
        // Actually business truth: Join fee is separate, paid with first payment that has excess
        // For simplicity: allocate oldest contribution first, then join fee, then fines

        // Track remaining per entry in chronological order
        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;

            // 1. Allocate to oldest unpaid contributions + join fees (oldest-first)
            foreach (var ob in rawQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;

                // Join fee first for first month obligation
                if (ob.JoinFeeOutstanding > 0)
                {
                    decimal alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }

                if (remaining <= 0) break;

                if (ob.ContributionOutstanding > 0)
                {
                    decimal alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }
            }

            // 2. If still remaining, it would be savings - not affecting queue
        }

        // Second pass: determine fines for overdue unpaid contributions
        foreach (var ob in rawQueue)
        {
            // Fine applies if overdue and contribution not fully paid by dueDate+grace
            if (ob.IsOverdue && ob.ContributionOutstanding > 0.01m)
            {
                ob.FineDue = policy.LateFine;
            }
        }

        // Third pass: allocate ledger again to fines oldest-first (if any ledger left after contrib)
        // Re-simulate with fines included: we already allocated all ledger to contrib/join,
        // but some ledger might have been intended for fines. We need to re-allocate from scratch
        // with correct order: oldest contrib+fine paired
        // Reset paid and re-allocate correctly

        foreach (var ob in rawQueue)
        {
            ob.ContributionPaid = 0;
            ob.JoinFeePaid = 0;
            ob.FinePaid = 0;
        }

        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;

            // Oldest-first: for each obligation in DueDate order, pay Contrib, then Fine (paired), then JoinFee
            foreach (var ob in rawQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;

                // Contribution first for this obligation
                if (ob.ContributionOutstanding > 0)
                {
                    decimal alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }

                if (remaining <= 0) break;

                // Fine paired with same month (if overdue)
                if (ob.FineOutstanding > 0)
                {
                    decimal alloc = Math.Min(remaining, ob.FineOutstanding);
                    ob.FinePaid += alloc;
                    remaining -= alloc;
                }

                if (remaining <= 0) break;

                // Join fee for first month
                if (ob.JoinFeeOutstanding > 0)
                {
                    decimal alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        // Re-evaluate fines after final allocation: if contrib now paid, fine may still be due if it was overdue at time of payment?
        // Spec: fine charged if not paid within grace period. Once charged, it remains even if contrib later paid.
        // So we keep FineDue as charged if IsOverdue and at any point contribution was unpaid past grace.
        // For simplicity: if obligation was overdue at asOf and contribution was ever unpaid past due, keep fine.
        // Our current logic already charges fine if IsOverdue and ContributionOutstanding >0 after all allocations.
        // But if contribution was paid late (after due), fine should still be due.
        // To handle late payment: check if earliest payment that covered this obligation arrived after DueDate+Grace.
        // For minimal, we charge fine if IsOverdue and (ContributionPaid==0 or paid late).
        // We approximate: if IsOverdue and ledger entry that paid it has CreatedAt > DueDate+Grace, then fine due.
        // For now, simpler: if IsOverdue, check if there was any month where no payment existed before due date.
        // We will keep current simple rule: if IsOverdue and Contribution was not fully paid by DueDate+Grace based on ledger timing, fine due.
        // For we implement timing check:

        foreach (var ob in rawQueue)
        {
            if (!ob.IsOverdue) continue;

            // Find payments that were available before dueDate+grace
            var graceDeadline = ob.DueDate.AddDays(policy.GracePeriodDays);
            var paymentsBeforeGrace = ledgerEntries
                .Where(l => l.CreatedAt <= graceDeadline)
                .Sum(l => l.Amount);

            // Calculate how much was needed before this obligation in queue order
            var neededBefore = rawQueue
                .Where(o => o.DueDate < ob.DueDate)
                .Sum(o => o.ContributionDue + o.JoinFeeDue);

            var availableForThis = paymentsBeforeGrace - neededBefore;

            // If available < contribution due, then fine should be charged
            if (availableForThis < ob.ContributionDue - 0.01m)
            {
                ob.FineDue = policy.LateFine;
            }
            else
            {
                // Paid on time, no fine - but if we already charged, keep it only if still unpaid? 
                // For on-time payment, no fine
                if (ob.FinePaid == 0)
                    ob.FineDue = 0;
            }
        }

        // Final re-allocation for fines that were newly charged due to timing
        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;
            // Subtract already allocated to contributions/join
            // Instead, re-do full allocation from scratch with final FineDue values
        }

        // Final allocation from scratch with final FineDue
        foreach (var ob in rawQueue)
        {
            ob.ContributionPaid = 0;
            ob.FinePaid = 0;
            ob.JoinFeePaid = 0;
        }

        foreach (var entry in ledgerEntries.OrderBy(e => e.CreatedAt))
        {
            decimal remaining = entry.Amount;
            foreach (var ob in rawQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;

                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.FineOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.FineOutstanding);
                    ob.FinePaid += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        return rawQueue.OrderBy(o => o.DueDate).ToList();
#endif
    }

    /// <summary>
    /// Allocate a new payment amount to outstanding queue oldest-first.
    /// Returns breakdown: contribution, fine, join fee, savings.
    /// </summary>
    public AllocationBreakdown AllocatePaymentToQueue(decimal paymentAmount, List<ObligationItem> outstandingQueue)
    {
        var breakdown = new AllocationBreakdown { PaymentAmount = paymentAmount };
        decimal remaining = paymentAmount;

        foreach (var ob in outstandingQueue.OrderBy(o => o.DueDate))
        {
            if (remaining <= 0) break;

            if (ob.ContributionOutstanding > 0)
            {
                var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                breakdown.ContributionAllocated += alloc;
                breakdown.Details.Add(new AllocationDetail
                {
                    Year = ob.Year,
                    Month = ob.Month,
                    Target = "Contribution",
                    Amount = alloc,
                    DueDate = ob.DueDate
                });
                remaining -= alloc;
            }
            if (remaining <= 0) break;

            if (ob.FineOutstanding > 0)
            {
                var alloc = Math.Min(remaining, ob.FineOutstanding);
                breakdown.FineAllocated += alloc;
                breakdown.Details.Add(new AllocationDetail
                {
                    Year = ob.Year,
                    Month = ob.Month,
                    Target = "Fine",
                    Amount = alloc,
                    DueDate = ob.DueDate
                });
                remaining -= alloc;
            }
            if (remaining <= 0) break;

            if (ob.JoinFeeOutstanding > 0)
            {
                var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                breakdown.JoinFeeAllocated += alloc;
                breakdown.Details.Add(new AllocationDetail
                {
                    Year = ob.Year,
                    Month = ob.Month,
                    Target = "JoiningFee",
                    Amount = alloc,
                    DueDate = ob.DueDate
                });
                remaining -= alloc;
            }
        }

        if (remaining > 0)
        {
            breakdown.SavingsAllocated = remaining;
            breakdown.Details.Add(new AllocationDetail
            {
                Year = 0,
                Month = 0,
                Target = "Savings",
                Amount = remaining,
                DueDate = DateTime.UtcNow
            });
        }

        return breakdown;
    }
}

public class AllocationBreakdown
{
    public decimal PaymentAmount { get; set; }
    public decimal ContributionAllocated { get; set; }
    public decimal FineAllocated { get; set; }
    public decimal JoinFeeAllocated { get; set; }
    public decimal SavingsAllocated { get; set; }
    public List<AllocationDetail> Details { get; set; } = new();
}

public class AllocationDetail
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Target { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
}
