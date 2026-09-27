using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// +D Historical JoinFee handling with approval workflow
/// For Frank's case: 10k via Chairperson not in M-Koba, needs approval before ledger entry
/// Prevents double-count: checks existing JoinFee paid via M-Koba before allowing manual entry
/// </summary>
public class HistoricalJoinFeeService
{
    private readonly ApplicationDbContext _context;
    private readonly BusinessRuleEngine _businessRuleEngine;
    private readonly ObligationLedgerService _obligationLedgerService;
    private readonly AccountResolverService _accountResolver;
    private readonly AuditService _auditService;

    public HistoricalJoinFeeService(
        ApplicationDbContext context,
        BusinessRuleEngine businessRuleEngine,
        ObligationLedgerService obligationLedgerService,
        AccountResolverService accountResolver,
        AuditService auditService)
    {
        _context = context;
        _businessRuleEngine = businessRuleEngine;
        _obligationLedgerService = obligationLedgerService;
        _accountResolver = accountResolver;
        _auditService = auditService;
    }

    public class HistoricalJoinFeeRequest
    {
        public Guid GroupId { get; set; }
        public Guid MemberId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = "";
        public Guid RequestedBy { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public async Task<FinancialEvent> RequestAsync(HistoricalJoinFeeRequest req)
    {
        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == req.MemberId)
            ?? throw new InvalidOperationException("Member not found");

        var policy = await _businessRuleEngine.GetActivePolicyAsync(req.GroupId, req.OccurredAt);

        // Double-count prevention: check already paid via M-Koba
        var queue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(req.MemberId, policy, req.OccurredAt);
        decimal alreadyPaid = queue.Sum(q => q.JoinFeePaid);
        try
        {
            var ledgerPaid = await _context.LedgerEntries
                .Where(l => l.GroupId == req.GroupId && l.UserId == member.UserId && l.Type == TransactionType.JoiningFee)
                .SumAsync(l => (decimal?)l.Amount) ?? 0m;
            if (ledgerPaid > alreadyPaid) alreadyPaid = ledgerPaid;
        }
        catch { }

        decimal remaining = Math.Max(0, policy.JoiningFee - alreadyPaid);
        if (req.Amount > remaining)
            throw new InvalidOperationException($"JoinFee already paid {alreadyPaid:N0}/{policy.JoiningFee:N0} via M-Koba. Requested {req.Amount:N0} exceeds remaining {remaining:N0}. This prevents double-count (Tunganege case).");

        // Check if approval needed based on threshold
        bool needsApproval = false;
        if (policy.JoinFeeApprovalThreshold.HasValue && req.Amount >= policy.JoinFeeApprovalThreshold.Value)
            needsApproval = true;
        if (policy.JoinFeeCaptureMode == JoinFeeCaptureMode.ManualOnly)
            needsApproval = true; // Manual entries always need approval in ManualOnly mode

        var financialEvent = new FinancialEvent
        {
            Id = Guid.NewGuid(),
            GroupId = req.GroupId,
            MemberId = req.MemberId,
            Type = FinancialEventType.JoiningFeePaid,
            Amount = req.Amount,
            OccurredAt = req.OccurredAt,
            CreatedBy = req.RequestedBy,
            Source = EventSource.Manual,
            SourceReference = $"HIST-JOINFEE-{req.MemberId}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            PolicyVersion = policy.Version,
            CorrelationId = Guid.NewGuid(),
            Status = needsApproval ? FinancialEventStatus.Pending : FinancialEventStatus.Approved,
            Reason = req.Reason,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { AlreadyPaid = alreadyPaid, Remaining = remaining, Requested = req.Amount, PolicyJoinFee = policy.JoiningFee })
        };

        _context.FinancialEvents.Add(financialEvent);
        await _context.SaveChangesAsync();

        // If auto-approved, create ledger entry immediately
        if (financialEvent.Status == FinancialEventStatus.Approved)
        {
            await CreateLedgerEntryAsync(financialEvent, member);
        }

        return financialEvent;
    }

    public async Task<FinancialEvent> ApproveAsync(Guid financialEventId, Guid approverGroupMemberId, string? note = null)
    {
        var fe = await _context.FinancialEvents.FirstOrDefaultAsync(e => e.Id == financialEventId)
            ?? throw new InvalidOperationException("FinancialEvent not found");

        if (fe.Status != FinancialEventStatus.Pending)
            throw new InvalidOperationException($"Event status is {fe.Status}, not Pending");

        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == fe.MemberId)
            ?? throw new InvalidOperationException("Member not found");

        fe.Status = FinancialEventStatus.Approved;
        fe.ApprovedByGroupMemberId = approverGroupMemberId;
        fe.ApprovedAt = DateTime.UtcNow;
        fe.ApprovalNote = note;

        await _context.SaveChangesAsync();

        await CreateLedgerEntryAsync(fe, member);

        return fe;
    }

    public async Task<FinancialEvent> RejectAsync(Guid financialEventId, Guid approverGroupMemberId, string? note = null)
    {
        var fe = await _context.FinancialEvents.FirstOrDefaultAsync(e => e.Id == financialEventId)
            ?? throw new InvalidOperationException("FinancialEvent not found");

        fe.Status = FinancialEventStatus.Rejected;
        fe.ApprovedByGroupMemberId = approverGroupMemberId;
        fe.ApprovedAt = DateTime.UtcNow;
        fe.ApprovalNote = note;

        await _context.SaveChangesAsync();
        return fe;
    }

    private async Task CreateLedgerEntryAsync(FinancialEvent fe, GroupMember member)
    {
        var savingsAccount = await _accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.Savings);

        // Check duplicate
        bool exists = await _context.LedgerEntries.AnyAsync(l => l.ReferenceNo == fe.SourceReference);
        if (exists) return;

        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = fe.GroupId,
            UserId = member.UserId,
            AccountId = savingsAccount.Id,
            Amount = fe.Amount,
            Type = TransactionType.JoiningFee,
            ReferenceNo = fe.SourceReference,
            Description = $"Historical JoinFee {fe.Amount:N0} - {fe.Reason} (Approved, ",
            CreatedAt = fe.OccurredAt
        };

        _context.LedgerEntries.Add(entry);

        await _auditService.CreateAsync(
            fe.GroupId, member.Id, fe.CreatedBy, "System", member.MemberNumber,
            AuditAction.PaymentReceived, "FinancialEvent", fe.Id,
            null, new { Amount = fe.Amount, Reason = fe.Reason, Source = fe.Source },
            EventSource.Manual, fe.SourceReference,
            new { Reason = fe.Reason, ApprovedBy = fe.ApprovedByGroupMemberId },
            fe.PolicyVersion, null, $"Historical JoinFee approval {fe.Amount:N0}", fe.CorrelationId ?? Guid.NewGuid());

        await _context.SaveChangesAsync();
    }

    public async Task<List<FinancialEvent>> GetPendingAsync(Guid groupId)
    {
        return await _context.FinancialEvents
            .Where(e => e.GroupId == groupId && e.Status == FinancialEventStatus.Pending && e.Type == FinancialEventType.JoiningFeePaid)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
    }
}
