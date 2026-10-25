using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ChamaLink.Domain.Localization;

/// <summary>
/// Ujumbe unaotafsirika (SASISHO14 pengo 12). Kila <c>$"..."</c> unaopitishwa kwenye kigezo cha aina <see cref="Loc"/>
/// unakamatwa na compiler kama kiolezo: <c>Template</c> = maandishi ya Kiswahili yenye <c>{0}</c>, <c>{1}</c>…;
/// <c>Args</c> = thamani (tayari zimepangwa kwa InvariantCulture, mf. <c>N0</c> → "640,000"); <c>Text</c> = Kiswahili kamili.
/// Frontend inatafsiri Template (ufunguo wa en.ts) kisha inaweka Args — hivyo EN haionyeshi Kiswahili.
/// Mfuatano wa kawaida (string) unabadilika kuwa Loc yenye Template = maandishi yenyewe.
/// </summary>
[InterpolatedStringHandler]
public struct Loc
{
    private StringBuilder? _tpl, _text;
    private List<string>? _args;
    private string? _fixedTemplate, _fixedText;
    private string[]? _fixedArgs;

    public Loc(int literalLength, int formattedCount)
    {
        _tpl = new StringBuilder(literalLength + formattedCount * 3);
        _text = new StringBuilder(literalLength + formattedCount * 8);
        _args = new List<string>(formattedCount);
        _fixedTemplate = null; _fixedText = null; _fixedArgs = null;
    }

    private Loc(string template, string[] args, string text)
    {
        _tpl = null; _text = null; _args = null;
        _fixedTemplate = template; _fixedArgs = args; _fixedText = text;
    }

    public static Loc Of(string template, IReadOnlyList<string> args, string text) => new(template, args.ToArray(), text);

    public void AppendLiteral(string s)
    {
        _tpl!.Append(s);
        _text!.Append(s);
    }

    public void AppendFormatted<T>(T value) => AppendFormatted(value, null);

    public void AppendFormatted<T>(T value, string? format)
    {
        string s;
        if (value is Loc inner) s = inner.Text;
        else if (value is IFormattable f) s = f.ToString(format, CultureInfo.InvariantCulture);
        else s = value?.ToString() ?? string.Empty;
        _tpl!.Append('{').Append(_args!.Count.ToString(CultureInfo.InvariantCulture)).Append('}');
        _args.Add(s);
        _text!.Append(s);
    }

    public void AppendFormatted(string? value) => AppendFormatted<string?>(value, null);

    public readonly string Template => _fixedTemplate ?? _tpl?.ToString() ?? string.Empty;
    public readonly string Text => _fixedText ?? _text?.ToString() ?? string.Empty;
    public readonly IReadOnlyList<string> Args => (IReadOnlyList<string>?)_fixedArgs ?? _args ?? (IReadOnlyList<string>)Array.Empty<string>();

    public readonly override string ToString() => Text;

    public static implicit operator Loc(string? s) => new(s ?? string.Empty, Array.Empty<string>(), s ?? string.Empty);
    public static implicit operator string(Loc l) => l.Text;

    /// <summary>Hufunga kiolezo kilichokamilika (kwa matumizi ya ternary: <c>c ? L($"..") : L($"..")</c>).</summary>
    public static Loc L(Loc l) => new(l.Template, l.Args.ToArray(), l.Text);

    /// <summary>Kiolezo + vigeu kwa ajili ya JSON (Check, bendera, arifa).</summary>
    public readonly LocMessage ToMessage() => new(Template, Args.ToArray());
}

/// <summary>Umbo la JSON la ujumbe unaotafsirika: tpl = kiolezo (Kiswahili), args = vigeu.</summary>
public sealed record LocMessage(string Tpl, string[] Args);
