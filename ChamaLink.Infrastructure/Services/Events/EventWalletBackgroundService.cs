using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services.Events;

/// <summary>
/// SASISHO18 — huduma ya kila siku ya vikundi vya "KILA TUKIO".
///
///   1. FAINI: wajibu uliopita tarehe ya mwisho (due date) na bado una deni
///      unapata faini ya kikundi (OverdueEventFineAmount), mara MOJA.
///      Kikundi kisicho weka faini (0) hakiguswi.
///   2. SWEEP: kila deni lililobaki linajaribiwa kulipwa kwa amana zilizopo
///      juu ya floor. Hii ni huduma ya usalama tu — amana mpya inalipa deni
///      mara moja inapoidhinishwa (StatementImportService → SweepDepositsAsync);
///      hapa tunakagua pia kwa sababu salio linaweza kubadilika kwa njia nyingine
///      (marekebisho ya kiongozi, mikopo, n.k.).
///
/// Kila kikundi kinasoma sera yake mwenyewe; hakuna namba iliyoandikwa ndani
/// ya msimbo (NoHardcodedRulesTests).
/// </summary>
public class EventWalletBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EventWalletBackgroundService> _logger;

    public EventWalletBackgroundService(IServiceProvider services, ILogger<EventWalletBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Huduma ya pochi za matukio imeanza...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kosa limetokea wakati wa kukagua pochi za matukio.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Hukimbia mara moja (inaitwa na jaribio pia).</summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var wallet = scope.ServiceProvider.GetRequiredService<EventWalletService>();
        var atRisk = scope.ServiceProvider.GetRequiredService<AtRiskService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hasEventGroups = await db.Groups.AsNoTracking()
            .AnyAsync(g => g.Type == GroupType.EventBased, ct);
        if (!hasEventGroups) return;

        var fined = await wallet.ChargeOverdueFinesAsync(DateTime.UtcNow, ct);
        if (fined > 0)
            _logger.LogInformation("Faini za deni la tukio zimetozwa: {Count}", fined);

        var swept = await wallet.SweepAllAsync(ct);
        if (swept > 0)
            _logger.LogInformation("Madeni ya matukio yamelipwa kwa amana zilizopo: {Count}", swept);

        // SASISHO19: kila siku, wajulishe walio hatarini kuondolewa (deni linabaki baada ya faini + neema).
        var atRiskCount = await atRisk.NotifyAllAtRiskAsync(ct);
        if (atRiskCount > 0)
            _logger.LogInformation("Wanachama hatarini kuondolewa wamearifiwa: {Count}", atRiskCount);
    }
}
