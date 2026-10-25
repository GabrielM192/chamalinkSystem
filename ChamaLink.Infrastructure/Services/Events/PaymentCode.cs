using System.Text;

namespace ChamaLink.Infrastructure.Services.Events;

/// <summary>
/// SASISHO21 — KODI YA MALIPO (kumbukumbu ya mwanachama kwa tukio).
///
/// Shida inayotatuliwa: kwenye kikundi cha wanachama 1,200, kila mtu anatuma
/// pesa kwenye namba moja. Bila kumbukumbu, hazina anasoma SMS moja moja na
/// kukisia ni nani. Kodi hii inafanya malipo kujitambulisha:
///
///     J3 - E07 - 0147
///     │    │      └── namba ya mwanachama (tarakimu 4 za mwisho)
///     │    └───────── tukio la 7 (kutoka CaseNumber: J3-MF-2026-007)
///     └────────────── code ya kikundi (Group.ShortCode)
///
/// Kanuni:
///   • Kodi HAITEGEMEI namba ya simu — simu inaweza kubadilika, kuandikwa
///     vibaya, au kutumwa na mtu mwingine. Kodi inatambulisha MTU na TUKIO.
///   • Kodi ni ya kudumu: ikishawekwa kwenye wajibu, haibadiliki.
///   • Kodi si sharti la malipo. Mfumo unatambua pia kwa namba ya simu +
///     kiasi + statement. Kodi ni njia ya uhakika zaidi, si ya pekee.
/// </summary>
public static class PaymentCode
{
    /// <summary>Upeo wa safu ya kodi kwenye database.</summary>
    public const int MaxLength = 20;

    /// <summary>Code ya kikundi: herufi/namba tu, herufi kubwa, upeo 6. Bila ShortCode → "KL".</summary>
    public static string GroupPart(string? shortCode)
    {
        var sb = new StringBuilder();
        foreach (var ch in (shortCode ?? string.Empty).Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch) && sb.Length < 6) sb.Append(ch);
        }
        return sb.Length == 0 ? "KL" : sb.ToString();
    }

    /// <summary>
    /// Sehemu ya tukio. CaseNumber ya ChamaLink ni "{SHORT}-MF-{YEAR}-{NNN}",
    /// hivyo "J3-MF-2026-007" → "E07". Bila namba → "E00".
    /// </summary>
    public static string EventPart(string? caseNumber)
    {
        if (string.IsNullOrWhiteSpace(caseNumber)) return "E00";

        var parts = caseNumber.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        var last = parts.Length > 0 ? parts[^1] : string.Empty;

        var digits = new StringBuilder();
        foreach (var ch in last)
            if (char.IsDigit(ch)) digits.Append(ch);

        if (digits.Length == 0) return "E00";

        var n = int.TryParse(digits.ToString(), out var v) ? v : 0;
        return $"E{n:00}";
    }

    /// <summary>
    /// Sehemu ya mwanachama: tarakimu 4 za mwisho za MemberNumber
    /// ("J3-2026-0147" → "0147"). Bila namba → "0000".
    /// </summary>
    public static string MemberPart(string? memberNumber)
    {
        if (string.IsNullOrWhiteSpace(memberNumber)) return "0000";

        var digits = new StringBuilder();
        foreach (var ch in memberNumber)
            if (char.IsDigit(ch)) digits.Append(ch);

        if (digits.Length == 0) return "0000";
        return digits.Length <= 4
            ? digits.ToString().PadLeft(4, '0')
            : digits.ToString()[^4..];
    }

    /// <summary>Kodi kamili, mf. "J3-E07-0147".</summary>
    public static string Build(string? shortCode, string? caseNumber, string? memberNumber)
        => $"{GroupPart(shortCode)}-{EventPart(caseNumber)}-{MemberPart(memberNumber)}";

    /// <summary>Je, kodi ina umbo linalokubalika? (kwa ulinganisho wa statement)</summary>
    public static bool LooksValid(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var c = code.Trim();
        if (c.Length is < 7 or > MaxLength) return false;
        var dashes = 0;
        foreach (var ch in c)
        {
            if (ch == '-') { dashes++; continue; }
            if (!char.IsLetterOrDigit(ch)) return false;
        }
        return dashes == 2;
    }

    /// <summary>
    /// Hutafuta kodi ndani ya maelezo ya muamala. Inavumilia uandishi wowote:
    ///
    ///     "J3-E07-0147"                 → J3-E07-0147
    ///     "nimelipa j3 e07 0147 asante" → J3-E07-0147
    ///     "NIMELIPA J3E070147"          → J3-E07-0147
    ///     "kodi:J3/E07/0147"            → J3-E07-0147
    ///
    /// Mbinu: maelezo hugawanywa kwa herufi zisizo za alnum, kisha hujaribiwa
    /// kipande KIMOJA na mchanganyiko wa vipande 2–4 mfululizo. Kila mchanganyiko
    /// hukaguliwa kwa umbo kamili: &lt;group 1–6&gt; E dd dddd (mwisho wa kipande).
    /// Hivyo "MALIPOJ3E070147ASANTE" HAIKUBALIKI (maneno yameunganishwa).
    /// </summary>
    public static string? Find(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var tokens = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in description.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { cur.Append(ch); continue; }
            if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) tokens.Add(cur.ToString());
        if (tokens.Count == 0) return null;

        // 1) Kipande kimoja (mf. "J3-E07-0147" → "J3E070147")
        foreach (var t in tokens)
            if (TryParseCompact(t, out var code)) return code;

        // 2) Vipande 2–4 mfululizo (mf. "J3" "E07" "0147")
        for (var span = 2; span <= 4; span++)
            for (var i = 0; i + span <= tokens.Count; i++)
            {
                var joined = new StringBuilder();
                for (var k = 0; k < span; k++) joined.Append(tokens[i + k]);
                if (TryParseCompact(joined.ToString(), out var code2)) return code2;
            }

        return null;
    }

    // ==================================================================
    // SASISHO21 (Sehemu ya 2) — KODI YA KUJIUNGA
    // ==================================================================
    //
    // Mtu anayeomba kujiunga HALIYE mwanachama bado, kwa hiyo hana wajibu
    // wa tukio. Kodi yake ni ya aina yake:
    //
    //     J3-JOIN-0007
    //     │   │      └── namba ya ombi kwenye kikundi (0001..9999)
    //     │   └───────── neno JOIN (halichanganyiki na E=event)
    //     └───────────── code ya kikundi
    //
    public const string JoinMarker = "JOIN";

    /// <summary>Kodi ya ombi la kujiunga, mf. "J3-JOIN-0007".</summary>
    public static string BuildJoin(string? shortCode, int sequence)
        => $"{GroupPart(shortCode)}-{JoinMarker}-{Math.Max(1, Math.Min(9999, sequence)):0000}";

    /// <summary>
    /// Hutafuta kodi ya kujiunga kwenye maelezo ("J3-JOIN-0007", "j3 join 7",
    /// "NIMELIPA J3JOIN0007"). Inarudisha kodi ya kawaida au null.
    /// </summary>
    public static string? FindJoin(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var tokens = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in description.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { cur.Append(ch); continue; }
            if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) tokens.Add(cur.ToString());

        // Vipande 3: GROUP · JOIN · NNNN
        for (var i = 0; i + 3 <= tokens.Count; i++)
        {
            if (tokens[i].Length is < 1 or > 6 || !tokens[i].All(char.IsLetterOrDigit)) continue;
            if (!tokens[i + 1].StartsWith(JoinMarker, StringComparison.Ordinal)) continue;
            var tail = tokens[i + 1][JoinMarker.Length..] + tokens[i + 2];
            if (tail.Length is < 1 or > 4 || !tail.All(char.IsDigit)) continue;
            var seq = int.Parse(tail);
            if (seq <= 0) continue;
            return $"{tokens[i]}-{JoinMarker}-{seq:0000}";
        }

        // Kipande kimoja kilichounganishwa: "J3JOIN0007"
        foreach (var t in tokens)
        {
            var at = t.IndexOf(JoinMarker, StringComparison.Ordinal);
            if (at <= 0) continue;
            var group = t[..at];
            var tail = t[(at + JoinMarker.Length)..];
            if (group.Length > 6 || tail.Length is < 1 or > 4 || !tail.All(char.IsDigit)) continue;
            var seq = int.Parse(tail);
            if (seq <= 0) continue;
            return $"{group}-{JoinMarker}-{seq:0000}";
        }

        return null;
    }

    /// <summary>Kipande kilichoondolewa vistari: &lt;group 1–6&gt;E&lt;dd&gt;&lt;dddd&gt;.</summary>
    private static bool TryParseCompact(string s, out string code)
    {
        code = string.Empty;
        if (s.Length < 8) return false;                 // 1 + 1 + 2 + 4
        var tailStart = s.Length - 7;                   // "E" + 6 tarakimu
        if (s[tailStart] != 'E') return false;
        for (var i = tailStart + 1; i < s.Length; i++)
            if (!char.IsDigit(s[i])) return false;

        var group = s[..tailStart];
        if (group.Length is < 1 or > 6) return false;
        foreach (var ch in group)
            if (!char.IsLetterOrDigit(ch)) return false;

        var candidate = $"{group}-E{s[tailStart + 1]}{s[tailStart + 2]}-{s[(tailStart + 3)..]}";
        if (!LooksValid(candidate)) return false;
        code = candidate;
        return true;
    }
}
