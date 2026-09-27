using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

// Sprint 1 gaps: Collection Rate Engine, Defaulter Engine, Group Balance
// Engine, Group Financial Summary Endpoint. This is the "financial
// intelligence" layer the ChamaLink vision calls for - it answers the
// leader's questions directly instead of leaving them to re-derive
// answers from raw ledger rows.
public class AnalyticsService
{
    private readonly ApplicationDbContext _context;
    private readonly FineService _fineService;

    public AnalyticsService(ApplicationDbContext context, FineService fineService)
    {
        _context = context;
        _fineService = fineService;
    }

    // BUG FIX (Collection Rate business-logic gap): this used to always
    // apply the Monthly Contribution formula (MonthlyContribution x
    // ActiveMembers), even for EventBased groups where
    // MonthlyContribution is 0 and there is no monthly cycle at all -
    // that produced a meaningless 0/0 rate for every Welfare/Event-Based
    // group. The formula now depends on the group's own Type, and Hybrid
    // combines both, since a Hybrid group genuinely carries both kinds of
    // obligation at once.
    public async Task<CollectionRateDto> GetCollectionRateAsync(Guid groupId, DateTime? period = null)
    {
        var group = await _context.Groups.Include(g => g.Settings)
            .FirstOrDefaultAsync(g => g.Id == groupId)
            ?? throw new NotFoundException("Kikundi hakikupatikana.");

        var target = period ?? DateTime.UtcNow;
        var periodStart = new DateTime(target.Year, target.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        decimal expected = 0m;
        decimal collected = 0m;

        if (group.Type == GroupType.MonthlySavings || group.Type == GroupType.Hybrid)
        {
            var (monthlyExpected, monthlyCollected) = await GetMonthlyCollectionAsync(group, periodStart);
            expected += monthlyExpected;
            collected += monthlyCollected;
        }

        if (group.Type == GroupType.EventBased || group.Type == GroupType.Hybrid)
        {
            var (eventExpected, eventCollected) = await GetEventCollectionAsync(groupId);
            expected += eventExpected;
            collected += eventCollected;
        }

        decimal rate = expected > 0 ? Math.Round(collected / expected * 100m, 1) : 0m;

        return new CollectionRateDto(groupId, periodStart, expected, collected, rate);
    }

    // Monthly Contribution formula: MonthlyContribution x currently-obligated
    // members (Active/Warning - same population the compliance job charges),
    // vs. what was actually posted to Savings this calendar month.
    private async Task<(decimal Expected, decimal Collected)> GetMonthlyCollectionAsync(Group group, DateTime periodStart)
    {
        var periodEnd = periodStart.AddMonths(1);
        decimal monthlyTarget = group.Settings?.Contribution.MonthlyContribution ?? 0m;

        int obligatedMembers = await _context.GroupMembers
            .CountAsync(m => m.GroupId == group.Id &&
                        (m.Status == MemberStatus.Active || m.Status == MemberStatus.Warning));

        decimal expected = monthlyTarget * obligatedMembers;

        var savingsAccountIds = await _context.Accounts
            .Where(a => a.Type == AccountType.Savings && a.GroupMember!.GroupId == group.Id)
            .Select(a => a.Id)
            .ToListAsync();

        decimal collected = savingsAccountIds.Count == 0
            ? 0m
            : await _context.LedgerEntries
                .Where(l => savingsAccountIds.Contains(l.AccountId) &&
                            l.Type == TransactionType.Contribution &&
                            l.CreatedAt >= periodStart && l.CreatedAt < periodEnd)
                .SumAsync(l => (decimal?)l.Amount) ?? 0m;

        return (expected, collected);
    }

    // Event/Welfare formula: an EventBased group has no calendar cycle, so
    // "expected" is what EventContribution already tracks per active event
    // (Expected/Paid per member) rather than a monthly target. This covers
    // every currently-open (not yet resolved) event at once, so the rate
    // reflects standing obligations rather than one arbitrary month.
    private async Task<(decimal Expected, decimal Collected)> GetEventCollectionAsync(Guid groupId)
    {
        var openEventIds = await _context.GroupEvents
            .Where(e => e.GroupId == groupId && !e.IsResolved)
            .Select(e => e.Id)
            .ToListAsync();

        if (openEventIds.Count == 0)
            return (0m, 0m);

        var totals = await _context.EventContributions
            .Where(ec => openEventIds.Contains(ec.GroupEventId))
            .GroupBy(_ => 1)
            .Select(g => new { Expected = g.Sum(x => x.ExpectedAmount), Paid = g.Sum(x => x.PaidAmount) })
            .FirstOrDefaultAsync();

        return (totals?.Expected ?? 0m, totals?.Paid ?? 0m);
    }

    public async Task<List<DefaulterDto>> GetDefaultersAsync(Guid groupId)
    {
        var members = await _context.GroupMembers
            .Include(m => m.User)
            .Where(m => m.GroupId == groupId && m.Status != MemberStatus.Exited)
            .ToListAsync();

        var result = new List<DefaulterDto>();

        foreach (var member in members)
        {
            decimal outstandingDebt = await _context.Debts
                .Where(d => d.GroupMemberId == member.Id && d.Status == DebtStatus.Outstanding)
                .SumAsync(d => (decimal?)(d.Amount - d.AmountCleared)) ?? 0m;

            decimal outstandingFine = await _context.Fines
                .Where(f => f.GroupMemberId == member.Id &&
                            (f.Status == FineStatus.Pending || f.Status == FineStatus.PartiallyPaid))
                .SumAsync(f => (decimal?)(f.Amount - f.AmountPaid)) ?? 0m;

            if (outstandingDebt <= 0 && outstandingFine <= 0)
                continue;

            var lastContribution = await _context.LedgerEntries
                .Where(l => l.UserId == member.UserId && l.GroupId == groupId &&
                            l.Type == TransactionType.Contribution)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => (DateTime?)l.CreatedAt)
                .FirstOrDefaultAsync();

            // NEW: how long this member has actually been behind - the
            // age (in days) of their oldest still-Outstanding Debt.
            var oldestOutstandingDebtPeriod = await _context.Debts
                .Where(d => d.GroupMemberId == member.Id && d.Status == DebtStatus.Outstanding)
                .OrderBy(d => d.Period)
                .Select(d => (DateTime?)d.Period)
                .FirstOrDefaultAsync();

            int daysLate = oldestOutstandingDebtPeriod.HasValue
                ? Math.Max(0, (int)(DateTime.UtcNow - oldestOutstandingDebtPeriod.Value).TotalDays)
                : 0;

            result.Add(new DefaulterDto(
                member.UserId,
                member.User?.FullName ?? "Mwanachama",
                member.User?.PhoneNumber ?? string.Empty,
                outstandingDebt,
                outstandingFine,
                lastContribution,
                daysLate
            ));
        }

        return result.OrderByDescending(d => d.OutstandingDebt + d.OutstandingFine).ToList();
    }

    // BUG FIX (Group Balance business-logic gap): a single "current
    // balance" number used to conflate cash on hand with money still out
    // on loan/owed. For a lending chama, a loan is still the group's
    // asset (expected back, with interest), so it must count toward what
    // the group is worth - but it is not cash sitting in the bank/mobile
    // money/cash box today. This now reports both views explicitly.
    public async Task<GroupBalanceDto> GetGroupBalanceAsync(Guid groupId)
    {
        var accountIds = await _context.Accounts
            .Where(a => a.GroupMember!.GroupId == groupId)
            .Select(a => new { a.Id, a.Type })
            .ToListAsync();

        var savingsIds = accountIds.Where(a => a.Type == AccountType.Savings).Select(a => a.Id).ToList();
        var loanIds = accountIds.Where(a => a.Type == AccountType.Loan).Select(a => a.Id).ToList();
        var fineIds = accountIds.Where(a => a.Type == AccountType.Fine).Select(a => a.Id).ToList();
        var welfareIds = accountIds.Where(a => a.Type == AccountType.SocialFund).Select(a => a.Id).ToList();

        decimal totalContributions = await SumAsync(savingsIds, TransactionType.Contribution);
        decimal totalLoansDisbursed = await SumAsync(loanIds, TransactionType.LoanDisbursement);
        decimal totalLoansRepaid = await SumAsync(loanIds, TransactionType.LoanRepayment);
        decimal totalFinesCollected = await SumAsync(fineIds, TransactionType.FinePayment);
        decimal totalWelfareTopUps = await SumAsync(welfareIds, TransactionType.WelfareTopUp);
        decimal totalWelfareDeductions = await SumAsync(welfareIds, TransactionType.WelfareDeduction);

        decimal totalWithdrawalsPaid = await _context.Withdrawals
            .Where(w => w.GroupId == groupId && w.Status == WithdrawalStatus.Paid)
            .SumAsync(w => (decimal?)w.Amount) ?? 0m;

        // FIX (A4): M-Koba statements contain "Withdraw / Transfer fund"
        // rows - money that left the group's mobile wallet (e.g. the
        // treasurer paying out a loan in cash). The import records these
        // in the WalletWithdrawals table, but until now the balance calc
        // only looked at the Withdrawals table (member-initiated payout
        // workflow), so imported wallet withdrawals were invisible and the
        // group appeared richer than reality.
        //
        // We subtract any WalletWithdrawal that has NOT already been
        // promoted into a paid Withdrawal record (linked via
        // WalletWithdrawalId). This way:
        //  - today (reconcile = categorise only): every imported withdrawal
        //    reduces cash immediately, because the money has physically left.
        //  - tomorrow (if reconcile starts creating linked Withdrawal(Paid)
        //    records): the WalletWithdrawal stops being subtracted here and
        //    is subtracted via totalWithdrawalsPaid instead - no double count.
        decimal totalWalletWithdrawals = await _context.WalletWithdrawals
            .Where(w => w.GroupId == groupId &&
                        !_context.Withdrawals.Any(wd => wd.WalletWithdrawalId == w.Id &&
                                                        wd.Status == WithdrawalStatus.Paid))
            .SumAsync(w => (decimal?)w.Amount) ?? 0m;

        // What is still owed back to the group - a real asset (expected
        // to return as cash eventually), just not cash on hand today.
        decimal outstandingLoans = totalLoansDisbursed - totalLoansRepaid;
        decimal outstandingFines = await _context.Fines
            .Where(f => f.GroupId == groupId && (f.Status == FineStatus.Pending || f.Status == FineStatus.PartiallyPaid))
            .SumAsync(f => (decimal?)(f.Amount - f.AmountPaid)) ?? 0m;
        decimal outstandingDebts = await _context.Debts
            .Where(d => d.GroupId == groupId && d.Status == DebtStatus.Outstanding)
            .SumAsync(d => (decimal?)(d.Amount - d.AmountCleared)) ?? 0m;

        // UPDATED (welfare mode configurability, see WelfareMode in
        // Enums.cs): this stays additive on purpose, regardless of a
        // group's WelfareMode setting. A WelfareDeduction never
        // represents cash actually leaving the group by itself - the
        // group still physically holds that money (in the bank/mobile
        // wallet) whether it's labelled "top-up" or "deduction" until a
        // real Withdrawal pays a beneficiary, and totalWithdrawalsPaid
        // below already accounts for that moment. If this line instead
        // subtracted deductions under DeductBalance mode, the same
        // payout would get counted twice: once here, and again when the
        // linked Withdrawal is marked Paid. WelfareMode only changes how
        // a single MEMBER's own welfare balance is displayed/penalised
        // (see WelfarePenaltyBackgroundService) - it is a different
        // question from "how much cash does the group have right now".
        decimal welfareHeld = totalWelfareTopUps + totalWelfareDeductions;

        // Cash Available: what is actually sitting in the group's own
        // bank/M-Pesa/Airtel Money/Mixx/cash box right now. A loan
        // disbursement is cash leaving the group's hands the moment it is
        // paid out (it becomes a receivable, not cash on hand); a
        // repayment brings cash back in. A Withdrawal only reduces cash
        // once it is actually Paid (not merely Approved). An imported M-Koba
        // wallet withdrawal reduces cash immediately - the money has
        // physically left the wallet regardless of what it was for.
        decimal cashAvailable = totalContributions + totalFinesCollected + welfareHeld
            - outstandingLoans - totalWithdrawalsPaid - totalWalletWithdrawals;

        // Total Assets: the group's real net worth - cash on hand plus
        // everything still owed back to it. This answers "how much is
        // this chama actually worth", as opposed to "how much could we
        // pay out today" (that's Cash Available).
        decimal totalAssets = cashAvailable + outstandingLoans + outstandingFines + outstandingDebts;

        return new GroupBalanceDto(
            groupId, totalContributions, cashAvailable, outstandingLoans,
            outstandingFines, outstandingDebts, welfareHeld, totalWithdrawalsPaid,
            totalWalletWithdrawals, totalAssets);
    }

    public async Task<GroupFinancialSummaryDto> GetGroupFinancialSummaryAsync(Guid groupId)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId)
            ?? throw new NotFoundException("Kikundi hakikupatikana.");

        int totalMembers = await _context.GroupMembers.CountAsync(m => m.GroupId == groupId);
        int activeMembers = await _context.GroupMembers
            .CountAsync(m => m.GroupId == groupId && m.Status == MemberStatus.Active);

        var balance = await GetGroupBalanceAsync(groupId);
        var collectionRate = await GetCollectionRateAsync(groupId);

        int openEvents = await _context.GroupEvents
            .CountAsync(e => e.GroupId == groupId && !e.IsResolved);

        int pendingWithdrawals = await _context.Withdrawals
            .CountAsync(w => w.GroupId == groupId && w.Status == WithdrawalStatus.Pending);

        return new GroupFinancialSummaryDto(
            groupId, group.Name, totalMembers, activeMembers,
            balance.CashAvailable, balance.OutstandingLoans, balance.OutstandingFines,
            balance.OutstandingDebts, balance.TotalAssets,
            collectionRate.CollectionRatePercent, openEvents, pendingWithdrawals);
    }

    private async Task<decimal> SumAsync(List<Guid> accountIds, TransactionType type)
    {
        if (accountIds.Count == 0) return 0m;
        return await _context.LedgerEntries
            .Where(l => accountIds.Contains(l.AccountId) && l.Type == type)
            .SumAsync(l => (decimal?)l.Amount) ?? 0m;
    }

    // NEW (Reports Engine gap: Member Financial Profile) - the member's
    // "financial passport". Replaces the old member-profile endpoint's
    // untyped anonymous object (savings/debt/fine only) with everything a
    // leader actually needs in one place: group/role context, real
    // contribution compliance, the member's actual Loans (not just an
    // account balance), event contribution history, and a quick-glance
    // Financial Score.
    public async Task<MemberFinancialProfileDto> GetMemberFinancialProfileAsync(Guid groupId, Guid userId)
    {
        var member = await _context.GroupMembers
            .Include(m => m.User)
            .Include(m => m.Group)
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId)
            ?? throw new NotFoundException("Mwanachama hajapatikana kwenye kikundi hiki.");

        var savingsAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.GroupMemberId == member.Id && a.Type == AccountType.Savings);

        decimal totalContributions = savingsAccount == null ? 0m : await _context.LedgerEntries
            .Where(l => l.AccountId == savingsAccount.Id && l.Type == TransactionType.Contribution)
            .SumAsync(l => (decimal?)l.Amount) ?? 0m;

        var settings = await _context.GroupSettings.FirstOrDefaultAsync(s => s.GroupId == groupId);
        decimal monthlyTarget = settings?.Contribution.MonthlyContribution ?? 0m;

        // BUG FIX (Member Financial Profile polish): this used to run
        // JoinedAt -> DateTime.UtcNow unconditionally, so a member who
        // left the group long ago (Inactive/Suspended/Exited) kept
        // accumulating "months owed" forever, making their compliance %
        // (and therefore their Financial Score) look worse the longer
        // they'd been gone - even though ContributionComplianceBackgroundService
        // itself had already stopped charging them the moment they left
        // Active/Warning. See ContributionExpectationCalculator
        // (TECH-DEBT-004) for the full reasoning and its known
        // precision limits.
        var lastContributionAt = savingsAccount == null ? null : await _context.LedgerEntries
            .Where(l => l.AccountId == savingsAccount.Id && l.Type == TransactionType.Contribution)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync();

        int monthsSinceJoining = ContributionExpectationCalculator.CalculateExpectedMonths(
            member.JoinedAt, member.Status, lastContributionAt, DateTime.UtcNow);
        decimal expectedContributions = monthlyTarget * Math.Max(0, monthsSinceJoining);
        decimal compliancePercent = expectedContributions > 0
            ? Math.Round(Math.Min(100m, totalContributions / expectedContributions * 100m), 1)
            : 100m;

        var debts = await _context.Debts
            .Where(d => d.GroupMemberId == member.Id)
            .ToListAsync();

        var outstandingDebts = debts.Where(d => d.Status == DebtStatus.Outstanding).ToList();
        decimal outstandingDebt = outstandingDebts.Sum(d => d.Amount - d.AmountCleared);

        // "Overdue" here means genuinely chronic - behind by more than one
        // full billing cycle, not just this month's shortfall which was
        // only just charged.
        var oneCycleAgo = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        decimal overdueDebt = outstandingDebts.Where(d => d.Period < oneCycleAgo).Sum(d => d.Amount - d.AmountCleared);

        // NEW (Member Financial Profile completion, "Financial
        // Obligations" section): across EVERY debt this member has ever
        // had (not just the still-Outstanding ones above) - a debt that
        // has since been Cleared or Waived can still carry a nonzero
        // AmountCleared from partial payments made before it reached
        // that final status.
        decimal totalDebtCleared = debts.Sum(d => d.AmountCleared);

        var fines = await _fineService.GetFinesForMemberAsync(member.Id);
        decimal totalFinesIssued = fines.Sum(f => f.Amount);
        decimal finesPaid = fines.Sum(f => f.AmountPaid);
        decimal outstandingFines = fines
            .Where(f => f.Status != FineStatus.Paid && f.Status != FineStatus.Waived)
            .Sum(f => f.Amount - f.AmountPaid);

        // NEW (Member Financial Profile completion, "Welfare" line): what
        // the member has actually paid INTO the welfare fund, distinct
        // from BenefitsReceived below (what they've been PAID OUT of the
        // group). Read directly off their SocialFund account rather than
        // via AccountResolverService, since a report should never create
        // an account as a side effect of being viewed - a member with no
        // SocialFund account yet simply has 0m here.
        var welfareAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.GroupMemberId == member.Id && a.Type == AccountType.SocialFund);
        decimal welfareContributions = welfareAccount == null ? 0m : await _context.LedgerEntries
            .Where(l => l.AccountId == welfareAccount.Id && l.Type == TransactionType.WelfareTopUp)
            .SumAsync(l => (decimal?)l.Amount) ?? 0m;

        var loans = await _context.Loans
            .Where(l => l.GroupMemberId == member.Id)
            .OrderByDescending(l => l.DisbursedAt)
            .ToListAsync();
        var loanDtos = loans.Select(l => new LoanResponseDto(
            l.Id, l.GroupId, l.GroupMemberId, l.UserId, member.User?.FullName,
            l.PrincipalAmount, l.InterestRate, l.InterestAmount, l.TotalPayable,
            l.AmountRepaid, l.OutstandingBalance, l.Purpose, l.DisbursedAt, l.DueDate,
            l.RepaidAt, l.Status.ToString(), l.IsOverdue)).ToList();
        var activeLoans = loans.Where(l => l.Status == LoanStatus.Active).ToList();
        decimal activeLoansOutstanding = activeLoans.Sum(l => l.OutstandingBalance);

        // NEW (Member Financial Profile completion, "Loans" section):
        // summarize the Loans list above so a leader (or later, a
        // report) doesn't have to re-total it themselves every time.
        // Deliberately across ALL loans regardless of status - a Repaid
        // loan still counts toward how much this member has ever
        // borrowed/repaid in total.
        decimal totalBorrowed = loans.Sum(l => l.PrincipalAmount);
        decimal totalRepaid = loans.Sum(l => l.AmountRepaid);
        int overdueLoansCount = loans.Count(l => l.IsOverdue);

        var eventContributions = await _context.EventContributions
            .Include(ec => ec.GroupEvent)
            .Where(ec => ec.GroupMemberId == member.Id)
            .OrderByDescending(ec => ec.CreatedAt)
            .Select(ec => new MemberEventContributionDto(
                ec.GroupEventId,
                ec.GroupEvent != null ? ec.GroupEvent.Title : "Tukio",
                ec.ExpectedAmount,
                ec.PaidAmount,
                ec.Status.ToString()))
            .ToListAsync();

        // BUG FIX (Reports Engine gap: Benefits Received had no real
        // beneficiary link): this used to sum TransactionType.ShareOut,
        // the annual dividend - a completely different concept from a
        // welfare/event payout, and one this codebase doesn't even issue
        // yet. Withdrawal now carries BeneficiaryGroupMemberId, so this
        // sums what the member has actually been paid out (Paid status
        // only - Pending/Approved-but-not-yet-paid hasn't reached them).
        decimal benefitsReceived = await _context.Withdrawals
            .Where(w => w.BeneficiaryGroupMemberId == member.Id && w.Status == WithdrawalStatus.Paid)
            .SumAsync(w => (decimal?)w.Amount) ?? 0m;

        // NEW (Member Financial Profile completion, "Governance"
        // section): how many WithdrawalApproval decisions (approve OR
        // reject) this member has personally cast as a leader.
        int approvalsParticipated = await _context.WithdrawalApprovals
            .CountAsync(a => a.GroupMemberId == member.Id);

        bool hasOverdueLoan = activeLoans.Any(l => l.IsOverdue);
        string score = ComputeFinancialScore(overdueDebt, outstandingFines, compliancePercent, hasOverdueLoan);
        string loanRiskCategory = ComputeLoanRiskCategory(loans, hasOverdueLoan);

        return new MemberFinancialProfileDto(
            member.UserId, member.Id, member.User?.FullName ?? "Mwanachama",
            member.User?.PhoneNumber ?? string.Empty,
            groupId, member.Group?.Name ?? string.Empty, member.Role.ToString(), member.Status.ToString(),
            member.JoinedAt,
            totalContributions, expectedContributions, compliancePercent,
            outstandingDebt, overdueDebt, totalDebtCleared,
            totalFinesIssued, finesPaid, outstandingFines,
            welfareContributions,
            activeLoansOutstanding, activeLoans.Count, loanDtos,
            totalBorrowed, totalRepaid, overdueLoansCount,
            eventContributions,
            benefitsReceived,
            approvalsParticipated,
            score,
            loanRiskCategory);
    }

    // NEW (Reports Engine gap: Members Report) - the one-screen table for
    // "everyone's standing at once" instead of opening each profile.
    public async Task<List<MemberReportRowDto>> GetMembersReportAsync(Guid groupId)
    {
        var members = await _context.GroupMembers
            .Include(m => m.User)
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        var result = new List<MemberReportRowDto>();

        foreach (var member in members)
        {
            var savingsAccount = await _context.Accounts
                .FirstOrDefaultAsync(a => a.GroupMemberId == member.Id && a.Type == AccountType.Savings);

            decimal savingsBalance = savingsAccount == null ? 0m : await _context.LedgerEntries
                .Where(l => l.AccountId == savingsAccount.Id && l.Type == TransactionType.Contribution)
                .SumAsync(l => (decimal?)l.Amount) ?? 0m;

            decimal outstandingDebt = await _context.Debts
                .Where(d => d.GroupMemberId == member.Id && d.Status == DebtStatus.Outstanding)
                .SumAsync(d => (decimal?)(d.Amount - d.AmountCleared)) ?? 0m;

            decimal outstandingFine = await _context.Fines
                .Where(f => f.GroupMemberId == member.Id &&
                            (f.Status == FineStatus.Pending || f.Status == FineStatus.PartiallyPaid))
                .SumAsync(f => (decimal?)(f.Amount - f.AmountPaid)) ?? 0m;

            var loans = await _context.Loans
                .Where(l => l.GroupMemberId == member.Id && l.Status != LoanStatus.Repaid)
                .ToListAsync();
            decimal outstandingLoan = loans.Sum(l => l.OutstandingBalance);

            result.Add(new MemberReportRowDto(
                member.UserId, member.User?.FullName ?? "Mwanachama", member.Status.ToString(),
                savingsBalance, outstandingDebt, outstandingFine, outstandingLoan));
        }

        return result.OrderBy(r => r.MemberName).ToList();
    }

    // A quick-glance grade, not a credit-bureau algorithm - the numbers
    // on the profile are what actually matters; this just summarizes them
    // for a leader scanning a list of many members at once. Tune the
    // thresholds together with your treasurers if they don't match how
    // your groups actually judge a member.
    private static string ComputeFinancialScore(
        decimal overdueDebt, decimal outstandingFines, decimal compliancePercent, bool hasOverdueLoan)
    {
        if (overdueDebt > 0 || hasOverdueLoan)
            return "D"; // Defaulter - genuinely behind, more than one cycle late
        if (outstandingFines > 0 || compliancePercent < 70m)
            return "C"; // Risk - something outstanding or falling behind
        if (compliancePercent >= 90m)
            return "A"; // Excellent
        return "B"; // Good
    }

    // NEW (Member Financial Profile completion, "Loans" section, Phase 1
    // of loan risk): deliberately a fact-based label (what happened),
    // not a High/Medium/Low judgement call (what it means) - severity
    // (how much money, how many loans) is left for a future numeric
    // 0-100 risk SCORE once Loan Portfolio/Reports Engine exist to make
    // use of it. This only ever answers "has this member currently, or
    // ever, had a late loan" - nothing about how bad.
    private static string ComputeLoanRiskCategory(List<Loan> loans, bool hasOverdueLoan)
    {
        if (hasOverdueLoan)
            return "CurrentOverdue"; // has an Active loan overdue right now

        if (loans.Any(l => l.LatePenaltyCharged))
            return "PreviousDelinquency"; // no current issue, but was late before

        return "GoodStanding"; // never late, nothing overdue right now
    }

}
