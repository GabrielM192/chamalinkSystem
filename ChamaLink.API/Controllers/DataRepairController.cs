using ChamaLink.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChamaLink.API.Extensions;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Domain;

namespace ChamaLink.API.Controllers;

/// <summary>
/// Awamu 2 (2026-09-19): Ukarabati wa data ya bug-era.
///
/// Kazi:
///   - Preview (READ-ONLY): inaonyesha counter drift, snapshots pungufu,
///     na mapengo ya rejesho la mikopo (bila kubadilisha data).
///   - Repair counters: hesabu upya TotalContributionsCount/AdvanceBalance
///     kutoka ledger (chanzo cha ukweli).
///   - Repair snapshots: jenga upya ComplianceSnapshots kwa miezi ya nyuma
///     (jedwali lilikuwa tupu kabla ya 2026-09-19).
///   - Full repair: counters + snapshots kwa pamoja.
///
/// Usalama: Treasurer/Chairperson/Secretary pekee (sio mwanachama wa kawaida).
/// </summary>
[LegacyEndpoint("Hakuna haja: Injini ya Ukweli haihifadhi hesabu zilizotokana - inacheza upya data ghafi kila ombi")]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DataRepairController : ControllerBase
{
    private readonly DataRepairService _repairService;
    private readonly GroupAuthorizationService _groupAuth;

    public DataRepairController(
        DataRepairService repairService,
        GroupAuthorizationService groupAuth)
    {
        _repairService = repairService;
        _groupAuth = groupAuth;
    }

    [HttpGet("preview/{groupId}")]
    public async Task<IActionResult> GetPreview(Guid groupId, [FromQuery] int months = 24)
    {
        await _groupAuth.RequireRoleAsync(
            User.GetUserId(), groupId,
            GroupRole.Treasurer, GroupRole.Chairperson, GroupRole.Secretary);

        var preview = await _repairService.GetPreviewAsync(groupId, months);
        return Ok(preview);
    }

    [HttpPost("repair-counters/{groupId}")]
    public async Task<IActionResult> RepairCounters(Guid groupId)
    {
        await _groupAuth.RequireRoleAsync(
            User.GetUserId(), groupId,
            GroupRole.Treasurer, GroupRole.Chairperson, GroupRole.Secretary);

        var result = await _repairService.RepairCountersAsync(groupId);
        return Ok(result);
    }

    [HttpPost("repair-snapshots/{groupId}")]
    public async Task<IActionResult> RepairSnapshots(Guid groupId, [FromQuery] int months = 24)
    {
        await _groupAuth.RequireRoleAsync(
            User.GetUserId(), groupId,
            GroupRole.Treasurer, GroupRole.Chairperson, GroupRole.Secretary);

        var result = await _repairService.RepairSnapshotsAsync(groupId, months);
        return Ok(result);
    }

    [HttpPost("full-repair/{groupId}")]
    public async Task<IActionResult> FullRepair(Guid groupId, [FromQuery] int months = 24)
    {
        await _groupAuth.RequireRoleAsync(
            User.GetUserId(), groupId,
            GroupRole.Treasurer, GroupRole.Chairperson, GroupRole.Secretary);

        var result = await _repairService.FullRepairAsync(groupId, months);
        return Ok(result);
    }
}
