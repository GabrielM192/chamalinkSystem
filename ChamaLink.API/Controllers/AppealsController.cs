using ChamaLink.API.Extensions;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Appeals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

/// <summary>
/// SASISHO21 — RUfAA.
///
///   Mwanachama : anapinga uamuzi unaomgusa (kufukuzwa/kubadilishwa hali,
///                kukataliwa dai la malipo, faini) na anaona hali ya rufaa yake.
///   Viongozi   : wanapiga kura — kila kiongozi kura MOJA. Rufaa inaamuliwa
///                tu pale viongozi wanaohitajika (sera, kamwe chini ya 2)
///                wanapokubaliana. Aliyeomba hapigani kura yake.
///
/// Rufaa haibadilishi pesa. Inabadilisha UAMUZI tu; kila hatua iko kwenye
/// kumbukumbu za ukaguzi.
/// </summary>
[Authorize]
[ApiController]
[Route("api/appeals")]
public class AppealsController : ControllerBase
{
    private readonly AppealService _appeals;

    public AppealsController(AppealService appeals) => _appeals = appeals;

    public sealed record FileBody(string Kind, Guid? SubjectId, string Reason);

    /// <summary>Mwanachama anawasilisha rufaa. Rufaa haiondoi deni yenyewe.</summary>
    [HttpPost("{groupId:guid}")]
    public async Task<IActionResult> File(Guid groupId, [FromBody] FileBody? body, CancellationToken ct)
    {
        if (body is null) throw new ValidationException("Taarifa za rufaa hazipo.");
        if (!Enum.TryParse<AppealKind>(body.Kind, true, out var kind))
            throw new ValidationException("Aina ya rufaa haijulikani (StatusChange, Claim, Fine, Other).");

        var r = await _appeals.FileAsync(groupId, User.GetUserId(), kind, body.SubjectId, body.Reason, ct);
        return Ok(new { appealId = r.AppealId, status = r.Status, requiredApprovals = r.RequiredApprovals, ujumbe = r.Ujumbe });
    }

    /// <summary>Orodha: mwanachama anaona yake; viongozi wanaona yote.</summary>
    [HttpGet("{groupId:guid}")]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct)
        => Ok(await _appeals.ListAsync(groupId, User.GetUserId(), ct));

    public sealed record VoteBody(bool Approve, string? Note);

    /// <summary>Kiongozi anapiga kura. Kura ikitosha, uamuzi unatekelezwa mara moja.</summary>
    [HttpPost("{groupId:guid}/{appealId:guid}/vote")]
    public async Task<IActionResult> Vote(Guid groupId, Guid appealId, [FromBody] VoteBody? body, CancellationToken ct)
    {
        if (body is null) throw new ValidationException("Kura haipo.");
        var row = await _appeals.VoteAsync(groupId, appealId, body.Approve, body.Note, User.GetUserId(), ct);
        return Ok(new
        {
            status = row?.Status,
            approvals = row?.Approvals ?? 0,
            rejections = row?.Rejections ?? 0,
            requiredApprovals = row?.RequiredApprovals ?? 0,
            effect = row?.EffectSummary,
            ujumbe = row?.Status == nameof(AppealStatus.Pending)
                ? $"Kura yako imepokelewa. Viongozi {row.RequiredApprovals - row.Approvals} zaidi wanahitajika."
                : "Uamuzi wa rufaa umekamilika."
        });
    }

    /// <summary>Mwombaji anafuta rufaa yake kabla ya uamuzi.</summary>
    [HttpDelete("{groupId:guid}/{appealId:guid}")]
    public async Task<IActionResult> Withdraw(Guid groupId, Guid appealId, CancellationToken ct)
    {
        await _appeals.WithdrawAsync(groupId, appealId, User.GetUserId(), ct);
        return Ok(new { status = "Withdrawn" });
    }
}
