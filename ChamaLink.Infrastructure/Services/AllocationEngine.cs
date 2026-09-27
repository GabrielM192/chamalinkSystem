using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Single Allocation Engine — executor of Business Rules.
/// One engine, one ledger, many reports. No duplicate logic.
/// </summary>
public class AllocationEngine
{
    private readonly BusinessRuleEngine _ruleEngine;

    public AllocationEngine(BusinessRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    /// <summary>
    /// NEW: Oldest-first allocation using ObligationLedgerService queue.
    /// This is the correct waterfall: oldest DueDate first, Contribution+Fine paired per month.
    /// For Frank: Jul payment -> May debt, Aug -> Jun debt.
    /// </summary>
    public AllocationResultDto AllocateOldestFirst(
        decimal paymentAmount,
        DateTime paymentDate,
        List<ObligationItem> outstandingQueue,
        GroupPolicy policy,
        List<Loan> activeLoans,
        int year,
        int month)
    {
        var correlationId = Guid.NewGuid();
        var result = new AllocationResultDto
        {
            PaymentReceived = paymentAmount,
            CorrelationId = correlationId
        };

        decimal remaining = paymentAmount;
        decimal loanTarget = 0m;
        foreach (var loan in activeLoans)
            loanTarget += GetExpectedRepaymentForMonth(loan, year, month);

        decimal contributionTarget = policy.MonthlyContribution;
        result.TargetForMonth = contributionTarget + loanTarget;

        // JoinFee must be its own full pass over the queue, never
        // interleaved inside the same per-obligation iteration as Contribution/Fine.
        // Interleaving it there was the root cause of the ordering bug: a lump-sum
        // payment would pay the FIRST month's Contribution+Fine, then immediately
        // drain the rest into that same first month's JoinFee (which can be as
        // large as 50,000) before the loop ever reached month 2's Contribution —
        // so later months wrongly showed as "Missed" even though the member had
        // paid enough overall. JoinFee now only ever spends money left over after
        // a complete oldest-first pass across every obligation's Contribution+Fine
        // — unless the group's own policy says otherwise (see JoinFeeMode below).
        var orderedQueue = outstandingQueue.OrderBy(o => o.DueDate).ToList();

        if (policy.JoinFeeMode == JoinFeeMode.MustCompleteBeforeContributing)
        {
            // Group's explicit rule: JoinFee must clear before anything else counts.
            remaining = AllocateJoinFeePass(orderedQueue, remaining, policy, result);
            remaining = AllocateContributionAndFinePass(orderedQueue, remaining, paymentDate, year, month, policy, result);
        }
        else
        {
            // Default (Optional / ParallelWithContributions): every month's
            // Contribution+Fine takes priority; JoinFee only gets what's left.
            remaining = AllocateContributionAndFinePass(orderedQueue, remaining, paymentDate, year, month, policy, result);
            remaining = AllocateJoinFeePass(orderedQueue, remaining, policy, result);
        }

        // 2. Loans for current month after contributions
        if (remaining > 0 && loanTarget > 0)
        {
            decimal toAllocate = Math.Min(remaining, loanTarget);
            foreach (var loan in activeLoans)
            {
                if (toAllocate <= 0) break;
                decimal expected = GetExpectedRepaymentForMonth(loan, year, month);
                decimal loanAlloc = Math.Min(toAllocate, expected);
                if (loanAlloc > 0)
                {
                    result.Allocations.Add(new AllocationItemDto
                    {
                        Target = "LoanRepayment",
                        Amount = loanAlloc,
                        PolicyVersion = policy.Version,
                        Year = year,
                        Month = month,
                        LoanId = loan.Id,
                        Note = $"Loan repayment {loan.Id} oldest-first"
                    });
                    toAllocate -= loanAlloc;
                    remaining -= loanAlloc;
                    result.PaidForMonth += loanAlloc;
                }
            }
        }

        // 3. Remaining goes to savings
        if (remaining > 0)
        {
            result.Allocations.Add(new AllocationItemDto
            {
                Target = "Savings",
                Amount = remaining,
                PolicyVersion = policy.Version,
                Note = "Excess to savings after oldest-first"
            });
            result.SavingsAdded = remaining;
            remaining = 0;
        }

        if (result.PaidForMonth >= result.TargetForMonth)
            result.Status = "Paid";
        else if (result.PaidForMonth > 0)
            result.Status = "PartiallyPaid";
        else
            result.Status = "Missed";

        return result;
    }

    /// <summary>
    /// Contribution+Fine pass only — oldest-first, paired per month.
    /// Never touches JoinFee. Extracted so JoinFee can be run as a clean separate
    /// pass (see AllocateOldestFirst) instead of competing for the same money
    /// inside one shared loop.
    /// </summary>
    private decimal AllocateContributionAndFinePass(
        List<ObligationItem> orderedQueue,
        decimal remaining,
        DateTime paymentDate,
        int year,
        int month,
        GroupPolicy policy,
        AllocationResultDto result)
    {
        foreach (var ob in orderedQueue)
        {
            if (remaining <= 0) break;

            if (ob.ContributionOutstanding > 0)
            {
                decimal alloc = Math.Min(remaining, ob.ContributionOutstanding);
                result.Allocations.Add(new AllocationItemDto
                {
                    Target = "Contribution",
                    Amount = alloc,
                    PolicyVersion = policy.Version,
                    Year = ob.Year,
                    Month = ob.Month,
                    Note = $"Contribution {ob.Year}-{ob.Month:00} due {ob.DueDate:yyyy-MM-dd} (oldest-first)"
                });
                remaining -= alloc;
                result.PaidForMonth += alloc;
                result.DebtCleared += (ob.Year != year || ob.Month != month) ? alloc : 0;
                ob.ContributionPaid += alloc;
            }

            if (remaining <= 0) break;

            // A queue may be built for the whole import year so later
            // payments can see every obligation. A fine is allocatable only
            // after this payment has crossed that obligation's grace deadline.
            if (ob.FineOutstanding > 0 &&
                paymentDate > ob.DueDate.AddDays(policy.GracePeriodDays))
            {
                decimal alloc = Math.Min(remaining, ob.FineOutstanding);
                result.Allocations.Add(new AllocationItemDto
                {
                    Target = "Fine",
                    Amount = alloc,
                    PolicyVersion = policy.Version,
                    Year = ob.Year,
                    Month = ob.Month,
                    Note = $"Fine {ob.Year}-{ob.Month:00} paired"
                });
                remaining -= alloc;
                result.FinePaid += alloc;
                ob.FinePaid += alloc;
            }
        }

        return remaining;
    }

    /// <summary>
    /// JoinFee-only pass — oldest-first across the queue (in practice
    /// only the first month ever carries JoinFeeDue &gt; 0, per
    /// ObligationLedgerService). Kept as its own pass so its position relative to
    /// Contribution/Fine is decided once, by JoinFeeMode, not by loop ordering.
    /// </summary>
    private decimal AllocateJoinFeePass(
        List<ObligationItem> orderedQueue,
        decimal remaining,
        GroupPolicy policy,
        AllocationResultDto result)
    {
        foreach (var ob in orderedQueue)
        {
            if (remaining <= 0) break;

            if (ob.JoinFeeOutstanding > 0)
            {
                decimal alloc = Math.Min(remaining, ob.JoinFeeOutstanding);
                result.Allocations.Add(new AllocationItemDto
                {
                    Target = "JoiningFee",
                    Amount = alloc,
                    PolicyVersion = policy.Version,
                    Year = ob.Year,
                    Month = ob.Month,
                    Note = $"Joining fee {ob.Year}-{ob.Month:00}"
                });
                remaining -= alloc;
                result.JoiningFeePaid += alloc;
                ob.JoinFeePaid += alloc;
            }
        }

        return remaining;
    }

    [Obsolete("Use AllocateOldestFirst with ObligationLedgerService queue. This legacy method uses single ContributionDebt number and is not oldest-first per month. Will be removed in .")]
    public AllocationResultDto Allocate(
        decimal paymentAmount,
        DateTime paymentDate,
        MemberFinancialPositionDto position,
        GroupPolicy policy,
        List<Loan> activeLoans,
        int year,
        int month)
    {
        // Legacy path kept for backward compat, but now does debt-first to fix #3 partially
        // For true oldest-first, callers should use AllocateOldestFirst with queue from ObligationLedgerService
        var correlationId = Guid.NewGuid();
        var result = new AllocationResultDto
        {
            PaymentReceived = paymentAmount,
            CorrelationId = correlationId
        };

        decimal remaining = paymentAmount;
        decimal contributionTarget = policy.MonthlyContribution;
        decimal loanTarget = 0m;
        foreach (var loan in activeLoans)
            loanTarget += GetExpectedRepaymentForMonth(loan, year, month);

        decimal totalTarget = contributionTarget + loanTarget;
        result.TargetForMonth = totalTarget;

        // Fixed: Debt first (oldest) before current contribution - partial fix for #3
        var order = GetAllocationOrderFixed(policy);

        foreach (var target in order)
        {
            if (remaining <= 0) break;
            switch (target)
            {
                case "Debt":
                    if (position.ContributionDebt > 0 && remaining > 0)
                    {
                        decimal toAllocate = Math.Min(remaining, position.ContributionDebt);
                        result.Allocations.Add(new AllocationItemDto
                        {
                            Target = "Debt",
                            Amount = toAllocate,
                            PolicyVersion = policy.Version,
                            Note = "Old contribution debt (fixed: debt first)"
                        });
                        remaining -= toAllocate;
                        result.DebtCleared += toAllocate;
                        result.PaidForMonth += toAllocate;
                    }
                    break;
                case "Fine":
                    if (position.OutstandingFine > 0 && remaining > 0)
                    {
                        decimal toAllocate = Math.Min(remaining, position.OutstandingFine);
                        result.Allocations.Add(new AllocationItemDto
                        {
                            Target = "Fine",
                            Amount = toAllocate,
                            PolicyVersion = policy.Version,
                            Note = "Fine payment"
                        });
                        remaining -= toAllocate;
                        result.FinePaid += toAllocate;
                    }
                    break;
                case "Contribution":
                    if (contributionTarget > 0)
                    {
                        decimal toAllocate = Math.Min(remaining, contributionTarget);
                        if (toAllocate > 0)
                        {
                            result.Allocations.Add(new AllocationItemDto
                            {
                                Target = "Contribution",
                                Amount = toAllocate,
                                PolicyVersion = policy.Version,
                                Year = year,
                                Month = month,
                                Note = $"Contribution {year}-{month:00}"
                            });
                            remaining -= toAllocate;
                            result.PaidForMonth += toAllocate;
                        }
                    }
                    break;
                case "LoanRepayment":
                    if (loanTarget > 0 && remaining > 0)
                    {
                        decimal toAllocate = Math.Min(remaining, loanTarget);
                        foreach (var loan in activeLoans)
                        {
                            if (toAllocate <= 0) break;
                            decimal expected = GetExpectedRepaymentForMonth(loan, year, month);
                            decimal loanAlloc = Math.Min(toAllocate, expected);
                            if (loanAlloc > 0)
                            {
                                result.Allocations.Add(new AllocationItemDto
                                {
                                    Target = "LoanRepayment",
                                    Amount = loanAlloc,
                                    PolicyVersion = policy.Version,
                                    Year = year,
                                    Month = month,
                                    LoanId = loan.Id,
                                    Note = $"Loan repayment {loan.Id}"
                                });
                                toAllocate -= loanAlloc;
                                remaining -= loanAlloc;
                                result.PaidForMonth += loanAlloc;
                            }
                        }
                    }
                    break;
                case "JoiningFee":
                    if (position.JoiningFeeBalance > 0 && remaining > 0)
                    {
                        decimal toAllocate = Math.Min(remaining, position.JoiningFeeBalance);
                        result.Allocations.Add(new AllocationItemDto
                        {
                            Target = "JoiningFee",
                            Amount = toAllocate,
                            PolicyVersion = policy.Version,
                            Note = "Joining fee"
                        });
                        remaining -= toAllocate;
                        result.JoiningFeePaid += toAllocate;
                    }
                    break;
            }
        }

        if (remaining > 0)
        {
            result.Allocations.Add(new AllocationItemDto
            {
                Target = "Savings",
                Amount = remaining,
                PolicyVersion = policy.Version,
                Note = "Excess to savings"
            });
            result.SavingsAdded = remaining;
            remaining = 0;
        }

        if (result.PaidForMonth >= totalTarget)
            result.Status = "Paid";
        else if (result.PaidForMonth > 0)
            result.Status = "PartiallyPaid";
        else
            result.Status = "Missed";

        return result;
    }

    public AllocationResultDto AllocateWithSavingsCover(
        decimal paymentAmount,
        DateTime paymentDate,
        MemberFinancialPositionDto position,
        GroupPolicy policy,
        List<Loan> activeLoans,
        int year,
        int month)
    {
#pragma warning disable CS0618
        var result = Allocate(paymentAmount, paymentDate, position, policy, activeLoans, year, month);
#pragma warning restore CS0618

        decimal shortfall = result.TargetForMonth - result.PaidForMonth;
        if (shortfall > 0 && position.AvailableSavings > 0)
        {
            decimal savingsCover = _ruleEngine.ShouldUseSavings(shortfall, position.AvailableSavings, policy);
            if (savingsCover > 0)
            {
                result.Allocations.Add(new AllocationItemDto
                {
                    Target = "SavingsCover",
                    Amount = savingsCover,
                    PolicyVersion = policy.Version,
                    Year = year,
                    Month = month,
                    Note = $"Covered by savings {savingsCover:N0}"
                });
                result.PaidForMonth += savingsCover;
                result.Status = result.PaidForMonth >= result.TargetForMonth ? "CoveredBySavings" : "PartiallyPaid";
            }
        }

        return result;
    }

    private List<string> GetAllocationOrder(GroupPolicy policy)
    {
        return policy.AllocationStrategy switch
        {
            AllocationStrategy.ContributionFirst => new List<string> { "Contribution", "LoanRepayment", "Fine", "JoiningFee", "Debt", "Savings" },
            AllocationStrategy.RepaymentFirst => new List<string> { "LoanRepayment", "Contribution", "Fine", "JoiningFee", "Debt", "Savings" },
            AllocationStrategy.Custom => new List<string> { "Fine", "Contribution", "LoanRepayment", "JoiningFee", "Debt", "Savings" },
            _ => new List<string> { "Contribution", "LoanRepayment", "Fine", "JoiningFee", "Debt", "Savings" }
        };
    }

    private List<string> GetAllocationOrderFixed(GroupPolicy policy)
    {
        // Fixed for #3: Debt first, then Fine, then Contribution, then Loan, then JoiningFee
        return policy.AllocationStrategy switch
        {
            AllocationStrategy.ContributionFirst => new List<string> { "Debt", "Fine", "Contribution", "LoanRepayment", "JoiningFee", "Savings" },
            AllocationStrategy.RepaymentFirst => new List<string> { "Debt", "LoanRepayment", "Fine", "Contribution", "JoiningFee", "Savings" },
            AllocationStrategy.Custom => new List<string> { "Debt", "Fine", "Contribution", "LoanRepayment", "JoiningFee", "Savings" },
            _ => new List<string> { "Debt", "Fine", "Contribution", "LoanRepayment", "JoiningFee", "Savings" }
        };
    }

    private decimal GetExpectedRepaymentForMonth(Loan loan, int year, int month)
    {
        // Simple flat repayment: Principal + Interest / months
        // For more accurate schedule, use LoanService.GetExpectedRepaymentForMonth
        try
        {
            return LoanService.GetExpectedRepaymentForMonth(loan, year, month);
        }
        catch
        {
            // Fallback
            if (loan.Status != LoanStatus.Active) return 0m;
            var total = loan.PrincipalAmount + loan.InterestAmount;
            var months = 6; // default
            if (loan.DueDate > loan.DisbursedAt)
            {
                months = Math.Max(1, (int)((loan.DueDate - loan.DisbursedAt).TotalDays / 30));
            }
            return total / months;
        }
    }
}
