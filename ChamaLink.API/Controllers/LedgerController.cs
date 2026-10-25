using ChamaLink.API.Filters;
using Microsoft.AspNetCore.Authorization;
using ChamaLink.API.Extensions;
using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

// NOTE: issue-loan / repay-loan used to live here. Removed - they were a
// second, incomplete way to issue/repay a loan that duplicated the
// proper Loan feature (interest, due date, status, portfolio report).
// Use LoanController instead: POST /api/Loan/{groupId}/issue and
// POST /api/Loan/{loanId}/repay.
[LegacyEndpoint("/api/truth/{groupId}/manual-payments  |  /api/truth/member/{memberId}")]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class LedgerController : ControllerBase
{
    private readonly LedgerService _ledgerService;
    private readonly GroupAuthorizationService _groupAuth;
    private readonly MemberCounterSyncService _counterSync;
    private readonly ApplicationDbContext _context;

    public LedgerController(
        LedgerService ledgerService,
        GroupAuthorizationService groupAuth,
        MemberCounterSyncService counterSync,
        ApplicationDbContext context)
    {
        _ledgerService = ledgerService;
        _groupAuth = groupAuth;
        _counterSync = counterSync;
        _context = context;
    }

    // UKARABATI WA DATA (2026-09-16)
    //
    // Kabla ya kurekebisha bug ya counter, kila mchango uliorekodiwa kwa
    // mkono ulipuuza GroupMember.TotalContributionsCount na AdvanceBalance.
    // Kwa hiyo data iliyopo kwenye database tayari si sahihi - kurekebisha
    // code pekee hakurekebishi rekodi za zamani.
    //
    // Endpoint hii inahesabu upya counter za wanachama wote wa kikundi
    // kutoka kwenye ledger, na inarudisha ukubwa wa tatizo lililopatikana.
    //
    // Ni idempotent: uiite mara ngapi utakavyo, matokeo ni yale yale.
    // Inaruhusiwa kwa viongozi tu (Treasurer/Chairperson/Secretary) kwa
    // sababu inabadilisha rekodi za wanachama wote.
    [HttpPost("reconcile-counters/{groupId}")]
    public async Task<IActionResult> ReconcileCounters(Guid groupId)
    {
        await _groupAuth.RequireRoleAsync(
            User.GetUserId(), groupId,
            GroupRole.Treasurer, GroupRole.Chairperson, GroupRole.Secretary);

        var result = await _counterSync.ReconcileGroupAsync(groupId);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = result.HadDrift
                ? $"Counter za wanachama {result.MembersCorrected} zimerekebishwa kutoka kwenye ledger."
                : "Counter zote zilikuwa sawa na ledger - hakuna kilichorekebishwa.",
            membersChecked = result.MembersChecked,
            membersCorrected = result.MembersCorrected,
            countDrift = result.CountDrift,
            balanceDrift = result.BalanceDrift
        });
    }

    // SECURITY FIX (audit 3.7: "Contribution endpoint haina actor
    // separation"): recording a contribution changes another member's
    // financial record - restricted to the group's own Treasurer or
    // Secretary, the people actually trusted to collect/record money.
    [HttpPost("contribution")]
    public async Task<IActionResult> RecordContribution([FromBody] RecordContributionDto dto)
    {

        await _groupAuth.RequireRoleAsync(User.GetUserId(), dto.GroupId, GroupRole.Treasurer, GroupRole.Secretary, GroupRole.Chairperson);

        var result = await _ledgerService.RecordContributionAsync(dto);
        return Ok(result);

    }

    [HttpGet("summary/{groupId}")]
    public async Task<IActionResult> GetGroupSummary(Guid groupId)
    {

        await _groupAuth.RequireMembershipAsync(User.GetUserId(), groupId);

        var result = await _ledgerService.GetGroupSummaryAsync(groupId);
        return Ok(result);

    }

    // SECURITY FIX (audit Stage 1 #7): RequireMembershipAsync only proved
    // the caller belongs to *a* group - it never checked that the {userId}
    // whose statement was being requested was the caller themselves. Any
    // ordinary member could read any other member's full financial
    // statement just by changing the userId in the URL. Now it's either
    // your own statement, or you hold one of the group's financial
    // leadership roles.
    [HttpGet("statement/{groupId}/{userId}")]
    public async Task<IActionResult> GetMemberStatement(Guid groupId, Guid userId)
    {

        var caller = await _groupAuth.RequireMembershipAsync(User.GetUserId(), groupId);

        bool isOwnStatement = caller.UserId == userId;
        // SASISHO17: nafasi INAYOTUMIKA (hai + nje ya kipindi cha kusubiri), si Role ghafi
        bool isLeader = ChamaLink.Infrastructure.Services.Common.MemberAccess.IsEffectiveLeader(caller);

        if (!isOwnStatement && !isLeader)
        throw new UnauthorizedAccessException("Unaweza kuona statement yako mwenyewe pekee, isipokuwa kama wewe ni kiongozi wa kikundi.");

        var result = await _ledgerService.GetMemberStatementAsync(groupId, userId);
        return Ok(result);

    }
}
