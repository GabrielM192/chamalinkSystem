using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services;

// LOAN ENGINE V2 (item 3/3): LoanSettings.LatePenaltyAmount existed on
// GroupSettings since the Owned Types refactor, but nothing ever read
// it - a loan could sit overdue forever with zero consequence beyond
// Loan.IsOverdue quietly flipping to true. This mirrors
// WelfarePenaltyBackgroundService's shape (same scan-scope-fine-save
// pattern), but for loans instead of welfare-fund events:
//   1. Find every Active loan whose DueDate has passed and that has
//      not already been penalized (LatePenaltyCharged == false).
//   2. Read the owning group's own LoanSettings.LatePenaltyAmount - a
//      group that never set one (0, the default) is left alone
//      entirely, same "field only until a group opts in" behaviour as
//      RepaymentDays/MaxLoanMultiplier before this.
//   3. Issue a Fine (FineReasonType.LoanOverdue) through FineService,
//      so the penalty lands in the same place - and can be paid off
//      the same way - as every other fine in the app, rather than
//      quietly inflating the loan's own PrincipalAmount/InterestAmount
//      figures.
//   4. Flip Loan.LatePenaltyCharged so re-running this job every day
//      never charges the same loan twice.
public class LoanPenaltyBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LoanPenaltyBackgroundService> _logger;

    public LoanPenaltyBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<LoanPenaltyBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Loan Penalty Background Service imeanza...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueLoansAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kosa limetokea wakati wa kuangalia mikopo iliyochelewa.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task ProcessOverdueLoansAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fineService = scope.ServiceProvider.GetRequiredService<FineService>();

        var now = DateTime.UtcNow;

        // Only Active + overdue + not-yet-penalized loans. Repaid/
        // Defaulted loans are excluded on purpose - a loan a group has
        // already written off or that a member already finished paying
        // should not keep accumulating fresh late fees.
        var overdueLoans = await context.Loans
            .Where(l => l.Status == LoanStatus.Active
                        && l.DueDate < now
                        && !l.LatePenaltyCharged)
            .ToListAsync();

        if (overdueLoans.Count == 0)
            return;

        // Grouped by GroupId so LoanSettings is read once per group
        // instead of once per loan.
        foreach (var groupLoans in overdueLoans.GroupBy(l => l.GroupId))
        {
            var groupId = groupLoans.Key;

            var settings = await context.GroupSettings
                .FirstOrDefaultAsync(s => s.GroupId == groupId);

            decimal penaltyAmount = settings?.Loan.LatePenaltyAmount ?? 0m;

            // A group that has never set a penalty (still the 0m
            // default) is left exactly as before - IsOverdue still
            // shows the loan as late, just with no financial
            // consequence, same as today.
            if (penaltyAmount <= 0m)
                continue;

            foreach (var loan in groupLoans)
            {
                _logger.LogInformation(
                    "Inatoza faini ya kuchelewa mkopo: LoanId={LoanId}, GroupMemberId={GroupMemberId}",
                    loan.Id, loan.GroupMemberId);

                await fineService.IssueFineAsync(
                    loan.GroupId,
                    loan.GroupMemberId,
                    loan.UserId,
                    penaltyAmount,
                    FineReasonType.LoanOverdue,
                    $"Faini ya kuchelewa kurejesha mkopo (Tarehe ya mwisho: {loan.DueDate:yyyy-MM-dd})",
                    dueDate: loan.DueDate);

                loan.LatePenaltyCharged = true;
            }
        }

        await context.SaveChangesAsync();
    }
}
