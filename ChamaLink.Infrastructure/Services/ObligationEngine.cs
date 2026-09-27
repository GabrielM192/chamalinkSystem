using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Monthly Obligation Engine
/// Kazi: Kutengeneza madeni yanayotakiwa kuwepo kabla ya kupokea malipo
/// Fix: Hakuna hardcoded Tunganege, hakuna Ukonga special case mara mbili, fine logic ya kweli
/// </summary>
public class ObligationEngine
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _ruleEngine;
    private readonly ObligationLedgerService _obligationLedgerService;

    public ObligationEngine(ApplicationDbContext context, BusinessRuleEngine ruleEngine, ObligationLedgerService obligationLedgerService)
    {
        _context = context;
        _ruleEngine = ruleEngine;
        _obligationLedgerService = obligationLedgerService;
    }

    /// <summary>
    /// Pata Effective Join Date — single source of truth, inatumika kila mahali
    /// </summary>
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
        {
            effective = new DateTime(earliestLedger.CreatedAt.Year, earliestLedger.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        return effective;
    }

    /// <summary>
    /// Tengeneza obligations zote za mwanachama kutoka EffectiveJoinDate hadi asOf
    /// FIXED25: DueDate = next month + DueDateDay (e.g. Jan due Feb 5), not 10th of same month.
    /// Uses ObligationLedgerService as single source.
    /// </summary>
    public async Task<List<MonthlyObligationDto>> GenerateObligationsAsync(Guid memberId, DateTime? asOf = null)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);
        else if (asOfDate.Kind == DateTimeKind.Local)
            asOfDate = asOfDate.ToUniversalTime();

        // Use single source ObligationLedgerService
        var queue = await _obligationLedgerService.GetOutstandingQueueAsync(memberId, asOfDate);

        var obligations = new List<MonthlyObligationDto>();
        foreach (var ob in queue)
        {
            obligations.Add(new MonthlyObligationDto
            {
                Year = ob.Year,
                Month = ob.Month,
                MonthName = ob.MonthName,
                ContributionDue = ob.ContributionDue,
                JoinFeeDue = ob.JoinFeeDue,
                FineDue = ob.FineDue,
                LoanDue = 0m,
                TotalDue = ob.TotalDue,
                DueDate = ob.DueDate,
                IsOverdue = ob.IsOverdue,
                PaidInMonth = ob.TotalPaid,
                IsPaid = ob.IsFullyPaid
            });
        }

        return obligations;
    }

    /// <summary>
    /// Member Truth Table — generic, no hardcoded Tunganege, uses ObligationLedgerService
    /// No name-based branching, no hardcoded 10k/50k fixture in production
    /// </summary>
    public async Task<TunganegeTruthTableDto> GetTunganegeTruthAsync(Guid memberId)
    {
        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        // Use single source queue for obligations
        var queue = await _obligationLedgerService.GetOutstandingQueueAsync(memberId, DateTime.UtcNow);
        var obligations = queue.Select(q => new MonthlyObligationDto
        {
            Year = q.Year,
            Month = q.Month,
            MonthName = q.MonthName,
            ContributionDue = q.ContributionDue,
            JoinFeeDue = q.JoinFeeDue,
            FineDue = q.FineDue,
            TotalDue = q.TotalDue,
            DueDate = q.DueDate,
            IsOverdue = q.IsOverdue
        }).ToList();

        var months = new List<TunganegeMonthDto>();
        decimal totalPaid = 0m, totalContribution = 0m, totalJoinFee = 0m, totalFinePaid = 0m, totalSavings = 0m;

        // Use ObligationLedgerService allocation logic oldest-first
        var tempQueue = queue.Select(q => new ObligationItem
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

        foreach (var entry in ledgerEntries)
        {
            decimal remaining = entry.Amount;
            decimal contribAllocated = 0m, joinFeeAllocated = 0m, fineAllocated = 0m, savingsAllocated = 0m;

            foreach (var ob in tempQueue.OrderBy(o => o.DueDate))
            {
                if (remaining <= 0) break;
                if (ob.ContributionOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.ContributionOutstanding);
                    ob.ContributionPaid += alloc;
                    contribAllocated += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.FineOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.FineOutstanding);
                    ob.FinePaid += alloc;
                    fineAllocated += alloc;
                    remaining -= alloc;
                }
                if (remaining <= 0) break;
                if (ob.JoinFeeOutstanding > 0)
                {
                    var alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                    ob.JoinFeePaid += alloc;
                    joinFeeAllocated += alloc;
                    remaining -= alloc;
                }
            }

            if (remaining > 0)
                savingsAllocated = remaining;

            months.Add(new TunganegeMonthDto
            {
                Month = entry.CreatedAt.ToString("MMM yyyy"),
                ReferenceNo = entry.ReferenceNo ?? "",
                Paid = entry.Amount,
                Contribution = contribAllocated,
                JoinFee = joinFeeAllocated,
                FinePaid = fineAllocated,
                Savings = savingsAllocated,
                Note = entry.Description ?? "",
                CreatedAt = entry.CreatedAt
            });

            totalPaid += entry.Amount;
            totalContribution += contribAllocated;
            totalJoinFee += joinFeeAllocated;
            totalFinePaid += fineAllocated;
            totalSavings += savingsAllocated;
        }

        // No hardcoded fallback for Tunganege - if no ledger, return empty truth from queue
        if (!ledgerEntries.Any())
        {
            return new TunganegeTruthTableDto
            {
                MemberId = memberId,
                FullName = member.User?.FullName ?? "Unknown",
                Months = new List<TunganegeMonthDto>(),
                TotalPaid = 0,
                TotalContribution = 0,
                TotalJoinFee = 0,
                TotalFinePaid = 0,
                TotalSavings = 0,
                IsBusinessTruth = false
            };
        }

        return new TunganegeTruthTableDto
        {
            MemberId = memberId,
            FullName = member.User?.FullName ?? "Unknown",
            Months = months,
            TotalPaid = totalPaid,
            TotalContribution = totalContribution,
            TotalJoinFee = totalJoinFee,
            TotalFinePaid = totalFinePaid,
            TotalSavings = totalSavings,
            IsBusinessTruth = false
        };
    }

    /// <summary>
    /// Fixture ya kubuni imeondolewa kabisa; ukweli unatoka ContributionTruthEngine.
    /// This method now throws - production code must not contain name-based fixtures.
    /// </summary>
    [Obsolete("Imeondolewa - tumia ContributionTruthEngine")]
    public TunganegeTruthTableDto GetTunganegeBusinessTruthFixture(Guid memberId, string fullName)
    {
        throw new NotSupportedException("GetTunganegeBusinessTruthFixture imeondolewa; tumia ContributionTruthEngine. Production code must not contain name-based fixtures.");
    }
}

public class MonthlyObligationDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public decimal ContributionDue { get; set; }
    public decimal JoinFeeDue { get; set; }
    public decimal FineDue { get; set; }
    public decimal LoanDue { get; set; }
    public decimal TotalDue { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public decimal PaidInMonth { get; set; }
    public bool IsPaid { get; set; }
}

public class TunganegeTruthTableDto
{
    public Guid MemberId { get; set; }
    public string FullName { get; set; } = "";
    public List<TunganegeMonthDto> Months { get; set; } = new();
    public decimal TotalPaid { get; set; }
    public decimal TotalContribution { get; set; }
    public decimal TotalJoinFee { get; set; }
    public decimal TotalFinePaid { get; set; }
    public decimal TotalSavings { get; set; }
    public bool IsBusinessTruth { get; set; }
}

public class TunganegeMonthDto
{
    public string Month { get; set; } = "";
    public string ReferenceNo { get; set; } = "";
    public decimal Paid { get; set; }
    public decimal Contribution { get; set; }
    public decimal JoinFee { get; set; }
    public decimal FinePaid { get; set; }
    public decimal Savings { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
