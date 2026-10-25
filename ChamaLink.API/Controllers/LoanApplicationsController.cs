using ChamaLink.API.Extensions;
using ChamaLink.Infrastructure.Services.Loans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

/// <summary>
/// SASISHO15 (AWAMU L): njia PEKEE ya kutoa mkopo — ombi → kura za viongozi huru → Mtunza-hazina anaanzisha →
/// kiongozi mwingine anathibitisha kwa rejea ya utoaji wa M-Koba. Sheria zote zinakaguliwa ndani ya LoanApplicationService.
/// </summary>
[Authorize]
[ApiController]
[Route("api/loan-applications/{groupId:guid}")]
public class LoanApplicationsController : ControllerBase
{
    private readonly LoanApplicationService _svc;
    public LoanApplicationsController(LoanApplicationService svc) => _svc = svc;

    public sealed record VoteRequest(bool Approve, string? Reason);
    public sealed record ConfirmRequest(string MkobaReference, bool AcknowledgeOtherRecipient, string? Note);
    public sealed record ReasonRequest(string Reason);
    public sealed record DecisionRequest(bool Approve, string? Note);

    [HttpGet("policy")]
    public async Task<IActionResult> Policy(Guid groupId, CancellationToken ct) => Ok(await _svc.PolicyAsync(groupId, User.GetUserId(), ct));

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(Guid groupId, [FromBody] LoanApplicationService.ApplyDto dto, CancellationToken ct)
        => Ok(await _svc.PreviewAsync(groupId, dto, User.GetUserId(), ct));

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct) => Ok(await _svc.ListAsync(groupId, User.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid id, CancellationToken ct) => Ok(await _svc.GetAsync(groupId, id, User.GetUserId(), ct));

    [HttpPost]
    public async Task<IActionResult> Apply(Guid groupId, [FromBody] LoanApplicationService.ApplyDto dto, CancellationToken ct)
    {
        var a = await _svc.ApplyAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { a.Id, a.ApplicationNumber, status = a.Status.ToString() });
    }

    [HttpPost("{id:guid}/vote")]
    public async Task<IActionResult> Vote(Guid groupId, Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
        var a = await _svc.VoteAsync(groupId, id, req.Approve, req.Reason, User.GetUserId(), ct);
        return Ok(new { a.Id, status = a.Status.ToString(), a.CloseReason });
    }

    [HttpPost("{id:guid}/initiate-disbursement")]
    public async Task<IActionResult> InitiateDisbursement(Guid groupId, Guid id, CancellationToken ct)
    {
        var a = await _svc.InitiateDisbursementAsync(groupId, id, User.GetUserId(), ct);
        return Ok(new { a.Id, status = a.Status.ToString() });
    }

    [HttpPost("{id:guid}/confirm-disbursement")]
    public async Task<IActionResult> ConfirmDisbursement(Guid groupId, Guid id, [FromBody] ConfirmRequest req, CancellationToken ct)
    {
        var a = await _svc.ConfirmDisbursementAsync(groupId, id, req.MkobaReference, req.AcknowledgeOtherRecipient, req.Note, User.GetUserId(), ct);
        return Ok(new { a.Id, status = a.Status.ToString(), a.LoanId, a.MkobaReference });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid groupId, Guid id, [FromBody] ReasonRequest req, CancellationToken ct)
    {
        var a = await _svc.CancelAsync(groupId, id, req.Reason, User.GetUserId(), ct);
        return Ok(new { a.Id, status = a.Status.ToString() });
    }

    [HttpGet("loans")]
    public async Task<IActionResult> Loans(Guid groupId, CancellationToken ct) => Ok(await _svc.LoansAsync(groupId, User.GetUserId(), ct));

    [HttpPost("loans/{loanId:guid}/default")]
    public async Task<IActionResult> RequestDefault(Guid groupId, Guid loanId, [FromBody] ReasonRequest req, CancellationToken ct)
    {
        var l = await _svc.RequestDefaultAsync(groupId, loanId, req.Reason, User.GetUserId(), ct);
        return Ok(new { l.Id, status = l.Status.ToString() });
    }

    [HttpPost("loans/{loanId:guid}/default/decide")]
    public async Task<IActionResult> DecideDefault(Guid groupId, Guid loanId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var l = await _svc.DecideDefaultAsync(groupId, loanId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { l.Id, status = l.Status.ToString() });
    }
}
