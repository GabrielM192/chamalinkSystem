using ChamaLink.API.Extensions;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>
/// SASISHO18 — POCHI ZA VIKUNDI VYA "KILA TUKIO".
///
///   Mwanachama  : anaona pochi YAKE tu (salio, floor, deni, historia).
///   Viongozi    : wanaona pochi za wote, madeni, na walio chini ya floor.
///
/// Hakuna endpoint ya kuandika salio kwa mkono: makato yanatokea TU tukio
/// linapoidhinishwa (BenefitService), na malipo yanatokea TU kwa amana
/// zilizokubaliwa (Injini ya Ukweli). Hii ni kanuni ya mradi.
/// </summary>
[Authorize]
[ApiController]
[Route("api/events")]
public class EventWalletController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly EventWalletService _wallet;
    private readonly AtRiskService _atRisk;

    public EventWalletController(ApplicationDbContext context, EventWalletService wallet, AtRiskService atRisk)
    {
        _context = context;
        _wallet = wallet;
        _atRisk = atRisk;
    }

    public sealed record ObligationDto(
        Guid ObligationId, Guid CaseId, string? CaseNumber, DateTime CreatedAt, DateTime? DueDate,
        decimal RequiredAmount, decimal DeductedAmount, decimal OutstandingAmount, string Status, bool Overdue,
        /// <summary>SASISHO21 — kodi ya malipo (mf. "J3-E07-0147"). Mwanachama anaiandika kwenye maelezo.</summary>
        string? PaymentCode);

    public sealed record WalletDto(
        Guid GroupId, Guid MemberId, string MemberNumber, decimal Balance, decimal Floor, decimal Available,
        decimal Outstanding, List<ObligationDto> Obligations);

    // ------------------------------------------------------------------
    // Mwanachama: pochi yake mwenyewe
    // ------------------------------------------------------------------
    /// <summary>Pochi yangu — salio, floor, deni na historia ya makato ya matukio.</summary>
    [HttpGet("{groupId}/wallet/me")]
    public async Task<IActionResult> MyWallet(Guid groupId, CancellationToken ct)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, User.GetUserId(), ct, tracking: false);
        return Ok(await BuildAsync(groupId, me.Id, me.MemberNumber, ct));
    }

    // ------------------------------------------------------------------
    // Viongozi: pochi ya mwanachama yeyote
    // ------------------------------------------------------------------
    [HttpGet("{groupId}/wallet/{memberId:guid}")]
    public async Task<IActionResult> MemberWallet(Guid groupId, Guid memberId, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct);
        var m = await _context.GroupMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == memberId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Mwanachama hakapatikana kwenye kikundi hiki.");
        return Ok(await BuildAsync(groupId, m.Id, m.MemberNumber, ct));
    }

    /// <summary>
    /// Orodha ya wanachama walio na deni la matukio (kiongozi).
    /// SASISHO21: inaonyesha deni, faini, AKIBA, kiasi cha kurejesha na siku —
    /// pamoja na kupakua CSV kwa kikao cha viongozi.
    /// </summary>
    [HttpGet("{groupId}/wallet/outstanding")]
    public async Task<IActionResult> Outstanding(Guid groupId, [FromQuery] string? format, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct);

        var isEvent = await _context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.Type == GroupType.EventBased).FirstOrDefaultAsync(ct);
        if (!isEvent) throw new ValidationException("Orodha ya deni la matukio ni ya vikundi vya \"kila tukio\" pekee.");

        var settings = await _context.TruthGroupSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        var fineAmount = settings?.OverdueEventFineAmount ?? 0m;
        var floor = settings?.MemberMinimumBalance ?? 0m;
        var now = DateTime.UtcNow;

        var open = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.Status == EventObligationStatus.Pending)
            .Select(o => new
            {
                o.GroupMemberId, o.RequiredAmount, o.DeductedAmount, o.DueDate, o.OverdueFineIssued
            })
            .ToListAsync(ct);

        // Salio la wote kwa query MOJA (SASISHO21 — kabla: queries 2 kwa kila mwanachama).
        var balances = await _wallet.BalancesAsync(groupId, ct);

        var grouped = open
            .GroupBy(o => o.GroupMemberId)
            .Select(g => new
            {
                MemberId = g.Key,
                Outstanding = g.Sum(x => Math.Max(0m, x.RequiredAmount - x.DeductedAmount)),
                Events = g.Count(),
                Fines = g.Count(x => x.OverdueFineIssued) * fineAmount,
                OldestDue = g.Where(x => x.DueDate != null).Min(x => x.DueDate)
            })
            .Where(x => x.Outstanding > 0)
            .OrderByDescending(x => x.Outstanding)
            .ToList();

        var ids = grouped.Select(x => x.MemberId).ToList();
        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && ids.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber, m.UserId })
            .ToListAsync(ct);
        var memberMap = members.ToDictionary(m => m.Id);

        var names = members.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _context.Users.AsNoTracking()
                .Where(u => members.Select(m => m.UserId).Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => (string?)u.FullName, ct);

        var list = grouped.Select(x =>
        {
            memberMap.TryGetValue(x.MemberId, out var m);
            balances.TryGetValue(x.MemberId, out var balance);
            var shortfall = Math.Max(0m, floor - balance);
            var days = x.OldestDue is { } d ? Math.Max(0, (int)Math.Floor((now - d).TotalDays)) : 0;

            return new OutstandingRowDto(
                x.MemberId,
                m?.MemberNumber,
                m is null ? null : names.GetValueOrDefault(m.UserId),
                x.Outstanding,
                x.Fines,
                balance,
                shortfall,
                x.Outstanding + shortfall,
                days,
                x.Events);
        }).ToList();

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return Csv("deni-la-matukio", new[]
            {
                "Namba ya mwanachama", "Jina", "Deni", "Faini", "Akiba", "Kurejesha", "Siku", "Matukio"
            }, list.Select(r => new[]
            {
                r.MemberNumber ?? "", r.Name ?? "",
                r.Outstanding.ToString("0.00"), r.Fines.ToString("0.00"),
                r.Balance.ToString("0.00"), r.RestoreAmount.ToString("0.00"),
                r.DaysOverdue.ToString(), r.Events.ToString()
            }));

        return Ok(list);
    }

    public sealed record OutstandingRowDto(
        Guid MemberId, string? MemberNumber, string? Name,
        decimal Outstanding, decimal Fines, decimal Balance, decimal FloorShortfall,
        decimal RestoreAmount, int DaysOverdue, int Events);

    /// <summary>CSV yenye BOM (Excel inafungua vizuri) na jina lenye tarehe.</summary>
    private FileContentResult Csv(string prefix, string[] headers, IEnumerable<string[]> rows)
    {
        static string Escape(string v)
            => v.Contains(',') || v.Contains('"') || v.Contains('\n')
                ? "\"" + v.Replace("\"", "\"\"") + "\""
                : v;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Escape)));

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", name);
    }

    /// <summary>Kukamilisha deni la mwanachama kwa amana zilizopo (kiongozi).</summary>
    [HttpPost("{groupId}/wallet/{memberId:guid}/sweep")]
    public async Task<IActionResult> Sweep(Guid groupId, Guid memberId, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct);
        var applied = await _wallet.SweepAsync(groupId, memberId, ct);
        return Ok(new { applied, message = applied > 0 ? "Deni limepunguzwa kwa amana zilizopo." : "Hakuna amana ya ziada ya kutumia." });
    }

    // ------------------------------------------------------------------
    // SASISHO19 — HATARINI KUONDOLEWA (kanuni za J3)
    // ------------------------------------------------------------------

    public sealed record AtRiskDto(
        Guid MemberId, string? MemberNumber, string? Name, decimal Outstanding, decimal FineCharged,
        decimal FloorShortfall, decimal Balance, decimal Floor, DateTime? OldestDueDate, int DaysOverdue,
        int PendingEvents, int? PendingRemovalApprovals,
        /// <summary>SASISHO21 — deni + pengo la akiba: kiasi cha kurejesha ili kurudi sawa.</summary>
        decimal RestoreAmount);

    /// <summary>
    /// Orodha ya wanachama walio hatarini kuondolewa — inaonekana kwa WANACHAMA WOTE
    /// (si siri ya viongozi). Pesa ya kikundi ni ya wote, hivyo orodha ni hadharani.
    /// </summary>
    [HttpGet("{groupId}/at-risk")]
    public async Task<IActionResult> AtRisk(Guid groupId, CancellationToken ct)
    {
        var rows = await _atRisk.ListAsync(groupId, User.GetUserId(), ct);

        // Ombi lolote la kuondolewa linalosubiri kura — linaonekana pia.
        var ids = rows.Select(r => r.MemberId).ToList();
        var pending = await _context.MemberStatusChangeRequests.AsNoTracking()
            .Where(r => r.GroupId == groupId && ids.Contains(r.GroupMemberId) && r.Status == ProposalStatus.Pending)
            .Select(r => new { r.GroupMemberId, r.Status, r.RequiredApprovals })
            .ToListAsync(ct);

        var list = rows.Select(r =>
        {
            var p = pending.FirstOrDefault(x => x.GroupMemberId == r.MemberId);
            return new AtRiskDto(r.MemberId, r.MemberNumber, r.Name, r.Outstanding, r.FineCharged,
                r.FloorShortfall, r.Balance, r.Floor, r.OldestDueDate, r.DaysOverdue, r.PendingEvents,
                p?.RequiredApprovals, r.RestoreAmount);
        }).ToList();

        return Ok(new { rows = list, members = list.Count });
    }

    /// <summary>Kiongozi anaomba kuondolewa — viongozi ZAIDI ya wawili wanathibitisha.</summary>
    [HttpPost("{groupId}/at-risk/{memberId:guid}/remove")]
    public async Task<IActionResult> ProposeRemoval(Guid groupId, Guid memberId, [FromBody] RemovalRequest body, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Kuondolewa kwa mwanachama kunaanzishwa na kiongozi.");
        var r = await _atRisk.ProposeRemovalAsync(groupId, memberId, body?.Reason ?? string.Empty, User.GetUserId(), ct);
        return Ok(new { requestId = r.Id, requiredApprovals = r.RequiredApprovals, ujumbe = "Ombi limeanzishwa. Viongozi wanatakiwa kupiga kura." });
    }

    public sealed record RemovalRequest(string? Reason);

    /// <summary>Gharama ya kurudi kwa mwanachama aliyeondolewa (2x faini + ada ya kujiunga).</summary>
    [HttpGet("{groupId}/at-risk/{memberId:guid}/reinstatement")]
    public async Task<IActionResult> Reinstatement(Guid groupId, Guid memberId, CancellationToken ct)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, User.GetUserId(), ct, tracking: false);
        if (me.Id != memberId && !MemberAccess.IsEffectiveLeader(me))
            throw new ForbiddenException("Gharama ya kurudi inaonekana kwa mhusika na viongozi pekee.");
        // Kanuni za kurudi ni za vikundi vya "kila tukio" (pochi) — si vya mchango wa mwezi.
        var isEvent = await _context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.Type == GroupType.EventBased).FirstOrDefaultAsync(ct);
        if (!isEvent) throw new ValidationException("Gharama ya kurudi inahusika na vikundi vya \"kila tukio\" pekee.");
        var c = await _atRisk.ReinstatementCostAsync(groupId, memberId, ct);
        return Ok(c);
    }

    // ------------------------------------------------------------------
    // SASISHO21 — MATUKIO YA KIKUNDI (kila mwanachama anaona)
    // ------------------------------------------------------------------

    public sealed record GroupEventRowDto(
        Guid CaseId, string? CaseNumber, string? EventCode, string Category, string Relationship,
        string? SubjectName, DateTime EventDate, decimal PerMemberAmount,
        string? BeneficiaryMemberNumber, DateTime? DueDate,
        /// <summary>Hali YANGU kwa tukio hili: Imelipwa / Hujalipa / Inahitaji uchunguzi / Umekwisha.</summary>
        string MyStatus, decimal MyOutstanding, string? PaymentCode);

    /// <summary>
    /// Matukio yote yaliyopitishwa na kikundi — msiba, kufukuzwa kazi, kifo cha mwanachama.
    /// Mwanachama anaona tukio + mnufaika + kiasi + HALI YAKE MWENYEWE pekee;
    /// haoni deni la mtu mwingine yeyote.
    /// </summary>
    [HttpGet("{groupId}/group-events")]
    public async Task<IActionResult> GroupEvents(Guid groupId, CancellationToken ct)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, User.GetUserId(), ct, tracking: false);

        var approved = new[]
        {
            BenefitCaseStatus.Approved, BenefitCaseStatus.Paid, BenefitCaseStatus.PaymentInitiated
        };

        var cases = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && approved.Contains(c.Status))
            .OrderByDescending(c => c.EventDate).ThenByDescending(c => c.CaseNumber)
            .Take(60)
            .Select(c => new
            {
                c.Id, c.CaseNumber, c.Category, c.Relationship, c.SubjectName,
                c.EventDate, c.PerMemberAmount, c.BeneficiaryGroupMemberId, c.Status
            })
            .ToListAsync(ct);

        var caseIds = cases.Select(c => c.Id).ToList();

        var mine = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.GroupMemberId == me.Id && caseIds.Contains(o.BenefitCaseId))
            .ToListAsync(ct);
        var mineByCase = mine.ToDictionary(o => o.BenefitCaseId);

        var benIds = cases.Select(c => c.BeneficiaryGroupMemberId).Distinct().ToList();
        var benNumbers = await _context.GroupMembers.AsNoTracking()
            .Where(m => benIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber })
            .ToDictionaryAsync(x => x.Id, x => x.MemberNumber, ct);

        var now = DateTime.UtcNow;

        var rows = cases.Select(c =>
        {
            mineByCase.TryGetValue(c.Id, out var o);
            var status = o is null ? "Hukuhusika"
                : o.Status == EventObligationStatus.Settled ? "Imelipwa"
                : o.Status == EventObligationStatus.Waived ? "Imesamehewa"
                : o.Status == EventObligationStatus.Exempted ? "Hukuhusika"
                : o.OutstandingAmount <= 0 ? "Imelipwa"
                : "Hujalipa";

            // Kikundi kinachokata akiba papo hapo: deni lililobaki ni Outstanding tu.
            return new GroupEventRowDto(
                c.Id,
                c.CaseNumber,
                PaymentCode.EventPart(c.CaseNumber),
                c.Category.ToString(),
                c.Relationship.ToString(),
                c.SubjectName,
                c.EventDate,
                c.PerMemberAmount,
                benNumbers.GetValueOrDefault(c.BeneficiaryGroupMemberId),
                o?.DueDate,
                status,
                o?.OutstandingAmount ?? 0m,
                o?.PaymentCode);
        }).ToList();

        var isEvent = await _context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.Type == GroupType.EventBased).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            groupId,
            isEventBased = isEvent,
            now,
            rows,
            jumla = new
            {
                matukio = rows.Count,
                nayodaiwa = rows.Where(r => r.MyStatus == "Hujalipa").Sum(r => r.MyOutstanding),
                yaliyolipwa = rows.Count(r => r.MyStatus == "Imelipwa")
            }
        });
    }

    // ------------------------------------------------------------------
    private async Task<WalletDto> BuildAsync(Guid groupId, Guid memberId, string memberNumber, CancellationToken ct)
    {
        // SASISHO21: wajibu wa kabla ya toleo hili hauna kodi — ijazwe mara moja.
        // (Ni kazi ya kikundi kizima, lakini hufanya kazi MOJA tu — idempotent.)
        await PaymentCodeBackfill.RunAsync(_context, groupId, ct);

        var w = await _wallet.GetWalletAsync(groupId, memberId, ct);

        var caseNumbers = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && w.History.Select(o => o.BenefitCaseId).Contains(c.Id))
            .Select(c => new { c.Id, c.CaseNumber })
            .ToDictionaryAsync(c => c.Id, c => c.CaseNumber, ct);

        var now = DateTime.UtcNow;
        var obligations = w.History
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new ObligationDto(
                o.Id, o.BenefitCaseId,
                caseNumbers.TryGetValue(o.BenefitCaseId, out var cn) ? cn : null,
                o.CreatedAt, o.DueDate,
                o.RequiredAmount, o.DeductedAmount, o.OutstandingAmount,
                o.Status.ToString(),
                o.DueDate is { } d && d < now && o.OutstandingAmount > 0,
                o.PaymentCode))
            .ToList();

        return new WalletDto(groupId, memberId, memberNumber, w.Balance, w.Floor, w.Available, w.Outstanding, obligations);
    }
}
