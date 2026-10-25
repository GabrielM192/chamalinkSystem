using ChamaLink.API.Extensions;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Benefits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChamaLink.API.Controllers;

/// <summary>
/// MAFAO (msiba/sherehe). Ruhusa za kina (maker-checker, nani anapiga kura, nani anaona cheti) zinakaguliwa ndani
/// ya BenefitService, ili kila njia ya kuingia (API, test, job) ifuate sheria zile zile.
///   Wanachama wote: kuona kesi, kanuni, tahadhari, daftari la utoaji wa M-Koba; kupiga kura wakiruhusiwa na sera.
///   Viongozi: kupendekeza/kuidhinisha sera na viwango, kufungua kesi, kuhakiki ushahidi, kuthibitisha malipo.
/// </summary>
[Authorize]
[ApiController]
[Route("api/benefits/{groupId:guid}")]
public class BenefitsController : ControllerBase
{
    private readonly BenefitService _benefits;
    private readonly GroupAuthorizationService _groupAuth;

    public BenefitsController(BenefitService benefits, GroupAuthorizationService groupAuth)
    {
        _benefits = benefits;
        _groupAuth = groupAuth;
    }

    public sealed record DecisionRequest(bool Approve, string? Note);

    // ---------------- Sera + viwango ----------------
    [HttpGet("policy")]
    public async Task<IActionResult> Policy(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.PolicyViewAsync(groupId, User.GetUserId(), ct));

    [HttpPost("policy/governance")]
    public async Task<IActionResult> ProposeGovernance(Guid groupId, [FromBody] BenefitService.GovernanceDto dto, CancellationToken ct)
    {
        var g = await _benefits.ProposeGovernanceAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { g.Id, status = g.Status.ToString() });
    }

    [HttpPost("policy/governance/{proposalId:guid}/decide")]
    public async Task<IActionResult> DecideGovernance(Guid groupId, Guid proposalId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var g = await _benefits.DecideGovernanceAsync(groupId, proposalId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { g.Id, status = g.Status.ToString() });
    }

    [HttpPost("policy/governance/{proposalId:guid}/cancel")]
    public async Task<IActionResult> CancelGovernance(Guid groupId, Guid proposalId, CancellationToken ct)
    {
        await _benefits.CancelGovernanceProposalAsync(groupId, proposalId, User.GetUserId(), ct);
        return NoContent();
    }

    [HttpPost("policy/rates")]
    public async Task<IActionResult> ProposeRate(Guid groupId, [FromBody] BenefitService.RateDto dto, CancellationToken ct)
    {
        var r = await _benefits.ProposeRateAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }

    [HttpPost("policy/rates/{rateId:guid}/decide")]
    public async Task<IActionResult> DecideRate(Guid groupId, Guid rateId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var r = await _benefits.DecideRateAsync(groupId, rateId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }

    [HttpPost("policy/rates/{rateId:guid}/cancel")]
    public async Task<IActionResult> CancelRate(Guid groupId, Guid rateId, CancellationToken ct)
    {
        await _benefits.CancelRateProposalAsync(groupId, rateId, User.GetUserId(), ct);
        return NoContent();
    }

    // ---------------- Kesi ----------------
    [HttpGet("cases")]
    public async Task<IActionResult> Cases(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.ListCasesAsync(groupId, User.GetUserId(), ct));

    [HttpGet("cases/{caseId:guid}")]
    public async Task<IActionResult> Case(Guid groupId, Guid caseId, CancellationToken ct)
        => Ok(await _benefits.CaseDetailAsync(groupId, caseId, User.GetUserId(), ct));

    /// <summary>Ukaguzi wa ustahiki + hesabu KABLA ya kufungua kesi (haihifadhi chochote).</summary>
    [HttpPost("cases/preview")]
    public async Task<IActionResult> Preview(Guid groupId, [FromBody] BenefitService.OpenCaseDto dto, CancellationToken ct)
    {
        await _benefits.RequireCanOpenForAsync(groupId, dto.BeneficiaryMemberId, User.GetUserId(), ct); // kiongozi: yeyote; mwanachama: yeye mwenyewe
        var a = await _benefits.AssessAsync(groupId, dto, null, ct);
        return Ok(new
        {
            eligible = a.Eligible, checks = a.Checks, perMemberAmount = a.PerMemberAmount, headcount = a.Headcount, gross = a.Gross,
            joinFeeDeducted = a.JoinFeeDeducted, arrearsDeducted = a.ArrearsDeducted, loanArrearsDeducted = a.LoanArrearsDeducted, net = a.Net, headcountRule = a.HeadcountRule,
            headcountDate = a.HeadcountDate,
            acceptedEvidence = a.Rate is null ? new List<string>() : BenefitService.ParseEvidence(a.Rate.AcceptedEvidence).Select(e => e.ToString()).ToList()
        });
    }

    [HttpPost("cases")]
    public async Task<IActionResult> Open(Guid groupId, [FromBody] BenefitService.OpenCaseDto dto, CancellationToken ct)
    {
        var c = await _benefits.OpenCaseAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { c.Id, c.CaseNumber, status = c.Status.ToString() });
    }

    [HttpPost("cases/historical")]
    public async Task<IActionResult> Historical(Guid groupId, [FromBody] BenefitService.HistoricalCaseDto dto, CancellationToken ct)
    {
        var c = await _benefits.RecordHistoricalCaseAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { c.Id, c.CaseNumber, status = c.Status.ToString() });
    }

    [HttpPost("cases/{caseId:guid}/historical/decide")]
    public async Task<IActionResult> DecideHistorical(Guid groupId, Guid caseId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var c = await _benefits.DecideHistoricalAsync(groupId, caseId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { c.Id, status = c.Status.ToString() });
    }

    /// <summary>multipart: file + documentType + documentNumber. PDF/JPG/PNG tu (inakaguliwa kwa maudhui, si jina).</summary>
    [HttpPost("cases/{caseId:guid}/evidence")]
    [EnableRateLimiting("upload")] // SASISHO17 (P1-9)
    [ChamaLink.API.Filters.ConfiguredRequestSizeLimit("Benefits:MaxEvidenceBytes")] // SASISHO17 (P1-8): kutoka config
    public async Task<IActionResult> Upload(Guid groupId, Guid caseId, IFormFile file, [FromForm] EvidenceDocumentType documentType,
        [FromForm] string documentNumber, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ChamaLink.Domain.Exceptions.ValidationException("Weka faili la cheti (PDF, JPG au PNG).");
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var e = await _benefits.UploadEvidenceAsync(groupId, caseId, documentType, documentNumber, file.FileName, ms.ToArray(), User.GetUserId(), ct);
        return Ok(new { e.Id, status = e.Status.ToString(), e.Sha256 });
    }

    [HttpPost("evidence/{evidenceId:guid}/verify")]
    public async Task<IActionResult> Verify(Guid groupId, Guid evidenceId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var (c, warning) = await _benefits.VerifyEvidenceWithWarningAsync(groupId, evidenceId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { caseId = c.Id, status = c.Status.ToString(), warning = warning is { } w ? new { message = w.Text, tpl = w.Template, args = w.Args } : null });
    }

    /// <summary>Faili la cheti — viongozi na mnufaika tu; kila kufunguliwa kunarekodiwa kwenye audit.</summary>
    [HttpGet("evidence/{evidenceId:guid}/file")]
    public async Task<IActionResult> EvidenceFile(Guid groupId, Guid evidenceId, CancellationToken ct)
    {
        var (e, bytes) = await _benefits.ReadEvidenceAsync(groupId, evidenceId, User.GetUserId(), ct);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, e.ContentType, e.FileName);
    }

    public sealed record VoteRequest(bool Approve, string? Reason);

    [HttpPost("cases/{caseId:guid}/vote")]
    public async Task<IActionResult> Vote(Guid groupId, Guid caseId, [FromBody] VoteRequest req, CancellationToken ct)
    {
        var r = await _benefits.VoteAsync(groupId, caseId, req.Approve, req.Reason, User.GetUserId(), ct);
        return Ok(new { status = r.Case.Status.ToString(), r.Approvals, r.Rejections, r.Required });
    }

    public sealed record InitiatePaymentRequest(string? Note);

    /// <summary>Hatua ya 1 ya malipo: mweka-malipo (nafasi kutoka sera) anahakiki pesa ipo na kuanzisha malipo.</summary>
    [HttpPost("cases/{caseId:guid}/initiate-payment")]
    public async Task<IActionResult> InitiatePayment(Guid groupId, Guid caseId, [FromBody] InitiatePaymentRequest req, CancellationToken ct)
    {
        var c = await _benefits.InitiatePaymentAsync(groupId, caseId, req.Note, User.GetUserId(), ct);
        return Ok(new { c.Id, status = c.Status.ToString(), c.PaymentInitiatedAt, c.PaymentInitiatedToPhone, c.CashAvailableAtInitiation });
    }

    public sealed record ConfirmPaymentRequest(string MkobaReference, bool AcknowledgeOtherRecipient = false, string? Note = null);

    /// <summary>Hatua ya 2 ya malipo: mtu TOFAUTI na aliyeanzisha anathibitisha kwa rejea ya M-Koba.</summary>
    [HttpPost("cases/{caseId:guid}/confirm-payment")]
    public async Task<IActionResult> ConfirmPayment(Guid groupId, Guid caseId, [FromBody] ConfirmPaymentRequest req, CancellationToken ct)
    {
        var c = await _benefits.ConfirmPaymentAsync(groupId, caseId, req.MkobaReference, User.GetUserId(), ct, req.AcknowledgeOtherRecipient, req.Note);
        return Ok(new { c.Id, status = c.Status.ToString(), c.MkobaReference, c.PaidAt });
    }

    /// <summary>SASISHO14: kesi inayosubiri pesa (AwaitingFunds) — kiongozi anakagua upya baada ya PDF mpya ya M-Koba.</summary>
    [HttpPost("cases/{caseId:guid}/recheck-funds")]
    public async Task<IActionResult> RecheckFunds(Guid groupId, Guid caseId, CancellationToken ct)
    {
        var c = await _benefits.RecheckFundsAsync(groupId, caseId, User.GetUserId(), ct);
        return Ok(new { c.Id, status = c.Status.ToString() });
    }

    public sealed record CancelRequest(string Reason);

    [HttpPost("cases/{caseId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid groupId, Guid caseId, [FromBody] CancelRequest req, CancellationToken ct)
    {
        var c = await _benefits.CancelCaseAsync(groupId, caseId, req.Reason, User.GetUserId(), ct);
        return Ok(new { c.Id, status = c.Status.ToString() });
    }

    // ---------------- M-Koba: utoaji + maelezo + tahadhari ----------------
    [HttpGet("withdrawals")]
    public async Task<IActionResult> Withdrawals(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireMembershipAsync(User.GetUserId(), groupId);
        return Ok(await _benefits.WithdrawalsAsync(groupId, User.GetUserId(), ct));
    }

    public sealed record ExplainRequest(string MkobaReference, WithdrawalExplanationKind Kind, string Description);

    [HttpPost("withdrawals/explain")]
    public async Task<IActionResult> Explain(Guid groupId, [FromBody] ExplainRequest req, CancellationToken ct)
    {
        var x = await _benefits.ExplainWithdrawalAsync(groupId, req.MkobaReference, req.Kind, req.Description, User.GetUserId(), ct);
        return Ok(new { x.Id, status = x.Status.ToString() });
    }

    [HttpPost("withdrawals/explanations/{id:guid}/decide")]
    public async Task<IActionResult> DecideExplanation(Guid groupId, Guid id, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var x = await _benefits.DecideExplanationAsync(groupId, id, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { x.Id, status = x.Status.ToString() });
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireMembershipAsync(User.GetUserId(), groupId);
        return Ok(await _benefits.AlertsAsync(groupId, ct));
    }

    // ---------------- Daftari + taarifa ya mwanachama ----------------
    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(Guid groupId, CancellationToken ct)
    {
        await _benefits.RequireLeaderOrAuditorAsync(groupId, User.GetUserId(), ct);
        return Ok(await _benefits.LedgerAsync(groupId, ct));
    }

    [HttpGet("ledger.csv")]
    public async Task<IActionResult> LedgerCsv(Guid groupId, CancellationToken ct)
    {
        await _benefits.RequireLeaderOrAuditorAsync(groupId, User.GetUserId(), ct);
        return File(await _benefits.LedgerCsvAsync(groupId, ct), "text/csv; charset=utf-8", $"daftari-la-mafao-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.MyBenefitsAsync(groupId, User.GetUserId(), ct));

    // ---------------- SASISHO13: uwazi, ulinganisho, hatari, ukaguzi, uhusiano ----------------
    /// <summary>Daftari la uwazi — KILA mwanachama (bila simu wala vyeti).</summary>
    [HttpGet("transparency")]
    public async Task<IActionResult> Transparency(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.TransparencyAsync(groupId, User.GetUserId(), ct));

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.ReconciliationAsync(groupId, User.GetUserId(), ct));

    [HttpGet("risk")]
    public async Task<IActionResult> Risk(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.RiskFlagsAsync(groupId, User.GetUserId(), ct));

    [HttpGet("audit/verify")]
    public async Task<IActionResult> VerifyAudit(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.VerifyAuditChainAsync(groupId, User.GetUserId(), ct));

    [HttpGet("cash")]
    public async Task<IActionResult> Cash(Guid groupId, CancellationToken ct)
    {
        await _benefits.RequireLeaderOrAuditorAsync(groupId, User.GetUserId(), ct);
        return Ok(await _benefits.CashPositionAsync(groupId, null, 0m, ct));
    }

    [HttpGet("relations")]
    public async Task<IActionResult> Relations(Guid groupId, CancellationToken ct)
        => Ok(await _benefits.RelationsAsync(groupId, User.GetUserId(), ct));

    [HttpPost("relations")]
    public async Task<IActionResult> AddRelation(Guid groupId, [FromBody] BenefitService.RelationDto dto, CancellationToken ct)
    {
        var r = await _benefits.AddRelationAsync(groupId, dto, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }

    public sealed record ReasonRequest(string Reason);

    /// <summary>SASISHO15: kiongozi wa pili anaidhinisha/kukataa uhusiano mpya (aliyeomba anaweza kughairi kwa approve=false).</summary>
    [HttpPost("relations/{relationId:guid}/decide")]
    public async Task<IActionResult> DecideRelation(Guid groupId, Guid relationId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var r = await _benefits.DecideRelationAsync(groupId, relationId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }

    [HttpPost("relations/{relationId:guid}/end")]
    public async Task<IActionResult> RequestEndRelation(Guid groupId, Guid relationId, [FromBody] ReasonRequest req, CancellationToken ct)
    {
        var r = await _benefits.RequestEndRelationAsync(groupId, relationId, req.Reason, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }

    [HttpPost("relations/{relationId:guid}/end/decide")]
    public async Task<IActionResult> DecideEndRelation(Guid groupId, Guid relationId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var r = await _benefits.DecideEndRelationAsync(groupId, relationId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString() });
    }
}
