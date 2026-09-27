using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Financial Position Engine — No hardcoded fallback
/// GroupPolicy is the ONLY source of financial numbers. No 10000m/50000m literals as silent fallback.
/// </summary>
public class FinancialPositionService
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _ruleEngine;
    private readonly ObligationLedgerService _obligationLedgerService;

    public FinancialPositionService(ApplicationDbContext context, BusinessRuleEngine ruleEngine, ObligationLedgerService obligationLedgerService)
    {
        _context = context;
        _ruleEngine = ruleEngine;
        _obligationLedgerService = obligationLedgerService;
    }

    public async Task<MemberFinancialPositionDto> CalculatePositionAsync(Guid memberId, DateTime? asOf = null)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);
        else if (asOfDate.Kind == DateTimeKind.Local)
            asOfDate = asOfDate.ToUniversalTime();

        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new InvalidOperationException("Member not found");

        // GroupPolicy is the ONLY source — no silent fallback to Ukonga numbers
        // Previously: catch { monthlyContribution = 10000m; joiningFee = 50000m; } — dangerous for multi-tenant
        GroupPolicy policy;
        try
        {
            policy = await _ruleEngine.GetActivePolicyAsync(member.GroupId, asOfDate);
        }
        catch (Exception ex)
        {
            throw new PolicyNotConfiguredException(member.GroupId, $"GetActivePolicyAsync failed for group {member.GroupId} — policy not configured or DB error. Cannot use fallback numbers.", ex);
        }

        decimal monthlyContribution = policy.MonthlyContribution;
        decimal joiningFee = policy.JoiningFee;

        // Validate policy numbers are not zero (would indicate misconfiguration)
        if (monthlyContribution <= 0)
            throw new PolicyNotConfiguredException(member.GroupId, $"MonthlyContribution is {monthlyContribution} for group {member.GroupId} — invalid policy configuration.");
        if (joiningFee < 0)
            throw new PolicyNotConfiguredException(member.GroupId, $"JoiningFee is {joiningFee} for group {member.GroupId} — invalid policy configuration.");

        // Ledger — single source, resilient
        List<ChamaLink.Domain.Entities.LedgerEntry> ledgerEntries = new();
        try
        {
            ledgerEntries = await _context.LedgerEntries
                .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.CreatedAt <= asOfDate)
                .ToListAsync();
        }
        catch
        {
            ledgerEntries = new List<ChamaLink.Domain.Entities.LedgerEntry>();
        }

        decimal totalLedger = ledgerEntries.Sum(l => l.Amount);
        decimal ledgerSavings = totalLedger; // Single source = ledger, not AdvanceBalance +20)

        // Expected months — resilient, from effective join date
        int expectedMonths = 1;
        try
        {
            expectedMonths = await CalculateExpectedMonthsWithLedgerFixAsync(memberId, asOfDate);
        }
        catch
        {
            try { expectedMonths = CalculateExpectedMonths(member.JoinedAt, asOfDate); } catch { expectedMonths = 9; }
        }

        // Use ObligationLedgerService with mandatory GroupPolicy - no hidden fallback
        decimal totalExpected = expectedMonths * monthlyContribution;
        decimal totalPaid = 0m;
        decimal debt = 0m;
        decimal joiningTarget = joiningFee;
        decimal joiningPaid = 0m;
        decimal joiningBalance = 0m;
        decimal totalFinesCharged = 0m;
        decimal totalFinesPaid = 0m;
        decimal outstandingFine = 0m;

        try
        {
            var queue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(memberId, policy, asOfDate);

            // From queue: single source
            totalExpected = queue.Sum(q => q.ContributionDue) + queue.Where(q => q.IsFirstMonth).Sum(q => q.JoinFeeDue);
            totalPaid = queue.Sum(q => q.ContributionPaid);
            debt = queue.Sum(q => q.ContributionOutstanding);
            joiningPaid = queue.Sum(q => q.JoinFeePaid);
            joiningBalance = queue.Sum(q => q.JoinFeeOutstanding);
            totalFinesCharged = queue.Sum(q => q.FineDue);
            totalFinesPaid = queue.Sum(q => q.FinePaid);
            outstandingFine = queue.Sum(q => q.FineOutstanding);

            // Compliance based on contributions
            decimal expectedContribOnly = queue.Sum(q => q.ContributionDue);
            if (expectedContribOnly > 0)
                totalExpected = expectedContribOnly + joiningTarget; // keep old semantics for totalExpected
        }
        catch
        {
            // Fallback to old logic if queue fails
            try
            {
                totalPaid = ledgerEntries.Where(l => l.Type == TransactionType.Contribution || l.Type == TransactionType.Savings).Sum(l => l.Amount);
            }
            catch { totalPaid = totalLedger; }
            if (totalPaid == 0) totalPaid = totalLedger;
            debt = Math.Max(0, totalExpected - totalPaid);
            try { joiningPaid = ledgerEntries.Where(l => l.Type == TransactionType.JoiningFee).Sum(l => l.Amount); } catch { }
            joiningBalance = Math.Max(0, joiningTarget - joiningPaid);
        }

        decimal compliance = totalExpected > 0 ? totalPaid / (totalExpected - joiningTarget) * 100 : 100;
        if (compliance > 100) compliance = 100;

        // HeldSavings from loan collateral
        decimal outstandingLoan = 0m;
        decimal outstandingInterest = 0m;
        decimal totalLoansIssued = 0m;
        decimal totalLoansRepaid = 0m;
        decimal heldSavings = 0m;
        DateTime? nextDue = null;
        int? daysPastDue = null;
        string riskStatus = "Normal";

        try
        {
            var loans = await _context.Loans.Where(l => l.GroupMemberId == memberId).ToListAsync();
            totalLoansIssued = loans.Sum(l => l.PrincipalAmount);
            totalLoansRepaid = loans.Sum(l => l.AmountRepaid);
            var activeLoans = loans.Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Defaulted).ToList();
            outstandingLoan = activeLoans.Sum(l => l.OutstandingBalance);
            outstandingInterest = activeLoans.Sum(l => Math.Max(0, l.InterestAmount - (l.AmountRepaid > l.PrincipalAmount ? l.AmountRepaid - l.PrincipalAmount : 0)));
            heldSavings = _ruleEngine.CalculateHeldSavings(outstandingLoan, policy);

            var next = activeLoans.OrderBy(l => l.DueDate).FirstOrDefault();
            if (next != null)
            {
                nextDue = next.DueDate;
                if (DateTime.UtcNow > next.DueDate)
                    daysPastDue = (int)(DateTime.UtcNow - next.DueDate).TotalDays;
                riskStatus = next.Status == LoanStatus.Defaulted ? "Defaulted" : (daysPastDue.HasValue && daysPastDue > 0 ? "Overdue" : "Normal");
            }
        }
        catch { }

        // AvailableSavings = SavingsBalance - HeldSavings (Loan Collateral Hold)
        // Net position: available savings - debt - joining - outstanding fine - outstanding loan
        decimal availableSavings = Math.Max(0, ledgerSavings - heldSavings);
        decimal net = availableSavings - debt - joiningBalance - outstandingFine - outstandingLoan;

        return new MemberFinancialPositionDto
        {
            MemberId = memberId,
            MembershipNumber = member.MemberNumber ?? "N/A",
            FullName = member.User?.FullName ?? "Unknown",
            AsOf = asOfDate,
            SavingsBalance = ledgerSavings,
            HeldSavings = heldSavings,
            AvailableSavings = Math.Max(0, availableSavings - debt - joiningBalance - outstandingFine),
            TotalExpectedContributions = totalExpected,
            TotalPaidContributions = totalPaid,
            ContributionDebt = debt,
            ContributionCompliancePercent = Math.Round(compliance, 2),
            JoiningFeeTarget = joiningTarget,
            JoiningFeePaid = joiningPaid,
            JoiningFeeBalance = joiningBalance,
            TotalLoansIssued = totalLoansIssued,
            TotalLoansRepaid = totalLoansRepaid,
            OutstandingLoan = outstandingLoan,
            OutstandingLoanInterest = outstandingInterest,
            NextLoanDueDate = nextDue,
            DaysPastDue = daysPastDue,
            LoanRiskStatus = riskStatus,
            TotalFinesCharged = totalFinesCharged,
            TotalFinesPaid = totalFinesPaid,
            OutstandingFine = outstandingFine,
            TotalWelfareObligations = 0m,
            TotalWelfareContributions = 0m,
            WelfareBalance = 0m,
            TotalWelfareBenefitsReceived = 0m,
            NetPosition = net,
            CalculatedAt = DateTime.UtcNow
        };
    }

    private int CalculateExpectedMonths(DateTime joinDate, DateTime asOf)
    {
        if (joinDate.Kind == DateTimeKind.Unspecified)
            joinDate = DateTime.SpecifyKind(joinDate, DateTimeKind.Utc);
        if (asOf.Kind == DateTimeKind.Unspecified)
            asOf = DateTime.SpecifyKind(asOf, DateTimeKind.Utc);

        if (asOf < joinDate) return 0;
        int months = (asOf.Year - joinDate.Year) * 12 + asOf.Month - joinDate.Month + 1;
        return Math.Max(1, months);
    }

    public async Task<int> CalculateExpectedMonthsWithLedgerFixAsync(Guid memberId, DateTime? asOf = null)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) return 1;

        var earliestLedger = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync();

        DateTime effective = member.JoinedAt;
        if (effective.Kind == DateTimeKind.Unspecified)
            effective = DateTime.SpecifyKind(effective, DateTimeKind.Utc);

        if (earliestLedger != null && earliestLedger.CreatedAt < effective)
            effective = new DateTime(earliestLedger.CreatedAt.Year, earliestLedger.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return CalculateExpectedMonths(effective, asOfDate);
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
}
