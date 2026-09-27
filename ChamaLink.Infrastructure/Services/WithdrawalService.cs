using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

// Withdrawal Governance gap: the first version of this service let ANY
// GroupMember approve or reject ANY withdrawal - there was no role check
// at all, let alone a way for a group to require more than one signer.
//
// SECURITY FIX (audit 1.6 / IDOR): every method that needs to know "who
// is doing this" now takes actorUserId - the UserId pulled from the
// caller's JWT (see ClaimsPrincipalExtensions.GetUserId) - and resolves
// that person's GroupMember row itself. Earlier versions accepted a
// GroupMemberId directly from the request, so anyone with a valid token
// could claim to be a different member (e.g. the Treasurer) just by
// sending that member's ID. That is no longer possible: the only
// identity these methods trust is whoever the token actually belongs to.
public class WithdrawalService
{
    private readonly ApplicationDbContext _context;

    public WithdrawalService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Withdrawal> CreateAsync(CreateWithdrawalDto dto, Guid actorUserId)
    {
        var recordedBy = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.UserId == actorUserId && m.GroupId == dto.GroupId)
            ?? throw new UnauthorizedAccessException("Wewe si mwanachama wa kikundi hiki.");

        // If this withdrawal is settling a specific event and no explicit
        // beneficiary was given, default to that event's own beneficiary
        // - this is what links a payout back to a member's Benefits
        // Received in their financial profile.
        Guid? beneficiaryGroupMemberId = dto.BeneficiaryGroupMemberId;
        if (dto.GroupEventId.HasValue && beneficiaryGroupMemberId == null)
        {
            var groupEvent = await _context.GroupEvents
                .FirstOrDefaultAsync(e => e.Id == dto.GroupEventId.Value && e.GroupId == dto.GroupId);
            beneficiaryGroupMemberId = groupEvent?.BeneficiaryGroupMemberId;
        }

        var withdrawal = new Withdrawal
        {
            Id = Guid.NewGuid(),
            GroupId = dto.GroupId,
            Amount = dto.Amount,
            Purpose = dto.Purpose,
            BeneficiaryName = dto.BeneficiaryName,
            BeneficiaryGroupMemberId = beneficiaryGroupMemberId,
            GroupEventId = dto.GroupEventId,
            RecordedByGroupMemberId = recordedBy.Id,
            ReferenceNo = dto.ReferenceNo,
            Notes = dto.Notes,
            Status = WithdrawalStatus.Pending,
            Date = DateTime.UtcNow
        };

        _context.Withdrawals.Add(withdrawal);
        await _context.SaveChangesAsync();
        return withdrawal;
    }

    // Records one leader's approve/reject vote and moves the withdrawal's
    // Status forward according to the group's configured approval rule.
    //
    // Rejection rule: any one of the allowed approver roles can veto a
    // withdrawal outright (matches how these groups actually work - a
    // single leader raising a real objection normally stops the payment
    // rather than needing everyone to agree it's bad).
    public async Task<Withdrawal> DecideAsync(Guid withdrawalId, Guid actorUserId, bool approve, string? reason)
    {
        var withdrawal = await _context.Withdrawals
            .Include(w => w.Approvals)
            .FirstOrDefaultAsync(w => w.Id == withdrawalId)
            ?? throw new NotFoundException("Withdrawal haikupatikana.");

        if (withdrawal.Status != WithdrawalStatus.Pending && withdrawal.Status != WithdrawalStatus.PartiallyApproved)
            throw new ConflictException("Withdrawal hii tayari imeshughulikiwa (imeshaidhinishwa, kukataliwa, au kulipwa).");

        var decidingMember = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.UserId == actorUserId && m.GroupId == withdrawal.GroupId)
            ?? throw new UnauthorizedAccessException("Wewe si mwanachama wa kikundi hiki.");

        if (withdrawal.Approvals.Any(a => a.GroupMemberId == decidingMember.Id))
            throw new ValidationException("Tayari umeshatoa uamuzi kwenye withdrawal hii.");

        var settings = await _context.GroupSettings
            .FirstOrDefaultAsync(s => s.GroupId == withdrawal.GroupId);
        var rule = ResolveApprovalRule(settings);

        if (!rule.AllowedRoles.Contains(decidingMember.Role))
        {
            var roleNames = string.Join(" au ", rule.AllowedRoles);
            throw new UnauthorizedAccessException($"Huna ruhusa ya kuidhinisha withdrawal hii kulingana na taratibu za kikundi. Wanaoruhusiwa: {roleNames}.");
        }

        _context.WithdrawalApprovals.Add(new WithdrawalApproval
        {
            Id = Guid.NewGuid(),
            WithdrawalId = withdrawal.Id,
            GroupMemberId = decidingMember.Id,
            RoleAtDecision = decidingMember.Role,
            Approved = approve,
            Reason = reason,
            DecidedAt = DateTime.UtcNow
        });

        if (!approve)
        {
            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.ApprovedByGroupMemberId = decidingMember.Id;
            withdrawal.RejectionReason = reason;
            withdrawal.DecisionAt = DateTime.UtcNow;
        }
        else
        {
            int approvalsSoFar = withdrawal.Approvals.Count(a => a.Approved) + 1;
            if (approvalsSoFar >= rule.RequiredApprovals)
            {
                withdrawal.Status = WithdrawalStatus.Approved;
                withdrawal.ApprovedByGroupMemberId = decidingMember.Id;
                withdrawal.DecisionAt = DateTime.UtcNow;
            }
            else
            {
                withdrawal.Status = WithdrawalStatus.PartiallyApproved;
            }
        }

        await _context.SaveChangesAsync();
        return withdrawal;
    }

    // SECURITY FIX (audit 1.7: "MarkPaid haina authorization"): this used
    // to be callable by anyone with a token, with no check at all. Only
    // the group's own Treasurer may confirm a payout actually happened -
    // matches who is trusted to move real money in these groups. (Like
    // the withdrawal approval rule, this could become a per-group
    // GroupSettings choice later; Treasurer-only is the safe default for
    // now rather than leaving it open to everyone.)
    public async Task<Withdrawal> MarkPaidAsync(Guid withdrawalId, Guid actorUserId)
    {
        var withdrawal = await GetOrThrowAsync(withdrawalId);

        var actor = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.UserId == actorUserId && m.GroupId == withdrawal.GroupId)
            ?? throw new UnauthorizedAccessException("Wewe si mwanachama wa kikundi hiki.");

        if (actor.Role != GroupRole.Treasurer)
            throw new UnauthorizedAccessException("Mtunza Hazina (Treasurer) pekee ndiye anaweza kuthibitisha malipo.");

        if (withdrawal.Status != WithdrawalStatus.Approved)
            throw new ValidationException("Withdrawal lazima iidhinishwe kikamilifu kwanza kabla ya kuwekwa kama imelipwa.");

        withdrawal.Status = WithdrawalStatus.Paid;
        await _context.SaveChangesAsync();
        return withdrawal;
    }

    public async Task<List<Withdrawal>> GetByGroupAsync(Guid groupId)
    {
        return await _context.Withdrawals
            .Include(w => w.Approvals)
            .Where(w => w.GroupId == groupId)
            .OrderByDescending(w => w.Date)
            .ToListAsync();
    }

    // Tells the caller (e.g. a "create withdrawal" screen) what the
    // group's current rule actually is, without needing to duplicate the
    // ResolveApprovalRule logic on the frontend.
    public async Task<(List<GroupRole> AllowedRoles, int RequiredApprovals)> GetApprovalRuleAsync(Guid groupId)
    {
        var settings = await _context.GroupSettings.FirstOrDefaultAsync(s => s.GroupId == groupId);
        var rule = ResolveApprovalRule(settings);
        return (rule.AllowedRoles.ToList(), rule.RequiredApprovals);
    }

    // The single place that turns a group's ApprovalMode setting into a
    // concrete (who can approve, how many are needed) rule. Delegates to
    // the shared ApprovalPolicyResolver (see that file - dedup fix,
    // second audit round item A) rather than keeping its own copy.
    private static (HashSet<GroupRole> AllowedRoles, int RequiredApprovals) ResolveApprovalRule(GroupSettings? settings)
    {
        return ApprovalPolicyResolver.Resolve(settings?.Governance.WithdrawalApproval);
    }

    private async Task<Withdrawal> GetOrThrowAsync(Guid id)
    {
        return await _context.Withdrawals.FindAsync(id)
            ?? throw new NotFoundException("Withdrawal haikupatikana.");
    }
}
