using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Events;

/// <summary>
/// SASISHO19 — "HATARINI KUONDOLEWA" (kanuni za J3 TOGETHER FOREVER, na kanuni za kikundi chochote cha tukio).
///
/// Mzunguko kamili wa deni la tukio:
///
///   1. Tukio linapoidhinishwa   → wajibu (EventObligation) wenye DueDate = tukio + EventTopUpWindowDays
///   2. DueDate inapita          → faini (OverdueEventFineAmount) inaongezwa kwenye RequiredAmount   [EventWalletService]
///   3. Faini + EventFineGraceDays zinapita deni likibaki
///                               → mwanachama anaingia kwenye orodha ya "HATARINI KUONDOLEWA"
///   4. Kiongozi anaomba kuondolewa → kura za viongozi RemovalApprovalsRequired (kanuni: ZAIDI ya wawili)
///   5. Akirudi                   → ReinstatementFineMultiplier x faini (+ ada ya kujiunga kama sera inasema)
///
/// Orodha ya hatarini inaonekana kwa WANACHAMA WOTE — si siri ya viongozi. Ni haki ya kila
/// mwanachama kujua nani hajalipa, kwa sababu pesa ya kikundi ni ya wote.
/// </summary>
public class AtRiskService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly TruthLedgerService _truth;
    private readonly EventWalletService _wallet;
    private readonly MemberStatusService _status;
    private readonly NotificationService _notify;

    public AtRiskService(ApplicationDbContext context, TruthLedgerService truth, EventWalletService wallet,
        MemberStatusService status, NotificationService notify)
    {
        _context = context; _truth = truth; _wallet = wallet; _status = status; _notify = notify;
    }

    // ------------------------------------------------------------------
    // 1. ORODHA — inaonekana kwa wanachama WOTE wa kikundi
    // ------------------------------------------------------------------

    public sealed record AtRiskRow(
        Guid MemberId, string? MemberNumber, string? Name,
        decimal Outstanding, decimal FineCharged, decimal FloorShortfall, decimal Balance, decimal Floor,
        DateTime? OldestDueDate, int DaysOverdue, int PendingEvents)
    {
        /// <summary>
        /// SASISHO21 — kiasi kamili cha kurejesha: deni lililobaki + pengo la akiba.
        /// Hii ndiyo namba mwanachama anaitaka ("nirejeshe ngapi ili nirudi sawa?").
        /// </summary>
        public decimal RestoreAmount => Outstanding + FloorShortfall;
    }

    /// <summary>Mwanachama mmoja kwenye orodha ya hatarini au la (kinashughulikia grace ya faini).</summary>
    private async Task<List<AtRiskRow>> BuildRowsAsync(Guid groupId, CancellationToken ct)
    {
        var isEvent = await _context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.Type == GroupType.EventBased).FirstOrDefaultAsync(ct);
        if (!isEvent) return new();

        var s = await _truth.GetSettingsAsync(groupId, ct);
        var graceDays = Math.Max(0, s.EventFineGraceDays);
        var floor = s.MemberMinimumBalance;
        var now = DateTime.UtcNow;

        // Deni lolote lililobaki ambalo faini yake imeshapita neema.
        var open = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.Status == EventObligationStatus.Pending && o.DueDate != null)
            .Select(o => new { o.GroupMemberId, o.RequiredAmount, o.DeductedAmount, o.DueDate })
            .ToListAsync(ct);

        var late = open
            .Where(o => o.DueDate!.Value.AddDays(graceDays) < now && o.RequiredAmount - o.DeductedAmount > 0)
            .ToList();
        if (late.Count == 0) return new();

        var ids = late.Select(o => o.GroupMemberId).Distinct().ToList();
        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && ids.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber, m.Status, Name = m.User != null ? m.User.FullName : null })
            .ToListAsync(ct);

        // SASISHO21 — salio la wote kwa query MOJA (kabla: queries 2 kwa kila mwanachama).
        var balances = await _wallet.BalancesAsync(groupId, ct);

        var rows = new List<AtRiskRow>();
        foreach (var g in late.GroupBy(o => o.GroupMemberId))
        {
            var m = members.FirstOrDefault(x => x.Id == g.Key);
            if (m is null) continue;
            // Aliyeondolewa tayari au hana uanachama hai haingii kwenye orodha ya "hatarini".
            if (!MemberAccess.ActiveStatuses.Contains(m.Status)) continue;

            balances.TryGetValue(g.Key, out var balance);
            var outstanding = g.Sum(o => o.RequiredAmount - o.DeductedAmount);
            var oldest = g.Min(o => o.DueDate);
            var daysOverdue = (int)Math.Floor((now - oldest!.Value).TotalDays);

            rows.Add(new AtRiskRow(
                m.Id, m.MemberNumber, m.Name,
                outstanding,
                FineCharged: s.OverdueEventFineAmount,   // kiasi cha faini ya sera (inaonekana kwa uwazi)
                FloorShortfall: Math.Max(0m, floor - balance),
                balance, floor, oldest, daysOverdue, g.Count()));
        }

        return rows.OrderByDescending(r => r.Outstanding).ThenByDescending(r => r.DaysOverdue).ToList();
    }

    /// <summary>Orodha ya hatarini — inaruhusiwa kwa mwanachama YEYOTE hai wa kikundi.</summary>
    public async Task<List<AtRiskRow>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);
        return await BuildRowsAsync(groupId, ct);
    }

    // ------------------------------------------------------------------
    // 2. KUONDOLEWA — kiongozi anaomba, viongozi ZAIDI ya wawili wanathibitisha
    // ------------------------------------------------------------------

    /// <summary>Idadi ya viongozi wanaohitajika: sera ya kikundi, lakini KAMWE chini ya 3.</summary>
    public const int MinimumRemovalApprovals = 3;

    /// <summary>Kikundi cha tukio pekee; mwanachama lazima awe kwenye orodha ya hatarini kweli.</summary>
    public async Task<MemberStatusChangeRequest> ProposeRemovalAsync(Guid groupId, Guid memberId, string reason, Guid userId, CancellationToken ct = default)
    {
        var rows = await BuildRowsAsync(groupId, ct);
        if (!rows.Any(r => r.MemberId == memberId))
            throw new ValidationException("Mwanachama huyu hayuko kwenye orodha ya hatarini kuondolewa (hana deni lililochelewa).");

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Eleza sababu ya kuondolewa (mf. deni la tukio halijalipwa baada ya faini na muda wa neema).");

        var settings = await _truth.GetSettingsAsync(groupId, ct);
        var gov = await _context.BenefitGovernances.AsNoTracking().FirstOrDefaultAsync(g => g.GroupId == groupId, ct);

        // "Viongozi ZAIDI ya wawili" — kanuni hii haipungui kwa sera ya mafao.
        var required = Math.Max(MinimumRemovalApprovals, Math.Max(settings.RemovalApprovalsRequired, gov?.StatusChangeApprovals ?? 0));

        return await _status.RequestAsync(groupId, new MemberStatusService.RequestDto
        {
            MemberId = memberId,
            // Kuondolewa kwa deni = kusimamishwa: kumbukumbu na salio zinabaki, anaweza kurudi kwa masharti.
            ToStatus = MemberStatus.Suspended,
            IsExpulsion = false,          // si kufukuzwa kwa nidhamu (hilo linahitaji kura ya wanachama wote)
            EffectiveDate = TruthLedgerService.NowEat().Date,
            Reason = reason.Trim(),
            EvidenceNote = "Kanuni ya kikundi: deni la tukio halijalipwa baada ya faini na muda wa neema."
        }, userId, ct, minApprovals: required);
    }

    // ------------------------------------------------------------------
    // 3. KURUDI — ada ya kurudi (2x faini + ada ya kujiunga)
    // ------------------------------------------------------------------

    public sealed record ReinstatementCost(
        decimal OutstandingDebt, decimal FineMultiplier, decimal FineBase, decimal ReinstatementFine,
        decimal JoiningFee, bool JoiningFeeRequired, decimal Total);

    /// <summary>Kiasi anachotakiwa kulipa mwanachama aliyeondolewa kabla ya kurejeshwa.</summary>
    public async Task<ReinstatementCost> ReinstatementCostAsync(Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        var s = await _truth.GetSettingsAsync(groupId, ct);
        var debt = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.GroupMemberId == memberId && o.Status == EventObligationStatus.Pending)
            .SumAsync(o => o.RequiredAmount - o.DeductedAmount, ct);

        var multiplier = s.ReinstatementFineMultiplier;
        var fineBase = s.OverdueEventFineAmount;
        var reinstatementFine = Math.Round(Math.Max(0m, multiplier) * Math.Max(0m, fineBase), 0, MidpointRounding.AwayFromZero);
        var joinFee = s.ReinstatementRequiresJoiningFee ? s.JoiningFee : 0m;
        var total = debt + reinstatementFine + joinFee;

        return new ReinstatementCost(debt, multiplier, fineBase, reinstatementFine, joinFee,
            s.ReinstatementRequiresJoiningFee, total);
    }

    /// <summary>
    /// Kinga ya kurejeshwa: mwanachama aliyeondolewa HARUDISHWI kama ada ya kurudi na deni
    /// havijalipwa. Salio la pochi linapima — amana mpya inaongezwa kwanza (M-Koba).
    /// </summary>
    public async Task RequireReinstatementPaidAsync(Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        var cost = await ReinstatementCostAsync(groupId, memberId, ct);
        if (cost.Total <= 0) return;

        // Kiasi chote kinapimwa kwenye pochi (akiba) — ndiyo pesa ambayo kikundi kimeikubali
        // kwa mwanachama. Kumbuka: ada ya kujiunga (kama bado haijalipwa) inakatwa KWANZA kwenye
        // amana mpya, kwa hiyo mwanachama anayelipa ada kwa mara ya kwanza anaweka kiasi zaidi.
        var balance = await _wallet.BalanceAsync(groupId, memberId, ct);
        if (balance >= cost.Total) return;

        throw new ValidationException(
            $"Mwanachama huyu ana deni la kurudi {cost.Total:N0} " +
            $"(deni la tukio {cost.OutstandingDebt:N0} + faini ya kurudi {cost.ReinstatementFine:N0}" +
            (cost.JoiningFeeRequired ? $" + ada ya kujiunga {cost.JoiningFee:N0}" : "") +
            $"). Salio la pochi ni {balance:N0} — linahitajika liwe {cost.Total:N0} kabla ya kurejeshwa. " +
            "Amana mpya ya M-Koba ya kiasi kinachobaki inahitajika; ada ya kujiunga isiyolipwa inakatwa kwanza.");
    }

    // ------------------------------------------------------------------
    // 4. ARIFA — kumjulisha mwanachama na viongozi
    // ------------------------------------------------------------------

    /// <summary>Huduma ya kila siku: arifa kwa vikundi vyote vya "kila tukio".</summary>
    public async Task<int> NotifyAllAtRiskAsync(CancellationToken ct = default)
    {
        var groups = await _context.Groups.AsNoTracking()
            .Where(g => g.Type == GroupType.EventBased).Select(g => g.Id).ToListAsync(ct);
        var total = 0;
        foreach (var g in groups) total += await NotifyAtRiskAsync(g, ct);
        return total;
    }

    /// <summary>Inarudisha idadi ya wanachama walioarifiwa.</summary>
    public async Task<int> NotifyAtRiskAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await BuildRowsAsync(groupId, ct);
        if (rows.Count == 0) return 0;

        var settings = await _truth.GetSettingsAsync(groupId, ct);
        var graceDays = Math.Max(0, settings.EventFineGraceDays);

        foreach (var r in rows)
        {
            await _notify.NotifyAsync(groupId, new[] { r.MemberId }, "MemberOverdue",
                "Uko hatarini kuondolewa",
                $"Deni lako la tukio {r.Outstanding:N0} halijalipwa. Faini imekwishatolewa na muda wa neema " +
                $"({graceDays} siku) umepita. Lipa kabla viongozi hawajaanza mchakato wa kukuondoa.",
                "/pochi", ct);
        }

        // Viongozi wote wanaona idadi — si orodha ya siri; orodha yenyewe iko kwenye ukurasa kwa wote.
        var leaders = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && MemberAccess.ActiveStatuses.Contains(m.Status))
            .ToListAsync(ct);
        var leaderIds = leaders.Where(m => MemberAccess.IsEffectiveLeader(m)).Select(m => m.Id).ToList();
        if (leaderIds.Count > 0)
            await _notify.NotifyAsync(groupId, leaderIds, "MemberOverdue",
                "Wanachama hatarini kuondolewa",
                $"Wanachama {rows.Count} wana deni la tukio lililochelewa zaidi ya muda wa neema. Fungua Pochi -> \"Hatarini kuondolewa\".",
                "/pochi", ct);

        await _context.SaveChangesAsync(ct);
        return rows.Count;
    }
}
