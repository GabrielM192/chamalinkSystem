using ChamaLink.Infrastructure.Services.Sms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services.Sms;

/// <summary>
/// SASISHO21 (Sehemu ya 2) — MWENYEKITI WA MNYORORO WA SMS.
///
/// Kila sekunde 30:
///   1. Hukumbusha wajibu unaokaribia tarehe ya mwisho (siku 2 kabla) — mara moja kwa wajibu.
///   2. Hutuma ujumbe uliopangwa (ukizingatia saa za utulivu na salio la credits).
///
/// Hakuna SMS inayotumwa nje ya saa za utulivu: ujumbe unasubiri hadi saa ya kuanza.
/// </summary>
public class SmsDispatchBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SmsDispatchBackgroundService> _log;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public SmsDispatchBackgroundService(IServiceScopeFactory scopes, ILogger<SmsDispatchBackgroundService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subiri mfumo uanze kwanza (migration, cache).
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var sms = scope.ServiceProvider.GetRequiredService<SmsService>();
                await sms.QueueDueRemindersAsync(DateTime.UtcNow, stoppingToken);
                var r = await sms.DispatchAsync(50, stoppingToken);
                if (r.Attempted > 0)
                    _log.LogInformation("SMS: {Sent} zimetumwa, {Failed} zimeshindikana, {Skipped} zimezuiwa",
                        r.Sent, r.Failed, r.Skipped);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Mnyororo hausimami kwa kosa la ujumbe mmoja.
                _log.LogWarning(ex, "SMS dispatch imeshindikana; itajaribiwa tena");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
