using ChamaLink.API.Filters;
using ChamaLink.API.Extensions;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>
/// Historical JoinFee with approval workflow
/// For manual entries like Frank's 10k via Chairperson not in M-Koba
/// </summary>
[LegacyEndpoint("/api/truth/{groupId}/manual-payments  (purpose = JoinFee)")]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class HistoricalJoinFeeController : ControllerBase
{
    private readonly HistoricalJoinFeeService _service;
    private readonly ApplicationDbContext _context;
    private readonly GroupAuthorizationService _groupAuth;

    public HistoricalJoinFeeController(HistoricalJoinFeeService service, ApplicationDbContext context, GroupAuthorizationService groupAuth)
    {
        _service = service;
        _context = context;
        _groupAuth = groupAuth;
    }

    [HttpPost("request")]
    public async Task<IActionResult> Request([FromBody] RequestDto dto)
    {
        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), dto.GroupId, g => g.ImportApproval);

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == dto.MemberId);
        if (member == null) return NotFound("Member not found");

        try
        {
            var fe = await _service.RequestAsync(new HistoricalJoinFeeService.HistoricalJoinFeeRequest
            {
                GroupId = dto.GroupId,
                MemberId = dto.MemberId,
                Amount = dto.Amount,
                Reason = dto.Reason,
                RequestedBy = User.GetUserId(),
                OccurredAt = dto.OccurredAt ?? DateTime.UtcNow
            });

            return Ok(new { Message = fe.Status == FinancialEventStatus.Pending ? "Pending approval" : "Approved and posted", Event = fe });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("approve/{financialEventId}")]
    public async Task<IActionResult> Approve(Guid financialEventId, [FromBody] ApproveDto dto)
    {
        var fe = await _context.FinancialEvents.FirstOrDefaultAsync(e => e.Id == financialEventId);
        if (fe == null) return NotFound("Event not found");

        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), fe.GroupId, g => g.ImportApproval);

        var approverMember = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == fe.GroupId && m.UserId == User.GetUserId());
        if (approverMember == null) return Forbid();

        try
        {
            var approved = await _service.ApproveAsync(financialEventId, approverMember.Id, dto.Note);
            return Ok(new { Message = "Approved and ledger entry created", Event = approved });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("reject/{financialEventId}")]
    public async Task<IActionResult> Reject(Guid financialEventId, [FromBody] ApproveDto dto)
    {
        var fe = await _context.FinancialEvents.FirstOrDefaultAsync(e => e.Id == financialEventId);
        if (fe == null) return NotFound("Event not found");

        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), fe.GroupId, g => g.ImportApproval);

        var approverMember = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == fe.GroupId && m.UserId == User.GetUserId());
        if (approverMember == null) return Forbid();

        var rejected = await _service.RejectAsync(financialEventId, approverMember.Id, dto.Note);
        return Ok(new { Message = "Rejected", Event = rejected });
    }

    [HttpGet("pending/{groupId}")]
    public async Task<IActionResult> GetPending(Guid groupId)
    {
        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), groupId, g => g.ImportApproval);
        var pending = await _service.GetPendingAsync(groupId);
        return Ok(pending);
    }

    public class RequestDto
    {
        public Guid GroupId { get; set; }
        public Guid MemberId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "";
        public DateTime? OccurredAt { get; set; }
    }

    public class ApproveDto
    {
        public string? Note { get; set; }
    }
}
