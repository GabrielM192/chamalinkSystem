using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Ledger Classification Audit uses ObligationLedgerService for consistency)
/// Kazi: Kuchunguza kila transaction ya zamani na kutambua ilipaswa kugawanywa vipi
/// Consistency: Uses same queue as FinancialPositionService and ReclassificationService
/// </summary>
public class LedgerClassificationService
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _ruleEngine;
    private readonly ObligationLedgerService _obligationLedgerService;

    public LedgerClassificationService(ApplicationDbContext context, BusinessRuleEngine ruleEngine, ObligationLedgerService obligationLedgerService)
    {
        _context = context;
        _ruleEngine = ruleEngine;
        _obligationLedgerService = obligationLedgerService;
    }

    public async Task<MemberClassificationAuditDto> AuditMemberAsync(Guid memberId)
    {
        var member = await _context.GroupMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        GroupPolicy policy;
        try
        {
            policy = await _ruleEngine.GetActivePolicyAsync(member.GroupId, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            throw new PolicyNotConfiguredException(member.GroupId, "Policy not configured", ex);
        }

        // Use ObligationLedgerService queue for consistency
        var queue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(memberId, policy, DateTime.UtcNow);

        var audits = new List<TransactionClassificationDto>();
        decimal totalReceived = ledgerEntries.Sum(e => e.Amount);
        decimal totalCurrentContribution = ledgerEntries.Where(l => l.Type == TransactionType.Contribution).Sum(l => l.Amount);

        // Use reclassification logic for should-be
        var workingQueue = queue.Select(q => new ObligationItem
        {
            Year = q.Year,
            Month = q.Month,
            MonthName = q.MonthName,
            DueDate = q.DueDate,
            ContributionDue = q.ContributionDue,
            ContributionPaid = 0,
            FineDue = q.FineDue,
            FinePaid = 0,
            JoinFeeDue = q.JoinFeeDue,
            JoinFeePaid = 0,
            IsOverdue = q.IsOverdue
        }).ToList();

        decimal totalShouldBeContribution = 0m;
        decimal totalShouldBeSavings = 0m;
        decimal totalShouldBeJoining = 0m;
        decimal totalShouldBeFine = 0m;

        foreach (var entry in ledgerEntries.OrderBy(e => e.CreatedAt))
        {
            decimal remaining = entry.Amount;
            decimal shouldContrib = 0m, shouldSavings = 0m, shouldJoin = 0m, shouldFine = 0m;

            foreach (var ob in workingQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    shouldContrib += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.FineOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.FineOutstanding);
                    ob.FinePaid += alloc;
                    shouldFine += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    shouldJoin += alloc;
                    remaining -= alloc;
                }
            }

            if (remaining > 0)
                shouldSavings = remaining;

            totalShouldBeContribution += shouldContrib;
            totalShouldBeSavings += shouldSavings;
            totalShouldBeJoining += shouldJoin;
            totalShouldBeFine += shouldFine;

            bool isMisclassified = false;
            if (entry.Type == TransactionType.Contribution && (shouldSavings > 0 || shouldJoin > 0 || shouldFine > 0))
                isMisclassified = true;
            if (entry.Type != TransactionType.Contribution && shouldContrib > 0 && entry.Type != TransactionType.JoiningFee && entry.Type != TransactionType.FinePayment)
                isMisclassified = true;

            audits.Add(new TransactionClassificationDto
            {
                LedgerEntryId = entry.Id,
                ReferenceNo = entry.ReferenceNo ?? "",
                CreatedAt = entry.CreatedAt,
                Amount = entry.Amount,
                CurrentType = entry.Type.ToString(),
                CurrentDescription = entry.Description ?? "",
                ShouldBeContribution = shouldContrib,
                ShouldBeSavings = shouldSavings,
                ShouldBeJoiningFee = shouldJoin,
                ShouldBeFine = shouldFine,
                ShouldBeLoanRepayment = 0m,
                IsMisclassified = isMisclassified,
                Reason = $"Oldest-first allocation using policy {policy.MonthlyContribution:N0}/{policy.JoiningFee:N0}/{policy.LateFine:N0}, Due {workingQueue.FirstOrDefault()?.DueDate:yyyy-MM-dd}",
                ExpectedMonthsBefore = 0,
                ExpectedMonthsCurrent = queue.Count,
                MonthlyTarget = policy.MonthlyContribution,
                ContributionsPaidAfter = (int)(totalShouldBeContribution / policy.MonthlyContribution)
            });
        }

        int misclassifiedCount = audits.Count(a => a.IsMisclassified);
        int expectedNow = queue.Count;
        decimal expectedTotal = queue.Sum(q => q.ContributionDue);

        return new MemberClassificationAuditDto
        {
            MemberId = memberId,
            MemberNumber = member.MemberNumber ?? "",
            FullName = member.User?.FullName ?? "Unknown",
            JoinedAt = member.JoinedAt,
            GroupId = member.GroupId,
            TotalReceived = totalReceived,
            TotalEntries = ledgerEntries.Count,
            ExpectedMonthsNow = expectedNow,
            ExpectedTotalNow = expectedTotal,
            CurrentBreakdown = new BreakdownDto
            {
                Contribution = totalCurrentContribution,
                Savings = ledgerEntries.Where(l => l.Type == TransactionType.Savings).Sum(l => l.Amount),
                JoiningFee = ledgerEntries.Where(l => l.Type == TransactionType.JoiningFee).Sum(l => l.Amount),
                Fine = ledgerEntries.Where(l => l.Type == TransactionType.FinePayment).Sum(l => l.Amount),
                LoanRepayment = ledgerEntries.Where(l => l.Type == TransactionType.LoanRepayment).Sum(l => l.Amount)
            },
            ShouldBeBreakdown = new BreakdownDto
            {
                Contribution = totalShouldBeContribution,
                Savings = totalShouldBeSavings,
                JoiningFee = totalShouldBeJoining,
                Fine = totalShouldBeFine,
                LoanRepayment = 0m
            },
            Difference = totalReceived - (totalShouldBeContribution + totalShouldBeSavings + totalShouldBeJoining + totalShouldBeFine),
            MisclassifiedCount = misclassifiedCount,
            IsClean = misclassifiedCount == 0,
            Transactions = audits,
            ComplianceIssue = $"Consistency: Received {totalReceived:N0} == ShouldBe {totalShouldBeContribution + totalShouldBeSavings + totalShouldBeJoining + totalShouldBeFine:N0} ? {Math.Abs(totalReceived - (totalShouldBeContribution + totalShouldBeSavings + totalShouldBeJoining + totalShouldBeFine)) < 1m}, Policy {policy.MonthlyContribution:N0}, Expected {expectedTotal:N0} ({expectedNow} months), Paid {totalShouldBeContribution:N0}, Compliance { (expectedTotal > 0 ? totalShouldBeContribution/expectedTotal*100 : 0):N1}%"
        };
    }

    public async Task<GroupClassificationAuditDto> AuditGroupAsync(Guid groupId)
    {
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync();
        var audits = new List<MemberClassificationAuditDto>();
        int totalMisclassified = 0;
        int cleanMembers = 0;

        foreach (var member in members)
        {
            var audit = await AuditMemberAsync(member.Id);
            audits.Add(audit);
            totalMisclassified += audit.MisclassifiedCount;
            if (audit.IsClean) cleanMembers++;
        }

        return new GroupClassificationAuditDto
        {
            GroupId = groupId,
            TotalMembers = members.Count,
            MembersChecked = audits.Count,
            CleanMembers = cleanMembers,
            MembersWithIssues = audits.Count - cleanMembers,
            TotalMisclassifiedTransactions = totalMisclassified,
            TotalReceived = audits.Sum(a => a.TotalReceived),
            IsClean = totalMisclassified == 0,
            Members = audits.OrderByDescending(a => a.MisclassifiedCount).ToList()
        };
    }

    private int CalculateExpectedMonths(DateTime joinDate, DateTime asOf)
    {
        if (asOf < joinDate) return 0;
        int months = (asOf.Year - joinDate.Year) * 12 + asOf.Month - joinDate.Month + 1;
        return Math.Max(0, months);
    }
}

public class MemberClassificationAuditDto
{
    public Guid MemberId { get; set; }
    public string MemberNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateTime JoinedAt { get; set; }
    public Guid GroupId { get; set; }
    public decimal TotalReceived { get; set; }
    public int TotalEntries { get; set; }
    public int ExpectedMonthsNow { get; set; }
    public decimal ExpectedTotalNow { get; set; }
    public BreakdownDto CurrentBreakdown { get; set; } = new();
    public BreakdownDto ShouldBeBreakdown { get; set; } = new();
    public decimal Difference { get; set; }
    public int MisclassifiedCount { get; set; }
    public bool IsClean { get; set; }
    public List<TransactionClassificationDto> Transactions { get; set; } = new();
    public string ComplianceIssue { get; set; } = "";
}

public class TransactionClassificationDto
{
    public Guid LedgerEntryId { get; set; }
    public string ReferenceNo { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal Amount { get; set; }
    public string CurrentType { get; set; } = "";
    public string CurrentDescription { get; set; } = "";
    public decimal ShouldBeContribution { get; set; }
    public decimal ShouldBeSavings { get; set; }
    public decimal ShouldBeJoiningFee { get; set; }
    public decimal ShouldBeFine { get; set; }
    public decimal ShouldBeLoanRepayment { get; set; }
    public bool IsMisclassified { get; set; }
    public string Reason { get; set; } = "";
    public int ExpectedMonthsBefore { get; set; }
    public int ExpectedMonthsCurrent { get; set; }
    public decimal MonthlyTarget { get; set; }
    public int ContributionsPaidAfter { get; set; }
}

public class BreakdownDto
{
    public decimal Contribution { get; set; }
    public decimal Savings { get; set; }
    public decimal JoiningFee { get; set; }
    public decimal Fine { get; set; }
    public decimal LoanRepayment { get; set; }
    public decimal Total => Contribution + Savings + JoiningFee + Fine + LoanRepayment;
}

public class GroupClassificationAuditDto
{
    public Guid GroupId { get; set; }
    public int TotalMembers { get; set; }
    public int MembersChecked { get; set; }
    public int CleanMembers { get; set; }
    public int MembersWithIssues { get; set; }
    public int TotalMisclassifiedTransactions { get; set; }
    public decimal TotalReceived { get; set; }
    public bool IsClean { get; set; }
    public List<MemberClassificationAuditDto> Members { get; set; } = new();
}
