using ChamaLink.Domain;
using System.Globalization;
using System.Text.RegularExpressions;
using ChamaLink.Application.DTOs;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Application.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Parses M-Koba PDF statements into transaction rows.
/// Each PDF row is a block of 2-3 physical lines with columns at different heights.
/// We use date tokens as anchors and X positions of headers to sort words into columns.
/// </summary>
public class MkobaParserService : IMKobaParserService
{
    private const double MaxRowExtent = 15.0;

    private static readonly Regex DateWordPattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex TimeWordPattern = new(@"^\d{2}:\d{2}:\d{2}(?:\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex PhoneWordPattern = new(@"^(255\d{9}|0[67]\d{8})$", RegexOptions.Compiled);
    private static readonly Regex MoneyWordPattern = new(@"([\d,]+.\d{2})\s*TZS", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream stream)
        => (await ParseStatementDetailedAsync(stream)).Transactions;

    private static readonly Regex GroupBalancePattern = new(@"Group\s*Balance:\s*([\d,]+\.\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContributionTotalPattern = new(@"Balance:\s*Contribution\s*([\d,]+\.\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Kama ParseStatementAsync, lakini: (1) inasoma BALANCE ya kila mstari, (2) inaripoti
    /// mistari ambayo haikusomeka badala ya kuiruka kimya, (3) inasoma jumla zilizotangazwa
    /// na PDF ("Group Balance", "Balance: Contribution") kwa ukaguzi.
    /// </summary>
    public async Task<MKobaParseResult> ParseStatementDetailedAsync(Stream stream)
    {
        var result = new MKobaParseResult();
        var transactions = result.Transactions;

        PdfDocument openedPdf;
        try
        {
            openedPdf = PdfDocument.Open(stream);
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new ValidationException("File is not a valid PDF. Please upload M-Koba PDF statement.");
        }

        using (var pdfDocument = openedPdf)
        {
            int order = 0;
            foreach (var page in pdfDocument.GetPages())
            {
                var pageText = page.Text ?? string.Empty;
                var gb = GroupBalancePattern.Match(pageText);
                if (gb.Success && result.DeclaredGroupBalance == null)
                    result.DeclaredGroupBalance = decimal.Parse(gb.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
                var ct = ContributionTotalPattern.Match(pageText);
                if (ct.Success)
                    result.DeclaredContributionTotal = decimal.Parse(ct.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);

                var words = page.GetWords().ToList();
                var columns = FindColumnBoundaries(words);
                if (columns == null) continue; // cover page or no headers

                foreach (var item in ParseRowsFromWords(words, columns, page.Number, result.UnreadableRows))
                {
                    item.PageNumber = page.Number;
                    item.RowOrder = ++order;
                    transactions.Add(item);
                }
            }
        }

        await Task.CompletedTask;
        return result;
    }

    private class ColumnBoundaries
    {
        public double DateX = 0;
        public double ReferenceX = 0;
        public double MemberX = 0;
        public double DescriptionX = 0;
    }

    private static ColumnBoundaries? FindColumnBoundaries(List<Word> words)
    {
        Word? Find(string text) => words.FirstOrDefault(w => string.Equals(w.Text, text, StringComparison.OrdinalIgnoreCase));

        var dateHeader = Find("Date");
        var refHeader = Find("Refference") ?? Find("Reference");
        var memberHeader = Find("Member");
        var descHeader = Find("Description");

        if (dateHeader == null || refHeader == null || memberHeader == null || descHeader == null)
            return null;

        return new ColumnBoundaries
        {
            DateX = dateHeader.BoundingBox.Left,
            ReferenceX = refHeader.BoundingBox.Left,
            MemberX = memberHeader.BoundingBox.Left,
            DescriptionX = descHeader.BoundingBox.Left
        };
    }

    private static List<MKobaTransactionItemDto> ParseRowsFromWords(List<Word> words, ColumnBoundaries columns,
        int pageNumber = 0, List<string>? unreadable = null)
    {
        var results = new List<MKobaTransactionItemDto>();

        var anchors = words
            .Where(w => DateWordPattern.IsMatch(w.Text) && w.BoundingBox.Left < columns.ReferenceX)
            .OrderByDescending(w => w.BoundingBox.Top)
            .ToList();

        for (int i = 0; i < anchors.Count; i++)
        {
            double anchorTop = anchors[i].BoundingBox.Top;
            double upperBound = i > 0 ? (anchors[i - 1].BoundingBox.Top + anchorTop) / 2 : anchorTop + MaxRowExtent;
            double lowerBound = i < anchors.Count - 1 ? (anchorTop + anchors[i + 1].BoundingBox.Top) / 2 : anchorTop - MaxRowExtent;

            var rowWords = words.Where(w => w.BoundingBox.Top >= lowerBound && w.BoundingBox.Top <= upperBound).ToList();
            var item = BuildTransactionFromRowWords(rowWords, columns);
            if (item != null) results.Add(item);
            else unreadable?.Add($"Ukurasa {pageNumber}: mstari usiosomeka — \"{string.Join(" ", rowWords.OrderByDescending(w => w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).Select(w => w.Text))}\"");
        }

        return results;
    }

    private static MKobaTransactionItemDto? BuildTransactionFromRowWords(List<Word> rowWords, ColumnBoundaries columns)
    {
        var moneyWords = rowWords.Where(w => MoneyWordPattern.IsMatch(w.Text)).OrderBy(w => w.BoundingBox.Left).ToList();
        if (moneyWords.Count == 0) return null;

        var amountMatch = MoneyWordPattern.Match(moneyWords[0].Text);
        string amountDigits = amountMatch.Groups[1].Value.Replace(",", "");
        if (!decimal.TryParse(amountDigits, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return null;

        var textWords = rowWords.Except(moneyWords).ToList();

        var dateWords = new List<Word>();
        var refWords = new List<Word>();
        var memberWords = new List<Word>();
        var descriptionWords = new List<Word>();

        foreach (var word in textWords)
        {
            double x = word.BoundingBox.Left;
            if (x >= columns.DescriptionX - 1) descriptionWords.Add(word);
            else if (x >= columns.MemberX - 1) memberWords.Add(word);
            else if (x >= columns.ReferenceX - 1) refWords.Add(word);
            else dateWords.Add(word);
        }

        string dateText = dateWords.FirstOrDefault(w => DateWordPattern.IsMatch(w.Text))?.Text ?? string.Empty;
        string timeText = dateWords.FirstOrDefault(w => TimeWordPattern.IsMatch(w.Text))?.Text ?? string.Empty;
        string referenceNo = string.Join(" ", refWords.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));

        if (string.IsNullOrEmpty(dateText) || string.IsNullOrEmpty(referenceNo)) return null;

        var parsedDate = ParseTransactionDate(dateText, timeText);
        if (parsedDate == null) return null; // Skip rows with unreadable dates instead of using today

        var phoneWord = memberWords.FirstOrDefault(w => PhoneWordPattern.IsMatch(w.Text));
        string phoneNumber = phoneWord != null ? CleanPhoneNumber(phoneWord.Text) : string.Empty;
        string memberName = string.Join(" ",
            memberWords.Except(phoneWord != null ? new[] { phoneWord } : Array.Empty<Word>())
                .OrderByDescending(w => w.BoundingBox.Top)
                .ThenBy(w => w.BoundingBox.Left)
                .Select(w => w.Text));

        string descriptionText = string.Join(" ", descriptionWords.OrderByDescending(w => w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).Select(w => w.Text));
        decimal? balance = null;
        if (moneyWords.Count > 1)
        {
            var bm = MoneyWordPattern.Match(moneyWords[^1].Text);
            if (decimal.TryParse(bm.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var b)) balance = b;
        }
        bool isWithdrawal = descriptionText.Contains("Withdraw", StringComparison.OrdinalIgnoreCase);

        return new MKobaTransactionItemDto
        {
            ReferenceNumber = referenceNo,
            TransactionDate = parsedDate.Value,
            PhoneNumber = phoneNumber,
            MemberName = ToDisplayName(memberName),
            Amount = amount,
            IsWithdrawal = isWithdrawal,
            Balance = balance,
            Description = descriptionText
        };
    }

    private static DateTime? ParseTransactionDate(string datePart, string timePart)
    {
        string combined = string.IsNullOrEmpty(timePart) ? datePart : $"{datePart} {timePart}";
        if (DateTime.TryParse(combined, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        // Date unreadable - caller will skip this row instead of using UtcNow
        return null;
    }

    private static string CleanPhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        string digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0")) digits = MkobaPlatform.CountryDialCode + digits.Substring(1);
        return digits;
    }

    private static string ToDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return rawName;
        var words = rawName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 1 ? char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant() : w.ToUpperInvariant());
        return string.Join(" ", words);
    }
}
