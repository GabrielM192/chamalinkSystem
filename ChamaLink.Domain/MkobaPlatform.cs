namespace ChamaLink.Domain;

/// <summary>
/// Ukweli wa CHANZO CHA DATA, si sheria ya kikundi: M-Koba ni huduma ya Vodacom Tanzania, kwa hiyo
/// taarifa zake zote ziko kwa saa za Afrika Mashariki (EAT, UTC+3) na namba za simu ni za Tanzania (+255).
/// Sheria za fedha za kikundi (mchango, faini, Kianzio, tarehe ya mwisho) HAZIKO hapa — ziko kwenye
/// TruthGroupSettings ya kila kikundi. Mfumo ukipanuka nje ya Tanzania, hapa ndipo pa kubadilisha.
/// </summary>
public static class MkobaPlatform
{
    public const string CountryDialCode = "255";
    /// <summary>Tarakimu za namba bila msimbo wa nchi wala 0 ya mwanzo (mf. 712805984).</summary>
    public const int NationalNumberLength = 9;
    /// <summary>Tarakimu ya kwanza ya namba za simu za mkononi Tanzania.</summary>
    public static readonly IReadOnlyList<char> MobilePrefixes = new[] { '6', '7' };

    /// <summary>Majina ya eneo la saa: IANA (Linux) kwanza, kisha Windows.</summary>
    public static readonly IReadOnlyList<string> TimeZoneIds = new[] { "Africa/Dar_es_Salaam", "E. Africa Standard Time" };
    public static readonly TimeSpan UtcOffset = TimeSpan.FromHours(3);

    public static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in TimeZoneIds)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { /* jaribu jina linalofuata */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("EAT", UtcOffset, "EAT", "EAT");
    }

    /// <summary>"+255 652 952 220", "0652952220", "652952220" -> "255652952220". Namba batili -> ArgumentException.</summary>
    public static string NormalizePhone(string? raw)
    {
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (d.StartsWith(CountryDialCode) && d.Length == CountryDialCode.Length + NationalNumberLength) return d;
        if (d.StartsWith('0') && d.Length == NationalNumberLength + 1) return CountryDialCode + d[1..];
        if (d.Length == NationalNumberLength && MobilePrefixes.Contains(d[0])) return CountryDialCode + d;
        throw new ArgumentException($"Namba ya simu '{raw}' si sahihi. Tumia mfumo 0XXXXXXXXX au +{CountryDialCode}XXXXXXXXX.");
    }
}
