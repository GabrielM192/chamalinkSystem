using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Business Rule Engine — foundation of ChamaLink V2.
/// Rules are the source, Allocation is executor. No hardcoded values.
/// </summary>
public class BusinessRuleEngine
{
    private readonly ApplicationDbContext _context;

    public BusinessRuleEngine(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GroupPolicy> GetActivePolicyAsync(Guid groupId, DateTime asOf)
    {
        if (asOf.Kind == DateTimeKind.Unspecified)
            asOf = DateTime.SpecifyKind(asOf, DateTimeKind.Utc);
        else if (asOf.Kind == DateTimeKind.Local)
            asOf = asOf.ToUniversalTime();

        try
        {
            var policy = await _context.GroupPolicies
                .Where(p => p.GroupId == groupId && p.EffectiveFrom <= asOf && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
                .OrderByDescending(p => p.Version)
                .FirstOrDefaultAsync();

            if (policy != null) return policy;
        }
        catch (Exception ex)
        {
            // Any error querying GroupPolicies - fallback to GroupSettings
            // Catches: 42P01 table not exist, CreatedBy null, Reason null, etc.
            // Prevents 500 on debug endpoints when DB is partially migrated
            // Log for debugging but continue to fallback
            Console.WriteLine($"[BusinessRuleEngine] GroupPolicies query failed for group {groupId}: {ex.Message} - falling back to GroupSettings");
        }

        // Fallback to GroupSettings for backward compatibility (V1)
        var group = await _context.Groups.Include(g => g.Settings).FirstOrDefaultAsync(g => g.Id == groupId);
        if (group?.Settings == null)
            throw new PolicyNotConfiguredException(groupId, $"No financial policy or group settings found for group {groupId}.");

        return new GroupPolicy
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Version = 1,
            EffectiveFrom = group.CreatedAt,
            MonthlyContribution = group.Settings.Contribution.MonthlyContribution,
            JoiningFee = group.Settings.Financial.JoiningFee > 0 ? group.Settings.Financial.JoiningFee : group.Settings.Financial.JoinFeeConfig.RequiredAmount,
            DueDateDay = group.Settings.Contribution.DueDateDay,
            JoinFeeMode = group.Settings.Financial.JoinFeeConfig.Mode,
            JoinFeeCaptureMode = group.Settings.Financial.JoinFeeConfig.CaptureMode,
            JoinFeeApprovalThreshold = group.Settings.Financial.JoinFeeConfig.ApprovalThreshold,
            LateFine = group.Settings.Contribution.LateFine,
            GracePeriodDays = group.Settings.Contribution.GracePeriodDays,
            MaxConsecutiveMissedMonths = group.Settings.Contribution.MaxConsecutiveMissedMonths,
            AllocationStrategy = AllocationStrategy.ContributionFirst,
            ShortfallStrategy = group.Settings.Contribution.ShortfallStrategy,
            DebtAllocationStrategy = group.Settings.Contribution.DebtAllocationStrategy,
            LoanEnabled = group.Settings.Loan.Enabled,
            LoanInterestRate = group.Settings.Loan.InterestRate,
            LoanInterestType = group.Settings.Loan.InterestType,
            LoanRepaymentDays = group.Settings.Loan.RepaymentDays,
            LoanLatePenaltyAmount = group.Settings.Loan.LatePenaltyAmount,
            LoanMaxMultiplier = group.Settings.Loan.MaxLoanMultiplier,
            LoanStrategy = group.Settings.Loan.LoanStrategy,
            GuarantorRequired = group.Settings.Loan.GuarantorRequired,
            MinGuarantors = group.Settings.Loan.MinGuarantors,
            WelfareEnabled = group.Settings.Event.EventsEnabled,
            WelfareFineAmount = group.Settings.Event.WelfareFineAmount,
            WelfareMode = group.Settings.Event.WelfareMode,
            IsActive = true,
            Reason = "Migrated from GroupSettings V1"
        };
    }

    public bool ShouldChargeFine(decimal shortfall, decimal availableSavings, GroupPolicy policy)
    {
        if (shortfall <= 0) return false;
        return policy.ShortfallStrategy switch
        {
            ShortfallStrategy.FineImmediately => true,
            ShortfallStrategy.UseSavingsThenFine => availableSavings < shortfall,
            ShortfallStrategy.CreateDebt => false,
            _ => true
        };
    }

    public decimal ShouldUseSavings(decimal shortfall, decimal availableSavings, GroupPolicy policy)
    {
        if (shortfall <= 0 || availableSavings <= 0) return 0m;
        return policy.ShortfallStrategy switch
        {
            ShortfallStrategy.UseSavingsThenFine => Math.Min(shortfall, availableSavings),
            _ => 0m
        };
    }

    public bool ShouldCreateDebt(decimal shortfall, GroupPolicy policy)
    {
        if (shortfall <= 0) return false;
        return policy.ShortfallStrategy != ShortfallStrategy.FineImmediately || shortfall > 0;
    }

    public string CalculateMonthlyStatus(decimal paid, decimal target, decimal savingsUsed, GroupPolicy policy)
    {
        if (paid >= target) return "Paid";
        if (paid + savingsUsed >= target) return "CoveredBySavings";
        if (paid > 0) return "PartiallyPaid";
        return "Missed";
    }

    public string CalculateMemberStatus(int consecutiveMissedMonths, GroupPolicy policy, string currentStatus)
    {
        if (currentStatus == "Exited" || currentStatus == "Deceased" || currentStatus == "Archived")
            return currentStatus;

        if (consecutiveMissedMonths >= policy.MaxConsecutiveMissedMonths)
            return "NonActive";
        if (consecutiveMissedMonths >= policy.MaxConsecutiveMissedMonths - 1)
            return "Warning";
        if (consecutiveMissedMonths >= 1)
            return "Inactive";
        return "Active";
    }

    public decimal CalculateHeldSavings(decimal outstandingLoan, GroupPolicy policy)
    {
        if (outstandingLoan <= 0) return 0m;
        return outstandingLoan * (policy.LoanCollateralPercent / 100m);
    }
}
