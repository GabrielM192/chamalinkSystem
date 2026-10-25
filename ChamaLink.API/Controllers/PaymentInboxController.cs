using ChamaLink.API.Extensions;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Documents;
using ChamaLink.Infrastructure.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>
/// SASISHO21 — SANDUKU LA MALIPO.
///
///   Mwanachama : anadai "nimelipa" (namba ya muamala) na anaona hali ya dai lake.
///   Viongozi   : wanaona mistari ya statement isiyotambulika, wanapendekezwa
///                mwanachama, wanathibitisha au wanakataa — kila hatua kwa audit.
///
/// Hakuna endpoint ya kuandika salio kwa mkono. Uthibitisho hubadilisha
/// MMILIKI wa mstari wa statement pekee; deni linapungua kwa waterfall ile ile.
/// </summary>
[Authorize]
[ApiController]
[Route("api/payments")]
public class PaymentInboxController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly PaymentInboxService _inbox;
    private readonly PaymentDocumentsService _docs;

    public PaymentInboxController(ApplicationDbContext context, PaymentInboxService inbox, PaymentDocumentsService docs)
    {
        _context = context;
        _inbox = inbox;
        _docs = docs;
    }

    // ------------------------------------------------------------------
    // Viongozi: sanduku
    // ------------------------------------------------------------------

    /// <summary>Kujenga sanduku kutoka statement iliyokwisha kuidhinishwa. Idempotent.</summary>
    [HttpPost("{groupId:guid}/inbox/sync")]
    public async Task<IActionResult> Sync(Guid groupId, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Kusawazisha sanduku la malipo ni kazi ya viongozi.");
        var added = await _inbox.SyncAsync(groupId, ct);
        return Ok(new { added, ujumbe = added == 0 ? "Hakuna mstari mpya." : $"Mistari {added} imeingizwa kwenye sanduku." });
    }

    /// <summary>Orodha ya risiti + muhtasari (default: zote, chujio kwa status).</summary>
    [HttpGet("{groupId:guid}/inbox")]
    public async Task<IActionResult> List(Guid groupId, [FromQuery] string? status, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Sanduku la malipo linaonekana kwa viongozi pekee.");
        var rows = await _inbox.ListAsync(groupId, status, ct);
        var summary = await _inbox.SummaryAsync(groupId, ct);
        return Ok(new { summary, rows });
    }

    /// <summary>Meza ya pande tatu: statement · madai · kinachotarajiwa.</summary>
    [HttpGet("{groupId:guid}/inbox/three-way")]
    public async Task<IActionResult> ThreeWay(Guid groupId, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Meza ya ulinganisho ni ya viongozi.");
        return Ok(await _inbox.ThreeWayAsync(groupId, ct));
    }

    /// <summary>memberId ni tupu kwa risiti ya ada ya kujiunga (hapana mwanachama bado).</summary>
    public sealed record ConfirmBody(Guid? MemberId, string? Note, bool SecondApproval);

    /// <summary>
    /// Kuthibitisha kwamba mstari wa statement ni wa mwanachama.
    /// Kiasi kikubwa: mwito wa kwanza unafungua ombi, wa pili (kiongozi MWINGINE) unakamilisha.
    /// </summary>
    [HttpPost("{groupId:guid}/inbox/{receiptId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid groupId, Guid receiptId, [FromBody] ConfirmBody? body, CancellationToken ct)
    {
        // memberId inaweza kuwa tupu kwa risiti ya ada ya KUJIUNGA (hapana mwanachama bado);
        // huduma yenyewe inakataa risiti nyingine zisizo na mwanachama.
        if (body is null) throw new ValidationException("Taarifa za uthibitisho hazipo.");
        return Ok(await _inbox.ConfirmAsync(groupId, receiptId, body.MemberId ?? Guid.Empty, body.Note, User.GetUserId(),
            body.SecondApproval, ct));
    }

    public sealed record RejectBody(string? Reason);

    [HttpPost("{groupId:guid}/inbox/{receiptId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid groupId, Guid receiptId, [FromBody] RejectBody? body, CancellationToken ct)
        => Ok(await _inbox.RejectAsync(groupId, receiptId, body?.Reason ?? string.Empty, User.GetUserId(), ct));

    // ------------------------------------------------------------------
    // Mwanachama: madai
    // ------------------------------------------------------------------

    public sealed record ClaimBody(decimal Amount, string? Reference, string? PaymentCode, string? Note);

    /// <summary>"Nimelipa" — madai yenye namba ya muamala. Hayabadilishi deni.</summary>
    [HttpPost("{groupId:guid}/claims")]
    public async Task<IActionResult> Claim(Guid groupId, [FromBody] ClaimBody? body, CancellationToken ct)
    {
        if (body is null) throw new ValidationException("Taarifa za dai hazipo.");
        var c = await _inbox.ClaimAsync(groupId, body.Amount, body.Reference ?? string.Empty,
            body.PaymentCode, body.Note, User.GetUserId(), ct);
        return Ok(new
        {
            claimId = c.Id, status = c.Status.ToString(),
            ujumbe = "Dai lako limepokelewa. Linahitaji uchunguzi wa hazina kabla ya kuthibitishwa — halibadilishi deni bado."
        });
    }

    /// <summary>Madai: mwanachama anaona yake; viongozi wanaona yote.</summary>
    [HttpGet("{groupId:guid}/claims")]
    public async Task<IActionResult> Claims(Guid groupId, CancellationToken ct)
        => Ok(await _inbox.ClaimsAsync(groupId, User.GetUserId(), ct));

    public sealed record DecideClaimBody(bool Approve, string? Note, Guid? ReceiptId);

    [HttpPost("{groupId:guid}/claims/{claimId:guid}/decide")]
    public async Task<IActionResult> DecideClaim(Guid groupId, Guid claimId, [FromBody] DecideClaimBody? body, CancellationToken ct)
    {
        if (body is null) throw new ValidationException("Uamuzi haujatumwa.");
        return Ok(await _inbox.DecideClaimAsync(groupId, claimId, body.Approve, body.Note, body.ReceiptId,
            User.GetUserId(), ct));
    }

    // ------------------------------------------------------------------
    // PDF (SASISHO21): risiti kwa mwanachama, ripoti kwa viongozi.
    // PDF inasoma ukweli ulioandikwa — haibadilishi deni wala kuhesabu upya.
    // ------------------------------------------------------------------

    /// <summary>Risiti ya malipo (PDF). Mwanachama anapakua yake; kiongozi yeyote wa kikundi.</summary>
    [HttpGet("{groupId:guid}/receipts/{receiptId:guid}.pdf")]
    public async Task<IActionResult> ReceiptPdf(Guid groupId, Guid receiptId, CancellationToken ct)
    {
        var (bytes, name) = await _docs.ReceiptAsync(groupId, receiptId, User.GetUserId(), ct);
        return File(bytes, "application/pdf", name);
    }

    /// <summary>Ripoti ya sanduku la malipo (PDF) — viongozi pekee.</summary>
    [HttpGet("{groupId:guid}/inbox/report.pdf")]
    public async Task<IActionResult> InboxReportPdf(Guid groupId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var (bytes, name) = await _docs.InboxReportAsync(groupId, User.GetUserId(), from, to, ct);
        return File(bytes, "application/pdf", name);
    }
}
