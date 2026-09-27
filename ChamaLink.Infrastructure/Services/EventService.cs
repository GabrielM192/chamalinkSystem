using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

public class EventService : IEventService
{
    private readonly ApplicationDbContext _context;
    private readonly AccountResolverService _accountResolver;
    private readonly DebtService _debtService;

    public EventService(ApplicationDbContext context, AccountResolverService accountResolver, DebtService debtService)
    {
        _context = context;
        _accountResolver = accountResolver;
        _debtService = debtService;
    }

    public async Task<EventResponseDto> TriggerEventAsync(TriggerEventDto dto)
    {
        // 1. Tafuta wanachama wote wa kikundi hiki
        var groupMembers = await _context.GroupMembers
            .Where(gm => gm.GroupId == dto.GroupId)
            .ToListAsync();

        if (!groupMembers.Any())
            throw new ValidationException("Kikundi hakina wanachama.");

        // NEW (audit Stage 2 gap: "MinimumReserveBalance haitekelezwi"):
        // GroupSettings.MinimumReserveBalance has existed since the
        // ChamaLink v1 scope work with the documented intent that a
        // member's welfare balance should never be pushed below this
        // floor by an event deduction - the deficit should become a Debt
        // instead. Nothing ever actually enforced that until now. Only
        // meaningful under WelfareMode.DeductBalance: under ContributePot
        // a deduction only ever raises the balance, so it can never
        // breach a floor.
        var groupSettings = await _context.GroupSettings
            .FirstOrDefaultAsync(s => s.GroupId == dto.GroupId);
        bool enforceReserveFloor = groupSettings != null
            && groupSettings.Event.WelfareMode == ChamaLink.Domain.WelfareMode.DeductBalance
            && groupSettings.Financial.MinimumReserveBalance > 0;

        var now = DateTime.UtcNow;
        var deadline = now.AddDays(dto.DeadlineDays);

        // 2. Sajili Tukio jipya (GroupEvent)
        var groupEvent = new GroupEvent
        {
            Id = Guid.NewGuid(),
            GroupId = dto.GroupId,
            Title = dto.Title,
            Description = dto.Description ?? string.Empty,
            AmountDeducted = dto.AmountDeducted,
            // BUG FIX: this was never set before, so it stayed 0 and the
            // M-Koba import waterfall's "Event Contribution" step could
            // never find any pending amount to allocate money to.
            TargetAmountPerMember = dto.TargetAmountPerMember,
            BeneficiaryGroupMemberId = dto.BeneficiaryGroupMemberId,
            BeneficiaryName = dto.BeneficiaryName,
            EventDate = now,
            DeadlineDate = deadline,
            IsActive = true,
            IsResolved = false
        };

        _context.GroupEvents.Add(groupEvent);

        // 3. Fyeka kiwango kilichopangwa kutoka kwa kila mwanachama kwenye Ledger
        //
        // BUG FIX: this used to set AccountId = member.Id (the GroupMember's own
        // Id), which is NOT an Account. That made these entries invisible to
        // GetMemberStatementAsync, which only looks for real Account rows.
        // Now we get (or create) each member's real "SocialFund" (welfare)
        // account first, the same way LedgerService does for savings/loans.
        // We also set UserId so the entry can be traced back to the person.
        foreach (var member in groupMembers)
        {
            var welfareAccount = await _accountResolver.GetOrCreateAccountAsync(
                member.Id, ChamaLink.Domain.AccountType.SocialFund);

            // NEW (MinimumReserveBalance enforcement): figure out how much
            // of AmountDeducted this specific member can actually afford
            // to have taken from their welfare balance without dropping
            // below the group's floor. Any part that doesn't fit becomes
            // a Debt instead (the member still owes it - see
            // DebtService.ClearWithPaymentAsync for how it later gets
            // cleared, e.g. through the M-Koba import waterfall).
            decimal amountToDeduct = dto.AmountDeducted;
            decimal shortfall = 0m;

            if (enforceReserveFloor)
            {
                var existingDeductions = await _context.LedgerEntries
                    .Where(l => l.AccountId == welfareAccount.Id && l.Type == ChamaLink.Domain.TransactionType.WelfareDeduction)
                    .SumAsync(l => l.Amount);
                var existingTopUps = await _context.LedgerEntries
                    .Where(l => l.AccountId == welfareAccount.Id && l.Type == ChamaLink.Domain.TransactionType.WelfareTopUp)
                    .SumAsync(l => l.Amount);
                decimal currentBalance = existingTopUps - existingDeductions;

                decimal maxAffordable = Math.Max(0m, currentBalance - groupSettings!.Financial.MinimumReserveBalance);
                if (dto.AmountDeducted > maxAffordable)
                {
                    shortfall = dto.AmountDeducted - maxAffordable;
                    amountToDeduct = maxAffordable;
                }
            }

            if (amountToDeduct > 0)
            {
                var ledgerEntry = new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = dto.GroupId,
                    AccountId = welfareAccount.Id,
                    UserId = member.UserId,
                    Amount = amountToDeduct,
                    Type = ChamaLink.Domain.TransactionType.WelfareDeduction,
                    Description = $"Makato ya Welfare: {dto.Title}",
                    CreatedAt = now
                };

                _context.LedgerEntries.Add(ledgerEntry);
            }

            if (shortfall > 0)
            {
                await _debtService.RecordShortfallAsync(
                    dto.GroupId, member.Id, member.UserId, shortfall, now,
                    $"Nakisi ya Welfare (Tukio: {dto.Title}) - salio la chini la {groupSettings!.Financial.MinimumReserveBalance:N0} halikuvunjwa");
            }

            // NEW (Sprint 2 gap: Event Contribution Tracking Haipo): one
            // EventContribution row per member so "who has contributed to
            // this event and who hasn't" can be answered directly, without
            // re-summing the ledger every time.
            _context.EventContributions.Add(new EventContribution
            {
                Id = Guid.NewGuid(),
                GroupEventId = groupEvent.Id,
                GroupMemberId = member.Id,
                UserId = member.UserId,
                ExpectedAmount = dto.TargetAmountPerMember,
                PaidAmount = 0m,
                Status = dto.TargetAmountPerMember > 0
                    ? ChamaLink.Domain.EventContributionStatus.Pending
                    : ChamaLink.Domain.EventContributionStatus.Paid
            });
        }

        await _context.SaveChangesAsync();

        return new EventResponseDto(
            groupEvent.Id,
            groupEvent.GroupId,
            groupEvent.Title,
            groupEvent.AmountDeducted,
            groupEvent.TargetAmountPerMember,
            groupEvent.EventDate,
            groupEvent.DeadlineDate,
            groupMembers.Count
        );
    }
}