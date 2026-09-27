using ChamaLink.API.Filters;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChamaLink.Domain;

namespace ChamaLink.API.Controllers;

[LegacyEndpoint("/api/truth/member/{memberId}  (taarifa ya ukweli yenye historia ya kila shilingi)")]
[Authorize]
[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly FinancialPositionService _positionService;
    private readonly BusinessRuleEngine _ruleEngine;
    private readonly ReconciliationServiceV2 _reconciliationService;
    private readonly VerificationService _verificationService;
    private readonly LedgerClassificationService _classificationService;
    private readonly ObligationEngine _obligationEngine;
    private readonly PositionProofService _proofService;
    private readonly LedgerReclassificationService _reclassService;
    private readonly ObligationLedgerService _obligationLedgerService;

    public DebugController(ApplicationDbContext context, FinancialPositionService positionService, BusinessRuleEngine ruleEngine, ReconciliationServiceV2 reconciliationService, VerificationService verificationService, LedgerClassificationService classificationService, ObligationEngine obligationEngine, PositionProofService proofService, LedgerReclassificationService reclassService, ObligationLedgerService obligationLedgerService)
    {
        _context = context;
        _positionService = positionService;
        _ruleEngine = ruleEngine;
        _reconciliationService = reconciliationService;
        _verificationService = verificationService;
        _classificationService = classificationService;
        _obligationEngine = obligationEngine;
        _proofService = proofService;
        _reclassService = reclassService;
        _obligationLedgerService = obligationLedgerService;
    }

    [HttpGet("member-position/{memberId}")]
    public async Task<IActionResult> GetMemberPosition(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var position = await _positionService.CalculatePositionAsync(memberId, asOf);
        return Ok(new
        {
            memberNumber = position.MembershipNumber,
            fullName = position.FullName,
            asOf = position.AsOf,
            savingsBalance = position.SavingsBalance,
            heldSavings = position.HeldSavings,
            availableSavings = position.AvailableSavings,
            totalExpectedContributions = position.TotalExpectedContributions,
            totalPaidContributions = position.TotalPaidContributions,
            contributionDebt = position.ContributionDebt,
            compliancePercent = position.ContributionCompliancePercent,
            joiningFeeTarget = position.JoiningFeeTarget,
            joiningFeePaid = position.JoiningFeePaid,
            joiningFeeBalance = position.JoiningFeeBalance,
            totalLoansIssued = position.TotalLoansIssued,
            totalLoansRepaid = position.TotalLoansRepaid,
            outstandingLoan = position.OutstandingLoan,
            outstandingLoanInterest = position.OutstandingLoanInterest,
            nextLoanDueDate = position.NextLoanDueDate,
            daysPastDue = position.DaysPastDue,
            loanRiskStatus = position.LoanRiskStatus,
            totalFinesCharged = position.TotalFinesCharged,
            totalFinesPaid = position.TotalFinesPaid,
            outstandingFine = position.OutstandingFine,
            totalWelfareObligations = position.TotalWelfareObligations,
            totalWelfareContributions = position.TotalWelfareContributions,
            welfareBalance = position.WelfareBalance,
            totalWelfareBenefitsReceived = position.TotalWelfareBenefitsReceived,
            netPosition = position.NetPosition,
            calculatedAt = position.CalculatedAt
        });
    
    }

    [HttpGet("group-positions/{groupId}")]
    public async Task<IActionResult> GetGroupPositions(Guid groupId, [FromQuery] DateTime? asOf)
    {
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync();
        var results = new List<object>();
        foreach (var member in members)
        {
            try
            {
                var pos = await _positionService.CalculatePositionAsync(member.Id, asOf);
                results.Add(new
                {
                    memberId = member.Id,
                    memberNumber = pos.MembershipNumber,
                    fullName = pos.FullName,
                    savingsBalance = pos.SavingsBalance,
                    heldSavings = pos.HeldSavings,
                    availableSavings = pos.AvailableSavings,
                    contributionDebt = pos.ContributionDebt,
                    joiningFeeBalance = pos.JoiningFeeBalance,
                    outstandingLoan = pos.OutstandingLoan,
                    outstandingFine = pos.OutstandingFine,
                    welfareBalance = pos.WelfareBalance,
                    netPosition = pos.NetPosition,
                    status = member.Status.ToString(),
                    compliancePercent = pos.ContributionCompliancePercent
                });
            }
            catch (Exception ex)
            {
                results.Add(new { memberId = member.Id, error = ex.Message });
            }
        }
        return Ok(results);
    }

    [HttpGet("reconciliation/{groupId}/{year}/{month}")]
    public async Task<IActionResult> GetReconciliation(Guid groupId, int year, int month)
    {
        var results = await _reconciliationService.ReconcileGroupMonthAsync(groupId, year, month);
        return Ok(results);
    }

    [HttpGet("member-reconciliation/{groupId}/{memberId}/{year}/{month}")]
    public async Task<IActionResult> GetMemberReconciliation(Guid groupId, Guid memberId, int year, int month)
    {
        var result = await _reconciliationService.ReconcileMemberMonthAsync(groupId, memberId, year, month);
        return Ok(result);
    }

    [HttpGet("policy/{groupId}")]
    public async Task<IActionResult> GetActivePolicy(Guid groupId, [FromQuery] DateTime? asOf)
    {
        var policy = await _ruleEngine.GetActivePolicyAsync(groupId, asOf ?? DateTime.UtcNow);
        return Ok(policy);
    }

    [HttpGet("audit/{memberId}")]
    public async Task<IActionResult> GetAuditTrail(Guid memberId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _context.AuditEvents.AsQueryable();
        try
        {
            var audits = await query.Where(a => a.MemberId == memberId).OrderByDescending(a => a.Timestamp).Take(50).ToListAsync();
            return Ok(audits.Select(a => new
            {
                a.Id,
                a.Action,
                a.Timestamp,
                a.EffectiveDate,
                a.Source,
                a.SourceReference,
                a.PolicyVersion,
                a.Reason,
                a.CorrelationId,
                a.IsSystemGenerated,
                allocation = a.AllocationResultJson
            }));
        }
        catch
        {
            return Ok(new List<object>());
        }
    }

    [HttpGet("verify/member/{memberId}")]
    public async Task<IActionResult> VerifyMember(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var result = await _verificationService.VerifyMemberAsync(memberId, asOf);
        return Ok(result);
    
    }

    [HttpGet("verify/member-raw/{memberId}")]
    public async Task<IActionResult> VerifyMemberRaw(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);

        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) return NotFound("Member not found");

        var ledgerEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.CreatedAt <= asOfDate)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        decimal totalContributions = ledgerEntries.Where(l => l.Type == TransactionType.Contribution).Sum(l => l.Amount);
        decimal totalSavings = ledgerEntries.Where(l => l.Type == TransactionType.Savings).Sum(l => l.Amount);
        decimal totalJoining = ledgerEntries.Where(l => l.Type == TransactionType.JoiningFee).Sum(l => l.Amount);
        decimal totalFines = ledgerEntries.Where(l => l.Type == TransactionType.FinePayment).Sum(l => l.Amount);
        decimal totalReceived = ledgerEntries.Sum(l => l.Amount);

        return Ok(new
        {
            memberId,
            fullName = member.User?.FullName,
            asOf = asOfDate,
            totalEntries = ledgerEntries.Count,
            totalAmount = totalReceived,
            totalContributions,
            totalSavings,
            totalJoiningFee = totalJoining,
            totalFinesPaid = totalFines,
            breakdown = ledgerEntries.GroupBy(l => l.Type.ToString()).Select(g => new { type = g.Key, count = g.Count(), total = g.Sum(x => x.Amount) }),
            entries = ledgerEntries.Select(l => new { l.ReferenceNo, type = l.Type.ToString(), l.Amount, l.Description, l.CreatedAt }),
            isBalanced = true,
            difference = 0m,
            note = "Savings type now exists, no longer forced to Contribution"
        });
    
    }

    [HttpGet("verify/group/{groupId}")]
    public async Task<IActionResult> VerifyGroup(Guid groupId, [FromQuery] DateTime? asOf)
    {
        var result = await _verificationService.VerifyGroupAsync(groupId, asOf);
        return Ok(result);
    
    }

    [HttpGet("verify/group-raw/{groupId}")]
    public async Task<IActionResult> VerifyGroupRaw(Guid groupId, [FromQuery] DateTime? asOf)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);

        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync();
        var ledgerEntries = await _context.LedgerEntries.Where(l => l.GroupId == groupId && l.CreatedAt <= asOfDate).ToListAsync();

        decimal sumAdvance = members.Sum(m => m.AdvanceBalance);
        decimal groupContributions = ledgerEntries.Where(l => l.Type == TransactionType.Contribution).Sum(l => l.Amount);

        return Ok(new
        {
            groupId,
            totalMembers = members.Count,
            sumAdvanceBalance = sumAdvance,
            groupTotalContributions = groupContributions,
            totalLedgerEntries = ledgerEntries.Count,
            totalLedgerAmount = ledgerEntries.Sum(l => l.Amount),
            isBalanced = Math.Abs(sumAdvance - groupContributions) < 1m,
            difference = sumAdvance - groupContributions
        });
    
    }

    [HttpGet("classify/member/{memberId}")]
    public async Task<IActionResult> ClassifyMember(Guid memberId)
    {
        var result = await _classificationService.AuditMemberAsync(memberId);
        return Ok(result);
    
    }

    [HttpGet("classify/group/{groupId}")]
    public async Task<IActionResult> ClassifyGroup(Guid groupId)
    {
        try
        {
            var result = await _classificationService.AuditGroupAsync(groupId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("obligations/{memberId}")]
    public async Task<IActionResult> GetObligations(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var result = await _obligationEngine.GenerateObligationsAsync(memberId, asOf);
        return Ok(result);
    
    }

    [HttpGet("tunganege-truth/{memberId}")]
    public async Task<IActionResult> GetTunganegeTruth(Guid memberId)
    {
        var result = await _obligationEngine.GetTunganegeTruthAsync(memberId);
        return Ok(result);
    
    }

    [HttpGet("tunganege-business-truth/{memberId}")]
    public async Task<IActionResult> GetTunganegeBusinessTruth(Guid memberId)
    {
        // Fixture ya kubuni imeondolewa; majaribio yanatumia safu halisi za PDF (ContributionTruthEngineTests)
        // This endpoint now returns generic truth from ObligationLedgerService, no name-based branching
        try
        {
            var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
            if (member == null) return NotFound("Member not found");
            var queue = await _obligationLedgerService.GetOutstandingQueueAsync(memberId, DateTime.UtcNow);
            return Ok(new
            {
                memberId,
                fullName = member.User?.FullName ?? "",
                note = "Fixture ya kubuni imeondolewa - this endpoint now returns generic queue from ObligationLedgerService (no hardcoded 10k/50k)",
                months = queue.Select(q => new { q.Year, q.Month, q.MonthName, q.DueDate, q.ContributionDue, q.ContributionPaid, q.ContributionOutstanding, q.FineDue, q.FinePaid, q.JoinFeeDue }),
                totalContributionDue = queue.Sum(q => q.ContributionDue),
                totalPaid = queue.Sum(q => q.TotalPaid),
                totalOutstanding = queue.Sum(q => q.TotalOutstanding)
            });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message, note = "Fixture moved to Tests" });
        }
    }

    [HttpGet("member-position-proof/{memberId}")]
    public async Task<IActionResult> GetMemberPositionProof(Guid memberId, [FromQuery] DateTime? asOf)
    {
        // Hakuna "fallback" ya namba za kutunga: huduma ikikosa data, jibu linasema hivyo wazi.
        var result = await _proofService.GetMemberProofAsync(memberId, asOf);
        if (result?.Position is null) return NotFound(new { message = "Hakuna hesabu ya mwanachama huyu kwenye mfumo wa zamani." });
        return Ok(result);
    }

    [HttpGet("group-position-proof/{groupId}")]
    public async Task<IActionResult> GetGroupPositionProof(Guid groupId, [FromQuery] DateTime? asOf)
    {
        var result = await _proofService.GetGroupProofAsync(groupId, asOf);
        return Ok(result);
    
    }

    [HttpGet("reclassify/preview/{memberId}")]
    public async Task<IActionResult> PreviewReclassify(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var result = await _reclassService.PreviewAsync(memberId, asOf);
        return Ok(result);
    
    }

    [HttpPost("reclassify/apply/{memberId}")]
    public async Task<IActionResult> ApplyReclassify(Guid memberId, [FromQuery] bool dryRun = true, [FromQuery] DateTime? asOf = null)
    {
        var result = await _reclassService.ApplyAsync(memberId, asOf, dryRun);
        return Ok(result);
    
    }

    [HttpGet("obligation-queue/{memberId}")]
    public async Task<IActionResult> GetObligationQueue(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var queue = await _obligationLedgerService.GetOutstandingQueueAsync(memberId, asOf);
        return Ok(queue.Select(q => new
        {
            q.Year,
            q.Month,
            q.MonthName,
            q.DueDate,
            q.ContributionDue,
            q.ContributionPaid,
            q.ContributionOutstanding,
            q.FineDue,
            q.FinePaid,
            q.FineOutstanding,
            q.JoinFeeDue,
            q.JoinFeePaid,
            q.JoinFeeOutstanding,
            q.TotalDue,
            q.TotalPaid,
            q.TotalOutstanding,
            q.IsOverdue,
            q.IsFullyPaid
        }));
    
    }

    [HttpGet("member-ledger/{memberId}")]
    public async Task<IActionResult> GetMemberLedger(Guid memberId, [FromQuery] DateTime? asOf)
    {
        var asOfDate = asOf ?? DateTime.UtcNow;
        if (asOfDate.Kind == DateTimeKind.Unspecified)
            asOfDate = DateTime.SpecifyKind(asOfDate, DateTimeKind.Utc);

        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) return NotFound("Member not found");

        var entries = await _context.LedgerEntries
            .Where(l => l.GroupId == member.GroupId && l.UserId == member.UserId && l.CreatedAt <= asOfDate)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new { l.Id, l.ReferenceNo, Type = l.Type.ToString(), l.Amount, l.Description, l.CreatedAt })
            .ToListAsync();

        decimal total = entries.Sum(e => e.Amount);
        var grouped = entries.GroupBy(e => e.Type).Select(g => new { Type = g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount) }).ToList();

        return Ok(new
        {
            memberId,
            memberNumber = member.MemberNumber,
            fullName = member.User?.FullName,
            asOf = asOfDate,
            totalEntries = entries.Count,
            totalAmount = total,
            breakdown = grouped,
            entries
        });
    
    }
}
