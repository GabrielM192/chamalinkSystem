using ChamaLink.API.Extensions;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>
/// Hali ya mwanachama (kufukuza / kujitoa / kufariki / kusimamishwa / kurejeshwa) kwa KURA — hakuna kiongozi mmoja
/// anayeweza kumtoa mwanachama. Mwanachama hafutwi kamwe; historia yake ya fedha inabaki.
/// </summary>
[Authorize]
[ApiController]
[Route("api/governance/{groupId:guid}/status-changes")]
public class GovernanceController : ControllerBase
{
    private readonly MemberStatusService _status;
    private readonly AtRiskService _atRisk;
    private readonly ApplicationDbContext _context;

    public GovernanceController(MemberStatusService status, AtRiskService atRisk, ApplicationDbContext context)
    {
        _status = status; _atRisk = atRisk; _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct) => Ok(await _status.ListAsync(groupId, User.GetUserId(), ct));

    [HttpPost]
    public async Task<IActionResult> SubmitRequest(Guid groupId, [FromBody] MemberStatusService.RequestDto dto, CancellationToken ct)
    {
        // SASISHO19: mwanachama aliyeondolewa (Suspended) HARUDISHWI kama ada ya kurudi
        // (faini x kizidishi + ada ya kujiunga) na deni la tukio havijalipwa.
        if (dto.ToStatus == MemberStatus.Active)
        {
            var isEvent = await _context.Groups.AsNoTracking()
                .Where(g => g.Id == groupId).Select(g => g.Type == GroupType.EventBased).FirstOrDefaultAsync(ct);
            if (isEvent) await _atRisk.RequireReinstatementPaidAsync(groupId, dto.MemberId, ct);
        }

        var r = await _status.RequestAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString(), r.RequiredApprovals });
    }

    public sealed record VoteRequest(bool Approve, string? Reason);

    [HttpPost("{requestId:guid}/vote")]
    public async Task<IActionResult> Vote(Guid groupId, Guid requestId, [FromBody] VoteRequest req, CancellationToken ct)
    {
        var r = await _status.VoteAsync(groupId, requestId, req.Approve, req.Reason, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }

    [HttpPost("{requestId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid groupId, Guid requestId, CancellationToken ct)
    {
        await _status.CancelAsync(groupId, requestId, User.GetUserId(), ct);
        return NoContent();
    }
}
