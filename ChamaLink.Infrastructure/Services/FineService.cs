using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

// Sprint 1 gaps: "Fine Entity Haipo" and "Automatic Fine Generation
// Haipo". This is the only place in the app that should create or pay
// down a Fine. It always writes a matching LedgerEntry (FineIssue /
// FinePayment) against the member's dedicated Fine account, so older
// ledger-based views (member statement, group summary) keep working.
//
// BUG FIX: WelfarePenaltyBackgroundService used to write its FineIssue
// LedgerEntry against the member's SocialFund (welfare) account, while
// MkobaImportController's fine-payment step only ever looked at the
// member's dedicated Fine account. That meant a welfare-deadline fine
// could never be found or paid off through an M-Koba import. Because
// IssueFineAsync below always resolves AccountType.Fine itself, every
// fine - regardless of who issues it - now lives in the same place and
// can be paid down consistently.
public class FineService
{
    private readonly ApplicationDbContext _context;
    private readonly AccountResolverService _accountResolver;

    public FineService(ApplicationDbContext context, AccountResolverService accountResolver)
    {
        _context = context;
        _accountResolver = accountResolver;
    }

    public async Task<Fine> IssueFineAsync(
        Guid groupId,
        Guid groupMemberId,
        Guid userId,
        decimal amount,
        FineReasonType reasonType,
        string reason,
        DateTime? period = null,
        Guid? groupEventId = null,
        DateTime? dueDate = null)
    {
        var fineAccount = await _accountResolver.GetOrCreateAccountAsync(groupMemberId, AccountType.Fine);

        var fine = new Fine
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            GroupMemberId = groupMemberId,
            UserId = userId,
            Amount = amount,
            ReasonType = reasonType,
            Reason = reason,
            Period = period,
            GroupEventId = groupEventId,
            DueDate = dueDate,
            IssuedAt = DateTime.UtcNow
        };

        _context.Fines.Add(fine);

        _context.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            AccountId = fineAccount.Id,
            UserId = userId,
            Amount = amount,
            Type = TransactionType.FineIssue,
            Description = reason,
            CreatedAt = fine.IssuedAt
        });

        return fine;
    }

    // Applies a payment amount against a member's oldest unpaid fines
    // first (FIFO), mirroring each portion into the Ledger (FinePayment).
    // Returns how much of the given amount was actually absorbed by
    // fines - any leftover is the caller's to apply elsewhere (e.g. the
    // M-Koba import waterfall's next step).
    public async Task<decimal> ApplyPaymentAsync(
        Guid groupId,
        Guid groupMemberId,
        Guid userId,
        decimal availableAmount,
        string? referenceNo,
        DateTime paidAt)
    {
        if (availableAmount <= 0)
            return 0m;

        var fineAccount = await _accountResolver.GetOrCreateAccountAsync(groupMemberId, AccountType.Fine);

        var openFines = await _context.Fines
            .Where(f => f.GroupMemberId == groupMemberId &&
                        (f.Status == FineStatus.Pending || f.Status == FineStatus.PartiallyPaid))
            .OrderBy(f => f.IssuedAt)
            .ToListAsync();

        decimal remaining = availableAmount;
        decimal totalApplied = 0m;

        foreach (var fine in openFines)
        {
            if (remaining <= 0) break;

            decimal outstanding = fine.Amount - fine.AmountPaid;
            if (outstanding <= 0) continue;

            decimal portion = Math.Min(outstanding, remaining);
            fine.AmountPaid += portion;
            fine.Status = fine.AmountPaid >= fine.Amount ? FineStatus.Paid : FineStatus.PartiallyPaid;
            if (fine.Status == FineStatus.Paid)
                fine.PaidAt = paidAt;

            _context.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                AccountId = fineAccount.Id,
                UserId = userId,
                Amount = portion,
                Type = TransactionType.FinePayment,
                ReferenceNo = referenceNo,
                Description = $"Malipo ya Faini (Ref: {referenceNo})",
                CreatedAt = paidAt
            });

            remaining -= portion;
            totalApplied += portion;
        }

        return totalApplied;
    }

    public async Task<decimal> GetPendingFineTotalAsync(Guid groupMemberId)
    {
        return await _context.Fines
            .Where(f => f.GroupMemberId == groupMemberId &&
                        (f.Status == FineStatus.Pending || f.Status == FineStatus.PartiallyPaid))
            .SumAsync(f => (decimal?)(f.Amount - f.AmountPaid)) ?? 0m;
    }

    public async Task<List<Fine>> GetFinesForGroupAsync(Guid groupId)
    {
        return await _context.Fines
            .Where(f => f.GroupId == groupId)
            .OrderByDescending(f => f.IssuedAt)
            .ToListAsync();
    }

    public async Task<List<Fine>> GetFinesForMemberAsync(Guid groupMemberId)
    {
        return await _context.Fines
            .Where(f => f.GroupMemberId == groupMemberId)
            .OrderByDescending(f => f.IssuedAt)
            .ToListAsync();
    }
}
