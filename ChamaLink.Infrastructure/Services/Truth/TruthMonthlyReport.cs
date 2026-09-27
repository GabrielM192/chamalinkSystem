using System.Globalization;
using System.Text;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// Taarifa ya mwezi MMOJA (AWAMU J). Kusoma tu: hakuna mgawanyo wa fedha hapa — kila namba inatoka kwenye
/// PeriodLine ambazo ContributionTruthEngine imeshazihesabu (zile zile za "Taarifa Yangu" na ripoti nyingine).
/// </summary>
public partial class TruthLedgerService
{
    public sealed record MonthlyReportRow(
        Guid MemberId, string MemberNumber, string FullName, string? Phone,
        decimal Target, decimal Paid, DateTime? PaidAt, string Status,
        decimal FineCharged, decimal FinePaid, bool FineWaived, decimal Outstanding,
        decimal TotalArrears, decimal JoinFeeOutstanding);

    public sealed record MonthlyReport(
        Guid GroupId, string GroupName, int Year, int Month, string MonthLabel, DateTime Deadline,
        bool MonthClosed, DateTime? DataUntil, bool DataComplete,
        int TotalMembers, int PaidOnTime, int PaidLate, int PartiallyPaid, int NotPaid, int StillOpen,
        decimal TotalExpected, decimal TotalCollected, decimal CollectionRatePercent,
        decimal FinesCharged, decimal FinesCollected, decimal OutstandingForMonth,
        List<MonthlyReportRow> Members, string WhatsAppText);

    public async Task<MonthlyReport> GetMonthlyReportAsync(Guid groupId, int year, int month, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new ValidationException("Mwezi lazima uwe kati ya 1 na 12.");
        var group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new NotFoundException("Kikundi hakipo.");
        var dbPolicy = await GetSettingsAsync(groupId, ct);
        var policy = ToTruthPolicy(dbPolicy);
        var ym = new YearMonth(year, month);
        if (ym.CompareTo(policy.GroupFirstPeriod) < 0)
            throw new ValidationException($"Michango ya kikundi ilianza {policy.GroupFirstPeriod.Label}. Chagua mwezi wa baadaye.");

        var all = await GetGroupTruthAsync(groupId, null, ct);
        var dataUntil = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId)
            .Select(t => (DateTime?)t.Timestamp).MaxAsync(ct);

        var rows = new List<MonthlyReportRow>();
        foreach (var t in all)
        {
            var line = t.Statement.Periods.FirstOrDefault(p => p.Period == ym);
            if (line is null || !line.WindowOpened) continue; // hakuwa mwanachama bado mwezi huo
            var paidAt = line.ContributionCompletedAt
                         ?? t.Statement.Allocations.Where(a => a.Period == ym && a.Target is "Contribution" or "Fine")
                             .Select(a => (DateTime?)a.Timestamp).Max();
            rows.Add(new MonthlyReportRow(
                t.MemberId, t.MemberNumber, t.Statement.Member.Name, t.Phones.FirstOrDefault(),
                line.ContributionDue, line.ContributionPaid, paidAt, line.Status.ToString(),
                line.FineWaived ? 0m : line.FineDue, line.FinePaid, line.FineWaived, line.Outstanding,
                t.Statement.Arrears, t.Statement.JoinFeeOutstanding));
        }
        rows = rows.OrderBy(r => NumberKey(r.MemberNumber)).ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        var deadline = policy.BaseDeadline(ym);
        var closed = NowEat() > deadline;
        var complete = dataUntil is { } du && du >= deadline;
        int Count(PeriodStatus s) => rows.Count(r => r.Status == s.ToString());
        var expected = rows.Sum(r => r.Target);
        var collected = rows.Sum(r => r.Paid);

        var report = new MonthlyReport(
            groupId, group.Name, year, month, MonthNameSw(month) + " " + year, deadline, closed, dataUntil, complete,
            rows.Count, Count(PeriodStatus.PaidOnTime), Count(PeriodStatus.PaidLate), Count(PeriodStatus.PartiallyPaid),
            Count(PeriodStatus.Unpaid), Count(PeriodStatus.Open),
            expected, collected, expected > 0 ? Math.Round(collected * 100m / expected, 1) : 0m,
            rows.Sum(r => r.FineCharged), rows.Sum(r => r.FinePaid), rows.Sum(r => r.Outstanding),
            rows, "");
        return report with { WhatsAppText = MonthlyReportText.Format(report) };
    }

    private static (int, string) NumberKey(string n)
    {
        var m = System.Text.RegularExpressions.Regex.Match(n ?? "", @"(\d+)$");
        return (m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : int.MaxValue, n ?? "");
    }

    private static readonly string[] SwMonths =
        { "Januari", "Februari", "Machi", "Aprili", "Mei", "Juni", "Julai", "Agosti", "Septemba", "Oktoba", "Novemba", "Desemba" };
    public static string MonthNameSw(int month) => SwMonths[month - 1];
}

/// <summary>Maandishi ya taarifa ya mwezi, tayari kubandikwa kwenye kundi la WhatsApp.</summary>
public static class MonthlyReportText
{
    private static readonly CultureInfo Num = CultureInfo.InvariantCulture;
    private static string N(decimal v) => v.ToString("#,0", Num);
    private static string D(DateTime? d) => d?.ToString("dd/MM/yyyy", Num) ?? "—";

    /// <summary>Kwenye kundi la WhatsApp namba ya simu inafichwa katikati: 255712805984 → 0712***984.</summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "hana namba";
        var local = phone.StartsWith(ChamaLink.Domain.MkobaPlatform.CountryDialCode) ? "0" + phone[ChamaLink.Domain.MkobaPlatform.CountryDialCode.Length..] : phone;
        return local.Length > 7 ? local[..4] + "***" + local[^3..] : local;
    }

    public static string StatusSw(string status) => status switch
    {
        nameof(PeriodStatus.PaidOnTime) => "✅ Kwa wakati",
        nameof(PeriodStatus.PaidLate) => "⏰ Amechelewa",
        nameof(PeriodStatus.PartiallyPaid) => "➗ Sehemu",
        nameof(PeriodStatus.Unpaid) => "❌ Hajalipa",
        nameof(PeriodStatus.Open) => "⏳ Bado (muda upo)",
        _ => status
    };

    public static string Format(TruthLedgerService.MonthlyReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📊 *TAARIFA YA MCHANGO — {r.MonthLabel.ToUpperInvariant()}*");
        sb.AppendLine($"Kikundi: {r.GroupName}");
        sb.AppendLine($"Mwisho wa kulipa: {D(r.Deadline)}");
        if (!r.MonthClosed)
            sb.AppendLine("_⏳ Muda wa kulipa bado haujaisha — \"bado\" si deni mpaka tarehe ya mwisho ipite._");
        if (!r.DataComplete)
            sb.AppendLine(r.DataUntil is null
                ? "_⚠️ Hakuna taarifa ya M-Koba iliyoingizwa bado._"
                : $"_⚠️ Taarifa ya M-Koba iliyoingizwa ni hadi {D(r.DataUntil)} — malipo ya baada ya hapo bado hayajaonekana._");
        sb.AppendLine();
        sb.AppendLine($"Wanachama: {r.TotalMembers}");
        sb.AppendLine($"✅ Kwa wakati: {r.PaidOnTime}");
        if (r.PaidLate > 0) sb.AppendLine($"⏰ Walichelewa: {r.PaidLate}");
        if (r.PartiallyPaid > 0) sb.AppendLine($"➗ Wamelipa sehemu: {r.PartiallyPaid}");
        if (r.MonthClosed) sb.AppendLine($"❌ Hawajalipa: {r.NotPaid}");
        else sb.AppendLine($"⏳ Bado hawajalipa: {r.StillOpen}");
        sb.AppendLine($"Kiwango cha ukusanyaji: {r.CollectionRatePercent.ToString("0.#", Num)}%");
        sb.AppendLine($"Kilichokusanywa: TSH {N(r.TotalCollected)} / {N(r.TotalExpected)}");
        if (r.FinesCharged > 0) sb.AppendLine($"Faini: zimelipwa TSH {N(r.FinesCollected)} kati ya {N(r.FinesCharged)}");
        if (r.OutstandingForMonth > 0) sb.AppendLine($"Deni la mwezi huu: TSH {N(r.OutstandingForMonth)}");
        sb.AppendLine();
        sb.AppendLine("---");
        foreach (var m in r.Members)
        {
            var id = string.IsNullOrEmpty(m.MemberNumber) ? "" : m.MemberNumber + " | ";
            sb.AppendLine($"{id}{m.FullName} | {MaskPhone(m.Phone)}");
            var line = $"Lengo: {N(m.Target)} | Alicholipa: {N(m.Paid)}";
            if (m.Paid > 0 && m.PaidAt is not null) line += $" ({m.PaidAt.Value.ToString("dd/MM", Num)})";
            line += $" | {StatusSw(m.Status)}";
            if (m.FineWaived) line += " | Faini imesamehewa";
            else if (m.FineCharged > 0) line += $" | Faini: {N(m.FineCharged)}";
            if (m.Outstanding > 0) line += $" | Deni la mwezi: {N(m.Outstanding)}";
            sb.AppendLine(line);
            var extra = new List<string>();
            if (m.TotalArrears > m.Outstanding) extra.Add($"deni lote la michango {N(m.TotalArrears)}");
            if (m.JoinFeeOutstanding > 0) extra.Add($"kianzio {N(m.JoinFeeOutstanding)}");
            if (extra.Count > 0) sb.AppendLine("   ↳ " + string.Join(", ", extra));
            sb.AppendLine();
        }
        sb.Append("_Imetolewa na ChamaLink kutoka M-Koba + kumbukumbu zilizoidhinishwa na viongozi._");
        return sb.ToString();
    }
}
