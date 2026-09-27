using ChamaLink.API.Filters;
using ChamaLink.API.Extensions;
using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

// This controller was missing before. IEventService existed, but nothing
// in the API called it and it was not registered in Program.cs, so there
// was no way to actually create a GroupEvent (e.g. Msiba/Sherehe) through
// the app. Without a GroupEvent, WelfarePenaltyBackgroundService had
// nothing to check for expired deadlines, so the whole welfare-penalty
// feature could never run in practice.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private readonly IEventService _eventService;
    private readonly GroupAuthorizationService _groupAuth;

    public EventController(IEventService eventService, GroupAuthorizationService groupAuth)
    {
        _eventService = eventService;
        _groupAuth = groupAuth;
    }

    // SECURITY FIX (audit 1.4: "Event trigger inaweza kufanywa na member
    // yeyote"): triggering an event deducts money from EVERY member's
    // welfare account at once - this used to need nothing but a valid
    // token and a GroupId. Only that group's Treasurer or Chairperson can
    // start one now.
    [LegacyEndpoint("Michango ya tukio inalipwa kupitia M-Koba na inaonekana kwenye /api/truth/{groupId}/cashbook")]
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerEvent([FromBody] TriggerEventDto dto)
    {

        await _groupAuth.RequireRoleAsync(User.GetUserId(), dto.GroupId, GroupRole.Treasurer, GroupRole.Chairperson);

        var result = await _eventService.TriggerEventAsync(dto);
        return Ok(result);

    }
}
