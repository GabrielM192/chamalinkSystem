using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Historical Ledger Reclassification using ObligationLedgerService as single source.
/// Consistency: TotalOriginal == TotalContribution+Join+Fine+Savings+Loan, MonthlyStatement matches.
/// No hardcoded 10000m/50000m, uses GroupPolicy only. Oldest-first allocation.
/// </summary>
public class LedgerReclassificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ObligationLedgerService _obligationLedgerService;
    private readonly BusinessRuleEngine _ruleEngine;

    public LedgerReclassificationService(ApplicationDbContext context, ObligationLedgerService obligationLedgerService, BusinessRuleEngine ruleEngine)
    {
        _context = context;
        _obligationLedgerService = obligationLedgerService;
        _ruleEngine = ruleEngine;
    }

    public class ReclassPreviewDto
    {
        public Guid MemberId { get; set; }
        public string FullName { get; set; } = "";
        public int TotalOriginalEntries { get; set; }
        public decimal TotalOriginalAmount { get; set; }
        public List<ReclassEntryDto> OriginalEntries { get; set; } = new();
        public List<ReclassEntryDto> ReclassifiedEntries { get; set; } = new();
        public decimal TotalContribution { get; set; }
        public decimal TotalJoinFee { get; set; }
        public decimal TotalFine { get; set; }
        public decimal TotalSavings { get; set; }
        public decimal TotalLoanRepayment { get; set; }
        public bool IsBalanced { get; set; }
        public decimal Difference { get; set; }
        public List<string> Notes { get; set; } = new();
        public int ExpectedMonths { get; set; }
        public decimal ExpectedTotal { get; set; }
        public decimal CompliancePercent { get; set; }
        public List<MonthlyStatementDto> MonthlyStatement { get; set; } = new();
        public Dictionary<string, decimal> PaidPerMonth { get; set; } = new();
        public Dictionary<string, List<AllocationSplitDto>> AllocationPerMonth { get; set; } = new();
        public List<ObligationItem> ObligationQueue { get; set; } = new();
    }

    public class ReclassEntryDto
    {
        public string ReferenceNo { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public decimal OriginalAmount { get; set; }
        public string OriginalType { get; set; } = "";
        public List<AllocationSplitDto> Splits { get; set; } = new();
    }

    public class AllocationSplitDto
    {
        public string Target { get; set; } = "";
        public TransactionType NewType { get; set; }
        public decimal Amount { get; set; }
        public string Month { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime DueDate { get; set; }
    }

    public class MonthlyStatementDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public decimal Obligation { get; set; }
        public decimal FineDue { get; set; }
        public decimal JoinFeeDue { get; set; }
        public decimal Paid { get; set; }
        public string Allocation { get; set; } = "";
        public decimal Balance { get; set; }
        public string Status { get; set; } = "";
        public List<AllocationSplitDto> Details { get; set; } = new();
        public DateTime DueDate { get; set; }
    }

    public async Task<ReclassPreviewDto> PreviewAsync(Guid memberId, DateTime? asOf = null)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);

        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        // GroupPolicy is only source, no fallback
        GroupPolicy policy;
        try
        {
            policy = await _ruleEngine.GetActivePolicyAsync(member.GroupId, asOfDate);
        }
        catch (Exception ex)
        {
            throw new PolicyNotConfiguredException(member.GroupId, $"Policy not configured for group {member.GroupId}", ex);
        }

        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.CreatedAt <= asOfDate)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        // Use ObligationLedgerService as single source for consistency
        var rawQueue = await _obligationLedgerService.GenerateRawQueueWithPolicyAsync(memberId, policy, asOfDate);
        var dueDateDay = _obligationLedgerService.GetDueDateDay(policy);

        // Build mutable queue for allocation simulation (oldest-first)
        var workingQueue = rawQueue.Select(q => new ObligationItem
        {
            Year = q.Year,
            Month = q.Month,
            MonthName = q.MonthName,
            DueDate = q.DueDate,
            IsFirstMonth = q.IsFirstMonth,
            ContributionDue = q.ContributionDue,
            ContributionPaid = 0,
            FineDue = 0, // will be set based on overdue logic
            FinePaid = 0,
            JoinFeeDue = q.JoinFeeDue,
            JoinFeePaid = 0,
            IsOverdue = q.IsOverdue
        }).ToList();

        // First pass: allocate to contributions to determine overdue.
        // JoinFee must run as its own separate sweep AFTER every obligation's
        // Contribution is tried (mirrors AllocationEngine.AllocateOldestFirst).
        // Previously this pass paid JoinFee BEFORE Contribution
        // For each obligation in turn — the same money-stealing bug fixed
        // in AllocationEngine, just reproduced independently here since this
        // service has always kept its own separate copy of the waterfall instead
        // of calling AllocationEngine.
        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;
            foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    remaining -= alloc;
                }
            }
            foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        // Determine fines for overdue unpaid
        foreach (var ob in workingQueue)
        {
            if (ob.IsOverdue && ob.ContributionOutstanding > 0.01m)
                ob.FineDue = policy.LateFine;
        }

        // Reset and re-allocate with fines included (oldest-first, contrib+fine paired)
        foreach (var ob in workingQueue)
        {
            ob.ContributionPaid = 0;
            ob.JoinFeePaid = 0;
            ob.FinePaid = 0;
        }

        // same reordering — Contribution+Fine (paired) for every
        // obligation first, JoinFee only from what survives, as its own sweep.
        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;
            foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
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
            }
            foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    remaining -= alloc;
                }
            }
        }

        // Timing check for fines (if paid after grace, fine still due)
        foreach (var ob in workingQueue)
        {
            if (!ob.IsOverdue) continue;
            var graceDeadline = ob.DueDate.AddDays(policy.GracePeriodDays);
            var paymentsBeforeGrace = ledgerEntries.Where(l => l.CreatedAt <= graceDeadline).Sum(l => l.Amount);
            var neededBefore = workingQueue.Where(o => o.DueDate < ob.DueDate).Sum(o => o.ContributionDue + o.JoinFeeDue);
            var availableForThis = paymentsBeforeGrace - neededBefore;
            if (availableForThis < ob.ContributionDue - 0.01m)
                ob.FineDue = policy.LateFine;
            else if (ob.FinePaid == 0)
                ob.FineDue = 0;
        }

        // Final allocation from scratch with final FineDue
        foreach (var ob in workingQueue)
        {
            ob.ContributionPaid = 0;
            ob.FinePaid = 0;
            ob.JoinFeePaid = 0;
        }

        var reclassified = new List<ReclassEntryDto>();
        var paidPerMonth = new Dictionary<string, decimal>();
        var allocationPerMonth = new Dictionary<string, List<AllocationSplitDto>>();
        decimal totalContrib = 0m, totalJoin = 0m, totalFine = 0m, totalSavings = 0m, totalLoan = 0m;

        foreach (var entry in ledgerEntries.OrderBy(e => e.CreatedAt))
        {
            decimal remaining = entry.Amount;
            var reclassEntry = new ReclassEntryDto
            {
                ReferenceNo = entry.ReferenceNo ?? "",
                CreatedAt = entry.CreatedAt,
                OriginalAmount = entry.Amount,
                OriginalType = entry.Type.ToString(),
                Splits = new List<AllocationSplitDto>()
            };

            // Contribution+Fine and JoinFee are now two separate full
            // sweeps over workingQueue, matching AllocationEngine.AllocateOldestFirst
            // instead of interleaving JoinFee inside the same per-month
            // iteration. JoinFeeMode decides which sweep runs first, per that
            // group's own configured rule (no hardcoded ordering).
            void ContributionAndFineSweep()
            {
                foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
                {
                    if (remaining <= 0) break;

                    if (ob.ContributionOutstanding > 0)
                    {
                        var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                        var split = new AllocationSplitDto
                        {
                            Target = "Contribution",
                            NewType = TransactionType.Contribution,
                            Amount = alloc,
                            Month = ob.MonthName,
                            DueDate = ob.DueDate,
                            Note = $"Contribution {ob.MonthName} due {ob.DueDate:yyyy-MM-dd} (oldest-first, policy {policy.MonthlyContribution:N0})"
                        };
                        reclassEntry.Splits.Add(split);
                        AddToMonthlyAllocation(allocationPerMonth, ob.MonthName, split);
                        AddPaid(paidPerMonth, ob.MonthName, alloc);
                        ob.ContributionPaid += alloc;
                        remaining -= alloc;
                        totalContrib += alloc;
                    }
                    if (remaining <= 0) break;

                    if (ob.FineOutstanding > 0)
                    {
                        var alloc = Math.Min(remaining, ob.FineOutstanding);
                        var split = new AllocationSplitDto
                        {
                            Target = "Fine",
                            NewType = TransactionType.FinePayment,
                            Amount = alloc,
                            Month = ob.MonthName,
                            DueDate = ob.DueDate,
                            Note = $"Fine {ob.MonthName} paired with contrib, LateFine {policy.LateFine:N0}"
                        };
                        reclassEntry.Splits.Add(split);
                        AddToMonthlyAllocation(allocationPerMonth, ob.MonthName, split);
                        AddPaid(paidPerMonth, ob.MonthName, alloc);
                        ob.FinePaid += alloc;
                        remaining -= alloc;
                        totalFine += alloc;
                    }
                }
            }

            void JoinFeeSweep()
            {
                foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
                {
                    if (remaining <= 0) break;

                    if (ob.JoinFeeOutstanding > 0)
                    {
                        var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                        var split = new AllocationSplitDto
                        {
                            Target = "JoiningFee",
                            NewType = TransactionType.JoiningFee,
                            Amount = alloc,
                            Month = ob.MonthName,
                            DueDate = ob.DueDate,
                            Note = $"JoiningFee {ob.MonthName} policy {policy.JoiningFee:N0}"
                        };
                        reclassEntry.Splits.Add(split);
                        // Join fee not in monthly allocation per se, but track
                        ob.JoinFeePaid += alloc;
                        remaining -= alloc;
                        totalJoin += alloc;
                    }
                }
            }

            if (policy.JoinFeeMode == JoinFeeMode.MustCompleteBeforeContributing)
            {
                JoinFeeSweep();
                ContributionAndFineSweep();
            }
            else
            {
                ContributionAndFineSweep();
                JoinFeeSweep();
            }

            if (remaining > 0)
            {
                var split = new AllocationSplitDto
                {
                    Target = "Savings",
                    NewType = TransactionType.Savings,
                    Amount = remaining,
                    Month = entry.CreatedAt.ToString("MMM yyyy"),
                    DueDate = entry.CreatedAt,
                    Note = "Excess to savings after oldest-first allocation"
                };
                reclassEntry.Splits.Add(split);
                totalSavings += remaining;
                remaining = 0;
            }

            reclassified.Add(reclassEntry);
        }

        // Monthly statement from workingQueue (now fully allocated)
        var monthlyStatement = new List<MonthlyStatementDto>();
        foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
        {
            var paid = paidPerMonth.TryGetValue(ob.MonthName, out var p) ? p : 0m;
            var allocs = allocationPerMonth.TryGetValue(ob.MonthName, out var list) ? list : new List<AllocationSplitDto>();
            string allocStr = allocs.Count > 0 ? string.Join(" + ", allocs.Select(a => $"{a.Target} {a.Amount:N0} ({a.Month})")) : (paid > 0 ? $"Contribution {paid:N0}" : "Outstanding");
            decimal balance = ob.TotalDue - ob.TotalPaid;
            string status = ob.IsFullyPaid ? "Paid" : (ob.TotalPaid > 0 ? "PartiallyPaid" : "Missed");

            monthlyStatement.Add(new MonthlyStatementDto
            {
                Year = ob.Year,
                Month = ob.Month,
                MonthName = ob.MonthName,
                Obligation = ob.ContributionDue,
                FineDue = ob.FineDue,
                JoinFeeDue = ob.JoinFeeDue,
                Paid = ob.TotalPaid,
                Allocation = allocStr,
                Balance = balance,
                Status = status,
                Details = allocs,
                DueDate = ob.DueDate
            });
        }

        decimal totalOriginal = ledgerEntries.Sum(e => e.Amount);
        decimal totalReclassified = totalContrib + totalJoin + totalFine + totalSavings + totalLoan;
        bool isBalanced = Math.Abs(totalOriginal - totalReclassified) < 1m;

        var originalEntriesDto = ledgerEntries.Select(e => new ReclassEntryDto
        {
            ReferenceNo = e.ReferenceNo ?? "",
            CreatedAt = e.CreatedAt,
            OriginalAmount = e.Amount,
            OriginalType = e.Type.ToString(),
            Splits = new List<AllocationSplitDto> { new AllocationSplitDto { Target = e.Type.ToString(), NewType = e.Type, Amount = e.Amount, Month = e.CreatedAt.ToString("MMM yyyy"), Note = e.Description ?? "", DueDate = e.CreatedAt } }
        }).ToList();

        decimal expectedTotal = workingQueue.Sum(q => q.ContributionDue);
        decimal compliance = expectedTotal > 0 ? totalContrib / expectedTotal * 100 : 0;

        return new ReclassPreviewDto
        {
            MemberId = memberId,
            FullName = member.User?.FullName ?? "Unknown",
            TotalOriginalEntries = ledgerEntries.Count,
            TotalOriginalAmount = totalOriginal,
            OriginalEntries = originalEntriesDto,
            ReclassifiedEntries = reclassified,
            TotalContribution = totalContrib,
            TotalJoinFee = totalJoin,
            TotalFine = totalFine,
            TotalSavings = totalSavings,
            TotalLoanRepayment = totalLoan,
            IsBalanced = isBalanced,
            Difference = totalOriginal - totalReclassified,
            Notes = new List<string>
            {
                $"ObligationLedgerService single source, DueDate = next month + {dueDateDay} (policy {policy.MonthlyContribution:N0}, Join {policy.JoiningFee:N0}, Fine {policy.LateFine:N0})",
                $"Policy Version {policy.Version}, Grace {policy.GracePeriodDays} days, Strategy {policy.AllocationStrategy}",
                $"Waterfall: Oldest DueDate first, Contribution+Fine paired per month, JoinFee, Savings last",
                $"Consistency: Original {totalOriginal:N0} == Reclassified {totalReclassified:N0} ? {isBalanced}, Diff {totalOriginal - totalReclassified:N0}",
                $"Expected {workingQueue.Count} months, Contrib {expectedTotal:N0}, Paid {totalContrib:N0}, Compliance {compliance:N2}%",
                $"JoinFee paid {totalJoin:N0} / {policy.JoiningFee:N0}, Fine paid {totalFine:N0}, Savings {totalSavings:N0}",
                $"No hardcoded literals, uses GroupPolicy only condition #1, #3)",
                $"JoinFee allocated as its own sweep ({(policy.JoinFeeMode == JoinFeeMode.MustCompleteBeforeContributing ? "before" : "after")} Contribution+Fine, per JoinFeeMode={policy.JoinFeeMode}) — no longer competes with a later month's Contribution inside the same pass"
            },
            ExpectedMonths = workingQueue.Count,
            ExpectedTotal = expectedTotal,
            CompliancePercent = Math.Round(compliance, 2),
            MonthlyStatement = monthlyStatement,
            PaidPerMonth = paidPerMonth,
            AllocationPerMonth = allocationPerMonth,
            ObligationQueue = workingQueue
        };
    }

    private void AddPaid(Dictionary<string, decimal> dict, string month, decimal amount)
    {
        if (!dict.ContainsKey(month)) dict[month] = 0;
        dict[month] += amount;
    }

    private void AddToMonthlyAllocation(Dictionary<string, List<AllocationSplitDto>> dict, string month, AllocationSplitDto split)
    {
        if (!dict.ContainsKey(month)) dict[month] = new List<AllocationSplitDto>();
        dict[month].Add(split);
    }

    public async Task<ReclassPreviewDto> ApplyAsync(Guid memberId, DateTime? asOf = null, bool dryRun = true)
    {
        var preview = await PreviewAsync(memberId, asOf);
        if (dryRun) return preview;

        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId) ?? throw new InvalidOperationException("Member not found");
        var ledgerEntries = await _context.LedgerEntries.Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId).OrderBy(l => l.CreatedAt).ToListAsync();

        // Consistency check before apply
        if (!preview.IsBalanced)
            throw new InvalidOperationException($"Cannot apply: not balanced, diff {preview.Difference}");

        for (int i = 0; i < ledgerEntries.Count && i < preview.ReclassifiedEntries.Count; i++)
        {
            var original = ledgerEntries[i];
            var reclass = preview.ReclassifiedEntries[i];

            if (reclass.Splits.Count == 1)
            {
                original.Type = reclass.Splits[0].NewType;
                original.Description = $"{reclass.Splits[0].Note} | Original: {original.Description} | Reclassified with policy consistency";
            }
            else if (reclass.Splits.Count > 1)
            {
                var first = reclass.Splits[0];
                original.Type = first.NewType;
                original.Amount = first.Amount;
                original.Description = $"{first.Note} (Split 1/{reclass.Splits.Count}, | Original: {original.Description}";

                for (int s = 1; s < reclass.Splits.Count; s++)
                {
                    var split = reclass.Splits[s];
                    var newEntry = new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = original.GroupId,
                        UserId = original.UserId,
                        AccountId = original.AccountId,
                        Type = split.NewType,
                        Amount = split.Amount,
                        Description = $"{split.Note} (Split {s+1}/{reclass.Splits.Count} from {original.ReferenceNo}, ",
                        ReferenceNo = $"{original.ReferenceNo}-SPLIT-{s+1}",
                        CreatedAt = original.CreatedAt.AddSeconds(s),
                    };
                    _context.LedgerEntries.Add(newEntry);
                }
            }
        }

        await _context.SaveChangesAsync();
        return await PreviewAsync(memberId, asOf);
    }

    // Group backfill - reclassify all members in group using single engine
    public async Task<GroupReclassResultDto> ApplyGroupAsync(Guid groupId, DateTime? asOf = null, bool dryRun = true)
    {
        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync();
        var results = new List<ReclassPreviewDto>();
        int balanced = 0, unbalanced = 0;
        decimal totalOriginal = 0m, totalReclassified = 0m;

        foreach (var member in members)
        {
            try
            {
                var preview = await PreviewAsync(member.Id, asOf);
                results.Add(preview);
                totalOriginal += preview.TotalOriginalAmount;
                totalReclassified += preview.TotalContribution + preview.TotalJoinFee + preview.TotalFine + preview.TotalSavings + preview.TotalLoanRepayment;
                if (preview.IsBalanced) balanced++; else unbalanced++;

                if (!dryRun && preview.IsBalanced)
                {
                    await ApplyAsync(member.Id, asOf, dryRun: false);
                }
            }
            catch (Exception ex)
            {
                results.Add(new ReclassPreviewDto
                {
                    MemberId = member.Id,
                    FullName = $"Error: {ex.Message}",
                    IsBalanced = false,
                    Difference = 0,
                    Notes = new List<string> { ex.Message }
                });
                unbalanced++;
            }
        }

        return new GroupReclassResultDto
        {
            GroupId = groupId,
            TotalMembers = members.Count,
            BalancedMembers = balanced,
            UnbalancedMembers = unbalanced,
            TotalOriginalAmount = totalOriginal,
            TotalReclassifiedAmount = totalReclassified,
            IsGroupBalanced = Math.Abs(totalOriginal - totalReclassified) < 1m,
            Difference = totalOriginal - totalReclassified,
            MemberResults = results,
            DryRun = dryRun
        };
    }

    public class GroupReclassResultDto
    {
        public Guid GroupId { get; set; }
        public int TotalMembers { get; set; }
        public int BalancedMembers { get; set; }
        public int UnbalancedMembers { get; set; }
        public decimal TotalOriginalAmount { get; set; }
        public decimal TotalReclassifiedAmount { get; set; }
        public bool IsGroupBalanced { get; set; }
        public decimal Difference { get; set; }
        public List<ReclassPreviewDto> MemberResults { get; set; } = new();
        public bool DryRun { get; set; }
    }
}
