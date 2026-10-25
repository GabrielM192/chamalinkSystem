using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Tests;

/// <summary>
/// Ushauri 2026-09-28: mafao (msiba/sherehe) + ulinzi (maker-checker kila mahali) + namba zenye vipindi +
/// faini ya asilimia + kura za hali ya mwanachama. Kila test inathibitisha kwamba MTU MMOJA hawezi kutoa pesa
/// wala kubadilisha "ukweli" peke yake.
/// </summary>
public class BenefitsAndGovernanceTests
{
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
        public required TruthLedgerService Truth;
        public required TruthSetupService Setup;
        public required BenefitService Benefits;
        public required MemberStatusService Status;
        public required MemoryStorage Storage;
        public Guid GroupId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId)> P = new();
        public Guid U(string n) => P[n].UserId;
        public Guid M(string n) => P[n].MemberId;
    }

    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<W> Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);
        var truth = new TruthLedgerService(db);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Benefits:MaxEvidenceBytes"] = "1048576" }).Build();
        var notify = new NotificationService(db);
        var storage = new MemoryStorage();
        var benefits = new BenefitService(db, truth, notify, storage, config, new ChamaLink.Infrastructure.Services.Events.EventWalletService(db, truth));
        var w = new W
        {
            Db = db, Truth = truth, Setup = new TruthSetupService(db, new NoParser(), truth), Benefits = benefits,
            Status = new MemberStatusService(db, benefits, notify), Storage = storage, GroupId = Guid.NewGuid()
        };
        db.Groups.Add(new Group { Id = w.GroupId, Name = "Kikundi cha majaribio", ShortCode = "TST" });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 10_000m, LateFine = 5_000m, JoiningFee = 50_000m, DueDateDay = 5, GracePeriodDays = 0,
            ContributionStartDate = Jan1, FoundingMembersCutoff = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
            FineOnPartialPayment = false, AutoDeductSavings = true,
            CashStatementMaxAgeDays = 120   // SASISHO17 (P0-4): bila sera hii malipo yanazuiwa (fail-closed)
        });
        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer), ("Secretary", GroupRole.Secretary),
                     ("M1", GroupRole.Member), ("M2", GroupRole.Member), ("M3", GroupRole.Member), ("Ben", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = "" });
            db.GroupMembers.Add(new GroupMember { Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = name, Role = role, Status = MemberStatus.Active, JoinedAt = Jan1 });
            w.P[name] = (mid, uid);
        }
        await db.SaveChangesAsync();
        return w;
    }

    private sealed class NoParser : ChamaLink.Application.Interfaces.IMKobaParserService
    {
        public Task<List<ChamaLink.Application.DTOs.MKobaTransactionItemDto>> ParseStatementAsync(Stream stream) => throw new NotSupportedException();
        public Task<ChamaLink.Application.DTOs.MKobaParseResult> ParseStatementDetailedAsync(Stream stream) => throw new NotSupportedException();
    }

    private static MkobaRawTransaction Raw(Guid groupId, string reference, DateTime at, decimal amount, bool withdrawal, string phone = "255700000000") => new()
    {
        Id = Guid.NewGuid(), GroupId = groupId, Reference = reference, Timestamp = DateTime.SpecifyKind(at, DateTimeKind.Utc) /* kama TruthSetupService: saa za EAT, Kind=Utc */, Amount = amount, IsWithdrawal = withdrawal, Phone = phone,
        PayerName = withdrawal ? "WITHDRAW" : "PAYER", Description = withdrawal ? "Withdraw" : "Deposit", StatementFileHash = "h"
    };

    private static BenefitService.GovernanceDto Gov() => new()
    {
        RequiredApprovals = 3, MinLeaderApprovals = 1, AllowMemberApprovers = true, RejectionsToReject = 2, VotingDays = 7,
        HeadcountRule = BenefitHeadcountRule.AllActiveMembers, ExcludeBeneficiaryFromHeadcount = true, FundingSource = BenefitFundingSource.GroupFund,
        DeductJoinFee = true, DeductArrears = false, RequireNoArrearsForEligibility = false, MinMembershipDays = 0, ClaimWindowDays = 60,
        PaymentConfirmationDays = 7, StatusChangeApprovals = 2, ExpulsionVotePercent = 75,
        RequireJoinFeePaid = false, RequireCashAvailability = true, ApproverCannotInitiatePayment = true, ExcludeRelatedParties = true,
        PaymentInitiatorRoles = new() { GroupRole.Treasurer }, PaymentConfirmerRoles = new() { GroupRole.Chairperson, GroupRole.Secretary }
    };

    private static async Task SetupPolicy(W w, Action<BenefitService.GovernanceDto>? tweak = null)
    {
        var dto = Gov();
        tweak?.Invoke(dto);
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, dto, w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        var r = await w.Benefits.ProposeRateAsync(w.GroupId, new BenefitService.RateDto
        {
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent, PerMemberAmount = 20_000m,
            AcceptedEvidence = new() { EvidenceDocumentType.DeathCertificate, EvidenceDocumentType.BurialPermit }, EffectiveFrom = Jan1
        }, w.U("Treasurer"));
        await w.Benefits.DecideRateAsync(w.GroupId, r.Id, true, null, w.U("Chair"));
    }

    private static byte[] Pdf(string salt) => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n" + salt + "\n%%EOF");

    private static BenefitService.OpenCaseDto CaseDto(W w, string subject = "Mama wa Ben") => new()
    {
        Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent, BeneficiaryMemberId = w.M("Ben"),
        SubjectName = subject, EventDate = TruthLedgerService.NowEat().Date.AddDays(-3)
    };

    // =====================================================================
    [Fact]
    public async Task Member_Can_Request_For_Self_Upload_Evidence_And_Cancel_Before_Voting()
    {
        var w = await Build();
        await SetupPolicy(w);
        // Mwanachama hawezi kuombea mwingine
        var forOther = CaseDto(w); forOther.BeneficiaryMemberId = w.M("M1");
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.OpenCaseAsync(w.GroupId, forOther, w.U("Ben")));
        // Anaomba kwa ajili yake mwenyewe → viongozi wanaarifiwa
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Ben"));
        Assert.Equal(BenefitCaseStatus.AwaitingEvidence, c.Status);
        Assert.Contains(await w.Db.MemberNotifications.ToListAsync(), n => n.Title.StartsWith("Ombi jipya la mafao"));
        // Anapakia cheti chake; kiongozi yeyote (si yeye) anahakiki
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-77", "c.pdf", Pdf("m"), w.U("Ben"));
        // Mwanachama mwingine hawezi kughairi ombi lisilo lake
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.CancelCaseAsync(w.GroupId, c.Id, "si langu", w.U("M1")));
        // Kabla kura hazijaanza: mwombaji anaweza kughairi
        var second = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Baba wa Ben"), w.U("Ben"));
        await w.Benefits.CancelCaseAsync(w.GroupId, second.Id, "nimekosea", w.U("Ben"));
        Assert.Equal(BenefitCaseStatus.Cancelled, (await w.Db.BenefitCases.AsNoTracking().SingleAsync(x => x.Id == second.Id)).Status);
        // Baada ya uhakiki kura zinaanza → mwombaji HAWEZI kughairi tena
        c = await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.Voting, c.Status);
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.CancelCaseAsync(w.GroupId, c.Id, "nimebadili", w.U("Ben")));
    }

    [Fact]
    public async Task Policy_Requires_Second_Leader_And_No_Defaults()
    {
        var w = await Build();
        // Hakuna sera -> hakuna kesi (fail-closed)
        await Assert.ThrowsAsync<PolicyNotConfiguredException>(() => w.Benefits.AssessAsync(w.GroupId, CaseDto(w)));
        // Sera isiyokamilika inakataliwa (hakuna default ya idadi ya kura n.k.)
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, new BenefitService.GovernanceDto { RequiredApprovals = 3 }, w.U("Chair")));
        // Kura 1 hairuhusiwi (mtu mmoja hawezi kutoa pesa)
        var one = Gov(); one.RequiredApprovals = 1; one.MinLeaderApprovals = 1;
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, one, w.U("Chair")));
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, Gov(), w.U("Chair"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Chair")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("M1"))); // si kiongozi
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Secretary"));
        Assert.NotNull(await w.Benefits.GetActiveGovernanceAsync(w.GroupId));
    }

    [Fact]
    public async Task FullFlow_Evidence_Votes_MkobaConfirmation_And_JoinFeeOffset()
    {
        var w = await Build();
        await SetupPolicy(w);

        // Ustahiki + hesabu: 6 (bila mnufaika) × 20,000 = 120,000; Kianzio 50,000 kinakatwa -> 70,000
        var a = await w.Benefits.AssessAsync(w.GroupId, CaseDto(w));
        Assert.True(a.Eligible, string.Join(" | ", a.Checks.Where(c => !c.Passed).Select(c => c.Message)));
        Assert.Equal(6, a.Headcount);
        Assert.Equal(120_000m, a.Gross);
        Assert.Equal(50_000m, a.JoinFeeDeducted);
        Assert.Equal(70_000m, a.Net);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("M1"))); // si kiongozi
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        Assert.Equal("TST-MF-2026-001", c.CaseNumber);
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Chair"))); // tukio lile lile mara mbili

        // Ushahidi: mnufaika anapakia; faili si PDF/JPG/PNG -> kataa
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-1", "a.txt",
            System.Text.Encoding.ASCII.GetBytes("hello"), w.U("Ben")));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "dc 12345", "cheti.pdf", Pdf("a"), w.U("Ben"));
        Assert.Equal("DC 12345", e.DocumentNumber);
        // Mwanachama asiye mnufaika wala kiongozi hawezi kupakia
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.BurialPermit, "BP-1", "b.pdf", Pdf("b"), w.U("M1")));

        // Wanaokataliwa kuhakiki: aliyepakia, aliyefungua kesi, mnufaika
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Secretary")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("M1")));
        c = await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.Voting, c.Status);
        Assert.NotNull(c.SnapshotAt);

        // Arifa: kila aliyehesabiwa anaona mgao wake
        Assert.Equal(6, await w.Db.MemberNotifications.CountAsync(n => n.Kind == "BenefitShare"));

        // Kura: mnufaika, aliyefungua kesi, aliyehakiki -> hawapigi
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Ben")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Secretary")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Treasurer")));
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, false, null, w.U("M3"))); // HAPANA bila sababu
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1"));
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1"))); // kura moja kwa mtu
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M2"));
        var r = await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Chair"));
        Assert.Equal(BenefitCaseStatus.Approved, r.Case.Status);

        // Malipo: hatua 2 (kuanzisha → kuthibitisha), muamala wa M-Koba wenye kiasi SAWA, baada ya idhini, usiotumika
        var now = TruthLedgerService.NowEat();
        w.Db.MemberPhones.Add(new MemberPhone { Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.M("Ben"), Phone = "255711111111", Status = TruthApprovalStatus.Approved, ValidFrom = Jan1 });
        var bal = Raw(w.GroupId, "BAL1", now.AddDays(-10), 10_000m, false); bal.Balance = 500_000m;
        w.Db.MkobaRawTransactions.AddRange(bal,
            Raw(w.GroupId, "WRONGAMT", now, 120_000m, true, "255711111111"),
            Raw(w.GroupId, "TOOEARLY", now.AddDays(-5), 70_000m, true, "255711111111"),
            Raw(w.GroupId, "DEPOSIT1", now, 70_000m, false),
            Raw(w.GroupId, "GOODREF", now.AddMinutes(-1), 70_000m, true, "255711111111"));
        await w.Db.SaveChangesAsync();
        // Kuthibitisha kabla ya kuanzisha -> kataa (na jaribio linaandikwa kwenye ukaguzi)
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "GOODREF", w.U("Secretary")));
        Assert.True(await w.Db.AuditEvents.AnyAsync(x => x.Action == AuditAction.RiskBlocked));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, null, w.U("Chair")));      // si nafasi ya kuanzisha (pia aliidhinisha)
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, null, w.U("M1")));         // si kiongozi
        c = await w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, null, w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.PaymentInitiated, c.Status);
        Assert.Equal("255711111111", c.PaymentInitiatedToPhone);
        Assert.Equal(500_000m, c.CashAvailableAtInitiation);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "GOODREF", w.U("Treasurer"))); // aliyeanzisha
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "GOODREF", w.U("M1")));   // si kiongozi
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "WRONGAMT", w.U("Secretary")));
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "TOOEARLY", w.U("Secretary")));
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "DEPOSIT1", w.U("Secretary")));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "HAIPO", w.U("Secretary")));
        c = await w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "goodref", w.U("Secretary"));
        Assert.Equal(BenefitCaseStatus.Paid, c.Status);

        // B6: maandishi ya fedha: 120,000 = Kianzio 50,000 + deni 0 + pesa 70,000 — na yanalingana na M-Koba + Injini ya Ukweli
        var entries = await w.Db.BenefitLedgerEntries.Where(x => x.BenefitCaseId == c.Id).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal(120_000m, entries.Single(x => x.Kind == BenefitLedgerKind.Approved).Amount);
        Assert.Equal(50_000m, entries.Single(x => x.Kind == BenefitLedgerKind.JoinFeeOffset).Amount);
        Assert.Equal(70_000m, entries.Single(x => x.Kind == BenefitLedgerKind.CashPaid).Amount);
        var rec = System.Text.Json.JsonSerializer.SerializeToElement(await w.Benefits.ReconciliationAsync(w.GroupId, w.U("Chair")), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.True(rec.GetProperty("ok").GetBoolean(), rec.ToString());

        // Injini ya Ukweli: Kianzio kilichokatwa kinaonekana kama kimelipwa (MAFAO-JF-...)
        var ben = await w.Truth.GetMemberTruthAsync(w.M("Ben"));
        Assert.Equal(50_000m, ben.Statement.JoinFeePaid);
        Assert.Equal(0m, ben.Statement.JoinFeeOutstanding);
        Assert.Equal(50_000m, ben.Statement.TotalWelfareOffset);

        // Daftari + tahadhari: utoaji ambao haujaelezwa unaonekana kwa wote
        var ledger = await w.Benefits.LedgerAsync(w.GroupId);
        Assert.Single(ledger);
        Assert.Equal("GOODREF", ledger[0].MkobaReference);
        var alerts = await w.Benefits.AlertsAsync(w.GroupId);
        var un = Assert.Single(alerts, x => x.Code == "UNEXPLAINED_WITHDRAWALS");
        Assert.Contains("WRONGAMT", un.Message);
        Assert.DoesNotContain("GOODREF", un.Message);

        // Mwanachama anaona alichochangia (mgao wake) na mnufaika anaona alichopokea
        var mine = System.Text.Json.JsonSerializer.SerializeToElement(await w.Benefits.MyBenefitsAsync(w.GroupId, w.U("M1")));
        Assert.Equal(20_000m, mine.GetProperty("totalContributed").GetDecimal());
        var bens = System.Text.Json.JsonSerializer.SerializeToElement(await w.Benefits.MyBenefitsAsync(w.GroupId, w.U("Ben")));
        Assert.Equal(70_000m, bens.GetProperty("totalReceived").GetDecimal());
    }

    [Fact]
    public async Task Same_Certificate_Cannot_Claim_Twice()
    {
        var w = await Build();
        await SetupPolicy(w);
        var c1 = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Baba wa Ben"), w.U("Secretary"));
        await w.Benefits.UploadEvidenceAsync(w.GroupId, c1.Id, EvidenceDocumentType.DeathCertificate, "DC-77", "a.pdf", Pdf("x"), w.U("Ben"));
        var c2 = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Mama wa Ben"), w.U("Secretary"));
        // faili lile lile (hash ile ile)
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.UploadEvidenceAsync(w.GroupId, c2.Id, EvidenceDocumentType.DeathCertificate, "DC-78", "b.pdf", Pdf("x"), w.U("Ben")));
        // namba ya cheti ile ile (faili tofauti)
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.UploadEvidenceAsync(w.GroupId, c2.Id, EvidenceDocumentType.DeathCertificate, "dc-77", "c.pdf", Pdf("y"), w.U("Ben")));
    }

    [Fact]
    public async Task Evidence_File_Visible_To_Leaders_And_Beneficiary_Only_And_Audited()
    {
        var w = await Build();
        await SetupPolicy(w);
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-9", "a.pdf", Pdf("z"), w.U("Ben"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.ReadEvidenceAsync(w.GroupId, e.Id, w.U("M1")));
        var (_, bytes) = await w.Benefits.ReadEvidenceAsync(w.GroupId, e.Id, w.U("Chair"));
        Assert.Equal(Pdf("z"), bytes);
        Assert.True(await w.Db.AuditEvents.AnyAsync(x => x.Action == AuditAction.EvidenceAccessed));
    }

    [Fact]
    public async Task Historical_Case_Must_Match_Mkoba_Exactly_And_Needs_Second_Leader()
    {
        var w = await Build();
        // Utoaji halisi wa Ukonga: 20,000 × 32 = 640,000 (DFNO319K1X, 23/06/2026)
        w.Db.MkobaRawTransactions.Add(Raw(w.GroupId, "DFNO319K1X", new DateTime(2026, 6, 23, 14, 0, 0), 640_000m, true));
        await w.Db.SaveChangesAsync();
        var dto = new BenefitService.HistoricalCaseDto
        {
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent, BeneficiaryMemberId = w.M("M2"), SubjectName = "Mzazi wa M2",
            EventDate = new DateTime(2026, 6, 20), MkobaReference = "DFNO319K1X", PerMemberAmount = 20_000m, Headcount = 31
        };
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Treasurer"))); // 620,000 ≠ 640,000
        dto.Headcount = 32;
        var c = await w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.HistoricalPendingConfirmation, c.Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("Treasurer")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("M2")));
        c = await w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("Chair"));
        Assert.Equal(BenefitCaseStatus.Paid, c.Status);
        // Reference moja = kesi moja
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Secretary")));
        Assert.DoesNotContain(await w.Benefits.AlertsAsync(w.GroupId), a => a.Code == "UNEXPLAINED_WITHDRAWALS");
    }

    [Fact]
    public async Task Withdrawal_Explanation_Needs_Second_Leader()
    {
        var w = await Build();
        w.Db.MkobaRawTransactions.Add(Raw(w.GroupId, "LOAN1", new DateTime(2026, 5, 2, 10, 0, 0), 100_000m, true));
        await w.Db.SaveChangesAsync();
        var x = await w.Benefits.ExplainWithdrawalAsync(w.GroupId, "LOAN1", WithdrawalExplanationKind.LoanDisbursement, "Mkopo wa M1 (kikao 1/5)", w.U("Treasurer"));
        Assert.Contains(await w.Benefits.AlertsAsync(w.GroupId), a => a.Code == "UNEXPLAINED_WITHDRAWALS");  // bado haujaidhinishwa
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideExplanationAsync(w.GroupId, x.Id, true, null, w.U("Treasurer")));
        await w.Benefits.DecideExplanationAsync(w.GroupId, x.Id, true, null, w.U("Chair"));
        Assert.DoesNotContain(await w.Benefits.AlertsAsync(w.GroupId), a => a.Code == "UNEXPLAINED_WITHDRAWALS");
    }

    [Fact]
    public async Task Expulsion_Needs_Three_Quarters_Of_All_Members_And_Stops_New_Obligations()
    {
        var w = await Build();
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, Gov(), w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        var effective = new DateTime(2026, 6, 30);
        var r = await w.Status.RequestAsync(w.GroupId, new MemberStatusService.RequestDto
        {
            MemberId = w.M("M3"), ToStatus = MemberStatus.Exited, IsExpulsion = true, EffectiveDate = effective, Reason = "Kanuni 12(3)"
        }, w.U("Chair"));
        Assert.Equal(5, r.RequiredApprovals); // ceil(75% × 6 wanaostahili kupiga kura)
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Status.VoteAsync(w.GroupId, r.Id, false, "sikubali", w.U("M3"))); // mhusika
        foreach (var n in new[] { "Chair", "Treasurer", "Secretary", "M1" })
            await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U(n));
        Assert.Equal(MemberStatus.Active, (await w.Db.GroupMembers.FindAsync(w.M("M3")))!.Status); // 4 < 5
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("M2"));
        Assert.Equal(MemberStatus.Exited, (await w.Db.GroupMembers.FindAsync(w.M("M3")))!.Status);
        var h = await w.Db.MemberStatusHistories.SingleAsync(x => x.MemberId == w.M("M3"));
        Assert.Equal(effective.Date, h.EffectiveFrom.Date);

        // Injini: hakuna michango mipya baada ya tarehe ya kutoka (Jan..Jun tu = miezi 6)
        var t = await w.Truth.GetMemberTruthAsync(w.M("M3"), new DateTime(2026, 9, 20, 12, 0, 0));
        Assert.Equal(new DateTime(2026, 6, 30), t.ObligationsEndAt!.Value.Date);
        Assert.Equal(6, t.Statement.Periods.Count);
    }

    [Fact]
    public async Task NonExpulsion_Status_Change_Needs_Leaders_Other_Than_Requester()
    {
        var w = await Build();
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, Gov(), w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        var r = await w.Status.RequestAsync(w.GroupId, new MemberStatusService.RequestDto
        {
            MemberId = w.M("M1"), ToStatus = MemberStatus.Deceased, EffectiveDate = new DateTime(2026, 7, 1), Reason = "Amefariki"
        }, w.U("Secretary"));
        Assert.True(r.LeadersOnly);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Secretary"))); // aliyeomba
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("M2")));        // si kiongozi
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Chair"));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Treasurer"));
        Assert.Equal(MemberStatus.Deceased, (await w.Db.GroupMembers.FindAsync(w.M("M1")))!.Status);
    }

    // ============================ SASISHO17 (K / NEW-4 / P0-1) ============================
    [Fact]
    public async Task S17_K_Future_Dated_Status_Change_Waits_For_Its_Date_Then_Scheduler_Applies_It()
    {
        var w = await Build();
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, Gov(), w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        var future = TruthLedgerService.NowEat().Date.AddDays(10);
        var r = await w.Status.RequestAsync(w.GroupId, new MemberStatusService.RequestDto
        {
            MemberId = w.M("M1"), ToStatus = MemberStatus.Suspended, EffectiveDate = future, Reason = "Kanuni 9"
        }, w.U("Secretary"));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Chair"));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Treasurer"));
        var req = await w.Db.MemberStatusChangeRequests.AsNoTracking().SingleAsync(x => x.Id == r.Id);
        Assert.Equal(ProposalStatus.Approved, req.Status);
        Assert.Null(req.AppliedAt);                                                            // bado haijatekelezwa
        Assert.Equal(MemberStatus.Active, (await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == w.M("M1"))).Status);
        Assert.Equal(0, await w.Status.ApplyDueAsync());                                       // tarehe haijafika

        // Tarehe ikifika, mpangaji (scheduler) anaitekeleza — mara moja tu
        var tracked = await w.Db.MemberStatusChangeRequests.SingleAsync(x => x.Id == r.Id);
        tracked.EffectiveDate = DateTime.SpecifyKind(TruthLedgerService.NowEat().Date, DateTimeKind.Utc);
        await w.Db.SaveChangesAsync();
        Assert.Equal(1, await w.Status.ApplyDueAsync());
        Assert.Equal(0, await w.Status.ApplyDueAsync());
        Assert.Equal(MemberStatus.Suspended, (await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == w.M("M1"))).Status);
        Assert.NotNull((await w.Db.MemberStatusChangeRequests.AsNoTracking().SingleAsync(x => x.Id == r.Id)).AppliedAt);
    }

    [Fact]
    public async Task S17_NEW4_Leader_Leaving_Active_Status_Loses_Role_And_Restoring_Does_Not_Return_It()
    {
        var w = await Build();
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, Gov(), w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        // Viongozi wa kutosha kupiga kura (si aliyeomba wala mhusika): Katibu + Naibu Katibu
        var uid = Guid.NewGuid();
        w.Db.Users.Add(new User { Id = uid, FullName = "Naibu", PhoneNumber = "" });
        w.Db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = w.GroupId, UserId = uid, MemberNumber = "Naibu", Role = GroupRole.Secretary, Status = MemberStatus.Active, JoinedAt = Jan1 });
        await w.Db.SaveChangesAsync();
        var today = TruthLedgerService.NowEat().Date;
        var r = await w.Status.RequestAsync(w.GroupId, new MemberStatusService.RequestDto
        {
            MemberId = w.M("Treasurer"), ToStatus = MemberStatus.Suspended, EffectiveDate = today, Reason = "Uchunguzi wa fedha"
        }, w.U("Chair"));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, w.U("Secretary"));
        Assert.Equal(ProposalStatus.Pending, (await w.Db.MemberStatusChangeRequests.AsNoTracking().SingleAsync(x => x.Id == r.Id)).Status);
        // Mtunza-hazina mhusika hawezi kujipigia kura
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Status.VoteAsync(w.GroupId, r.Id, false, "hapana", w.U("Treasurer")));
        await w.Status.VoteAsync(w.GroupId, r.Id, true, null, uid);

        var t = await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == w.M("Treasurer"));
        Assert.Equal(MemberStatus.Suspended, t.Status);
        Assert.Equal(GroupRole.Member, t.Role);                                                 // cheo kimeondolewa
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.LeaderRoleRevokedByStatus);
        await Assert.ThrowsAsync<ForbiddenException>(() => new GroupAuthorizationService(w.Db).RequireMembershipAsync(w.U("Treasurer"), w.GroupId));

        // Akirejeshwa (Active) bado ni Mwanachama — cheo kinarudi kwa kura ya cheo tu
        var back = await w.Status.RequestAsync(w.GroupId, new MemberStatusService.RequestDto
        {
            MemberId = w.M("Treasurer"), ToStatus = MemberStatus.Active, EffectiveDate = today, Reason = "Uchunguzi umekwisha"
        }, w.U("Chair"));
        await w.Status.VoteAsync(w.GroupId, back.Id, true, null, w.U("Secretary"));
        await w.Status.VoteAsync(w.GroupId, back.Id, true, null, uid);
        var t2 = await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == w.M("Treasurer"));
        Assert.Equal(MemberStatus.Active, t2.Status);
        Assert.Equal(GroupRole.Member, t2.Role);
    }

    [Fact]
    public async Task S17_P0_1_RequireMembership_Is_Active_Only_But_Own_Record_Stays_Readable()
    {
        var w = await Build();
        var auth = new GroupAuthorizationService(w.Db);
        foreach (var st in new[] { MemberStatus.Suspended, MemberStatus.Exited, MemberStatus.Deceased, MemberStatus.Archived })
        {
            var m = await w.Db.GroupMembers.SingleAsync(x => x.Id == w.M("M2"));
            m.Status = st;
            await w.Db.SaveChangesAsync();
            await Assert.ThrowsAsync<ForbiddenException>(() => auth.RequireMembershipAsync(w.U("M2"), w.GroupId));
            await Assert.ThrowsAsync<ForbiddenException>(() => auth.RequireRoleAsync(w.U("M2"), w.GroupId, GroupRole.Member));
            Assert.Equal(w.M("M2"), (await auth.RequireOwnRecordAsync(w.U("M2"), w.GroupId)).Id);   // kusoma yake mwenyewe
        }
        // Kiongozi aliyesimamishwa: hata nafasi yake ya zamani haimpi ruhusa
        var c = await w.Db.GroupMembers.SingleAsync(x => x.Id == w.M("Secretary"));
        c.Status = MemberStatus.Suspended;
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => auth.RequireRoleAsync(w.U("Secretary"), w.GroupId, GroupRole.Secretary));
        // Mgeni: 403
        await Assert.ThrowsAsync<ForbiddenException>(() => auth.RequireOwnRecordAsync(Guid.NewGuid(), w.GroupId));
    }

    // ============================ NAMBA ZA SIMU ============================
    [Fact]
    public async Task Phone_Validity_Period_Fauzia_Number_Counts_For_Abdulla_Only_Until_End_Date()
    {
        var w = await Build();
        const string fauzia = "255658479089";
        w.Db.MkobaRawTransactions.AddRange(
            Raw(w.GroupId, "JUN1", new DateTime(2026, 6, 3, 10, 0, 0), 10_000m, false, fauzia),
            Raw(w.GroupId, "JUL1", new DateTime(2026, 7, 3, 10, 0, 0), 10_000m, false, fauzia));
        await w.Db.SaveChangesAsync();

        var p = await w.Setup.AddPhoneAsync(w.GroupId, w.M("M1"), fauzia, w.U("Treasurer"), "Namba ya Fauzia ililipia M1", default, null, new DateTime(2026, 6, 30));
        Assert.Equal(1, p.DepositsFound); // ndani ya kipindi tu
        await w.Setup.DecidePhoneAsync(w.GroupId, p.Phone.Id, true, w.U("Chair"), null);
        var t = await w.Truth.GetMemberTruthAsync(w.M("M1"), new DateTime(2026, 9, 1, 12, 0, 0));
        Assert.Equal(10_000m, t.Statement.TotalAcceptedFromMkoba);
        var un = await w.Truth.GetUnattributedAsync(w.GroupId);
        Assert.Contains(un.DepositsWithUnknownPhone, d => d.Reference == "JUL1");   // baada ya tarehe -> viongozi waamue
        Assert.DoesNotContain(un.DepositsWithUnknownPhone, d => d.Reference == "JUN1");

        // Kipindi kinachopishana kwa mtu mwingine -> kataa; kuanzia Julai -> sawa
        await Assert.ThrowsAsync<ConflictException>(() => w.Setup.AddPhoneAsync(w.GroupId, w.M("M2"), fauzia, w.U("Treasurer"), null, default, new DateTime(2026, 6, 15), null));
        var p2 = await w.Setup.AddPhoneAsync(w.GroupId, w.M("M2"), fauzia, w.U("Treasurer"), null, default, new DateTime(2026, 7, 1), null);
        await w.Setup.DecidePhoneAsync(w.GroupId, p2.Phone.Id, true, w.U("Secretary"), null);
        Assert.Equal(10_000m, (await w.Truth.GetMemberTruthAsync(w.M("M2"), new DateTime(2026, 9, 1, 12, 0, 0))).Statement.TotalAcceptedFromMkoba);
    }

    [Fact]
    public async Task Phone_End_And_Remove_Need_Second_Leader_History_Kept()
    {
        var w = await Build();
        var p = await w.Setup.AddPhoneAsync(w.GroupId, w.M("M1"), "255711111111", w.U("Treasurer"), null);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.RemovePhoneAsync(w.GroupId, p.Phone.Id, w.U("Chair"))); // si aliyeomba
        await w.Setup.DecidePhoneAsync(w.GroupId, p.Phone.Id, true, w.U("Chair"), null);
        await Assert.ThrowsAsync<ConflictException>(() => w.Setup.RemovePhoneAsync(w.GroupId, p.Phone.Id, w.U("Treasurer"))); // iliyoidhinishwa haifutwi

        await w.Setup.RequestEndPhoneAsync(w.GroupId, p.Phone.Id, new DateTime(2026, 6, 30), "Si yake tena", w.U("Treasurer"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.DecideEndPhoneAsync(w.GroupId, p.Phone.Id, true, w.U("Treasurer"), null));
        var ended = await w.Setup.DecideEndPhoneAsync(w.GroupId, p.Phone.Id, true, w.U("Chair"), null);
        Assert.Equal(new DateTime(2026, 6, 30), ended.ValidTo!.Value.Date);
        Assert.Null(ended.PendingValidTo);
        Assert.True(await w.Db.MemberPhones.AnyAsync(x => x.Id == p.Phone.Id)); // historia inabaki
        Assert.True(await w.Db.AuditEvents.AnyAsync(x => x.Action == AuditAction.PhoneLinkEnded));

        // Mhusika hawezi kuidhinisha namba yake mwenyewe
        var mine = await w.Setup.AddPhoneAsync(w.GroupId, w.M("Chair"), "255722222222", w.U("Treasurer"), null);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.DecidePhoneAsync(w.GroupId, mine.Phone.Id, true, w.U("Chair"), null));
    }

    // ============================ KANUNI ============================
    [Fact]
    public async Task LateFine_Percent_Mode_Follows_Contribution()
    {
        var w = await Build();
        var s = await w.Db.TruthGroupSettings.SingleAsync();
        s.LateFinePercent = 50m; s.MonthlyContribution = 15_000m;
        await w.Db.SaveChangesAsync();
        var t = await w.Truth.GetMemberTruthAsync(w.M("M1"), new DateTime(2026, 2, 20, 12, 0, 0));
        var jan = t.Statement.Periods.First();
        Assert.Equal(7_500m, jan.FineDue); // 50% ya 15,000 — si 5,000 ya zamani
    }

    [Fact]
    public async Task Settings_Change_After_First_Setup_Needs_Second_Leader()
    {
        var w = await Build();
        var r = await w.Setup.ProposeSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto { LateFineMode = "Percent", LateFinePercent = 50m }, w.U("Treasurer"), null);
        Assert.False(r.Applied);
        Assert.Null((await w.Db.TruthGroupSettings.AsNoTracking().SingleAsync()).LateFinePercent);  // bado haijabadilika
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Setup.DecideSettingsAsync(w.GroupId, r.ProposalId!.Value, true, null, w.U("Treasurer")));
        await w.Setup.DecideSettingsAsync(w.GroupId, r.ProposalId!.Value, true, null, w.U("Chair"));
        Assert.Equal(50m, (await w.Db.TruthGroupSettings.AsNoTracking().SingleAsync()).LateFinePercent);

        await Assert.ThrowsAsync<ArgumentException>(() => w.Setup.UpdateSettingsAsync(w.GroupId,
            new TruthSetupService.SettingsDto { LateFineMode = "Fixed", LateFinePercent = 50m }, w.U("Chair")));       // njia moja tu
        await Assert.ThrowsAsync<ArgumentException>(() => w.Setup.UpdateSettingsAsync(w.GroupId,
            new TruthSetupService.SettingsDto { LateFineMode = "Percent", LateFinePercent = 150m }, w.U("Chair")));

        // Pendekezo lisilobadilisha kitu, au lenye thamani batili, linakataliwa mara moja
        var cur = await w.Db.TruthGroupSettings.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => w.Setup.ProposeSettingsAsync(w.GroupId,
            new TruthSetupService.SettingsDto { MonthlyContribution = cur.MonthlyContribution, LateFineMode = "Percent", LateFinePercent = 50m, DueDateDay = cur.DueDateDay }, w.U("Treasurer"), null));
        await Assert.ThrowsAsync<ArgumentException>(() => w.Setup.ProposeSettingsAsync(w.GroupId,
            new TruthSetupService.SettingsDto { LateFinePercent = 150m }, w.U("Treasurer"), null));
        // SASISHO15: kufungua mikopo bila sera kamili kunakataliwa wakati wa KUPENDEKEZA (si kiongozi wa pili akiidhinisha)
        await Assert.ThrowsAsync<ArgumentException>(() => w.Setup.ProposeSettingsAsync(w.GroupId,
            new TruthSetupService.SettingsDto { LoansEnabled = true, LoanInterestPercent = 10m }, w.U("Treasurer"), null));
        Assert.False(await w.Db.TruthSettingsProposals.AnyAsync(p => p.Status == ProposalStatus.Pending));
    }

    [Fact]
    public void Approval_Resolver_Is_Fail_Closed()
    {
        Assert.Throws<ConflictException>(() => ApprovalPolicyResolver.Resolve(null));
        Assert.Throws<ConflictException>(() => ApprovalPolicyResolver.Resolve(new ApprovalPolicy { Mode = ApprovalMode.CustomApproval, CustomRoles = "Member" }));
        // Manaibu wanaruhusiwa: idadi haifungwi kwa idadi ya nafasi
        var (roles, n) = ApprovalPolicyResolver.Resolve(new ApprovalPolicy { Mode = ApprovalMode.CustomApproval, CustomRoles = "Treasurer", CustomRequiredApprovals = 2 });
        Assert.Single(roles);
        Assert.Equal(2, n);
    }

    // =====================================================================
    //  SASISHO13 — majaribio ya udanganyifu na idhini (B10)
    // =====================================================================
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);
    private static System.Text.Json.JsonElement J(object o) => System.Text.Json.JsonSerializer.SerializeToElement(o, WebJson);

    /// <summary>Kesi iliyoidhinishwa kwa kura: Secretary anafungua, Ben anapakia, Treasurer anahakiki, M1+M2+Chair wanaidhinisha. Net 70,000.</summary>
    private static async Task<BenefitCase> ApprovedCase(W w, string subject = "Mama wa Ben", string doc = "DC-1")
    {
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, subject), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, doc, "c.pdf", Pdf(subject + doc), w.U("Ben"));
        await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1"));
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M2"));
        var r = await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Chair"));
        Assert.Equal(BenefitCaseStatus.Approved, r.Case.Status);
        return r.Case;
    }

    private static void Balance(W w, string reference, DateTime at, decimal balance)
    {
        var row = Raw(w.GroupId, reference, at, 1_000m, false);
        row.Balance = balance;
        w.Db.MkobaRawTransactions.Add(row);
    }

    [Fact]
    public async Task Policy_Rejects_Unsafe_Payment_Roles_And_Contradictory_JoinFee_Rules()
    {
        var w = await Build();
        var bad = Gov(); bad.PaymentInitiatorRoles = new() { GroupRole.Member };
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, bad, w.U("Chair")));
        var both = Gov(); both.RequireJoinFeePaid = true; both.DeductJoinFee = true;
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, both, w.U("Chair")));
        var noRoles = Gov(); noRoles.PaymentConfirmerRoles = null;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, noRoles, w.U("Chair")));
        Assert.Contains("PaymentConfirmerRoles", ex.Message);
        var noAmountPeriod = Gov(); noAmountPeriod.MaxBenefitAmountPerPeriod = 100_000m;
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, noAmountPeriod, w.U("Chair")));

        // Matoleo: 1, kisha 2 — toleo la zamani halibadiliki
        var g1 = await w.Benefits.ProposeGovernanceAsync(w.GroupId, Gov(), w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g1.Id, true, null, w.U("Treasurer"));
        var g2 = await w.Benefits.ProposeGovernanceAsync(w.GroupId, new BenefitService.GovernanceDto { CooldownDays = 30 }, w.U("Secretary"));
        Assert.Equal(1, g1.Version);
        Assert.Equal(2, g2.Version);
        Assert.Equal(30, g2.CooldownDays);
        Assert.Equal(g1.PaymentInitiatorRoles, g2.PaymentInitiatorRoles); // imerithiwa
        Assert.Null((await w.Db.BenefitGovernances.AsNoTracking().FirstAsync(x => x.Id == g1.Id)).CooldownDays);
    }

    [Fact]
    public async Task Related_Party_Cannot_Verify_Or_Vote_And_Removing_Relation_Needs_Second_Leader()
    {
        var w = await Build();
        await SetupPolicy(w, d => { d.RequiredApprovals = 2; d.MinLeaderApprovals = 0; });
        var r1 = await w.Benefits.AddRelationAsync(w.GroupId, new BenefitService.RelationDto { MemberAId = w.M("Ben"), MemberBId = w.M("M1"), Type = MemberRelationType.Spouse }, w.U("Secretary"));
        var r2 = await w.Benefits.AddRelationAsync(w.GroupId, new BenefitService.RelationDto { MemberAId = w.M("Chair"), MemberBId = w.M("Ben"), Type = MemberRelationType.Sibling }, w.U("Secretary"));
        // SASISHO15: uhusiano mpya unasubiri kiongozi wa pili (maker-checker); aliyesajili hawezi kuuidhinisha, mhusika pia hawezi
        Assert.Equal(MemberRelationStatus.Pending, r1.Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideRelationAsync(w.GroupId, r1.Id, true, null, w.U("Secretary")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideRelationAsync(w.GroupId, r2.Id, true, null, w.U("Chair")));
        Assert.Equal(MemberRelationStatus.Active, (await w.Benefits.DecideRelationAsync(w.GroupId, r1.Id, true, null, w.U("Treasurer"))).Status);
        await w.Benefits.DecideRelationAsync(w.GroupId, r2.Id, true, null, w.U("Treasurer"));
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.AddRelationAsync(w.GroupId,
            new BenefitService.RelationDto { MemberAId = w.M("M1"), MemberBId = w.M("Ben"), Type = MemberRelationType.Spouse }, w.U("Chair"))); // tayari
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.AddRelationAsync(w.GroupId,
            new BenefitService.RelationDto { MemberAId = w.M("M2"), MemberBId = w.M("M3"), Type = MemberRelationType.Other }, w.U("M1"))); // si kiongozi

        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-9", "c.pdf", Pdf("rel"), w.U("Ben"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Chair"))); // ndugu
        c = await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.Voting, c.Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1")));    // mwenza
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Chair"))); // ndugu
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M2"));
        var r = await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M3"));
        Assert.Equal(BenefitCaseStatus.Approved, r.Case.Status);

        // Kuondoa uhusiano: aliyeomba hawezi kuamua, wala mhusika; kiongozi mwingine anaamua
        var rel = await w.Db.MemberRelations.FirstAsync(x => x.MemberBId == w.M("M1"));
        await w.Benefits.RequestEndRelationAsync(w.GroupId, rel.Id, "wameachana", w.U("Treasurer"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideEndRelationAsync(w.GroupId, rel.Id, true, null, w.U("Treasurer")));
        var rel2 = await w.Db.MemberRelations.FirstAsync(x => x.MemberAId == w.M("Chair"));
        await w.Benefits.RequestEndRelationAsync(w.GroupId, rel2.Id, "kosa", w.U("Treasurer"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideEndRelationAsync(w.GroupId, rel2.Id, true, null, w.U("Chair"))); // uhusiano wake
        var ended = await w.Benefits.DecideEndRelationAsync(w.GroupId, rel.Id, true, null, w.U("Secretary"));
        Assert.Equal(MemberRelationStatus.Ended, ended.Status);
    }

    [Fact]
    public async Task Cash_Check_From_Mkoba_Blocks_Initiation_And_Other_Recipient_Needs_Explanation()
    {
        var w = await Build();
        await SetupPolicy(w);
        var c = await ApprovedCase(w);
        var now = TruthLedgerService.NowEat();

        // Hakuna PDF yenye salio -> haiwezi kuanzishwa
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "x", w.U("Treasurer")));
        // Salio 50,000 < 70,000 -> kataa + jaribio linaandikwa
        Balance(w, "B1", now.AddDays(-2), 50_000m);
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "familia", w.U("Treasurer")));
        var risk = J(await w.Benefits.RiskFlagsAsync(w.GroupId, w.U("Chair")));
        var codes = risk.GetProperty("flags").EnumerateArray().Select(f => f.GetProperty("code").GetString()).ToList();
        Assert.Contains("CASH_INSUFFICIENT", codes);
        Assert.Contains("BLOCKED_CASH_INSUFFICIENT", codes);

        // Salio jipya 200,000 -> sawa. Ben hana namba -> inahitaji maelezo
        Balance(w, "B2", now.AddDays(-1), 200_000m);
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, null, w.U("Treasurer")));
        c = await w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "Pesa itapokelewa na mke wake", w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.PaymentInitiated, c.Status);
        Assert.Equal(200_000m, c.CashAvailableAtInitiation);

        // Utoaji ulienda kwa namba isiyo ya mnufaika: lazima ukiri + eleze
        w.Db.MkobaRawTransactions.Add(Raw(w.GroupId, "PAYX", now, 70_000m, true, "255799999999"));
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "PAYX", w.U("Secretary")));
        c = await w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "PAYX", w.U("Secretary"), default, true, "Mke wa Ben alipokea");
        Assert.Equal(BenefitCaseStatus.Paid, c.Status);
        risk = J(await w.Benefits.RiskFlagsAsync(w.GroupId, w.U("Chair")));
        Assert.Contains(risk.GetProperty("flags").EnumerateArray(), f => f.GetProperty("code").GetString() == "PAYMENT_TO_OTHER_PHONE");

        // Kesi iliyolipwa haiwezi kughairiwa / kulipwa tena
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.CancelCaseAsync(w.GroupId, c.Id, "futa", w.U("Chair")));
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "PAYX", w.U("Chair")));
    }

    [Fact]
    public async Task Approver_Cannot_Initiate_When_Policy_Says_So()
    {
        var w = await Build();
        await SetupPolicy(w, d => d.PaymentInitiatorRoles = new() { GroupRole.Treasurer, GroupRole.Chairperson });
        var c = await ApprovedCase(w); // Chair aliidhinisha kwa kura
        Balance(w, "B1", TruthLedgerService.NowEat().AddDays(-1), 1_000_000m);
        await w.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "x", w.U("Chair")));
        Assert.Contains("kura", ex.Message);
        c = await w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "x", w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.PaymentInitiated, c.Status);
        // Akiba: kesi iliyoanzishwa inapunguza pesa inayopatikana kwa kesi nyingine
        var cash = await w.Benefits.CashPositionAsync(w.GroupId, null, 0m);
        Assert.Equal(70_000m, cash.Reserved);
        Assert.Equal(930_000m, cash.Available);
    }

    [Fact]
    public async Task Cooldown_PeriodLimit_And_JoinFee_Eligibility_Block_Profit_Seeking()
    {
        var w = await Build();
        await SetupPolicy(w, d => { d.CooldownDays = 30; d.MaxBenefitAmountPerPeriod = 150_000m; d.MaxBenefitPeriodDays = 365; });
        await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Mama wa Ben"), w.U("Secretary"));
        var a = await w.Benefits.AssessAsync(w.GroupId, CaseDto(w, "Baba wa Ben"));
        Assert.False(a.Checks.Single(c => c.Code == "COOLDOWN").Passed);
        Assert.False(a.Checks.Single(c => c.Code == "MAX_AMOUNT_PERIOD").Passed);
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Baba wa Ben"), w.U("Secretary")));

        var w2 = await Build();
        await SetupPolicy(w2, d => { d.DeductJoinFee = false; d.RequireJoinFeePaid = true; });
        var b = await w2.Benefits.AssessAsync(w2.GroupId, CaseDto(w2));
        Assert.False(b.Checks.Single(c => c.Code == "JOIN_FEE_PAID").Passed); // Kianzio 50,000 hakijalipwa
    }

    [Fact]
    public async Task Same_Subject_Claimed_By_Two_Members_Is_Flagged_And_Historical_Path_Closed_After_Go_Live()
    {
        var w = await Build();
        await SetupPolicy(w);
        await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Juma Hamisi"), w.U("Secretary"));
        var other = CaseDto(w, "juma  hamisi"); other.BeneficiaryMemberId = w.M("M1");
        var a = await w.Benefits.AssessAsync(w.GroupId, other);
        Assert.Contains(a.Checks, c => c.Code == "SUBJECT_OTHER_MEMBER");
        await w.Benefits.OpenCaseAsync(w.GroupId, other, w.U("Secretary"));
        var risk = J(await w.Benefits.RiskFlagsAsync(w.GroupId, w.U("Treasurer")));
        Assert.Contains(risk.GetProperty("flags").EnumerateArray(), f => f.GetProperty("code").GetString() == "SUBJECT_MULTI_CLAIM");

        // Utoaji wa BAADA ya mfumo kuanza kusimamia mafao hauwezi kurekodiwa kama "kihistoria"
        w.Db.MkobaRawTransactions.Add(Raw(w.GroupId, "LATEPAY", TruthLedgerService.NowEat(), 120_000m, true));
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecordHistoricalCaseAsync(w.GroupId, new BenefitService.HistoricalCaseDto
        {
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent, BeneficiaryMemberId = w.M("M2"), SubjectName = "X",
            EventDate = TruthLedgerService.NowEat().Date.AddDays(-2), MkobaReference = "LATEPAY", PerMemberAmount = 20_000m, Headcount = 6
        }, w.U("Chair")));
    }

    [Fact]
    public async Task Audit_Hash_Chain_Detects_Edit_And_Delete()
    {
        var w = await Build();
        await SetupPolicy(w);
        var ok = await w.Benefits.VerifyAuditChainAsync(w.GroupId, w.U("Chair"));
        Assert.True(ok.Ok, ok.Message);
        Assert.True(ok.Sealed >= 4);
        // Pengo 12: ujumbe una kiolezo + hoja (frontend inatafsiri)
        Assert.Equal("Mnyororo uko salama: kumbukumbu {0} zimehakikiwa.", ok.Tpl);
        Assert.Equal(ok.Sealed.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(ok.TArgs!));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VerifyAuditChainAsync(w.GroupId, w.U("M1")));

        var victim = await w.Db.AuditEvents.OrderBy(a => a.ChainIndex).Skip(1).FirstAsync();
        victim.Reason = "imebadilishwa kwa siri";
        await w.Db.SaveChangesAsync();
        var bad = await AuditChain.VerifyAsync(w.Db, w.GroupId);
        Assert.False(bad.Ok);
        Assert.Equal(victim.ChainIndex, bad.BrokenAtIndex);
        Assert.StartsWith("Tukio {0} ({1}, {2}) limebadilishwa", bad.Tpl);

        var w2 = await Build();
        await SetupPolicy(w2);
        await AuditChain.SealAsync(w2.Db, w2.GroupId);
        w2.Db.AuditEvents.Remove(await w2.Db.AuditEvents.OrderBy(a => a.ChainIndex).Skip(1).FirstAsync());
        await w2.Db.SaveChangesAsync();
        Assert.False((await AuditChain.VerifyAsync(w2.Db, w2.GroupId)).Ok);
    }

    [Fact]
    public async Task Transparency_Ledger_For_All_Members_Hides_Phones_Evidence_And_Risk_Is_Leaders_Only()
    {
        var w = await Build();
        await SetupPolicy(w);
        await ApprovedCase(w);
        var t = J(await w.Benefits.TransparencyAsync(w.GroupId, w.U("M3")));
        var row = Assert.Single(t.GetProperty("rows").EnumerateArray());
        Assert.Equal(120_000m, row.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(50_000m, row.GetProperty("joinFeeDeducted").GetDecimal());
        Assert.Equal(70_000m, row.GetProperty("netAmount").GetDecimal());
        var text = t.ToString().ToLowerInvariant();
        Assert.DoesNotContain("phone", text);
        Assert.DoesNotContain("sha256", text);
        Assert.DoesNotContain("evidence", text);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.RiskFlagsAsync(w.GroupId, w.U("M3")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.RelationsAsync(w.GroupId, w.U("M3")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.ReconciliationAsync(w.GroupId, w.U("M3")));
    }

    [Fact]
    public async Task Tampered_Evidence_File_Is_Detected_On_Read_And_Duplicate_Upload_Attempt_Is_Logged()
    {
        var w = await Build();
        await SetupPolicy(w);
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-5", "c.pdf", Pdf("orig"), w.U("Ben"));
        var c2 = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Baba wa Ben"), w.U("Secretary"));
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.UploadEvidenceAsync(w.GroupId, c2.Id, EvidenceDocumentType.DeathCertificate, "DC-6", "d.pdf", Pdf("orig"), w.U("Ben")));
        Assert.Contains(await w.Db.AuditEvents.Where(a => a.Action == AuditAction.RiskBlocked).ToListAsync(), a => a.AfterJson!.Contains("DUPLICATE_FILE"));

        w.Storage.Files[e.StorageKey] = Pdf("mabadiliko");
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.ReadEvidenceAsync(w.GroupId, e.Id, w.U("Chair")));
        Assert.Contains(await w.Db.AuditEvents.Where(a => a.Action == AuditAction.RiskBlocked).ToListAsync(), a => a.AfterJson!.Contains("EVIDENCE_TAMPERED"));
    }

    [Fact]
    public async Task Zero_Net_Case_Posts_Ledger_With_Offsets_And_No_Cash()
    {
        var w = await Build();
        // Kiwango kidogo: 6 × 5,000 = 30,000 < Kianzio 50,000 -> yote yanakatwa, hakuna pesa inayotoka
        await SetupPolicy(w);
        var r = await w.Benefits.ProposeRateAsync(w.GroupId, new BenefitService.RateDto
        {
            Category = BenefitCategory.Sherehe, Relationship = BenefitRelationship.Child, PerMemberAmount = 5_000m,
            AcceptedEvidence = new() { EvidenceDocumentType.BirthCertificate }, EffectiveFrom = Jan1
        }, w.U("Treasurer"));
        await w.Benefits.DecideRateAsync(w.GroupId, r.Id, true, null, w.U("Chair"));
        var dto = CaseDto(w, "Mtoto wa Ben"); dto.Category = BenefitCategory.Sherehe; dto.Relationship = BenefitRelationship.Child;
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, dto, w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.BirthCertificate, "BC-1", "b.pdf", Pdf("bc"), w.U("Ben"));
        await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1"));
        await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M2"));
        var v = await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("Chair"));
        Assert.Equal(BenefitCaseStatus.Paid, v.Case.Status);
        var entries = await w.Db.BenefitLedgerEntries.Where(x => x.BenefitCaseId == c.Id).ToListAsync();
        Assert.Equal(30_000m, entries.Single(x => x.Kind == BenefitLedgerKind.Approved).Amount);
        Assert.Equal(30_000m, entries.Single(x => x.Kind == BenefitLedgerKind.JoinFeeOffset).Amount);
        Assert.Equal(0m, entries.Single(x => x.Kind == BenefitLedgerKind.CashPaid).Amount);
        var ben = await w.Truth.GetMemberTruthAsync(w.M("Ben"));
        Assert.Equal(20_000m, ben.Statement.JoinFeeOutstanding);
        Assert.True(J(await w.Benefits.ReconciliationAsync(w.GroupId, w.U("Chair"))).GetProperty("ok").GetBoolean());
    }

    // =====================================================================
    //  SASISHO14 — mapengo 12
    // =====================================================================
    private static async Task<BenefitCase> VotedCase(W w, string subject = "Mama wa Ben", string doc = "DC-1", string verifier = "Treasurer",
        params string[] voters)
    {
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, subject), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, doc, "c.pdf", Pdf(subject + doc), w.U("Ben"));
        await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U(verifier));
        BenefitService.VoteResult? r = null;
        foreach (var v in voters.Length > 0 ? voters : new[] { "M1", "M2", "Chair" })
            r = await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U(v));
        return r!.Case;
    }

    [Fact]
    public async Task Gap1_Approval_Without_Cash_Goes_To_AwaitingFunds_Until_Recheck()
    {
        var w = await Build();
        await SetupPolicy(w, d => d.CashCheckAtApproval = true);
        var c = await VotedCase(w);
        Assert.Equal(BenefitCaseStatus.AwaitingFunds, c.Status);
        // Haiwezi kuanzishwa malipo wakati inasubiri pesa
        await Assert.ThrowsAnyAsync<AppException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "x", w.U("Treasurer")));
        // Kagua upya bila salio -> kosa; mwanachama wa kawaida hawezi
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecheckFundsAsync(w.GroupId, c.Id, w.U("Treasurer")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.RecheckFundsAsync(w.GroupId, c.Id, w.U("M1")));
        Balance(w, "B1", TruthLedgerService.NowEat().AddDays(-1), 50_000m);
        await w.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecheckFundsAsync(w.GroupId, c.Id, w.U("Treasurer")));
        Assert.Contains("{0}", ex.Template);               // ujumbe unatafsirika (pengo 12)
        Assert.Contains("50,000", ex.Args);
        // Pesa ikiingia -> Approved; kesi nyingine zinazosubiri hazipunguzi (hazijahifadhiwa)
        Balance(w, "B2", TruthLedgerService.NowEat(), 300_000m);
        await w.Db.SaveChangesAsync();
        c = await w.Benefits.RecheckFundsAsync(w.GroupId, c.Id, w.U("Secretary"));
        Assert.Equal(BenefitCaseStatus.Approved, c.Status);
        Assert.True(await w.Db.AuditEvents.AnyAsync(a => a.Action == AuditAction.BenefitFundsRechecked));
        c = await w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "familia", w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.PaymentInitiated, c.Status);
        // Arifa zina kiolezo kinachotafsirika
        Assert.Contains(await w.Db.MemberNotifications.ToListAsync(), n => n.LocJson != null && n.LocJson.Contains("{0}"));
    }

    [Fact]
    public async Task Gap2_Relatives_Of_Case_Opener_Cannot_Vote_And_Policy_Chooses_Relation_Types()
    {
        var w = await Build();
        await SetupPolicy(w, d => d.ConflictRelationTypes = new() { MemberRelationType.Spouse });
        // M1 ni mke wa Katibu (aliyefungua kesi) -> mgongano; M2 ni ndugu (Sibling) wa Ben lakini sera haijumuishi Sibling
        var ra = await w.Benefits.AddRelationAsync(w.GroupId, new BenefitService.RelationDto { MemberAId = w.M("Secretary"), MemberBId = w.M("M1"), Type = MemberRelationType.Spouse }, w.U("Chair"));
        var rb = await w.Benefits.AddRelationAsync(w.GroupId, new BenefitService.RelationDto { MemberAId = w.M("Ben"), MemberBId = w.M("M2"), Type = MemberRelationType.Sibling }, w.U("Chair"));
        await w.Benefits.DecideRelationAsync(w.GroupId, ra.Id, true, null, w.U("Treasurer"));
        await w.Benefits.DecideRelationAsync(w.GroupId, rb.Id, true, null, w.U("Treasurer"));
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-9", "c.pdf", Pdf("g2"), w.U("Ben"));
        await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1")));
        var r = await w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M2")); // Sibling haizuiwi na sera hii
        Assert.Equal(1, r.Approvals);
        // Sera bila aina yoyote wakati mgongano umewashwa -> kosa
        var bad = Gov(); bad.ConflictRelationTypes = new();
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, bad, w.U("Chair")));
    }

    [Fact]
    public async Task Gap3_Approver_Verifier_And_Auditor_Roles_Come_From_Policy()
    {
        var w = await Build();
        await SetupPolicy(w, d =>
        {
            d.ApproverRoles = new() { GroupRole.Chairperson, GroupRole.Treasurer, GroupRole.Secretary };
            d.AllowMemberApprovers = false; d.RequiredApprovals = 2; d.MinLeaderApprovals = 2;
            d.EvidenceVerifierRoles = new() { GroupRole.Treasurer };
            d.AuditorMemberIds = new() { w.M("M3") };
        });
        // Mnufaika anafungua ombi lake mwenyewe; Mhakiki (Mtunza-hazina) hapigi kura (F-M2) → Mwenyekiti + Katibu = 2
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Ben"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.DeathCertificate, "DC-3", "c.pdf", Pdf("g3"), w.U("Ben"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Chair"))); // si Mhakiki
        await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.VoteAsync(w.GroupId, c.Id, true, null, w.U("M1"))); // wanachama hawapigi kura
        // Mkaguzi (M3) anasoma daftari/ulinganisho/hatari; M2 hawezi
        await w.Benefits.ReconciliationAsync(w.GroupId, w.U("M3"));
        await w.Benefits.RiskFlagsAsync(w.GroupId, w.U("M3"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.ReconciliationAsync(w.GroupId, w.U("M2")));
        // Mkaguzi hawezi kuandika (kuanzisha malipo / kuongeza uhusiano)
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.AddRelationAsync(w.GroupId,
            new BenefitService.RelationDto { MemberAId = w.M("M1"), MemberBId = w.M("M2"), Type = MemberRelationType.Spouse }, w.U("M3")));
        // Mhakiki lazima awe kiongozi
        var bad = Gov(); bad.EvidenceVerifierRoles = new() { GroupRole.Member };
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, bad, w.U("Chair")));
    }

    [Fact]
    public async Task Gap4_High_Value_Payment_Cannot_Be_Initiated_By_An_Approver()
    {
        var w = await Build();
        await SetupPolicy(w, d =>
        {
            d.ApproverCannotInitiatePayment = false; d.HighValueThreshold = 50_000m;
            d.PaymentInitiatorRoles = new() { GroupRole.Treasurer, GroupRole.Chairperson };
        });
        var c = await ApprovedCase(w); // net 70,000 ≥ 50,000; Chair alipiga kura
        Balance(w, "B1", TruthLedgerService.NowEat().AddDays(-1), 1_000_000m);
        await w.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "x", w.U("Chair")));
        Assert.Contains("50,000", ex.Message);
        c = await w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "x", w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.PaymentInitiated, c.Status);
    }

    [Fact]
    public async Task Gap5_Same_Subject_Is_Duplicate_Regardless_Of_Relationship_And_Can_Be_Blocked_Across_Members()
    {
        var w = await Build();
        await SetupPolicy(w, d => d.BlockSameSubjectAcrossMembers = true);
        await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w, "Mama wa Ben"), w.U("Secretary"));
        var again = CaseDto(w, "  mama WA ben "); again.Relationship = BenefitRelationship.Spouse; // uhusiano tofauti, mtu yule yule
        var a = await w.Benefits.AssessAsync(w.GroupId, again);
        Assert.False(a.Checks.Single(x => x.Code == "DUPLICATE").Passed);
        var other = CaseDto(w, "Mama wa Ben"); other.BeneficiaryMemberId = w.M("M1");
        var b = await w.Benefits.AssessAsync(w.GroupId, other);
        Assert.False(b.Checks.Single(x => x.Code == "SUBJECT_OTHER_MEMBER").Passed);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.OpenCaseAsync(w.GroupId, other, w.U("Secretary")));
        Assert.Contains(ex.Details, d => d.Tpl.Contains("mwanachama mwingine"));
    }

    [Fact]
    public async Task Gap6_Initiator_Sees_Member_Number_And_Approved_Phone()
    {
        var w = await Build();
        await SetupPolicy(w);
        w.Db.MemberPhones.Add(new MemberPhone { Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.M("Ben"), Phone = "255711111111", Status = TruthApprovalStatus.Approved, ValidFrom = Jan1 });
        await w.Db.SaveChangesAsync();
        var c = await ApprovedCase(w);
        var leader = J(await w.Benefits.CaseDetailAsync(w.GroupId, c.Id, w.U("Treasurer")));
        Assert.Equal("Ben", leader.GetProperty("beneficiaryMemberNumber").GetString());
        Assert.Equal("255711111111", leader.GetProperty("beneficiaryPhone").GetString());
        var member = J(await w.Benefits.CaseDetailAsync(w.GroupId, c.Id, w.U("M1")));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, member.GetProperty("beneficiaryPhone").ValueKind);
    }

    [Fact]
    public async Task Gap8_Historical_Cash_Needs_Cutoff_Receipt_Three_People_And_Posts_Ledger()
    {
        var w = await Build();
        await SetupPolicy(w);
        var dto = new BenefitService.HistoricalCaseDto
        {
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent, BeneficiaryMemberId = w.M("M2"), SubjectName = "Baba wa M2",
            EventDate = new DateTime(2026, 2, 1), PerMemberAmount = 20_000m, Headcount = 6, PaymentChannel = BenefitPaymentChannel.Cash,
            CashPaidAt = new DateTime(2026, 2, 3), CashRecipientName = "M2", CashRecipientPhone = "0712 345 678", ExceptionReason = "Kabla ya M-Koba, pesa ilikuwa kwa mweka-hazina"
        };
        // Hakuna tarehe ya mwisho ya kumbukumbu za mkononi -> haiwezekani
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Treasurer")));
        (await w.Db.TruthGroupSettings.FirstAsync()).ManualEntryCutoff = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        await w.Db.SaveChangesAsync();
        dto.CashPaidAt = new DateTime(2026, 4, 5);
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Treasurer"))); // baada ya cutoff
        dto.CashPaidAt = new DateTime(2026, 2, 3);
        dto.ExceptionReason = " ";
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Treasurer")));
        dto.ExceptionReason = "Kabla ya M-Koba, pesa ilikuwa kwa mweka-hazina";
        var c = await w.Benefits.RecordHistoricalCaseAsync(w.GroupId, dto, w.U("Treasurer"));
        Assert.Equal(BenefitCaseStatus.HistoricalPendingConfirmation, c.Status);
        Assert.Equal("255712345678", c.CashRecipientPhone);
        // Bila risiti -> haithibitishwi
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("Chair")));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.PaymentReceipt, "RISITI-7", "r.pdf", Pdf("risiti"), w.U("Treasurer"));
        await w.Benefits.VerifyEvidenceAsync(w.GroupId, e.Id, true, null, w.U("Secretary"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("Treasurer"))); // aliyerekodi
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("Secretary"))); // mhakiki
        var d1 = J(await w.Benefits.CaseDetailAsync(w.GroupId, c.Id, w.U("Chair")));
        Assert.True(d1.GetProperty("canConfirmHistorical").GetBoolean());
        c = await w.Benefits.DecideHistoricalAsync(w.GroupId, c.Id, true, null, w.U("Chair"));
        Assert.Equal(BenefitCaseStatus.Paid, c.Status);
        var entries = await w.Db.BenefitLedgerEntries.Where(x => x.BenefitCaseId == c.Id).ToListAsync();
        Assert.Equal(120_000m, entries.Single(x => x.Kind == BenefitLedgerKind.CashPaid).Amount);
        Assert.True(J(await w.Benefits.ReconciliationAsync(w.GroupId, w.U("Chair"))).GetProperty("ok").GetBoolean());
        // Haiingii kwenye "utoaji wa M-Koba usioelezwa" na haitumii reference ya M-Koba
        Assert.Null(c.MkobaReference);
        // Snapshot lock: baada ya kuthibitishwa, hakuna ushahidi mpya
        Assert.False(J(await w.Benefits.CaseDetailAsync(w.GroupId, c.Id, w.U("Chair"))).GetProperty("canUpload").GetBoolean());
        await Assert.ThrowsAsync<ConflictException>(() => w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.PaymentReceipt, "RISITI-8", "r2.pdf", Pdf("risiti2"), w.U("Treasurer")));
    }

    [Fact]
    public async Task Gap9_New_Policy_Cannot_Start_In_The_Past_And_Future_Policy_Applies_From_Its_Date()
    {
        var w = await Build();
        await SetupPolicy(w);
        var today = TruthLedgerService.NowEat().Date;
        var past = Gov(); past.EffectiveFrom = today.AddDays(-10); past.RequiredApprovals = 4;
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, past, w.U("Chair")));
        var future = Gov(); future.EffectiveFrom = today.AddDays(10); future.RequiredApprovals = 4;
        var g = await w.Benefits.ProposeGovernanceAsync(w.GroupId, future, w.U("Chair"));
        await w.Benefits.DecideGovernanceAsync(w.GroupId, g.Id, true, null, w.U("Treasurer"));
        Assert.Equal(3, (await w.Benefits.GetActiveGovernanceAsync(w.GroupId))!.RequiredApprovals);
        Assert.Equal(4, (await w.Benefits.GovernanceForDateAsync(w.GroupId, today.AddDays(11)))!.RequiredApprovals);
        // Tukio la jana linatumia sera ya zamani (toleo 1)
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        Assert.Equal(1, c.PolicyVersion);
    }

    [Fact]
    public async Task Gap10_Rate_EffectiveTo_Ends_The_Rate_And_Headcount_Date_Follows_Policy()
    {
        var w = await Build();
        await SetupPolicy(w, d => d.HeadcountAsOf = HeadcountDateBasis.ClaimDate);
        var today = TruthLedgerService.NowEat().Date;
        var r = await w.Benefits.ProposeRateAsync(w.GroupId, new BenefitService.RateDto
        {
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Spouse, PerMemberAmount = 30_000m,
            AcceptedEvidence = new() { EvidenceDocumentType.DeathCertificate }, EffectiveFrom = Jan1, EffectiveTo = today.AddDays(-5)
        }, w.U("Treasurer"));
        await w.Benefits.DecideRateAsync(w.GroupId, r.Id, true, null, w.U("Chair"));
        var dto = CaseDto(w, "Mke wa Ben"); dto.Relationship = BenefitRelationship.Spouse; dto.EventDate = today.AddDays(-2);
        var a = await w.Benefits.AssessAsync(w.GroupId, dto);
        Assert.False(a.Checks.Single(x => x.Code == "RATE").Passed);
        dto.EventDate = today.AddDays(-6);
        a = await w.Benefits.AssessAsync(w.GroupId, dto);
        Assert.True(a.Checks.Single(x => x.Code == "RATE").Passed);
        Assert.Equal(30_000m, a.PerMemberAmount);
        Assert.Contains(today.ToString("dd/MM/yyyy"), a.Checks.Single(x => x.Code == "HEADCOUNT").Message);
        // Tarehe ya mwisho kabla ya kuanza -> kosa
        await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeRateAsync(w.GroupId, new BenefitService.RateDto
        {
            Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Child, PerMemberAmount = 10_000m,
            AcceptedEvidence = new() { EvidenceDocumentType.DeathCertificate }, EffectiveFrom = today, EffectiveTo = today.AddDays(-1)
        }, w.U("Treasurer")));
    }

    [Fact]
    public async Task Gap11_Ledger_Postings_Are_In_The_Audit_Hash_Chain()
    {
        var w = await Build();
        await SetupPolicy(w);
        var c = await ApprovedCase(w);
        Balance(w, "B1", TruthLedgerService.NowEat().AddDays(-1), 1_000_000m);
        await w.Db.SaveChangesAsync();
        c = await w.Benefits.InitiatePaymentAsync(w.GroupId, c.Id, "familia", w.U("Treasurer"));
        w.Db.MkobaRawTransactions.Add(Raw(w.GroupId, "PAY1", TruthLedgerService.NowEat(), c.NetAmount, true));
        await w.Db.SaveChangesAsync();
        await w.Benefits.ConfirmPaymentAsync(w.GroupId, c.Id, "PAY1", w.U("Secretary"), default, true, "mpokeaji ni familia");
        var posted = await w.Db.AuditEvents.Where(a => a.Action == AuditAction.BenefitLedgerPosted).ToListAsync();
        Assert.NotEmpty(posted);
        Assert.Contains(posted, a => a.AfterJson != null && a.AfterJson.Contains("CashPaid"));
    }

    [Fact]
    public async Task Gap12_Validation_Messages_Carry_Template_Args_And_Details()
    {
        var w = await Build();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.Benefits.ProposeGovernanceAsync(w.GroupId, new BenefitService.GovernanceDto(), w.U("Chair")));
        Assert.Contains("{0}", ex.Template);
        Assert.NotEmpty(ex.Details);
        Assert.All(ex.Details, d => Assert.False(string.IsNullOrWhiteSpace(d.Tpl)));
    }

    // =====================================================================
    // SASISHO15
    // =====================================================================
    [Fact]
    public async Task S15_Verifying_A_Document_Type_The_Rate_Does_Not_Accept_Returns_A_Clear_Warning()
    {
        var w = await Build();
        await SetupPolicy(w);
        var c = await w.Benefits.OpenCaseAsync(w.GroupId, CaseDto(w), w.U("Secretary"));
        var e = await w.Benefits.UploadEvidenceAsync(w.GroupId, c.Id, EvidenceDocumentType.MarriageCertificate, "MC-1", "m.pdf", Pdf("mc"), w.U("Ben"));
        var (case1, warning) = await w.Benefits.VerifyEvidenceWithWarningAsync(w.GroupId, e.Id, true, null, w.U("Treasurer"));
        Assert.NotNull(warning);
        Assert.Contains(BenefitService.EvidenceName(EvidenceDocumentType.DeathCertificate), warning!.Value.Text);
        Assert.Equal(BenefitCaseStatus.AwaitingVerification, case1.Status); // haiendi kwenye kura
    }

    [Fact]
    public async Task S15_Governance_View_Warns_When_Leaders_Are_Too_Few_For_Independent_Votes()
    {
        var w = await Build();
        await SetupPolicy(w, d => { d.MinLeaderApprovals = 2; });
        var view = await w.Benefits.PolicyViewAsync(w.GroupId, w.U("Chair"));
        var json = System.Text.Json.JsonSerializer.Serialize(view);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var warnings = doc.RootElement.GetProperty("active").GetProperty("capacityWarnings");
        Assert.True(warnings.GetArrayLength() >= 1);
        Assert.Contains("Viongozi wanaoweza kupiga kura ni 3", warnings[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task S15_Truth_Reads_Welfare_Offsets_From_The_Immutable_Benefit_Ledger()
    {
        var w = await Build();
        var paid = new BenefitCase
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, CaseNumber = "TST-MF-2026-099", Category = BenefitCategory.Msiba, Relationship = BenefitRelationship.Parent,
            BeneficiaryGroupMemberId = w.M("Ben"), SubjectName = "x", EventDate = Jan1.AddMonths(4), Status = BenefitCaseStatus.Paid,
            GrossAmount = 100_000m, JoinFeeDeducted = 20_000m, ArrearsDeducted = 0m, LoanArrearsDeducted = 0m, NetAmount = 80_000m,
            PaidAt = Jan1.AddMonths(4), CreatedByUserId = w.U("Secretary")
        };
        w.Db.BenefitCases.Add(paid);
        foreach (var (k, amt) in new[] { (BenefitLedgerKind.Approved, 100_000m), (BenefitLedgerKind.JoinFeeOffset, 20_000m), (BenefitLedgerKind.CashPaid, 80_000m) })
            w.Db.BenefitLedgerEntries.Add(new BenefitLedgerEntry { Id = Guid.NewGuid(), GroupId = w.GroupId, BenefitCaseId = paid.Id, BeneficiaryGroupMemberId = w.M("Ben"), Kind = k, Amount = amt, Reference = "r" });
        await w.Db.SaveChangesAsync();
        Assert.Equal(20_000m, (await w.Truth.GetMemberTruthAsync(w.M("Ben"))).Statement.JoinFeePaid);

        // Mtu akihariri safu ya kesi moja kwa moja kwenye DB, ukweli haubadiliki — daftari ndilo chanzo
        paid.JoinFeeDeducted = 50_000m;
        await w.Db.SaveChangesAsync();
        Assert.Equal(20_000m, (await w.Truth.GetMemberTruthAsync(w.M("Ben"))).Statement.JoinFeePaid);
    }
}

/// <summary>
/// InMemory haikatai DateTime yenye Kind=Unspecified, lakini PostgreSQL (timestamptz, Npgsql 6+) inakataa.
/// Kizuizi hiki kinaiga tabia ya Npgsql kwa jedwali za mafao/sera ili hitilafu ionekane kwenye majaribio, si kwa mtumiaji.
/// </summary>
internal sealed class UtcOnlyInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private static readonly HashSet<Type> Guarded = new()
    {
        typeof(BenefitGovernance), typeof(BenefitRate), typeof(BenefitCase), typeof(BenefitEvidence), typeof(GovernanceVote),
        typeof(MemberStatusChangeRequest), typeof(MkobaWithdrawalExplanation), typeof(MemberNotification), typeof(MemberRelation), typeof(BenefitLedgerEntry)
    };

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> SavingChanges(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void Check(DbContext? ctx)
    {
        if (ctx is null) return;
        foreach (var e in ctx.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified && Guarded.Contains(e.Entity.GetType())))
            foreach (var p in e.Properties)
                if (p.CurrentValue is DateTime d && d.Kind != DateTimeKind.Utc)
                    throw new InvalidOperationException($"{e.Entity.GetType().Name}.{p.Metadata.Name} = {d:O} (Kind={d.Kind}) — PostgreSQL timestamptz inahitaji Kind=Utc.");
    }

}
