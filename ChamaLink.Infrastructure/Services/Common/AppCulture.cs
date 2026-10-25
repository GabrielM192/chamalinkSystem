using System.Globalization;

namespace ChamaLink.Infrastructure.Services.Common;

/// <summary>
/// SASISHO17: utamaduni (culture) wa mchakato mzima unawekwa kuwa InvariantCulture.
/// Sababu: maandishi ya seva (ujumbe, ripoti, CSV, WhatsApp) yasibadilike kulingana na locale ya mashine
/// inayoendesha API. Mf. kwenye mashine yenye locale ya fr_FR/de_DE, "{x:N0}" ingetoa "45 000" au "45.000"
/// badala ya "45,000" — ripoti zingetofautiana kati ya server, na tafsiri ya EN ya namba (tArg) ingeshindwa.
/// Lugha ya mtumiaji (SW/EN) inashughulikiwa na frontend, si na culture ya server.
/// Inaitwa mwanzoni mwa Program.cs na kwenye ModuleInitializer ya majaribio (mazingira yale yale).
/// </summary>
public static class AppCulture
{
    public static void UseInvariant()
    {
        var c = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = c;
        CultureInfo.DefaultThreadCurrentUICulture = c;
        CultureInfo.CurrentCulture = c;
        CultureInfo.CurrentUICulture = c;
    }
}
