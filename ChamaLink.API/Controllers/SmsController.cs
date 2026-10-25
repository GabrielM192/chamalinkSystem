using ChamaLink.API.Extensions;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Sms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

/// <summary>
/// SASISHO21 (Sehemu ya 2) — SMS ZA NO-REPLY.
///
/// Mwanachama asiye na smartphone anafikiwa kwa SMS. Viongozi wanaona foleni,
/// kumbukumbu ya uwasilishaji na salio la credits — na wanaongeza salio.
///
/// Hakuna endpoint ya kutuma ujumbe wa mtu binafsi kwa maandishi ya mkono:
/// ujumbe unatoka kwenye violezo tu (hakuna uvujaji wa taarifa).
/// </summary>
[Authorize]
[ApiController]
[Route("api/sms")]
public class SmsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly SmsService _sms;

    public SmsController(ApplicationDbContext context, SmsService sms)
    {
        _context = context;
        _sms = sms;
    }

    /// <summary>Muhtasari: salio, foleni, zilizoshindikana.</summary>
    [HttpGet("{groupId:guid}/overview")]
    public async Task<IActionResult> Overview(Guid groupId, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Salio la SMS linaonekana kwa viongozi pekee.");
        return Ok(await _sms.OverviewAsync(groupId, ct));
    }

    /// <summary>Kumbukumbu ya ujumbe (namba zimefichwa).</summary>
    [HttpGet("{groupId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid groupId, [FromQuery] string? status, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Kumbukumbu ya SMS ni ya viongozi.");
        return Ok(await _sms.ListAsync(groupId, status, 200, ct));
    }

    public sealed record TopUpBody(int Credits, string? Reason);

    /// <summary>Kuongeza salio la SMS (credits zilizonunuliwa). Kila ongezeko linarekodiwa.</summary>
    [HttpPost("{groupId:guid}/credits")]
    public async Task<IActionResult> TopUp(Guid groupId, [FromBody] TopUpBody? body, CancellationToken ct)
    {
        if (body is null) throw new ValidationException("Idadi ya SMS haijatumwa.");
        var acc = await _sms.TopUpAsync(groupId, body.Credits, body.Reason, User.GetUserId(), ct);
        return Ok(new { credits = acc.Credits, totalPurchased = acc.TotalPurchased });
    }

    /// <summary>Kutuma foleni sasa (bila kusubiri sekunde 30 za huduma ya nyuma).</summary>
    [HttpPost("{groupId:guid}/dispatch")]
    public async Task<IActionResult> Dispatch(Guid groupId, CancellationToken ct)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, User.GetUserId(), ct,
            "Kutuma SMS ni kazi ya viongozi.");
        var r = await _sms.DispatchAsync(100, ct);
        return Ok(new { attempted = r.Attempted, sent = r.Sent, failed = r.Failed, skipped = r.Skipped });
    }

    public sealed record DeliveryBody(string? Reference, string? Status);

    /// <summary>
    /// Mtandao/mtoa-huduma anarudisha hali ya uwasilishaji.
    /// Ulinzi: kichwa cha siri `X-Sms-Callback` kinapaswa kulingana na `Sms:CallbackSecret`.
    /// Kama siri haijawekwa, endpoint inakataliwa (haifunguliwi kwa bahati mbaya).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{groupId:guid}/delivery")]
    public async Task<IActionResult> Delivery(Guid groupId, [FromBody] DeliveryBody? body, IConfiguration config, CancellationToken ct)
    {
        var secret = config["Sms:CallbackSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return NotFound();
        if (!Request.Headers.TryGetValue("X-Sms-Callback", out var got) || got != secret)
            return Unauthorized();

        if (body?.Reference is null || body.Status is null) throw new ValidationException("Rejea na hali vinahitajika.");
        await _sms.MarkDeliveredAsync(body.Reference, body.Status, ct);
        return Ok(new { ok = true });
    }
}
