using Microsoft.EntityFrameworkCore;
using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Awamu 2 (2026-09-19): Ukarabati wa data ya bug-era (Ukonga demo + yoyote).
/// 
/// TATIZO: data iliyoingizwa kabla ya Awamu 1 ilikuwa na:
///   1) Counter drift — TotalContributionsCount/AdvanceBalance hazikusasishwa
///      na LedgerService (mchango wa mkono) → GroupMember ina namba za zamani.
///   2) ComplianceSnapshots tupu — UpsertSnapshotAsync haikuitwa KAMWE kabla
///      ya 2026-09-19, hivyo ripoti za Uzingatiaji zilisoma jedwali tupu.
///   3) Loan repayment gaps — import haikuwa na hatua ya rejesho, hivyo pesa
///      iliyopaswa kwenda mikopo ilienda akiba/KIANZIO kimakosa.
///
/// HUDUMA HII:
///   - HAIHARIBU ledger — inasoma tu na kurekebisha counters + snapshots.
///   - Preview ni READ-ONLY (hakuna SaveChanges).
///   - Repair ni idempotent — ukiirudia, inarudi 0 corrected.
///   - Loan gaps ni ripoti TU (hatuhamishi pesa moja kwa moja — uamuzi wa
///     uongozi, si wa mfumo).
/// </summary>
public class DataRepairService
{
    private readonly ApplicationDbContext _context;
    private readonly MemberCounterSyncService _counterSync;
    private readonly ComplianceSnapshotService _snapshotService;

    public DataRepairService(
        ApplicationDbContext context,
        MemberCounterSyncService counterSync,
        ComplianceSnapshotService snapshotService)
    {
        _context = context;
        _counterSync = counterSync;
        _snapshotService = snapshotService;
    }

    public async Task<DataRepairPreviewDto> GetPreviewAsync(Guid groupId, int monthsBack = 24)
    {
        var group = await _context.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group is null)
            throw new InvalidOperationException($"Group {groupId} not found");

        var members = await _context.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Include(m => m.User)
            .ToListAsync();

        var memberIds = members.Select(m => m.Id).ToList();

        // ── 1. Counter drift (bila SaveChanges) ────────────────────────
        var savingsAccounts = await _context.Accounts
            .Where(a => memberIds.Contains(a.GroupMemberId) && a.Type == AccountType.Savings)
            .ToDictionaryAsync(a => a.GroupMemberId, a => a.Id);

        var accountIds = savingsAccounts.Values.ToList();

        var ledgerTotals = await _context.LedgerEntries
            .Where(e => accountIds.Contains(e.AccountId) && e.Type == TransactionType.Contribution)
            .GroupBy(e => e.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count(), Total = g.Sum(e => e.Amount) })
            .ToListAsync();

        var byAccount = ledgerTotals.ToDictionary(t => t.AccountId, t => t);

        var counterDrifts = new List<CounterDriftRowDto>();
        int driftCount = 0;

        foreach (var member in members)
        {
            int newCount = 0;
            decimal newBalance = 0m;

            if (savingsAccounts.TryGetValue(member.Id, out var accId) &&
                byAccount.TryGetValue(accId, out var totals))
            {
                newCount = totals.Count;
                newBalance = totals.Total;
            }

            bool hasDrift = member.TotalContributionsCount != newCount ||
                            member.AdvanceBalance != newBalance;

            if (hasDrift) driftCount++;

            counterDrifts.Add(new CounterDriftRowDto(
                member.Id,
                member.User?.FullName ?? member.Id.ToString()[..8],
                member.TotalContributionsCount,
                newCount,
                member.AdvanceBalance,
                newBalance,
                hasDrift));
        }

        // ── 2. Missing snapshots ───────────────────────────────────────
        // Angalia miezi ya nyuma kuanzia group.CreatedAt hadi leo.
        // Tunachukua max(monthsBack, miezi tangu group iumbwe).
        var now = DateTime.UtcNow;
        var startMonth = new DateTime(group.CreatedAt.Year, group.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthsSinceCreation = ((now.Year - startMonth.Year) * 12) + now.Month - startMonth.Month + 1;
        int monthsToCheck = Math.Min(monthsBack, Math.Max(monthsSinceCreation, 1));

        var checkFrom = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthsToCheck + 1);

        var snapshots = await _context.ComplianceSnapshots
            .Where(s => s.GroupId == groupId && s.Month >= checkFrom)
            .GroupBy(s => s.Month)
            .Select(g => new { Month = g.Key, Count = g.Count() })
            .ToListAsync();

        var snapByMonth = snapshots.ToDictionary(x => x.Month, x => x.Count);

        var missingRows = new List<MissingSnapshotRowDto>();
        int totalMissing = 0;

        for (int i = 0; i < monthsToCheck; i++)
        {
            var month = checkFrom.AddMonths(i);
            int actual = snapByMonth.TryGetValue(month, out var c) ? c : 0;
            int expected = members.Count;
            if (actual < expected)
            {
                int missing = expected - actual;
                totalMissing += missing;
                missingRows.Add(new MissingSnapshotRowDto(month, expected, actual, missing));
            }
        }

        // ── 3. Loan repayment gaps (ripoti tu) ─────────────────────────
        var loans = await _context.Loans
            .AsNoTracking()
            .Where(l => l.GroupId == groupId)
            .ToListAsync();

        var loanGaps = new List<LoanRepaymentGapRowDto>();

        // Kwa kila mwezi wa miezi 12 iliyopita, angalia kama kuna mkopo
        // uliokuwa Active na expected > 0 lakini paid = 0, na akiba iliingia.
        int loanCheckMonths = Math.Min(12, monthsToCheck);
        var loanCheckFrom = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-loanCheckMonths + 1);

        // Pre-load ledger ya marejesho na michango kwa miezi hiyo
        var repaymentEntries = await _context.LedgerEntries
            .Where(e => e.GroupId == groupId &&
                        e.Type == TransactionType.LoanRepayment &&
                        e.CreatedAt >= loanCheckFrom)
            .ToListAsync();

        var savingsEntries = await _context.LedgerEntries
            .Where(e => e.GroupId == groupId &&
                        e.Type == TransactionType.Contribution &&
                        e.CreatedAt >= loanCheckFrom)
            .ToListAsync();

        // GroupId -> UserId -> month -> amount
        var repaymentByUserMonth = repaymentEntries
            .GroupBy(e => new { e.UserId, Month = new DateTime(e.CreatedAt.Year, e.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc) })
            .ToDictionary(g => (g.Key.UserId, g.Key.Month), g => g.Sum(x => x.Amount));

        var savingsByUserMonth = savingsEntries
            .GroupBy(e => new { e.UserId, Month = new DateTime(e.CreatedAt.Year, e.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc) })
            .ToDictionary(g => (g.Key.UserId, g.Key.Month), g => g.Sum(x => x.Amount));

        foreach (var member in members)
        {
            var memberLoans = loans.Where(l => l.GroupMemberId == member.Id).ToList();
            if (memberLoans.Count == 0) continue;

            for (int i = 0; i < loanCheckMonths; i++)
            {
                var month = loanCheckFrom.AddMonths(i);
                decimal expected = LoanService.GetExpectedRepaymentsTotal(memberLoans, month.Year, month.Month);
                if (expected <= 0) continue;

                repaymentByUserMonth.TryGetValue((member.UserId, month), out var paid);
                savingsByUserMonth.TryGetValue((member.UserId, month), out var savingsPosted);

                if (paid < expected && savingsPosted > 0)
                {
                    string note = paid == 0
                        ? $"Hakuna rejesho mwezi huu, lakini akiba TSH {savingsPosted:N0} iliingizwa — huenda ilipaswa kuwa rejesho (bug-era)"
                        : $"Rejesho sehemu TSH {paid:N0}/{expected:N0}, akiba TSH {savingsPosted:N0} — huenda ziada ilipaswa kuwa rejesho";

                    loanGaps.Add(new LoanRepaymentGapRowDto(
                        member.Id,
                        member.User?.FullName ?? member.Id.ToString()[..8],
                        month,
                        expected,
                        paid,
                        savingsPosted,
                        note));
                }
            }
        }

        var warnings = new List<string>();
        if (driftCount > 0)
            warnings.Add($"Counter drift: wanachama {driftCount} wana namba tofauti na ledger (mchango wa mkono haukusasisha counters).");
        if (totalMissing > 0)
            warnings.Add($"Snapshots pungufu: {totalMissing} snapshots hazipo (jedwali lilikuwa tupu kabla ya 2026-09-19).");
        if (loanGaps.Count > 0)
            warnings.Add($"Mapengo ya rejesho: miezi {loanGaps.Count} ambapo rejesho lilikosekana lakini akiba iliingizwa — angalia kama ni data ya bug-era.");

        bool hasIssues = driftCount > 0 || totalMissing > 0 || loanGaps.Count > 0;

        return new DataRepairPreviewDto(
            groupId,
            group.Name,
            DateTime.UtcNow,
            members.Count,
            counterDrifts.Where(c => c.HasDrift).OrderBy(c => c.MemberName).ToList(),
            driftCount,
            missingRows.OrderBy(m => m.Month).ToList(),
            totalMissing,
            loanGaps.OrderBy(g => g.Month).ThenBy(g => g.MemberName).ToList(),
            warnings,
            hasIssues);
    }

    public async Task<DataRepairResultDto> RepairCountersAsync(Guid groupId)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) throw new InvalidOperationException($"Group {groupId} not found");

        var result = await _counterSync.ReconcileGroupAsync(groupId);
        await _context.SaveChangesAsync();

        return new DataRepairResultDto(
            groupId,
            "counters",
            DateTime.UtcNow,
            result.MembersChecked,
            result.MembersCorrected,
            0,
            0,
            new List<string>
            {
                result.HadDrift
                    ? $"Counter za wanachama {result.MembersCorrected} zimerekebishwa."
                    : "Counter zote zilikuwa sawa — hakuna kilichorekebishwa.",
                $"Count drift: {result.CountDrift}, Balance drift: TSH {result.BalanceDrift:N0}"
            });
    }

    public async Task<DataRepairResultDto> RepairSnapshotsAsync(Guid groupId, int monthsBack = 24)
    {
        var group = await _context.Groups
            .Include(g => g.Settings)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group is null) throw new InvalidOperationException($"Group {groupId} not found");

        var settings = group.Settings;
        if (settings == null)
            throw new InvalidOperationException("Group settings not found");

        var members = await _context.GroupMembers
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var startMonth = new DateTime(group.CreatedAt.Year, group.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthsSinceCreation = ((now.Year - startMonth.Year) * 12) + now.Month - startMonth.Month + 1;
        int monthsToCheck = Math.Min(monthsBack, Math.Max(monthsSinceCreation, 1));
        var checkFrom = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthsToCheck + 1);

        int created = 0, updated = 0;

        // Pre-load accounts, loans, ledger, debts, fines
        var memberIds = members.Select(m => m.Id).ToList();
        var savingsAccounts = await _context.Accounts
            .Where(a => memberIds.Contains(a.GroupMemberId) && a.Type == AccountType.Savings)
            .ToDictionaryAsync(a => a.GroupMemberId, a => a.Id);

        var allLoans = await _context.Loans
            .Where(l => l.GroupId == groupId)
            .ToListAsync();

        var loansByMember = allLoans.GroupBy(l => l.GroupMemberId).ToDictionary(g => g.Key, g => g.ToList());

        for (int i = 0; i < monthsToCheck; i++)
        {
            var periodStart = checkFrom.AddMonths(i);
            var periodEnd = periodStart.AddMonths(1);

            foreach (var member in members)
            {
                // Skip members who joined after this month
                if (member.JoinedAt > periodEnd) continue;
                if (member.Status == MemberStatus.Exited) continue;

                // Paid this month (contribution)
                decimal paidThisMonth = 0m;
                if (savingsAccounts.TryGetValue(member.Id, out var savingsAccId))
                {
                    paidThisMonth = await _context.LedgerEntries
                        .Where(l => l.AccountId == savingsAccId &&
                                    l.Type == TransactionType.Contribution &&
                                    l.CreatedAt >= periodStart &&
                                    l.CreatedAt < periodEnd)
                        .SumAsync(l => (decimal?)l.Amount) ?? 0m;
                }

                // Expected contribution
                decimal expectedContribution = settings.Contribution.MonthlyContribution;

                // Consecutive missed months (compute from debts)
                int consecutiveMissed = await ComputeConsecutiveMissedMonthsAsync(member.Id, periodStart);

                // Loan repayment
                decimal expectedRepayment = 0m;
                decimal paidRepayment = 0m;

                if (loansByMember.TryGetValue(member.Id, out var mLoans))
                {
                    expectedRepayment = LoanService.GetExpectedRepaymentsTotal(mLoans, periodStart.Year, periodStart.Month);

                    paidRepayment = await _context.LedgerEntries
                        .Where(l => l.GroupId == groupId &&
                                    l.UserId == member.UserId &&
                                    l.Type == TransactionType.LoanRepayment &&
                                    l.CreatedAt >= periodStart &&
                                    l.CreatedAt < periodEnd)
                        .SumAsync(l => (decimal?)l.Amount) ?? 0m;
                }

                // Check if snapshot exists
                var existing = await _context.ComplianceSnapshots
                    .FirstOrDefaultAsync(s => s.GroupMemberId == member.Id && s.Month == periodStart);

                bool isNew = existing == null;

                await _snapshotService.UpsertSnapshotAsync(
                    _context, groupId, member.Id, periodStart,
                    expectedContribution,
                    Math.Min(paidThisMonth, expectedContribution),
                    consecutiveMissed,
                    member.Status,
                    expectedRepayment,
                    paidRepayment);

                if (isNew) created++; else updated++;
            }
        }

        await _context.SaveChangesAsync();

        return new DataRepairResultDto(
            groupId,
            "snapshots",
            DateTime.UtcNow,
            members.Count,
            0,
            created,
            updated,
            new List<string>
            {
                $"Snapshots zimejengwa upya kwa miezi {monthsToCheck} (kutoka {checkFrom:yyyy-MM} hadi {now:yyyy-MM}).",
                $"Mpya: {created}, Zilizosasishwa: {updated}"
            });
    }

    public async Task<DataRepairResultDto> FullRepairAsync(Guid groupId, int monthsBack = 24)
    {
        var counterResult = await RepairCountersAsync(groupId);
        var snapshotResult = await RepairSnapshotsAsync(groupId, monthsBack);

        return new DataRepairResultDto(
            groupId,
            "full",
            DateTime.UtcNow,
            counterResult.MembersChecked,
            counterResult.MembersCorrected,
            snapshotResult.SnapshotsCreated,
            snapshotResult.SnapshotsUpdated,
            new List<string>(counterResult.Details.Concat(snapshotResult.Details))
            {
                "Ukarabati kamili umekamilika — counters + snapshots."
            });
    }

    private async Task<int> ComputeConsecutiveMissedMonthsAsync(Guid groupMemberId, DateTime periodStart)
    {
        // Hesabu kurudi nyuma kutoka mwezi huu: kila mwezi ambao una Debt ya
        // Outstanding au hakuna mchango, ni missed. Inasimama mara tu mwezi
        // uliolipwa unapopatikana.
        int consecutive = 0;
        var checkMonth = periodStart;

        // Angalia hadi miezi 24 nyuma (kikomo cha usalama)
        for (int i = 0; i < 24; i++)
        {
            bool hasDebt = await _context.Debts
                .AnyAsync(d => d.GroupMemberId == groupMemberId && d.Period == checkMonth);

            if (hasDebt)
            {
                consecutive++;
                checkMonth = checkMonth.AddMonths(-1);
            }
            else
            {
                break;
            }
        }

        return consecutive;
    }
}
