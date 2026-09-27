using ChamaLink.Application.DTOs;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Reconciliation Engine V2 — proves M-Koba Statement = Ledger = Monthly Statement
/// This is what makes treasurers trust the system.
/// </summary>
public class ReconciliationServiceV2
{
    private readonly ApplicationDbContext _context;
    private readonly AllocationEngine _allocationEngine;
    private readonly BusinessRuleEngine _ruleEngine;
    private readonly ObligationLedgerService _obligationLedgerService;

    public ReconciliationServiceV2(ApplicationDbContext context, AllocationEngine allocationEngine, BusinessRuleEngine ruleEngine, ObligationLedgerService obligationLedgerService)
    {
        _context = context;
        _allocationEngine = allocationEngine;
        _ruleEngine = ruleEngine;
        _obligationLedgerService = obligationLedgerService;
    }

    public async Task<ReconciliationResultDto> ReconcileMemberMonthAsync(Guid groupId, Guid memberId, int year, int month)
    {
        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) throw new InvalidOperationException("Member not found");

        var policy = await _ruleEngine.GetActivePolicyAsync(groupId, new DateTime(year, month, 1));

        // 1. M-Koba imported amount for this month
        var mkobaAmount = await _context.LedgerEntries
            .Where(l => l.GroupId == groupId && l.UserId == member.UserId && l.CreatedAt.Year == year && l.CreatedAt.Month == month)
            .Where(l => l.ReferenceNo != null && (l.ReferenceNo.StartsWith("MKOBA-") || l.ReferenceNo.StartsWith("TREAS-")))
            .SumAsync(l => (decimal?)l.Amount) ?? 0m;

        // 2. Ledger total for this month (all sources)
        var ledgerAmount = await _context.LedgerEntries
            .Where(l => l.GroupId == groupId && l.UserId == member.UserId && l.CreatedAt.Year == year && l.CreatedAt.Month == month)
            .SumAsync(l => (decimal?)l.Amount) ?? 0m;

        // 3. Expected from policy
        decimal expectedContribution = policy.MonthlyContribution;
        var activeLoans = await _context.Loans.Where(l => l.GroupMemberId == memberId && l.Status == LoanStatus.Active).ToListAsync();
        decimal expectedLoan = 0m;
        foreach (var loan in activeLoans)
        {
            expectedLoan += GetExpectedRepayment(loan, year, month);
        }
        decimal expectedTotal = expectedContribution + expectedLoan;

        // 4. Allocated via AllocationEngine oldest-first logic
        var outstandingQueue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(member.Id, policy, new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59, DateTimeKind.Utc));
        var allocation = _allocationEngine.AllocateOldestFirst(mkobaAmount, new DateTime(year, month, 1), outstandingQueue, policy, activeLoans, year, month);

        decimal allocatedTotal = allocation.Allocations.Sum(a => a.Amount);

        var result = new ReconciliationResultDto
        {
            GroupId = groupId,
            MemberId = memberId,
            MembershipNumber = member.MemberNumber ?? "",
            FullName = member.User?.FullName ?? "",
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1).ToString("MMMM yyyy"),
            MkobaImported = mkobaAmount,
            LedgerTotal = ledgerAmount,
            ExpectedTotal = expectedTotal,
            AllocatedTotal = allocatedTotal,
            Difference = mkobaAmount - allocatedTotal,
            IsBalanced = Math.Abs(mkobaAmount - allocatedTotal) < 0.01m && Math.Abs(ledgerAmount - allocatedTotal) < 0.01m
        };

        if (!result.IsBalanced)
        {
            if (Math.Abs(mkobaAmount - ledgerAmount) > 0.01m)
                result.Reasons.Add($"Ledger total ({ledgerAmount:N0}) != M-Koba imported ({mkobaAmount:N0}) — duplicate or manual entry exists");
            if (Math.Abs(mkobaAmount - allocatedTotal) > 0.01m)
                result.Reasons.Add($"Allocated ({allocatedTotal:N0}) != Imported ({mkobaAmount:N0}) — allocation logic mismatch");
            if (mkobaAmount == 0 && expectedTotal > 0)
                result.Reasons.Add($"No payment imported but expected {expectedTotal:N0} — missed contribution");
        }
        else
        {
            result.Reasons.Add("Balanced — M-Koba = Ledger = Allocated");
        }

        return result;
    }

    public async Task<List<ReconciliationResultDto>> ReconcileGroupMonthAsync(Guid groupId, int year, int month)
    {
        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync();
        var results = new List<ReconciliationResultDto>();

        foreach (var member in members)
        {
            var result = await ReconcileMemberMonthAsync(groupId, member.Id, year, month);
            results.Add(result);
        }

        return results;
    }

    private decimal GetExpectedRepayment(Loan loan, int year, int month)
    {
        try { return LoanService.GetExpectedRepaymentForMonth(loan, year, month); }
        catch { return 0m; }
    }
}

public class ReconciliationResultDto
{
    public Guid GroupId { get; set; }
    public Guid MemberId { get; set; }
    public string MembershipNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal MkobaImported { get; set; }
    public decimal LedgerTotal { get; set; }
    public decimal ExpectedTotal { get; set; }
    public decimal AllocatedTotal { get; set; }
    public decimal Difference { get; set; }
    public bool IsBalanced { get; set; }
    public List<string> Reasons { get; set; } = new();
}
