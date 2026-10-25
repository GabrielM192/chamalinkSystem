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
               // SASISHO17: jibu kwa mtumiaji halitaji GUID wala njia za API (hakuna ramani ya ndani kwa mshambuliaji)
               "Kikundi hakijaweka mipangilio ya fedha. Viongozi waweke kanuni kwanza.");

    /// <summary>
    /// SASISHO16: taarifa ya Injini ya Ukweli kuhusu mwanachama — msimbo thabiti + ujumbe unaotafsirika (Tpl/Args),
    /// kwa lugha ya biashara (si majina ya safu za DB). Kind: "notice" (mwanachama mwenyewe anaweza kuona) au
    /// "dataQuality" (ubora wa data — viongozi/wakaguzi tu).
    /// </summary>
    public sealed record TruthNotice(string Code, string Kind, string Message, string Tpl, string[] Args)
    {
        public const string KindNotice = "notice";
        public const string KindDataQuality = "dataQuality";
        public static TruthNotice Of(string code, string kind, ChamaLink.Domain.Localization.Loc l) => new(code, kind, l.Text, l.Template, l.Args.ToArray());
    }

    public sealed record MemberTruth(Guid MemberId, string MemberNumber, IReadOnlyList<string> Phones, TruthStatement Statement, List<string> Warnings)
    {
        /// <summary>Tarehe halisi ya kujiunga (ya mapema kati ya JoinedAt na malipo ya kwanza yaliyothibitishwa), EAT.</summary>
        public DateTime EffectiveJoin { get; init; }
        /// <summary>Tarehe ambayo majukumu yalikoma (kujitoa/kifo/kufukuzwa kulikoidhinishwa). Null = bado ni mwanachama.</summary>
        public DateTime? ObligationsEndAt { get; init; }
        /// <summary>SASISHO16: taarifa zenye msimbo (Warnings = maandishi yake ya Kiswahili, kwa urithi).</summary>
        public List<TruthNotice> Notices { get; init; } = new();
    }

    /// <summary>Hali ambazo mwanachama hana majukumu mapya (baada ya tarehe ya mabadiliko).</summary>
    public static readonly MemberStatus[] TerminalStatuses = { MemberStatus.Exited, MemberStatus.Deceased, MemberStatus.Archived };

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
        // SASISHO18: kikundi cha "kila tukio" hakina vipindi vya mwezi.
        var eventBased = await IsEventBasedAsync(member.GroupId, ct);
        return Compute(member, raw, dbPolicy, ToTruthPolicy(dbPolicy, eventBased), asOf, hypotheticalPayments);
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
        var policy = ToTruthPolicy(dbPolicy, await IsEventBasedAsync(groupId, ct));
        var raw = await LoadRawAsync(groupId, members.Select(m => m.Id).ToList(), ct);
        return members.Select(m => Compute(m, raw, dbPolicy, policy, asOf, null)).ToList();
    }

    /// <summary>Data ghafi ya wanachama waliotajwa (mmoja au wote) — hakuna hesabu hapa.</summary>
    private sealed record RawData(
        ILookup<Guid, MemberPhone> PhonesByMember,
        ILookup<string, MkobaRawTransaction> DepositsByPhone,
        /// <summary>SASISHO21: amana zilizothibitishwa kuwa za mwanachama (hata kama namba si yake).</summary>
        ILookup<Guid, MkobaRawTransaction> DepositsByOwner,
        ILookup<Guid, BenefitOffset> BenefitOffsetsByMember,
        Dictionary<Guid, DateTime> ObligationsEnd,
        ILookup<Guid, MkobaStatementGap> GapsByMember,
        ILookup<Guid, FinancialEvent> ManualByMember,
        ILookup<Guid, Loan> LoansByMember,
        List<TruthAdjustmentRecord> Adjustments);

    /// <summary>Makato ya kesi moja ya mafao kama yalivyoandikwa kwenye daftari lisilobadilika (BenefitLedgerEntries).</summary>
    private sealed record BenefitOffset(Guid MemberId, string CaseNumber, DateTime At, decimal JoinFee, decimal Arrears, decimal LoanArrears, bool LegacyArrears);

    private async Task<RawData> LoadRawAsync(Guid groupId, IReadOnlyCollection<Guid> memberIds, CancellationToken ct)
    {
        // Namba zilizoidhinishwa na kiongozi wa pili TU (Pending/Rejected hazihesabiwi); muda wa umiliki unaheshimiwa
        var phoneRows = await _context.MemberPhones.AsNoTracking()
            .Where(p => memberIds.Contains(p.GroupMemberId) && p.Status == TruthApprovalStatus.Approved)
            .ToListAsync(ct);
        var allPhones = phoneRows.Select(p => p.Phone).Distinct().ToList();

        // 1. M-Koba deposits. SASISHO21: mstari wenye MMILIKI aliyethibitishwa
        // (AttributedGroupMemberId) unamilikiwa na mtu HUYO tu — hauna kuhesabiwa kwa namba pia.
        var deposits = await _context.MkobaRawTransactions.AsNoTracking()
            .Where(t => t.GroupId == groupId && !t.IsWithdrawal
                        && ((t.AttributedGroupMemberId == null && allPhones.Contains(t.Phone))
                            || (t.AttributedGroupMemberId != null && memberIds.Contains(t.AttributedGroupMemberId.Value))))
            .ToListAsync(ct);
        var byPhone = deposits.Where(d => d.AttributedGroupMemberId == null).ToList();
        var byOwner = deposits.Where(d => d.AttributedGroupMemberId != null).ToList();

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

        // 6. Makato ya mafao (Kianzio/deni lililokatwa kwenye msiba/sherehe) — kesi zilizolipwa na kuthibitishwa
        // SASISHO15 (P1): chanzo ni DAFTARI LA MAFAO (BenefitLedgerEntries — linaandikwa mara moja kesi inapolipwa, halibadiliki),
        // si safu za BenefitCase zinazoweza kuhaririwa. Kesi ya zamani iliyolipwa bila daftari (kabla ya SASISHO13) inasomwa kutoka kesi.
        var paidCases = await _context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Status == BenefitCaseStatus.Paid && memberIds.Contains(c.BeneficiaryGroupMemberId))
            .Select(c => new { c.Id, c.BeneficiaryGroupMemberId, c.CaseNumber, c.PaidAt, c.ApprovedAt, c.CreatedAt, c.JoinFeeDeducted, c.ArrearsDeducted, c.LoanArrearsDeducted })
            .ToListAsync(ct);
        var caseIds = paidCases.Select(c => c.Id).ToList();
        var ledger = (await _context.BenefitLedgerEntries.AsNoTracking()
                .Where(e => caseIds.Contains(e.BenefitCaseId)).Select(e => new { e.BenefitCaseId, e.Kind, e.Amount }).ToListAsync(ct))
            .ToLookup(e => e.BenefitCaseId);
        var offsets = new List<BenefitOffset>();
        foreach (var c in paidCases)
        {
            var at = c.PaidAt ?? c.ApprovedAt ?? c.CreatedAt;
            var rows = ledger[c.Id].ToList();
            var o = rows.Count > 0
                ? new BenefitOffset(c.BeneficiaryGroupMemberId, c.CaseNumber, at,
                    rows.Where(r => r.Kind == BenefitLedgerKind.JoinFeeOffset).Sum(r => r.Amount),
                    rows.Where(r => r.Kind == BenefitLedgerKind.ArrearsOffset).Sum(r => r.Amount),
                    rows.Where(r => r.Kind == BenefitLedgerKind.LoanArrearsOffset).Sum(r => r.Amount),
                    c.LoanArrearsDeducted is null)
                : new BenefitOffset(c.BeneficiaryGroupMemberId, c.CaseNumber, at, c.JoinFeeDeducted, c.ArrearsDeducted, c.LoanArrearsDeducted ?? 0m, c.LoanArrearsDeducted is null);
            if (o.JoinFee > 0 || o.Arrears > 0 || o.LoanArrears > 0) offsets.Add(o);
        }

        // 7. Kujitoa/kifo/kufukuzwa kulikoidhinishwa: tarehe ya mwisho ya majukumu
        var terminalMembers = await _context.GroupMembers.AsNoTracking()
            .Where(m => memberIds.Contains(m.Id) && TerminalStatuses.Contains(m.Status)).Select(m => new { m.Id, m.Status }).ToListAsync(ct);
        var ends = new Dictionary<Guid, DateTime>();
        if (terminalMembers.Count > 0)
        {
            var ids = terminalMembers.Select(t => t.Id).ToList();
            var hist = await _context.MemberStatusHistories.AsNoTracking().Where(h => ids.Contains(h.MemberId)).ToListAsync(ct);
            foreach (var t in terminalMembers)
            {
                var h = hist.Where(x => x.MemberId == t.Id && x.ToStatus == t.Status).OrderByDescending(x => x.ChangedAt).FirstOrDefault();
                if (h is not null) ends[t.Id] = Unspecified(h.EffectiveFrom);
            }
        }

        return new RawData(
            phoneRows.ToLookup(p => p.GroupMemberId),
            byPhone.ToLookup(d => d.Phone),
            byOwner.ToLookup(d => d.AttributedGroupMemberId!.Value),
            offsets.ToLookup(c => c.MemberId),
            ends,
            gaps.ToLookup(g => g.AssignedGroupMemberId!.Value),
            manual.ToLookup(e => e.MemberId!.Value),
            loans.ToLookup(l => l.GroupMemberId),
            adj);
    }

    /// <summary>Hesabu ya mwanachama mmoja kutoka data ghafi iliyokwisha pakiwa (hakuna DB hapa).</summary>
    private static MemberTruth Compute(GroupMember member, RawData raw, TruthGroupSettings dbPolicy, TruthPolicy policy,
        DateTime asOf, IReadOnlyList<TruthPayment>? hypotheticalPayments)
    {
        var links = raw.PhonesByMember[member.Id].ToList();
        var phones = links.Select(l => l.Phone).Distinct().ToList();
        var notices = new List<TruthNotice>();
        if (phones.Count == 0)
            notices.Add(TruthNotice.Of("NO_APPROVED_PHONE", TruthNotice.KindDataQuality,
                $"Mwanachama hana namba ya simu iliyoidhinishwa, kwa hiyo malipo yake ya M-Koba hayawezi kuonekana. Kiongozi aweke namba, na kiongozi wa pili aidhinishe."));

        var payments = new List<TruthPayment>();

        // Muamala unahesabiwa kwa mwanachama huyu tu kama kiungo chake kinafunika SAA ya muamala (ValidFrom..ValidTo)
        payments.AddRange(phones.SelectMany(ph => raw.DepositsByPhone[ph])
            .Where(d => links.Any(l => l.Phone == d.Phone && l.Covers(Unspecified(d.Timestamp))))
            .DistinctBy(d => d.Reference)
            .Select(d => new TruthPayment(d.Reference, Unspecified(d.Timestamp), d.Amount, PaymentSource.Mkoba)));

        // SASISHO21: amana zilizothibitishwa kwenye sanduku la malipo (kodi ya malipo / uamuzi wa viongozi).
        payments.AddRange(raw.DepositsByOwner[member.Id]
            .DistinctBy(d => d.Reference)
            .Select(d => new TruthPayment(d.Reference, Unspecified(d.Timestamp), d.Amount, PaymentSource.Mkoba)));

        // Makato ya mafao: Kianzio kilichokatwa = Kianzio kimelipwa; deni lililokatwa = malipo (waterfall kamili)
        foreach (var c in raw.BenefitOffsetsByMember[member.Id])
        {
            var at = Unspecified(c.At);
            if (c.JoinFee > 0)
                payments.Add(new TruthPayment($"MAFAO-JF-{c.CaseNumber}", at, c.JoinFee, PaymentSource.WelfareOffset,
                    PaymentPurpose.JoinFee, IsApproved: true, Note: $"Kianzio kimekatwa kwenye mafao {c.CaseNumber}"));
            // SASISHO14: LoanArrearsDeducted == null => kesi ya zamani (deni lilijumuisha mkopo -> Auto).
            // Kesi mpya: deni la michango -> Contributions (mchango/faini tu); deni la mkopo -> rejea yake (Auto: mkopo kwanza).
            if (c.Arrears > 0)
                payments.Add(new TruthPayment($"MAFAO-DENI-{c.CaseNumber}", at, c.Arrears, PaymentSource.WelfareOffset,
                    c.LegacyArrears ? PaymentPurpose.Auto : PaymentPurpose.Contributions, IsApproved: true,
                    Note: $"Deni la michango limekatwa kwenye mafao {c.CaseNumber}"));
            if (c.LoanArrears > 0)
                payments.Add(new TruthPayment($"MAFAO-MKOPO-{c.CaseNumber}", at, c.LoanArrears, PaymentSource.WelfareOffset,
                    PaymentPurpose.Auto, IsApproved: true, Note: $"Deni la mkopo limekatwa kwenye mafao {c.CaseNumber}"));
        }

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
            notices.Add(TruthNotice.Of("MEMBERSHIP_START_ADJUSTED", TruthNotice.KindDataQuality,
                $"Tarehe ya kuanza uanachama imechukuliwa kutoka malipo ya kwanza yaliyothibitishwa ({f0:dd/MM/yyyy}), kwa sababu tarehe iliyosajiliwa ({joinedEat:dd/MM/yyyy}) ni ya baadaye."));

        DateTime? endAt = raw.ObligationsEnd.TryGetValue(member.Id, out var e0) ? e0 : null;
        if (endAt is { } ea)
            notices.Add(TruthNotice.Of("OBLIGATIONS_ENDED", TruthNotice.KindNotice,
                $"Hali ya uanachama: {Benefits.MemberStatusService.StatusName(member.Status)} kuanzia {ea:dd/MM/yyyy}. Hakuna michango mipya baada ya tarehe hiyo; madeni ya kabla yanabaki."));
        var statement = ContributionTruthEngine.Replay(ToTruthMember(member, effectiveJoin, dbPolicy, policy, endAt), payments, policy, asOf, truthLoans, truthAdj);
        return new MemberTruth(member.Id, member.MemberNumber, phones, statement, notices.Select(n => n.Message).ToList())
            { EffectiveJoin = effectiveJoin, ObligationsEndAt = endAt, Notices = notices };
    }

    /// <summary>Pesa iliyoingia M-Koba lakini haijulikani ni ya nani (namba haijawekwa / pengo la PDF).</summary>
    public async Task<UnattributedReport> GetUnattributedAsync(Guid groupId, CancellationToken ct = default)
    {
        // Muamala "haujulikani" kama HAKUNA kiungo kilichoidhinishwa kinachofunika saa yake (namba haijawekwa,
        // bado inasubiri idhini, au ilifungwa kabla ya muamala huu).
        var links = await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Status == TruthApprovalStatus.Approved).ToListAsync(ct);
        var linkedPhones = links.Select(l => l.Phone).ToHashSet();
        var unknown = (await _context.MkobaRawTransactions.AsNoTracking()
                .Where(t => t.GroupId == groupId && !t.IsWithdrawal)
                .OrderBy(t => t.Timestamp).ToListAsync(ct))
            .Where(t => t.AttributedGroupMemberId == null
                        && (!linkedPhones.Contains(t.Phone) || !links.Any(l => l.Phone == t.Phone && l.Covers(Unspecified(t.Timestamp)))))
            .ToList();
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
            .Where(p => p.GroupId == groupId && wPhones.Contains(p.Phone) && p.Status == TruthApprovalStatus.Approved)
            .Join(_context.GroupMembers, p => p.GroupMemberId, m => m.Id, (p, m) => new { p.Phone, m.Id, Name = m.User != null ? m.User.FullName : m.MemberNumber, Link = p })
            .ToListAsync(ct);
        var list = wRows
            .OrderByDescending(r => r.Timestamp)
            .Select(r =>
            {
                var o = owners.FirstOrDefault(x => x.Phone == r.Phone && x.Link.Covers(Unspecified(r.Timestamp)));
                return new CashWithdrawal(r.Reference, r.Timestamp, r.Amount, r.Phone, r.PayerName, r.Description,
                    o?.Id, o?.Name, r.Balance);
            })
            .ToList();

        return new Cashbook(first.Timestamp, rows[^1].Timestamp, opening, deposits, withdrawals, last, missing, months, list);
    }

    // ------------------------------------------------------------------

    /// <summary>SASISHO18: kikundi hiki ni cha "kila tukio"? (hakuna mchango wa mwezi)</summary>
    private async Task<bool> IsEventBasedAsync(Guid groupId, CancellationToken ct)
        => await _context.Groups.AsNoTracking()
               .Where(g => g.Id == groupId)
               .Select(g => g.Type == GroupType.EventBased)
               .FirstOrDefaultAsync(ct);

    public static TruthPolicy ToTruthPolicy(TruthGroupSettings p, bool isEventBased = false)
    {
        var start = p.ContributionStartDate;
        return new TruthPolicy
        {
            IsEventBased = isEventBased,
            MonthlyContribution = p.MonthlyContribution,
            LateFine = p.LateFine,
            LateFinePercent = p.LateFinePercent,
            // SASISHO21: sehemu ya ada ya kujiunga iliyo AKIBA ya mwanachama haitozwi kama kianzio.
            // Mf. J3: 30,000 = 20,000 akiba + 10,000 kianzio -> injini inatoza 10,000 pekee.
            // Sera ya kikundi ndiyo inaamua; 0 (chaguo-msingi) = tabia ya zamani kabisa.
            JoiningFee = Math.Max(0m, p.JoiningFee - Math.Max(0m, p.JoiningSavingsPortion)),
            // SASISHO21: kikundi cha "kila tukio" hakina tarehe ya mwezi; 0 si kosa — tunatumia 1 kama nafasi.
            DueDay = p.DueDateDay is >= 1 and <= 28 ? p.DueDateDay : 1,
            GracePeriodDays = p.GracePeriodDays,
            FineOnPartialPayment = p.FineOnPartialPayment,
            GroupFirstPeriod = new YearMonth(start.Year, start.Month),
            ManualEntryCutoff = p.ManualEntryCutoff is { } c ? Unspecified(c) : null,
            LoanLatePenalty = p.LoanLatePenaltyAmount,
            AutoDeductSavings = p.AutoDeductSavings
        };
    }

    private static TruthMember ToTruthMember(GroupMember m, DateTime effectiveJoin, TruthGroupSettings dbPolicy, TruthPolicy policy, DateTime? obligationsEndAt)
    {
        bool founder = dbPolicy.FoundingMembersCutoff is null || effectiveJoin <= Unspecified(dbPolicy.FoundingMembersCutoff.Value);
        return new TruthMember
        {
            MemberId = m.Id.ToString(),
            Name = m.User?.FullName ?? m.MemberNumber,
            FirstPeriod = founder ? policy.GroupFirstPeriod : YearMonth.Of(effectiveJoin),
            JoinFeeRequiredUpfront = !founder,
            ObligationsEndAt = obligationsEndAt
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
