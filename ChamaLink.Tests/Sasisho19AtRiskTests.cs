using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Events;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO19 — KANUNI ZA J3 TOGETHER FOREVER (na kanuni za kikundi chochote cha "kila tukio").
///
/// Kila jaribio linathibitisha kanuni moja:
///   1. Mwanachama anaingia "hatarini kuondolewa" TU baada ya faini + siku za neema kupita.
///   2. Orodha hiyo inaonekana kwa mwanachama YEYOTE hai — si siri ya viongozi.
///   3. Kuondolewa kunahitaji viongozi ZAIDI ya wawili (>= 3) — sera haiwezi kupunguza.
///   4. Mwanachama asiye na deni hawezi kuondolewa kwa njia hii.
///   5. Gharama ya kurudi = deni lililobaki + (faini x kizidishi) + ada ya kujiunga.
///   6. Kurudi kunazuiwa mpaka gharama hiyo ilipwe (salio la pochi linapima).
///   7. Vikundi vya mwezi havina orodha ya hatarini.
///   8. Arifa zinatumwa kwa mwanachama na kwa viongozi.
/// </summary>
public class Sasisho19AtRiskTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal sealed class MemoryStorage : IEvidenceStorage
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public Task<string> SaveAsync(Guid groupId, string sha256, string extension, byte[] content, CancellationToken ct)
        {
            var key = $"{groupId:N}/{sha256}{extension}";
            Files[key] = content;
            return Task.FromResult(key);
        }
        public Task<byte[]> ReadAsync(string storageKey, CancellationToken ct) => Task.FromResult(Files[storageKey]);
    }

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required EventWalletService Wallet;
        public AtRiskService AtRisk = null!;
        public required MemberStatusService Status;
        public Guid GroupId;
        public Guid CaseId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId, string Phone)> P = new();
        public Guid M(string n) => P[n].MemberId;
        public Guid U(string n) => P[n].UserId;
    }

    private static async Task<W> Build(
        GroupType type = GroupType.EventBased,
        int graceDays = 3,
        int removalApprovals = 0,
        decimal multiplier = 2m,
        bool joinFeeRequired = true,
        decimal floor = 20_000m,
        decimal fine = 5_000m,
        decimal joinFee = 30_000m,
        int statusChangeApprovals = 2,
        bool governance = true)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var truth = new TruthLedgerService(db);
        var notify = new NotificationService(db);
        var wallet = new EventWalletService(db, truth);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Benefits:MaxEvidenceBytes"] = "1048576" }).Build();
        var benefits = new BenefitService(db, truth, notify, new MemoryStorage(), config, wallet);

        var w = new W
        {
            Db = db, Wallet = wallet, GroupId = Guid.NewGuid(), CaseId = Guid.NewGuid(),
            Status = new MemberStatusService(db, benefits, notify)
        };
        w.AtRisk = new AtRiskService(db, truth, wallet, w.Status, notify);

        db.Groups.Add(new Group { Id = w.GroupId, Name = "J3 Together Forever", ShortCode = "J3T", Type = type });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId,
            MonthlyContribution = 0m, LateFine = 0m, JoiningFee = joinFee, DueDateDay = 5, GracePeriodDays = 0,
            ContributionStartDate = Jan1, FineOnPartialPayment = false, AutoDeductSavings = false,
            MemberMinimumBalance = floor, EventTopUpWindowDays = 5, OverdueEventFineAmount = fine,
            // SASISHO19
            EventFineGraceDays = graceDays, RemovalApprovalsRequired = removalApprovals,
            ReinstatementFineMultiplier = multiplier, ReinstatementRequiresJoiningFee = joinFeeRequired
        });

        var i = 0;
        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer),
                     ("Secretary", GroupRole.Secretary), ("D_SEC", GroupRole.Secretary),
                     ("M1", GroupRole.Member), ("M2", GroupRole.Member), ("M3", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            var phone = $"2557100000{i++:00}";
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = phone });
            db.GroupMembers.Add(new GroupMember
            {
                Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = name, Role = role,
                Status = MemberStatus.Active, JoinedAt = Jan1
            });
            db.MemberPhones.Add(new MemberPhone
            {
                Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = mid, Phone = phone,
                AddedByUserId = uid, Status = TruthApprovalStatus.Approved
            });
            w.P[name] = (mid, uid, phone);
        }

        // Tukio (kesi) ambalo wajibu wake unaundwa kwa mkono kwenye majaribio.
        db.BenefitCases.Add(new BenefitCase
        {
            Id = w.CaseId, GroupId = w.GroupId, CaseNumber = "J3-001",
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent,
            BeneficiaryGroupMemberId = w.M("M3"), SubjectName = "Mama wa M3",
            EventDate = Jan1, Status = BenefitCaseStatus.Approved, ApprovedAt = Jan1,
            PerMemberAmount = 10_000m, Headcount = 3, GrossAmount = 30_000m, NetAmount = 30_000m,
            CountedMemberIdsJson = System.Text.Json.JsonSerializer.Serialize(
                new[] { w.M("M1"), w.M("M2") })
        });

        await db.SaveChangesAsync();

        // Sera ya mafao + idhini (viongozi wawili: mmoja apendekeze, mwingine aidhinishe).
        if (governance)
        {
            var g = await benefits.ProposeGovernanceAsync(w.GroupId, Gov(statusChangeApprovals), w.U("Chair"));
            await benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        }

        return w;
    }

    private static BenefitService.GovernanceDto Gov(int statusChangeApprovals) => new()
    {
        RequiredApprovals = 3, MinLeaderApprovals = 1, AllowMemberApprovers = true, RejectionsToReject = 2, VotingDays = 7,
        HeadcountRule = BenefitHeadcountRule.AllActiveMembers, ExcludeBeneficiaryFromHeadcount = true,
        FundingSource = BenefitFundingSource.GroupFund, DeductJoinFee = true, DeductArrears = false,
        RequireNoArrearsForEligibility = false, MinMembershipDays = 0, ClaimWindowDays = 60,
        PaymentConfirmationDays = 7, StatusChangeApprovals = statusChangeApprovals, ExpulsionVotePercent = 75,
        RequireJoinFeePaid = false, RequireCashAvailability = true, ApproverCannotInitiatePayment = true,
        ExcludeRelatedParties = true,
        PaymentInitiatorRoles = new() { GroupRole.Treasurer },
        PaymentConfirmerRoles = new() { GroupRole.Chairperson, GroupRole.Secretary }
    };

    /// <summary>Deni la tukio lililochelewa (wajibu wa mwanachama mmoja).</summary>
    private static async Task Owe(W w, string member, decimal required, decimal deducted = 0m, int daysAgo = 6)
    {
        w.Db.EventObligations.Add(new EventObligation
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, BenefitCaseId = w.CaseId, GroupMemberId = w.M(member),
            RequiredAmount = required, DeductedAmount = deducted, Status = EventObligationStatus.Pending,
            DueDate = DateTime.UtcNow.AddDays(-daysAgo), CreatedAt = DateTime.UtcNow.AddDays(-daysAgo - 5),
            OverdueFineIssued = true
        });
        await w.Db.SaveChangesAsync();
    }

    private static async Task Deposit(W w, string member, decimal amount, int seq = 1)
    {
        w.Db.MkobaRawTransactions.Add(new MkobaRawTransaction
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = $"REF-{member}-{seq}",
            Timestamp = DateTime.UtcNow.AddDays(-1), Amount = amount, IsWithdrawal = false,
            Phone = w.P[member].Phone, PayerName = member, Description = "Deposit", StatementFileHash = "h"
        });
        await w.Db.SaveChangesAsync();
    }

    // ==================================================================
    // 1. MTAJI WA ORODHA
    // ==================================================================
    [Fact]
    public async Task Hatarini_Inaanza_TU_Baada_Ya_Faini_Na_Siku_Za_Neema()
    {
        var w = await Build(graceDays: 3);
        await Owe(w, "M1", 10_000m, daysAgo: 5);   // 5 > 3 -> hatarini
        await Owe(w, "M2", 10_000m, daysAgo: 2);   // 2 < 3 -> bado neema

        var rows = await w.AtRisk.ListAsync(w.GroupId, w.U("M3"));

        var row = Assert.Single(rows);
        Assert.Equal(w.M("M1"), row.MemberId);
        Assert.Equal(10_000m, row.Outstanding);
        Assert.Equal(5_000m, row.FineCharged);        // faini ya sera, inaonekana kwa uwazi
        Assert.Equal(20_000m, row.FloorShortfall);    // hana salio; floor 20,000
        Assert.Equal(5, row.DaysOverdue);
        Assert.Equal(1, row.PendingEvents);
    }

    [Fact]
    public async Task Deni_Lililolipwa_Sehemu_Ya_Kutosha_Haingii_Orodhani()
    {
        var w = await Build(graceDays: 0);
        await Owe(w, "M1", 10_000m, deducted: 10_000m, daysAgo: 9); // deni 0
        await Owe(w, "M2", 10_000m, deducted: 4_000m, daysAgo: 9);  // deni 6,000

        var rows = await w.AtRisk.ListAsync(w.GroupId, w.U("Chair"));

        var row = Assert.Single(rows);
        Assert.Equal(w.M("M2"), row.MemberId);
        Assert.Equal(6_000m, row.Outstanding);
    }

    // ==================================================================
    // 2. ORODHA NI YA WOTE
    // ==================================================================
    [Fact]
    public async Task Orodha_Ya_Hatarini_Inaonekana_Kwa_Mwanachama_Wa_Kawaida()
    {
        var w = await Build();
        await Owe(w, "M1", 10_000m, daysAgo: 8);

        var rows = await w.AtRisk.ListAsync(w.GroupId, w.U("M3"));

        Assert.Single(rows);
        Assert.Equal("M1", rows[0].Name);            // jina linaonekana kwa mwanachama mwingine
        Assert.Equal("M1", rows[0].MemberNumber);

        // Asiye mwanachama wa kikundi haoni orodha
        await Assert.ThrowsAsync<ForbiddenException>(() => w.AtRisk.ListAsync(w.GroupId, Guid.NewGuid()));
    }

    // ==================================================================
    // 3. KUONDOLEWA — ZAIDI YA WAWILI
    // ==================================================================
    [Fact]
    public async Task Kuondolewa_Kunahitaji_Viongozi_Zaidi_Ya_Wawili()
    {
        // Sera ya mafao inasema 2 na sera ya kikundi 1 — kanuni (3) inashinda.
        var w = await Build(removalApprovals: 1, statusChangeApprovals: 2);
        await Owe(w, "M1", 10_000m, daysAgo: 10);

        var r = await w.AtRisk.ProposeRemovalAsync(w.GroupId, w.M("M1"),
            "Deni la tukio halijalipwa baada ya faini na muda wa neema.", w.U("Chair"));

        Assert.Equal(3, r.RequiredApprovals);
        Assert.False(r.IsExpulsion);                  // kusimamishwa kwa deni, si kufukuzwa
        Assert.Equal(MemberStatus.Suspended, r.ToStatus);

        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Treasurer"));
        Assert.Equal(MemberStatus.Active, (await w.Db.GroupMembers.FindAsync(w.M("M1")))!.Status); // 1

        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Secretary"));
        Assert.Equal(MemberStatus.Active, (await w.Db.GroupMembers.FindAsync(w.M("M1")))!.Status); // 2 — bado haitoshi

        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("D_SEC"));                       // 3
        Assert.Equal(MemberStatus.Suspended, (await w.Db.GroupMembers.FindAsync(w.M("M1")))!.Status);
    }

    [Fact]
    public async Task Kuondolewa_Hakuhitaji_Sera_Ya_Mafao()
    {
        // Kikundi cha J3 kinaweza kutumia pochi pekee (hakuna sera ya mafao bado).
        // Kuondolewa kwa deni la tukio ni jambo la uongozi — kanuni ya viongozi 3 inatosha.
        var w = await Build(governance: false);
        await Owe(w, "M1", 10_000m, daysAgo: 10);

        var r = await w.AtRisk.ProposeRemovalAsync(w.GroupId, w.M("M1"),
            "Deni la tukio halijalipwa baada ya faini na muda wa neema.", w.U("Chair"));

        Assert.Equal(3, r.RequiredApprovals);
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Treasurer"));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Secretary"));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("D_SEC"));
        Assert.Equal(MemberStatus.Suspended, (await w.Db.GroupMembers.FindAsync(w.M("M1")))!.Status);
    }

    [Fact]
    public async Task Mwanachama_Asiye_Denini_Hawezi_Kuondolewa()
    {
        var w = await Build();
        await Owe(w, "M1", 10_000m, daysAgo: 10);

        await Assert.ThrowsAsync<ValidationException>(() =>
            w.AtRisk.ProposeRemovalAsync(w.GroupId, w.M("M2"), "Hana deni lakini nataka kumtoa.", w.U("Chair")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            w.AtRisk.ProposeRemovalAsync(w.GroupId, w.M("M1"), "  ", w.U("Chair")));
    }

    // ==================================================================
    // 4. KURUDI — GHARAMA
    // ==================================================================
    [Fact]
    public async Task Gharama_Ya_Kurudi_Ni_Deni_Pamoja_Na_Faini_Mara_Mbili_Na_Ada_Ya_Kujiunga()
    {
        var w = await Build(fine: 5_000m, multiplier: 2m, joinFee: 30_000m, joinFeeRequired: true);
        await Owe(w, "M1", 10_000m, daysAgo: 10);                        // deni 10,000
        await Owe(w, "M1", 10_000m, deducted: 4_000m, daysAgo: 12);      // deni 6,000  -> jumla 16,000

        var c = await w.AtRisk.ReinstatementCostAsync(w.GroupId, w.M("M1"));

        Assert.Equal(16_000m, c.OutstandingDebt);
        Assert.Equal(2m, c.FineMultiplier);
        Assert.Equal(5_000m, c.FineBase);
        Assert.Equal(10_000m, c.ReinstatementFine);                      // 2 x 5,000
        Assert.True(c.JoiningFeeRequired);
        Assert.Equal(30_000m, c.JoiningFee);
        Assert.Equal(56_000m, c.Total);                                  // 16,000 + 10,000 + 30,000
    }

    [Fact]
    public async Task Sera_Inaweza_Kuondoa_Ada_Ya_Kurudi()
    {
        var w = await Build(multiplier: 0m, joinFeeRequired: false);
        await Owe(w, "M1", 10_000m, daysAgo: 10);

        var c = await w.AtRisk.ReinstatementCostAsync(w.GroupId, w.M("M1"));

        Assert.Equal(0m, c.ReinstatementFine);
        Assert.Equal(0m, c.JoiningFee);
        Assert.Equal(10_000m, c.Total);                                  // deni pekee
    }

    // ==================================================================
    // 5. KURUDI — KINGA
    // ==================================================================
    [Fact]
    public async Task Kurudi_Kunazuiwa_Mpaka_Gharama_Yote_Ilipwe()
    {
        var w = await Build(fine: 5_000m, multiplier: 2m, joinFee: 30_000m, joinFeeRequired: true);
        await Owe(w, "M1", 10_000m, daysAgo: 10);                        // 10,000 + 10,000 faini + 30,000 ada = 50,000

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            w.AtRisk.RequireReinstatementPaidAsync(w.GroupId, w.M("M1")));
        Assert.Contains("50,000", ex.Message);

        await Deposit(w, "M1", 30_000m);                                     // inalipa ada ya kujiunga (pochi 0)
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.AtRisk.RequireReinstatementPaidAsync(w.GroupId, w.M("M1")));   // bado haitoshi

        await Deposit(w, "M1", 20_000m, seq: 2);                             // pochi 20,000
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.AtRisk.RequireReinstatementPaidAsync(w.GroupId, w.M("M1")));

        await Deposit(w, "M1", 30_000m, seq: 3);                             // pochi 50,000 — imetimia
        await w.AtRisk.RequireReinstatementPaidAsync(w.GroupId, w.M("M1"));
    }

    [Fact]
    public async Task Aliye_Kwisha_Lipa_Ada_Ya_Kujiunga_Lazima_Awe_Na_Kiasi_Kamili_Pochini()
    {
        var w = await Build(fine: 5_000m, multiplier: 2m, joinFee: 30_000m, joinFeeRequired: true);
        await Owe(w, "M1", 10_000m, daysAgo: 10);

        await Deposit(w, "M1", 30_000m);            // ada ya kujiunga imelipwa; pochi 0
        await Deposit(w, "M1", 49_000m, seq: 2);    // pochi 49,000 — haitoshi kwa 50,000
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.AtRisk.RequireReinstatementPaidAsync(w.GroupId, w.M("M1")));

        await Deposit(w, "M1", 1_000m, seq: 3);     // pochi 50,000 — imetimia
        await w.AtRisk.RequireReinstatementPaidAsync(w.GroupId, w.M("M1"));
    }

    // ==================================================================
    // 6. VIKUNDI VYA MWEZI
    // ==================================================================
    [Fact]
    public async Task Kikundi_Cha_Mwezi_Hakina_Orodha_Ya_Hatarini()
    {
        var w = await Build(type: GroupType.MonthlySavings);
        await Owe(w, "M1", 10_000m, deducted: 0m, daysAgo: 60);

        Assert.Empty(await w.AtRisk.ListAsync(w.GroupId, w.U("Chair")));
    }

    // ==================================================================
    // 7. ARIFA
    // ==================================================================
    [Fact]
    public async Task Arifa_Zinatumwa_Kwa_Mwanachama_Na_Viongozi()
    {
        var w = await Build();
        await Owe(w, "M1", 10_000m, daysAgo: 10);

        Assert.Equal(1, await w.AtRisk.NotifyAtRiskAsync(w.GroupId));

        var notes = await w.Db.MemberNotifications.Where(n => n.GroupId == w.GroupId && n.Kind == "MemberOverdue").ToListAsync();
        Assert.Contains(notes, n => n.RecipientGroupMemberId == w.M("M1"));
        Assert.Contains(notes, n => n.RecipientGroupMemberId == w.M("Chair"));
        Assert.Contains(notes, n => n.RecipientGroupMemberId == w.M("Treasurer"));
        Assert.DoesNotContain(notes, n => n.RecipientGroupMemberId == w.M("M2"));   // hana deni
    }
}
