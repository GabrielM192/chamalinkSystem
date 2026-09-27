using ChamaLink.Domain; // Au ChamaLink.Domain.Entities
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services;

public class WelfarePenaltyBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WelfarePenaltyBackgroundService> _logger;

    public WelfarePenaltyBackgroundService(
        IServiceProvider serviceProvider, 
        ILogger<WelfarePenaltyBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Welfare Penalty Background Service imeanza...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredEventsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kosa limetokea wakati wa kuangalia Welfare Penalties.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task ProcessExpiredEventsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var accountResolver = scope.ServiceProvider.GetRequiredService<AccountResolverService>();
        var fineService = scope.ServiceProvider.GetRequiredService<FineService>();

        var now = DateTime.UtcNow;

        var expiredEvents = await context.GroupEvents
            .Where(e => !e.IsResolved && e.DeadlineDate <= now)
            .ToListAsync();

        foreach (var evt in expiredEvents)
        {
            _logger.LogInformation($"Inashughulikia Faini za Event: {evt.Title}");

            var groupMembers = await context.GroupMembers
                .Where(m => m.GroupId == evt.GroupId)
                .ToListAsync();

            // NEW (welfare mode configurability): different groups mean
            // different things by "WelfareDeduction" - see WelfareMode in
            // Enums.cs. Load the group's own choice once per event instead
            // of assuming DeductBalance for everyone.
            var groupSettings = await context.GroupSettings
                .FirstOrDefaultAsync(s => s.GroupId == evt.GroupId);
            var welfareMode = groupSettings?.Event.WelfareMode ?? WelfareMode.DeductBalance;

            // (audit finding: "minimumThreshold ni dhana ile ile
            // na GroupSettings.MinimumReserveBalance"): these two used to be
            // a hardcoded local constant (20000m) here AND a separate,
            // already-configurable field used elsewhere (EventService's
            // deduction-floor check) for what is really the same idea - the
            // floor a member's welfare balance should not be allowed to sit
            // below. They are now the same field. NOTE for group admins: a
            // group that never explicitly set MinimumReserveBalance had it
            // default to 0 - the fine below will effectively never trigger
            // for such a group until MinimumReserveBalance is set to a real
            // value (e.g. 20000, matching the old global hardcoded default).
            var minimumThreshold = groupSettings?.Financial.MinimumReserveBalance ?? 0m;

            // Replaces the `fineAmount = 5000m` constant that
            // used to be hardcoded here. EventSettings.WelfareFineAmount
            // defaults to 5000m (see GroupSettingsModules.cs), so existing
            // groups keep the exact same fine amount until an admin
            // explicitly changes it.
            if (groupSettings is null)
            {
                // Hakuna kiasi cha faini "cha kubuni": kikundi kisicho na mipangilio hakitozwi faini ya ustawi.
                _logger.LogWarning("Kikundi {GroupId} hakina GroupSettings — faini ya ustawi imerukwa kwa tukio {EventId}.", evt.GroupId, evt.Id);
                continue;
            }
            var fineAmount = groupSettings.Event.WelfareFineAmount;

            foreach (var member in groupMembers)
            {
                // BUG FIX: this used to filter by AccountId == member.Id, but
                // member.Id is a GroupMember Id, not an Account Id, so this
                // check was always comparing against the wrong column and
                // could never correctly match the welfare entries created
                // by EventService. Now we look up the member's real welfare
                // ("SocialFund") account first, same as everywhere else.
                var welfareAccount = await accountResolver.GetOrCreateAccountAsync(
                    member.Id, AccountType.SocialFund);

                var deductions = await context.LedgerEntries
                    .Where(l => l.AccountId == welfareAccount.Id && l.Type == TransactionType.WelfareDeduction)
                    .SumAsync(l => l.Amount);

                var topUps = await context.LedgerEntries
                    .Where(l => l.AccountId == welfareAccount.Id && l.Type == TransactionType.WelfareTopUp)
                    .SumAsync(l => l.Amount);

                // UPDATED (was hardcoded `topUps - deductions`): under
                // ContributePot mode a deduction is itself a mandatory
                // contribution, so it should raise the balance the same
                // way a top-up does, not lower it.
                decimal welfareBalance = welfareMode == WelfareMode.ContributePot
                    ? topUps + deductions
                    : topUps - deductions;

                if (welfareBalance < minimumThreshold)
                {
                    // BUG FIX: this used to write the FineIssue LedgerEntry
                    // straight against the member's SocialFund (welfare)
                    // account. MkobaImportController's fine-payment step only
                    // ever looked at the member's dedicated Fine account, so
                    // a welfare-deadline fine created here could never
                    // actually be found or paid off during an M-Koba import.
                    // FineService always resolves the real Fine account, so
                    // this fine now lives in the same place as every other
                    // fine and can be paid down consistently. It is also
                    // recorded as a structured Fine entity (Sprint 1 gap)
                    // instead of only a raw ledger row.
                    await fineService.IssueFineAsync(
                        evt.GroupId, member.Id, member.UserId, fineAmount,
                        FineReasonType.WelfareBalanceBelowMinimum,
                        $"Faini ya kuchelewa kujaza Mfuko wa Welfare (Tukio: {evt.Title})",
                        groupEventId: evt.Id);
                }
            }

            evt.IsResolved = true;
            // BUG FIX: IsActive was never turned off once an event was
            // resolved, so MkobaImportController could keep treating an
            // expired event as the "active" one for new imports.
            evt.IsActive = false;
        }

        await context.SaveChangesAsync();
    }
}