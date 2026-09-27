using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// HESABU TENA counter za GroupMember kutoka kwenye Ledger.
///
/// TATIZU (liligunduliwa 2026-09-16, baada ya ukaguzi wa Principal Audit):
///
/// GroupMember ina safu mbili za "denormalized" (zimehifadhiwa badala ya
/// kuhesabiwa):
///
///     TotalContributionsCount   - mara ngapi mwanachama amechangia
///     AdvanceBalance            - jumla ya akiba ya mwanachama
///
/// Mfumo una NJIA TATU tofauti zinazoandika mchango, na zilikuwa HAZIFANYI
/// kazi kwa namna moja:
///
///     Njia                              TotalContributionsCount  AdvanceBalance
///     ------------------------------------------------------------------------
///     TreasuryImportService (Excel)     imebadilishwa            imebadilishwa
///     MkobaImportController (M-Koba)    imebadilishwa            imebadilishwa
///     LedgerService (mchango wa mkono)  HAIJABADILISHWA          HAIJABADILISHWA
///
/// Athari halisi: mchango unaorekodiwa kwa mkono na Treasurer HAUKUONEKANA
/// kwenye ripoti zinazosoma counter hizo. Mfano halisi:
///
///     ReportsController.cs:124  →  "   • Michango: Mara {TotalContributionsCount}"
///
/// Yaani ripoti ya WhatsApp - kipengele kinachotumika zaidi na watumiaji -
/// ilikuwa inatoa NAMBA MBAYA. Mwanachama aliye na michango 12 (5 ya mikono,
/// 7 ya M-Koba) alikuwa akionyeshwa "Mara 7".
///
/// SULUHISHO:
///
/// Badala ya kuongeza counter kwenye kila njia (ambayo inaruhusu njia ya
/// nne kuongezwa baadaye na kusahau tena), tunahesabu counter hizo KUTOKA
/// KWENYE LEDGER kila tunapozihitaji. Ledger ni chanzo cha ukweli - ndiyo
/// kanuni kuu ya mfumo huu ("the ledger is the source of truth").
///
/// Kwa nini siyo "[NotMapped] computed property" kama ukaguzi ulipendekeza?
/// Kwa sababu EF Core haiwezi kutafsiri `=> LedgerEntries.Count()` kwenye
/// safu iliyohifadhiwa bila migration kubwa na kubadilisha kila mahali
/// panaposomwa. Njia hii inatoa matokeo sawa (hakuna drift) kwa hatari ndogo.
///
/// MAANA ZA KIHESABU (zimeelezwa ili zisiwe na utata):
///
///     TotalContributionsCount = idadi ya LedgerEntry zenye
///                               Type == Contribution
///                               kwenye akaunti ya Savings ya mwanachama
///
///     AdvanceBalance          = jumla ya Amount za hizo LedgerEntry
///
/// Kumbuka: hii ni tofauti DOGO na tabia ya zamani ya import. Zamani,
/// MkobaImport iliongeza AdvanceBalance kwa kila hatua ya mgawanyo (kulipa
/// deni, mchango wa sasa, na ziada), hivyo pesa moja iliyogawanywa mara
/// mbili ilihesabiwa mara mbili. Hesabu mpya inahesabu kila LedgerEntry
/// mara moja - ndiyo sahihi zaidi.
/// </summary>
public class MemberCounterSyncService
{
    private readonly ApplicationDbContext _context;

    public MemberCounterSyncService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hesabu upya counter za mwanachama mmoja kulingana na ledger yake.
    /// HAIIANDIKI database - mwandaji anaita SaveChangesAsync.
    /// </summary>
    public async Task ReconcileMemberAsync(Guid groupMemberId)
    {
        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.Id == groupMemberId);

        if (member is null)
            return;

        var savingsAccountId = await _context.Accounts
            .Where(a => a.GroupMemberId == groupMemberId && a.Type == AccountType.Savings)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync();

        // Mwanachama asiye na akaunti ya Savings hajachangia kamwe.
        if (savingsAccountId is null)
        {
            member.TotalContributionsCount = 0;
            member.AdvanceBalance = 0m;
            return;
        }

        var totals = await _context.LedgerEntries
            .Where(e => e.AccountId == savingsAccountId.Value &&
                        e.Type == TransactionType.Contribution)
            .GroupBy(e => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Total = g.Sum(e => (decimal?)e.Amount) ?? 0m
            })
            .FirstOrDefaultAsync();

        member.TotalContributionsCount = totals?.Count ?? 0;
        member.AdvanceBalance = totals?.Total ?? 0m;
    }

    /// <summary>
    /// Hesabu upya counter za wanachama WOTE wa kikundi kimoja.
    /// Inatumika (a) baada ya kurekebisha bug hii ili kurejesha data ya zamani,
    /// na (b) kama chombo cha ukarabati kinachoweza kuitwa na uongozi.
    ///
    /// Inarudisha idadi ya wanachama ambao counter zao zilikuwa si sahihi -
    /// hivyo unaweza kuona ukubwa wa tatizo.
    /// </summary>
    public async Task<CounterReconcileResult> ReconcileGroupAsync(Guid groupId)
    {
        var members = await _context.GroupMembers
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        // Pakia akaunti zote za Savings za kikundi kwa mara moja (siyo N+1).
        var memberIds = members.Select(m => m.Id).ToList();

        var savingsAccounts = await _context.Accounts
            .Where(a => memberIds.Contains(a.GroupMemberId) && a.Type == AccountType.Savings)
            .ToDictionaryAsync(a => a.GroupMemberId, a => a.Id);

        // Hesabu zote za michango kwa query moja (siyo query moja kwa kila mwanachama).
        var accountIds = savingsAccounts.Values.ToList();

        var ledgerTotals = await _context.LedgerEntries
            .Where(e => accountIds.Contains(e.AccountId) &&
                        e.Type == TransactionType.Contribution)
            .GroupBy(e => e.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Count = g.Count(),
                Total = g.Sum(e => e.Amount)
            })
            .ToListAsync();

        var byAccount = ledgerTotals.ToDictionary(t => t.AccountId, t => t);

        var result = new CounterReconcileResult();

        foreach (var member in members)
        {
            var oldCount = member.TotalContributionsCount;
            var oldBalance = member.AdvanceBalance;

            if (savingsAccounts.TryGetValue(member.Id, out var accountId) &&
                byAccount.TryGetValue(accountId, out var totals))
            {
                member.TotalContributionsCount = totals.Count;
                member.AdvanceBalance = totals.Total;
            }
            else
            {
                member.TotalContributionsCount = 0;
                member.AdvanceBalance = 0m;
            }

            if (oldCount != member.TotalContributionsCount || oldBalance != member.AdvanceBalance)
            {
                result.MembersCorrected++;
                result.CountDrift += Math.Abs(oldCount - member.TotalContributionsCount);
                result.BalanceDrift += Math.Abs(oldBalance - member.AdvanceBalance);
            }

            result.MembersChecked++;
        }

        return result;
    }
}

/// <summary>Matokeo ya ukarabati wa counter.</summary>
public class CounterReconcileResult
{
    /// <summary>Jumla ya wanachama waliokaguliwa.</summary>
    public int MembersChecked { get; set; }

    /// <summary>Wanachama ambao counter zao zilikuwa si sahihi na zimerekebishwa.</summary>
    public int MembersCorrected { get; set; }

    /// <summary>Jumla ya tofauti za idadi ya michango iliyopatikana.</summary>
    public int CountDrift { get; set; }

    /// <summary>Jumla ya tofauti za kiasi (TZS) iliyopatikana.</summary>
    public decimal BalanceDrift { get; set; }

    public bool HadDrift => MembersCorrected > 0;
}
