using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Events;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO21 — KODI YA MALIPO NA SANDUKU LA MALIPO.
///
/// Kila jaribio linathibitisha kanuni moja:
///   1. Kodi inaundwa kwa mtindo mmoja na inasomwa hata kwenye maelezo machafu.
///   2. Wajibu mpya unapata kodi yake; kodi ni ya kipekee ndani ya kikundi.
///   3. Wajibu wa zamani unajazwa kodi (backfill) — mara moja tu.
///   4. Mstari wa statement wenye kodi unapendekezwa kwa mwanachama sahihi.
///   5. Uthibitisho hubadilisha MMILIKI wa mstari — pesa inaonekana kwa mwanachama
///      hata kama namba si yake, na HAIONEKANI kwa mwenye namba.
///   6. Kiasi kikubwa kinahitaji kiongozi wa PILI; aliyeanza hawezi kujithibitisha.
///   7. Dai la mwanachama halibadilishi deni mpaka mstari uthibitishwe.
///   8. Salio la wanachama wote linapatikana kwa hesabu moja (hakuna N+1).
/// </summary>
public class Sasisho21PaymentTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required EventWalletService Wallet;
        public required PaymentInboxService Inbox;
        public Guid GroupId;
        public Guid CaseId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId, string Phone)> P = new();
        public Guid M(string n) => P[n].MemberId;
        public Guid U(string n) => P[n].UserId;
    }

    private static async Task<W> Build(decimal floor = 20_000m, decimal fine = 5_000m,
        decimal secondApproval = 0m, bool claims = true, decimal joinFee = 0m)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var truth = new TruthLedgerService(db);
        var notify = new NotificationService(db);
        var wallet = new EventWalletService(db, truth);

        var w = new W
        {
            Db = db, Wallet = wallet, Inbox = new PaymentInboxService(db, wallet, notify),
            GroupId = Guid.NewGuid(), CaseId = Guid.NewGuid()
        };

        db.Groups.Add(new Group { Id = w.GroupId, Name = "J3 Together Forever", ShortCode = "J3", Type = GroupType.EventBased });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 0m, LateFine = 0m, JoiningFee = joinFee,
            DueDateDay = 5, GracePeriodDays = 0, ContributionStartDate = Jan1,
            MemberMinimumBalance = floor, EventTopUpWindowDays = 5, OverdueEventFineAmount = fine,
            EventFineGraceDays = 3, ReinstatementFineMultiplier = 2m, ReinstatementRequiresJoiningFee = true,
            PaymentSecondApprovalAmount = secondApproval, ClaimsEnabled = claims
        });

        var i = 0;
        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer),
                     ("Secretary", GroupRole.Secretary), ("M1", GroupRole.Member), ("M2", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            var phone = $"2557100000{i++:00}";
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = phone });
            db.GroupMembers.Add(new GroupMember
            {
                Id = mid, GroupId = w.GroupId, UserId = uid,
                MemberNumber = $"J3-2026-{i:000}", Role = role, Status = MemberStatus.Active, JoinedAt = Jan1
            });
            db.MemberPhones.Add(new MemberPhone
            {
                Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = mid, Phone = phone,
                AddedByUserId = uid, Status = TruthApprovalStatus.Approved
            });
            w.P[name] = (mid, uid, phone);
        }

        db.BenefitCases.Add(new BenefitCase
        {
            Id = w.CaseId, GroupId = w.GroupId, CaseNumber = "J3-MF-2026-007",
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent,
            BeneficiaryGroupMemberId = w.M("M2"), SubjectName = "Mama wa M2",
            EventDate = Jan1, Status = BenefitCaseStatus.Approved, ApprovedAt = Jan1,
            PerMemberAmount = 10_000m, Headcount = 2, GrossAmount = 20_000m, NetAmount = 20_000m
        });

        await db.SaveChangesAsync();
        return w;
    }

    private static async Task Deposit(W w, string phone, decimal amount, string reference, string description = "Deposit")
    {
        w.Db.MkobaRawTransactions.Add(new MkobaRawTransaction
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = reference, Timestamp = DateTime.UtcNow.AddDays(-1),
            Amount = amount, IsWithdrawal = false, Phone = phone, PayerName = "Mtu", Description = description,
            StatementFileHash = "h"
        });
        await w.Db.SaveChangesAsync();
    }

    // ==================================================================
    // 1. KODI — muundo na usomaji
    // ==================================================================

    [Fact]
    public void Kodi_Inaundwa_Kwa_Mtindo_Ule_Ule()
    {
        Assert.Equal("J3-E07-0147", PaymentCode.Build("J3", "J3-MF-2026-007", "J3-2026-0147"));
        Assert.Equal("J3-E07-0147", PaymentCode.Build("j3", "j3-mf-2026-7", "J3-2026-0147"));
        // Kikundi kisicho na code: "KL" (kikundi) — si tupu, si GUID.
        Assert.StartsWith("KL-E", PaymentCode.Build(null, "J3-MF-2026-007", "X-0147"));
    }

    [Theory]
    [InlineData("J3-E07-0147")]
    [InlineData("j3 e07 0147")]
    [InlineData("Malipo J3E070147 asante")]
    [InlineData("J3-E07/0147")]
    [InlineData("J3_E07_0147")]
    public void Find_Inasoma_Kodi_Kwenye_Maelezo_Machafu(string description)
        => Assert.Equal("J3-E07-0147", PaymentCode.Find(description));

    [Theory]
    [InlineData("Hakuna kodi hapa")]
    [InlineData("Ref E7 0147 hakuna")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_Haipati_Kodi_Isiyokuwepo(string? description)
        => Assert.Null(PaymentCode.Find(description));

    [Fact]
    public void Kodi_Haizidi_Urefu_Wa_Safu()
    {
        var code = PaymentCode.Build("KIKUNDI-REFU-SANA", "X-MF-2026-12345", "M-2026-9999");
        Assert.True(code.Length <= PaymentCode.MaxLength);
        Assert.True(PaymentCode.LooksValid(code));
    }

    // ==================================================================
    // 2. Wajibu mpya unapata kodi; kodi ni ya kipekee
    // ==================================================================

    [Fact]
    public async Task Wajibu_Mpya_Unapata_Kodi_Yake()
    {
        var w = await Build();
        var c = await w.Db.BenefitCases.FirstAsync(x => x.Id == w.CaseId);
        await w.Wallet.PostObligationsAsync(w.GroupId, c, w.U("Chair"));

        var obligations = await w.Db.EventObligations.Where(o => o.BenefitCaseId == w.CaseId).ToListAsync();
        Assert.NotEmpty(obligations);
        Assert.All(obligations, o => Assert.False(string.IsNullOrWhiteSpace(o.PaymentCode)));
        Assert.All(obligations, o => Assert.StartsWith("J3-E07-", o.PaymentCode));
        // Kodi moja kwa wajibu mmoja
        Assert.Equal(obligations.Count, obligations.Select(o => o.PaymentCode).Distinct().Count());
    }

    // ==================================================================
    // 3. Backfill — mara moja tu, haigusi zilizo na kodi
    // ==================================================================

    [Fact]
    public async Task Backfill_Inajaza_Zamani_Mara_Moja_Tu()
    {
        var w = await Build();
        w.Db.EventObligations.Add(new EventObligation
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, BenefitCaseId = w.CaseId, GroupMemberId = w.M("M1"),
            RequiredAmount = 10_000m, DeductedAmount = 0m, Status = EventObligationStatus.Pending,
            CreatedAt = Jan1, DueDate = Jan1.AddDays(5)
        });
        await w.Db.SaveChangesAsync();

        var first = await PaymentCodeBackfill.RunAsync(w.Db, w.GroupId);
        var second = await PaymentCodeBackfill.RunAsync(w.Db, w.GroupId);

        Assert.Equal(1, first);
        Assert.Equal(0, second);   // idempotent: hakuna kuandika mara ya pili
        var o = await w.Db.EventObligations.FirstAsync(x => x.GroupId == w.GroupId);
        var memberNumber = await w.Db.GroupMembers.Where(m => m.Id == w.M("M1")).Select(m => m.MemberNumber).FirstAsync();
        Assert.Equal(PaymentCode.Build("J3", "J3-MF-2026-007", memberNumber), o.PaymentCode);
        Assert.StartsWith("J3-E07-", o.PaymentCode);
    }

    // ==================================================================
    // 4. Sanduku: kodi → pendekezo sahihi
    // ==================================================================

    [Fact]
    public async Task Sanduku_Linapendekeza_Mwanachama_Kwa_Kodi()
    {
        var w = await Build();
        w.Db.EventObligations.Add(new EventObligation
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, BenefitCaseId = w.CaseId, GroupMemberId = w.M("M1"),
            RequiredAmount = 10_000m, DeductedAmount = 0m, Status = EventObligationStatus.Pending,
            CreatedAt = Jan1, DueDate = Jan1.AddDays(5), PaymentCode = "J3-E07-0004"
        });
        await w.Db.SaveChangesAsync();

        // Namba haijulikani (ya mtu mwingine), lakini maelezo yana kodi.
        await Deposit(w, "255799999999", 10_000m, "QX1", "NIMELIPA J3-E07-0004");

        var added = await w.Inbox.SyncAsync(w.GroupId);
        Assert.Equal(1, added);

        var r = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId);
        Assert.Equal(PaymentReceiptStatus.Suggested, r.Status);
        Assert.Equal("Code", r.MatchRule);
        Assert.Equal(w.M("M1"), r.SuggestedGroupMemberId);
        Assert.Equal("J3-E07-0004", r.PaymentCodeSeen);
    }

    [Fact]
    public async Task Sanduku_Haliingizi_Mstari_Mara_Mbili()
    {
        var w = await Build();
        await Deposit(w, "255799999999", 5_000m, "QX2");
        Assert.Equal(1, await w.Inbox.SyncAsync(w.GroupId));
        Assert.Equal(0, await w.Inbox.SyncAsync(w.GroupId));
    }

    [Fact]
    public async Task Sanduku_La_Mwanachama_Mwenyewe_Linahesabiwa_Tayari()
    {
        var w = await Build();
        await Deposit(w, w.P["M1"].Phone, 5_000m, "QX3", "Deposit");

        await w.Inbox.SyncAsync(w.GroupId);
        var r = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId);
        Assert.Equal(PaymentReceiptStatus.AutoMatched, r.Status);
        Assert.Equal(w.M("M1"), r.ConfirmedGroupMemberId);
    }

    // ==================================================================
    // 5. Uthibitisho: mmiliki anabadilika; hakuna kuhesabu mara mbili
    // ==================================================================

    [Fact]
    public async Task Uthibitisho_Unabadilisha_Mmiliki_Wa_Mstari()
    {
        var w = await Build();
        w.Db.EventObligations.Add(new EventObligation
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, BenefitCaseId = w.CaseId, GroupMemberId = w.M("M1"),
            RequiredAmount = 10_000m, DeductedAmount = 0m, Status = EventObligationStatus.Pending,
            CreatedAt = Jan1, DueDate = DateTime.UtcNow.AddDays(5), PaymentCode = "J3-E07-0004"
        });
        await w.Db.SaveChangesAsync();

        // Mke wa M1 analipa kwa namba yake (haijasajiliwa) akiweka kodi ya M1.
        // 30,000: 20,000 akiba + 10,000 ya kulipa deni la tukio.
        await Deposit(w, "255788888888", 30_000m, "QX4", "J3-E07-0004");

        var before = await w.Wallet.BalanceAsync(w.GroupId, w.M("M1"));
        await w.Inbox.SyncAsync(w.GroupId);
        var receipt = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId && x.Reference == "QX4");
        await w.Inbox.ConfirmAsync(w.GroupId, receipt.Id, w.M("M1"), "Mke wake amelipa", w.U("Treasurer"));

        // Amana 30,000: 10,000 ililipa deni la tukio (sweep), 20,000 ikabaki kama akiba.
        var after = await w.Wallet.BalanceAsync(w.GroupId, w.M("M1"));
        Assert.Equal(before + 20_000m, after);

        // Wajibu umelipwa kwa sweep (waterfall ya injini, si hesabu mpya)
        var o = await w.Db.EventObligations.FirstAsync(x => x.PaymentCode == "J3-E07-0004");
        Assert.Equal(EventObligationStatus.Settled, o.Status);

        // Mstari umemilikiwa; hauna kuhesabiwa kwa namba tena.
        var tx = await w.Db.MkobaRawTransactions.FirstAsync(x => x.Reference == "QX4");
        Assert.Equal(w.M("M1"), tx.AttributedGroupMemberId);
    }

    [Fact]
    public async Task Mstari_Unaomilikiwa_Hauhesabiwi_Kwa_Mwenye_Namba()
    {
        var w = await Build();
        // Namba ni ya M2 lakini pesa inathibitishwa kuwa ya M1 (alikopesha namba).
        await Deposit(w, w.P["M2"].Phone, 7_000m, "QX5");
        await w.Inbox.SyncAsync(w.GroupId);   // itakuwa AutoMatched kwa M2 (namba ni yake)

        var balanceM2Before = await w.Wallet.BalanceAsync(w.GroupId, w.M("M2"));

        var receipt = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId && x.Reference == "QX5");
        // Kuthibitisha risiti ya AutoMatched kwa mtu MWINGINE inakataliwa (si kuhamisha kimya).
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            w.Inbox.ConfirmAsync(w.GroupId, receipt.Id, w.M("M1"), "si kweli", w.U("Treasurer")));
        Assert.Contains("tayari", ex.Message);

        Assert.Equal(balanceM2Before, await w.Wallet.BalanceAsync(w.GroupId, w.M("M2")));
    }

    // ==================================================================
    // 6. Mikono miwili kwa kiasi kikubwa
    // ==================================================================

    [Fact]
    public async Task Kiasi_Kikubwa_Kinahitaji_Kiongozi_Wa_Pili()
    {
        var w = await Build(secondApproval: 100_000m);
        await Deposit(w, "255799000001", 150_000m, "QX6", "malipo");
        await w.Inbox.SyncAsync(w.GroupId);
        var r = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId && x.Reference == "QX6");

        // Hatua 1: kiongozi mmoja — haitakamilika
        var step1 = await w.Inbox.ConfirmAsync(w.GroupId, r.Id, w.M("M1"), "ameleta risiti", w.U("Treasurer"));
        Assert.Contains("MkonoWapili", step1.ToString());
        var mid = await w.Db.PaymentReceipts.FirstAsync(x => x.Id == r.Id);
        Assert.Equal(PaymentReceiptStatus.Unmatched, mid.Status);

        // Mtu yule yule hawezi kuwa wa pili
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Inbox.ConfirmAsync(w.GroupId, r.Id, w.M("M1"), null, w.U("Treasurer"), markSecondApproval: true));

        // Kiongozi mwingine anakamilisha
        await w.Inbox.ConfirmAsync(w.GroupId, r.Id, w.M("M1"), null, w.U("Chair"), markSecondApproval: true);
        var done = await w.Db.PaymentReceipts.FirstAsync(x => x.Id == r.Id);
        Assert.Equal(PaymentReceiptStatus.Confirmed, done.Status);
        Assert.Equal(w.U("Treasurer"), done.ConfirmedByUserId);
        Assert.Equal(w.U("Chair"), done.SecondApproverUserId);
    }

    [Fact]
    public async Task Kiasi_Kidogo_Kinahitaji_Mkono_Mmoja_Tu()
    {
        var w = await Build(secondApproval: 100_000m);
        await Deposit(w, "255799000002", 20_000m, "QX7", "malipo");
        await w.Inbox.SyncAsync(w.GroupId);
        var r = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId && x.Reference == "QX7");

        await w.Inbox.ConfirmAsync(w.GroupId, r.Id, w.M("M1"), null, w.U("Treasurer"));
        Assert.Equal(PaymentReceiptStatus.Confirmed, (await w.Db.PaymentReceipts.FirstAsync(x => x.Id == r.Id)).Status);
    }

    [Fact]
    public async Task Mwanachama_Aliyesimamishwa_Hawezi_Kupokea_Pesa()
    {
        var w = await Build();
        var m = await w.Db.GroupMembers.FirstAsync(x => x.Id == w.M("M1"));
        m.Status = MemberStatus.Suspended;
        await Deposit(w, "255799000003", 9_000m, "QX8", "malipo");
        await w.Inbox.SyncAsync(w.GroupId);
        var r = await w.Db.PaymentReceipts.FirstAsync(x => x.GroupId == w.GroupId && x.Reference == "QX8");

        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Inbox.ConfirmAsync(w.GroupId, r.Id, w.M("M1"), null, w.U("Treasurer")));
    }

    // ==================================================================
    // 7. Madai ya mwanachama
    // ==================================================================

    [Fact]
    public async Task Dai_Halibadilishi_Deni()
    {
        var w = await Build();
        var before = await w.Wallet.BalanceAsync(w.GroupId, w.M("M1"));

        var claim = await w.Inbox.ClaimAsync(w.GroupId, 10_000m, "MPESA-99881", "J3-E07-0004", "nmelipa jana", w.U("M1"));

        Assert.Equal(PaymentClaimStatus.Pending, claim.Status);
        Assert.Equal(before, await w.Wallet.BalanceAsync(w.GroupId, w.M("M1")));
    }

    [Fact]
    public async Task Dai_Haliwezi_Kuthibitishwa_Bila_Mstari_Wa_Statement()
    {
        var w = await Build();
        var claim = await w.Inbox.ClaimAsync(w.GroupId, 10_000m, "MPESA-99882", null, null, w.U("M1"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Inbox.DecideClaimAsync(w.GroupId, claim.Id, true, "sawa", null, w.U("Treasurer")));
    }

    [Fact]
    public async Task Namba_Ya_Muamala_Haiwezi_Kudaiwa_Mara_Mbili()
    {
        var w = await Build();
        await w.Inbox.ClaimAsync(w.GroupId, 5_000m, "MPESA-777", null, null, w.U("M1"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            w.Inbox.ClaimAsync(w.GroupId, 5_000m, "MPESA-777", null, null, w.U("M2")));
    }

    [Fact]
    public async Task Mwanachama_Haoni_Madai_Ya_Mwenzake()
    {
        var w = await Build();
        await w.Inbox.ClaimAsync(w.GroupId, 5_000m, "MPESA-778", null, null, w.U("M1"));

        var mine = await w.Inbox.ClaimsAsync(w.GroupId, w.U("M2"));
        var leader = await w.Inbox.ClaimsAsync(w.GroupId, w.U("Treasurer"));
        Assert.Empty(mine);
        Assert.Single(leader);
    }

    // ==================================================================
    // 8. Utendaji: salio la wote kwa hesabu moja
    // ==================================================================

    [Fact]
    public async Task Balances_Znarudisha_Salio_La_Wanachama_Wote()
    {
        var w = await Build();
        await Deposit(w, w.P["M1"].Phone, 50_000m, "QX9");
        await Deposit(w, w.P["M2"].Phone, 30_000m, "QX10");

        var balances = await w.Wallet.BalancesAsync(w.GroupId);

        Assert.Equal(50_000m, balances[w.M("M1")]);
        Assert.Equal(30_000m, balances[w.M("M2")]);
        Assert.Equal(await w.Wallet.BalanceAsync(w.GroupId, w.M("M1")), balances[w.M("M1")]);
    }

    [Fact]
    public async Task Makato_Ya_Tukio_Yanapunguza_Salio_La_Wote()
    {
        var w = await Build(floor: 20_000m);
        await Deposit(w, w.P["M1"].Phone, 50_000m, "QX11", "Deposit");
        var c = await w.Db.BenefitCases.FirstAsync(x => x.Id == w.CaseId);
        await w.Wallet.PostObligationsAsync(w.GroupId, c, w.U("Chair"));

        var balances = await w.Wallet.BalancesAsync(w.GroupId);
        var single = await w.Wallet.BalanceAsync(w.GroupId, w.M("M1"));
        Assert.Equal(single, balances[w.M("M1")]);
        Assert.Equal(40_000m, balances[w.M("M1")]);   // 50,000 − 10,000 zilizokatwa
    }
}
