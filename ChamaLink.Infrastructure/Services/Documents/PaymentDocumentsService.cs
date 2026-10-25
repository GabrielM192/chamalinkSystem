using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Documents;

/// <summary>
/// PDF ZA HAZINA (SASISHO21).
///
///   1. RISITI YA MALIPO — mwanachama akipokea uthibitisho, anapata karatasi
///      yenye jina lake, kiasi, kodi ya malipo, namba ya muamala na deni lililobaki.
///   2. RIPOTI YA SANDUKU — kiongozi anapata orodha ya mistari yote ya statement:
///      iliyotambuliwa, iliyopendekezwa, isiyotambuliwa, na jumla.
///
/// Kanuni: PDF inasoma UKWELI ulioandikwa tayari. Hahesabu kitu kipya, haibadilishi
/// deni, na haiandiki tena kile ambacho hakiko kwenye database.
/// </summary>
public class PaymentDocumentsService
{
    private readonly ApplicationDbContext _context;

    public PaymentDocumentsService(ApplicationDbContext context) => _context = context;

    private static string Money(decimal v) => "TSh " + v.ToString("N0", new System.Globalization.CultureInfo("sw-TZ"));
    private static string Date(DateTime d) => d.ToString("dd/MM/yyyy HH:mm");

    // ==================================================================
    // 1. RISITI
    // ==================================================================

    /// <summary>
    /// Risiti ya malipo yaliyothibitishwa. Mwanachama anaweza kuipakua mwenyewe;
    /// viongozi wanaweza kuipakua ya mtu yeyote wa kikundi.
    /// </summary>
    public async Task<(byte[] Bytes, string FileName)> ReceiptAsync(Guid groupId, Guid receiptId, Guid userId, CancellationToken ct = default)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct, tracking: false);
        var leader = MemberAccess.IsEffectiveLeader(me);

        var r = await _context.PaymentReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == receiptId && x.GroupId == groupId, ct)
                ?? throw new NotFoundException("Risiti haipo.");

        // Mwanachama anaona risiti zake tu.
        if (!leader && r.ConfirmedGroupMemberId != me.Id && r.SuggestedGroupMemberId != me.Id)
            throw new ForbiddenException("Risiti hii si yako.");

        var group = await _context.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct) ?? "Kikundi";

        var memberId = r.ConfirmedGroupMemberId ?? r.SuggestedGroupMemberId;
        GroupMember? member = null;
        if (memberId is { } mid)
            member = await _context.GroupMembers.AsNoTracking().Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == mid && m.GroupId == groupId, ct);

        decimal? owed = null, balance = null;
        if (member is not null)
        {
            owed = await _context.EventObligations.AsNoTracking()
                .Where(o => o.GroupId == groupId && o.GroupMemberId == member.Id && o.Status == EventObligationStatus.Pending)
                .SumAsync(o => o.RequiredAmount - o.DeductedAmount, ct);
            balance = await _context.MkobaRawTransactions.AsNoTracking()
                .Where(t => t.GroupId == groupId && t.AttributedGroupMemberId == member.Id && !t.IsWithdrawal)
                .SumAsync(t => t.Amount, ct);
        }

        var pdf = new SimplePdf();

        pdf.Line("RISITI YA MALIPO", 17f, bold: true, tone: 2, leading: 24f);
        pdf.Line(group, 11f, tone: 1, leading: 16f);
        pdf.Gap(2f);
        pdf.Rule(1f, 2);

        pdf.Line($"Risiti namba: {r.Id.ToString()[..8].ToUpperInvariant()}", 10f, bold: true);
        pdf.Line($"Tarehe: {Date(r.OccurredAt)}", 10f, tone: 1);
        pdf.Gap(6f);

        pdf.Band("MLIPAJI");
        pdf.Row("Mwanachama", member?.MemberNumber ?? "-", 10f);
        pdf.Row("Jina", member?.User?.FullName ?? r.PayerName ?? "-", 10f);
        pdf.Row("Namba ya simu", r.Phone ?? member?.User?.PhoneNumber ?? "-", 10f);

        pdf.Gap(4f);
        pdf.Band("MALIPO");
        pdf.Row("Kiasi", Money(r.Amount), 12f, bold: true);
        pdf.Row("Kumbukumbu (muamala)", r.Reference ?? "-", 10f);
        pdf.Row("Kodi ya malipo", r.PaymentCodeSeen ?? "-", 10f);
        pdf.Row("Chanzo", r.Source.ToString(), 10f);
        pdf.Row("Hali", r.Status == PaymentReceiptStatus.Confirmed ? "Imethibitishwa" : r.Status.ToString(), 10f);
        if (r.ConfirmedAt is { } ca)
            pdf.Row("Ilithibitishwa", Date(ca), 10f);

        if (member is not null)
        {
            pdf.Gap(4f);
            pdf.Band("BAADA YA MALIPO");
            // Onyesha jumla tu kama mstari wa M-Koba umeshapachikwa — usionyeshe sifuri ya uongo.
            if (balance is > 0) pdf.Row("Jumla ya michango iliyowekwa", Money(balance.Value), 10f);
            if (owed is not null)
                pdf.Row("Deni lililobaki", owed.Value > 0 ? Money(owed.Value) : "Hakuna deni", 11f, bold: owed.Value > 0);
        }

        pdf.Gap(10f);
        pdf.Rule();
        pdf.Line("Karatasi hii imetolewa na mfumo kutoka kumbukumbu halisi za kikundi.", 8f, tone: 1);
        pdf.Line("Hakuna kiasi kilichoandikwa kwa mkono. Kila shilingi ina mstari wake wa M-Koba.", 8f, tone: 1);

        return (pdf.Build(), $"Risiti-{member?.MemberNumber ?? "malipo"}-{r.OccurredAt:yyyyMMdd}.pdf");
    }

    // ==================================================================
    // 2. RIPOTI YA SANDUKU LA MALIPO
    // ==================================================================

    /// <summary>
    /// Ripoti ya mistari yote ya sanduku kwa kipindi. Viongozi pekee.
    /// Inaonyesha kila kitu: kilichotambuliwa, kilichopendekezwa, kisichojulikana.
    /// </summary>
    public async Task<(byte[] Bytes, string FileName)> InboxReportAsync(Guid groupId, Guid userId,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct,
            "Ripoti ya sanduku la malipo ni ya viongozi.");

        var group = await _context.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct) ?? "Kikundi";

        var q = _context.PaymentReceipts.AsNoTracking().Where(x => x.GroupId == groupId);
        if (from is { } f) q = q.Where(x => x.OccurredAt >= f);
        if (to is { } t) q = q.Where(x => x.OccurredAt <= t);
        var rows = await q.OrderByDescending(x => x.OccurredAt).Take(400).ToListAsync(ct);

        var memberIds = rows.Where(x => (x.ConfirmedGroupMemberId ?? x.SuggestedGroupMemberId) != null)
            .Select(x => (x.ConfirmedGroupMemberId ?? x.SuggestedGroupMemberId)!.Value).Distinct().ToList();
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User)
            .Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber, Name = m.User != null ? m.User.FullName : m.MemberNumber })
            .ToDictionaryAsync(x => x.Id, x => x, ct);

        var pdf = new SimplePdf();
        pdf.Line("RIPOTI YA SANDUKU LA MALIPO", 16f, bold: true, tone: 2, leading: 22f);
        pdf.Line(group, 11f, tone: 1, leading: 15f);
        pdf.Gap(2f);
        var period = from is null && to is null ? "Kipindi: yote" :
            $"Kipindi: {(from?.ToString("dd/MM/yyyy") ?? "mwanzo")} - {(to?.ToString("dd/MM/yyyy") ?? "leo")}";
        pdf.Line(period, 9f, tone: 1);
        pdf.Line($"Imetolewa: {Date(DateTime.UtcNow)}", 9f, tone: 1);
        pdf.Gap(4f);
        pdf.Rule(1f, 2);

        pdf.Band("MUHTASARI");
        pdf.Row("Mistari yote", rows.Count.ToString(), 10f);
        pdf.Row("Imethibitishwa", rows.Count(x => x.Status == PaymentReceiptStatus.Confirmed).ToString(), 10f);
        pdf.Row("Inapendekezwa", rows.Count(x => x.Status == PaymentReceiptStatus.Suggested).ToString(), 10f);
        pdf.Row("Haijatambuliwa", rows.Count(x => x.Status == PaymentReceiptStatus.Unmatched).ToString(), 10f);
        pdf.Row("Jumla ya fedha zilizothibitishwa",
            Money(rows.Where(x => x.Status == PaymentReceiptStatus.Confirmed).Sum(x => x.Amount)), 11f, bold: true);
        pdf.Row("Jumla isiyotambuliwa",
            Money(rows.Where(x => x.Status == PaymentReceiptStatus.Unmatched).Sum(x => x.Amount)), 11f, bold: true);

        pdf.Gap(8f);
        pdf.Band("MISTARI");
        pdf.Row("TAREHE / KUMBUKUMBU", "KIASI", 8f, bold: true, tone: 1);
        pdf.Rule(0.4f, 1);

        foreach (var r in rows)
        {
            pdf.EnsureSpace(26f);
            var mid = r.ConfirmedGroupMemberId ?? r.SuggestedGroupMemberId;
            members.TryGetValue(mid ?? Guid.Empty, out var m);
            var head = $"{r.OccurredAt:dd/MM/yy}  {r.Reference ?? "-"}";
            pdf.Row(head, Money(r.Amount), 9f);
            var who = m is null ? "haijatambuliwa" : $"{m.MemberNumber} {m.Name}";
            pdf.Line($"{r.Status} - {who}" + (string.IsNullOrWhiteSpace(r.PaymentCodeSeen) ? "" : $" - {r.PaymentCodeSeen}"),
                8f, tone: 1, indent: 8f, leading: 11f);
            if (rows.Count <= 120) pdf.Rule(0.3f, 1);
            else pdf.Gap(2f);
        }

        if (rows.Count == 0)
            pdf.Line("Hakuna mistari kwenye kipindi hiki.", 10f, tone: 1);

        pdf.Gap(14f);
        pdf.EnsureSpace(60f);
        pdf.Rule();
        pdf.Line("SAHIHI ZA VIONGOZI", 9f, bold: true, tone: 1);
        pdf.Gap(16f);
        pdf.Line("Mtunza-hazina: ______________________________        Mwenyekiti: ______________________________", 9f, tone: 1);
        pdf.Gap(6f);
        pdf.Line("Karatasi hii inatoka kwenye kumbukumbu za mfumo; si hesabu ya mtu.", 8f, tone: 1);

        var name = $"Ripoti-Ya-Malipo-{DateTime.UtcNow:yyyyMMdd}.pdf";
        return (pdf.Build(), name);
    }
}
