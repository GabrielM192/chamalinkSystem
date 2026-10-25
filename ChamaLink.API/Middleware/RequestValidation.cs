namespace ChamaLink.API.Middleware;

/// <summary>SASISHO16: jina la sehemu ya ombi bila maelezo ya ndani ($.x, [0], "req.").</summary>
public static class RequestValidation
{
    private static readonly HashSet<string> BodyParamNames = new(StringComparer.OrdinalIgnoreCase) { "req", "dto", "request", "body", "model", "input", "payload", "data" };

    public static string FieldName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var k = key.Trim();
        if (k.StartsWith("$", StringComparison.Ordinal)) k = k.TrimStart('$').TrimStart('.');
        var dot = k.LastIndexOf('.');
        if (dot >= 0) k = k[(dot + 1)..];
        var br = k.IndexOf('[');
        if (br >= 0) k = k[..br];
        k = new string(k.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (k.Length == 0) return string.Empty;
        // jina la kigezo cha action (mwili mzima wa ombi) si sehemu ambayo mtumiaji anaijua
        if (BodyParamNames.Contains(k)) return string.Empty;
        if (k.Length > 40) k = k[..40];
        return char.ToLowerInvariant(k[0]) + k[1..];
    }

    /// <summary>
    /// Ujumbe wa DataAnnotations ULIOANDIKWA NA SISI (mf. "Barua pepe si sahihi.") unaruhusiwa kurudi kwa mtumiaji;
    /// maandishi ya framework (Kiingereza, JSON parser: "The JSON value could not be converted to System.Decimal. Path: $.amount | LineNumber...",
    /// "The value 'abc' is not valid") HAYARUDI — yanaonyesha aina za ndani na kurudisha ingizo la mshambulizi.
    /// </summary>
    public static string? OwnMessage(Microsoft.AspNetCore.Mvc.ModelBinding.ModelError e)
    {
        if (e.Exception is not null) return null;
        var m = e.ErrorMessage?.Trim();
        if (string.IsNullOrEmpty(m) || m.Length > 200) return null;
        if (m.StartsWith("The ", StringComparison.Ordinal) || m.StartsWith("A ", StringComparison.Ordinal)
            || m.StartsWith("'", StringComparison.Ordinal) || m.StartsWith("$", StringComparison.Ordinal)) return null;
        foreach (var bad in new[] { "JSON", "Path:", "LineNumber", "System.", "BytePosition", "field is required", "is not valid", "is invalid", "could not" })
            if (m.Contains(bad, StringComparison.OrdinalIgnoreCase)) return null;
        return m;
    }
}
