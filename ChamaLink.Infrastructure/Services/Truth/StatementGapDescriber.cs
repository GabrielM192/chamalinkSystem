using System.Globalization;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// Maelezo kamili ya pengo la mnyororo wa balance wa PDF ya M-Koba — kwa KILA kikundi.
///
/// Pengo = balance ya muamala uliotangulia + kiasi cha muamala unaofuata ≠ balance ya muamala unaofuata.
/// Maana yake: pesa iliingia (au ilitoka) KATI ya miamala hiyo miwili, lakini mstari wake haupo kwenye PDF.
/// Miamala ya pande zote mbili yenyewe ni sahihi na imeshahesabiwa kwa wenyewe — ni MIPAKA ya pengo tu.
///
/// Chanzo kimoja cha maelezo haya: ripoti ya import, GET /gaps, na bango la muhtasari — ili ujumbe ulingane kila mahali.
/// Wanaowezekana (PossibleMemberIds) ni wenye miamala ya mipakani — ni PENDEKEZO la kuchagua haraka tu;
/// hakuna kinachohesabiwa kwa mtu bila ushahidi + idhini ya viongozi wawili (maker-checker).
/// </summary>
public static class StatementGapDescriber
{
    public sealed record GapNeighbor(
        string Reference, DateTime Timestamp, string PayerName, string Phone, decimal Amount, bool IsWithdrawal,
        decimal? Balance, Guid? MemberId, string? MemberName);

    public sealed record GapDescription(
        Guid Id, string? AfterReference, string BeforeReference, DateTime WindowStart, DateTime WindowEnd, decimal Amount,
        GapNeighbor? Previous, GapNeighbor? Next,
        decimal? BalanceAfterPrevious, decimal? ExpectedBalanceBeforeNext,
        string Title, string Message, IReadOnlyList<Guid> PossibleMemberIds);

    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    public static async Task<List<GapDescription>> DescribeAsync(
        ApplicationDbContext context, Guid groupId, IReadOnlyCollection<MkobaStatementGap> gaps, CancellationToken ct = default)
    {
        if (gaps.Count == 0) return new List<GapDescription>();

        var refs = gaps.SelectMany(g => new[] { g.AfterReference, g.BeforeReference })
            .Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).Distinct().ToList();
        var rows = await context.MkobaRawTransactions.AsNoTracking()
            .Where(t => t.GroupId == groupId && refs.Contains(t.Reference))
            .ToListAsync(ct);
        var byRef = rows.GroupBy(r => r.Reference).ToDictionary(g => g.Key, g => g.First());

        var phones = rows.Select(r => r.Phone).Distinct().ToList();
        var phoneMembers = await context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && phones.Contains(p.Phone) && p.Status == TruthApprovalStatus.Approved)
            .Join(context.GroupMembers, p => p.GroupMemberId, m => m.Id,
                (p, m) => new { p.Phone, m.Id, Name = m.User != null ? m.User.FullName : m.MemberNumber })
            .ToListAsync(ct);
        var memberByPhone = phoneMembers.GroupBy(x => x.Phone).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Name));

        GapNeighbor? Neighbor(string? reference)
        {
            if (reference is null || !byRef.TryGetValue(reference, out var t)) return null;
            var has = memberByPhone.TryGetValue(t.Phone, out var mm);
            return new GapNeighbor(t.Reference, t.Timestamp, t.PayerName, t.Phone, t.Amount, t.IsWithdrawal, t.Balance,
                has ? mm.Id : null, has ? mm.Name : null);
        }

        return gaps.OrderBy(g => g.WindowStart).Select(g =>
        {
            var prev = Neighbor(g.AfterReference);
            var next = Neighbor(g.BeforeReference);
            var expected = next?.Balance is { } nb ? nb - (next.IsWithdrawal ? -next.Amount : next.Amount) : (decimal?)null;
            var possible = new[] { next?.MemberId, prev?.MemberId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
            var (title, message) = Compose(g, prev, next, expected);
            return new GapDescription(g.Id, g.AfterReference, g.BeforeReference, g.WindowStart, g.WindowEnd, g.Amount,
                prev, next, prev?.Balance, expected, title, message, possible);
        }).ToList();
    }

    private static string Who(GapNeighbor n) => n.MemberName ?? (string.IsNullOrWhiteSpace(n.PayerName) ? n.Phone : n.PayerName);
    private static string When(DateTime t) => t.ToString("yyyy-MM-dd HH:mm", Fmt);
    private static string Money(decimal v) => v.ToString("N0", Fmt);

    private static (string Title, string Message) Compose(MkobaStatementGap g, GapNeighbor? prev, GapNeighbor? next, decimal? expected)
    {
        var amount = Math.Abs(g.Amount);
        var title = g.Amount > 0
            ? $"Balance imeruka: {Money(amount)} iliingia bila mstari kwenye PDF"
            : $"Balance imeshuka: {Money(amount)} ilitoka bila mstari kwenye PDF";

        if (prev is null || next is null || prev.Balance is null || expected is null)
            return (title, g.Amount > 0
                ? $"Pesa {Money(amount)} iliingia kati ya {g.AfterReference ?? "(mwanzo)"} na {g.BeforeReference}, lakini mstari wake haupo kwenye PDF."
                : $"Pesa {Money(amount)} ilitoka kati ya {g.AfterReference ?? "(mwanzo)"} na {g.BeforeReference}, lakini mstari wake haupo kwenye PDF.");

        var direction = g.Amount > 0 ? "iliingia" : "ilitoka";
        var message =
            $"Baada ya muamala {prev.Reference} ({Who(prev)}, {When(prev.Timestamp)}) balance ilikuwa {Money(prev.Balance.Value)}. " +
            $"Muamala unaofuata {next.Reference} ({Who(next)}, {When(next.Timestamp)}) wa {Money(next.Amount)} unaonyesha balance ilikuwa " +
            $"{Money(expected.Value)} kabla yake. Kwa hiyo {Money(amount)} {direction} kati ya {When(prev.Timestamp)} na {When(next.Timestamp)} " +
            $"lakini mstari wake HAUPO kwenye PDF. Miamala ya {Who(prev)} na {Who(next)} yenyewe ni sahihi na imeshahesabiwa — " +
            (g.Amount > 0
                ? "tafuta mwenye pesa hii kwa ushahidi (SMS ya M-Pesa/M-Koba) kisha mtambue."
                : "waulize viongozi/M-Koba pesa hii ilitolewa kwa nini.");
        return (title, message);
    }
}
