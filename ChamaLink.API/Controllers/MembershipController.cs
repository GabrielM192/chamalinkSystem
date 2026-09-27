using ChamaLink.API.Extensions;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

/// <summary>
/// USAJILI NA UANACHAMA (AWAMU I).
///   Mtu yeyote mwenye akaunti:  groups (kuanzisha kikundi), join (kuomba kujiunga kwa kodi), my-requests
///   Viongozi:                   {groupId}/requests, {groupId}/members (kuongeza mwanachama bila akaunti)
///   Mwenyekiti pekee:           short-code, join-code, nafasi (role), kutoa namba rasmi, kuidhinisha Katibu/Mtunza-hazina
/// Ruhusa zinakaguliwa ndani ya MembershipService (dhidi ya DB, si JWT), kwa hiyo nafasi ikibadilishwa inafanya kazi mara moja.
/// Kila kitendo kinaandika AuditEvent.
/// </summary>
[Authorize]
[ApiController]
[Route("api/membership")]
public class MembershipController : ControllerBase
{
    private readonly MembershipService _svc;
    public MembershipController(MembershipService svc) => _svc = svc;

    // ---------------------------------------------------------------- Mtu yeyote mwenye akaunti

    public sealed record CreateGroupRequest(string Name, string? Description, string Type, string ShortCode);

    /// <summary>POST /api/membership/groups — anayeanzisha anakuwa Mwenyekiti.</summary>
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<GroupType>(req.Type, true, out var type) || !Enum.IsDefined(type))
            throw new ValidationException("Chagua aina ya kikundi: MonthlySavings (kila mwezi), EventBased (kila tukio) au Hybrid.");
        return Ok(await _svc.CreateGroupAsync(User.GetUserId(), req.Name, req.Description, type, req.ShortCode, ct));
    }

    /// <summary>GET /api/membership/join/{code} — jina la kikundi, ili mtu ahakiki kabla ya kuomba.</summary>
    [HttpGet("join/{code}")]
    public async Task<IActionResult> PreviewJoin(string code, CancellationToken ct)
        => Ok(await _svc.PreviewJoinAsync(User.GetUserId(), code, ct));

    public sealed record JoinRequest(string JoinCode, string RequestedRole, string? Note);

    [HttpPost("join")]
    public async Task<IActionResult> Join([FromBody] JoinRequest req, CancellationToken ct)
    {
        var r = await _svc.RequestToJoinAsync(User.GetUserId(), req.JoinCode, ParseRole(req.RequestedRole), req.Note, ct);
        return Ok(new { requestId = r.Id, status = r.Status.ToString(), requestedRole = r.RequestedRole.ToString() });
    }

    [HttpGet("my-requests")]
    public async Task<IActionResult> MyRequests(CancellationToken ct) => Ok(await _svc.MyRequestsAsync(User.GetUserId(), ct));

    [HttpDelete("my-requests/{requestId:guid}")]
    public async Task<IActionResult> CancelMyRequest(Guid requestId, CancellationToken ct)
    {
        await _svc.CancelMyRequestAsync(User.GetUserId(), requestId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- Viongozi

    [HttpGet("{groupId:guid}/profile")]
    public async Task<IActionResult> Profile(Guid groupId, CancellationToken ct)
    {
        await _svc.MembersAsync(groupId, User.GetUserId(), ct); // viongozi tu (inakagua ruhusa)
        return Ok(await _svc.GetProfileAsync(groupId, ct));
    }

    public sealed record ShortCodeRequest(string ShortCode);

    [HttpPut("{groupId:guid}/short-code")]
    public async Task<IActionResult> SetShortCode(Guid groupId, [FromBody] ShortCodeRequest req, CancellationToken ct)
        => Ok(await _svc.SetShortCodeAsync(groupId, req.ShortCode, User.GetUserId(), ct));

    [HttpPost("{groupId:guid}/join-code")]
    public async Task<IActionResult> RegenerateJoinCode(Guid groupId, CancellationToken ct)
        => Ok(new { joinCode = await _svc.RegenerateJoinCodeAsync(groupId, User.GetUserId(), ct) });

    [HttpGet("{groupId:guid}/requests")]
    public async Task<IActionResult> Requests(Guid groupId, CancellationToken ct)
        => Ok(await _svc.PendingRequestsAsync(groupId, User.GetUserId(), ct));

    public sealed record DecisionRequest(bool Approve, Guid? LinkMemberId, string? Note);

    [HttpPost("{groupId:guid}/requests/{requestId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid groupId, Guid requestId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var r = await _svc.DecideRequestAsync(groupId, requestId, req.Approve, req.LinkMemberId, req.Note, User.GetUserId(), ct);
        return Ok(new { requestId = r.Id, status = r.Status.ToString(), memberId = r.LinkedMemberId });
    }

    [HttpGet("{groupId:guid}/members")]
    public async Task<IActionResult> Members(Guid groupId, CancellationToken ct)
        => Ok(await _svc.MembersAsync(groupId, User.GetUserId(), ct));

    public sealed record AddMemberRequest(string FullName, string? Phone, DateTime? JoinedAt);

    [HttpPost("{groupId:guid}/members")]
    public async Task<IActionResult> AddMember(Guid groupId, [FromBody] AddMemberRequest req, CancellationToken ct)
        => Ok(await _svc.AddMemberRecordAsync(groupId, req.FullName, req.Phone, req.JoinedAt, User.GetUserId(), ct));

    public sealed record RoleRequest(string Role);

    [HttpPut("{groupId:guid}/members/{memberId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid groupId, Guid memberId, [FromBody] RoleRequest req, CancellationToken ct)
        => Ok(await _svc.ChangeRoleAsync(groupId, memberId, ParseRole(req.Role), User.GetUserId(), ct));

    [HttpPost("{groupId:guid}/member-numbers/issue")]
    public async Task<IActionResult> IssueNumbers(Guid groupId, CancellationToken ct)
    {
        var r = await _svc.IssueMissingNumbersAsync(groupId, User.GetUserId(), ct);
        return Ok(new { issued = r.Issued, changes = r.Changes.Select(c => new { fullName = c.FullName, oldNumber = c.OldNumber, newNumber = c.NewNumber }) });
    }

    private static GroupRole ParseRole(string? raw)
    {
        if (!Enum.TryParse<GroupRole>(raw, true, out var role) || !Enum.IsDefined(role))
            throw new ValidationException("Nafasi si sahihi. Tumia: Member, Secretary, Treasurer, Chairperson au MemberRepresentative.");
        return role;
    }
}
