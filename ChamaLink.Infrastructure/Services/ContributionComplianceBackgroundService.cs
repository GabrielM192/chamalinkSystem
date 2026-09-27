using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services;

// Sprint 1 gap: "Automatic Fine Generation Haipo" - WelfarePenaltyBackgroundService
// only ever checked Event/welfare deadlines, never Monthly Contribution
// due dates. This runs once a day and, for every Monthly Contribution /
// Hybrid group, once a member's grace period for the current month has
// passed without them reaching that month's target:
//   1. Records the shortfall as a Debt (the missing principal).
//   2. Issues a Fine for GroupSettings.LateFine (the penalty for being late).
// Both are created at most once per member per month (guarded by
// checking for an existing Debt/Fine for that GroupMemberId+Period), so
// re-running the job every day doesn't double-charge anyone.
public class ContributionComplianceBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContributionComplianceBackgroundService> _logger;

    public ContributionComplianceBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ContributionComplianceBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Contribution Compliance Background Service imeanza...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueContributionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kosa limetokea wakati wa kuangalia michango iliyochelewa.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task ProcessOverdueContributionsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var accountResolver = scope.ServiceProvider.GetRequiredService<AccountResolverService>();
        var fineService = scope.ServiceProvider.GetRequiredService<FineService>();
        var debtService = scope.ServiceProvider.GetRequiredService<DebtService>();
        var snapshotService = scope.ServiceProvider.GetRequiredService<ComplianceSnapshotService>();

        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var groups = await context.Groups
            .Include(g => g.Settings)
            .Where(g => g.Type == GroupType.MonthlySavings || g.Type == GroupType.Hybrid)
            .ToListAsync();

        foreach (var group in groups)
        {
            var settings = group.Settings;
            if (settings == null || settings.Contribution.MonthlyContribution <= 0)
                continue;

            var graceDeadline = periodStart
                .AddDays(Math.Max(0, settings.Contribution.DueDateDay - 1))
                .AddDays(settings.Contribution.GracePeriodDays);

            if (now < graceDeadline)
                continue; // bado tuko ndani ya grace period ya mwezi huu

            // BUG FIX: this used to be `Status != Exited`, which meant
            // Suspended and Inactive members - who have no live obligation
            // to keep contributing - were still being charged a fresh
            // Debt + Fine every month like an Active member. Only members
            // who are still expected to be contributing (Active, or
            // Warning - already flagged but still active) get processed
            // here.
            var members = await context.GroupMembers
                .Where(m => m.GroupId == group.Id &&
                            (m.Status == MemberStatus.Active || m.Status == MemberStatus.Warning))
                .ToListAsync();

            foreach (var member in members)
            {
                // Usimtoze mwanachama aliyejiunga baada ya deadline ya
                // mwezi huu kupita - hakupata nafasi ya kulipa.
                if (member.JoinedAt > graceDeadline)
                    continue;

                var savingsAccount = await accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.Savings);

                decimal paidThisMonth = await context.LedgerEntries
                    .Where(l => l.AccountId == savingsAccount.Id &&
                                l.Type == TransactionType.Contribution &&
                                l.CreatedAt >= periodStart)
                    .SumAsync(l => (decimal?)l.Amount) ?? 0m;

                decimal shortfall = settings.Contribution.MonthlyContribution - paidThisMonth;

                int consecutiveMissedMonths = 0;

                if (shortfall <= 0)
                {
                    // Ukonga Rules Specification v1.2, sehemu 4 (Phase 2 —
                    // Member Status Engine): mwezi huu umelipwa kikamilifu
                    // -> ConsecutiveMissedMonths inarudi 0 kwa mwezi huu
                    // pekee, na Status inarudi Active (Warning -> Active
                    // recovery). Hii HAIFUTI madeni ya miezi ya nyuma -
                    // Debt records za miezi iliyopita zinabaki Outstanding
                    // hadi zilipwe wazi (angalia DebtService.ClearWithPaymentAsync).
                    //
                    // Inatumika kwa Active/Warning pekee - Inactive/NonActive
                    // hairudishwi hapa kamwe: "ENGINE INASIMAMA HAPO... 
                    // Haifanyi automatic reactivation" (sehemu 4). Uamuzi wa
                    // kumrejesha NonActive member ni wa uongozi (manual
                    // review), si wa background job hii.
                    if (member.Status == MemberStatus.Warning)
                    {
                        member.Status = MemberStatus.Active;
                    }
                }
                else
                {
                bool alreadyProcessed =
                    await context.Debts.AnyAsync(d => d.GroupMemberId == member.Id && d.Period == periodStart) ||
                    await context.Fines.AnyAsync(f => f.GroupMemberId == member.Id && f.Period == periodStart);

                if (!alreadyProcessed)
                {
                    // The Debt always reflects the true shortfall - that money
                    // is genuinely still owed regardless of how small it is.
                    await debtService.RecordShortfallAsync(
                        group.Id, member.Id, member.UserId, shortfall, periodStart,
                        $"Deni la mchango wa mwezi {periodStart:MMMM yyyy}");

                    // NEW (Fine threshold / "grace amount" gap): a Fine is a
                    // penalty for being late, not for the shortfall itself, so
                    // a group can define a tolerance (MinimumShortfallForFine)
                    // below which a small shortfall still becomes a Debt but
                    // is not treated as fine-worthy lateness. Defaults to 0,
                    // i.e. any shortfall at all is fined - same as before.
                    if (settings.Contribution.LateFine > 0 && shortfall >= settings.Contribution.MinimumShortfallForFine)
                    {
                        await fineService.IssueFineAsync(
                            group.Id, member.Id, member.UserId, settings.Contribution.LateFine,
                            FineReasonType.LateMonthlyContribution,
                            $"Faini ya kuchelewa mchango wa mwezi {periodStart:MMMM yyyy}",
                            period: periodStart);
                    }
                }

                // Ukonga Rules Specification v1.2, sehemu 4 (Phase 2 — Member
                // Status Engine). ConsecutiveMissedMonths sasa inahesabiwa
                // moja kwa moja kutoka kwenye Debt records halisi (rekodi
                // moja kwa kila mwezi uliokosekana kwa mwanachama huyu),
                // badala ya heuristic ya zamani ("hakuna mchango miezi 3
                // zilizopita"). Hii pia ndiyo inayotumia
                // MaxConsecutiveMissedMonths kutoka GroupSettings (Phase 1) -
                // thamani ya kikundi, si tena 3 iliyokuwa hardcoded.
                consecutiveMissedMonths = await ComputeConsecutiveMissedMonthsAsync(
                    context, member.Id, periodStart);

                if (consecutiveMissedMonths >= settings.Contribution.MaxConsecutiveMissedMonths)
                {
                    // NonActive (sehemu 4). MemberStatus.Inactive ndiyo
                    // thamani iliyopo kwenye enum inayolingana na "NonActive"
                    // ya spec - angalia sehemu 4: "ENGINE INASIMAMA HAPO.
                    // Haifanyi automatic reactivation... Inasubiri uamuzi wa
                    // uongozi." (Mwenyekiti/Mtunza Hazina ndiye anaamua
                    // Reactivate / Keep NonActive / Remove - nje ya scope ya
                    // Phase 2 hii.)
                    member.Status = MemberStatus.Inactive;
                }
                else
                {
                    // 1..(Max-1) miezi mfululizo -> Warning (sehemu 4,
                    // jedwali la Status thresholds).
                    member.Status = MemberStatus.Warning;
                }
                }

                // ── COMPLIANCE SNAPSHOT (wiring fix — 2026-09-19) ──────
                // BUG ILIYOKUWEPO: UpsertSnapshotAsync ilikuwa haiitwi
                // KAMWE kwenye codebase — jedwali la ComplianceSnapshots
                // lilikuwa tupu milele, na ripoti zote za Uzingatiaji
                // (sehemu 5/7/8) zilisoma jedwali tupu. Hii ndiyo sababu
                // moja kuu ya "taarifa zisizo za kweli". Sasa kila mwezi
                // kwa kila mwanachama anayeprocesswa, snapshot inaandikwa
                // — pamoja na wajibu wa marejesho ya mikopo (Awamu 1).
                var memberLoans = await context.Loans
                    .Where(l => l.GroupMemberId == member.Id && l.Status == LoanStatus.Active)
                    .ToListAsync();

                decimal expectedRepayment = LoanService.GetExpectedRepaymentsTotal(
                    memberLoans, periodStart.Year, periodStart.Month);

                decimal paidRepayment = await context.LedgerEntries
                    .Where(l => l.GroupId == group.Id &&
                                l.UserId == member.UserId &&
                                l.Type == TransactionType.LoanRepayment &&
                                l.CreatedAt >= periodStart &&
                                l.CreatedAt < periodStart.AddMonths(1))
                    .SumAsync(l => (decimal?)l.Amount) ?? 0m;

                await snapshotService.UpsertSnapshotAsync(
                    context, group.Id, member.Id, periodStart,
                    settings.Contribution.MonthlyContribution,
                    Math.Min(paidThisMonth, settings.Contribution.MonthlyContribution),
                    consecutiveMissedMonths, member.Status,
                    expectedRepayment, paidRepayment);
            }
        }

        await context.SaveChangesAsync();
    }

    // Ukonga Rules Specification v1.2, sehemu 4: "ConsecutiveMissedMonths
    // inarudi 0 mara tu mwezi mmoja ukilipwa kikamilifu." Kwa hiyo
    // tunahesabu kwa kurudi nyuma kutoka mwezi wa sasa, mwezi kwa mwezi,
    // tukihesabu Debt moja kwa kila mwezi uliokosekana, na kusimama mara
    // tu tunapokutana na mwezi usio na Debt (yaani ulilipwa kikamilifu, au
    // ni kabla ya mwanachama kujiunga). Hii haihitaji field mpya ya
    // ConsecutiveMissedMonths kwenye GroupMember - Debt table (Sprint 1)
    // tayari ina rekodi moja kwa kila mwezi uliokosekana, kwa hiyo ni
    // "source of truth" ya kutosha kwa Phase 2. Field ya kudumu
    // itaongezwa kwenye ComplianceSnapshot (Phase 4) kama optimization.
    private static async Task<int> ComputeConsecutiveMissedMonthsAsync(
        ApplicationDbContext context, Guid groupMemberId, DateTime currentPeriodStart)
    {
        int count = 0;
        var period = currentPeriodStart;

        while (await context.Debts.AnyAsync(d => d.GroupMemberId == groupMemberId && d.Period == period))
        {
            count++;
            period = period.AddMonths(-1);
        }

        return count;
    }
}
