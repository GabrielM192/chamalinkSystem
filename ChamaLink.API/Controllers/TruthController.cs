using ChamaLink.API.Extensions;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Ledger;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>
/// INJINI YA UKWELI.
///   Kusoma: mwanachama yeyote anaona taarifa YAKE ({groupId}/me, member/{id} yake) na kanuni (settings).
///           Taarifa za wanachama wote (group, members, monthly-report, cashbook, unattributed, gaps, adjustments) = viongozi.
///   Kuandika (viongozi tu: Mwenyekiti/Mtunza-hazina/Katibu) — kila kitu kinakaguliwa (AuditEvents, zinaandikwa na TruthSetupService
///   ndani ya SaveChanges ile ile ya kitendo):
///     settings, reset, import-mkoba, phones, manual-payments, adjustments, gaps
/// Taarifa HAZIHIFADHIWI — zinahesabiwa upya kila ombi kutoka data ghafi.
/// </summary>
[Authorize]
[ApiController]
[Route("api/truth")]
public class TruthController : ControllerBase
{
    private static readonly GroupRole[] Leaders = { GroupRole.Chairperson, GroupRole.Treasurer, GroupRole.Secretary };

    private readonly TruthLedgerService _truth;
    private readonly TruthSetupService _setup;
    private readonly GroupAuthorizationService _groupAuth;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _config;
    private readonly ResetRequestService _reset;
    private readonly StatementImportService _imports;

    public TruthController(TruthLedgerService truth, TruthSetupService setup, GroupAuthorizationService groupAuth,
        ApplicationDbContext context, IConfiguration config, ResetRequestService reset,
        StatementImportService imports)
    {
        _imports = imports;
        _config = config;
        _reset = reset;
        _truth = truth;
        _setup = setup;
        _groupAuth = groupAuth;
        _context = context;
    }

    // ============================ KUSOMA ============================
    private const string MemberForbidden = "Huna ruhusa ya kuona taarifa hii.";

    /// <summary>GET /api/truth/member/{memberId}?asOf=2026-08-05T23:59:59 (saa za EAT)</summary>
    [HttpGet("member/{memberId:guid}")]
    public async Task<IActionResult> Member(Guid memberId, [FromQuery] DateTime? asOf, CancellationToken ct)
    {
        var groupId = await _context.GroupMembers.Where(m => m.Id == memberId).Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);
        // SASISHO17 (NEW-8): memberId isiyokuwepo = jibu lilelile la "huna ruhusa" (haifichui kama GUID ipo)
        if (groupId is null) throw new ChamaLink.Domain.Exceptions.ForbiddenException(MemberForbidden);
        // Mwanachama anaona taarifa YAKE tu (hata kama ametoka — hesabu yake ya mwisho); viongozi HAI wanaona ya kila mwanachama
        // GUID ya kikundi kingine / ya mwanachama mwingine bila ruhusa → ujumbe ULE ULE (hakuna tofauti ya kuchunguza)
        bool viewerIsLeader;
        try
        {
            var me = await _groupAuth.RequireOwnRecordAsync(User.GetUserId(), groupId.Value);
            if (me.Id != memberId)
            {
                await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId.Value, Leaders);
                viewerIsLeader = true;
            }
            else viewerIsLeader = ChamaLink.Infrastructure.Services.Common.MemberAccess.IsEffectiveLeader(me);
        }
        catch (ChamaLink.Domain.Exceptions.ForbiddenException) { throw new ChamaLink.Domain.Exceptions.ForbiddenException(MemberForbidden); }
        return Ok(ToDto(await _truth.GetMemberTruthAsync(memberId, asOf, ct), includeAllocations: true, viewerIsLeader: viewerIsLeader));
    }

    /// <summary>GET /api/truth/group/{groupId}?asOf=... — jedwali linalolingana na Excel ya mtunza-hazina.</summary>
    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> Group(Guid groupId, [FromQuery] DateTime? asOf, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders); // taarifa za wanachama wote = viongozi tu
        var all = await _truth.GetGroupTruthAsync(groupId, asOf, ct);
        var un = await _truth.GetUnattributedAsync(groupId, ct);
        return Ok(new
        {
            groupId,
            asOf = all.FirstOrDefault()?.Statement.AsOf,
            members = all.Select(t => ToDto(t, includeAllocations: false, viewerIsLeader: true)),
            totals = new
            {
                acceptedFromMkoba = all.Sum(t => t.Statement.TotalAcceptedFromMkoba),
                acceptedManual = all.Sum(t => t.Statement.TotalAcceptedManual - t.Statement.TotalWelfareOffset),
                welfareOffset = all.Sum(t => t.Statement.TotalWelfareOffset),
                finesWaivedAmount = all.Sum(t => t.Statement.FinesWaivedAmount),
                contributionPaid = all.Sum(t => t.Statement.TotalContributionPaid),
                finePaid = all.Sum(t => t.Statement.TotalFinePaid),
                joinFeePaid = all.Sum(t => t.Statement.JoinFeePaid),
                joinFeeOutstanding = all.Sum(t => t.Statement.JoinFeeOutstanding),
                loanRepaid = all.Sum(t => t.Statement.TotalLoanRepaid),
                loanOutstanding = all.Sum(t => t.Statement.LoanOutstanding),
                savings = all.Sum(t => t.Statement.Savings),
                arrears = all.Sum(t => t.Statement.Arrears),
                unattributedMkobaMoney = un.Total,
                // Mapengo ya mnyororo wa balance ambayo bado hayajathibitishwa (yanaonyeshwa kama bango tofauti)
                statementGapCount = un.UnresolvedGaps.Count,
                statementGapAmount = un.UnresolvedGaps.Where(g => g.Amount > 0).Sum(g => g.Amount),
                statementGapOutflow = un.UnresolvedGaps.Where(g => g.Amount < 0).Sum(g => -g.Amount)
            }
        });
    }

    /// <summary>Pesa iliyoingia M-Koba bila mwenyewe kujulikana (namba haijawekwa / mstari umekosekana kwenye PDF).</summary>
    [HttpGet("{groupId:guid}/unattributed")]
    public async Task<IActionResult> Unattributed(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders); // taarifa za wanachama wote = viongozi tu
        var r = await _truth.GetUnattributedAsync(groupId, ct);
        return Ok(new
        {
            total = r.Total,
            depositsWithUnknownPhone = r.DepositsWithUnknownPhone.Select(t => new { t.Reference, t.Timestamp, t.Phone, t.PayerName, t.Amount }),
            statementGaps = r.UnresolvedGaps.Select(g => new { g.Id, g.AfterReference, g.BeforeReference, g.WindowStart, g.WindowEnd, g.Amount, status = g.ResolutionStatus?.ToString() ?? "Haijatatuliwa" })
        });
    }

    /// <summary>GET /api/truth/{groupId}/cashbook — daftari la fedha la M-Koba (ingia/toka/salio kwa mwezi).</summary>
    [HttpGet("{groupId:guid}/cashbook")]
    public async Task<IActionResult> Cashbook(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders); // taarifa za wanachama wote = viongozi tu
        return Ok(await _truth.GetCashbookAsync(groupId, ct));
    }

    /// <summary>
    /// GET /api/truth/{groupId}/monthly-report?year=2026&amp;month=7 — taarifa ya mwezi MMOJA kwa kila mwanachama
    /// (namba ya uanachama, simu, lengo, alicholipa, tarehe, hali, faini, deni) + maandishi tayari kwa WhatsApp.
    /// Kusoma tu: namba zote zinatoka kwenye Injini ya Ukweli ile ile.
    /// </summary>
    [HttpGet("{groupId:guid}/monthly-report")]
    public async Task<IActionResult> MonthlyReport(Guid groupId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        return Ok(await _truth.GetMonthlyReportAsync(groupId, year, month, ct));
    }

    /// <summary>GET /api/truth/{groupId}/me — taarifa ya ukweli ya mtumiaji aliyeingia (mwanachama yeyote).</summary>
    [HttpGet("{groupId:guid}/me")]
    public async Task<IActionResult> Me(Guid groupId, CancellationToken ct)
    {
        var me = await _groupAuth.RequireOwnRecordAsync(User.GetUserId(), groupId); // SASISHO17: taarifa yako tu, hali yoyote
        return Ok(ToDto(await _truth.GetMemberTruthAsync(me.Id, null, ct), includeAllocations: true,
            viewerIsLeader: ChamaLink.Infrastructure.Services.Common.MemberAccess.IsEffectiveLeader(me)));
    }

    /// <summary>GET /api/truth/{groupId}/members — orodha nyepesi ya wanachama (kwa fomu/uchaguzi).</summary>
    [HttpGet("{groupId:guid}/members")]
    public async Task<IActionResult> Members(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders); // taarifa za wanachama wote = viongozi tu
        // Namba zinazohesabu malipo pekee (zimeidhinishwa na hazijafungwa) — nyingine hazimfanyi mtu "ana simu"
        var phones = await _context.MemberPhones.Where(p => p.GroupId == groupId
                && p.Status == TruthApprovalStatus.Approved && p.ValidTo == null)
            .Select(p => new { p.GroupMemberId, p.Phone }).ToListAsync(ct);
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        return Ok(members
            .OrderBy(m => m.User != null ? m.User.FullName : m.MemberNumber)
            .Select(m => new
            {
                memberId = m.Id,
                fullName = m.User?.FullName ?? m.MemberNumber,
                memberNumber = m.MemberNumber,
                role = m.Role.ToString(),
                phones = phones.Where(p => p.GroupMemberId == m.Id).Select(p => p.Phone).ToList()
            }));
    }

    // SASISHO16: "welfare-deduction" (kikokotoo cha msiba/sherehe kwa kiasi chochote) IMEONDOLEWA. Ilikuwa njia ya
    // pili ya hesabu ya mafao, tofauti na kesi rasmi (BenefitService: sera → kiwango × wanaohesabiwa → makato →
    // kiasi halisi, kinachofungwa kura zinapoanza). Hesabu ya mafao sasa ina njia MOJA tu.

    // ============================ KUANDIKA (viongozi) ============================

    [HttpGet("{groupId:guid}/settings")]
    public async Task<IActionResult> GetSettings(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireMembershipAsync(User.GetUserId(), groupId);
        var p = await _context.TruthGroupSettings.FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        if (p is null) return NotFound(new { error = "Mipangilio bado haijawekwa. Tumia PUT /api/truth/{groupId}/settings." });
        return Ok(new
        {
            p.MonthlyContribution, p.LateFine, p.LateFinePercent, lateFineMode = p.LateFinePercent is null ? "Fixed" : "Percent", p.JoiningFee, p.DueDateDay, p.GracePeriodDays,
            p.FineOnPartialPayment, p.AutoDeductSavings, p.ContributionStartDate, p.ManualEntryCutoff, p.FoundingMembersCutoff, p.LoanLatePenaltyAmount,
            p.LoansEnabled, p.LoanInterestPercent, p.LoanRepaymentMonths, p.LoanMaxAmount, p.LoanMaxMultipleOfContributions, p.LoanMinMembershipMonths,
            p.LoanAllowWithArrears, p.LoanAllowMultipleActive, p.LoanMinCashReserve, p.LoanRequiredApprovals, p.LoanApprovalValidDays,
            p.RoleChangeApprovals, p.LeaderCoolingOffDays, p.CashStatementMaxAgeDays,
            // SASISHO18 — sera ya vikundi vinavyochangishana KILA TUKIO
            p.MemberMinimumBalance, p.EventTopUpWindowDays, p.OverdueEventFineAmount,
            p.MaxEventDeductionPerMemberPerMonth, p.MaxEventsPerMonth, p.BeneficiaryContributes,
            // SASISHO19 — kanuni za J3: faini -> neema -> hatari ya kuondolewa -> kurudi
            p.EventFineGraceDays, p.RemovalApprovalsRequired, p.ReinstatementFineMultiplier, p.ReinstatementRequiresJoiningFee,
            // S17: violezo KAMILI (si minyororo) ili vitafsirike; UI inajenga sentensi yenyewe kutoka DueDateDay/GracePeriodDays.
            kanuni = p.GracePeriodDays > 0
                ? $"Mchango wa mwezi M unatakiwa kufika tarehe {p.DueDateDay} ya mwezi M+1 saa {TruthPolicy.EndOfDay:hh\\:mm\\:ss} + siku {p.GracePeriodDays} za neema."
                : $"Mchango wa mwezi M unatakiwa kufika tarehe {p.DueDateDay} ya mwezi M+1 saa {TruthPolicy.EndOfDay:hh\\:mm\\:ss} (hakuna siku za neema — faini kuanzia tarehe {p.DueDateDay + 1})."
        });
    }

    [HttpPut("{groupId:guid}/settings")]
    public async Task<IActionResult> PutSettings(Guid groupId, [FromBody] TruthSetupService.SettingsDto dto, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var r = await _setup.ProposeSettingsAsync(groupId, dto, User.GetUserId(), null, ct);
        return Ok(new { applied = r.Applied, proposalId = r.ProposalId, ujumbe = r.Ujumbe });
    }

    /// <summary>GET /api/truth/{groupId}/settings/proposals — mapendekezo ya kubadilisha kanuni (viongozi).</summary>
    [HttpGet("{groupId:guid}/settings/proposals")]
    public async Task<IActionResult> SettingsProposals(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        return Ok(await _setup.SettingsProposalsAsync(groupId, User.GetUserId(), ct));
    }

    /// <summary>Kiongozi wa pili anaidhinisha/kukataa; aliyependekeza anaweza kughairi (approve=false).</summary>
    [HttpPost("{groupId:guid}/settings/proposals/{proposalId:guid}/decide")]
    public async Task<IActionResult> DecideSettings(Guid groupId, Guid proposalId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var p = await _setup.DecideSettingsAsync(groupId, proposalId, req.Approve, req.Note, User.GetUserId(), ct);
        return Ok(new { p.Id, status = p.Status.ToString() });
    }

    /// <summary>
    /// POST /api/truth/{groupId}/reset?dryRun=true — angalia kitakachofutwa (hakuna kinachofutwa).
    /// SASISHO17 (P0-6): kufuta KWELI kunapitia ombi + idhini ya kiongozi wa pili (reset/requests). dryRun=false hapa inakataliwa.
    /// </summary>
    [HttpPost("{groupId:guid}/reset")]
    public async Task<IActionResult> Reset(Guid groupId, [FromQuery] bool dryRun = true, [FromQuery] bool keepLoans = false, CancellationToken ct = default)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, GroupRole.Chairperson, GroupRole.Treasurer);
        if (!dryRun) throw new ValidationException("Kufuta data kunahitaji ombi la kiongozi mmoja na idhini ya kiongozi wa pili (Mipangilio → Kufuta data).");
        return Ok(await _setup.ResetGroupFinancialDataAsync(groupId, true, keepLoans, User.GetUserId(), ct));
    }

    public sealed record ResetRequestBody(bool KeepLoans, string? Confirm);
    public sealed record ResetApproveBody(string? Confirm);

    [HttpGet("{groupId:guid}/reset/requests")]
    public async Task<IActionResult> ResetRequests(Guid groupId, CancellationToken ct)
        => Ok(await _reset.ListAsync(groupId, User.GetUserId(), ct));

    [HttpPost("{groupId:guid}/reset/requests")]
    public async Task<IActionResult> RequestReset(Guid groupId, [FromBody] ResetRequestBody req, CancellationToken ct)
    {
        var r = await _reset.RequestAsync(groupId, req.KeepLoans, req.Confirm, User.GetUserId(), ct);
        return Ok(new { r.Id, status = r.Status.ToString(), r.ExpiresAt });
    }

    [HttpPost("{groupId:guid}/reset/requests/{requestId:guid}/approve")]
    public async Task<IActionResult> ApproveReset(Guid groupId, Guid requestId, [FromBody] ResetApproveBody req, CancellationToken ct)
        => Ok(await _reset.ApproveAsync(groupId, requestId, req.Confirm, User.GetUserId(), ct));

    [HttpPost("{groupId:guid}/reset/requests/{requestId:guid}/reject")]
    public async Task<IActionResult> RejectReset(Guid groupId, Guid requestId, CancellationToken ct)
    {
        await _reset.CancelAsync(groupId, requestId, User.GetUserId(), ct);
        return NoContent();
    }

    /// <summary>
    /// POST /api/truth/{groupId}/import-mkoba (multipart: file) — SASISHO17: inatengeneza RASIMU tu (haiingizi data).
    /// Kiongozi wa pili anaidhinisha kwa POST mkoba-imports/{id}/approve.
    /// </summary>
    [HttpPost("{groupId:guid}/import-mkoba")]
    [EnableRateLimiting("upload")]
    [ChamaLink.API.Filters.ConfiguredRequestSizeLimit("Mkoba:MaxUploadBytes")] // SASISHO17 (P1-8): kutoka config
    public async Task<IActionResult> ImportMkoba(Guid groupId, IFormFile file, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        if (file is null || file.Length == 0) throw new ValidationException("Weka faili la PDF la M-Koba.");
        if (file.Length > _imports.MaxUploadBytes) throw new ValidationException($"Faili ni kubwa mno (kikomo ni MB {_imports.MaxUploadBytes / (1024 * 1024)}).");
        await using var s = file.OpenReadStream();
        return Ok(await _imports.StageAsync(groupId, s, file.FileName, User.GetUserId(), ct));
    }

    /// <summary>Kikomo cha nje cha ombi zima (ulinzi wa Kestrel); kikomo halisi cha faili ni Mkoba:MaxUploadBytes.</summary>

    [HttpGet("{groupId:guid}/mkoba-imports")]
    public async Task<IActionResult> MkobaImports(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        return Ok(await _imports.ListAsync(groupId, User.GetUserId(), ct));
    }

    [HttpGet("{groupId:guid}/mkoba-imports/{importId:guid}")]
    public async Task<IActionResult> MkobaImport(Guid groupId, Guid importId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        return Ok(await _imports.GetAsync(groupId, importId, User.GetUserId(), ct));
    }

    [HttpGet("{groupId:guid}/mkoba-imports/{importId:guid}/file")]
    public async Task<IActionResult> MkobaImportFile(Guid groupId, Guid importId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var (bytes, name) = await _imports.FileAsync(groupId, importId, User.GetUserId(), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, "application/pdf", name);
    }

    [HttpPost("{groupId:guid}/mkoba-imports/{importId:guid}/approve")]
    public async Task<IActionResult> ApproveMkobaImport(Guid groupId, Guid importId, [FromBody] DecisionRequest? req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        return Ok(await _imports.ApproveAsync(groupId, importId, req?.Note, User.GetUserId(), ct));
    }

    /// <summary>Kiongozi mwingine anakataa (sababu lazima); aliyepakia anaghairi.</summary>
    [HttpPost("{groupId:guid}/mkoba-imports/{importId:guid}/reject")]
    public async Task<IActionResult> RejectMkobaImport(Guid groupId, Guid importId, [FromBody] DecisionRequest? req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        await _imports.RejectAsync(groupId, importId, req?.Note, User.GetUserId(), ct);
        return NoContent();
    }

    // ---- Namba za simu ----
    [HttpGet("{groupId:guid}/phones")]
    public async Task<IActionResult> Phones(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        return Ok(await _setup.PhoneOverviewAsync(groupId, User.GetUserId(), ct));
    }

    public sealed record AddPhoneRequest(Guid MemberId, string Phone, string? Note, DateTime? ValidFrom = null, DateTime? ValidTo = null);
    public sealed record EndPhoneRequest(DateTime ValidTo, string Reason);

    [HttpPost("{groupId:guid}/phones")]
    public async Task<IActionResult> AddPhone(Guid groupId, [FromBody] AddPhoneRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var r = await _setup.AddPhoneAsync(groupId, req.MemberId, req.Phone, User.GetUserId(), req.Note, ct, req.ValidFrom, req.ValidTo);
        return Ok(new { phoneId = r.Phone.Id, phone = r.Phone.Phone, status = r.Phone.Status.ToString(), r.MatchedInMkoba, r.MkobaNameSeen, r.DepositsFound, r.DepositsTotal, r.Ujumbe });
    }

    /// <summary>Kiongozi wa pili anaidhinisha/kukataa kuunganishwa kwa namba (maker-checker).</summary>
    [HttpPost("{groupId:guid}/phones/{phoneId:guid}/decide")]
    public async Task<IActionResult> DecidePhone(Guid groupId, Guid phoneId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var p = await _setup.DecidePhoneAsync(groupId, phoneId, req.Approve, User.GetUserId(), req.Note, ct);
        return Ok(new { phoneId = p.Id, status = p.Status.ToString() });
    }

    /// <summary>Ombi la kufunga namba kuanzia tarehe (historia inabaki; malipo ya baadaye hayahesabiwi kwake).</summary>
    [HttpPost("{groupId:guid}/phones/{phoneId:guid}/end")]
    public async Task<IActionResult> EndPhone(Guid groupId, Guid phoneId, [FromBody] EndPhoneRequest req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var p = await _setup.RequestEndPhoneAsync(groupId, phoneId, req.ValidTo, req.Reason, User.GetUserId(), ct);
        return Ok(new { phoneId = p.Id, pendingValidTo = p.PendingValidTo });
    }

    [HttpPost("{groupId:guid}/phones/{phoneId:guid}/end/decide")]
    public async Task<IActionResult> DecideEndPhone(Guid groupId, Guid phoneId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var p = await _setup.DecideEndPhoneAsync(groupId, phoneId, req.Approve, User.GetUserId(), req.Note, ct);
        return Ok(new { phoneId = p.Id, validTo = p.ValidTo });
    }

    [HttpDelete("{groupId:guid}/phones/{phoneId:guid}")]
    public async Task<IActionResult> RemovePhone(Guid groupId, Guid phoneId, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        await _setup.RemovePhoneAsync(groupId, phoneId, User.GetUserId(), ct);
        return NoContent();
    }

    // ---- Manual (kumbukumbu za kabla ya M-Koba / Kianzio cha mkononi) ----
    public sealed record ManualPaymentRequest(Guid MemberId, decimal Amount, DateTime OccurredAt, bool IsJoinFee, string Reason,
        EvidenceType? EvidenceType, string? EvidenceReference);
    public sealed record DecisionRequest(bool Approve, string? Note);

    public sealed record PreviewPaymentRequest(decimal Amount, DateTime OccurredAt, bool IsJoinFee);

    /// <summary>
    /// POST /api/truth/{groupId}/member/{memberId}/preview-payment — HAKIKISHO: kiongozi anaona malipo
    /// yatakavyogawanywa na hali ya mwanachama kabla/baada, KABLA ya kuwasilisha. Hakuna kinachohifadhiwa.
    /// </summary>
    [HttpPost("{groupId:guid}/member/{memberId:guid}/preview-payment")]
    public async Task<IActionResult> PreviewPayment(Guid groupId, Guid memberId, [FromBody] PreviewPaymentRequest req, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var r = await _setup.PreviewManualPaymentAsync(groupId, memberId, req.Amount, req.OccurredAt, req.IsJoinFee, ct);
        return Ok(new
        {
            r.Valid, r.Problems, r.Before, r.After, r.ChangedMonths, r.PendingRequestsForMember,
            newAllocations = r.NewAllocations.Select(a => new { a.Target, period = a.Period?.Label, a.Amount, a.Explanation })
        });
    }

    [HttpPost("{groupId:guid}/manual-payments")]
    public async Task<IActionResult> RequestManual(Guid groupId, [FromBody] ManualPaymentRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var fe = await _setup.RequestManualPaymentAsync(groupId, req.MemberId, req.Amount, req.OccurredAt, req.IsJoinFee, req.Reason, req.EvidenceType, req.EvidenceReference, User.GetUserId(), ct);
        return Ok(new { fe.Id, fe.Status, ujumbe = "Imehifadhiwa kama OMBI. Kiongozi MWINGINE aidhinishe ndipo ionekane kwenye taarifa." });
    }

    [HttpGet("{groupId:guid}/manual-payments")]
    public async Task<IActionResult> ListManual(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var me = User.GetUserId();
        var names = await Names(groupId, ct);
        var list = await _context.FinancialEvents
            .Where(e => e.GroupId == groupId && e.Source == EventSource.Manual
                        && (e.Type == FinancialEventType.JoiningFeePaid || e.Type == FinancialEventType.PaymentReceived))
            .OrderByDescending(e => e.CreatedAt).ToListAsync(ct);
        return Ok(list.Select(e => new
        {
            e.Id, e.MemberId,
            memberName = e.MemberId is { } mid && names.Members.TryGetValue(mid, out var mn) ? mn : "?",
            kind = e.Type == FinancialEventType.JoiningFeePaid ? "JoinFee" : "Payment",
            e.Amount, e.OccurredAt, e.CreatedAt,
            status = e.Status.ToString(),
            e.Reason,
            evidenceType = e.EvidenceType?.ToString(),
            e.EvidenceReference,
            createdByName = names.Users.TryGetValue(e.CreatedBy, out var cn) ? cn : "?",
            createdByMe = e.CreatedBy == me,
            approvedByName = e.ApprovedByGroupMemberId is { } am && names.Members.TryGetValue(am, out var an) ? an : null,
            e.ApprovedAt, e.ApprovalNote,
            canDecide = e.Status == FinancialEventStatus.Pending && e.CreatedBy != me
        }));
    }

    [HttpPost("{groupId:guid}/manual-payments/{eventId:guid}/decision")]
    public async Task<IActionResult> DecideManual(Guid groupId, Guid eventId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var fe = await _setup.DecideManualPaymentAsync(groupId, eventId, req.Approve, User.GetUserId(), actor.Id, req.Note, ct);
        return Ok(new { fe.Id, fe.Status });
    }

    // ---- Msamaha wa faini / nyongeza ya muda ----
    public sealed record AdjustmentRequest(Guid? MemberId, TruthAdjustmentKind Kind, int Year, int Month, int ExtraDays, string Reason);

    [HttpPost("{groupId:guid}/adjustments")]
    public async Task<IActionResult> RequestAdjustment(Guid groupId, [FromBody] AdjustmentRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var a = await _setup.RequestAdjustmentAsync(groupId, req.MemberId, req.Kind, req.Year, req.Month, req.ExtraDays, req.Reason, User.GetUserId(), ct);
        return Ok(AdjDto(a, await Names(groupId, ct)));
    }

    [HttpGet("{groupId:guid}/adjustments")]
    public async Task<IActionResult> ListAdjustments(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders); // taarifa za wanachama wote = viongozi tu
        var names = await Names(groupId, ct);
        var list = await _context.TruthAdjustments.Where(a => a.GroupId == groupId)
            .OrderByDescending(a => a.RequestedAt).ToListAsync(ct);
        return Ok(list.Select(a => AdjDto(a, names)));
    }

    [HttpPost("{groupId:guid}/adjustments/{id:guid}/decision")]
    public async Task<IActionResult> DecideAdjustment(Guid groupId, Guid id, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var a = await _setup.DecideAdjustmentAsync(groupId, id, req.Approve, User.GetUserId(), req.Note, ct);
        return Ok(AdjDto(a, await Names(groupId, ct)));
    }

    // ---- Mapengo ya PDF ----
    /// <summary>GET /api/truth/{groupId}/gaps — mapengo yote ya PDF na hali yake.</summary>
    [HttpGet("{groupId:guid}/gaps")]
    public async Task<IActionResult> Gaps(Guid groupId, CancellationToken ct)
    {
        await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders); // taarifa za wanachama wote = viongozi tu
        var me = User.GetUserId();
        var names = await Names(groupId, ct);
        var list = await _context.MkobaStatementGaps.Where(g => g.GroupId == groupId).OrderBy(g => g.WindowStart).ToListAsync(ct);
        var described = (await StatementGapDescriber.DescribeAsync(_context, groupId, list, ct)).ToDictionary(d => d.Id);
        return Ok(list.Select(g => new
        {
            g.Id, g.AfterReference, g.BeforeReference, g.WindowStart, g.WindowEnd, g.Amount,
            previous = described[g.Id].Previous,
            next = described[g.Id].Next,
            balanceAfterPrevious = described[g.Id].BalanceAfterPrevious,
            expectedBalanceBeforeNext = described[g.Id].ExpectedBalanceBeforeNext,
            title = described[g.Id].Title,
            message = described[g.Id].Message,
            possibleMemberIds = described[g.Id].PossibleMemberIds,
            status = g.ResolutionStatus?.ToString() ?? "Unresolved",
            assignedMemberId = g.AssignedGroupMemberId,
            assignedMemberName = g.AssignedGroupMemberId is { } m && names.Members.TryGetValue(m, out var n) ? n : null,
            g.Evidence,
            requestedByName = g.RequestedByUserId is { } r && names.Users.TryGetValue(r, out var rn) ? rn : null,
            approvedByName = g.ApprovedByUserId is { } ap && names.Users.TryGetValue(ap, out var apn) ? apn : null,
            canDecide = g.ResolutionStatus == TruthApprovalStatus.Pending && g.RequestedByUserId != me
        }));
    }

    public sealed record GapResolveRequest(Guid MemberId, string Evidence);

    [HttpPost("{groupId:guid}/gaps/{gapId:guid}/resolve")]
    public async Task<IActionResult> ResolveGap(Guid groupId, Guid gapId, [FromBody] GapResolveRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var g = await _setup.RequestGapResolutionAsync(groupId, gapId, req.MemberId, req.Evidence, User.GetUserId(), ct);
        return Ok(new { g.Id, g.Amount, status = g.ResolutionStatus?.ToString(), ujumbe = "Kiongozi MWINGINE athibitishe." });
    }

    [HttpPost("{groupId:guid}/gaps/{gapId:guid}/decision")]
    public async Task<IActionResult> DecideGap(Guid groupId, Guid gapId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var actor = await _groupAuth.RequireRoleAsync(User.GetUserId(), groupId, Leaders);
        var g = await _setup.DecideGapResolutionAsync(groupId, gapId, req.Approve, User.GetUserId(), req.Note, ct);
        return Ok(new { g.Id, status = g.ResolutionStatus?.ToString() ?? "Haijatatuliwa" });
    }

    // ------------------------------------------------------------------

    private sealed record NameMaps(Dictionary<Guid, string> Members, Dictionary<Guid, string> Users);

    private async Task<NameMaps> Names(Guid groupId, CancellationToken ct)
    {
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        return new NameMaps(
            members.ToDictionary(m => m.Id, m => m.User?.FullName ?? m.MemberNumber),
            members.GroupBy(m => m.UserId).ToDictionary(g => g.Key, g => g.First().User?.FullName ?? g.First().MemberNumber));
    }

    private object AdjDto(TruthAdjustmentRecord a, NameMaps names)
    {
        var me = User.GetUserId();
        return new
        {
            a.Id, a.GroupMemberId,
            memberName = a.GroupMemberId is { } m && names.Members.TryGetValue(m, out var n) ? n : "Wanachama wote",
            kind = a.Kind.ToString(), a.Year, a.Month, a.ExtraDays, a.Reason,
            status = a.Status.ToString(), a.RequestedAt,
            requestedByName = names.Users.TryGetValue(a.RequestedByUserId, out var rn) ? rn : "?",
            decidedByName = a.DecidedByUserId is { } d && names.Users.TryGetValue(d, out var dn) ? dn : null,
            a.DecidedAt,
            canDecide = a.Status == TruthApprovalStatus.Pending && a.RequestedByUserId != me
        };
    }


    /// <param name="viewerIsLeader">
    /// SASISHO16: mwanachama wa kawaida haoni taarifa za ubora wa data (dataQuality) wala sababu za ndani za
    /// malipo yaliyokataliwa — hizo ni za viongozi kufanyia kazi.
    /// </param>
    [NonAction]
    public static object ToDto(TruthLedgerService.MemberTruth t, bool includeAllocations, bool viewerIsLeader)
    {
        var s = t.Statement;
        return new
        {
            memberId = t.MemberId,
            memberNumber = t.MemberNumber,
            fullName = s.Member.Name,
            phones = t.Phones,
            asOf = s.AsOf,
            invariantOk = s.InvariantDifference == 0,
            totals = new
            {
                acceptedFromMkoba = s.TotalAcceptedFromMkoba,
                // F-L3: makato ya mafao yana sehemu yake (welfareOffset); acceptedManual = malipo ya mkononi TU
                acceptedManual = s.TotalAcceptedManual - s.TotalWelfareOffset,
                contributionPaid = s.TotalContributionPaid,
                fineCharged = s.TotalFineCharged,
                finePaid = s.TotalFinePaid,
                finesWaived = s.FinesWaivedCount,
                finesWaivedAmount = s.FinesWaivedAmount,
                joinFeeDue = s.JoinFeeDue,
                joinFeePaid = s.JoinFeePaid,
                joinFeeOutstanding = s.JoinFeeOutstanding,
                loanRepaid = s.TotalLoanRepaid,
                loanPenaltyPaid = s.TotalLoanPenaltyPaid,
                welfareOffset = s.TotalWelfareOffset,
                totalAccepted = s.TotalAccepted,
                loanOutstanding = s.LoanOutstanding,
                loanOverdue = s.LoanOverdue,
                savings = s.Savings,
                arrears = s.Arrears,
                currentDue = s.CurrentDue,
                totalDebtIncludingJoinFee = s.TotalDebtIncludingJoinFee,
                monthsInArrears = s.MonthsInArrears.Select(p => p.Label)
            },
            monthlyStatement = s.Periods.Select(p => new
            {
                month = p.Period.Label,
                deadline = p.Deadline,
                status = p.Status.ToString(),
                contributionDue = p.ContributionDue,
                contributionPaid = p.ContributionPaid,
                fineDue = p.FineDue,
                finePaid = p.FinePaid,
                fineWaived = p.FineWaived,
                fineWaivedAmount = p.FineWaivedAmount,
                extraGraceDays = p.ExtraGraceDays,
                loanDue = p.LoanDue,
                loanPaid = p.LoanPaid,
                coveredBySavings = p.CoveredBySavings,
                outstanding = p.Outstanding,
                excelCell = p.Status is PeriodStatus.Unpaid ? "*" : p.ExcelCell.ToString("0"),
                paidBy = p.PaidByReferences
            }),
            loans = s.Loans.Select(l => new { l.LoanId, l.DisbursedAt, l.TotalRepayable, l.Repaid, l.Outstanding, l.PenaltyCharged, l.PenaltyPaid, l.OverdueInstallments }),
            allocations = includeAllocations
                ? s.Allocations.Select(a => new { a.Reference, a.Timestamp, a.Target, period = a.Period?.Label, a.Amount, a.Explanation })
                : null,
            rejected = viewerIsLeader
                ? s.Rejected.Select(r => (object)new { r.Payment.Reference, r.Payment.Timestamp, r.Payment.Amount, r.Reason })
                : Array.Empty<object>(),
            notices = t.Notices.Where(n => viewerIsLeader || n.Kind == TruthLedgerService.TruthNotice.KindNotice)
                .Select(n => new { n.Code, n.Kind, n.Message, n.Tpl, n.Args }),
            warnings = t.Notices.Where(n => viewerIsLeader || n.Kind == TruthLedgerService.TruthNotice.KindNotice).Select(n => n.Message)
        };
    }
}
