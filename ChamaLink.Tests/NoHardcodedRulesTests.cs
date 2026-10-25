using System.Text.RegularExpressions;

namespace ChamaLink.Tests;

/// <summary>
/// Hakuna sheria ya fedha iliyoandikwa ndani ya code ya production: kiasi cha mchango, faini, Kianzio n.k.
/// lazima kitoke kwenye mipangilio ya kikundi. Test inasoma source code halisi (backend + frontend);
/// ikikuta kiasi cha pesa (>= 1,000) nje ya comments, inafeli na kuonyesha faili na mstari.
/// Majina ya wanachama (Frank, Tunganege...) nayo hayaruhusiwi kwenye mantiki ya production.
/// </summary>
public class NoHardcodedRulesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChamaLink.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "ChamaLink.sln haikupatikana juu ya " + AppContext.BaseDirectory + " — test hii inahitaji source code.");
    }

    private static readonly string[] BackendProjects = { "ChamaLink.API", "ChamaLink.Application", "ChamaLink.Domain", "ChamaLink.Infrastructure" };
    private static readonly string[] ExcludedDirs = { "bin", "obj", "Migrations", "node_modules", "dist" };

    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(ExcludedDirs.Contains));

    /// <summary>Ondoa comments (// na /* */) na maandishi ndani ya string ili kubaki na code tu.</summary>
    private static string CodeOnly(string src)
    {
        src = Regex.Replace(src, @"/\*.*?\*/", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
        var lines = src.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var l = Regex.Replace(lines[i], "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`[^`]*`", "\"\"");
            var c = l.IndexOf("//", StringComparison.Ordinal);
            lines[i] = c >= 0 ? l[..c] : l;
        }
        return string.Join('\n', lines);
    }

    // 1,000 au zaidi: 10000m, 10_000m, 5000.0m (C#) | 10000, 10_000 (TS — thamani za pesa za UI)
    private static readonly Regex CsMoney = new(@"(?<![\w.])\d{1,3}(?:_?\d{3})+(?:\.\d+)?[mM]\b");
    private static readonly Regex TsMoney = new(@"(?<![\w.#-])\d{1,3}(?:_\d{3})+\b|(?<![\w.#-])[1-9]\d{3,5}(?:\.\d+)?(?![\w%])");

    private static List<string> Scan(IEnumerable<string> files, Regex rx, string root, Func<string, bool>? ignoreLine = null)
    {
        var hits = new List<string>();
        foreach (var f in files)
        {
            var lines = CodeOnly(File.ReadAllText(f)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (ignoreLine?.Invoke(lines[i]) == true) continue;
                foreach (Match m in rx.Matches(lines[i]))
                    hits.Add($"{Path.GetRelativePath(root, f)}:{i + 1}  {m.Value}  ←  {lines[i].Trim()}");
            }
        }
        return hits;
    }

    [Fact]
    public void Backend_Has_No_Hardcoded_Money_Amounts()
    {
        var root = RepoRoot();
        var files = BackendProjects.SelectMany(p => SourceFiles(Path.Combine(root, p), "*.cs"));
        var hits = Scan(files, CsMoney, root);
        Assert.True(hits.Count == 0, "Kiasi cha pesa kimeandikwa ndani ya code (kiweke kwenye mipangilio ya kikundi):\n" + string.Join("\n", hits));
    }

    // Namba kamili >= 1,000 BILA "m" (mf. `decimal x = 50000;` au `AvailableSavings = 1000000`) — C# inaigeuza kimya kuwa decimal.
    private static readonly Regex CsBareLargeNumber = new(@"(?<![\w.])\d{1,3}(?:_?\d{3})+(?![\w.])");

    // Matumizi yanayojulikana ambayo SI pesa wala sheria za kikundi. Kuongeza hapa kunahitaji sababu wazi.
    private static readonly string[] NonMoneyContexts =
    {
        "1024",               // ukubwa wa faili (bytes)
        "RequestSizeLimit",   // kikomo cha ukubwa wa upload
        "StringLength",       // urefu wa maandishi
        "HasMaxLength",       // urefu wa safu ya database (EF)
        "year <",             // ukaguzi wa mwaka unaoeleweka (mf. year < 2000)
        "CompliancePercent",  // kikomo cha usalama cha asilimia
        "new Random()",       // kodi ya kikundi ya nasibu
        "OtpModulus",         // SASISHO20: TOTP = tarakimu 6 (RFC 6238), si pesa
        "DetailJsonMaxChars", // SASISHO20: kikomo cha urefu wa JSON ya kumbukumbu
        "MaxAuditRows",       // SASISHO20: kikomo cha mistari ya kumbukumbu inayorudishwa
        "MaxTopUpCredits",    // SASISHO21: idadi ya juu ya SMS kwa ongezeko moja (si pesa; salio la ujumbe)
        "MaxLength(1500)",    // SASISHO21: urefu wa maelezo ya rufaa (herufi, si pesa)
        "MaxLength(1000)",    // SASISHO21: urefu wa uamuzi wa rufaa (herufi, si pesa)
        "Trim(note, 1000)",   // SASISHO21: kukata maelezo kwa urefu (herufi, si pesa)
    };

    [Fact]
    public void Backend_Has_No_Hardcoded_Large_Numbers_Without_Decimal_Suffix()
    {
        var root = RepoRoot();
        var files = BackendProjects.SelectMany(p => SourceFiles(Path.Combine(root, p), "*.cs"));
        var hits = Scan(files, CsBareLargeNumber, root, line => NonMoneyContexts.Any(line.Contains));
        Assert.True(hits.Count == 0, "Namba kubwa imeandikwa ndani ya code (kama ni pesa/sheria, isome kutoka mipangilio ya kikundi):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Backend_Has_No_Hardcoded_Calendar_Dates()
    {
        // Tarehe kama `new DateTime(2026, 9, 1)` inafanya ripoti "isimame" siku fulani — lazima itoke kwa mtumiaji au saa ya mfumo.
        var root = RepoRoot();
        var files = BackendProjects.SelectMany(p => SourceFiles(Path.Combine(root, p), "*.cs"));
        var hits = Scan(files, new Regex(@"new\s+DateTime(?:Offset)?\s*\(\s*\d{4}\s*,"), root);
        Assert.True(hits.Count == 0, "Tarehe imeandikwa ndani ya code:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Frontend_Has_No_Hardcoded_Money_Amounts()
    {
        var root = RepoRoot();
        var src = Path.Combine(root, "chamalink-web-new", "src");
        Assert.True(Directory.Exists(src), "Frontend haikupatikana: " + src);
        var files = SourceFiles(src, "*.ts").Concat(SourceFiles(src, "*.tsx"));
        // Madarasa ya Tailwind/CSS (mf. "w-[1200px]", "z-1000") na muda wa ms (setTimeout(…, 4000)) si pesa.
        var hits = Scan(files, TsMoney, root, line =>
            line.Contains("setTimeout") || line.Contains("setInterval") || line.Contains("duration") || line.Contains("staleTime")
            || line.Contains("Ms ") || line.Contains("Ms=") || line.Contains("_MS") || line.Contains("timeout"));
        Assert.True(hits.Count == 0, "Kiasi cha pesa kimeandikwa ndani ya frontend (kisome kutoka mipangilio ya kikundi):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Production_Code_Has_No_Member_Name_Branching()
    {
        var root = RepoRoot();
        var files = BackendProjects.SelectMany(p => SourceFiles(Path.Combine(root, p), "*.cs"));
        var names = new Regex(@"\b(Frank|Tunganege|Japhet|Tito|Abdulla|Fauzia|Ukonga)\b", RegexOptions.IgnoreCase);
        var hits = new List<string>();
        foreach (var f in files)
        {
            // Majina ndani ya string (ujumbe) yameondolewa na CodeOnly; yakibaki ni identifiers/mantiki.
            var lines = CodeOnly(File.ReadAllText(f)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (names.IsMatch(lines[i])) hits.Add($"{Path.GetRelativePath(root, f)}:{i + 1}  {lines[i].Trim()}");
        }
        Assert.True(hits.Count == 0, "Mantiki inayotegemea majina ya wanachama/kikundi:\n" + string.Join("\n", hits));
    }
}
