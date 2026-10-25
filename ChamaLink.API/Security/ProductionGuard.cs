namespace ChamaLink.API.Security;

/// <summary>
/// SASISHO17 (P1-5, ukaguzi S16): API INAKATAA KUANZA nje ya Development/Testing ikiwa usanidi wowote hatari umewashwa.
/// Kwa nini: swichi hizi zinafaa kwa uhamisho/majaribio tu; zikisahaulika production zinafungua njia za kufuta data,
/// kupita Injini ya Ukweli, au kuvuja maelezo ya ndani. Hakuna swichi ya "kupuuza" — kurekebisha config ndiyo njia pekee.
/// </summary>
public static class ProductionGuard
{
    public const string LeakedSandboxJwtKey = "ChamaLinkSandboxSecretKey2024ForTestingOnlyMinimum32Bytes!!";
    private const int MinJwtKeyLength = 43;      // ≈ bit 256 kwa base64
    private const int MinJwtDistinctChars = 16;  // kinga dhidi ya "aaaa…"/maneno ya kawaida

    /// <summary>Matatizo yote (tupu = salama). Haitoi thamani za siri kwenye ujumbe.</summary>
    public static List<string> Problems(IConfiguration c)
    {
        var p = new List<string>();
        static bool On(IConfiguration c, string k) => bool.TryParse(c[k], out var b) && b;

        if (On(c, "Truth:AllowReset")) p.Add("Truth:AllowReset=true (kufuta data ya fedha) haliruhusiwi production.");
        if (On(c, "Legacy:Endpoints")) p.Add("Legacy:Endpoints=true (njia za zamani zinazopita Injini ya Ukweli) haliruhusiwi production.");
        if (On(c, "Legacy:BackgroundJobs")) p.Add("Legacy:BackgroundJobs=true haliruhusiwi production.");
        if (On(c, "Errors:IncludeExceptionDetails")) p.Add("Errors:IncludeExceptionDetails=true (inavuja maelezo ya ndani) haliruhusiwi production.");

        var jwt = c["Jwt:Key"] ?? string.Empty;
        if (jwt.Length < MinJwtKeyLength || jwt.Distinct().Count() < MinJwtDistinctChars || jwt.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) || jwt == LeakedSandboxJwtKey)
            p.Add($"Jwt:Key si imara (inahitaji angalau herufi {MinJwtKeyLength} za nasibu, mf. openssl rand -base64 64).");

        var ek = c["Benefits:EvidenceEncryptionKey"];
        var evidenceOk = false;
        if (!string.IsNullOrWhiteSpace(ek))
        {
            try { evidenceOk = Convert.FromBase64String(ek).Length == 32; } catch (FormatException) { }
        }
        if (!evidenceOk) p.Add("Benefits:EvidenceEncryptionKey haipo au si base64 ya byte 32 (vyeti vingehifadhiwa bila usimbaji).");

        if (string.IsNullOrWhiteSpace(c["Benefits:EvidencePath"])) p.Add("Benefits:EvidencePath haijawekwa (vyeti na PDF za M-Koba zinahifadhiwa hapo).");
        if (!long.TryParse(c["Benefits:MaxEvidenceBytes"], out var meb) || meb <= 0) p.Add("Benefits:MaxEvidenceBytes haijawekwa.");

        var origins = c.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        foreach (var o in origins)
        {
            if (!Uri.TryCreate(o, UriKind.Absolute, out var u)) { p.Add($"Cors:AllowedOrigins ina thamani isiyo URL: '{o}'."); continue; }
            if (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) p.Add($"Cors:AllowedOrigins ina localhost ('{o}') — production lazima iwe domain halisi.");
            else if (u.Scheme != Uri.UriSchemeHttps) p.Add($"Cors:AllowedOrigins '{o}' si https.");
        }
        return p;
    }

    public static void EnforceOrThrow(IConfiguration c, IHostEnvironment env, ILogger log)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing")) return;
        var problems = Problems(c);
        if (problems.Count == 0) return;
        foreach (var x in problems) log.LogCritical("ProductionGuard: {Problem}", x);
        throw new InvalidOperationException("ProductionGuard: API haiwezi kuanza — usanidi hatari kwa production:\n - " + string.Join("\n - ", problems));
    }
}
