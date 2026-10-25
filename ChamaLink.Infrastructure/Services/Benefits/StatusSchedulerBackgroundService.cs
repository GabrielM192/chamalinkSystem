using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services.Benefits;

/// <summary>
/// SASISHO17 (K, ukaguzi S16 P1): mabadiliko ya hali yaliyoidhinishwa kwa tarehe ya BAADAYE yanatekelezwa siku yake.
/// Kabla ya hapa hali ilibadilishwa mara moja hata kama tarehe ilikuwa ya baadaye (mwanachama alipoteza haki mapema),
/// au haikutekelezwa kabisa. Inaendeshwa mara moja wakati API inaanza, kisha kila Jobs:StatusSchedulerMinutes (chaguo-msingi 60).
/// </summary>
public sealed class StatusSchedulerBackgroundService : BackgroundService
{
    private const int DefaultMinutes = 60;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<StatusSchedulerBackgroundService> _log;
    private readonly TimeSpan _interval;

    public StatusSchedulerBackgroundService(IServiceScopeFactory scopes, ILogger<StatusSchedulerBackgroundService> log, IConfiguration config)
    {
        _scopes = scopes;
        _log = log;
        var m = int.TryParse(config["Jobs:StatusSchedulerMinutes"], out var v) && v > 0 ? v : DefaultMinutes;
        _interval = TimeSpan.FromMinutes(m);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<MemberStatusService>();
                var n = await svc.ApplyDueAsync(stoppingToken);
                if (n > 0) _log.LogInformation("Mabadiliko ya hali {Count} yaliyopangwa yametekelezwa.", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Kutekeleza mabadiliko ya hali yaliyopangwa kumeshindikana; itajaribu tena."); }
            try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
