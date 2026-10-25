using ChamaLink.API.Security;
using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Ledger;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO17 — majaribio ya kurudi nyuma (regression) kwa kila kipengele cha ripoti ya ukaguzi wa usalama wa SASISHO16.
/// Kila jaribio linataja kipengele (P0-x / P1-x / NEW-x) ili kosa likirudi ijulikane mara moja ni lipi.
/// </summary>
public class Sasisho17SecurityTests
{
    private sealed class Parser : IMKobaParserService
    {
        public List<MKobaTransactionItemDto> Rows { get; set; } = new();
        public Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream s) => Task.FromResult(Rows);
        public Task<MKobaParseResult> ParseStatementDetailedAsync(Stream s) => Task.FromResult(new MKobaParseResult { Transactions = Rows });
    }

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required TruthSetupService Setup;
        public required Parser Parser;
        public required BenefitsAndGovernanceTests.MemoryStorage Storage;
        public Guid GroupId;
        public readonly Dictionary<string, (Guid Member, Guid User)> P = new();
        public Guid U(string n) => P[n].User;
        public Guid M(string n) => P[n].Member;
    }

    private static MKobaTransactionItemDto Dep(string r, DateTime at, string phone, decimal amount, decimal balance, int order)
        => new() { ReferenceNumber = r, TransactionDate = at, PhoneNumber = phone, MemberName = "MTU " + order, Amount = amount, Balance = balance, RowOrder = order };

    private static async Task<W> Build(int? cashMaxAgeDays = 120)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var parser = new Parser();
        var truth = new TruthLedgerService(db);
        var w = new W { Db = db, Parser = parser, Setup = new TruthSetupService(db, parser, truth), Storage = new BenefitsAndGovernanceTests.MemoryStorage(), GroupId = Guid.NewGuid() };
        var jan1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Groups.Add(new Group { Id = w.GroupId, Name = "Kikundi S17", ShortCode = "S17" });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 10_000m, LateFine = 5_000m, JoiningFee = 0m, DueDateDay = 5, GracePeriodDays = 0,
            ContributionStartDate = jan1, FoundingMembersCutoff = jan1.AddMonths(3), ManualEntryCutoff = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc),
            FineOnPartialPayment = false, AutoDeductSavings = true, CashStatementMaxAgeDays = cashMaxAgeDays
        });
        foreach (var (name, role) in new[] { ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer), ("Secretary", GroupRole.Secretary), ("M1", GroupRole.Member) })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, FullName = name, Email = name.ToLowerInvariant() + "@s17.tz", PasswordHash = "x", PhoneNumber = "" });
            db.GroupMembers.Add(new GroupMember { Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = name, Role = role, Status = MemberStatus.Active, JoinedAt = jan1 });
            w.P[name] = (mid, uid);
        }
        await db.SaveChangesAsync();
        return w;
    }

    private static TruthLedgerServiceTests.FakePdfImports Imports(W w) => new(w.Db, w.Parser, w.Storage, w.Setup);
    private static Guid IdOf(object o) => (Guid)o.GetType().GetProperty("Id")!.GetValue(o)!;
    private static MemoryStream Pdf() => new(TruthLedgerServiceTests.FakePdf());

    // ===================================================================== P0-2 / P1-7..9: PDF ya M-Koba kwa maker-checker
    [Fact]
    public async Task P0_2_Statement_Is_Staged_Only_And_Needs_A_Different_Leader()
    {
        var w = await Build();
        w.Parser.Rows = new() { Dep("R1", new(2026, 2, 1, 9, 0, 0), "255700000001", 10_000m, 10_000m, 1), Dep("R2", new(2026, 2, 2, 9, 0, 0), "255700000002", 10_000m, 20_000m, 2) };
        var svc = Imports(w);
        var staged = await svc.StageAsync(w.GroupId, Pdf(), "../../etc/passwd\u0001.pdf", w.U("Treasurer"));
        Assert.Equal(0, await w.Db.MkobaRawTransactions.CountAsync());                                // hakuna kinachoingia kabla ya idhini
        var imp = await w.Db.MkobaStatementImports.SingleAsync();
        Assert.Equal("passwd.pdf", imp.FileName);                                                     // jina la faili limesafishwa
        Assert.Contains(w.Storage.Files.Keys, k => k == imp.StorageKey);                              // faili limehifadhiwa (forensics)

        // Rasimu moja tu kwa wakati mmoja
        await Assert.ThrowsAsync<ConflictException>(() => svc.StageAsync(w.GroupId, Pdf(), "b.pdf", w.U("Chair")));
        // Aliyepakia hawezi kuidhinisha; mwanachama wa kawaida hawezi kabisa
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ApproveAsync(w.GroupId, IdOf(staged), null, w.U("Treasurer")));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ApproveAsync(w.GroupId, IdOf(staged), null, w.U("M1")));
        // Kikundi kingine hakiioni rasimu hii
        await Assert.ThrowsAnyAsync<Exception>(() => svc.ApproveAsync(Guid.NewGuid(), IdOf(staged), null, w.U("Chair")));

        var ok = await svc.ApproveAsync(w.GroupId, IdOf(staged), "nimehakiki", w.U("Chair"));
        Assert.Equal(2, ok.Report.Inserted);
        Assert.All(await w.Db.MkobaRawTransactions.ToListAsync(), t => Assert.Equal(imp.Id, t.ImportId));
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.StatementImportApproved);
        await Assert.ThrowsAsync<ConflictException>(() => svc.ApproveAsync(w.GroupId, IdOf(staged), null, w.U("Secretary"))); // mara moja tu
    }

    [Fact]
    public async Task P1_7_Same_Reference_With_Different_Data_Is_Refused_And_Recorded()
    {
        var w = await Build();
        w.Parser.Rows = new() { Dep("R1", new(2026, 2, 1, 9, 0, 0), "255700000001", 10_000m, 10_000m, 1) };
        var svc = Imports(w);
        await svc.ApproveAsync(w.GroupId, IdOf(await svc.StageAsync(w.GroupId, Pdf(), "a.pdf", w.U("Treasurer"))), null, w.U("Chair"));

        // PDF "iliyohaririwa": rejea ile ile, kiasi tofauti
        w.Parser.Rows = new() { Dep("R1", new(2026, 2, 1, 9, 0, 0), "255700000001", 90_000m, 90_000m, 1), Dep("R9", new(2026, 3, 1, 9, 0, 0), "255700000001", 10_000m, 100_000m, 2) };
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.StageAsync(w.GroupId, Pdf(), "b.pdf", w.U("Treasurer")));
        Assert.Contains("data TOFAUTI", ex.Message);
        Assert.Single(await w.Db.MkobaRawTransactions.ToListAsync());
        Assert.Equal(10_000m, (await w.Db.MkobaRawTransactions.SingleAsync()).Amount);
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.RiskBlocked && a.AfterJson!.Contains("STATEMENT_CONFLICT"));
    }

    [Fact]
    public async Task P1_8_Non_Pdf_Oversize_And_Tampered_Staged_File_Are_Refused()
    {
        var w = await Build();
        w.Parser.Rows = new() { Dep("R1", new(2026, 2, 1, 9, 0, 0), "255700000001", 10_000m, 10_000m, 1) };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mkoba:MaxUploadBytes"] = "64" }).Build();
        var small = new StatementImportService(w.Db, w.Parser, w.Storage, w.Setup, new NotificationService(w.Db), cfg,
            new ChamaLink.Infrastructure.Services.Events.EventWalletService(w.Db, new TruthLedgerService(w.Db)));
        await Assert.ThrowsAsync<ValidationException>(() => small.StageAsync(w.GroupId, new MemoryStream(new byte[65]), "big.pdf", w.U("Treasurer")));
        var svc = Imports(w);
        await Assert.ThrowsAsync<ValidationException>(() => svc.StageAsync(w.GroupId, new MemoryStream("MZ\u0090 not a pdf"u8.ToArray()), "x.pdf", w.U("Treasurer")));

        var staged = await svc.StageAsync(w.GroupId, Pdf(), "a.pdf", w.U("Treasurer"));
        var key = (await w.Db.MkobaStatementImports.SingleAsync()).StorageKey!;
        w.Storage.Files[key] = "%PDF-1.4 imebadilishwa"u8.ToArray();                                  // faili limeguswa baada ya kupakiwa
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ApproveAsync(w.GroupId, IdOf(staged), null, w.U("Chair")));
        Assert.Contains("limebadilika", ex.Message);
        Assert.Equal(0, await w.Db.MkobaRawTransactions.CountAsync());
    }

    [Fact]
    public async Task P1_9_Reject_Needs_A_Reason_From_Others_And_Uploader_Can_Cancel()
    {
        var w = await Build();
        w.Parser.Rows = new() { Dep("R1", new(2026, 2, 1, 9, 0, 0), "255700000001", 10_000m, 10_000m, 1) };
        var svc = Imports(w);
        var a = await svc.StageAsync(w.GroupId, Pdf(), "a.pdf", w.U("Treasurer"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.RejectAsync(w.GroupId, IdOf(a), "  ", w.U("Chair")));
        await svc.RejectAsync(w.GroupId, IdOf(a), "Kipindi si sahihi", w.U("Chair"));
        Assert.Equal(StatementImportStatus.Rejected, (await w.Db.MkobaStatementImports.SingleAsync()).Status);

        var b = await svc.StageAsync(w.GroupId, Pdf(), "b.pdf", w.U("Treasurer"));
        await svc.RejectAsync(w.GroupId, IdOf(b), null, w.U("Treasurer"));
        Assert.Equal(StatementImportStatus.Cancelled, (await w.Db.MkobaStatementImports.SingleAsync(x => x.Id == IdOf(b))).Status);
        Assert.Equal(0, await w.Db.MkobaRawTransactions.CountAsync());
    }

    // ===================================================================== P0-4: salio la M-Koba la zamani halitoshi kulipa
    [Fact]
    public async Task P0_4_Stale_Or_Unconfigured_Statement_Is_Never_Sufficient()
    {
        foreach (var (policy, daysAgo, expectSufficient) in new (int?, int, bool)[] { (null, 1, false), (30, 1, true), (30, 45, false) })
        {
            var w = await Build(policy);
            var at = TruthLedgerService.NowEat().Date.AddDays(-daysAgo).AddHours(10);
            w.Db.MkobaRawTransactions.Add(new MkobaRawTransaction
            {
                Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = "B" + daysAgo, Timestamp = DateTime.SpecifyKind(at, DateTimeKind.Utc), Phone = "255700000001",
                PayerName = "X", Amount = 1_000_000m, Balance = 1_000_000m, StatementFileHash = "h", ImportedByUserId = w.U("Chair")
            });
            await w.Db.SaveChangesAsync();
            var benefits = new BenefitService(w.Db, new TruthLedgerService(w.Db), new NotificationService(w.Db), w.Storage, new ConfigurationBuilder().Build(), new ChamaLink.Infrastructure.Services.Events.EventWalletService(w.Db, new TruthLedgerService(w.Db)));
            var cash = await benefits.CashPositionAsync(w.GroupId, null, 10_000m);
            Assert.Equal(expectSufficient, cash.Sufficient);
            Assert.Equal(!expectSufficient, cash.Stale);
            Assert.Equal(policy, cash.MaxAgeDays);
        }
    }

    // ===================================================================== NEW-3: anayeanzisha hawezi kuwa mhusika
    [Fact]
    public async Task NEW_3_Leader_Cannot_Initiate_Money_Or_Phones_For_Themselves()
    {
        var w = await Build();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.RequestManualPaymentAsync(w.GroupId, w.M("Treasurer"), 10_000m,
            new DateTime(2026, 2, 1, 10, 0, 0), false, "Malipo ya mkononi Feb", EvidenceType.CashBook, "Daftari uk. 3", w.U("Treasurer")));
        Assert.Equal(TruthSetupService.MakerIsSubjectMessage, ex.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.AddPhoneAsync(w.GroupId, w.M("Treasurer"), "0711222333", w.U("Treasurer"), null));
        // kiongozi mwingine anaweza kuanzisha kwa ajili yake
        await w.Setup.RequestManualPaymentAsync(w.GroupId, w.M("Treasurer"), 10_000m, new DateTime(2026, 2, 1, 10, 0, 0), false,
            "Malipo ya mkononi Feb", EvidenceType.CashBook, "Daftari uk. 3", w.U("Chair"));
    }

    // ===================================================================== P0-1 / NEW-4: hali isiyo hai = hakuna nguvu
    [Fact]
    public async Task P0_1_Suspended_Or_CoolingOff_Leader_Has_No_Powers()
    {
        var w = await Build();
        var t = await w.Db.GroupMembers.SingleAsync(m => m.Id == w.M("Treasurer"));
        t.Status = MemberStatus.Suspended;
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => MemberAccess.RequireEffectiveLeaderAsync(w.Db, w.GroupId, w.U("Treasurer")));
        await Assert.ThrowsAsync<ForbiddenException>(() => MemberAccess.RequireActiveAsync(w.Db, w.GroupId, w.U("Treasurer")));

        var s = await w.Db.GroupMembers.SingleAsync(m => m.Id == w.M("Secretary"));
        s.LeaderPowersFrom = DateTime.UtcNow.AddDays(3);
        Assert.Equal(GroupRole.Member, MemberAccess.EffectiveRole(s));
        Assert.Equal(GroupRole.Secretary, MemberAccess.EffectiveRole(s, DateTime.UtcNow.AddDays(4)));
        // Asiye mwanachama: 403 (si 404) — kuwepo kwa kikundi hakuthibitishwi kwa mgeni
        await Assert.ThrowsAsync<ForbiddenException>(() => MemberAccess.RequireActiveAsync(w.Db, w.GroupId, Guid.NewGuid()));
    }

    // ===================================================================== P0-6: kufuta data = viongozi wawili + swichi ya server
    [Fact]
    public async Task P0_6_Reset_Needs_Server_Switch_And_A_Second_Leader()
    {
        var w = await Build();
        var notify = new NotificationService(w.Db);
        var off = new ResetRequestService(w.Db, w.Setup, notify, new ConfigurationBuilder().Build());
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => off.RequestAsync(w.GroupId, false, "Kikundi S17", w.U("Chair")));
        Assert.Equal(ResetRequestService.DisabledMessage, ex.Message);
        Assert.DoesNotContain("AllowReset", ex.Message);                                              // jina la swichi halivuji
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.ResetBlocked);

        var on = new ResetRequestService(w.Db, w.Setup, notify,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ResetRequestService.AllowResetKey] = "true" }).Build());
        await Assert.ThrowsAsync<ValidationException>(() => on.RequestAsync(w.GroupId, false, "jina baya", w.U("Chair")));
        await Assert.ThrowsAsync<ForbiddenException>(() => on.RequestAsync(w.GroupId, false, "Kikundi S17", w.U("Secretary")));   // si Mwenyekiti/Mtunza
        var r = await on.RequestAsync(w.GroupId, false, "Kikundi S17", w.U("Chair"));
        await Assert.ThrowsAsync<ForbiddenException>(() => on.ApproveAsync(w.GroupId, r.Id, "Kikundi S17", w.U("Chair")));        // aliyeomba
        await on.ApproveAsync(w.GroupId, r.Id, "Kikundi S17", w.U("Treasurer"));
        Assert.Equal(ProposalStatus.Approved, (await w.Db.GroupResetRequests.SingleAsync()).Status);
    }

    // ===================================================================== M: kubadilisha ufunguo wa vyeti bila kupoteza faili
    [Fact]
    public void M_Evidence_Key_Rotation_Keeps_Old_Files_Readable_And_New_Files_Use_New_Key()
    {
        var oldKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var plain = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 cheti");
        var before = EvidenceCrypto.Protect(Cfg(new() { ["Benefits:EvidenceEncryptionKey"] = oldKey }), "g/c/1.pdf", plain);

        var rotated = Cfg(new() { ["Benefits:EvidenceEncryptionKey"] = newKey, [EvidenceCrypto.PreviousKeysKey] = oldKey });
        Assert.Equal(plain, EvidenceCrypto.Unprotect(rotated, "g/c/1.pdf", before));                 // ya zamani bado inafunguka
        var after = EvidenceCrypto.Protect(rotated, "g/c/2.pdf", plain);
        Assert.Throws<ConflictException>(() => EvidenceCrypto.Unprotect(Cfg(new() { ["Benefits:EvidenceEncryptionKey"] = oldKey }), "g/c/2.pdf", after)); // mpya = ufunguo mpya tu
        Assert.Throws<ConflictException>(() => EvidenceCrypto.Unprotect(Cfg(new() { ["Benefits:EvidenceEncryptionKey"] = newKey }), "g/c/1.pdf", before)); // bila PreviousKeys
        Assert.Throws<ConflictException>(() => EvidenceCrypto.Unprotect(rotated, "g/c/OTHER.pdf", before));          // AAD: haiwezi kuhamishwa
    }

    // ===================================================================== P0-5: namba za mnufaika kwa TAREHE ya muamala
    [Fact]
    public async Task P0_5_Beneficiary_Phones_Are_Those_Valid_At_The_Transaction_Date_Only()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var g = Guid.NewGuid(); var m = Guid.NewGuid(); var other = Guid.NewGuid(); var u = Guid.NewGuid();
        MemberPhone P(Guid mem, string phone, TruthApprovalStatus s, DateTime? from, DateTime? to) => new()
            { Id = Guid.NewGuid(), GroupId = g, GroupMemberId = mem, Phone = phone, Status = s, ValidFrom = from, ValidTo = to, AddedByUserId = u };
        db.MemberPhones.AddRange(
            P(m, "255711000001", TruthApprovalStatus.Approved, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31)),  // namba ya ZAMANI (sasa ni ya mtu mwingine)
            P(m, "255711000002", TruthApprovalStatus.Approved, new DateTime(2026, 4, 1), null),                      // namba ya SASA
            P(m, "255711000003", TruthApprovalStatus.Pending, new DateTime(2026, 1, 1), null),                       // haijaidhinishwa: haihesabiwi kamwe
            P(other, "255711000009", TruthApprovalStatus.Approved, new DateTime(2026, 1, 1), null));                 // mwanachama mwingine
        await db.SaveChangesAsync();

        Assert.Equal(new[] { "255711000001" }, await MemberPhonesAt.ListAsync(db, g, m, new DateTime(2026, 2, 10, 12, 0, 0), default));
        // Siku ya mwisho ya namba ya zamani inahesabiwa mzima (hadi 23:59:59 EAT); kesho yake ni namba mpya tu
        Assert.Equal(new[] { "255711000001" }, await MemberPhonesAt.ListAsync(db, g, m, new DateTime(2026, 3, 31, 23, 59, 0), default));
        Assert.Equal(new[] { "255711000002" }, await MemberPhonesAt.ListAsync(db, g, m, new DateTime(2026, 4, 1, 0, 0, 1), default));
        // Malipo ya leo kwenda namba ya ZAMANI (iliyorudishwa kwa mtu mwingine) SI malipo kwa mnufaika → onyo linatolewa
        var now = await MemberPhonesAt.ListAsync(db, g, m, new DateTime(2026, 8, 20), default);
        Assert.DoesNotContain("255711000001", now);
        Assert.DoesNotContain("255711000003", now);
        Assert.DoesNotContain("255711000009", now);
    }

    // ===================================================================== P1-10: funguo za kufuli ni bit 64, za kipekee
    [Fact]
    public void P1_10_Advisory_Lock_Keys_Are_64bit_Deterministic_And_Distinct()
    {
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid();
        Assert.Equal(DbTx.Key(g1, DbTx.CashCommitment), DbTx.Key(g1, DbTx.CashCommitment));
        Assert.NotEqual(DbTx.Key(g1, DbTx.CashCommitment), DbTx.Key(g2, DbTx.CashCommitment));
        Assert.NotEqual(DbTx.Key(g1, DbTx.CashCommitment), DbTx.Key(g1, DbTx.StatementImport));
        var keys = Enumerable.Range(0, 2000).Select(_ => DbTx.Key(Guid.NewGuid(), DbTx.Decision)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains(keys, k => k > int.MaxValue || k < int.MinValue);                             // kweli ni bit 64
    }

    // ===================================================================== P1-5: API haianzi production ikiwa usanidi ni hatari
    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Cfg(Dictionary<string, string?> d) => new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    private static Dictionary<string, string?> SafeConfig() => new()
    {
        ["Jwt:Key"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
        ["Benefits:EvidenceEncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        ["Benefits:EvidencePath"] = "/var/lib/chamalink/evidence", ["Benefits:MaxEvidenceBytes"] = "5242880",
        ["Cors:AllowedOrigins:0"] = "https://chamalink.example.org"
    };

    [Fact]
    public void P1_5_ProductionGuard_Refuses_Unsafe_Configuration()
    {
        Assert.Empty(ProductionGuard.Problems(Cfg(SafeConfig())));
        ProductionGuard.EnforceOrThrow(Cfg(SafeConfig()), new Env(), NullLogger.Instance);

        foreach (var (k, v) in new[]
                 {
                     ("Truth:AllowReset", "true"), ("Legacy:Endpoints", "true"), ("Legacy:BackgroundJobs", "true"), ("Errors:IncludeExceptionDetails", "true"),
                     ("Jwt:Key", ProductionGuard.LeakedSandboxJwtKey), ("Jwt:Key", new string('a', 64)), ("Benefits:EvidenceEncryptionKey", "fupi"),
                     ("Benefits:EvidencePath", ""), ("Cors:AllowedOrigins:0", "http://localhost:5173"), ("Cors:AllowedOrigins:0", "http://chamalink.example.org")
                 })
        {
            var c = SafeConfig(); c[k] = v;
            var ex = Assert.Throws<InvalidOperationException>(() => ProductionGuard.EnforceOrThrow(Cfg(c), new Env(), NullLogger.Instance));
            Assert.DoesNotContain(c["Benefits:EvidenceEncryptionKey"]!, ex.Message);                 // siri haziandikwi kwenye ujumbe
        }
        // Development inaruhusiwa (majaribio ya ndani)
        var dev = SafeConfig(); dev["Truth:AllowReset"] = "true";
        ProductionGuard.EnforceOrThrow(Cfg(dev), new Env { EnvironmentName = "Development" }, NullLogger.Instance);
    }
}
