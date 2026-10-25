using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Events;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO18 — vikundi vinavyochangishana KILA TUKIO.
///
/// Kila jaribio linathibitisha kanuni moja ya biashara:
///   1. Tukio lililoidhinishwa linatengeneza wajibu kwa kila mwanachama ALIYEHESABIWA.
///   2. Idempotent — tukio moja haliwezi kumkata mwanachama mara mbili.
///   3. Floor — salio halishuki chini ya MemberMinimumBalance; kinachobaki ni deni.
///   4. Sweep — amana mpya inalipa deni la ZAMANI kwanza.
///   5. Vikundi vya mwezi havipati wajibu wowote (kinga ya engine).
///   6. Mnufaika hachangii (isipokuwa sera inasema vinginevyo).
///   7. Kikomo cha makato kwa mwezi kinaheshimiwa.
/// </summary>
public class Sasisho18EventGroupsTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required EventWalletService Wallet;
        public Guid GroupId;
        public Guid CaseId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId, string Phone)> P = new();
        public Guid M(string n) => P[n].MemberId;
        public Guid U(string n) => P[n].UserId;
    }

    private static async Task<W> Build(
        GroupType type = GroupType.EventBased,
        decimal floor = 50_000m,
        int windowDays = 14,
        decimal capPerMonth = 0m,
        bool beneficiaryContributes = false,
        decimal overdueFine = 0m)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var truth = new TruthLedgerService(db);
        var w = new W { Db = db, Wallet = new EventWalletService(db, truth), GroupId = Guid.NewGuid(), CaseId = Guid.NewGuid() };

        db.Groups.Add(new Group { Id = w.GroupId, Name = "Kikundi cha matukio", ShortCode = "EVT", Type = type });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId,
            MonthlyContribution = 0m, LateFine = 0m, JoiningFee = 0m, DueDateDay = 5, GracePeriodDays = 0,
            ContributionStartDate = Jan1, FineOnPartialPayment = false, AutoDeductSavings = false,
            MemberMinimumBalance = floor, EventTopUpWindowDays = windowDays,
            MaxEventDeductionPerMemberPerMonth = capPerMonth, BeneficiaryContributes = beneficiaryContributes,
            OverdueEventFineAmount = overdueFine
        });

        var i = 0;
        foreach (var name in new[] { "A", "B", "C", "Ben" })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            var phone = $"2557000000{i++:00}";
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = phone });
            db.GroupMembers.Add(new GroupMember
            {
                Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = name, Role = GroupRole.Member,
                Status = MemberStatus.Active, JoinedAt = Jan1
            });
            db.MemberPhones.Add(new MemberPhone
            {
                Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = mid, Phone = phone,
                AddedByUserId = uid, Status = TruthApprovalStatus.Approved   // namba imethibitishwa na kiongozi wa pili
            });
            w.P[name] = (mid, uid, phone);
        }

        // Kesi ya tukio (msiba) — kiasi cha kila mwanachama 10,000
        db.BenefitCases.Add(new BenefitCase
        {
            Id = w.CaseId, GroupId = w.GroupId, CaseNumber = "EVT-001",
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent,
            BeneficiaryGroupMemberId = w.M("Ben"), SubjectName = "Mama wa Ben",
            EventDate = Jan1, Status = BenefitCaseStatus.Approved, ApprovedAt = Jan1,
            PerMemberAmount = 10_000m, Headcount = 4, GrossAmount = 40_000m, NetAmount = 40_000m,
            CountedMemberIdsJson = System.Text.Json.JsonSerializer.Serialize(
                new[] { w.M("A"), w.M("B"), w.M("C"), w.M("Ben") })
        });

        await db.SaveChangesAsync();
        return w;
    }

    /// <summary>Amana ya M-Koba inayoingia kwenye pochi ya mwanachama.</summary>
    private static async Task Deposit(W w, string member, decimal amount, int seq = 1)
    {
        var phone = w.P[member].Phone;
        w.Db.MkobaRawTransactions.Add(new MkobaRawTransaction
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = $"REF-{member}-{seq}",
            Timestamp = DateTime.SpecifyKind(Jan1.AddDays(seq), DateTimeKind.Utc),
            Amount = amount, IsWithdrawal = false, Phone = phone, PayerName = member,
            Description = "Deposit", StatementFileHash = "h"
        });
        await w.Db.SaveChangesAsync();
    }

    private static BenefitCase Case(W w) => w.Db.BenefitCases.First(c => c.Id == w.CaseId);

    // ==================================================================
    [Fact]
    public async Task Floor_Huheshimiwa_Na_Kilichobaki_Ni_Deni()
    {
        var w = await Build(floor: 50_000m);
        await Deposit(w, "A", 55_000m);   // 55,000 − 50,000 floor = 5,000 inapatikana
        await Deposit(w, "B", 80_000m);   // 30,000 inapatikana -> anakata 10,000 yote
        await Deposit(w, "C", 5_000m);    // chini ya floor -> hakuna kinachokatwa

        var created = await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));
        Assert.Equal(3, created); // Ben hachangii (sera)

        var a = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"));
        Assert.Equal(10_000m, a.RequiredAmount);
        Assert.Equal(5_000m, a.DeductedAmount);
        Assert.Equal(5_000m, a.OutstandingAmount);
        Assert.Equal(EventObligationStatus.Pending, a.Status);
        Assert.NotNull(a.DueDate); // window 14

        var b = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("B"));
        Assert.Equal(10_000m, b.DeductedAmount);
        Assert.Equal(0m, b.OutstandingAmount);
        Assert.Equal(EventObligationStatus.Settled, b.Status);

        var c = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("C"));
        Assert.Equal(0m, c.DeductedAmount);
        Assert.Equal(10_000m, c.OutstandingAmount);

        // Salio halijashuka chini ya floor
        Assert.Equal(50_000m, await w.Wallet.BalanceAsync(w.GroupId, w.M("A")));
        Assert.Equal(70_000m, await w.Wallet.BalanceAsync(w.GroupId, w.M("B")));
        Assert.Equal(5_000m, await w.Wallet.BalanceAsync(w.GroupId, w.M("C")));
    }

    [Fact]
    public async Task Tukio_Moja_Halimkati_Mwanachama_Mara_Mbili()
    {
        var w = await Build();
        await Deposit(w, "B", 80_000m);

        var first = await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));
        var second = await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        Assert.Equal(3, first);
        Assert.Equal(0, second); // idempotent
        Assert.Equal(3, await w.Db.EventObligations.CountAsync(o => o.BenefitCaseId == w.CaseId));
        Assert.Equal(10_000m, await w.Db.EventObligations.Where(o => o.GroupMemberId == w.M("B")).SumAsync(o => o.DeductedAmount));
    }

    [Fact]
    public async Task Sweep_Inalipa_Deni_La_Zamani_Kwanza()
    {
        var w = await Build(floor: 50_000m);
        // A hana pesa kabisa -> deni 10,000
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));
        Assert.Equal(10_000m, (await w.Wallet.GetWalletAsync(w.GroupId, w.M("A"))).Outstanding);

        // Ameingiza 60,000: 10,000 inalipa deni, 50,000 inabaki (floor)
        await Deposit(w, "A", 60_000m);
        var applied = await w.Wallet.SweepAsync(w.GroupId, w.M("A"));

        Assert.Equal(10_000m, applied);
        var wallet = await w.Wallet.GetWalletAsync(w.GroupId, w.M("A"));
        Assert.Equal(0m, wallet.Outstanding);
        Assert.Equal(50_000m, wallet.Balance);
        Assert.Empty(wallet.OpenObligations);
    }

    [Fact]
    public async Task Sweep_Hairuki_Floor_Na_Deni_Linabaki()
    {
        var w = await Build(floor: 50_000m);
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A")); // deni 10,000

        await Deposit(w, "A", 52_000m); // 2,000 tu inapatikana juu ya floor
        var applied = await w.Wallet.SweepAsync(w.GroupId, w.M("A"));

        Assert.Equal(2_000m, applied);
        Assert.Equal(8_000m, (await w.Wallet.GetWalletAsync(w.GroupId, w.M("A"))).Outstanding);
        Assert.Equal(50_000m, await w.Wallet.BalanceAsync(w.GroupId, w.M("A")));
    }

    [Fact]
    public async Task Kikundi_Cha_Mwezi_Hakipati_Wajibu_Wowote()
    {
        var w = await Build(type: GroupType.MonthlySavings);
        await Deposit(w, "B", 80_000m);

        var created = await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        Assert.Equal(0, created);
        Assert.Empty(w.Db.EventObligations);
    }

    [Fact]
    public async Task Mnufaika_Hachangii_Isipokuwa_Sera_Inasema()
    {
        var w1 = await Build(beneficiaryContributes: false);
        await w1.Wallet.PostObligationsAsync(w1.GroupId, Case(w1), w1.U("A"));
        Assert.False(await w1.Db.EventObligations.AnyAsync(o => o.GroupMemberId == w1.M("Ben")));

        var w2 = await Build(beneficiaryContributes: true);
        await w2.Wallet.PostObligationsAsync(w2.GroupId, Case(w2), w2.U("A"));
        Assert.True(await w2.Db.EventObligations.AnyAsync(o => o.GroupMemberId == w2.M("Ben")));
    }

    [Fact]
    public async Task Kikomo_Cha_Mwezi_Kinaheshimiwa()
    {
        var w = await Build(floor: 0m, capPerMonth: 6_000m);
        await Deposit(w, "B", 80_000m);

        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var b = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("B"));
        Assert.Equal(6_000m, b.DeductedAmount);   // kikomo 6,000 hata salio lina 80,000
        Assert.Equal(4_000m, b.OutstandingAmount);
    }

    [Fact]
    public async Task Floor_Ya_Sifuri_Inaruhusu_Makato_Hadi_Mwisho()
    {
        var w = await Build(floor: 0m);
        await Deposit(w, "A", 4_000m);

        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var a = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"));
        Assert.Equal(4_000m, a.DeductedAmount);
        Assert.Equal(6_000m, a.OutstandingAmount);
        Assert.Equal(0m, await w.Wallet.BalanceAsync(w.GroupId, w.M("A")));
    }

    [Fact]
    public async Task Wajibu_Unaandikwa_Kwenye_Mnyororo_Wa_Ukaguzi()
    {
        var w = await Build();
        await Deposit(w, "B", 80_000m);
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var audit = await w.Db.AuditEvents
            .Where(a => a.Action == AuditAction.WelfareObligationCreated && a.GroupId == w.GroupId)
            .ToListAsync();

        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.Equal(w.CaseId, a.CorrelationId));
    }

    // ==================================================================
    // SASISHO18.1 — faini ya deni lililochelewa + sweep ya amana mpya
    // ==================================================================

    [Fact]
    public async Task Faini_Inatozwa_Mara_Moja_Deni_Linapochelewa()
    {
        var w = await Build(overdueFine: 2_000m);
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var a = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"));
        a.DueDate = DateTime.UtcNow.AddDays(-1);
        await w.Db.SaveChangesAsync();

        var charged = await w.Wallet.ChargeOverdueFinesAsync();
        Assert.Equal(1, charged);                       // B na Ben hawana deni / hawakuwepo

        a = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"));
        Assert.Equal(12_000m, a.RequiredAmount);        // 10,000 deni + 2,000 faini
        Assert.Equal(12_000m, a.OutstandingAmount);
        Assert.True(a.OverdueFineIssued);

        // Kukimbia tena hakulipizi faini mara ya pili
        Assert.Equal(0, await w.Wallet.ChargeOverdueFinesAsync());
        Assert.Equal(12_000m, (await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"))).RequiredAmount);
    }

    [Fact]
    public async Task Faini_Haitozwi_Kama_Sera_Ni_Sifuri()
    {
        var w = await Build(overdueFine: 0m);
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var a = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"));
        a.DueDate = DateTime.UtcNow.AddDays(-3);
        await w.Db.SaveChangesAsync();

        Assert.Equal(0, await w.Wallet.ChargeOverdueFinesAsync());
        Assert.Equal(10_000m, (await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("A"))).RequiredAmount);
    }

    [Fact]
    public async Task Faini_Haitozwi_Kwa_Deni_Lililokwisha_Lipwa()
    {
        var w = await Build(overdueFine: 2_000m);
        await Deposit(w, "B", 80_000m);                                  // B analipa yote
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var b = await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("B"));
        Assert.Equal(EventObligationStatus.Settled, b.Status);
        b.DueDate = DateTime.UtcNow.AddDays(-1);                         // tarehe imepita, lakini deni 0
        await w.Db.SaveChangesAsync();

        Assert.Equal(0, await w.Wallet.ChargeOverdueFinesAsync());
        Assert.Equal(10_000m, (await w.Db.EventObligations.FirstAsync(o => o.GroupMemberId == w.M("B"))).RequiredAmount);
    }

    [Fact]
    public async Task Amama_Mpya_Inalipa_Deni_Mara_Moja_Inapoidhinishwa()
    {
        var w = await Build();
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));
        await Deposit(w, "A", 60_000m);

        var touched = await w.Wallet.SweepDepositsAsync(w.GroupId, new[] { w.P["A"].Phone });

        Assert.Equal(1, touched);
        Assert.Equal(0m, (await w.Wallet.GetWalletAsync(w.GroupId, w.M("A"))).Outstanding);
        Assert.Equal(50_000m, await w.Wallet.BalanceAsync(w.GroupId, w.M("A")));
    }

    [Fact]
    public async Task Sweep_Ya_Amana_Haifanyi_Kitu_Kwa_Kikundi_Cha_Mwezi()
    {
        var w = await Build(type: GroupType.MonthlySavings);
        await Deposit(w, "A", 60_000m);

        Assert.Equal(0, await w.Wallet.SweepDepositsAsync(w.GroupId, new[] { w.P["A"].Phone }));
    }

    [Fact]
    public async Task Historia_Inaonyesha_Makato_Yaliyokamilika()
    {
        var w = await Build();
        await Deposit(w, "B", 80_000m);
        await w.Wallet.PostObligationsAsync(w.GroupId, Case(w), w.U("A"));

        var wallet = await w.Wallet.GetWalletAsync(w.GroupId, w.M("B"));

        Assert.Empty(wallet.OpenObligations);                            // hakuna deni lililobaki
        var h = Assert.Single(wallet.History);                           // lakini historia ipo
        Assert.Equal(EventObligationStatus.Settled, h.Status);
        Assert.Equal(10_000m, h.DeductedAmount);
    }
}
