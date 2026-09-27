using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChamaLink.API.Filters;

/// <summary>
/// Inazuia (kwa CODE, si kwa maandishi) endpoints za vizazi vya zamani vya "injini"
/// (V1 LedgerReclassification/ObligationEngine, V2 AllocationEngine/LedgerEntries).
///
/// Kwa nini: Injini ya Ukweli inasoma data GHAFI pekee (MkobaRawTransactions, manual
/// FinancialEvents zilizoidhinishwa, Loans, TruthAdjustments). Pesa ikiandikwa kupitia
/// njia ya zamani (LedgerEntries) Injini ya Ukweli HAIIONI — mwanachama angekuwa na
/// taarifa mbili tofauti. Ripoti za zamani (AnalyticsService n.k.) zinasoma LedgerEntries
/// hizo hizo, kwa hiyo nazo zimezuiwa ili kusiwe na "namba za pili".
///
/// Zinarudisha 410 Gone + njia mpya ya kutumia. Kuziwasha tena (kwa makusudi, mf. kuhamisha
/// data ya zamani): appsettings -> "Legacy": { "Endpoints": true }.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class LegacyEndpointAttribute : Attribute, IAsyncResourceFilter
{
    public const string ConfigKey = "Legacy:Endpoints";

    /// <summary>Njia mpya ya Injini ya Ukweli inayochukua nafasi ya endpoint hii.</summary>
    public string UseInstead { get; }

    public LegacyEndpointAttribute(string useInstead) => UseInstead = useInstead;

    // Resource filter: inaendeshwa KABLA ya model binding/validation — ombi linazuiwa kabla body haijasomwa
    // (vinginevyo [ApiController] ingejibu 400/415 kwanza na kuficha kwamba njia imefungwa).
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (config.GetValue<bool>(ConfigKey))
        {
            context.HttpContext.Response.Headers["Deprecation"] = "true";
            await next();
            return;
        }

        context.Result = new ObjectResult(new
        {
            message = "Njia hii ni ya mfumo wa zamani na imefungwa: pesa au ripoti zake haziendani na Injini ya Ukweli.",
            useInstead = UseInstead,
            howToEnable = $"Kwa uhamisho wa data tu: weka \"{ConfigKey}\" = true kwenye appsettings."
        })
        { StatusCode = StatusCodes.Status410Gone };
    }
}
