using System.Globalization;
using System.Text;

namespace ChamaLink.Infrastructure.Services.Documents;

/// <summary>
/// KICHAPISHI CHA PDF BILA MSAIDIZI WA NJE.
///
/// Kwa nini tumeandika hii wenyewe: ChamaLink inaenda kwenye kompyuta za vikundi
/// ambazo hazina intaneti ya uhakika. Maktaba ya PDF ya NuGet ingehitaji kupakuliwa
/// wakati wa kujenga mfumo — na ikiwa seva haina intaneti, hakuna PDF kabisa.
/// Hii inatumia fonts mbili tu zinazokaa ndani ya PDF (Helvetica), hivyo faili
/// inafunguka kwenye kila kifaa: simu, kompyuta, printa.
///
/// Ni KICHAPISHI CHA MAANDISHI pekee (hakuna picha) — kwa risiti na ripoti za
/// hazina hiyo inatosha, na inafanya faili kuwa ndogo.
/// </summary>
public sealed class SimplePdf
{
    public const float PageWidth = 595f;   // A4 kwa pointi
    public const float PageHeight = 842f;
    public const float MarginLeft = 46f;
    public const float MarginRight = 46f;
    public const float MarginTop = 50f;
    public const float MarginBottom = 52f;

    private sealed class Page
    {
        public readonly StringBuilder Content = new();
        public float Y = PageHeight - MarginTop;
    }

    private readonly List<Page> _pages = new();
    private Page _page;

    public SimplePdf()
    {
        _page = new Page();
        _pages.Add(_page);
    }

    public float CurrentY => _page.Y;
    public int PageCount => _pages.Count;
    public float BodyBottom => MarginBottom + 18f;

    private void NewPage()
    {
        _page = new Page();
        _pages.Add(_page);
    }

    public void EnsureSpace(float needed)
    {
        if (_page.Y - needed < BodyBottom) NewPage();
    }

    public void Gap(float points) => _page.Y -= points;

    /// <summary>Maandishi ya mstari mmoja. Rangi: 0 = nyeusi, 1 = kijivu, 2 = nzito (bluu ya kikundi).</summary>
    public void Text(float x, string text, float size = 10f, bool bold = false, int tone = 0)
    {
        var safe = Sanitize(text);
        if (safe.Length == 0) return;
        var font = bold ? "/F2" : "/F1";
        var (r, g, b) = tone switch
        {
            1 => (0.36f, 0.40f, 0.45f),
            2 => (0.08f, 0.18f, 0.35f),
            _ => (0f, 0f, 0f)
        };
        _page.Content.Append(CultureInfo.InvariantCulture,
            $"{r:0.###} {g:0.###} {b:0.###} rg BT {font} {size:0.##} Tf {x:0.##} {_page.Y:0.##} Td ({Escape(safe)}) Tj ET\n");
    }

    /// <summary>Mstari wa maandishi kwenye nafasi ya sasa, kisha kushuka.</summary>
    public void Line(string text, float size = 10f, bool bold = false, int tone = 0, float indent = 0f, float leading = 14f)
    {
        EnsureSpace(leading);
        Text(MarginLeft + indent, text, size, bold, tone);
        _page.Y -= leading;
    }

    /// <summary>Kipengele kilichopangwa kwa safu: maandishi kushoto, kiasi kulia.</summary>
    public void Row(string left, string right, float size = 10f, bool bold = false, int tone = 0,
        float rightEdge = PageWidth - MarginRight, float leading = 14f, float indent = 0f)
    {
        EnsureSpace(leading);
        Text(MarginLeft + indent, left, size, bold, tone);
        var w = Width(right, size, bold);
        Text(rightEdge - w, right, size, bold, tone);
        _page.Y -= leading;
    }

    /// <summary>Mstari wa kugawanya.</summary>
    public void Rule(float thickness = 0.6f, int tone = 1)
    {
        EnsureSpace(6f);
        var (r, g, b) = tone switch
        {
            2 => (0.08f, 0.18f, 0.35f),
            _ => (0.78f, 0.81f, 0.84f)
        };
        _page.Content.Append(CultureInfo.InvariantCulture,
            $"{r:0.###} {g:0.###} {b:0.###} RG {thickness:0.##} w {MarginLeft:0.##} {_page.Y:0.##} m {PageWidth - MarginRight:0.##} {_page.Y:0.##} l S\n");
        _page.Y -= 8f;
    }

    /// <summary>Kisanduku cha rangi (kichwa cha sehemu).</summary>
    public void Band(string text, float height = 20f, float size = 10f)
    {
        EnsureSpace(height + 8f);
        _page.Content.Append(CultureInfo.InvariantCulture,
            $"0.94 0.95 0.97 rg {MarginLeft:0.##} {_page.Y - height + 10f:0.##} {PageWidth - MarginLeft - MarginRight:0.##} {height:0.##} re f\n");
        Text(MarginLeft + 6f, text, size, bold: true, tone: 2);
        _page.Y -= height + 4f;
    }

    /// <summary>Ukubwa wa maandishi kwa Helvetica (wastani wa herufi 0.52 × ukubwa).</summary>
    public static float Width(string text, float size, bool bold = false)
        => Sanitize(text).Length * size * (bold ? 0.556f : 0.5f);

    /// <summary>Inafunga hati na kurudisha bytes za PDF.</summary>
    public byte[] Build()
    {
        var objects = new List<string>();

        // 1 = Catalog, 2 = Pages, 3 = F1, 4 = F2, kisha kila ukurasa: page + content
        objects.Add(""); // 0 haitumiki (index = nambari ya object - 1)
        objects.Add(""); // 1 Catalog
        objects.Add(""); // 2 Pages
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        var pageRefs = new List<string>();
        foreach (var p in _pages)
        {
            var content = p.Content.ToString();
            // Nambari ya object = index kwenye list (index 0 haitumiki).
            var pageNum = objects.Count;
            var contentNum = pageNum + 1;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0.##} {PageHeight:0.##}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentNum} 0 R >>");
            pageRefs.Add($"{pageNum} 0 R");

            var bytes = Encoding.ASCII.GetBytes(content);
            objects.Add($"<< /Length {bytes.Length} >>\nstream\n{content}\nendstream");
        }

        objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[2] = $"<< /Type /Pages /Kids [{string.Join(" ", pageRefs)}] /Count {_pages.Count} >>";

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        var offsets = new List<int>();
        var ascii = Encoding.Latin1;
        foreach (var (obj, i) in objects.Select((o, i) => (o, i)))
        {
            if (i == 0) { offsets.Add(0); continue; }
            offsets.Add(ascii.GetByteCount(sb.ToString()));
            sb.Append($"{i} 0 obj\n{obj}\nendobj\n");
        }

        var xrefPos = ascii.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Count}\n");
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i < objects.Count; i++)
            sb.Append($"{offsets[i]:0000000000} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");

        return ascii.GetBytes(sb.ToString());
    }

    /// <summary>PDF inatumia WinAnsi: herufi za Kiswahili zote ni ASCII. Mengine yanabadilishwa.</summary>
    private static string Sanitize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                '\u2014' or '\u2013' => '-',
                '\u2019' or '\u2018' => '\'',
                '\u201c' or '\u201d' => '"',
                '\u00b7' => '-',
                '\u2265' => '>',
                '\u2264' => '<',
                _ => ch <= 0xFF ? ch : '?'
            });
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
}
