using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// UPANDE WA KUSOMA wa Injini ya Ukweli.
///
/// Vyanzo (na hakuna kingine):
///   1. MkobaRawTransactions (deposits) — zinaunganishwa na mwanachama kwa NAMBA YA SIMU (MemberPhones)
///   2. MkobaStatementGaps zilizothibitishwa (Approved)  — pesa iliyokosekana kwenye PDF
///   3. FinancialEvents za Manual (Kianzio / pesa ya mkononi) — maker-checker + ManualEntryCutoff
///   4. Loans (ratiba ya marejesho)
///   5. TruthAdjustments (msamaha wa faini / nyongeza ya muda) zilizoidhinishwa
/// LedgerEntries na Excel HAZITUMIKI.
/// </summary>
public partial class TruthLedgerService
{
    private static readonly TimeZoneInfo Eat = ResolveEat();

    private readonly ApplicationDbContext _context;

    public TruthLedgerService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TruthGroupSettings> GetSettingsAsync(Guid groupId, CancellationToken ct = default)
        => await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct)
           ?? throw new PolicyNotConfiguredException(groupId,
               $"Kikundi {groupId} hakijaweka mipangilio ya Injini ya Ukweli. Viongozi waweke kupitia PUT /api/truth/{groupId}/settings.");

    public sealed record MemberTruth(Guid MemberId, string MemberNumber, IReadOnlyList<string> Phones, TruthStatement Statement, List<string> Warnings);

    public sealed record UnattributedReport(
        List<MkobaRawTransaction> DepositsWithUnknownPhone,
        List<MkobaStatementGap> UnresolvedGaps,
        decimal Total);

    public static DateTime NowEat() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Eat);

    public async Task<MemberTruth> GetMemberTruthAsync(Guid memberId, DateTime? asOfEat = null, CancellationToken ct = default,
        IReadOnlyList<TruthPayment>? hypotheticalPayments = null)
    {
        var member = await _context.GroupMembers.AsNoTracking().Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId, ct)
            ?? throw new KeyNotFoundException($"Member {memberId} not found");

        var asOf = Unspecified(asOfEat ?? NowEat());
        var dbPolicy = await GetSettingsAsync(member.GroupId, ct);
        var raw = await LoadRawAsync(member.GroupId, new[] { member.Id }, ct);
        return Compute(member, raw, dbPolicy, ToTruthPolicy(dbPolicy), asOf, hypotheticalPayments);
    }

    /// <summary>
    /// Taarifa za wanachama WOTE. (TATHMINI #7) Zamani iliita GetMemberTruthAsync kwa kila mwanachama
    /// (~7 queries x wanachama). Sasa data ghafi yote ya kikundi inapakiwa MARA MOJA (queries 8 bila kujali
    /// idadi ya wanachama) kisha kila mmoja anachezwa upya kwenye memory kwa Compute() ile ile —
    /// kwa hiyo matokeo ni sawa kabisa na ya mwanachama mmoja.
    /// </summary>
    public async Task<List<MemberTruth>> GetGroupTruthAsync(Guid groupId, DateTime? asOfEat = null, CancellationToken ct = default)
    {
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User)
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.MemberNumber).ToListAsync(ct);
        if (members.Count == 0) return new List<MemberTruth>();

        var asOf = Unspecified(asOfEat ?? NowEat());
        var dbPolicy = await GetSettingsAsync(groupId, ct);
        var policy = ToTruthPolicy(dbPolicy);
        var raw = await LoadRawAsync(groupId, members.Select(m => m.Id).ToList(), ct);
        return members.Select(m => Compute(m, raw, dbPolicy, policy, asOf, null)).ToList();
    }

    /// <summary>Data ghafi ya wanachama waliotajwa (mmoja au wote) — hakuna hesabu hapa.</summary>
    private sealed record RawData(
        ILookup<Guid, string> PhonesByMember,
        ILookup<string, MkobaRawTransaction> DepositsByPhone,
        ILookup<Guid, MkobaStatementGap> GapsByMember,
        ILookup<Guid, FinancialEvent> ManualByMember,
        ILookup<Guid, Loan> LoansByMember,
        List<TruthAdjustmentRecord> Adjustments);

    private async Task<RawData> LoadRawAsync(Guid groupId, IReadOnlyCollection<Guid> memberIds, CancellationToken ct)
    {
        var phoneRows = await _context.MemberPhones.AsNoTracking()
            .Where(p => memberIds.Contains(p.GroupMemberId))
            .Select(p => new { p.GroupMemberId, p.Phone }).ToListAsync(ct);
        var allPhones = phoneRows.Select(p => p.Phone).Distinct().ToList();

        // 1. M-Koba deposits kwa namba ya simu
        var deposits = await _context.MkobaRawTransactions.AsNoTracking()
            .Where(t => t.GroupId == groupId && !t.IsWithdrawal && allPhones.Contains(t.Phone))
            .ToListAsync(ct);

        // 2. Mapengo yaliyothibitishwa
        var gaps = await _context.MkobaStatementGaps.AsNoTracking()
            .Where(g => g.GroupId == groupId && g.AssignedGroupMemberId != null
                        && memberIds.Contains(g.AssignedGroupMemberId.Value) && g.ResolutionStatus != null)
            .ToListAsync(ct);

        // 3. Manual (maker-checker)
        var manual = await _context.FinancialEvents.AsNoTracking()
            .Where(e => e.MemberId != null && memberIds.Contains(e.MemberId.Value) && e.Source == EventSource.Manual
                        && (e.Type == FinancialEventType.JoiningFeePaid || e.Type == FinancialEventType.PaymentReceived)
                        && e.Status != FinancialEventStatus.Rejected && e.Status != FinancialEventStatus.Cancelled)
            .ToListAsync(ct);

        // 4. Mikopo (ratiba) — marejesho yanatoka kwenye malipo, si LedgerEntries
        var loans = await _context.Loans.AsNoTracking()
            .Where(l => memberIds.Contains(l.GroupMemberId)).ToListAsync(ct);

        // 5. Msamaha / nyongeza ya muda (ya mwanachama au ya kikundi kizima)
        var adj = await _context.TruthAdjustments.AsNoTracking()
            .Where(a => a.GroupId == groupId && (a.GroupMemberId == null || memberIds.Contains(a.GroupMemberId.Value)))
            .ToListAsync(ct);

        return new RawData(
            phoneRows.ToLookup(p => p.GroupMemberId, p => p.Phone),
            deposits.ToLookup(d => d.Phone),
            gaps.ToLookup(g => g.AssignedGroupMemberId!.Value),
            manual.ToLookup(e => e.MemberId!.Value),
            loans.ToLookup(l => l.GroupMemberId),
            adj);
    }

    /// <summary>Hesabu ya mwanachama mmoja kutoka data ghafi iliyokwisha pakiwa (hakuna DB hapa).</summary>
    private static MemberTruth Compute(GroupMember member, RawData raw, TruthGroupSettings dbPolicy, TruthPolicy policy,
        DateTime asOf, IReadOnlyList<TruthPayment>? hypotheticalPayments)
    {
        var phones = raw.PhonesByMember[member.Id].ToList();
        var warnings = new List<string>();
        if (phones.Count == 0)
            warnings.Add("Mwanachama hana namba ya simu iliyowekwa — malipo yake ya M-Koba hayawezi kuonekana. Kiongozi aweke namba.");

        var payments = new List<TruthPayment>();

        payments.AddRange(phones.Distinct().SelectMany(ph => raw.DepositsByPhone[ph])
            .Select(d => new TruthPayment(d.Reference, Unspecified(d.Timestamp), d.Amount, PaymentSource.Mkoba)));

        payments.AddRange(raw.GapsByMember[member.Id].Where(g => g.ResolutionStatus != TruthApprovalStatus.Rejected).Select(g => new TruthPayment(
            $"GAP-{g.BeforeReference}", Unspecified(g.AssumedTimestamp ?? g.WindowEnd), g.Amount, PaymentSource.MkobaGapResolved,
            IsApproved: g.ResolutionStatus == TruthApprovalStatus.Approved, Note: g.Evidence)));

        // Malipo ya mkononi (Auto) yanapita waterfall kamili: fungu la mkopo KWANZA, kisha madeni ya zamani, JoinFee, akiba.
        payments.AddRange(raw.ManualByMember[member.Id].Select(e => new TruthPayment(
            e.SourceReference ?? $"FE-{e.Id}", Unspecified(e.OccurredAt), e.Amount, PaymentSource.ManualHistorical,
            e.Type == FinancialEventType.JoiningFeePaid ? PaymentPurpose.JoinFee : PaymentPurpose.Auto,
            IsApproved: e.Status == FinancialEventStatus.Approved, Note: e.Reason)));

        var truthLoans = raw.LoansByMember[member.Id].Select(l => TruthLoan.EqualInstallments(
            l.Id.ToString(), Unspecified(l.DisbursedAt), Unspecified(l.DueDate), l.PrincipalAmount + l.InterestAmount)).ToList();

        var truthAdj = raw.Adjustments.Where(a => a.GroupMemberId == null || a.GroupMemberId == member.Id).Select(a => new TruthAdjustment(
            a.Kind == TruthAdjustmentKind.FineWaiver ? AdjustmentKind.FineWaiver : AdjustmentKind.GraceExtension,
            new YearMonth(a.Year, a.Month), a.ExtraDays, a.Status == TruthApprovalStatus.Approved, a.Reason, a.Id.ToString())).ToList();

        // Hakikisho (preview): malipo ya kufikirika ambayo HAYAJAHIFADHIWA — kiongozi aone matokeo kabla ya kuwasilisha
        if (hypotheticalPayments is not null) payments.AddRange(hypotheticalPayments);

        // Tarehe HALISI ya kujiunga = ya mapema kati ya JoinedAt na malipo ya kwanza yaliyothibitishwa.
        // (JoinedAt ya DB mara nyingi ni tarehe rekodi ilipotengenezwa/importiwa, si ya kujiunga.)
        var joinedEat = Unspecified(member.JoinedAt);
        var firstProof = payments.Where(p => p.Source == PaymentSource.Mkoba || p.IsApproved)
            .Select(p => (DateTime?)p.Timestamp).Min();
        var effectiveJoin = firstProof is { } fp && fp < joinedEat ? fp : joinedEat;
        if (firstProof is { } f0 && f0 < joinedEat.AddDays(-1))
            warnings.Add($"JoinedAt ya DB ({joinedEat:yyyy-MM-dd}) ni baada ya malipo ya kwanza ({f0:yyyy-MM-dd}) — tarehe ya malipo ya kwanza imetumika.");

        var statement = ContributionTruthEngine.Replay(ToTruthMember(member, effectiveJoin, dbPolicy, policy), payments, policy, asOf, truthLoans, truthAdj);
        return new MemberTruth(member.Id, member.MemberNumber, phones, statement, warnings);
    }

    /// <summary>Pesa iliyoingia M-Koba lakini haijulikani ni ya nani (namba haijawekwa / pengo la PDF).</summary>
    public async Task<UnattributedReport> GetUnattributedAsync(Guid groupId, CancellationToken ct = default)
    {
        var known = _context.MemberPhones.Where(p => p.GroupId == groupId).Select(p => p.Phone);
        var unknown = await _context.MkobaRawTransactions
            .Where(t => t.GroupId == groupId && !t.IsWithdrawal && !known.Contains(t.Phone))
            .OrderBy(t => t.Timestamp).ToListAsync(ct);
        var gaps = await _context.MkobaStatementGaps
            .Where(g => g.GroupId == groupId && g.ResolutionStatus != TruthApprovalStatus.Approved)
            .OrderBy(g => g.WindowStart).ToListAsync(ct);
        return new UnattributedReport(unknown, gaps, unknown.Sum(t => t.Amount) + gaps.Where(g => g.Amount > 0).Sum(g => g.Amount));
    }

    public sealed record CashMonth(string Month, int Year, int MonthNumber, decimal Deposits, int DepositCount,
        decimal Withdrawals, int WithdrawalCount, decimal? ClosingBalance);

    public sealed record CashWithdrawal(string Reference, DateTime Timestamp, decimal Amount, string Phone,
        string PayerName, string Description, Guid? MemberId, string? MemberName, decimal? BalanceAfter);

    public sealed record Cashbook(
        DateTime? FirstTransaction,
        DateTime? LastTransaction,
        decimal? OpeningBalance,
        decimal Deposits,
        decimal Withdrawals,
        decimal? LastBalance,
        /// <summary>LastBalance − (Opening + Deposits − Withdrawals): pesa ambayo balance ya M-Koba
        /// inathibitisha iliingia lakini mstari wake haupo kwenye PDF (mapengo). 0 = mnyororo kamili.</summary>
        decimal? MissingFromStatement,
        List<CashMonth> Months,
        List<CashWithdrawal> WithdrawalList);

    /// <summary>
    /// Daftari la fedha la M-Koba: pesa iliyoingia/iliyotoka kwa kila mwezi na salio.
    /// Linasoma MkobaRawTransactions GHAFI tu — halihusiani na mgawanyo wa michango,
    /// kwa hiyo haliwezi kupingana na taarifa za wanachama.
    /// </summary>
    public async Task<Cashbook> GetCashbookAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await _context.MkobaRawTransactions.AsNoTracking()
            .Where(t => t.GroupId == groupId)
            .OrderBy(t => t.Timestamp).ThenBy(t => t.RowOrder)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new Cashbook(null, null, null, 0, 0, null, null, new List<CashMonth>(), new List<CashWithdrawal>());

        var first = rows[0];
        decimal? opening = first.Balance is { } b ? b - (first.IsWithdrawal ? -first.Amount : first.Amount) : null;
        var deposits = rows.Where(r => !r.IsWithdrawal).Sum(r => r.Amount);
        var withdrawals = rows.Where(r => r.IsWithdrawal).Sum(r => r.Amount);
        var last = rows.LastOrDefault(r => r.Balance.HasValue)?.Balance;
        decimal? missing = opening is { } o && last is { } l ? l - (o + deposits - withdrawals) : null;

        string[] sw = { "Jan", "Feb", "Mac", "Apr", "Mei", "Jun", "Jul", "Ago", "Sep", "Okt", "Nov", "Des" };
        var months = rows
            .GroupBy(r => (r.Timestamp.Year, r.Timestamp.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new CashMonth(
                $"{sw[g.Key.Month - 1]} {g.Key.Year}", g.Key.Year, g.Key.Month,
                g.Where(r => !r.IsWithdrawal).Sum(r => r.Amount), g.Count(r => !r.IsWithdrawal),
                g.Where(r => r.IsWithdrawal).Sum(r => r.Amount), g.Count(r => r.IsWithdrawal),
                g.LastOrDefault(r => r.Balance.HasValue)?.Balance))
            .ToList();

        var wRows = rows.Where(r => r.IsWithdrawal).ToList();
        var wPhones = wRows.Select(r => r.Phone).Distinct().ToList();
        var owners = await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && wPhones.Contains(p.Phone))
            .Join(_context.GroupMembers, p => p.GroupMemberId, m => m.Id, (p, m) => new { p.Phone, m.Id, Name = m.User != null ? m.User.FullName : m.MemberNumber })
            .ToListAsync(ct);
        var list = wRows
            .OrderByDescending(r => r.Timestamp)
            .Select(r =>
            {
                var o = owners.FirstOrDefault(x => x.Phone == r.Phone);
                return new CashWithdrawal(r.Reference, r.Timestamp, r.Amount, r.Phone, r.PayerName, r.Description,
                    o?.Id, o?.Name, r.Balance);
            })
            .ToList();

        return new Cashbook(first.Timestamp, rows[^1].Timestamp, opening, deposits, withdrawals, last, missing, months, list);
    }

    // ------------------------------------------------------------------

    public static TruthPolicy ToTruthPolicy(TruthGroupSettings p)
    {
        var start = p.ContributionStartDate;
        return new TruthPolicy
        {
            MonthlyContribution = p.MonthlyContribution,
            LateFine = p.LateFine,
            JoiningFee = p.JoiningFee,
            DueDay = p.DueDateDay,
            GracePeriodDays = p.GracePeriodDays,
            FineOnPartialPayment = p.FineOnPartialPayment,
            GroupFirstPeriod = new YearMonth(start.Year, start.Month),
            ManualEntryCutoff = p.ManualEntryCutoff is { } c ? Unspecified(c) : null,
            LoanLatePenalty = p.LoanLatePenaltyAmount,
            AutoDeductSavings = p.AutoDeductSavings
        };
    }

    private static TruthMember ToTruthMember(GroupMember m, DateTime effectiveJoin, TruthGroupSettings dbPolicy, TruthPolicy policy)
    {
        bool founder = dbPolicy.FoundingMembersCutoff is null || effectiveJoin <= Unspecified(dbPolicy.FoundingMembersCutoff.Value);
        return new TruthMember
        {
            MemberId = m.Id.ToString(),
            Name = m.User?.FullName ?? m.MemberNumber,
            FirstPeriod = founder ? policy.GroupFirstPeriod : YearMonth.Of(effectiveJoin),
            JoinFeeRequiredUpfront = !founder
        };
    }

    internal static DateTime Unspecified(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Unspecified);

    private static TimeZoneInfo ResolveEat() => MkobaPlatform.ResolveTimeZone();
}

public static class PhoneNumbers
{
    /// <summary>Angalia MkobaPlatform.NormalizePhone.</summary>
    public static string Normalize(string raw) => MkobaPlatform.NormalizePhone(raw);
}
