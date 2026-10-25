using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChamaLink.API.Filters;

/// <summary>
/// SASISHO17 (P1-8): kikomo cha ukubwa wa ombi (body) kinasomwa kutoka CONFIG — si namba iliyoandikwa ndani ya code.
/// Kikomo = thamani ya ufunguo (mf. Benefits:MaxEvidenceBytes) + nafasi ndogo ya bahasha ya multipart. Kinawekwa KABLA
/// ya model binding (resource filter), kwa hiyo Kestrel inakata ombi kubwa mapema bila kulisoma lote kwenye kumbukumbu.
/// Huduma yenyewe bado inakagua ukubwa halisi wa faili (ulinzi wa tabaka mbili).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ConfiguredRequestSizeLimitAttribute : Attribute, IResourceFilter, IOrderedFilter
{
    private const long EnvelopeBytes = 1024 * 1024;         // vichwa vya multipart + sehemu za fomu
    private const long FallbackBytes = 5 * 1024 * 1024;      // config ikikosekana (ProductionGuard inazuia hilo production)

    public ConfiguredRequestSizeLimitAttribute(string configKey) => ConfigKey = configKey;

    public string ConfigKey { get; }
    public int Order => 900; // sawa na RequestSizeLimitAttribute ya ASP.NET

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var cfg = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var max = long.TryParse(cfg[ConfigKey], out var v) && v > 0 ? v : FallbackBytes;
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = max + EnvelopeBytes;
        var form = context.HttpContext.Features.Get<IFormFeature>();
        if (form is null || !context.HttpContext.Request.HasFormContentType) return;
        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request,
            new Microsoft.AspNetCore.Http.Features.FormOptions { MultipartBodyLengthLimit = max + EnvelopeBytes }));
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
