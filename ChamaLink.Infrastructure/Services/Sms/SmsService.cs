using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services.Sms;

/// <summary>
/// SASISHO21 (Sehemu ya 2) — SMS ZA NO-REPLY.
///
/// Mwanachama wa kikundi cha 1,200 mara nyingi hana smartphone. Tovuti ni ya
/// viongozi; mwanachama anafikiwa kwa SMS. Bila SMS, deni linakua "siri" hadi
/// siku ya kuondolewa.
///
/// Kanuni za gharama na maadili (zote ni sera ya kikundi, si namba ndani ya msimbo):
///   • Salio la CREDITS, si bajeti ya mwezi: credits zikiisha, SMS zinasimama.
///   • Saa za utulivu: ujumbe unasubiri, haukataliwi.
///   • Kikomo cha SMS kwa mwanachama kwa tukio (kawaida 3).
///   • Hakuna jina la marehemu, hakuna kiasi cha mwenzake, hakuna namba za wengine.
///   • Kila ujumbe una kumbukumbu: mtoa-huduma, kumbukumbu yake, hali, gharama.
/// </summary>
public class SmsService
{
    private readonly ApplicationDbContext _context;
    private readonly ISmsGateway _gateway;
    private readonly NotificationService _notify;
    private readonly ILogger<SmsService> _log;

    /// <summary>Kikomo cha kawaida cha herufi kwa SMS moja.</summary>
    public const int DefaultCharsPerMessage = 160;

    /// <summary>Kikomo cha usalama cha ongezeko moja la credits (kuzuia kosa la kuandika sufuri nyingi).</summary>
    public const int MaxTopUpCredits = 50_000;   // kikomo cha ongezeko moja (si pesa: idadi ya SMS)

    public SmsService(ApplicationDbContext context, ISmsGateway gateway, NotificationService notify, ILogger<SmsService> log)
    {
        _context = context;
        _gateway = gateway;
        _notify = notify;
        _log = log;
    }

    // ==================================================================
    // 1. VIOLETI (Kiswahili, vifupi, bila majina ya wengine)
    // ==================================================================

    /// <summary>
    /// Violezo vya ujumbe. Namba na tarehe zinajazwa kwa wakati wa kutuma.
    /// Maneno "kikundi" — kamwe "kila kikundi".
    /// </summary>
    public static class Templates
    {
        public const string EventAnnouncement = "EventAnnouncement";
        public const string Reminder = "Reminder";
        public const string Fine = "Fine";
        public const string AtRisk = "AtRisk";
        public const string PaymentConfirmed = "PaymentConfirmed";
        public const string JoiningPayment = "JoiningPayment";

        public static string Render(string key, IReadOnlyDictionary<string, string> v)
        {
            string Get(string k) => v.TryGetValue(k, out var x) ? x : string.Empty;

            return key switch
            {
                EventAnnouncement =>
                    $"{Get("kikundi")}: Tukio jipya la kikundi. Mchango wako ni TSh {Get("kiasi")}. " +
                    $"Mwisho {Get("tarehe")}. Kodi yako {Get("kodi")}.",

                Reminder =>
                    $"{Get("kikundi")}: Siku {Get("siku")} zimebaki. Deni lako ni TSh {Get("kiasi")} ({Get("kodi")}). " +
                    $"Mwisho {Get("tarehe")}.",

                Fine =>
                    $"{Get("kikundi")}: Muda wa malipo umepita. Deni lako sasa ni TSh {Get("kiasi")} " +
                    $"pamoja na faini. Kodi yako {Get("kodi")}.",

                AtRisk =>
                    $"{Get("kikundi")}: Hatua ya mwisho kabla ya kusimamishwa. Lipa TSh {Get("kiasi")} " +
                    $"({Get("kodi")}) kabla ya {Get("tarehe")}.",

                PaymentConfirmed =>
                    $"{Get("kikundi")}: Malipo yako ya TSh {Get("kiasi")} yamethibitishwa. Asante. " +
                    $"Salio lako ni TSh {Get("salio")}.",

                JoiningPayment =>
                    $"{Get("kikundi")}: Ombi lako la kujiunga limepokelewa. Lipa TSh {Get("kiasi")} " +
                    $"ukitumia kodi {Get("kodi")} ndani ya siku {Get("siku")}.",

                _ => Get("ujumbe")
            };
        }
    }

    // ==================================================================
    // 2. SALIO LA CREDITS
    // ==================================================================

    public async Task<SmsAccount> AccountAsync(Guid groupId, CancellationToken ct = default)
    {
        var acc = await _context.SmsAccounts.FirstOrDefaultAsync(a => a.GroupId == groupId, ct);
        if (acc is null)
        {
            acc = new SmsAccount { GroupId = groupId, Credits = 0 };
            _context.SmsAccounts.Add(acc);
            await _context.SaveChangesAsync(ct);
        }
        return acc;
    }

    /// <summary>Viongozi wanaongeza credits (zilizonunuliwa kwa mtoa-huduma). Si pesa ya mfumo — ni salio la ujumbe.</summary>
    public async Task<SmsAccount> TopUpAsync(Guid groupId, int credits, string? reason, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, "Kuweka salio la SMS ni kazi ya viongozi.");
        if (credits <= 0) throw new ValidationException("Weka idadi ya SMS iliyo zaidi ya sifuri.");
        if (credits > MaxTopUpCredits) throw new ValidationException($"Idadi moja ya SMS isizidi {MaxTopUpCredits:N0}. Weka kwa awamu.");

        var acc = await AccountAsync(groupId, ct);
        var before = new { acc.Credits };
        acc.Credits += credits;
        acc.TotalPurchased += credits;
        acc.UpdatedAt = DateTime.UtcNow;
        acc.UpdatedByUserId = userId;

        await AuditTrail.AddAsync(_context, groupId, null, userId, AuditAction.Updated, nameof(SmsAccount), groupId,
            before, new { acc.Credits, Added = credits }, null,
            string.IsNullOrWhiteSpace(reason) ? "Kuongeza salio la SMS" : reason.Trim()[..Math.Min(reason.Trim().Length, 300)], ct);
        await _context.SaveChangesAsync(ct);
        return acc;
    }

    public sealed record SmsOverview(int Credits, int TotalPurchased, int TotalSent, int Queued, int Failed, int Suppressed,
        int LowThreshold, bool Enabled);

    public async Task<SmsOverview> OverviewAsync(Guid groupId, CancellationToken ct = default)
    {
        var acc = await AccountAsync(groupId, ct);
        var s = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        var queued = await _context.SmsMessages.CountAsync(m => m.GroupId == groupId && m.Status == SmsStatus.Queued, ct);
        var failed = await _context.SmsMessages.CountAsync(m => m.GroupId == groupId && m.Status == SmsStatus.Failed, ct);
        var suppressed = await _context.SmsMessages.CountAsync(m => m.GroupId == groupId && m.Status == SmsStatus.Suppressed, ct);
        return new SmsOverview(acc.Credits, acc.TotalPurchased, acc.TotalSent, queued, failed, suppressed,
            s?.SmsLowCreditThreshold ?? 0, s?.SmsEnabled ?? false);
    }

    // ==================================================================
    // 3. KUPANGA UJUMBE
    // ==================================================================

    /// <summary>
    /// Hupanga SMS kwa mwanachama mmoja au wengi. Hii haitumi — inaweka kwenye foleni.
    /// Inaheshimu: kikundi kimewasha SMS, kikomo kwa tukio, salio la credits, saa za utulivu.
    /// </summary>
    public async Task<int> QueueAsync(Guid groupId, IEnumerable<Guid> memberIds, string templateKey,
        IReadOnlyDictionary<string, string> values, string? relatedEntityType = null, Guid? relatedEntityId = null,
        Guid? createdByUserId = null, CancellationToken ct = default)
    {
        var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        if (settings is null || !settings.SmsEnabled) return 0;

        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && ids.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber, m.Status })
            .ToListAsync(ct);
        if (members.Count == 0) return 0;

        var phones = await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && ids.Contains(p.GroupMemberId)
                        && p.Status == TruthApprovalStatus.Approved && p.ValidTo == null)
            .OrderByDescending(p => p.AddedAt)
            .ToListAsync(ct);
        var phoneOf = phones.GroupBy(p => p.GroupMemberId)
            .ToDictionary(g => g.Key, g => g.First().Phone);

        // Kikomo kwa mwanachama kwa tukio moja.
        var cap = settings.SmsMaxPerMemberPerEvent;
        HashSet<Guid> already = new();
        if (cap > 0 && relatedEntityId is not null)
        {
            var counts = await _context.SmsMessages.AsNoTracking()
                .Where(m => m.GroupId == groupId && m.RelatedEntityId == relatedEntityId && m.Status != SmsStatus.Suppressed)
                .GroupBy(m => m.GroupMemberId)
                .Select(g => new { MemberId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            already = counts.Where(c => c.MemberId is not null && c.Count >= cap)
                .Select(c => c.MemberId!.Value).ToHashSet();
        }

        var acc = await AccountAsync(groupId, ct);
        var maxChars = settings.SmsMaxCharsPerMessage > 0 ? settings.SmsMaxCharsPerMessage : DefaultCharsPerMessage;
        var sendAfter = NextAllowedSend(DateTime.UtcNow, settings);
        var now = DateTime.UtcNow;
        var queued = 0;

        foreach (var m in members)
        {
            if (!MemberAccess.IsActive(m.Status)) continue;
            if (already.Contains(m.Id)) continue;

            if (!phoneOf.TryGetValue(m.Id, out var phone))
            {
                _context.SmsMessages.Add(Suppressed(groupId, m.Id, string.Empty, templateKey, settings,
                    "Mwanachama hana namba iliyoidhinishwa", relatedEntityType, relatedEntityId, createdByUserId, now));
                continue;
            }

            var body = Templates.Render(templateKey, values);
            var credits = Math.Max(1, (int)Math.Ceiling(body.Length / (double)maxChars));

            if (acc.Credits < credits)
            {
                _context.SmsMessages.Add(Suppressed(groupId, m.Id, phone, templateKey, settings,
                    "Salio la SMS halitoshi", relatedEntityType, relatedEntityId, createdByUserId, now));
                continue;
            }

            _context.SmsMessages.Add(new SmsMessage
            {
                Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = m.Id, Phone = phone,
                TemplateKey = templateKey, Body = body, Status = SmsStatus.Queued,
                SendAfter = sendAfter, Credits = credits,
                RelatedEntityType = relatedEntityType, RelatedEntityId = relatedEntityId,
                CreatedByUserId = createdByUserId, CreatedAt = now
            });
            queued++;
        }

        // Salio linakatwa WAKATI WA KUTUMA (DispatchAsync), si wakati wa kupanga:
        // hivyo credits haziwezi kupotea kwa ujumbe ambao haujatumwa.
        // Ujumbe unaozidi salio unazuiwa wakati wa kutuma (Suppressed) — wazi kwenye kumbukumbu.
        await _context.SaveChangesAsync(ct);
        return queued;
    }

    private static SmsMessage Suppressed(Guid groupId, Guid memberId, string phone, string templateKey,
        TruthGroupSettings settings, string reason, string? entityType, Guid? entityId, Guid? userId, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = memberId, Phone = phone,
            TemplateKey = templateKey, Body = string.Empty, Status = SmsStatus.Suppressed,
            SuppressedReason = reason, Credits = 0,
            RelatedEntityType = entityType, RelatedEntityId = entityId, CreatedByUserId = userId, CreatedAt = now
        };

    /// <summary>
    /// Saa inayofuata inayoruhusiwa kutuma (EAT). Nje ya saa za utulivu, ujumbe
    /// unasubiri hadi saa ya kuanza — haukataliwi.
    /// </summary>
    public static DateTime? NextAllowedSend(DateTime utcNow, TruthGroupSettings s)
    {
        var from = s.SmsQuietFromHour;
        var to = s.SmsQuietToHour;
        if (from <= 0 && to >= 24) return null;                 // hakuna kizuizi

        var eat = TimeZoneInfo.ConvertTimeFromUtc(utcNow, MkobaPlatform.ResolveTimeZone());
        var hour = eat.Hour;

        var allowed = from < to
            ? hour >= from && hour < to
            : hour >= from || hour < to;

        if (allowed) return null;

        // Saa inayofuata ya kuanza (leo au kesho), kisha rudisha UTC.
        var start = eat.Date.AddHours(from == 0 ? 0 : from);
        if (eat >= start) start = start.AddDays(1);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), MkobaPlatform.ResolveTimeZone());
        return startUtc;
    }

    // ==================================================================
    // 4. MNYORORO: kutuma
    // ==================================================================

    public sealed record DispatchResult(int Attempted, int Sent, int Failed, int Skipped);

    /// <summary>
    /// Hutuma ujumbe uliopangwa na uliofika wakati wake. Huitwa na huduma ya nyuma
    /// (kila sekunde 30) au kwa mkono na kiongozi.
    /// </summary>
    public async Task<DispatchResult> DispatchAsync(int take = 50, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var batch = await _context.SmsMessages
            .Where(m => m.Status == SmsStatus.Queued && (m.SendAfter == null || m.SendAfter <= now))
            .OrderBy(m => m.CreatedAt).Take(take).ToListAsync(ct);
        if (batch.Count == 0) return new DispatchResult(0, 0, 0, 0);

        var accounts = await _context.SmsAccounts
            .Where(a => batch.Select(b => b.GroupId).Contains(a.GroupId))
            .ToDictionaryAsync(a => a.GroupId, ct);

        var sent = 0; var failed = 0; var skipped = 0;

        foreach (var m in batch)
        {
            if (!accounts.TryGetValue(m.GroupId, out var acc) || acc.Credits < m.Credits)
            {
                m.Status = SmsStatus.Suppressed;
                m.SuppressedReason = "Salio la SMS halitoshi";
                skipped++;
                continue;
            }

            m.Attempts++;
            var result = await _gateway.SendAsync(m.Phone, m.Body, ct);

            if (result.Success)
            {
                m.Status = SmsStatus.Sent;
                m.ProviderReference = result.Reference;
                m.ProviderStatus = "Sent";
                m.SentAt = now;
                m.FailureReason = null;
                acc.Credits -= m.Credits;
                acc.TotalSent += 1;
                acc.UpdatedAt = now;
                sent++;
            }
            else
            {
                m.FailureReason = result.Failure;
                if (m.Attempts >= 3)
                {
                    m.Status = SmsStatus.Failed;
                    failed++;
                }
                else
                {
                    // Jaribio la pili/la tatu: dakika 5 baadaye.
                    m.SendAfter = now.AddMinutes(5);
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        // Onyo la salio kwa viongozi (mara moja kwa wakati mmoja).
        foreach (var (groupId, acc) in accounts)
        {
            var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
            var threshold = settings?.SmsLowCreditThreshold ?? 0;
            if (threshold > 0 && acc.Credits <= threshold)
                await WarnLowCreditsAsync(groupId, acc.Credits, ct);
        }

        return new DispatchResult(batch.Count, sent, failed, skipped);
    }

    private async Task WarnLowCreditsAsync(Guid groupId, int credits, CancellationToken ct)
    {
        var leaders = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && MemberAccess.LeaderRoles.Contains(m.Role)
                        && MemberAccess.ActiveStatuses.Contains(m.Status))
            .Select(m => m.Id).ToListAsync(ct);
        if (leaders.Count == 0) return;

        var recent = await _context.MemberNotifications.AsNoTracking()
            .AnyAsync(n => n.GroupId == groupId && n.Kind == "SmsLowCredits" && n.CreatedAt > DateTime.UtcNow.AddHours(-12), ct);
        if (recent) return;

        await _notify.NotifyAsync(groupId, leaders, "SmsLowCredits", "Salio la SMS linaisha",
            $"SMS zilizobaki: {credits}. Ongezeni salio ili kumbusho lisiache kufika kwa wanachama.", "/mipangilio/sms", ct);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Mtandao unatuma hali ya uwasilishaji (delivery report) kwa kumbukumbu ya mtoa-huduma.</summary>
    public async Task<bool> MarkDeliveredAsync(string providerReference, string status, CancellationToken ct = default)
    {
        var m = await _context.SmsMessages.FirstOrDefaultAsync(x => x.ProviderReference == providerReference, ct);
        if (m is null) return false;
        m.ProviderStatus = status?.Length > 40 ? status[..40] : status;
        if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "DELIVRD", StringComparison.OrdinalIgnoreCase))
        {
            m.Status = SmsStatus.Delivered;
            m.DeliveredAt = DateTime.UtcNow;
        }
        else if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(status, "UNDELIV", StringComparison.OrdinalIgnoreCase))
        {
            m.Status = SmsStatus.Failed;
            m.FailureReason = $"Mtandao: {m.ProviderStatus}";
        }
        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ==================================================================
    // 5. VICHOCHEO
    // ==================================================================

    /// <summary>Tukio jipya: kila mwanachama anapata kiasi chake na kodi yake.</summary>
    public async Task<int> QueueEventAnnouncementAsync(Guid groupId, BenefitCase benefitCase, string groupName,
        IReadOnlyList<(Guid MemberId, decimal Amount, string? Code)> members, CancellationToken ct = default)
    {
        var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        if (settings is null || !settings.SmsEnabled || members.Count == 0) return 0;

        var tz = MkobaPlatform.ResolveTimeZone();
        var due = DateTime.UtcNow.AddDays(Math.Max(0, settings.EventTopUpWindowDays));
        var dueText = TimeZoneInfo.ConvertTimeFromUtc(due, tz).ToString("dd/MM/yyyy");
        var total = 0;

        foreach (var m in members)
        {
            total += await QueueAsync(groupId, new[] { m.MemberId }, Templates.EventAnnouncement,
                new Dictionary<string, string>
                {
                    ["kikundi"] = groupName,
                    ["kiasi"] = m.Amount.ToString("N0"),
                    ["tarehe"] = dueText,
                    ["kodi"] = m.Code ?? "-"
                }, nameof(BenefitCase), benefitCase.Id, ct: ct);
        }
        return total;
    }

    /// <summary>Kumbusho la siku 2 kabla ya tarehe ya mwisho (hutumwa na huduma ya nyuma).</summary>
    public async Task<int> QueueDueRemindersAsync(DateTime now, CancellationToken ct = default)
    {
        var horizon = now.AddDays(2);
        var due = await _context.EventObligations.AsNoTracking()
            .Where(o => o.Status == EventObligationStatus.Pending && o.DueDate != null
                        && o.DueDate > now && o.DueDate <= horizon)
            .Select(o => new { o.Id, o.GroupId, o.GroupMemberId, o.RequiredAmount, o.DeductedAmount, o.PaymentCode, o.DueDate })
            .ToListAsync(ct);
        if (due.Count == 0) return 0;

        var groups = await _context.Groups.AsNoTracking()
            .Where(g => due.Select(d => d.GroupId).Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var sentAlready = (await _context.SmsMessages.AsNoTracking()
                .Where(m => m.TemplateKey == Templates.Reminder && m.RelatedEntityId != null
                            && m.CreatedAt > now.AddDays(-3))
                .Select(m => m.RelatedEntityId!.Value).ToListAsync(ct))
            .ToHashSet();

        var tz = MkobaPlatform.ResolveTimeZone();
        var total = 0;
        foreach (var o in due)
        {
            if (sentAlready.Contains(o.Id)) continue;
            var outstanding = Math.Max(0m, o.RequiredAmount - o.DeductedAmount);
            if (outstanding <= 0) continue;
            var days = Math.Max(1, (int)Math.Ceiling((o.DueDate!.Value - now).TotalDays));

            total += await QueueAsync(o.GroupId, new[] { o.GroupMemberId }, Templates.Reminder,
                new Dictionary<string, string>
                {
                    ["kikundi"] = groups.GetValueOrDefault(o.GroupId, "Kikundi"),
                    ["siku"] = days.ToString(),
                    ["kiasi"] = outstanding.ToString("N0"),
                    ["kodi"] = o.PaymentCode ?? "-",
                    ["tarehe"] = TimeZoneInfo.ConvertTimeFromUtc(o.DueDate.Value, tz).ToString("dd/MM/yyyy")
                }, nameof(EventObligation), o.Id, ct: ct);
        }
        return total;
    }

    /// <summary>Malipo yamethibitishwa: mwanachama anajua deni lililobaki.</summary>
    public Task<int> QueuePaymentConfirmedAsync(Guid groupId, Guid memberId, decimal amount, decimal balance,
        string? code, string groupName, CancellationToken ct = default)
        => QueueAsync(groupId, new[] { memberId }, Templates.PaymentConfirmed,
            new Dictionary<string, string>
            {
                ["kikundi"] = groupName,
                ["kiasi"] = amount.ToString("N0"),
                ["salio"] = balance.ToString("N0"),
                ["kodi"] = code ?? "-"
            }, nameof(PaymentReceipt), null, ct: ct);

    /// <summary>Faini imetozwa (huitwa na ChargeOverdueFinesAsync).</summary>
    public Task<int> QueueFineAsync(Guid groupId, Guid memberId, decimal outstanding, string? code, string groupName,
        Guid obligationId, CancellationToken ct = default)
        => QueueAsync(groupId, new[] { memberId }, Templates.Fine,
            new Dictionary<string, string>
            {
                ["kikundi"] = groupName,
                ["kiasi"] = outstanding.ToString("N0"),
                ["kodi"] = code ?? "-"
            }, nameof(EventObligation), obligationId, ct: ct);

    /// <summary>Muombaji wa kujiunga anapata kodi na kiasi (kabla ya uanachama).</summary>
    public async Task<bool> QueueJoiningPaymentAsync(Guid groupId, string phone, string groupName, decimal amount,
        string code, int windowDays, CancellationToken ct = default)
    {
        var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        if (settings is null || !settings.SmsEnabled) return false;
        if (string.IsNullOrWhiteSpace(phone)) return false;

        var body = Templates.Render(Templates.JoiningPayment, new Dictionary<string, string>
        {
            ["kikundi"] = groupName,
            ["kiasi"] = amount.ToString("N0"),
            ["kodi"] = code,
            ["siku"] = Math.Max(1, windowDays).ToString()
        });

        _context.SmsMessages.Add(new SmsMessage
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = null, Phone = phone,
            TemplateKey = Templates.JoiningPayment, Body = body, Status = SmsStatus.Queued,
            SendAfter = NextAllowedSend(DateTime.UtcNow, settings), Credits = 1,
            RelatedEntityType = nameof(MembershipRequest), CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);
        return true;
    }

    // ==================================================================
    // 6. KUMBUKUMBU
    // ==================================================================

    public sealed record SmsRow(Guid Id, string? MemberNumber, string Phone, string TemplateKey, string Body,
        string Status, int Attempts, string? ProviderStatus, string? FailureReason, string? SuppressedReason,
        DateTime CreatedAt, DateTime? SentAt, DateTime? DeliveredAt);

    public async Task<List<SmsRow>> ListAsync(Guid groupId, string? status, int take = 200, CancellationToken ct = default)
    {
        var q = _context.SmsMessages.AsNoTracking().Where(m => m.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SmsStatus>(status, true, out var st))
            q = q.Where(m => m.Status == st);

        var rows = await q.OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync(ct);
        var ids = rows.Where(r => r.GroupMemberId != null).Select(r => r.GroupMemberId!.Value).Distinct().ToList();
        var numbers = await _context.GroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.Id)).Select(m => new { m.Id, m.MemberNumber })
            .ToDictionaryAsync(m => m.Id, m => m.MemberNumber, ct);

        // Namba ya simu inaonyeshwa kwa kificho (faragha).
        static string Mask(string p) => string.IsNullOrWhiteSpace(p) || p.Length < 6 ? p
            : string.Concat(p.AsSpan(0, 4), "****", p.AsSpan(p.Length - 3));

        return rows.Select(r => new SmsRow(r.Id,
            r.GroupMemberId is { } gid ? numbers.GetValueOrDefault(gid) : null,
            Mask(r.Phone), r.TemplateKey, r.Body, r.Status.ToString(), r.Attempts,
            r.ProviderStatus, r.FailureReason, r.SuppressedReason, r.CreatedAt, r.SentAt, r.DeliveredAt)).ToList();
    }
}
