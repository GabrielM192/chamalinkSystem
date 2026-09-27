// ============================================================================
//  ContributionTruthEngine v2 — "Injini ya Ukweli"
//  ----------------------------------------------------------------------------
//  Injini SAFI (pure): haina DB, haina EF, haina DateTime.UtcNow ndani yake.
//  Ingizo lile lile  ==>  matokeo yale yale, KILA MARA (deterministic replay).
//
//  FORMULA:
//
//  1. DEADLINE ya kipindi M (kwa mwanachama husika):
//        Deadline(M) = tarehe DueDay ya mwezi M+1, 23:59:59
//                      + GracePeriodDays ya kikundi            (Ukonga = 0)
//                      + GraceExtension iliyoidhinishwa ya mwezi M (kama ipo)
//        Dirisha(M)  = ( Deadline(M-1) , Deadline(M) ]
//     Ukonga (grace 0): anayelipa hadi tar 5 saa 23:59:59 yuko kwa wakati;
//     kuanzia tar 6 00:00:00 amechelewa.
//
//  2. DEADLINE IKIPITA (kwa mpangilio huu):
//        a) AKIBA inakatwa kulipa kilichobaki cha kipindi hicho
//           (rejesho la mkopo kwanza, kisha mchango)
//        b) FAINI (5,000) — kama mchango bado haujakamilika NA:
//              - FineOnPartialPayment=false (Ukonga): hakulipa CHOCHOTE kwa wakati
//              - FineOnPartialPayment=true           : hajakamilisha
//           ...isipokuwa kuna MSAMAHA (FineWaiver) ulioidhinishwa na viongozi.
//        c) ADHABU YA MKOPO (LoanLatePenalty, kama kikundi kimeiweka > 0)
//
//  3. UGAWAJI WA KILA MALIPO (waterfall):
//        0) [Mwanachama mpya / Purpose=JoinFee] -> Kianzio
//        1) REJESHO LA MKOPO la vipindi vilivyofunguliwa, kongwe kwanza (+ adhabu yake)
//        2) Mchango(M) -> Faini(M), kongwe kwanza
//        3) Kianzio kilichobaki
//        4) AKIBA (inakatwa moja kwa moja deadline ikipita bila malipo)
//
//  4. CHANZO CHA UKWELI:
//        - M-Koba             : daima (reference ya kipekee)
//        - Pengo la M-Koba    : balance ya M-Koba inathibitisha pesa iliingia; mwenyewe
//                               anathibitishwa na viongozi (lazima Approved)
//        - Manual             : lazima Approved NA tarehe <= ManualEntryCutoff ya kikundi
//
//  5. INVARIANT:
//        Σ malipo = Σ Mchango + Σ Faini + Σ Kianzio + Σ Rejesho + Σ Adhabu + Akiba
// ============================================================================

namespace ChamaLink.Domain.Ledger;

public enum PaymentSource
{
    Mkoba = 1,
    ManualHistorical = 2,
    /// <summary>Makato ya Kianzio kutoka malipo ya Msiba/Sherehe (Reference = withdrawal ya M-Koba).</summary>
    WelfareOffset = 3,
    /// <summary>Muamala uliokosekana kwenye PDF lakini balance ya M-Koba inathibitisha; mwenyewe ameidhinishwa.</summary>
    MkobaGapResolved = 4
}

public enum PaymentPurpose
{
    /// <summary>Mfumo uamue kwa waterfall (default).</summary>
    Auto = 0,
    /// <summary>Pesa iliyotolewa kwa ajili ya Kianzio tu (manual / welfare offset).</summary>
    JoinFee = 1
}

public enum PeriodStatus
{
    NotYetOpen = 0,
    Open = 1,            // dirisha liko wazi, bado si deni
    PaidOnTime = 2,
    PaidLate = 3,
    PartiallyPaid = 4,   // deadline imepita; kuna kiasi, bado kuna deni
    Unpaid = 5
}

public sealed record YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public YearMonth Next() => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);
    public YearMonth Prev() => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);
    public int CompareTo(YearMonth? o) => o is null ? 1 : (Year * 12 + Month).CompareTo(o.Year * 12 + o.Month);
    public static bool operator <(YearMonth a, YearMonth b) => a.CompareTo(b) < 0;
    public static bool operator >(YearMonth a, YearMonth b) => a.CompareTo(b) > 0;
    public static bool operator <=(YearMonth a, YearMonth b) => a.CompareTo(b) <= 0;
    public static bool operator >=(YearMonth a, YearMonth b) => a.CompareTo(b) >= 0;
    public static YearMonth Of(DateTime d) => new(d.Year, d.Month);
    public override string ToString() => $"{Year}-{Month:00}";
    public string Label => new DateTime(Year, Month, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Sheria za fedha za kikundi. HAKUNA thamani ya default kwa kiasi cha pesa, siku ya mwisho au
/// mwezi wa kuanza: kila kikundi LAZIMA kiweke zake (TruthGroupSettings) — vinginevyo kikundi
/// chenye mchango wa 20,000 kingehesabiwa kimya kimya kwa viwango vya kikundi kingine.
/// </summary>
public sealed class TruthPolicy
{
    /// <summary>Mwisho wa siku ya mwisho (sheria: "kufikia tarehe X" = hadi saa 23:59:59 ya siku hiyo).</summary>
    public static readonly TimeSpan EndOfDay = new(23, 59, 59);

    public required decimal MonthlyContribution { get; init; }
    public required decimal LateFine { get; init; }
    public required decimal JoiningFee { get; init; }
    /// <summary>Mchango wa mwezi M unatakiwa kufika tarehe hii ya mwezi M+1.</summary>
    public required int DueDay { get; init; }
    /// <summary>Siku za ziada baada ya DueDay kabla ya faini (0 = faini kuanzia siku inayofuata).</summary>
    public required int GracePeriodDays { get; init; }
    /// <summary>Mwezi wa kwanza wa michango ya kikundi (waanzilishi wanaanzia hapa).</summary>
    public required YearMonth GroupFirstPeriod { get; init; }
    /// <summary>false = aliyelipa sehemu kwa wakati hatozwi faini; kilichobaki kinabaki deni.</summary>
    public required bool FineOnPartialPayment { get; init; }
    public TimeSpan DeadlineTimeOfDay { get; init; } = EndOfDay;
    /// <summary>Manual zenye tarehe baada ya hii zinakataliwa. Null = manual hairuhusiwi.</summary>
    public DateTime? ManualEntryCutoff { get; init; }
    /// <summary>Adhabu kwa rejesho la mkopo lisilolipwa kwa wakati (0 = hakuna).</summary>
    public decimal LoanLatePenalty { get; init; }
    /// <summary>Akiba ikatwe moja kwa moja deadline ikipita bila malipo.</summary>
    public required bool AutoDeductSavings { get; init; }

    public DateTime BaseDeadline(YearMonth p)
    {
        var n = p.Next();
        int day = Math.Min(DueDay, DateTime.DaysInMonth(n.Year, n.Month));
        return new DateTime(n.Year, n.Month, day).Add(DeadlineTimeOfDay).AddDays(GracePeriodDays);
    }

    public void Validate()
    {
        if (MonthlyContribution <= 0) throw new InvalidOperationException("MonthlyContribution lazima iwe > 0");
        if (LateFine < 0 || JoiningFee < 0 || LoanLatePenalty < 0) throw new InvalidOperationException("Viwango haviwezi kuwa hasi");
        if (DueDay is < 1 or > 28) throw new InvalidOperationException("DueDay lazima iwe 1..28");
        if (GracePeriodDays is < 0 or > 25) throw new InvalidOperationException("GracePeriodDays lazima iwe 0..25");
    }
}

public sealed class TruthMember
{
    public required string MemberId { get; init; }
    public required string Name { get; init; }
    /// <summary>Waanzilishi = kipindi cha kwanza cha kikundi (SI tarehe ya malipo ya kwanza).</summary>
    public required YearMonth FirstPeriod { get; init; }
    public bool JoinFeeRequiredUpfront { get; init; }
    public decimal? JoiningFeeOverride { get; init; }
}

public sealed record TruthPayment(
    string Reference,
    DateTime Timestamp,          // saa za EAT (Africa/Dar_es_Salaam)
    decimal Amount,
    PaymentSource Source = PaymentSource.Mkoba,
    PaymentPurpose Purpose = PaymentPurpose.Auto,
    bool IsApproved = true,
    string? Note = null);

/// <summary>Mkopo: ratiba ya marejesho (kipindi -> kiasi). Rejesho la kipindi M linadaiwa kwa Deadline(M).</summary>
public sealed record TruthLoan(string LoanId, DateTime DisbursedAt, decimal TotalRepayable, IReadOnlyList<(YearMonth Period, decimal Amount)> Schedule)
{
    /// <summary>Ratiba sawa na LoanService: miezi kutoka mwezi wa kutolewa hadi mwezi wa mwisho, mafungu sawa,
    /// fungu la mwisho linabeba mabaki ya rounding.</summary>
    public static TruthLoan EqualInstallments(string id, DateTime disbursedAt, DateTime finalDueDate, decimal totalRepayable)
    {
        var start = YearMonth.Of(disbursedAt);
        var end = YearMonth.Of(finalDueDate) < start ? start : YearMonth.Of(finalDueDate);
        int n = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
        var inst = Math.Round(totalRepayable / n, 0, MidpointRounding.AwayFromZero);
        var list = new List<(YearMonth, decimal)>();
        decimal acc = 0; var p = start;
        for (int i = 1; i <= n; i++, p = p.Next())
        {
            var amt = i == n ? totalRepayable - acc : Math.Min(inst, totalRepayable - acc);
            if (amt <= 0) break;
            list.Add((p, amt)); acc += amt;
        }
        return new TruthLoan(id, disbursedAt, totalRepayable, list);
    }
}

public enum AdjustmentKind { FineWaiver = 1, GraceExtension = 2 }

/// <summary>Msamaha wa faini au nyongeza ya muda kwa mwezi fulani — lazima uidhinishwe na viongozi.</summary>
public sealed record TruthAdjustment(AdjustmentKind Kind, YearMonth Period, int ExtraDays, bool IsApproved, string Reason, string Reference);

public sealed record AllocationLine(
    string Reference, DateTime Timestamp, string Target, YearMonth? Period, decimal Amount, string Explanation);

public sealed record RejectedPayment(TruthPayment Payment, string Reason);

public sealed class PeriodLine
{
    public required YearMonth Period { get; init; }
    public DateTime Deadline { get; init; }
    public decimal ContributionDue { get; set; }
    public decimal ContributionPaid { get; set; }
    public decimal FineDue { get; set; }
    public decimal FinePaid { get; set; }
    public decimal LoanDue { get; set; }
    public decimal LoanPaid { get; set; }
    public decimal LoanPenaltyDue { get; set; }
    public decimal LoanPenaltyPaid { get; set; }
    public decimal CoveredBySavings { get; set; }
    public bool FineWaived { get; set; }
    public string? FineWaiverReason { get; set; }
    public int ExtraGraceDays { get; set; }
    public bool WindowOpened { get; set; }
    public bool DeadlinePassed { get; set; }
    public DateTime? ContributionCompletedAt { get; set; }
    public List<string> PaidByReferences { get; } = new();

    public decimal ContributionOutstanding => Math.Max(0, ContributionDue - ContributionPaid);
    public decimal FineOutstanding => Math.Max(0, FineDue - FinePaid);
    public decimal LoanOutstanding => Math.Max(0, LoanDue - LoanPaid) + Math.Max(0, LoanPenaltyDue - LoanPenaltyPaid);
    public decimal Outstanding => ContributionOutstanding + FineOutstanding;
    /// <summary>Seli ya Excel ya mtunza-hazina (mchango + faini).</summary>
    public decimal ExcelCell => ContributionPaid + FinePaid;

    public PeriodStatus Status
    {
        get
        {
            if (!WindowOpened) return PeriodStatus.NotYetOpen;
            if (!DeadlinePassed)
                return ContributionOutstanding == 0 ? PeriodStatus.PaidOnTime : PeriodStatus.Open;
            if (Outstanding == 0)
                return ContributionCompletedAt.HasValue && ContributionCompletedAt.Value <= Deadline
                    ? PeriodStatus.PaidOnTime : PeriodStatus.PaidLate;
            return ContributionPaid + FinePaid > 0 ? PeriodStatus.PartiallyPaid : PeriodStatus.Unpaid;
        }
    }
}

public sealed class LoanLine
{
    public required string LoanId { get; init; }
    public DateTime DisbursedAt { get; init; }
    public decimal TotalRepayable { get; init; }
    public decimal Repaid { get; set; }
    public decimal PenaltyCharged { get; set; }
    public decimal PenaltyPaid { get; set; }
    public decimal Outstanding => Math.Max(0, TotalRepayable - Repaid);
    public decimal OverdueInstallments { get; set; }
}

public sealed class TruthStatement
{
    public required TruthMember Member { get; init; }
    public required DateTime AsOf { get; init; }
    public required List<PeriodLine> Periods { get; init; }
    public required List<LoanLine> Loans { get; init; }
    public required List<AllocationLine> Allocations { get; init; }
    public required List<RejectedPayment> Rejected { get; init; }

    public decimal JoinFeeDue { get; init; }
    public decimal JoinFeePaid { get; init; }
    public decimal JoinFeeOutstanding => Math.Max(0, JoinFeeDue - JoinFeePaid);
    /// <summary>Akiba ya mwanachama (ziada baada ya kila deni na Kianzio).</summary>
    public decimal Savings { get; init; }

    public decimal TotalAccepted { get; init; }
    public decimal TotalAcceptedFromMkoba { get; init; }
    public decimal TotalAcceptedManual { get; init; }

    public decimal TotalContributionPaid => Periods.Sum(p => p.ContributionPaid);
    public decimal TotalFineCharged => Periods.Sum(p => p.FineDue);
    public decimal TotalFinePaid => Periods.Sum(p => p.FinePaid);
    public int FinesWaivedCount => Periods.Count(p => p.FineWaived && p.DeadlinePassed);
    public decimal TotalLoanRepaid => Loans.Sum(l => l.Repaid);
    public decimal TotalLoanPenaltyPaid => Loans.Sum(l => l.PenaltyPaid);
    public decimal LoanOutstanding => Loans.Sum(l => l.Outstanding + Math.Max(0, l.PenaltyCharged - l.PenaltyPaid));
    public decimal LoanOverdue => Periods.Where(p => p.DeadlinePassed).Sum(p => p.LoanOutstanding);

    /// <summary>Deni la michango+faini la vipindi ambavyo deadline imepita.</summary>
    public decimal Arrears => Periods.Where(p => p.DeadlinePassed).Sum(p => p.Outstanding);
    public decimal CurrentDue => Periods.Where(p => p.WindowOpened && !p.DeadlinePassed).Sum(p => p.Outstanding + p.LoanOutstanding);
    public decimal TotalDebtIncludingJoinFee => Arrears + JoinFeeOutstanding + LoanOverdue;
    public IEnumerable<YearMonth> MonthsInArrears => Periods.Where(p => p.DeadlinePassed && p.Outstanding > 0).Select(p => p.Period);

    public decimal InvariantDifference =>
        TotalAccepted - (TotalContributionPaid + TotalFinePaid + JoinFeePaid + TotalLoanRepaid + TotalLoanPenaltyPaid + Savings);
}

public static class ContributionTruthEngine
{
    private enum EvKind { OpenWindow = 0, Payment = 1, Deadline = 2 } // mpangilio ndani ya sekunde moja

    public static TruthStatement Replay(
        TruthMember member,
        IEnumerable<TruthPayment> payments,
        TruthPolicy policy,
        DateTime asOf,
        IEnumerable<TruthLoan>? loans = null,
        IEnumerable<TruthAdjustment>? adjustments = null)
    {
        policy.Validate();
        var firstPeriod = member.FirstPeriod < policy.GroupFirstPeriod ? policy.GroupFirstPeriod : member.FirstPeriod;
        decimal joinFeeDue = member.JoiningFeeOverride ?? policy.JoiningFee;
        decimal joinFeePaid = 0m, savings = 0m;

        var allocations = new List<AllocationLine>();
        var rejected = new List<RejectedPayment>();
        var periods = new List<PeriodLine>();
        var adj = (adjustments ?? Array.Empty<TruthAdjustment>()).Where(a => a.IsApproved).ToList();

        // ---- 1. Chuja malipo ----
        var accepted = new List<TruthPayment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments.OrderBy(p => p.Timestamp).ThenBy(p => p.Reference, StringComparer.Ordinal))
        {
            if (p.Amount <= 0) { rejected.Add(new(p, "Kiasi si chanya")); continue; }
            if (string.IsNullOrWhiteSpace(p.Reference)) { rejected.Add(new(p, "Hakuna Reference")); continue; }
            if (!seen.Add(p.Reference)) { rejected.Add(new(p, "Reference imerudiwa (duplicate) — haihesabiwi mara mbili")); continue; }
            if (p.Timestamp > asOf) continue;
            switch (p.Source)
            {
                case PaymentSource.ManualHistorical:
                    if (!p.IsApproved) { rejected.Add(new(p, "Manual haijaidhinishwa (inasubiri kiongozi wa pili)")); continue; }
                    if (policy.ManualEntryCutoff is null)
                    { rejected.Add(new(p, "Kikundi hakijaweka ManualEntryCutoff — manual haziwezi kukubaliwa")); continue; }
                    if (p.Timestamp > policy.ManualEntryCutoff.Value)
                    { rejected.Add(new(p, $"Manual baada ya cutoff {policy.ManualEntryCutoff:yyyy-MM-dd} — M-Koba ndiyo chanzo cha ukweli")); continue; }
                    break;
                case PaymentSource.WelfareOffset:
                    if (!p.IsApproved) { rejected.Add(new(p, "Makato ya msiba/sherehe hayajaidhinishwa")); continue; }
                    if (p.Purpose != PaymentPurpose.JoinFee) { rejected.Add(new(p, "WelfareOffset lazima iwe Purpose=JoinFee")); continue; }
                    break;
                case PaymentSource.MkobaGapResolved:
                    if (!p.IsApproved) { rejected.Add(new(p, "Pengo la M-Koba halijathibitishwa mwenyewe")); continue; }
                    break;
                case PaymentSource.Mkoba:
                    if (p.Purpose != PaymentPurpose.Auto) { rejected.Add(new(p, "M-Koba haiwezi kuwekewa Purpose kwa mkono")); continue; }
                    break;
            }
            accepted.Add(p);
        }

        // ---- 2. Vipindi + deadline za mwanachama huyu ----
        var events = new List<(DateTime At, EvKind Kind, int Seq, object Item)>();
        int seq = 0;
        DateTime prevDeadline = DateTime.MinValue;
        for (var ym = firstPeriod; ; ym = ym.Next())
        {
            var opens = ym == firstPeriod ? new DateTime(ym.Year, ym.Month, 1) : prevDeadline.AddSeconds(1);
            if (opens > asOf) break;
            int extra = adj.Where(a => a.Kind == AdjustmentKind.GraceExtension && a.Period == ym).Select(a => a.ExtraDays).DefaultIfEmpty(0).Max();
            var waiver = adj.FirstOrDefault(a => a.Kind == AdjustmentKind.FineWaiver && a.Period == ym);
            var line = new PeriodLine
            {
                Period = ym,
                Deadline = policy.BaseDeadline(ym).AddDays(extra),
                ContributionDue = policy.MonthlyContribution,
                ExtraGraceDays = extra,
                FineWaived = waiver is not null,
                FineWaiverReason = waiver?.Reason
            };
            periods.Add(line);
            events.Add((opens, EvKind.OpenWindow, seq++, line));
            if (line.Deadline <= asOf) events.Add((line.Deadline, EvKind.Deadline, seq++, line));
            prevDeadline = line.Deadline;
        }

        // ---- 3. Mikopo: weka marejesho kwenye vipindi ----
        var loanLines = new List<LoanLine>();
        var installments = new List<(PeriodLine Line, LoanLine Loan, decimal Due)>(); // kwa ugawaji
        var instPaid = new Dictionary<(PeriodLine, LoanLine), decimal>();
        var penDue = new Dictionary<(PeriodLine, LoanLine), decimal>();
        foreach (var loan in (loans ?? Array.Empty<TruthLoan>()).Where(l => l.DisbursedAt <= asOf).OrderBy(l => l.DisbursedAt))
        {
            var ll = new LoanLine { LoanId = loan.LoanId, DisbursedAt = loan.DisbursedAt, TotalRepayable = loan.TotalRepayable };
            loanLines.Add(ll);
            foreach (var (period, amount) in loan.Schedule)
            {
                var line = periods.FirstOrDefault(p => p.Period == period);
                if (line is null) continue; // kipindi hakijafika bado (au kabla ya mwanachama)
                line.LoanDue += amount;
                installments.Add((line, ll, amount));
                instPaid[(line, ll)] = 0m;
                penDue[(line, ll)] = 0m;
            }
        }

        foreach (var p in accepted) events.Add((p.Timestamp, EvKind.Payment, seq++, p));

        // ---- 4. Replay ----
        foreach (var ev in events.OrderBy(e => e.At).ThenBy(e => e.Kind).ThenBy(e => e.Seq))
        {
            switch (ev.Kind)
            {
                case EvKind.OpenWindow:
                    ((PeriodLine)ev.Item).WindowOpened = true;
                    break;

                case EvKind.Deadline:
                {
                    var line = (PeriodLine)ev.Item;
                    // a) Akiba inakatwa kwanza (kabla ya faini)
                    if (policy.AutoDeductSavings && savings > 0 && (line.ContributionOutstanding > 0 || InstallmentsOutstanding(line) > 0))
                    {
                        var s = savings; savings = 0;
                        var left = PayLoans(s, "AKIBA", ev.At, "Akiba", onlyLine: line);
                        left = PayContribution(line, left, "AKIBA", ev.At, "Akiba", fromSavings: true);
                        savings += left;
                    }
                    line.DeadlinePassed = true;
                    // b) Faini
                    bool unpaid = line.ContributionOutstanding > 0;
                    bool paidNothing = line.ContributionPaid == 0;
                    if (unpaid && (policy.FineOnPartialPayment || paidNothing) && policy.LateFine > 0)
                    {
                        if (line.FineWaived)
                            allocations.Add(new($"WAIVER-{line.Period}", ev.At, "FineWaived", line.Period, 0m,
                                $"Faini ya {line.Period.Label} imesamehewa na viongozi: {line.FineWaiverReason}"));
                        else
                            line.FineDue = policy.LateFine;
                    }
                    // c) Adhabu ya mkopo
                    if (policy.LoanLatePenalty > 0)
                        foreach (var inst in installments.Where(i => i.Line == line && instPaid[(i.Line, i.Loan)] < i.Due))
                        {
                            penDue[(inst.Line, inst.Loan)] += policy.LoanLatePenalty;
                            line.LoanPenaltyDue += policy.LoanLatePenalty;
                            inst.Loan.PenaltyCharged += policy.LoanLatePenalty;
                        }
                    break;
                }

                case EvKind.Payment:
                {
                    var p = (TruthPayment)ev.Item;
                    var label = p.Source switch
                    {
                        PaymentSource.Mkoba => "M-Koba",
                        PaymentSource.WelfareOffset => "Makato ya msiba/sherehe",
                        PaymentSource.MkobaGapResolved => "M-Koba (pengo lililothibitishwa)",
                        _ => "Manual (imeidhinishwa)"
                    };
                    Allocate(p.Amount, p.Reference, p.Timestamp, p.Purpose, label);
                    break;
                }
            }
        }

        foreach (var ll in loanLines)
            ll.OverdueInstallments = installments.Where(i => i.Loan == ll && i.Line.DeadlinePassed)
                .Sum(i => Math.Max(0, i.Due - instPaid[(i.Line, i.Loan)]));

        var stmt = new TruthStatement
        {
            Member = member,
            AsOf = asOf,
            Periods = periods,
            Loans = loanLines,
            Allocations = allocations,
            Rejected = rejected,
            JoinFeeDue = joinFeeDue,
            JoinFeePaid = joinFeePaid,
            Savings = savings,
            TotalAccepted = accepted.Sum(p => p.Amount),
            TotalAcceptedFromMkoba = accepted.Where(p => p.Source is PaymentSource.Mkoba or PaymentSource.MkobaGapResolved).Sum(p => p.Amount),
            TotalAcceptedManual = accepted.Where(p => p.Source is PaymentSource.ManualHistorical or PaymentSource.WelfareOffset).Sum(p => p.Amount),
        };

        if (stmt.InvariantDifference != 0)
            throw new InvalidOperationException(
                $"INVARIANT IMEVUNJIKA kwa {member.Name}: tofauti {stmt.InvariantDifference:N0}. Ripoti haitolewi.");
        return stmt;

        // ------------------------------------------------------------------
        decimal InstallmentsOutstanding(PeriodLine line) =>
            installments.Where(i => i.Line == line).Sum(i => Math.Max(0, i.Due - instPaid[(i.Line, i.Loan)]));

        void Allocate(decimal amount, string reference, DateTime at, PaymentPurpose purpose, string src)
        {
            decimal remaining = amount;

            // 0) Kianzio kwanza (mwanachama mpya au Purpose=JoinFee)
            if (purpose == PaymentPurpose.JoinFee || member.JoinFeeRequiredUpfront)
                remaining = PayJoinFee(remaining, reference, at, purpose == PaymentPurpose.JoinFee
                    ? $"{src}: imeelekezwa Kianzio" : $"{src}: mwanachama mpya — Kianzio kwanza");

            // 1) Rejesho la mkopo (vipindi vilivyofunguliwa, kongwe kwanza)
            remaining = PayLoans(remaining, reference, at, src, onlyLine: null);

            // 2) Mchango -> Faini, kongwe kwanza
            foreach (var line in periods.Where(l => l.WindowOpened))
            {
                if (remaining <= 0) break;
                remaining = PayContribution(line, remaining, reference, at, src, fromSavings: false);
                if (remaining <= 0) break;
                if (line.FineOutstanding > 0)
                {
                    var a = Math.Min(remaining, line.FineOutstanding);
                    line.FinePaid += a; remaining -= a;
                    if (!line.PaidByReferences.Contains(reference)) line.PaidByReferences.Add(reference);
                    allocations.Add(new(reference, at, "Fine", line.Period, a, $"{src}: faini ya {line.Period.Label}"));
                }
            }

            // 3) Kianzio
            if (remaining > 0) remaining = PayJoinFee(remaining, reference, at, $"{src}: ziada -> Kianzio");

            // 4) Akiba
            if (remaining > 0)
            {
                savings += remaining;
                allocations.Add(new(reference, at, "Savings", null, remaining, $"{src}: ziada -> Akiba (itakatwa mwezi usipolipa)"));
            }
        }

        decimal PayContribution(PeriodLine line, decimal remaining, string reference, DateTime at, string src, bool fromSavings)
        {
            if (remaining <= 0 || line.ContributionOutstanding <= 0) return remaining;
            var a = Math.Min(remaining, line.ContributionOutstanding);
            line.ContributionPaid += a;
            if (fromSavings) line.CoveredBySavings += a;
            if (line.ContributionOutstanding == 0) line.ContributionCompletedAt = at;
            line.PaidByReferences.Add(reference);
            allocations.Add(new(reference, at, "Contribution", line.Period, a,
                $"{src}: mchango {line.Period.Label}" + (line.DeadlinePassed ? " (deni la nyuma)" : "")));
            return remaining - a;
        }

        decimal PayLoans(decimal remaining, string reference, DateTime at, string src, PeriodLine? onlyLine)
        {
            foreach (var inst in installments
                         .Where(i => i.Line.WindowOpened && (onlyLine is null || i.Line == onlyLine))
                         .OrderBy(i => i.Line.Period).ThenBy(i => i.Loan.DisbursedAt))
            {
                if (remaining <= 0) break;
                var key = (inst.Line, inst.Loan);
                var owe = inst.Due - instPaid[key];
                if (owe > 0)
                {
                    var a = Math.Min(remaining, owe);
                    instPaid[key] += a; inst.Line.LoanPaid += a; inst.Loan.Repaid += a; remaining -= a;
                    if (!inst.Line.PaidByReferences.Contains(reference)) inst.Line.PaidByReferences.Add(reference);
                    allocations.Add(new(reference, at, "LoanRepayment", inst.Line.Period, a,
                        $"{src}: rejesho la mkopo {inst.Loan.LoanId} ({inst.Line.Period.Label})"));
                }
                if (remaining <= 0) break;
                var pen = penDue[key];
                if (pen > 0)
                {
                    var a = Math.Min(remaining, pen);
                    penDue[key] -= a; inst.Line.LoanPenaltyPaid += a; inst.Loan.PenaltyPaid += a; remaining -= a;
                    allocations.Add(new(reference, at, "LoanPenalty", inst.Line.Period, a, $"{src}: adhabu ya rejesho {inst.Line.Period.Label}"));
                }
            }
            return remaining;
        }

        decimal PayJoinFee(decimal remaining, string reference, DateTime at, string why)
        {
            var outstanding = Math.Max(0, joinFeeDue - joinFeePaid);
            if (remaining <= 0 || outstanding <= 0) return remaining;
            var a = Math.Min(remaining, outstanding);
            joinFeePaid += a;
            allocations.Add(new(reference, at, "JoinFee", null, a, why));
            return remaining - a;
        }
    }
}

/// <summary>
/// Msiba/Sherehe: kabla mwanachama hajapewa kiasi chake, deni la Kianzio linakatwa kwanza
/// (hiari: arrears za michango na marejesho ya mkopo yaliyochelewa).
/// </summary>
public static class WelfareDeductionRule
{
    public sealed record Result(decimal Approved, decimal JoinFeeDeducted, decimal ArrearsDeducted, decimal NetPayout);

    public static Result Compute(decimal approvedAmount, TruthStatement statement, bool alsoDeductArrears = false)
    {
        if (approvedAmount < 0) throw new ArgumentOutOfRangeException(nameof(approvedAmount));
        var jf = Math.Min(approvedAmount, statement.JoinFeeOutstanding);
        var left = approvedAmount - jf;
        var arr = alsoDeductArrears ? Math.Min(left, statement.Arrears + statement.LoanOverdue) : 0m;
        return new Result(approvedAmount, jf, arr, left - arr);
    }
}

/// <summary>
/// Ukaguzi wa mnyororo wa balance wa statement ya M-Koba:
///   balance(i) = balance(i-1) + deposit(i)   au   balance(i-1) − withdrawal(i)
/// Kila pale mnyororo unapovunjika = MUAMALA UMEKOSEKANA kwenye PDF ("pengo").
/// Pesa ya pengo ni ya kweli (balance ya M-Koba inaithibitisha) lakini mwenyewe
/// hajulikani — inabaki "Unattributed" mpaka viongozi waithibitishe.
/// </summary>
public static class StatementIntegrity
{
    public sealed record Row(string Reference, DateTime Timestamp, decimal Amount, bool IsWithdrawal, decimal? Balance, int Order);
    public sealed record Gap(string? AfterReference, string BeforeReference, DateTime WindowStart, DateTime WindowEnd,
        decimal BalanceBefore, decimal ExpectedBalanceBefore, decimal Amount);

    public static List<Gap> FindGaps(IEnumerable<Row> rows)
    {
        var gaps = new List<Gap>();
        Row? prev = null;
        foreach (var r in rows.Where(r => r.Balance.HasValue).OrderBy(r => r.Timestamp).ThenBy(r => r.Order))
        {
            if (prev is not null)
            {
                var impliedBefore = r.Balance!.Value - (r.IsWithdrawal ? -r.Amount : r.Amount);
                var diff = impliedBefore - prev.Balance!.Value;
                if (diff != 0)
                    gaps.Add(new Gap(prev.Reference, r.Reference, prev.Timestamp, r.Timestamp, prev.Balance.Value, impliedBefore, diff));
            }
            prev = r;
        }
        return gaps;
    }
}
