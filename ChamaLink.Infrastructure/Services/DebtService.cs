using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

// Sprint 1 gap: "Debt Table Haipo". Records a member's missed
// contribution as an explicit, trackable Debt (separate from the Fine
// penalty issued alongside it - see FineService).
public class DebtService
{
    private readonly ApplicationDbContext _context;

    public DebtService(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Debt> RecordShortfallAsync(
        Guid groupId, Guid groupMemberId, Guid userId,
        decimal amount, DateTime period, string reason)
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            GroupMemberId = groupMemberId,
            UserId = userId,
            Amount = amount,
            Reason = reason,
            Period = period,
            CreatedAt = DateTime.UtcNow
        };

        _context.Debts.Add(debt);
        return Task.FromResult(debt);
    }

    // Applies an over-payment (money paid beyond what the current period
    // needs) against the member's oldest open Debts first, so past
    // shortfalls get cleared automatically as soon as the member catches
    // up, without a treasurer having to do it by hand.
    //
    // FIX (2026-09-16): DebtAllocationStrategy ILIKUWA HAISOMWI KAMWE.
    //
    // Ukaguzi wa nje (Principal Audit, K#5) ulisema ManualAllocation
    // "inanymaza na kufanya kama CurrentMonthFirst". HIYO SI KWELI - hali
    // halisi ilikuwa mbaya zaidi:
    //
    //   GroupSettingsModules.cs:57  → chaguo-msingi = CurrentMonthFirst
    //   DebtService (code hii)      → OrderBy(d => d.Period) = zamani kwanza
    //                                 = OldestDebtFirst
    //
    // Yaani kila kikundi kwa CHAGUO-MSINGI kilikuwa kinapata mgawanyo wa
    // OldestDebtFirst, kinyume kabisa na settings zake. `grep` ya
    // DebtAllocationStrategy katika code ya biashara ilirudisha matokeo
    // ya kuhifadhi/kusoma settings pekee - hakuna iliyoiTEKELEZA.
    //
    // Kwa nini kukataa badala ya kutekeleza sasa hivi?
    // Mpangilio wa mgawanyo hauko ndani ya method hii pekee - upo kwenye
    // "waterfall" ya MkobaImportController (STEP 1 faini → 2 tukio → 3 mchango
    // wa mwezi → 3.4 joining fee → 3.5 madeni ya zamani → 4 ziada).
    // `OldestDebtFirst` ingehitaji kubadilisha mpangilio huo mzima, na
    // `ManualAllocation` inahitaji UI ya mtunza-hazina kuchagua deni gani.
    // Kuzitekeleza kwa haraka bila vipimo ingekuwa hatari zaidi kuliko
    // kukataa. Kwa hiyo: CurrentMonthFirst (ndiyo inayofanya kazi na
    // waterfall iliyopo) inaruhusiwa; nyingine zinakataliwa kwa ujumbe wazi.
    public async Task<decimal> ClearWithPaymentAsync(
        Guid groupMemberId, decimal availableAmount, Guid? groupId = null)
    {
        // Kikataa mikakati ambayo haijatekelezwa - kabla ya kusonga pesa.
        if (groupId is Guid gid)
        {
            var strategy = await _context.GroupSettings
                .Where(s => s.GroupId == gid)
                .Select(s => s.Contribution.DebtAllocationStrategy)
                .FirstOrDefaultAsync();

            if (strategy != DebtAllocationStrategy.CurrentMonthFirst)
                throw new Domain.Exceptions.ValidationException(
                    $"Mkakati wa mgawanyo wa madeni '{strategy}' bado haujatekelezwa. " +
                    "Mfumo unaunga mkono 'CurrentMonthFirst' pekee kwa sasa. " +
                    "Badilisha DebtAllocationStrategy kwenye settings za kikundi ili kuendelea. " +
                    "(Kukataa hapa ni bora kuliko kugawa pesa kwa mpangilio usio sahihi kimya kimya.)");
        }

        if (availableAmount <= 0)
            return 0m;

        var openDebts = await _context.Debts
            .Where(d => d.GroupMemberId == groupMemberId && d.Status == DebtStatus.Outstanding)
            .OrderBy(d => d.Period)
            .ToListAsync();

        decimal remaining = availableAmount;
        decimal totalApplied = 0m;

        foreach (var debt in openDebts)
        {
            if (remaining <= 0) break;

            decimal outstanding = debt.Amount - debt.AmountCleared;
            if (outstanding <= 0) continue;

            decimal portion = Math.Min(outstanding, remaining);
            debt.AmountCleared += portion;
            if (debt.AmountCleared >= debt.Amount)
            {
                debt.Status = DebtStatus.Cleared;
                debt.ClearedAt = DateTime.UtcNow;
            }

            remaining -= portion;
            totalApplied += portion;
        }

        return totalApplied;
    }

    public async Task<decimal> GetOutstandingTotalAsync(Guid groupMemberId)
    {
        return await _context.Debts
            .Where(d => d.GroupMemberId == groupMemberId && d.Status == DebtStatus.Outstanding)
            .SumAsync(d => (decimal?)(d.Amount - d.AmountCleared)) ?? 0m;
    }

    public async Task<List<Debt>> GetDebtsForGroupAsync(Guid groupId)
    {
        return await _context.Debts
            .Where(d => d.GroupId == groupId)
            .OrderByDescending(d => d.Period)
            .ToListAsync();
    }
}
