using ChamaLink.API.Extensions;
using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GroupController : ControllerBase
{
    private readonly GroupService _groupService;
    private readonly GroupAuthorizationService _groupAuth;

    public GroupController(GroupService groupService, GroupAuthorizationService groupAuth)
    {
        _groupService = groupService;
        _groupAuth = groupAuth;
    }

    // SECURITY FIX (audit 4.1: "adminUserId inatoka route... Hakuna
    // comparison na JWT user"): this used to take adminUserId as a route
    // parameter, so anyone with any valid token could create a group and
    // name a DIFFERENT user as its admin. The admin is now always the
    // authenticated caller - there is no longer a way to create a group
    // "on behalf of" someone else.
    [HttpPost("create")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
    {

        var adminUserId = User.GetUserId();
        var result = await _groupService.CreateGroupAsync(adminUserId, dto);
        return Ok(result);

    }

    // SECURITY FIX (audit 4.5 / policy table "Add member -> Chairperson/
    // Admin"): anyone with a token could previously add a member to any
    // group by guessing/knowing its groupId. Now only that group's own
    // Chairperson can.
    [HttpPost("{groupId}/add-member")]
    public async Task<IActionResult> AddMember(Guid groupId, [FromBody] AddMemberDto dto)
    {

        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, GroupRole.Chairperson);

        var result = await _groupService.AddMemberAsync(groupId, dto);
        return Ok(new { success = result, message = "Mwanachama ameongezwa kikamilifu." });

    }

    // NEW (frontend foundation gap): the only way, until now, for a
    // logged-in user to discover which group(s) they belong to. Every
    // member can see their own memberships - no role restriction, since
    // this reveals nothing beyond "you belong to these groups".
    [HttpGet("my-groups")]
    public async Task<IActionResult> GetMyGroups()
    {
        var groups = await _groupService.GetMyGroupsAsync(User.GetUserId());
        return Ok(groups);
    }

    // Reading settings only requires being a member - no financial
    // change happens here, and members should be able to see their own
    // group's rules (contribution amount, grace period, etc).
    [HttpGet("{groupId}/settings")]
    public async Task<IActionResult> GetSettings(Guid groupId)
    {

        await _groupAuth.RequireMembershipAsync(User.GetUserId(), groupId);

        var settings = await _groupService.GetSettingsAsync(groupId);
        return Ok(settings);

    }

    // SECURITY FIX (audit 1.3: "Group settings zinaweza kubadilishwa bila
    // role validation"): this used to let anyone with a token rewrite a
    // group's contribution amount, fine, grace period, joining fee,
    // minimum reserve, or withdrawal approval rule. Only the Chairperson
    // may change settings now.
    [HttpPut("{groupId}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid groupId, [FromBody] UpdateGroupSettingsDto dto)
    {

        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, GroupRole.Chairperson);

        var settings = await _groupService.UpdateSettingsAsync(groupId, dto);
        return Ok(settings);

    }
}
