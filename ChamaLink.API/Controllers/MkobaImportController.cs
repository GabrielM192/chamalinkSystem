using ChamaLink.API.Filters;
using ChamaLink.API.Extensions;
using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

[LegacyEndpoint("/api/truth/{groupId}/import-mkoba")]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MkobaImportController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IMKobaParserService _parserService;
    private readonly AccountResolverService _accountResolver;
    private readonly FineService _fineService;
    private readonly DebtService _debtService;
    private readonly LoanService _loanService;
    private readonly GroupAuthorizationService _groupAuth;
    private readonly AllocationEngine _allocationEngine;
    private readonly BusinessRuleEngine _businessRuleEngine;
    private readonly FinancialPositionService _financialPositionService;
    private readonly ObligationLedgerService _obligationLedgerService;
    private readonly AuditService _auditService;

    public MkobaImportController(
        ApplicationDbContext context,
        IMKobaParserService parserService,
        AccountResolverService accountResolver,
        FineService fineService,
        DebtService debtService,
        LoanService loanService,
        GroupAuthorizationService groupAuth,
        AllocationEngine allocationEngine,
        BusinessRuleEngine businessRuleEngine,
        FinancialPositionService financialPositionService,
        ObligationLedgerService obligationLedgerService,
        AuditService auditService)
    {
        _context = context;
        _parserService = parserService;
        _accountResolver = accountResolver;
        _fineService = fineService;
        _debtService = debtService;
        _loanService = loanService;
        _groupAuth = groupAuth;
        _allocationEngine = allocationEngine;
        _businessRuleEngine = businessRuleEngine;
        _financialPositionService = financialPositionService;
        _obligationLedgerService = obligationLedgerService;
        _auditService = auditService;
    }

    [RequestSizeLimit(10 * 1024 * 1024)]
    [HttpPost("upload-statement/{groupId}")]
    public async Task<ActionResult<MKobaImportResultDto>> UploadStatement(Guid groupId, IFormFile file)
    {
        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), groupId, g => g.ImportApproval);

        if (file == null || file.Length == 0)
            return BadRequest("Please select M-Koba file.");

        const long maxBytes = 10 * 1024 * 1024;
        if (file.Length > maxBytes)
            return BadRequest($"File too large ({file.Length / (1024.0 * 1024.0):F1} MB). Max 10 MB.");

        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            fileBytes = ms.ToArray();
        }

        string fileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileBytes));
        bool alreadyImported = await _context.ImportedStatements.AnyAsync(s => s.GroupId == groupId && s.FileHash == fileHash);
        if (alreadyImported)
            return BadRequest("This statement was already uploaded for this group.");

        List<MKobaTransactionItemDto> transactions;
        using var stream = new MemoryStream(fileBytes);
        transactions = await _parserService.ParseStatementAsync(stream);

        if (!transactions.Any())
            return BadRequest("No transactions found in file.");

        var request = new MKobaImportRequestDto { GroupId = groupId, Transactions = transactions };
        var response = await ProcessTransactionsInternal(request);

        _context.ImportedStatements.Add(new ImportedStatement
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            FileHash = fileHash,
            FileName = file.FileName,
            TransactionCount = transactions.Count
        });
        await _context.SaveChangesAsync();

        return response;
    }

    private async Task<ActionResult<MKobaImportResultDto>> ProcessTransactionsInternal(MKobaImportRequestDto request)
    {
        var result = new MKobaImportResultDto { TotalSubmitted = request.Transactions.Count };

        var group = await _context.Groups.Include(g => g.Settings).FirstOrDefaultAsync(g => g.Id == request.GroupId);
        if (group == null) return BadRequest("Group not found.");

        var policy = await _businessRuleEngine.GetActivePolicyAsync(request.GroupId, DateTime.UtcNow);

        GroupEvent? activeEvent = null;
        if (group.Type == GroupType.EventBased || group.Type == GroupType.Hybrid)
        {
            activeEvent = await _context.GroupEvents.FirstOrDefaultAsync(e => e.GroupId == request.GroupId && e.IsActive);
        }

        // Allocation is chronological. Sorting here is required because the
        // imported PDF order is not a contract, while each later payment must
        // see the ledger entries created by earlier payments in this request.
        foreach (var item in request.Transactions
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.ReferenceNumber, StringComparer.Ordinal))
        {
            if (item.IsWithdrawal)
            {
                bool dup = await _context.WalletWithdrawals.AnyAsync(w => w.GroupId == request.GroupId && w.ReferenceNo == item.ReferenceNumber);
                if (dup)
                {
                    result.IgnoredDuplicates++;
                    result.Warnings.Add($"Ref {item.ReferenceNumber} withdrawal already exists.");
                    continue;
                }

                _context.WalletWithdrawals.Add(new WalletWithdrawal
                {
                    Id = Guid.NewGuid(),
                    GroupId = request.GroupId,
                    ReferenceNo = item.ReferenceNumber,
                    MemberName = item.MemberName,
                    PhoneNumber = item.PhoneNumber,
                    Amount = item.Amount,
                    TransactionDate = item.TransactionDate
                });
                result.WithdrawalsSkipped++;
                result.Warnings.Add($"Ref {item.ReferenceNumber} is withdrawal {item.Amount:N0} TZS - saved to withdrawal log.");
                continue;
            }

            bool isDuplicate = await _context.LedgerEntries.AnyAsync(l => l.GroupId == request.GroupId && l.ReferenceNo == item.ReferenceNumber);
            if (isDuplicate)
            {
                result.IgnoredDuplicates++;
                result.Warnings.Add($"Ref {item.ReferenceNumber} already exists.");
                continue;
            }

            string cleanPhone = item.PhoneNumber.Replace(" ", "").Replace("+", "");
            if (cleanPhone.StartsWith("0")) cleanPhone = MkobaPlatform.CountryDialCode + cleanPhone.Substring(1);

            var member = await _context.GroupMembers.Include(m => m.User)
                .FirstOrDefaultAsync(m => m.GroupId == request.GroupId && m.User != null &&
                    (m.User.PhoneNumber == cleanPhone || m.User.PhoneNumber.EndsWith(cleanPhone.Substring(Math.Max(0, cleanPhone.Length - 9)))));

            if (member == null)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == cleanPhone);
                if (user == null)
                {
                    string fullName = string.IsNullOrWhiteSpace(item.MemberName) ? $"Member {cleanPhone}" : item.MemberName;
                    user = new User { Id = Guid.NewGuid(), PhoneNumber = cleanPhone, FullName = fullName };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();
                }

                member = new GroupMember
                {
                    Id = Guid.NewGuid(),
                    GroupId = request.GroupId,
                    UserId = user.Id,
                    Status = MemberStatus.New,
                    TotalContributionsCount = 0,
                    JoinedAt = DateTime.UtcNow
                };
                _context.GroupMembers.Add(member);
                await _context.SaveChangesAsync();
            }

            var savingsAccount = await _accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.Savings);
            var eventAccount = await _accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.SocialFund);

            // Single source oldest-first allocation
            var position = await _financialPositionService.CalculatePositionAsync(member.Id, item.TransactionDate);
            var activeLoans = await _context.Loans.Where(l => l.GroupMemberId == member.Id && l.Status == LoanStatus.Active).ToListAsync();
            var outstandingQueue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(member.Id, policy, item.TransactionDate);

            var allocation = _allocationEngine.AllocateOldestFirst(
                paymentAmount: item.Amount,
                paymentDate: item.TransactionDate,
                outstandingQueue: outstandingQueue,
                policy: policy,
                activeLoans: activeLoans,
                year: item.TransactionDate.Year,
                month: item.TransactionDate.Month);

            var correlationId = allocation.CorrelationId;

            var paymentEvent = new FinancialEvent
            {
                Id = Guid.NewGuid(),
                GroupId = request.GroupId,
                MemberId = member.Id,
                Type = FinancialEventType.PaymentReceived,
                Amount = item.Amount,
                OccurredAt = item.TransactionDate,
                CreatedBy = member.UserId,
                Source = EventSource.MKobaImport,
                SourceReference = item.ReferenceNumber,
                PolicyVersion = policy.Version,
                CorrelationId = correlationId
            };
            _context.FinancialEvents.Add(paymentEvent);

            // Late fine check
            if (group.Type == GroupType.MonthlySavings || group.Type == GroupType.Hybrid)
            {
                DateTime periodStart = new DateTime(item.TransactionDate.Year, item.TransactionDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                bool alreadyContributed = await _context.LedgerEntries.AnyAsync(l =>
                    l.GroupId == request.GroupId && l.UserId == member.UserId &&
                    l.Type == TransactionType.Contribution && l.CreatedAt >= periodStart && l.CreatedAt < periodStart.AddMonths(1));
                bool alreadyFined = await _context.Fines.AnyAsync(f =>
                    f.GroupMemberId == member.Id && f.ReasonType == FineReasonType.LateMonthlyContribution && f.Period == periodStart);

                int dueDay = policy.DueDateDay;
                int grace = policy.GracePeriodDays;
                var contributionDueDate = new DateTime(
                    item.TransactionDate.Year,
                    item.TransactionDate.Month,
                    1,
                    0, 0, 0,
                    DateTimeKind.Utc)
                    .AddMonths(1)
                    .AddDays(dueDay - 1)
                    .AddDays(grace);
                bool isLate = item.TransactionDate > contributionDueDate;

                if (!alreadyContributed && !alreadyFined && isLate)
                {
                    if (_businessRuleEngine.ShouldChargeFine(policy.MonthlyContribution, position.AvailableSavings, policy))
                    {
                        await _fineService.IssueFineAsync(request.GroupId, member.Id, member.UserId, policy.LateFine,
                            FineReasonType.LateMonthlyContribution, $"Late contribution {periodStart:MMMM yyyy}", period: periodStart);
                        result.Warnings.Add($"{member.User?.FullName ?? item.MemberName}: late fine {policy.LateFine:N0} issued for {periodStart:MMMM yyyy}.");
                    }
                }
            }

            // Create ledger entries from allocation - unique RefNo per month to allow one tx to pay multiple months
            int allocIndex = 0;
            foreach (var alloc in allocation.Allocations)
            {
                allocIndex++;
                if (alloc.Target == "SavingsCover") continue;

                string refNo = alloc.Target switch
                {
                    "Contribution" => alloc.Year > 0 ? $"{item.ReferenceNumber}-CONTRIB-{alloc.Year:0000}-{alloc.Month:00}" : (allocIndex == 1 ? item.ReferenceNumber : $"{item.ReferenceNumber}-CONTRIB-{allocIndex}"),
                    "LoanRepayment" => $"{item.ReferenceNumber}-REJESHO-{alloc.LoanId}-{alloc.Year:0000}-{alloc.Month:00}",
                    "Fine" => alloc.Year > 0 ? $"{item.ReferenceNumber}-FINE-{alloc.Year:0000}-{alloc.Month:00}" : $"{item.ReferenceNumber}-FINE-{allocIndex}",
                    "JoiningFee" => alloc.Year > 0 ? $"{item.ReferenceNumber}-KIANZIO-{alloc.Year:0000}-{alloc.Month:00}" : $"{item.ReferenceNumber}-KIANZIO",
                    "Debt" => $"{item.ReferenceNumber}-DENI-{allocIndex}",
                    "Savings" => $"{item.ReferenceNumber}-AKIBA",
                    _ => $"{item.ReferenceNumber}-{alloc.Target}-{allocIndex}"
                };

                bool exists = await _context.LedgerEntries.AnyAsync(l => l.GroupId == request.GroupId && l.ReferenceNo == refNo);
                if (exists)
                {
                    result.IgnoredDuplicates++;
                    continue;
                }

                if (alloc.Target == "LoanRepayment" && alloc.LoanId.HasValue)
                {
                    decimal repaid = await _loanService.ApplyRepaymentFromImportAsync(member.Id, member.UserId, request.GroupId, alloc.Amount, item.TransactionDate, item.ReferenceNumber);
                    if (repaid > 0) result.TotalRepaymentsApplied += repaid;
                }
                else if (alloc.Target == "Debt")
                {
                    // Should not occur with AllocateOldestFirst, kept for backward compat
                    decimal cleared = await _debtService.ClearWithPaymentAsync(member.Id, alloc.Amount, request.GroupId);
                    if (cleared > 0)
                    {
                        _context.LedgerEntries.Add(new LedgerEntry
                        {
                            Id = Guid.NewGuid(),
                            GroupId = request.GroupId,
                            UserId = member.UserId,
                            AccountId = savingsAccount.Id,
                            Amount = cleared,
                            Type = TransactionType.Contribution,
                            ReferenceNo = refNo,
                            Description = $"Pay old debt {alloc.Year}-{alloc.Month:00} Ref {item.ReferenceNumber} (oldest-first)",
                            CreatedAt = item.TransactionDate
                        });
                    }
                }
                else if (alloc.Target == "Contribution")
                {
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = request.GroupId,
                        UserId = member.UserId,
                        AccountId = savingsAccount.Id,
                        Amount = alloc.Amount,
                        Type = TransactionType.Contribution,
                        ReferenceNo = refNo,
                        Description = $"Contribution {alloc.Year}-{alloc.Month:00} (oldest-first {alloc.Note}) M-Koba Ref {item.ReferenceNumber}",
                        CreatedAt = item.TransactionDate
                    });
                    member.TotalContributionsCount += 1;
                    if (member.Status == MemberStatus.New || member.Status == MemberStatus.Warning)
                        member.Status = MemberStatus.Active;
                }
                else if (alloc.Target == "Savings")
                {
                    member.AdvanceBalance += alloc.Amount;
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = request.GroupId,
                        UserId = member.UserId,
                        AccountId = savingsAccount.Id,
                        Amount = alloc.Amount,
                        Type = TransactionType.Savings,
                        ReferenceNo = refNo,
                        Description = $"Advance savings {item.TransactionDate:MMMM yyyy} M-Koba Ref {item.ReferenceNumber} ({alloc.Note})",
                        CreatedAt = item.TransactionDate
                    });
                    result.TotalWelfareAdded += alloc.Amount;
                }
                else if (alloc.Target == "Fine")
                {
                    var fineAccount = await _accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.Fine);
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = request.GroupId,
                        UserId = member.UserId,
                        AccountId = fineAccount.Id,
                        Amount = alloc.Amount,
                        Type = TransactionType.FinePayment,
                        ReferenceNo = refNo,
                        Description = $"Fine {alloc.Year}-{alloc.Month:00} {alloc.Note} Ref {item.ReferenceNumber}",
                        CreatedAt = item.TransactionDate
                    });
                    result.TotalFinesDeducted += alloc.Amount;
                }
                else if (alloc.Target == "JoiningFee")
                {
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = request.GroupId,
                        UserId = member.UserId,
                        AccountId = savingsAccount.Id,
                        Amount = alloc.Amount,
                        Type = TransactionType.JoiningFee,
                        ReferenceNo = refNo,
                        Description = $"Joining fee {alloc.Year}-{alloc.Month:00} {alloc.Note} Ref {item.ReferenceNumber}",
                        CreatedAt = item.TransactionDate
                    });
                }

                var eventType = alloc.Target switch
                {
                    "Contribution" => FinancialEventType.ContributionPaid,
                    "LoanRepayment" => FinancialEventType.LoanRepaymentPaid,
                    "Fine" => FinancialEventType.FinePaid,
                    "JoiningFee" => FinancialEventType.JoiningFeePaid,
                    "Debt" => FinancialEventType.DebtCleared,
                    "Savings" => FinancialEventType.SavingsDeposited,
                    _ => FinancialEventType.SavingsDeposited
                };

                _context.FinancialEvents.Add(new FinancialEvent
                {
                    Id = Guid.NewGuid(),
                    GroupId = request.GroupId,
                    MemberId = member.Id,
                    Type = eventType,
                    Amount = alloc.Amount,
                    OccurredAt = item.TransactionDate,
                    CreatedBy = member.UserId,
                    Source = EventSource.MKobaImport,
                    SourceReference = refNo,
                    PolicyVersion = policy.Version,
                    CorrelationId = correlationId
                });
            }

            // Event contribution for EventBased
            if (activeEvent != null)
            {
                var eventContribution = await _context.EventContributions.FirstOrDefaultAsync(ec => ec.GroupEventId == activeEvent.Id && ec.GroupMemberId == member.Id);
                if (eventContribution == null)
                {
                    eventContribution = new EventContribution
                    {
                        Id = Guid.NewGuid(),
                        GroupEventId = activeEvent.Id,
                        GroupMemberId = member.Id,
                        UserId = member.UserId,
                        ExpectedAmount = activeEvent.TargetAmountPerMember,
                        PaidAmount = 0m,
                        Status = EventContributionStatus.Pending
                    };
                    _context.EventContributions.Add(eventContribution);
                }

                decimal pending = Math.Max(0m, eventContribution.ExpectedAmount - eventContribution.PaidAmount);
                if (pending > 0 && allocation.Allocations.Any(a => a.Target == "Contribution"))
                {
                    var contribAlloc = allocation.Allocations.First(a => a.Target == "Contribution");
                    decimal apply = Math.Min(pending, contribAlloc.Amount);
                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = request.GroupId,
                        UserId = member.UserId,
                        AccountId = eventAccount.Id,
                        Amount = apply,
                        Type = TransactionType.EventContribution,
                        ReferenceNo = item.ReferenceNumber,
                        Description = $"Event [{activeEvent.Title}] Ref {activeEvent.Id} via {item.ReferenceNumber}",
                        CreatedAt = item.TransactionDate
                    });
                    eventContribution.PaidAmount += apply;
                    eventContribution.LastPaidAt = item.TransactionDate;
                    eventContribution.Status = eventContribution.PaidAmount >= eventContribution.ExpectedAmount ? EventContributionStatus.Paid : EventContributionStatus.PartiallyPaid;
                }
            }

            await _auditService.CreateAsync(
                groupId: request.GroupId,
                memberId: member.Id,
                actorId: member.UserId,
                actorRole: "System",
                actorMembershipNumber: member.MemberNumber,
                action: AuditAction.PaymentReceived,
                entityType: "FinancialEvent",
                entityId: paymentEvent.Id,
                before: null,
                after: new { Amount = item.Amount, Reference = item.ReferenceNumber, Allocation = allocation },
                source: EventSource.MKobaImport,
                sourceReference: item.ReferenceNumber,
                sourceMetadata: new { FileName = item.ReferenceNumber, Amount = item.Amount, Member = item.MemberName },
                policyVersion: policy.Version,
                allocationResult: allocation,
                reason: $"M-Koba import {item.TransactionDate:MMMM yyyy} oldest-first",
                correlationId: correlationId,
                isSystemGenerated: true);

            // Make the current payment visible to the next transaction before
            // the next queue is built. Without this, all payments in one PDF
            // import were allocated against the same stale position.
            await _context.SaveChangesAsync();
            result.SuccessfullyProcessed++;
        }

        await _context.SaveChangesAsync();
        return Ok(result);
    }

    [HttpGet("withdrawals/{groupId}")]
    public async Task<ActionResult<List<WalletWithdrawalDto>>> GetWithdrawals(Guid groupId)
    {
        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), groupId, g => g.ImportApproval);
        var withdrawals = await _context.WalletWithdrawals.Where(w => w.GroupId == groupId)
            .OrderByDescending(w => w.TransactionDate)
            .Select(w => new WalletWithdrawalDto(w.Id, w.GroupId, w.ReferenceNo, w.MemberName, w.PhoneNumber, w.Amount, w.TransactionDate, w.IsReconciled, w.ReconciliationNote))
            .ToListAsync();
        return Ok(withdrawals);
    }

    [HttpPatch("withdrawals/{withdrawalId}/reconcile")]
    public async Task<IActionResult> ReconcileWithdrawal(Guid withdrawalId, [FromBody] ReconcileWithdrawalDto dto)
    {
        var withdrawal = await _context.WalletWithdrawals.FindAsync(withdrawalId);
        if (withdrawal == null) return NotFound(new { message = "Withdrawal not found." });
        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), withdrawal.GroupId, g => g.ImportApproval);
        withdrawal.IsReconciled = true;
        withdrawal.ReconciliationNote = dto.Note;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Withdrawal reconciled." });
    }
}
