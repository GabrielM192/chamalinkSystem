using System.Globalization;
using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ExcelDataReader;

using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

// Parses a treasurer's monthly-grid Excel ledger into TreasuryExcelRowDto
// rows. Structure (from the real VIJANA UMOJA MKOBA sheet):
//   - Header row contains: S/N | NAM | JAN..DEC | (blank) | KIANZIO | (blank NEG) | MSIBA | SHEREHE | MAWASILIANO
//   - One member row each; some members go "NON ACTIVE MEMBER" (the words
//     NON, ACTIVE, MEMBER split across 3 consecutive month columns).
//   - '*' in a month cell = did not contribute that month (0).
//   - JUMLA / AKIBA ILIYOPO rows are totals - skipped by name.
// This parser is structural only: no member matching, no writes.
public class TreasuryExcelParserService : ITreasuryExcelParserService
{
    // ExcelDataReader needs the CodePages encoding provider registered once
    // on .NET Core/.NET 8 (the real sheet is xlsx, so usually fine, but the
    // legacy .xls path requires it; registering is harmless and idempotent).
    static TreasuryExcelParserService()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private static readonly string[] MonthHeaders =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    public async Task<List<TreasuryExcelRowDto>> ParseAsync(Stream stream)
    {
        var rows = new List<TreasuryExcelRowDto>();

        // FIX (ukaguzi 2026-09-15): ExcelReaderFactory.CreateReader hutupa
        // HeaderException ("Invalid file signature") mtu akipakia faili ambalo
        // si Excel halisi. Hiyo si AppException, kwa hiyo ilikuwa inaangukia
        // GlobalExceptionMiddleware kama HTTP 500 - yaani kosa la MTUMIAJI
        // (amepandisha faili baya) lilikuwa likionekana kama kosa la SERVER.
        //
        // Niliithibitisha: POST faili la maandishi tu -> HTTP 500
        //                       "ExcelDataReader.Exceptions.HeaderException:
        //                        Invalid file signature."
        //
        // Sasa tunaitoa kama ValidationException -> HTTP 400 na ujumbe
        // unaomweleza mtumiaji nini cha kufanya.
        IExcelDataReader reader;
        try
        {
            // Leave the stream open to the caller; ExcelDataReader disposes its
            // own internal reader on Dispose.
            reader = ExcelReaderFactory.CreateReader(stream);
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new ValidationException(
                "Faili hili si Excel halisi (.xlsx au .xls). Tafadhali pakia faili la Treasury lililotolewa na benki/M-Koba.");
        }

        using var _ = reader;

        // Build a 2D snapshot of cells so we can scan rows arbitrarily for
        // the header row and then read member rows. The sheets are small
        // (a single yearly ledger) so materialising is cheap and keeps the
        // logic simple vs. streaming the reader twice.
        var sheet = new List<List<object?>>();

        // First result set = first (only) sheet.
        do
        {
            while (reader.Read())
            {
                var row = new List<object?>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i));
                sheet.Add(row);
            }
        } while (reader.NextResult());

        // 1. Find the header row: the first row that contains a cell == "NAM"
        // AND a cell == "JAN". (The real sheet starts at Excel row 2.)
        int headerRowIndex = -1;
        for (int r = 0; r < sheet.Count; r++)
        {
            var row = sheet[r];
            bool hasName = false, hasJan = false;
            foreach (var cell in row)
            {
                var s = NormalizeCell(cell);
                if (s == "NAM") hasName = true;
                else if (s == "JAN") hasJan = true;
            }
            if (hasName && hasJan) { headerRowIndex = r; break; }
        }

        if (headerRowIndex < 0)
        {
            // No recognizable header - cannot parse safely. Caller turns
            // this into a BadRequest.
            throw new InvalidDataException(
                "Header row haikupatikana (lazima kiwe na column 'NAM' na 'JAN'). " +
                "Hakikisha Excel ni ile ya mtunza-hazina (mwezi = column, mwanachama = safu).");
        }

        // 2. Build column index map from the header row.
        var header = sheet[headerRowIndex];
        int colName = -1, colKianzio = -1, colMsiba = -1, colSherehe = -1, colPhone = -1;
        var monthCol = new int[12];
        for (int i = 0; i < 12; i++) monthCol[i] = -1;

        for (int i = 0; i < header.Count; i++)
        {
            var h = NormalizeCell(header[i]);
            if (h.Length == 0) continue;
            if (h == "NAM") colName = i;
            else if (h == "KIANZIO") colKianzio = i;
            else if (h == "MSIBA") colMsiba = i;
            else if (h == "SHEREHE") colSherehe = i;
            else if (h == "MAWASILIANO") colPhone = i;
            else
            {
                for (int m = 0; m < 12; m++)
                {
                    if (h == MonthHeaders[m]) { monthCol[m] = i; break; }
                }
            }
        }

        if (colName < 0 || monthCol[0] < 0)
        {
            throw new InvalidDataException(
                "Column za 'NAM' au 'JAN' hazikupatikana kwenye header.");
        }

        // 3. Read member rows (everything after the header).
        for (int r = headerRowIndex + 1; r < sheet.Count; r++)
        {
            var row = sheet[r];

            string name = colName < row.Count ? NormalizeName(row[colName]) : string.Empty;
            if (string.IsNullOrEmpty(name)) continue;

            // Skip totals / summary rows by name.
            if (name == "JUMLA" || name.Contains("AKIBA")) continue;

            var dto = new TreasuryExcelRowDto
            {
                ExcelName = (colName < row.Count ? row[colName]?.ToString() ?? "" : "").Trim(),
                RowNumber = r + 1 // 1-indexed Excel row
            };

            // Months
            for (int m = 0; m < 12; m++)
            {
                int c = monthCol[m];
                if (c < 0 || c >= row.Count) continue;
                dto.MonthlyAmounts[m] = ParseAmount(row[c]);
            }

            // Detect "NON ACTIVE MEMBER" triple: three consecutive month
            // columns whose cell text is NON, ACTIVE, MEMBER. Record the
            // month the member became inactive from (the NON month).
            for (int m = 0; m <= 9; m++)
            {
                int cN = monthCol[m], cA = (m + 1) < 12 ? monthCol[m + 1] : -1,
                    cM = (m + 2) < 12 ? monthCol[m + 2] : -1;
                if (cN < 0 || cA < 0 || cM < 0) continue;
                if (cN >= row.Count || cA >= row.Count || cM >= row.Count) continue;

                if (NormalizeCell(row[cN]) == "NON" &&
                    NormalizeCell(row[cA]) == "ACTIVE" &&
                    NormalizeCell(row[cM]) == "MEMBER")
                {
                    dto.IsNonActive = true;
                    dto.NonActiveFromMonth = m + 1; // 1-based
                    // Those three cells are markers, not amounts - make sure
                    // they are null so nothing gets posted for them.
                    dto.MonthlyAmounts[m] = null;
                    dto.MonthlyAmounts[m + 1] = null;
                    dto.MonthlyAmounts[m + 2] = null;
                    break;
                }
            }

            // If a member is flagged non-active, also drop any amounts that
            // landed at/after the inactivity month (they are not real
            // contributions - they were the NON/ACTIVE/MEMBER words or blanks).
            if (dto.IsNonActive)
            {
                for (int m = dto.NonActiveFromMonth - 1; m < 12; m++)
                    dto.MonthlyAmounts[m] = null;
            }

            // KIANZIO / MSIBA / SHEREHE (separate columns)
            dto.JoiningFee = colKianzio >= 0 && colKianzio < row.Count
                ? ParseAmount(row[colKianzio]) : null;
            dto.Msiba = colMsiba >= 0 && colMsiba < row.Count
                ? ParseAmount(row[colMsiba]) : null;
            dto.Sherehe = colSherehe >= 0 && colSherehe < row.Count
                ? ParseAmount(row[colSherehe]) : null;

            // Phone (hint only - matching ignores it)
            if (colPhone >= 0 && colPhone < row.Count)
                dto.Phone = row[colPhone]?.ToString()?.Trim();

            rows.Add(dto);
        }

        await Task.CompletedTask;
        return rows;
    }

    // ---- helpers ---------------------------------------------------------

    // A cell becomes a decimal? amount, or null when it is unpaid ('*'),
    // a non-numeric marker (NON/ACTIVE/MEMBER), or blank. Handles numbers
    // stored as doubles (Excel's native), as ints, and as strings like
    // "10000" or "10,000" (thousands separators from a manual edit).
    private static decimal? ParseAmount(object? v)
    {
        if (v == null) return null;

        switch (v)
        {
            case double d:
                return Math.Round((decimal)d, 2);
            case float f:
                return Math.Round((decimal)f, 2);
            case int i:
                return i;
            case long l:
                return l;
            case decimal dec:
                return dec;
            case bool:
                return null;
            case string s:
                var t = s.Trim();
                if (t.Length == 0) return null;
                if (t == "*") return null; // unpaid marker
                // a numeric string, possibly with thousands separators
                if (decimal.TryParse(t, NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                // could be the NON/ACTIVE/MEMBER words - not an amount
                return null;
            default:
                // fallback: convert to string and retry
                var str = v.ToString()?.Trim();
                if (string.IsNullOrEmpty(str) || str == "*") return null;
                if (decimal.TryParse(str, NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var parsed2))
                    return parsed2;
                return null;
        }
    }

    // Normalise a header/cell label for matching: uppercase, trim, collapse
    // spaces. Used for column identification and the NON/ACTIVE/MEMBER
    // token detection.
    private static string NormalizeCell(object? v)
    {
        if (v == null) return string.Empty;
        var s = v.ToString()?.Trim().ToUpperInvariant().TrimEnd('.') ?? string.Empty;
        return CollapseSpaces(s);
    }

    // Normalise a member name (used to decide skip/keep and for matching).
    private static string NormalizeName(object? v)
    {
        if (v == null) return string.Empty;
        var s = v.ToString()?.Trim().ToUpperInvariant().TrimEnd('.') ?? string.Empty;
        return CollapseSpaces(s);
    }

    private static string CollapseSpaces(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool lastSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace && sb.Length > 0) sb.Append(' ');
                lastSpace = true;
            }
            else { sb.Append(ch); lastSpace = false; }
        }
        return sb.ToString().Trim();
    }
}
