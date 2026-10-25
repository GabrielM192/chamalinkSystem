using ChamaLink.Domain.Exceptions;
using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Ledger;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// Mtiririko KAMILI kama viongozi watakavyofanya:
///   reset → import PDF (data ghafi) → kuweka namba za simu → Kianzio cha mkononi (maker-checker)
///   → taarifa ya ukweli ya Frank na Tunganege inalingana na Excel ya viongozi.
/// </summary>
public class TruthLedgerServiceTests
{
    private sealed class FakeParser : IMKobaParserService
    {
        public List<MKobaTransactionItemDto> Rows { get; set; } = new();
        public Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream stream) => Task.FromResult(Rows);
        public Task<MKobaParseResult> ParseStatementDetailedAsync(Stream stream)
            => Task.FromResult(new MKobaParseResult { Transactions = Rows });
    }

    private static MKobaTransactionItemDto Dep(string reference, DateTime at, string phone, string name, decimal amount, decimal balance, int order)
        => new() { ReferenceNumber = reference, TransactionDate = at, PhoneNumber = phone, MemberName = name, Amount = amount, Balance = balance, RowOrder = order };

    private sealed class World
    {
        public required ApplicationDbContext Db;
        public required TruthLedgerService Truth;
        public required TruthSetupService Setup;
        public required FakeParser Parser;
        public Guid GroupId, Frank, Tunganege, Treasurer = Guid.NewGuid(), Chair = Guid.NewGuid(), ChairMember = Guid.NewGuid();
    }

    /// <summary>Namba inaunganishwa na mtunza-hazina na KUIDHINISHWA na mwenyekiti (maker-checker) — ndipo inahesabiwa.</summary>
    private static async Task<TruthSetupService.PhoneResult> Link(World w, Guid member, string phone)
    {
        var r = await w.Setup.AddPhoneAsync(w.GroupId, member, phone, w.Treasurer, null);
        await w.Setup.DecidePhoneAsync(w.GroupId, r.Phone.Id, true, w.Chair, null);
        return r;
    }

    private static async Task<World> Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var parser = new FakeParser();
        var truth = new TruthLedgerService(db);
        var w = new World { Db = db, Truth = truth, Parser = parser, Setup = new TruthSetupService(db, parser, truth) };
        w.GroupId = Guid.NewGuid(); w.Frank = Guid.NewGuid(); w.Tunganege = Guid.NewGuid();

        db.Groups.Add(new Group { Id = w.GroupId, Name = "Ukonga mkoba wing" });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId,
            MonthlyContribution = 10_000m, LateFine = 5_000m, JoiningFee = 50_000m, DueDateDay = 5, GracePeriodDays = 0,
            ContributionStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FoundingMembersCutoff = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
            ManualEntryCutoff = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc),
            FineOnPartialPayment = false, AutoDeductSavings = true
        });
        foreach (var (id, name, no, joined) in new[]
                 {
                     (w.Frank, "Frank Mkoma", "14", new DateTime(2026, 9, 20)),      // kama DB halisi: JoinedAt = tarehe ya rekodi (Sept)
                     (w.Tunganege, "Tunganege", "22", new DateTime(2026, 9, 20)),
                 })
        {
            var uid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = "" });
            db.GroupMembers.Add(new GroupMember { Id = id, GroupId = w.GroupId, UserId = uid, MemberNumber = no,
                JoinedAt = DateTime.SpecifyKind(joined, DateTimeKind.Utc) });
        }
        // Takataka za majaribio ya zamani (ledger iliyogawanywa vibaya) — reset inazifuta
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), GroupId = w.GroupId, UserId = Guid.NewGuid(), ReferenceNo = "OLD-KIANZIO",
            Type = TransactionType.JoiningFee, Amount = 45_000m, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return w;
    }

    // Mistari ya PDF (Jan–Aug 1) + PDF ya Aug (Frank Aug 4) + PDF ya Sep (malipo ya Aug)
    private static List<MKobaTransactionItemDto> PdfRows(bool withMissingRow) 
    {
        var rows = new List<MKobaTransactionItemDto>
        {
            Dep("DAV7MMA7IG7", new(2026, 1, 31, 11, 43, 38), "255742674429", "TUNGANEGE", 10_000m, 10_000m, 1),
            Dep("DB44MPHC7XI", new(2026, 2, 4, 19, 33, 49), "255712805984", "FRANK MKOMA", 10_000m, 20_000m, 2),
            Dep("DC39N9ITZ9V", new(2026, 3, 3, 16, 59, 46), "255742674429", "TUNGANEGE", 10_000m, 30_000m, 3),
            Dep("DC40NA6VY5G", new(2026, 3, 4, 15, 21, 54), "255712805984", "FRANK MKOMA", 10_000m, 40_000m, 4),
            // Pengo: mstari wa 10,000 wa mtu mwingine haupo kwenye PDF (balance inaruka 40k -> 60k)
            Dep("DD3B70DB6G",  new(2026, 4, 3, 12, 14, 58), "255712805984", "FRANK MKOMA", 10_000m, 60_000m, 5),
            Dep("DD4JP0DMNF",  new(2026, 4, 4, 8, 35, 19), "255742674429", "TUNGANEGE", 60_000m, 120_000m, 6),
            Dep("DE2JP0S1EL",  new(2026, 5, 2, 6, 44, 41), "255742674429", "TUNGANEGE", 10_000m, 130_000m, 7),
            Dep("DFOJP1HA89",  new(2026, 6, 24, 17, 59, 27), "255742674429", "TUNGANEGE", 25_000m, 155_000m, 8),
            Dep("DG4B71O0UO",  new(2026, 7, 4, 22, 18, 11), "255712805984", "FRANK MKOMA", 15_000m, 170_000m, 9),
            Dep("DGOJP1VX3T",  new(2026, 7, 24, 22, 42, 20), "255742674429", "TUNGANEGE", 10_000m, 180_000m, 10),
            Dep("DH4AUGFRNK",  new(2026, 8, 4, 10, 0, 0), "255712805984", "FRANK MKOMA", 15_000m, 195_000m, 11),
            Dep("DI2SEPTUNG",  new(2026, 9, 2, 10, 0, 0), "255742674429", "TUNGANEGE", 10_000m, 205_000m, 12),
            Dep("DI3SEPFRNK",  new(2026, 9, 3, 10, 0, 0), "255712805984", "FRANK MKOMA", 15_000m, 220_000m, 13),
        };
        if (withMissingRow)
            rows.Insert(4, Dep("DD1MISSING", new(2026, 3, 20, 9, 0, 0), "255700000001", "MTU MWINGINE", 10_000m, 50_000m, 5));
        return rows;
    }

    /// <summary>PDF bandia: inapita ukaguzi wa "%PDF-" lakini kurasa hazisomwi na PdfPig (parser bandia ndiyo inayorudisha safu).</summary>
    internal sealed class FakePdfImports : StatementImportService
    {
        public FakePdfImports(ApplicationDbContext db, IMKobaParserService parser, ChamaLink.Infrastructure.Services.Benefits.IEvidenceStorage storage, TruthSetupService setup)
            : base(db, parser, storage, setup, new ChamaLink.Infrastructure.Services.Benefits.NotificationService(db),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                new ChamaLink.Infrastructure.Services.Events.EventWalletService(db, new TruthLedgerService(db))) { }
        protected override int CountPages(byte[] bytes) => 1;
    }

    private static int _pdfSeq;
    internal static byte[] FakePdf() => System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4 bandia {Interlocked.Increment(ref _pdfSeq)}");

    /// <summary>
    /// SASISHO17: import = mtunza-hazina anapakia (rasimu) → mwenyekiti anaidhinisha. Ikiwa safu zote zipo tayari, kupakia kunakataliwa
    /// (409) — ripoti inarudishwa kama "0 mpya" ili majaribio ya zamani ya kurudia PDF yaendelee kuthibitisha kutorudufu.
    /// </summary>
    private static async Task<TruthSetupService.ImportReport> Import(World w)
    {
        // Viongozi wawili wenye akaunti wanahitajika kwa maker-checker ya PDF. Wanaongezwa kwa muda wa import tu ili
        // hesabu za kikundi (idadi ya wanachama, ripoti za mwezi) za majaribio haya ya Frank/Tunganege zisibadilike.
        var leaders = new List<GroupMember>();
        foreach (var (uid, role) in new[] { (w.Treasurer, GroupRole.Treasurer), (w.Chair, GroupRole.Chairperson) })
        {
            if (!await w.Db.Users.AnyAsync(u => u.Id == uid))
                w.Db.Users.Add(new User { Id = uid, FullName = role.ToString(), PhoneNumber = "", Email = $"{uid:N}@mfano.tz", PasswordHash = "x" });
            var gm = new GroupMember { Id = uid == w.Chair ? w.ChairMember : Guid.NewGuid(), GroupId = w.GroupId, UserId = uid, Role = role };
            w.Db.GroupMembers.Add(gm); leaders.Add(gm);
        }
        await w.Db.SaveChangesAsync();
        try { return await ImportCore(w); }
        finally { w.Db.GroupMembers.RemoveRange(leaders); await w.Db.SaveChangesAsync(); }
    }

    private static async Task<TruthSetupService.ImportReport> ImportCore(World w)
    {
        var svc = new FakePdfImports(w.Db, w.Parser, new BenefitsAndGovernanceTests.MemoryStorage(), w.Setup);
        object staged;
        try { staged = await svc.StageAsync(w.GroupId, new MemoryStream(FakePdf()), "statement.pdf", w.Treasurer); }
        catch (ConflictException ex) when (ex.Message.StartsWith("Hakuna muamala mpya"))
        {
            var n = w.Parser.Rows.Select(r => r.ReferenceNumber).Distinct().Count();
            return new TruthSetupService.ImportReport("", n, 0, n, new(), new(), 0, 0, null, null, null, new(), new());
        }
        var id = (Guid)staged.GetType().GetProperty("Id")!.GetValue(staged)!;
        // aliyepakia hawezi kuidhinisha
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ApproveAsync(w.GroupId, id, null, w.Treasurer));
        return (await svc.ApproveAsync(w.GroupId, id, "nimehakiki", w.Chair)).Report;
    }

    [Fact]
    public async Task FullFlow_Reset_Import_Phones_ManualJoinFee_MatchesLeadersExcel()
    {
        var w = await Build();

        // 1. Reset (dry-run kwanza, kisha kweli)
        var dry = await w.Setup.ResetGroupFinancialDataAsync(w.GroupId, dryRun: true, keepLoans: false, actorUserId: w.Chair);
        Assert.Equal(1, dry.Rows["LedgerEntries"]);
        Assert.Equal(1, await w.Db.LedgerEntries.CountAsync());                 // dry-run haifuti
        await w.Setup.ResetGroupFinancialDataAsync(w.GroupId, dryRun: false, keepLoans: false, actorUserId: w.Chair);
        Assert.Equal(0, await w.Db.LedgerEntries.CountAsync());
        Assert.Equal(2, await w.Db.GroupMembers.CountAsync());                  // wanachama hawaguswi

        // 2. Import PDF — data ghafi + pengo linagunduliwa
        w.Parser.Rows = PdfRows(withMissingRow: false);
        var imp = await Import(w);
        Assert.Equal(13, imp.Inserted);
        Assert.Single(imp.Gaps);                                                 // 10k iliyokosekana kabla ya DD3B70DB6G
        Assert.Equal(2, imp.UnknownPhones.Count);                                // namba bado hazijawekwa

        // Kuingiza PDF ileile tena (au PDF zinazopishana) hakurudufu chochote
        var again = await Import(w);
        Assert.Equal(0, again.Inserted);
        Assert.Equal(13, again.AlreadyExisted);

        // 3. Bila namba ya simu: hakuna malipo yanayoonekana + onyo
        var before = await w.Truth.GetMemberTruthAsync(w.Frank, new DateTime(2026, 9, 25, 12, 0, 0));
        Assert.Equal(0m, before.Statement.TotalAccepted);
        Assert.NotEmpty(before.Warnings);
        // SASISHO16: ilani yenye code thabiti (frontend inatafsiri kwa kiolezo) — si maandishi ya ndani
        var phoneNotice = Assert.Single(before.Notices, n => n.Code == "NO_APPROVED_PHONE");
        Assert.Equal(TruthLedgerService.TruthNotice.KindDataQuality, phoneNotice.Kind);
        Assert.False(string.IsNullOrWhiteSpace(phoneNotice.Tpl));

        // 4. Viongozi wanaweka namba (mfumo wowote wa kuandika unakubalika)
        var p1 = await w.Setup.AddPhoneAsync(w.GroupId, w.Frank, "0712 805 984", w.Treasurer, null);
        Assert.True(p1.MatchedInMkoba);
        Assert.Equal("FRANK MKOMA", p1.MkobaNameSeen);
        Assert.Equal(TruthApprovalStatus.Pending, p1.Phone.Status);
        var notYet = await w.Truth.GetMemberTruthAsync(w.Frank, new DateTime(2026, 9, 25, 12, 0, 0));
        Assert.Equal(0m, notYet.Statement.TotalAccepted);                                  // bado haijaidhinishwa -> haihesabiwi
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Setup.DecidePhoneAsync(w.GroupId, p1.Phone.Id, true, w.Treasurer, null));    // aliyeomba hawezi kuidhinisha
        await w.Setup.DecidePhoneAsync(w.GroupId, p1.Phone.Id, true, w.Chair, null);
        await Link(w, w.Tunganege, "+255742674429");
        await Assert.ThrowsAsync<ConflictException>(() =>
            w.Setup.AddPhoneAsync(w.GroupId, w.Tunganege, "255712805984", w.Treasurer, null)); // namba moja = mtu mmoja

        // 5. Kianzio cha mkononi cha Frank: mtunza-hazina anaomba, mwenyekiti anaidhinisha
        var fe = await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15), true,
            "Kianzio mkononi kwa mtunza-hazina", EvidenceType.CashBook, "Daftari la fedha uk. 1", w.Treasurer);
        var pending = await w.Truth.GetMemberTruthAsync(w.Frank, new DateTime(2026, 9, 25, 12, 0, 0));
        Assert.Equal(0m, pending.Statement.JoinFeePaid);                          // haijaidhinishwa -> haihesabiwi
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Setup.DecideManualPaymentAsync(w.GroupId, fe.Id, true, w.Treasurer, Guid.NewGuid(), null)); // maker ≠ checker
        await w.Setup.DecideManualPaymentAsync(w.GroupId, fe.Id, true, w.Chair, w.ChairMember, "Nimethibitisha daftari");

        // Kurudufu Kianzio kunazuiwa (40k tu imebaki)
        await Assert.ThrowsAsync<ConflictException>(() =>
            w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 45_000m, new DateTime(2026, 3, 15), true, "x", EvidenceType.CashBook, "Daftari la fedha uk. 1", w.Treasurer));
        // Baada ya cutoff: manual inakataliwa
        await Assert.ThrowsAsync<ConflictException>(() =>
            w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 9, 10), false, "x", EvidenceType.CashBook, "Daftari la fedha uk. 1", w.Treasurer));

        // 6. UKWELI
        var asOf = new DateTime(2026, 9, 25, 12, 0, 0);
        var f = (await w.Truth.GetMemberTruthAsync(w.Frank, asOf)).Statement;
        Assert.Equal(1, f.Periods[0].Period.Month);                               // anaanza JAN (mwanzilishi)
        Assert.Equal(new[] { 7, 8 }, f.MonthsInArrears.Select(p => p.Month));      // anadaiwa Jul + Aug
        Assert.Equal(30_000m, f.Arrears);
        Assert.Equal(10_000m, f.JoinFeePaid);
        Assert.Equal(40_000m, f.JoinFeeOutstanding);
        Assert.Equal(0m, f.InvariantDifference);

        var t = (await w.Truth.GetMemberTruthAsync(w.Tunganege, asOf)).Statement;
        Assert.Equal(0m, t.Arrears);
        Assert.Equal(50_000m, t.JoinFeePaid);
        Assert.Equal(0m, t.JoinFeeOutstanding);
        Assert.Equal(15_000m, t.Periods[4].ExcelCell);                            // May = 15k (deni + faini)
        Assert.Equal(0m, t.Savings);
        Assert.Equal(0m, t.InvariantDifference);

        // 7. Ripoti ya pesa isiyo na mwenyewe = pengo la 10k tu
        var un = await w.Truth.GetUnattributedAsync(w.GroupId);
        Assert.Empty(un.DepositsWithUnknownPhone);
        Assert.Equal(10_000m, un.Total);

        // 8. PDF kamili (yenye mstari uliokosekana) inaingizwa baadaye -> pengo linaondoka lenyewe
        w.Parser.Rows = PdfRows(withMissingRow: true);
        var imp2 = await Import(w);
        Assert.Equal(1, imp2.Inserted);
        Assert.Empty(imp2.Gaps);
        Assert.Equal(0, await w.Db.MkobaStatementGaps.CountAsync());
    }

    [Fact]
    public async Task Gap_AssignedToMember_CountsOnlyAfterSecondLeaderApproves()
    {
        var w = await Build();
        w.Parser.Rows = PdfRows(withMissingRow: false);
        await Import(w);
        await Link(w, w.Frank, "255712805984");
        var gap = await w.Db.MkobaStatementGaps.SingleAsync();

        await w.Setup.RequestGapResolutionAsync(w.GroupId, gap.Id, w.Frank, "SMS ya M-Koba ya Frank, ref DD1XXXX", w.Treasurer);
        var asOf = new DateTime(2026, 9, 25, 12, 0, 0);
        Assert.Equal(75_000m, (await w.Truth.GetMemberTruthAsync(w.Frank, asOf)).Statement.TotalAccepted); // bado
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.DecideGapResolutionAsync(w.GroupId, gap.Id, true, w.Treasurer));
        await w.Setup.DecideGapResolutionAsync(w.GroupId, gap.Id, true, w.Chair);
        Assert.Equal(85_000m, (await w.Truth.GetMemberTruthAsync(w.Frank, asOf)).Statement.TotalAccepted);
    }

    [Fact]
    public async Task FineWaiver_RecordedByLeaders_RemovesFineOnlyWhenApproved()
    {
        var w = await Build();
        w.Parser.Rows = PdfRows(withMissingRow: false);
        await Import(w);
        await Link(w, w.Tunganege, "255742674429");
        var asOf = new DateTime(2026, 9, 25, 12, 0, 0);

        var a = await w.Setup.RequestAdjustmentAsync(w.GroupId, w.Tunganege, TruthAdjustmentKind.FineWaiver, 2026, 5, 0, "Msiba wa familia", w.Treasurer);
        Assert.Equal(5_000m, (await w.Truth.GetMemberTruthAsync(w.Tunganege, asOf)).Statement.Periods[4].FineDue);
        await w.Setup.DecideAdjustmentAsync(w.GroupId, a.Id, true, w.Chair);
        var s = (await w.Truth.GetMemberTruthAsync(w.Tunganege, asOf)).Statement;
        Assert.Equal(0m, s.Periods[4].FineDue);
        Assert.True(s.Periods[4].FineWaived);
        Assert.Equal(5_000m, s.Savings);                                           // 5k iliyokuwa faini -> akiba
        Assert.Equal(0m, s.InvariantDifference);
    }

    [Fact]
    public async Task OnlyFirstPdf_JoinedAtIsRecordDate_StillFounders_ExactTruth()
    {
        // Hali halisi ya Mkoma: PDF moja tu (Jan–Aug 1), JoinedAt = Sept (tarehe ya rekodi)
        var w = await Build();
        w.Parser.Rows = PdfRows(withMissingRow: false).Where(r => r.TransactionDate < new DateTime(2026, 8, 2)).ToList();
        await Import(w);
        await Link(w, w.Frank, "255712805984");
        await Link(w, w.Tunganege, "255742674429");
        var asOf = new DateTime(2026, 9, 25, 22, 0, 0);

        var f = (await w.Truth.GetMemberTruthAsync(w.Frank, asOf)).Statement;
        Assert.Equal(1, f.Periods[0].Period.Month);                                  // mwanzilishi: anaanza JAN
        Assert.Equal(0m, f.JoinFeePaid);                                             // M-Koba HAIENDI Kianzio
        Assert.Equal(30_000m, f.TotalContributionPaid - 10_000m);                    // Jan,Feb,Mar + Apr
        Assert.Equal(PeriodStatus.PaidLate, f.Periods[3].Status);                   // Apr = 15k ya Jul 4
        Assert.Equal(new[] { 5, 6, 7, 8 }, f.MonthsInArrears.Select(p => p.Month));  // bila PDF ya Aug/Sep
        Assert.Equal(60_000m, f.Arrears);
        Assert.Equal(50_000m, f.JoinFeeOutstanding);                                 // mpaka manual 10k iidhinishwe

        var t = (await w.Truth.GetMemberTruthAsync(w.Tunganege, asOf)).Statement;
        Assert.Equal(1, t.Periods[0].Period.Month);
        Assert.Equal(50_000m, t.JoinFeePaid);                                        // kutoka 60k ya Apr 4
        Assert.Equal(new[] { 8 }, t.MonthsInArrears.Select(p => p.Month));           // Aug: malipo yake yako PDF ya Sep
        Assert.Equal(15_000m, t.Arrears);
        Assert.Equal(0m, t.Savings);
    }

    [Fact]
    public async Task Preview_ShowsEffectWithoutSaving_AndSameRulesAsSubmit()
    {
        var w = await Build();
        w.Parser.Rows = PdfRows(withMissingRow: false);
        await Import(w);
        await Link(w, w.Frank, "255712805984");

        var p = await w.Setup.PreviewManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15), true);
        Assert.True(p.Valid);
        Assert.Equal(50_000m, p.Before.JoinFeeOutstanding);
        Assert.Equal(40_000m, p.After.JoinFeeOutstanding);
        Assert.Equal("JoinFee", Assert.Single(p.NewAllocations).Target);
        Assert.Equal(0, await w.Db.FinancialEvents.CountAsync());                   // hakuna kilichohifadhiwa

        // Malipo ya kawaida (si Kianzio): yanalipa deni la zamani kwanza — preview inaonyesha mwezi uliobadilika
        var p2 = await w.Setup.PreviewManualPaymentAsync(w.GroupId, w.Frank, 15_000m, new DateTime(2026, 8, 10), false);
        Assert.True(p2.Valid);
        Assert.Contains(p2.ChangedMonths, c => c.Month == "Jul 2026" && c.OutstandingAfter == 0m);

        var bad = await w.Setup.PreviewManualPaymentAsync(w.GroupId, w.Frank, 60_000m, new DateTime(2026, 9, 10), true);
        Assert.False(bad.Valid);
        Assert.Equal(2, bad.Problems.Count);                                          // baada ya cutoff + inazidi Kianzio
        await Assert.ThrowsAsync<ConflictException>(() =>
            w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 60_000m, new DateTime(2026, 9, 10), true, "x", EvidenceType.CashBook, "Daftari la fedha uk. 1", w.Treasurer));
    }

    [Fact]
    public async Task Cashbook_ShowsMonthlyFlows_OpeningBalance_AndMissingRows()
    {
        var w = await Build();
        var t0 = new DateTime(2026, 1, 24, 11, 0, 0);
        w.Db.MkobaRawTransactions.AddRange(
            new MkobaRawTransaction { Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = "A1", Timestamp = t0, Phone = "255700000001", Amount = 10_000m, Balance = 11_000m, RowOrder = 1 },
            new MkobaRawTransaction { Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = "A2", Timestamp = t0.AddDays(10), Phone = "255700000002", Amount = 20_000m, Balance = 31_000m, RowOrder = 2 },
            // mstari wa 10,000 umekosekana hapa (balance inaruka 31k -> 41k -> 21k)
            new MkobaRawTransaction { Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = "W1", Timestamp = t0.AddDays(20), Phone = "255700000003", PayerName = "MPOKEAJI", Amount = 20_000m, Balance = 21_000m, IsWithdrawal = true, RowOrder = 3 });
        await w.Db.SaveChangesAsync();

        var c = await w.Truth.GetCashbookAsync(w.GroupId);
        Assert.Equal(1_000m, c.OpeningBalance);
        Assert.Equal(30_000m, c.Deposits);
        Assert.Equal(20_000m, c.Withdrawals);
        Assert.Equal(21_000m, c.LastBalance);
        Assert.Equal(10_000m, c.MissingFromStatement);
        Assert.Equal(new[] { "Jan 2026", "Feb 2026" }, c.Months.Select(m => m.Month));
        Assert.Equal(21_000m, c.Months[1].ClosingBalance);
        Assert.Equal("MPOKEAJI", Assert.Single(c.WithdrawalList).PayerName);
    }

    private static async Task<World> ImportedWithPhones()
    {
        var w = await Build();
        w.Parser.Rows = PdfRows(withMissingRow: true);
        await Import(w);
        await Link(w, w.Frank, "255712805984");
        await Link(w, w.Tunganege, "255742674429");
        return w;
    }

    private static async Task Approve(World w, FinancialEvent fe)
        => await w.Setup.DecideManualPaymentAsync(w.GroupId, fe.Id, true, w.Chair, w.ChairMember, "Nimethibitisha");

    [Fact]
    public async Task ManualAutoPayment_BeforeCutoff_PaysLoanInstallmentFirst_ThenWaterfall()
    {
        // TATHMINI #2: malipo ya mkononi (si JoinFee) lazima yapite waterfall ILE ILE ya M-Koba:
        // fungu la mkopo kwanza. Marejesho hayaandikwi tena kupitia LoanController (LedgerEntries).
        var w = await ImportedWithPhones();
        w.Db.Loans.Add(new Loan
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.Frank, UserId = Guid.NewGuid(),
            PrincipalAmount = 20_000m, InterestAmount = 0m, Status = LoanStatus.Active,
            DisbursedAt = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc)             // Mar 10k + Apr 10k
        });
        await w.Db.SaveChangesAsync();

        var at = new DateTime(2026, 3, 20, 10, 0, 0);
        var fe = await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, at, false, "Marejesho taslimu, risiti 7", EvidenceType.CashBook, "Daftari la fedha uk. 1", w.Treasurer);

        var pending = (await w.Truth.GetMemberTruthAsync(w.Frank, new DateTime(2026, 9, 25))).Statement;
        Assert.DoesNotContain(pending.Allocations, a => a.Timestamp == at);            // bila idhini haihesabiwi

        await Approve(w, fe);
        var s = (await w.Truth.GetMemberTruthAsync(w.Frank, new DateTime(2026, 9, 25))).Statement;
        var mine = s.Allocations.Where(a => a.Timestamp == at).ToList();
        Assert.Equal(10_000m, mine.Where(a => a.Target == "LoanRepayment").Sum(a => a.Amount)); // yote -> fungu la Mar
        Assert.Equal(3, mine.Single().Period!.Month);
        Assert.Equal(0m, s.InvariantDifference);
    }

    [Fact]
    public async Task GroupTruth_BulkLoad_IsIdentical_To_PerMemberTruth()
    {
        // TATHMINI #7: GetGroupTruthAsync inapakia data mara moja; matokeo lazima yawe sawa kabisa.
        var w = await ImportedWithPhones();
        await Approve(w, await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15), true, "Kianzio", EvidenceType.CashBook, "Daftari la fedha uk. 1", w.Treasurer));
        w.Db.Loans.Add(new Loan
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.Tunganege, UserId = Guid.NewGuid(),
            PrincipalAmount = 30_000m, InterestAmount = 3_000m, Status = LoanStatus.Active,
            DisbursedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), DueDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc)
        });
        w.Db.TruthAdjustments.Add(new TruthAdjustmentRecord
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = null, Kind = TruthAdjustmentKind.GraceExtension,
            Year = 2026, Month = 4, ExtraDays = 3, Reason = "Sikukuu", Status = TruthApprovalStatus.Approved
        });
        await w.Db.SaveChangesAsync();

        var asOf = new DateTime(2026, 9, 25, 12, 0, 0);
        var group = await w.Truth.GetGroupTruthAsync(w.GroupId, asOf);
        Assert.Equal(2, group.Count);
        foreach (var g in group)
        {
            var one = await w.Truth.GetMemberTruthAsync(g.MemberId, asOf);
            Assert.Equal(Fingerprint(one), Fingerprint(g));
        }

        static string Fingerprint(TruthLedgerService.MemberTruth m)
        {
            var s = m.Statement;
            return string.Join("|", new[]
            {
                string.Join(",", m.Phones), string.Join(",", m.Warnings),
                $"{s.TotalAccepted}/{s.Arrears}/{s.JoinFeePaid}/{s.JoinFeeOutstanding}/{s.Savings}/{s.LoanOutstanding}/{s.InvariantDifference}",
                string.Join(";", s.Periods.Select(p => $"{p.Period}:{p.ContributionPaid}:{p.FinePaid}:{p.FineDue}:{p.LoanPaid}:{p.Status}")),
                string.Join(";", s.Allocations.Select(a => $"{a.Reference}:{a.Target}:{a.Period}:{a.Amount}")),
            });
        }
    }

    [Theory]
    [InlineData(null, "Risiti Na. 12")]
    [InlineData(EvidenceType.Receipt, "")]
    [InlineData(EvidenceType.Receipt, "   ")]
    [InlineData((EvidenceType)42, "Risiti Na. 12")]
    public async Task ManualPayment_WithoutTraceableEvidence_IsNotSaved(EvidenceType? type, string reference)
    {
        var w = await ImportedWithPhones();
        await Assert.ThrowsAsync<ArgumentException>(() => w.Setup.RequestManualPaymentAsync(
            w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15), true, "Kianzio taslimu", type, reference, w.Treasurer));
        Assert.Equal(0, await w.Db.FinancialEvents.CountAsync(e => e.Source == EventSource.Manual));
    }

    [Fact]
    public async Task ManualPayment_StoresEvidence_AndApproverIsRecorded()
    {
        var w = await ImportedWithPhones();
        var fe = await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15, 10, 0, 0), true,
            "  Kianzio taslimu  ", EvidenceType.Receipt, "  Risiti Na. 12  ", w.Treasurer);
        await Approve(w, fe);
        var saved = await w.Db.FinancialEvents.SingleAsync(e => e.Id == fe.Id);
        Assert.Equal(EvidenceType.Receipt, saved.EvidenceType);
        Assert.Equal("Risiti Na. 12", saved.EvidenceReference);
        Assert.Equal("Kianzio taslimu", saved.Reason);
        Assert.Equal(new DateTime(2026, 3, 15, 10, 0, 0), saved.OccurredAt);   // tarehe ya malipo, si ya kuingiza
        Assert.True(saved.CreatedAt > saved.OccurredAt);
        Assert.Equal(w.ChairMember, saved.ApprovedByGroupMemberId);
        Assert.NotNull(saved.ApprovedAt);
    }

    [Fact]
    public async Task FirstSettings_MustSpecifyEveryMoneyRule_NoSilentDefaults()
    {
        var w = await Build();
        var fresh = Guid.NewGuid();
        w.Db.Groups.Add(new Group { Id = fresh, Name = "Kikundi kipya" });
        await w.Db.SaveChangesAsync();

        // SASISHO17: ValidationException yenye kiolezo (Template) kinachotafsirika kwa EN
        var ex = await Assert.ThrowsAsync<ChamaLink.Domain.Exceptions.ValidationException>(() => w.Setup.UpdateSettingsAsync(fresh,
            new TruthSetupService.SettingsDto { ContributionStartDate = new DateTime(2026, 1, 1) }, w.Chair));
        Assert.Equal("Mipangilio ya kwanza haijakamilika. Weka: {0}.", ex.Template);
        Assert.Contains("MonthlyContribution", ex.Message);
        Assert.Contains("LateFine", ex.Message);
        Assert.Contains("JoiningFee", ex.Message);
        Assert.Contains("DueDateDay", ex.Message);
        Assert.Contains("AutoDeductSavings", ex.Message);
        Assert.False(await w.Db.TruthGroupSettings.AnyAsync(x => x.GroupId == fresh));

        // Kikundi chenye kanuni tofauti kabisa kinahesabiwa kwa kanuni ZAKE
        await w.Setup.UpdateSettingsAsync(fresh, new TruthSetupService.SettingsDto
        {
            ContributionStartDate = new DateTime(2026, 1, 1), MonthlyContribution = 20_000m, LateFine = 2_000m,
            JoiningFee = 0m, DueDateDay = 10, GracePeriodDays = 2, FineOnPartialPayment = true, AutoDeductSavings = false
        }, w.Chair);
        var p = TruthLedgerService.ToTruthPolicy(await w.Truth.GetSettingsAsync(fresh));
        Assert.Equal(20_000m, p.MonthlyContribution);
        Assert.Equal(0m, p.JoiningFee);
        Assert.False(p.AutoDeductSavings);
        Assert.Equal(new DateTime(2026, 2, 12, 23, 59, 59), p.BaseDeadline(new YearMonth(2026, 1)));
    }

    [Fact]
    public async Task ManualPayment_LeavesCompleteAuditTrail_WhoWhatWhenEvidenceAndApprover()
    {
        var w = await ImportedWithPhones();
        var fe = await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15, 10, 0, 0), true,
            "Kianzio taslimu kwa Mwenyekiti", EvidenceType.CashBook, "Daftari la fedha uk. 4", w.Treasurer);
        await Approve(w, fe);

        var trail = await w.Db.AuditEvents.Where(a => a.EntityId == fe.Id).OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(2, trail.Count);

        var created = trail[0];
        Assert.Equal(AuditAction.Created, created.Action);
        Assert.Equal(w.Treasurer, created.ActorId);
        Assert.Equal(w.Frank, created.MemberId);
        Assert.Equal(EventSource.Manual, created.Source);
        Assert.Equal("Kianzio taslimu kwa Mwenyekiti", created.Reason);
        Assert.Contains("Daftari la fedha uk. 4", created.AfterJson);
        Assert.Contains("CashBook", created.AfterJson);

        var approved = trail[1];
        Assert.Equal(AuditAction.GovernanceApproved, approved.Action);
        Assert.Equal(w.Chair, approved.ActorId);
        Assert.NotEqual(created.ActorId, approved.ActorId);                 // maker ≠ checker, hata kwenye kumbukumbu
        Assert.Contains("Pending", approved.BeforeJson);
        Assert.Contains("Approved", approved.AfterJson);
        Assert.Equal("Nimethibitisha", approved.Reason);
    }

    [Fact]
    public async Task RejectedManualPayment_IsStillInAuditTrail()
    {
        var w = await ImportedWithPhones();
        var fe = await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15), true,
            "Kianzio taslimu", EvidenceType.LeaderStatement, "Tamko la Mwenyekiti", w.Treasurer);
        await w.Setup.DecideManualPaymentAsync(w.GroupId, fe.Id, false, w.Chair, w.ChairMember, "Hakuna risiti");
        var rejected = await w.Db.AuditEvents.SingleAsync(a => a.EntityId == fe.Id && a.Action == AuditAction.GovernanceRejected);
        Assert.Equal("Hakuna risiti", rejected.Reason);
        Assert.Equal(FinancialEventStatus.Rejected, (await w.Db.FinancialEvents.SingleAsync(e => e.Id == fe.Id)).Status);
    }

    [Fact]
    public async Task SettingsChange_IsAudited_WithBeforeAndAfter()
    {
        var w = await Build();
        await w.Setup.UpdateSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto { LateFine = 2_000m }, w.Chair);
        var a = await w.Db.AuditEvents.SingleAsync(x => x.EntityType == nameof(TruthGroupSettings));
        Assert.Equal(AuditAction.GroupPolicyVersioned, a.Action);
        Assert.Contains("\"LateFine\":5000", a.BeforeJson);
        Assert.Contains("\"LateFine\":2000", a.AfterJson);
        Assert.Equal(w.Chair, a.ActorId);
    }

    [Fact]
    public async Task AuditTrail_SurvivesTestDataReset()
    {
        var w = await ImportedWithPhones();
        var fe = await w.Setup.RequestManualPaymentAsync(w.GroupId, w.Frank, 10_000m, new DateTime(2026, 3, 15), true,
            "Kianzio taslimu", EvidenceType.Receipt, "Risiti Na. 7", w.Treasurer);
        await w.Setup.ResetGroupFinancialDataAsync(w.GroupId, dryRun: false, keepLoans: false, actorUserId: w.Chair);
        Assert.False(await w.Db.FinancialEvents.AnyAsync(e => e.Id == fe.Id));        // data ya majaribio imefutwa
        Assert.True(await w.Db.AuditEvents.AnyAsync(a => a.EntityId == fe.Id));        // kumbukumbu yake imebaki
        Assert.True(await w.Db.AuditEvents.AnyAsync(a => a.EntityType == "GroupFinancialData" && a.ActorId == w.Chair));
    }

    [Fact]
    public async Task RejectedGap_ClearsGap_ButAuditKeepsTheEvidence()
    {
        var w = await Build();
        w.Parser.Rows = PdfRows(withMissingRow: false);
        await Import(w);
        var gap = await w.Db.MkobaStatementGaps.SingleAsync();
        await w.Setup.RequestGapResolutionAsync(w.GroupId, gap.Id, w.Frank, "SMS ya M-Koba DB4XYZ", w.Treasurer);
        await w.Setup.DecideGapResolutionAsync(w.GroupId, gap.Id, false, w.Chair, "SMS si ya Frank");
        Assert.Null((await w.Db.MkobaStatementGaps.SingleAsync(g => g.Id == gap.Id)).Evidence);
        var rej = await w.Db.AuditEvents.SingleAsync(a => a.EntityId == gap.Id && a.Action == AuditAction.GovernanceRejected);
        Assert.Contains("SMS ya M-Koba DB4XYZ", rej.BeforeJson);
        Assert.Equal("SMS si ya Frank", rej.Reason);                 // sababu ya kukataa haipotei
    }

    [Fact]
    public async Task MonthlyReport_July_MatchesLeadersTruth_ForFrankAndTunganege_WithWhatsAppText()
    {
        // Julai 2026 (mwisho 5 Agosti): Tunganege alilipa 10,000 tarehe 24/07 → kwa wakati.
        // Frank: 15,000 ya 04/08 ililipa deni la ZAMANI (Mei), kwa hiyo Julai haijalipwa → 10,000 + faini 5,000.
        var w = await ImportedWithPhones();
        var r = await w.Truth.GetMonthlyReportAsync(w.GroupId, 2026, 7);

        Assert.Equal("Julai 2026", r.MonthLabel);
        Assert.Equal(2, r.TotalMembers);
        Assert.True(r.MonthClosed);
        Assert.True(r.DataComplete);

        var frank = r.Members.Single(m => m.MemberId == w.Frank);
        Assert.Equal(("Unpaid", 10_000m, 0m, 5_000m, 15_000m), (frank.Status, frank.Target, frank.Paid, frank.FineCharged, frank.Outstanding));
        // "Deni lote" ni la LEO (si la Julai): Julai + Agosti = 30,000 + kila mwezi uliopita tangu hapo (jaribio halitegemei tarehe)
        var frankNow = await w.Truth.GetMemberTruthAsync(w.Frank, null);
        Assert.True(frank.TotalArrears >= 30_000m);
        Assert.Equal(frankNow.Statement.Arrears, frank.TotalArrears);
        Assert.Equal(50_000m, frank.JoinFeeOutstanding);

        var tung = r.Members.Single(m => m.MemberId == w.Tunganege);
        Assert.Equal(("PaidOnTime", 10_000m, 0m, 0m), (tung.Status, tung.Paid, tung.FineCharged, tung.Outstanding));
        Assert.Equal(new DateTime(2026, 7, 24), tung.PaidAt!.Value.Date);

        Assert.Equal((1, 1, 20_000m, 10_000m, 50.0m), (r.PaidOnTime, r.NotPaid, r.TotalExpected, r.TotalCollected, r.CollectionRatePercent));
        Assert.Equal(15_000m, r.OutstandingForMonth);

        // Maandishi ya WhatsApp: namba ya uanachama, simu iliyofichwa katikati, hali, faini na deni
        Assert.Contains("TAARIFA YA MCHANGO — JULAI 2026", r.WhatsAppText);
        Assert.Contains("14 | Frank Mkoma | 0712***984", r.WhatsAppText);
        Assert.Contains("❌ Hajalipa | Faini: 5,000 | Deni la mwezi: 15,000", r.WhatsAppText);
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"deni lote la michango {frank.TotalArrears:N0}, kianzio 50,000"), r.WhatsAppText);
        Assert.Contains("22 | Tunganege | 0742***429", r.WhatsAppText);
        Assert.DoesNotContain("255712805984", r.WhatsAppText);  // namba kamili haisambazwi kwenye kundi
    }

    [Fact]
    public async Task MonthlyReport_BeforeTheGroupStarted_IsRejected_NotSilentlyEmpty()
    {
        var w = await ImportedWithPhones();
        await Assert.ThrowsAsync<ValidationException>(() => w.Truth.GetMonthlyReportAsync(w.GroupId, 2025, 12));
        await Assert.ThrowsAsync<ValidationException>(() => w.Truth.GetMonthlyReportAsync(w.GroupId, 2026, 13));
    }

    [Fact]
    public void PhoneNumbers_Normalize()
    {
        Assert.Equal("255652952220", PhoneNumbers.Normalize("+255 652 952 220"));
        Assert.Equal("255652952220", PhoneNumbers.Normalize("0652952220"));
        Assert.Equal("255652952220", PhoneNumbers.Normalize("652952220"));
        Assert.Throws<ArgumentException>(() => PhoneNumbers.Normalize("12345"));
    }

    // ---- Maelezo ya pengo: yanafanya kazi kwa KILA kikundi, yanaonyesha mipaka yake kwa majina + balance ----
    [Fact]
    public async Task GapMessage_NamesBothBoundaryRows_ShowsBalances_AndWarnsOnImport()
    {
        var w = await Build();
        await Link(w, w.Frank, "0712805984");
        w.Parser.Rows = PdfRows(withMissingRow: false);
        var imp = await Import(w);

        // Onyo la juu kabisa la import linaeleza kuwa balance imeruka
        Assert.StartsWith("Mnyororo wa balance umeruka mara 1", imp.Warnings[0]);

        var gaps = await w.Db.MkobaStatementGaps.ToListAsync();
        var d = Assert.Single(await StatementGapDescriber.DescribeAsync(w.Db, w.GroupId, gaps));
        Assert.Equal("DC40NA6VY5G", d.Previous!.Reference);          // mpaka wa kabla (muamala sahihi)
        Assert.Equal("DD3B70DB6G", d.Next!.Reference);               // mpaka wa baada (muamala sahihi)
        Assert.Equal("Frank Mkoma", d.Next.MemberName);              // jina la mwanachama, si la M-Koba tu
        Assert.Equal(40_000m, d.BalanceAfterPrevious);
        Assert.Equal(50_000m, d.ExpectedBalanceBeforeNext);
        Assert.Equal(10_000m, d.Amount);
        Assert.Contains("HAUPO kwenye PDF", d.Message);
        Assert.Contains("yenyewe ni sahihi", d.Message);
        Assert.Equal(new[] { w.Frank }, d.PossibleMemberIds);         // pendekezo tu — hakuna kinachohesabiwa bila idhini

        // Pendekezo halihesabu pesa: Frank hapati 10k ya pengo mpaka viongozi wawili waidhinishe
        var t = await w.Truth.GetMemberTruthAsync(w.Frank, new DateTime(2026, 9, 25, 12, 0, 0));
        Assert.DoesNotContain(t.Statement.Allocations, a => a.Reference.StartsWith("GAP-"));
    }

    [Fact]
    public async Task GapMessage_OutflowWithoutRow_IsReportedToo_AndGroupsDoNotLeak()
    {
        var w = await Build();
        w.Parser.Rows = new List<MKobaTransactionItemDto>
        {
            Dep("R1", new(2026, 2, 1, 9, 0, 0), "255700000001", "MTU A", 10_000m, 100_000m, 1),
            // 20,000 zilitoka bila mstari: 100k -> (80k) -> 90k
            Dep("R2", new(2026, 2, 2, 9, 0, 0), "255700000002", "MTU B", 10_000m, 90_000m, 2),
        };
        var imp = await Import(w);
        Assert.Contains(imp.Warnings, x => x.StartsWith("Mnyororo wa balance umeruka"));

        var d = Assert.Single(await StatementGapDescriber.DescribeAsync(w.Db, w.GroupId, await w.Db.MkobaStatementGaps.ToListAsync()));
        Assert.Equal(-20_000m, d.Amount);
        Assert.StartsWith("Balance imeshuka", d.Title);
        Assert.Equal("MTU A", d.Previous!.PayerName);                // namba haijaunganishwa → jina la M-Koba
        Assert.Empty(d.PossibleMemberIds);

        // Kikundi kingine hakioni mapengo/miamala ya kikundi hiki
        Assert.Empty(await StatementGapDescriber.DescribeAsync(w.Db, Guid.NewGuid(), new List<MkobaStatementGap>()));
        var other = await StatementGapDescriber.DescribeAsync(w.Db, Guid.NewGuid(), await w.Db.MkobaStatementGaps.ToListAsync());
        Assert.Null(other.Single().Previous);                        // miamala ya kikundi kingine haisomwi
    }
}
