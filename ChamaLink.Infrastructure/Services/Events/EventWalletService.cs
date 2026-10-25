using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Events;

/// <summary>
/// SASISHO18 — INJINI YA POCHI KWA VIKUNDI VYA "KILA TUKIO".
///
/// Kanuni kuu (moja tu):
///
///     Salio la pochi = (pesa zote zilizokubaliwa kwa mwanachama)
///                    − (michango ya matukio iliyokwisha kukatwa)
///
/// Kila tukio (BenefitCase) linapoidhinishwa, kila mwanachama anayestahili
/// anapata <see cref="EventObligation"/> yenye kiasi kinachotakiwa. Kiasi hicho
/// kinakatwa kwenye pochi mara moja — LAKINI si chini ya floor
/// (Event.MemberMinimumBalance). Kinachobaki kinakuwa deni linalokamilishwa
/// (sweep) kila amana mpya inapoingia, kuanzia deni la zamani.
///
/// Kwa nini hii ni salama:
///   • UNIQUE(BenefitCaseId, GroupMemberId) — tukio moja haliwezi kumkata
///     mwanachama mara mbili hata idhini ikirudiwa.
///   • DeductedAmount ndiyo rekodi MOJA ya kilichokatwa; hakuna kuandika
///     kwenye LedgerEntry wala kwenye kitabu kingine.
///   • Kila mwendo unaandikwa kwenye mnyororo wa ukaguzi (AuditEvents).
/// </summary>
public class EventWalletService
{
    private readonly ApplicationDbContext _context;
    private readonly TruthLedgerService _truth;

        private readonly ChamaLink.Infrastructure.Services.Sms.SmsService? _sms;

    public EventWalletService(ApplicationDbContext context, TruthLedgerService truth,
        // SASISHO21 (Sehemu ya 2): kila tukio linatangazwa kwa SMS — hiari ili
        // majaribio ya zamani (yanayounda huduma hii pekee) yasiathirike.
        ChamaLink.Infrastructure.Services.Sms.SmsService? sms = null)
    {
        _context = context; _truth = truth; _sms = sms;
    }

    public sealed record WalletSnapshot(
        Guid GroupId,
        Guid GroupMemberId,
        decimal Balance,
        decimal Floor,
        decimal Available,
        decimal Outstanding,
        IReadOnlyList<EventObligation> OpenObligations,
        // Historia: makato YOTE ya mwanachama (Pending, Settled, Waived, Exempted).
        // Ukurasa wa /pochi unaonyesha historia hii — si deni lililobaki tu.
        IReadOnlyList<EventObligation> History);

    // ------------------------------------------------------------------
    // 1. KUSOMA
    // ------------------------------------------------------------------

    /// <summary>Salio la pochi la mwanachama (pesa iliyokubaliwa − makato ya matukio).</summary>
    public async Task<decimal> BalanceAsync(Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        var credited = await CreditedAsync(memberId, ct);
        var deducted = await _context.EventObligations
            .Where(o => o.GroupId == groupId && o.GroupMemberId == memberId)
            .SumAsync(o => o.DeductedAmount, ct);
        return Math.Max(0m, credited - deducted);
    }

    /// <summary>
    /// SASISHO21 — salio la wanachama WENGI kwa query MOJA.
    ///
    /// Kabla ya toleo hili, orodha ya walio chelewa ilimwita BalanceAsync kwa kila
    /// mwanachama (queries 2 kwa mtu). Kwa wanachama 1,200 hiyo ilikuwa queries 2,400.
    /// Hapa tunahesabu salio la kikundi kizima mara moja, kisha kutoa makato
    /// ya matukio kwa GROUP BY mmoja.
    /// </summary>
    public async Task<Dictionary<Guid, decimal>> BalancesAsync(Guid groupId, CancellationToken ct = default)
    {
        var credited = new Dictionary<Guid, decimal>();
        try
        {
            var truth = await _truth.GetGroupTruthAsync(groupId, null, ct);
            foreach (var t in truth) credited[t.MemberId] = t.Statement.Savings;
        }
        catch (Exception)
        {
            // Kikundi kisichokuwa na mipangilio ya Ukweli bado kinaonyesha orodha —
            // salio 0 badala ya 409. (Sawa na tabia ya MembersAsync.)
            credited.Clear();
        }

        var deducted = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId)
            .GroupBy(o => o.GroupMemberId)
            .Select(g => new { MemberId = g.Key, Total = g.Sum(x => x.DeductedAmount) })
            .ToDictionaryAsync(x => x.MemberId, x => x.Total, ct);

        var result = new Dictionary<Guid, decimal>(credited.Count);
        foreach (var (memberId, amount) in credited)
        {
            deducted.TryGetValue(memberId, out var minus);
            result[memberId] = Math.Max(0m, amount - minus);
        }
        foreach (var (memberId, minus) in deducted)
            if (!result.ContainsKey(memberId)) result[memberId] = 0m;

        return result;
    }

    /// <summary>Pesa zote zilizokubaliwa kwa mwanachama (kutoka Injini ya Ukweli — M-Koba + makubaliano).</summary>
    private async Task<decimal> CreditedAsync(Guid memberId, CancellationToken ct)
    {
        var t = await _truth.GetMemberTruthAsync(memberId, null, ct);
        // Kwa kikundi cha "kila tukio" hakuna michango ya mwezi, faini, Kianzio
        // wala mikopo — kwa hiyo Savings HUU ni pesa yake yote iliyokubaliwa.
        return t.Statement.Savings;
    }

    /// <summary>Taarifa kamili ya pochi: salio, floor, kinachopatikana, deni lililobaki, na historia.</summary>
    public async Task<WalletSnapshot> GetWalletAsync(Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        var floor = await FloorAsync(groupId, ct);
        var balance = await BalanceAsync(groupId, memberId, ct);
        // Historia: makato yote (pia yaliyokwisha kamilika au kusamehewa), mapya kwanza.
        var history = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.GroupMemberId == memberId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(60)
            .ToListAsync(ct);
        var open = history
            .Where(o => o.Status == EventObligationStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .ToList();
        var outstanding = open.Sum(o => o.OutstandingAmount);
        return new WalletSnapshot(groupId, memberId, balance, floor,
            Math.Max(0m, balance - floor), outstanding, open, history);
    }

    private async Task<decimal> FloorAsync(Guid groupId, CancellationToken ct)
    {
        var s = await _truth.GetSettingsAsync(groupId, ct);
        return s.MemberMinimumBalance;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Orodha ya wanachama waliohesabiwa kwenye tukio. Kesi mpya huhifadhi
    /// CountedMemberIdsJson wakati wa snapshot; kama haipo (kesi ya zamani),
    /// tunarudi kwa wanachama hai wa kikundi.
    /// </summary>
    private async Task<List<Guid>> CountedMemberIdsAsync(Guid groupId, BenefitCase c, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(c.CountedMemberIdsJson))
        {
            try
            {
                var ids = JsonSerializer.Deserialize<List<Guid>>(c.CountedMemberIdsJson!, Json);
                if (ids is { Count: > 0 }) return ids;
            }
            catch (JsonException) { /* data mbovu -> rudi kwa wanachama hai */ }
        }

        return await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && NotificationService.InGroupStatuses.Contains(m.Status))
            .Select(m => m.Id)
            .ToListAsync(ct);
    }

    // ------------------------------------------------------------------
    // 2. KUANDIKA — kutengeneza wajibu tukio linapoidhinishwa
    // ------------------------------------------------------------------

    /// <summary>
    /// Inatengeneza wajibu wa kila mwanachama kwa tukio lililoidhinishwa na kukata
    /// kwenye pochi hadi floor. Inarudisha idadi ya wajibu wapya (idempotent:
    /// kuitisha mara ya pili kunarudisha 0).
    /// </summary>
    public async Task<int> PostObligationsAsync(Guid groupId, BenefitCase benefitCase, Guid actorUserId, CancellationToken ct = default)
    {
        var group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new NotFoundException("Kikundi hakijapatikana.");

        // Engine hii ni ya vikundi vya "kila tukio". Hybrid itaongezwa baadaye.
        if (group.Type != GroupType.EventBased) return 0;

        var settings = await _truth.GetSettingsAsync(groupId, ct);

        var floor = settings.MemberMinimumBalance;
        var windowDays = settings.EventTopUpWindowDays;
        var capPerMonth = settings.MaxEventDeductionPerMemberPerMonth;
        var beneficiaryContributes = settings.BeneficiaryContributes;
        var required = benefitCase.PerMemberAmount;

        if (required <= 0) return 0;

        var memberIds = await CountedMemberIdsAsync(groupId, benefitCase, ct);
        if (beneficiaryContributes == false)
            memberIds = memberIds.Where(id => id != benefitCase.BeneficiaryGroupMemberId).ToList();

        // Wajibu uliopo tayari (kukataa mara mbili)
        var existing = await _context.EventObligations.AsNoTracking()
            .Where(o => o.BenefitCaseId == benefitCase.Id)
            .Select(o => o.GroupMemberId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var now = DateTime.UtcNow;
        var windowStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Makato ya mwezi huu kwa kila mwanachama (kwa kikomo cha mwezi)
        Dictionary<Guid, decimal> deductedThisMonth = capPerMonth > 0
            ? await _context.EventObligations
                .Where(o => o.GroupId == groupId && o.CreatedAt >= windowStart)
                .GroupBy(o => o.GroupMemberId)
                .Select(g => new { MemberId = g.Key, Total = g.Sum(x => x.DeductedAmount) })
                .ToDictionaryAsync(x => x.MemberId, x => x.Total, ct)
            : new Dictionary<Guid, decimal>();

        // SASISHO21 — namba za wanachama kwa kodi ya malipo (query MOJA, si kwa kila mtu).
        var memberNumbers = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && memberIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber })
            .ToDictionaryAsync(x => x.Id, x => x.MemberNumber, ct);

        var shortCode = await _context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.ShortCode).FirstOrDefaultAsync(ct);

        // SASISHO21 (utendaji) — salio la wanachama WOTE kwa query MOJA kabla ya kitanzi.
        // Kabla ya toleo hili, kila mwanachama alisababisha kujengwa upya kwa Ukweli
        // (queries 2+ kwa mtu). Kwa tukio la wanachama 1,200 hiyo ilikuwa maelfu ya queries
        // kwa tukio moja — ndiyo sababu vikao vilikuwa 25–35 saa.
        // Makato yanayotokea ndani ya kitanzi hiki yanatunzwa hapa chini ili salio
        // liongezeke kwa usahihi pasipo kuuliza database tena.
        var balances = await BalancesAsync(groupId, ct);

        var created = 0;

        foreach (var memberId in memberIds.Distinct())
        {
            if (existingSet.Contains(memberId)) continue;

            balances.TryGetValue(memberId, out var balance);
            var usable = Math.Max(0m, balance - floor);

            if (capPerMonth > 0)
            {
                deductedThisMonth.TryGetValue(memberId, out var already);
                usable = Math.Min(usable, Math.Max(0m, capPerMonth - already));
            }

            var deduct = Math.Min(required, usable);

            // Salio linapungua kwa kile kilichokatwa sasa (hakuna kuuliza database tena).
            if (deduct > 0) balances[memberId] = Math.Max(0m, balance - deduct);

            var obligation = new EventObligation
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                BenefitCaseId = benefitCase.Id,
                GroupMemberId = memberId,
                RequiredAmount = required,
                DeductedAmount = deduct,
                Status = deduct >= required ? EventObligationStatus.Settled : EventObligationStatus.Pending,
                DueDate = windowDays > 0 ? now.AddDays(windowDays) : null,
                CreatedAt = now,
                SettledAt = deduct >= required ? now : null,
                PaymentCode = PaymentCode.Build(shortCode, benefitCase.CaseNumber,
                    memberNumbers.TryGetValue(memberId, out var mn) ? mn : null)
            };
            _context.EventObligations.Add(obligation);
            created++;

            if (deduct > 0)
                deductedThisMonth[memberId] = (deductedThisMonth.TryGetValue(memberId, out var a) ? a : 0m) + deduct;

            // Mnyororo wa ukaguzi — kumbukumbu ni sehemu ya SaveChanges ile ile.
            _context.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(), GroupId = groupId, MemberId = memberId,
                ActorId = actorUserId,
                ActorRole = await ActorRoleAsync(groupId, actorUserId, ct),
                Action = AuditAction.WelfareObligationCreated,
                EntityType = nameof(EventObligation), EntityId = obligation.Id,
                AfterJson = JsonSerializer.Serialize(new
                {
                    benefitCase.CaseNumber, Required = required, Deducted = deduct,
                    Outstanding = required - deduct, obligation.DueDate, Status = obligation.Status.ToString()
                }),
                Source = EventSource.Manual, SourceReference = benefitCase.CaseNumber,
                Reason = "Mchango wa tukio (kikundi cha kila tukio)",
                Timestamp = now, EffectiveDate = now,
                CorrelationId = benefitCase.Id, IsSystemGenerated = true
            });
        }

        // Je, kikundi kimevuka kikomo cha matukio kwa mwezi?
        // (Kikomo kinakaguliwa kabla ya idhini; hapa tunakumbuka tu kama kimevukwa.)
        if (settings.MaxEventsPerMonth > 0)
        {
            var eventsThisMonth = await _context.BenefitCases.CountAsync(
                c => c.GroupId == groupId && c.ApprovedAt != null && c.ApprovedAt >= windowStart, ct);
            if (eventsThisMonth > settings.MaxEventsPerMonth)
            {
                _context.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid(), GroupId = groupId, MemberId = null,
                    ActorId = actorUserId,
                    Action = AuditAction.Updated,
                    EntityType = "MaxEventsPerMonth",
                    EntityId = benefitCase.Id,
                    AfterJson = JsonSerializer.Serialize(new { eventsThisMonth, settings.MaxEventsPerMonth }),
                    Source = EventSource.Manual, SourceReference = benefitCase.CaseNumber,
                    Reason = "Kikundi kimevuka kikomo cha matukio kwa mwezi",
                    Timestamp = now, EffectiveDate = now,
                    CorrelationId = benefitCase.Id, IsSystemGenerated = true
                });
            }
        }

        await _context.SaveChangesAsync(ct);

        // SASISHO21 (Sehemu ya 2): tangazo la tukio kwa SMS — kila mwanachama
        // anapata KIASI CHAKE na KODI YAKE pekee (hakuna kiasi cha mwenzake).
        if (created > 0 && _sms is not null)
        {
            var groupName = group.Name;
            var posted = await _context.EventObligations.AsNoTracking()
                .Where(o => o.BenefitCaseId == benefitCase.Id)
                .Select(o => new { o.GroupMemberId, o.PaymentCode })
                .ToListAsync(ct);
            await _sms.QueueEventAnnouncementAsync(groupId, benefitCase, groupName,
                posted.Select(x => (x.GroupMemberId, required, x.PaymentCode)).ToList(), ct);
        }

        return created;
    }

    private async Task<string> ActorRoleAsync(Guid groupId, Guid userId, CancellationToken ct)
        => await _context.GroupMembers.AsNoTracking()
               .Where(m => m.GroupId == groupId && m.UserId == userId)
               .Select(m => m.Role.ToString())
               .FirstOrDefaultAsync(ct) ?? "HajulikaniKwenyeKikundi";

    // ------------------------------------------------------------------
    // 3. SWEEP — amana mpya inalipa deni la zamani kwanza
    // ------------------------------------------------------------------

    /// <summary>
    /// Inatumia nafasi iliyopo kwenye pochi kulipa madeni ya matukio yaliyobaki,
    /// kuanzia la zamani (OldestDebtFirst). Inarudisha kiasi kilicholipwa.
    /// </summary>
    public async Task<decimal> SweepAsync(Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        var floor = await FloorAsync(groupId, ct);
        var balance = await BalanceAsync(groupId, memberId, ct);
        var available = Math.Max(0m, balance - floor);
        if (available <= 0) return 0m;

        var open = await _context.EventObligations
            .Where(o => o.GroupId == groupId && o.GroupMemberId == memberId && o.Status == EventObligationStatus.Pending)
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .ToListAsync(ct);
        if (open.Count == 0) return 0m;

        var applied = 0m;
        var now = DateTime.UtcNow;

        foreach (var o in open)
        {
            if (available <= 0) break;
            var outstanding = o.OutstandingAmount;
            if (outstanding <= 0) continue;

            var pay = Math.Min(outstanding, available);
            o.DeductedAmount += pay;
            available -= pay;
            applied += pay;

            if (o.DeductedAmount >= o.RequiredAmount)
            {
                o.Status = EventObligationStatus.Settled;
                o.SettledAt = now;
            }

            _context.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(), GroupId = groupId, MemberId = memberId,
                ActorId = Guid.Empty,
                Action = AuditAction.WelfareContributionPaid,
                EntityType = nameof(EventObligation), EntityId = o.Id,
                AfterJson = JsonSerializer.Serialize(new { Paid = pay, o.DeductedAmount, o.RequiredAmount, Status = o.Status.ToString() }),
                Source = EventSource.Manual, SourceReference = "Sweep",
                Reason = "Amana imelipa deni la tukio",
                Timestamp = now, EffectiveDate = now,
                CorrelationId = o.BenefitCaseId, IsSystemGenerated = true
            });
        }

        await _context.SaveChangesAsync(ct);
        return applied;
    }

    /// <summary>
    /// AMANA MPYA INALIPA DENI LA ZAMANI (SASISHO18).
    ///
    /// Inaitwa mara tu PDF ya M-Koba inapoidhinishwa. Kwa kila namba iliyopokea
    /// pesa kwenye kundi hili, deni la matukio lililobaki linalipwa kwa salio
    /// lililopo JUU ya floor — deni la zamani kwanza (SweepAsync).
    /// Kikundi cha mwezi → 0 (hakuna pochi).
    /// </summary>
    public async Task<int> SweepDepositsAsync(Guid groupId, IEnumerable<string> phones, CancellationToken ct = default)
    {
        var isEvent = await _context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.Type == GroupType.EventBased)
            .FirstOrDefaultAsync(ct);
        if (!isEvent) return 0;

        var list = phones.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        if (list.Count == 0) return 0;

        var memberIds = await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Status == TruthApprovalStatus.Approved && list.Contains(p.Phone))
            .Select(p => p.GroupMemberId).Distinct().ToListAsync(ct);

        var touched = 0;
        foreach (var memberId in memberIds)
            if (await SweepAsync(groupId, memberId, ct) > 0) touched++;
        return touched;
    }

    /// <summary>
    /// FAINI YA DENI LA TUKIO LINALOCHELEWA (SASISHO18).
    ///
    /// Wajibu uliopita tarehe ya mwisho (DueDate) na ambao bado una deni:
    /// faini ya kikundi (OverdueEventFineAmount) inaongezwa kwenye kiasi
    /// kinachotakiwa cha wajibu huo — SI kwenye kitabu kingine. Hivyo deni
    /// moja linabaki, na amana ijayo inalipa deni + faini kwa mpigo mmoja.
    ///
    ///   • 0 (chaguo-msingi) = hakuna faini kabisa.
    ///   • OverdueFineIssued = true inazuia faini hiyo kuongezwa mara ya pili
    ///     kila siku huduma inapokimbia.
    ///   • DueDate null (window 0) = hakuna muda wa mwisho → hakuna faini.
    /// </summary>
    public async Task<int> ChargeOverdueFinesAsync(DateTime? asOf = null, CancellationToken ct = default)
    {
        var now = asOf ?? DateTime.UtcNow;

        var candidates = await _context.EventObligations
            .Where(o => o.Status == EventObligationStatus.Pending
                        && !o.OverdueFineIssued
                        && o.DueDate != null
                        && o.DueDate < now)
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        var charged = 0;

        foreach (var group in candidates.GroupBy(o => o.GroupId))
        {
            var isEvent = await _context.Groups.AsNoTracking()
                .Where(g => g.Id == group.Key).Select(g => g.Type == GroupType.EventBased)
                .FirstOrDefaultAsync(ct);
            if (!isEvent) continue;

            var settings = await _context.TruthGroupSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.GroupId == group.Key, ct);
            var fine = settings?.OverdueEventFineAmount ?? 0m;

            foreach (var o in group)
            {
                // Deni limekwisha lipwa kikamilifu (lakini halijafungwa) — hakuna faini.
                if (o.OutstandingAmount <= 0)
                {
                    o.OverdueFineIssued = true;
                    continue;
                }
                if (fine <= 0) continue;

                o.RequiredAmount += fine;
                o.OverdueFineIssued = true;
                charged++;

                _context.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid(), GroupId = o.GroupId, MemberId = o.GroupMemberId,
                    ActorId = Guid.Empty,
                    Action = AuditAction.Updated,
                    EntityType = nameof(EventObligation), EntityId = o.Id,
                    AfterJson = JsonSerializer.Serialize(new
                    {
                        Fine = fine, Required = o.RequiredAmount, Deducted = o.DeductedAmount,
                        Outstanding = o.OutstandingAmount, o.DueDate, Reason = "OverdueEventFine"
                    }),
                    Source = EventSource.Manual, SourceReference = "OverdueEventFine",
                    Reason = "Faini ya deni la tukio lililochelewa",
                    Timestamp = now, EffectiveDate = now,
                    CorrelationId = o.BenefitCaseId, IsSystemGenerated = true
                });
            }
        }

        // Hifadhi pia OverdueFineIssued ya wajibu uliokwisha lipwa (bila faini).
        await _context.SaveChangesAsync(ct);

        // SASISHO21 (Sehemu ya 2): taarifa ya faini kwa SMS — mara moja kwa wajibu.
        if (charged > 0 && _sms is not null)
        {
            var fined = await _context.EventObligations.AsNoTracking()
                .Where(o => o.GroupId != Guid.Empty && o.OverdueFineIssued && o.Status == EventObligationStatus.Pending
                            && o.DueDate != null && o.DueDate < now && o.RequiredAmount >= o.DeductedAmount)
                .OrderByDescending(o => o.CreatedAt).Take(200)
                .Select(o => new { o.Id, o.GroupId, o.GroupMemberId, o.RequiredAmount, o.DeductedAmount, o.PaymentCode })
                .ToListAsync(ct);
            var names = await _context.Groups.AsNoTracking()
                .Where(g => fined.Select(f => f.GroupId).Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
            foreach (var o in fined)
                await _sms.QueueFineAsync(o.GroupId, o.GroupMemberId, Math.Max(0m, o.RequiredAmount - o.DeductedAmount),
                    o.PaymentCode, names.GetValueOrDefault(o.GroupId, "Kikundi"), o.Id, ct);
        }

        return charged;
    }

    /// <summary>
    /// Sweep kwa vikundi vyote vya "kila tukio" — inaitwa na huduma ya kila siku.
    /// </summary>
    public async Task<int> SweepAllAsync(CancellationToken ct = default)
    {
        var groupIds = await _context.Groups.AsNoTracking()
            .Where(g => g.Type == GroupType.EventBased)
            .Select(g => g.Id).ToListAsync(ct);

        var touched = 0;
        foreach (var groupId in groupIds)
        {
            var memberIds = await _context.EventObligations.AsNoTracking()
                .Where(o => o.GroupId == groupId && o.Status == EventObligationStatus.Pending)
                .Select(o => o.GroupMemberId).Distinct().ToListAsync(ct);

            foreach (var memberId in memberIds)
            {
                var applied = await SweepAsync(groupId, memberId, ct);
                if (applied > 0) touched++;
            }
        }
        return touched;
    }
}
