using ChamaLink.Domain.Ledger;

namespace ChamaLink.Tests;

/// <summary>
/// Majaribio haya yanafunga UKWELI uliothibitishwa na viongozi wa Ukonga
/// (Excel ya mtunza-hazina + M-Koba PDF). Kama code ikibadilika na kuvunja
/// hesabu hizi, build inafeli — taarifa za uongo haziwezi kufika production.
/// </summary>
public class ContributionTruthEngineTests
{
    private static readonly TruthPolicy Ukonga = new()
    {
        MonthlyContribution = 10_000m,
        LateFine = 5_000m,
        JoiningFee = 50_000m,
        DueDay = 5,
        GracePeriodDays = 0,            // Ukonga: hakuna grace
        FineOnPartialPayment = false,   // aliyelipa kwa wakati (hata sehemu) hatozwi faini
        AutoDeductSavings = true,       // ziada ya zamani inalipa mwezi usiolipwa
        GroupFirstPeriod = new YearMonth(2026, 1),
        ManualEntryCutoff = new DateTime(2026, 3, 31, 23, 59, 59)
    };

    private static readonly DateTime AsOf = new(2026, 9, 25, 12, 0, 0);

    private static TruthMember Founder(string name) =>
        new() { MemberId = name, Name = name, FirstPeriod = new YearMonth(2026, 1) };

    private static PeriodLine P(TruthStatement s, int month) => s.Periods.Single(p => p.Period.Month == month);

    // Malipo halisi ya Frank (M-Koba PDF + taarifa ya viongozi kwa Aug 4 na Sep)
    private static readonly TruthPayment[] FrankPayments =
    {
        new("DB44MPHC7XI", new(2026, 2, 4, 19, 33, 49), 10_000m),   // Jan (kabla ya Feb 5)
        new("DC40NA6VY5G", new(2026, 3, 4, 15, 21, 54), 10_000m),   // Feb
        new("DD3B70DB6G",  new(2026, 4, 3, 12, 14, 58), 10_000m),   // Mar
        new("DG4B71O0UO",  new(2026, 7, 4, 22, 18, 11), 15_000m),   // dirisha la Jun -> deni la Apr
        new("AUG-04-FRANK", new(2026, 8, 4, 10, 0, 0), 15_000m),    // dirisha la Jul -> deni la May (PDF ya Aug)
        new("SEP-FRANK",   new(2026, 9, 3, 10, 0, 0), 15_000m),     // dirisha la Aug -> deni la Jun
        new("MANUAL-JF-FRANK", new(2026, 3, 15), 10_000m, PaymentSource.ManualHistorical,
            PaymentPurpose.JoinFee, IsApproved: true, Note: "Kianzio mkononi kwa mtunza-hazina"),
    };

    private static readonly TruthPayment[] TunganegePayments =
    {
        new("DAV7MMA7IG7", new(2026, 1, 31, 11, 43, 38), 10_000m),  // Jan
        new("DC39N9ITZ9V", new(2026, 3, 3, 16, 59, 46), 10_000m),   // Feb (kwa wakati)
        new("DD4JP0DMNF",  new(2026, 4, 4, 8, 35, 19), 60_000m),    // Mar 10k + Kianzio 50k
        new("DE2JP0S1EL",  new(2026, 5, 2, 6, 44, 41), 10_000m),    // Apr
        new("DFOJP1HA89",  new(2026, 6, 24, 17, 59, 27), 25_000m),  // May 15k (deni+faini) + Jun 10k
        new("DGOJP1VX3T",  new(2026, 7, 24, 22, 42, 20), 10_000m),  // Jul
        new("SEP-TUNGANEGE", new(2026, 9, 2, 10, 0, 0), 10_000m),   // Aug (kwa wakati)
    };

    [Fact]
    public void Frank_MatchesLeadersTruth()
    {
        var s = ContributionTruthEngine.Replay(Founder("Frank Mkoma"), FrankPayments, Ukonga, AsOf);

        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 1).Status);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 2).Status);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 3).Status);

        foreach (var m in new[] { 4, 5, 6 })
        {
            Assert.Equal(PeriodStatus.PaidLate, P(s, m).Status);
            Assert.Equal(15_000m, P(s, m).ExcelCell);                 // Excel: APR/MAY/JUN = 15000
        }
        Assert.Equal("DG4B71O0UO", P(s, 4).PaidByReferences.Single());  // malipo ya Jul 4 -> Apr
        Assert.Equal("AUG-04-FRANK", P(s, 5).PaidByReferences.Single());
        Assert.Equal("SEP-FRANK", P(s, 6).PaidByReferences.Single());

        foreach (var m in new[] { 7, 8 })
        {
            Assert.Equal(PeriodStatus.Unpaid, P(s, m).Status);
            Assert.Equal(15_000m, P(s, m).Outstanding);               // 10k + 5k faini
        }
        Assert.Equal(PeriodStatus.Open, P(s, 9).Status);              // Sep: bado si deni (deadline Oct 5)

        Assert.Equal(30_000m, s.Arrears);
        Assert.Equal(new[] { 7, 8 }, s.MonthsInArrears.Select(p => p.Month));
        Assert.Equal(10_000m, s.JoinFeePaid);
        Assert.Equal(40_000m, s.JoinFeeOutstanding);
        Assert.Equal(60_000m, s.TotalContributionPaid);
        Assert.Equal(15_000m, s.TotalFinePaid);   // Apr, May, Jun
        Assert.Equal(25_000m, s.TotalFineCharged); // Apr..Aug
        Assert.Equal(0m, s.Savings);
        Assert.Equal(0m, s.InvariantDifference);
    }

    [Fact]
    public void Tunganege_MatchesLeadersTruth()
    {
        var s = ContributionTruthEngine.Replay(Founder("Tunganege Mwalubilo"), TunganegePayments, Ukonga, AsOf);

        foreach (var m in new[] { 1, 2, 3, 4, 6, 7, 8 })
        {
            Assert.Equal(PeriodStatus.PaidOnTime, P(s, m).Status);
            Assert.Equal(10_000m, P(s, m).ExcelCell);
        }
        Assert.Equal(PeriodStatus.PaidLate, P(s, 5).Status);
        Assert.Equal(15_000m, P(s, 5).ExcelCell);                     // Excel: MAY = 15000

        // 60,000 ya Apr 4 = 10,000 mchango wa Mar + 50,000 Kianzio (SI Apr, SI faini ya Feb)
        var sixty = s.Allocations.Where(a => a.Reference == "DD4JP0DMNF").ToList();
        Assert.Equal(10_000m, sixty.Single(a => a.Target == "Contribution").Amount);
        Assert.Equal(3, sixty.Single(a => a.Target == "Contribution").Period!.Month);
        Assert.Equal(50_000m, sixty.Single(a => a.Target == "JoinFee").Amount);

        // 25,000 ya Jun 24 = 15,000 deni la May + 10,000 Jun
        var twentyFive = s.Allocations.Where(a => a.Reference == "DFOJP1HA89").ToList();
        Assert.Equal(15_000m, twentyFive.Where(a => a.Period!.Month == 5).Sum(a => a.Amount));
        Assert.Equal(10_000m, twentyFive.Where(a => a.Period!.Month == 6).Sum(a => a.Amount));

        Assert.Equal(0m, s.Arrears);
        Assert.Equal(0m, s.JoinFeeOutstanding);
        Assert.Equal(5_000m, s.TotalFinePaid);
        Assert.Equal(80_000m, s.TotalContributionPaid);
        Assert.Equal(PeriodStatus.Open, P(s, 9).Status);
    }

    [Fact]
    public void Frank_AsOfEndOfPdf_ShowsOnlyWhatMkobaProves()
    {
        // PDF inaishia 2026-08-01: malipo ya Aug 4 bado hayapo -> ripoti isibuni.
        var s = ContributionTruthEngine.Replay(Founder("Frank Mkoma"), FrankPayments, Ukonga, new DateTime(2026, 8, 1, 23, 59, 59));
        Assert.Equal(PeriodStatus.PaidLate, P(s, 4).Status);
        Assert.Equal(PeriodStatus.Unpaid, P(s, 5).Status);
        Assert.Equal(PeriodStatus.Unpaid, P(s, 6).Status);
        Assert.Equal(PeriodStatus.Open, P(s, 7).Status);  // dirisha la Jul liko wazi hadi Aug 5
        Assert.Equal(30_000m, s.Arrears);
    }

    [Fact]
    public void JoinFee_NeverSwallowsContributionDebt()
    {
        // Bug ya zamani: 15,000 ilienda yote Kianzio wakati mwezi wa 2 una deni.
        var pays = new[]
        {
            new TruthPayment("A", new(2026, 1, 20), 10_000m),
            new TruthPayment("B", new(2026, 3, 20), 15_000m), // Feb haikulipwa (deadline Mar 5)
        };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 3, 25));
        Assert.Equal(PeriodStatus.PaidLate, P(s, 2).Status);  // 10k + 5k faini
        Assert.Equal(0m, s.JoinFeePaid);
        Assert.Equal(PeriodStatus.Open, P(s, 3).Status);
    }

    [Fact]
    public void PaymentInGraceDays_BelongsToPreviousMonth()
    {
        var s = ContributionTruthEngine.Replay(Founder("X"),
            new[] { new TruthPayment("A", new(2026, 2, 5, 23, 59, 59), 10_000m) }, Ukonga, new DateTime(2026, 2, 10));
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 1).Status);
        Assert.Equal(0m, P(s, 1).FineDue);
        Assert.Equal(PeriodStatus.Open, P(s, 2).Status);
    }

    [Fact]
    public void ManualAfterCutoff_IsRejected_NotSilentlyCounted()
    {
        var pays = new[]
        {
            new TruthPayment("M1", new(2026, 5, 1), 20_000m, PaymentSource.ManualHistorical, PaymentPurpose.JoinFee),
            new TruthPayment("M2", new(2026, 2, 1), 20_000m, PaymentSource.ManualHistorical, PaymentPurpose.JoinFee, IsApproved: false),
        };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, AsOf);
        Assert.Equal(2, s.Rejected.Count);
        Assert.Equal(0m, s.JoinFeePaid);
    }

    [Fact]
    public void DuplicateMkobaReference_CountedOnce()
    {
        var p = new TruthPayment("DUP", new(2026, 1, 20), 10_000m);
        var s = ContributionTruthEngine.Replay(Founder("X"), new[] { p, p }, Ukonga, new DateTime(2026, 1, 25));
        Assert.Equal(10_000m, s.TotalAccepted);
        Assert.Single(s.Rejected);
    }

    [Fact]
    public void NewMember_PaysJoinFeeFirst()
    {
        var m = new TruthMember { MemberId = "N", Name = "Mpya", FirstPeriod = new YearMonth(2026, 9), JoinFeeRequiredUpfront = true };
        var s = ContributionTruthEngine.Replay(m, new[] { new TruthPayment("A", new(2026, 9, 10), 60_000m) }, Ukonga, AsOf);
        Assert.Equal(50_000m, s.JoinFeePaid);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 9).Status);
    }

    [Fact]
    public void Excess_GoesToSavings_AndIsDeductedAtDeadlineIfMemberDoesNotPay()
    {
        var pays = new[] { new TruthPayment("A", new(2026, 1, 20), 80_000m) }; // Jan 10k + Kianzio 50k + Akiba 20k
        var mid = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 2, 20));
        Assert.Equal(20_000m, mid.Savings);                         // bado ni akiba mpaka deadline
        Assert.Equal(PeriodStatus.Open, P(mid, 2).Status);

        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 4, 10));
        Assert.Equal(50_000m, s.JoinFeePaid);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 2).Status);       // akiba ilikatwa 05 Mar -> hakuna faini
        Assert.Equal(10_000m, P(s, 2).CoveredBySavings);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 3).Status);
        Assert.Equal(0m, s.Savings);
        Assert.Equal(0m, s.TotalFineCharged);
    }

    [Fact]
    public void GroupThatDisablesAutoDeduct_KeepsSavings_AndMissedMonthBecomesDebt()
    {
        var noAuto = new TruthPolicy
        {
            MonthlyContribution = Ukonga.MonthlyContribution, LateFine = Ukonga.LateFine, JoiningFee = Ukonga.JoiningFee,
            DueDay = Ukonga.DueDay, GracePeriodDays = Ukonga.GracePeriodDays, FineOnPartialPayment = Ukonga.FineOnPartialPayment,
            GroupFirstPeriod = Ukonga.GroupFirstPeriod, AutoDeductSavings = false
        };
        var pays = new[] { new TruthPayment("A", new(2026, 1, 20), 80_000m) };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, noAuto, new DateTime(2026, 3, 10));
        Assert.Equal(20_000m, s.Savings);                              // akiba haiguswi
        Assert.Equal(0m, P(s, 2).CoveredBySavings);
        Assert.Equal(PeriodStatus.Unpaid, P(s, 2).Status);
        Assert.Equal(Ukonga.LateFine, P(s, 2).FineDue);
    }

    [Fact]
    public void Savings_NotTouched_WhenMemberPaysOnTime()
    {
        var pays = new[]
        {
            new TruthPayment("A", new(2026, 1, 20), 70_000m),  // Jan + Kianzio + 10k akiba
            new TruthPayment("B", new(2026, 3, 1), 10_000m),   // Feb kwa wakati
        };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 3, 10));
        Assert.Equal(10_000m, s.Savings);
        Assert.Equal(0m, P(s, 2).CoveredBySavings);
    }

    [Fact]
    public void NoGrace_PaymentOn6th_IsLate_AndFined()
    {
        var s = ContributionTruthEngine.Replay(Founder("X"),
            new[] { new TruthPayment("A", new(2026, 2, 6, 0, 0, 0), 15_000m) }, Ukonga, new DateTime(2026, 2, 10));
        Assert.Equal(PeriodStatus.PaidLate, P(s, 1).Status);
        Assert.Equal(5_000m, P(s, 1).FinePaid);
    }

    [Fact]
    public void GroupGracePeriod_ExtendsDeadline()
    {
        var withGrace = new TruthPolicy
        {
            MonthlyContribution = Ukonga.MonthlyContribution, LateFine = Ukonga.LateFine, JoiningFee = Ukonga.JoiningFee,
            DueDay = Ukonga.DueDay, FineOnPartialPayment = Ukonga.FineOnPartialPayment, AutoDeductSavings = Ukonga.AutoDeductSavings, GroupFirstPeriod = Ukonga.GroupFirstPeriod,
            GracePeriodDays = 3
        };
        var s = ContributionTruthEngine.Replay(Founder("X"),
            new[] { new TruthPayment("A", new(2026, 2, 8, 20, 0, 0), 10_000m) }, withGrace, new DateTime(2026, 2, 20));
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 1).Status);        // tar 5 + siku 3 = tar 8
        Assert.Equal(new DateTime(2026, 2, 8, 23, 59, 59), P(s, 1).Deadline);
    }

    [Fact]
    public void PartialPaymentOnTime_NoFine_RemainderIsArrears()
    {
        var s = ContributionTruthEngine.Replay(Founder("X"),
            new[] { new TruthPayment("A", new(2026, 2, 3), 5_000m) }, Ukonga, new DateTime(2026, 2, 20));
        Assert.Equal(0m, P(s, 1).FineDue);
        Assert.Equal(PeriodStatus.PartiallyPaid, P(s, 1).Status);
        Assert.Equal(5_000m, s.Arrears);
    }

    [Fact]
    public void ApprovedFineWaiver_RemovesFine_UnapprovedDoesNot()
    {
        var pays = new[] { new TruthPayment("A", new(2026, 2, 6), 10_000m) }; // siku 1 baada ya deadline
        var approved = new[] { new TruthAdjustment(AdjustmentKind.FineWaiver, new(2026, 1), 0, true, "Mtandao wa M-Koba ulikuwa chini", "W1") };
        var pending = new[] { new TruthAdjustment(AdjustmentKind.FineWaiver, new(2026, 1), 0, false, "?", "W2") };

        var a = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 2, 20), adjustments: approved);
        Assert.Equal(0m, P(a, 1).FineDue);
        Assert.True(P(a, 1).FineWaived);
        Assert.Equal(PeriodStatus.PaidLate, P(a, 1).Status);           // bado ni kuchelewa, ila bila faini
        Assert.Contains(a.Allocations, x => x.Target == "FineWaived");

        var b = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 2, 20), adjustments: pending);
        Assert.Equal(5_000m, P(b, 1).FineDue);
    }

    [Fact]
    public void ApprovedGraceExtension_ForOneMonth()
    {
        var pays = new[] { new TruthPayment("A", new(2026, 2, 9), 10_000m) };
        var ext = new[] { new TruthAdjustment(AdjustmentKind.GraceExtension, new(2026, 1), 5, true, "Sikukuu", "G1") };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 2, 20), adjustments: ext);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 1).Status);
        Assert.Equal(new DateTime(2026, 2, 10, 23, 59, 59), P(s, 1).Deadline);
    }

    [Fact]
    public void Loan_RepaymentIsTakenFirst_ThenNormalWaterfall()
    {
        // Mkopo 100,000 (jumla ya kurejesha) Mar–Jun: 25,000 kila mwezi
        var loan = TruthLoan.EqualInstallments("L1", new DateTime(2026, 3, 10), new DateTime(2026, 6, 30), 100_000m);
        var pays = new[]
        {
            new TruthPayment("J", new(2026, 1, 20), 60_000m),  // Jan + Kianzio
            new TruthPayment("F", new(2026, 3, 1), 10_000m),   // Feb
            new TruthPayment("M", new(2026, 4, 3), 35_000m),   // Mar: rejesho 25k KWANZA + mchango 10k
            new TruthPayment("A", new(2026, 5, 2), 30_000m),   // Apr: rejesho 25k + 5k tu mchango
        };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 5, 10), loans: new[] { loan });

        Assert.Equal(25_000m, P(s, 3).LoanPaid);
        Assert.Equal(PeriodStatus.PaidOnTime, P(s, 3).Status);
        Assert.Equal(25_000m, P(s, 4).LoanPaid);
        Assert.Equal(5_000m, P(s, 4).ContributionPaid);              // rejesho lilitangulia
        Assert.Equal(PeriodStatus.PartiallyPaid, P(s, 4).Status);     // alilipa kwa wakati -> hakuna faini
        Assert.Equal(50_000m, s.TotalLoanRepaid);
        Assert.Equal(50_000m, s.Loans.Single().Outstanding);
        Assert.Equal(0m, s.LoanOverdue);
        Assert.Equal(0m, s.InvariantDifference);
    }

    [Fact]
    public void Loan_MissedInstallment_IsOverdue_AndPaidBeforeContributionsNextTime()
    {
        var loan = TruthLoan.EqualInstallments("L1", new DateTime(2026, 1, 10), new DateTime(2026, 2, 28), 40_000m); // 20k Jan, 20k Feb
        var pays = new[]
        {
            new TruthPayment("J", new(2026, 1, 20), 60_000m),  // Jan: rejesho 20k + mchango 10k + Kianzio 30k
            new TruthPayment("F", new(2026, 3, 10), 15_000m),  // amechelewa: rejesho la Feb kwanza
        };
        var s = ContributionTruthEngine.Replay(Founder("X"), pays, Ukonga, new DateTime(2026, 3, 20), loans: new[] { loan });
        Assert.Equal(20_000m, P(s, 1).LoanPaid);
        Assert.Equal(30_000m, s.JoinFeePaid);
        Assert.Equal(15_000m, P(s, 2).LoanPaid);                      // 15k yote kwa rejesho la Feb
        Assert.Equal(0m, P(s, 2).ContributionPaid);
        Assert.Equal(5_000m, s.LoanOverdue);
        Assert.Equal(15_000m, s.Arrears);                             // Feb 10k + faini 5k
    }

    [Fact]
    public void StatementIntegrity_FindsTheTwoMissingRowsInUkongaPdf()
    {
        var rows = new[]
        {
            new StatementIntegrity.Row("DB44MPJ95MC", new(2026, 2, 4, 20, 20, 55), 10_000m, false, 251_000m, 1),
            new StatementIntegrity.Row("DB45MPN1AAJ", new(2026, 2, 4, 22, 27, 41), 10_000m, false, 271_000m, 2), // Japhet
            new StatementIntegrity.Row("DB42MPNOA9O", new(2026, 2, 4, 23, 8, 1), 20_000m, false, 291_000m, 3),
            new StatementIntegrity.Row("DB59MPY4JJB", new(2026, 2, 5, 14, 7, 24), 10_000m, false, 361_000m, 4),
            new StatementIntegrity.Row("DB75MR687FZ", new(2026, 2, 7, 2, 15, 11), 10_000m, false, 381_000m, 5),  // Tito
            new StatementIntegrity.Row("DB95MSUA1X1", new(2026, 2, 9, 12, 25, 17), 390_000m, true, 1_000m, 6),
        };
        var gaps = StatementIntegrity.FindGaps(rows);
        // 291k -> 361k pia ni pengo kwenye mfano huu mfupi (tumeruka mistari), kwa hiyo tunaangalia ya Japhet/Tito tu
        Assert.Contains(gaps, g => g.BeforeReference == "DB45MPN1AAJ" && g.Amount == 10_000m);
        Assert.Contains(gaps, g => g.BeforeReference == "DB75MR687FZ" && g.Amount == 10_000m);
    }
    [Fact]
    public void Welfare_DeductsJoinFeeDebtFirst()
    {
        var s = ContributionTruthEngine.Replay(Founder("Frank Mkoma"), FrankPayments, Ukonga, AsOf);
        var r = WelfareDeductionRule.Compute(465_000m, s);
        Assert.Equal(40_000m, r.JoinFeeDeducted);
        Assert.Equal(425_000m, r.NetPayout);
    }

    [Fact]
    public void Replay_IsDeterministic_RegardlessOfInputOrder()
    {
        var a = ContributionTruthEngine.Replay(Founder("F"), FrankPayments, Ukonga, AsOf);
        var b = ContributionTruthEngine.Replay(Founder("F"), FrankPayments.Reverse(), Ukonga, AsOf);
        Assert.Equal(a.Periods.Select(p => (p.Status, p.ExcelCell)), b.Periods.Select(p => (p.Status, p.ExcelCell)));
        Assert.Equal(a.JoinFeePaid, b.JoinFeePaid);
    }
}
