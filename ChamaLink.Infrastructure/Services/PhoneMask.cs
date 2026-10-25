namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// SASISHO16 (faragha/PII): namba ya simu iliyofichwa kwa wasio na haki ya kuiona kamili.
/// Tarakimu 5 za mwanzo (nchi + mtandao) na 3 za mwisho zinabaki ili mwanachama atambue "ni namba ya nani" bila kuweza kuipiga.
/// </summary>
public static class PhoneMask
{
    public static string Digits(string? phone) => new((phone ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string Mask(string? phone)
    {
        var d = Digits(phone);
        if (d.Length < 9) return new string('•', Math.Max(d.Length, 4));
        return d[..5] + new string('•', d.Length - 8) + d[^3..];
    }
}
