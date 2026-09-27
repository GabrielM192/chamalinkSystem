using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

// Ukonga Rules Specification v1.2, sehemu 6 (Phase 4). Writes/updates the
// ONE ComplianceSnapshot row per (GroupMember, Month) - a pure reporting
// layer over what Phase 2 (Member Status Engine) and Phase 3 (Fine Engine
// + ARCH-001 debt allocation) have already decided. This service never
// changes Status, never creates a Debt or Fine, never moves money - it
// only reads what those two phases already produced and records it, so
// Reports/Dashboard (Phase 5) can query one table instead of re-scanning
// LedgerEntries/Debts/Fines every time.
public class ComplianceSnapshotService
{
    // Upserts the snapshot for one member for one month. Called by
    // ContributionComplianceBackgroundService right after it has finished
    // deciding this member's Status for the period, so every number here
    // is already final for this run. Safe to call again for the same
    // (member, month) - e.g. the daily re-run - it always overwrites in
    // place rather than appending a duplicate row (sehemu 6: "snapshot
    // MOJA kwa kila mwanachama, kila mwezi").
    public async Task UpsertSnapshotAsync(
        ApplicationDbContext context,
        Guid groupId,
        Guid groupMemberId,
        DateTime periodStart,
        decimal expectedContribution,
        decimal paidContribution,
        int consecutiveMissedMonths,
        MemberStatus status,
        decimal expectedLoanRepayment = 0m,
        decimal paidLoanRepayment = 0m)
    {
        // TotalMissedMonths (sehemu 4: cumulative, historia yote,
        // "HAIWEZI kupungua tena") - every Debt row is exactly one missed
        // month for this member, whether or not it has since been
        // cleared, so counting all of them is the correct all-time total.
        int totalMissedMonths = await context.Debts
            .CountAsync(d => d.GroupMemberId == groupMemberId);

        // OutstandingContributionDebt (sehemu 4/6: deni la MCHANGO pekee -
        // si faini, si mkopo, angalia "Kwa nini Debt inagawanywa kwa
        // aina"). Sum of what's still unpaid across every open Debt, not
        // just the current month's - a fully-paid current month does not
        // touch this (ARCH-001 / DebtAllocationStrategy is the only thing
        // that reduces it).
        decimal outstandingContributionDebt = await context.Debts
            .Where(d => d.GroupMemberId == groupMemberId && d.Status == DebtStatus.Outstanding)
            .SumAsync(d => (decimal?)(d.Amount - d.AmountCleared)) ?? 0m;

        // This month's late-contribution fine, if one was issued (sehemu
        // 6: FineIssuedAmount/FinePaidAmount are amount-based, not a bool,
        // because a member can pay only part of a fine).
        var fine = await context.Fines
            .Where(f => f.GroupMemberId == groupMemberId &&
                        f.Period == periodStart &&
                        f.ReasonType == FineReasonType.LateMonthlyContribution)
            .FirstOrDefaultAsync();

        decimal fineIssuedAmount = fine?.Amount ?? 0m;
        decimal finePaidAmount = fine?.AmountPaid ?? 0m;

        var snapshot = await context.ComplianceSnapshots
            .FirstOrDefaultAsync(s => s.GroupMemberId == groupMemberId && s.Month == periodStart);

        if (snapshot == null)
        {
            snapshot = new ComplianceSnapshot
            {
                Id = Guid.NewGuid(),
                GroupMemberId = groupMemberId,
                GroupId = groupId,
                Month = periodStart,
                CreatedAt = DateTime.UtcNow
            };
            context.ComplianceSnapshots.Add(snapshot);
        }

        snapshot.ExpectedContribution = expectedContribution;
        snapshot.PaidContribution = paidContribution;
        snapshot.ExpectedLoanRepayment = expectedLoanRepayment;
        snapshot.PaidLoanRepayment = paidLoanRepayment;
        snapshot.FineIssuedAmount = fineIssuedAmount;
        snapshot.FinePaidAmount = finePaidAmount;
        // OutstandingFineAmount is derived (sehemu 6), always recomputed
        // alongside the two fields above rather than tracked separately,
        // so it can never drift out of sync with them.
        snapshot.OutstandingFineAmount = fineIssuedAmount - finePaidAmount;
        snapshot.TotalMissedMonths = totalMissedMonths;
        snapshot.ConsecutiveMissedMonths = consecutiveMissedMonths;
        snapshot.OutstandingContributionDebt = outstandingContributionDebt;
        snapshot.Status = status;
    }
}
