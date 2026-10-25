using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Loans;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO15 (F-H1): mkopo hauwezi kutolewa na mtu mmoja, kwa riba ya kubuni, zaidi ya pesa iliyopo, kwa mkopaji mwenyewe,
/// wala bila utoaji halisi wa M-Koba unaolingana.
/// </summary>
public class LoanApplicationTests
{
    internal sealed class W
    {
        public required ApplicationDbContext Db;
        public required LoanApplicationService Loans;
        public required BenefitService Benefits;
        public required TruthLedgerService Truth;
        public Guid GroupId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId)> P = new();
        public Guid U(string n) => P[n].UserId;
        public Guid M(string n) => P[n].MemberId;
    }

    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static async Task<W> Build(decimal balance = 1_000_000m, int approvals = 2, bool fromJanuary = false)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);
        var truth = new TruthLedgerService(db);
        var notify = new NotificationService(db);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Benefits:MaxEvidenceBytes"] = "1048576" }).Build();
        var benefits = new BenefitService(db, truth, notify, new BenefitsAndGovernanceTests.MemoryStorage(), config, new ChamaLink.Infrastructure.Services.Events.EventWalletService(db, truth));
        var w = new W { Db = db, Truth = truth, Benefits = benefits, Loans = new LoanApplicationService(db, truth, notify, benefits), GroupId = Guid.NewGuid() };
        var now = TruthLedgerService.NowEat();
        // Michango ianze mwezi huu (hakuna deni bado) isipokuwa test inahitaji malimbikizo
        var start = fromJanuary ? Jan1 : new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Groups.Add(new Group { Id = w.GroupId, Name = "Kikundi cha mikopo", ShortCode = "TST" });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 10_000m, LateFine = 5_000m, JoiningFee = 0m, DueDateDay = 5, GracePeriodDays = 0,
            ContributionStartDate = start, FoundingMembersCutoff = start.AddMonths(3), FineOnPartialPayment = false, AutoDeductSavings = true,
            LoansEnabled = true, LoanInterestPercent = 10m, LoanRepaymentMonths = 3, LoanMaxAmount = 500_000m, LoanAllowWithArrears = false,
            LoanAllowMultipleActive = false, LoanMinCashReserve = 100_000m, LoanRequiredApprovals = approvals, LoanApprovalValidDays = 7, CashStatementMaxAgeDays = 120
        });
        var i = 0;
        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer), ("Secretary", GroupRole.Secretary),
                     ("M1", GroupRole.Member), ("M2", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = "" });
            db.GroupMembers.Add(new GroupMember { Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = name, Role = role, Status = MemberStatus.Active, JoinedAt = start });
            db.MemberPhones.Add(new MemberPhone { Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = mid, Phone = $"25571100000{++i}", Status = TruthApprovalStatus.Approved });
            w.P[name] = (mid, uid);
        }
        db.MkobaRawTransactions.Add(Raw(w.GroupId, "BAL1", now.AddDays(-1), 1_000m, false, "255799999999", balance));
        await db.SaveChangesAsync();
        return w;
    }

    private static MkobaRawTransaction Raw(Guid groupId, string reference, DateTime at, decimal amount, bool withdrawal, string phone, decimal? balance = null) => new()
    {
        Id = Guid.NewGuid(), GroupId = groupId, Reference = reference, Timestamp = DateTime.SpecifyKind(at, DateTimeKind.Utc), Amount = amount,
        IsWithdrawal = withdrawal, Phone = phone, PayerName = withdrawal ? "WITHDRAW" : "PAYER", Description = withdrawal ? "Withdraw" : "Deposit",
        StatementFileHash = "h", Balance = balance
    };

    private static LoanApplicationService.ApplyDto Req(decimal amount, Guid? borrower = null) => new() { Amount = amount, Purpose = "Biashara", BorrowerMemberId = borrower };

    [Fact]
    public async Task Full_Flow_Needs_Independent_Votes_Treasurer_Initiation_And_Matching_Mkoba_Withdrawal_By_Another_Leader()
    {
        var w = await Build();
        var app = await w.Loans.ApplyAsync(w.GroupId, Req(100_000m), w.U("M1"));
        Assert.Equal("TST-MK-" + TruthLedgerService.NowEat().Year + "-001", app.ApplicationNumber);
        // Riba na miezi vinatoka kwenye sera — si kwa aliyeomba
        Assert.Equal(10_000m, app.InterestAmount);
        Assert.Equal(3, app.RepaymentMonths);
        Assert.Equal(LoanApplicationStatus.Voting, app.Status);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("M2"))); // si kiongozi
        await Assert.ThrowsAsync<ValidationException>(() => w.Loans.VoteAsync(w.GroupId, app.Id, false, null, w.U("Chair"))); // kukataa bila sababu
        await w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Chair"));
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Chair")));   // kura mara mbili
        app = await w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Secretary"));
        Assert.Equal(LoanApplicationStatus.Approved, app.Status);
        Assert.NotNull(app.ApprovalExpiresAt);
        Assert.Empty(await w.Db.Loans.ToListAsync()); // idhini SI pesa

        // Pesa iliyoidhinishwa inashikiliwa (mafao/mikopo mingine haiwezi kuitumia)
        var cash = await w.Benefits.CashPositionAsync(w.GroupId, null, 0m);
        Assert.Equal(100_000m, cash.Reserved);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.InitiateDisbursementAsync(w.GroupId, app.Id, w.U("Chair"))); // kuna Mtunza-hazina huru
        app = await w.Loans.InitiateDisbursementAsync(w.GroupId, app.Id, w.U("Treasurer"));
        Assert.Equal(LoanApplicationStatus.DisbursementInitiated, app.Status);

        var now = TruthLedgerService.NowEat();
        w.Db.MkobaRawTransactions.AddRange(
            Raw(w.GroupId, "WDWRONG", now, 90_000m, true, "255711000004"),
            Raw(w.GroupId, "DEP1", now, 100_000m, false, "255766666666"),
            Raw(w.GroupId, "WDOTHER", now, 100_000m, true, "255788888888"),
            Raw(w.GroupId, "WDOK", now, 100_000m, true, "255711000004"));
        await w.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "WDOK", false, null, w.U("Treasurer"))); // aliyeanzisha
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "WDOK", false, null, w.U("M1")));        // si kiongozi
        await Assert.ThrowsAsync<NotFoundException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "NOPE", false, null, w.U("Secretary")));
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "WDWRONG", false, null, w.U("Secretary"))); // kiasi
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "DEP1", false, null, w.U("Secretary")));    // si utoaji
        var other = await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "WDOTHER", false, null, w.U("Secretary")));
        Assert.Contains("255788888888", other.Message);

        var view = await w.Loans.GetAsync(w.GroupId, app.Id, w.U("Secretary"));
        var candidates = (System.Collections.IEnumerable)view.GetType().GetProperty("candidates")!.GetValue(view)!;
        Assert.Equal(2, candidates.Cast<object>().Count()); // WDOTHER + WDOK (kiasi sawa, baada ya idhini, havijatumika)

        app = await w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "wdok", false, null, w.U("Secretary"));
        Assert.Equal(LoanApplicationStatus.Disbursed, app.Status);
        var loan = await w.Db.Loans.SingleAsync();
        Assert.Equal("WDOK", loan.DisbursementReference);
        Assert.Equal(app.Id, loan.LoanApplicationId);
        Assert.Equal(100_000m, loan.PrincipalAmount);
        Assert.Equal(10_000m, loan.InterestAmount);
        Assert.Equal(loan.DisbursedAt.AddMonths(2), loan.DueDate); // miezi 3 ya marejesho (ikijumuisha mwezi wa utoaji)

        // Injini ya Ukweli inauona mkopo
        var t = await w.Truth.GetMemberTruthAsync(w.M("M1"));
        Assert.Equal(110_000m, t.Statement.LoanOutstanding);
        // Rejea ile ile haiwezi kutumika tena (mafao wala mkopo mwingine)
        Assert.NotNull(await ChamaLink.Infrastructure.Services.Common.WithdrawalRegistry.UsedByOtherAsync(w.Db, w.GroupId, "WDOK", Guid.NewGuid(), default));
        // Mkopo mwingine hauruhusiwi wakati huu unadaiwa (sera LoanAllowMultipleActive=false)
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ApplyAsync(w.GroupId, Req(50_000m), w.U("M1")));
    }

    [Fact]
    public async Task Treasurer_Borrower_Cannot_Vote_Or_Initiate_And_Chair_Is_Fallback_Initiator()
    {
        var w = await Build();
        var app = await w.Loans.ApplyAsync(w.GroupId, Req(100_000m), w.U("Treasurer"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Treasurer")));
        await w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Chair"));
        app = await w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Secretary"));
        Assert.Equal(LoanApplicationStatus.Approved, app.Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.InitiateDisbursementAsync(w.GroupId, app.Id, w.U("Treasurer")));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.InitiateDisbursementAsync(w.GroupId, app.Id, w.U("Secretary")));
        app = await w.Loans.InitiateDisbursementAsync(w.GroupId, app.Id, w.U("Chair"));
        w.Db.MkobaRawTransactions.Add(Raw(w.GroupId, "WDT", TruthLedgerService.NowEat(), 100_000m, true, "255711000002"));
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "WDT", false, null, w.U("Treasurer"))); // mkopaji
        app = await w.Loans.ConfirmDisbursementAsync(w.GroupId, app.Id, "WDT", false, null, w.U("Secretary"));
        Assert.Equal(LoanApplicationStatus.Disbursed, app.Status);
    }

    [Fact]
    public async Task Application_Is_Refused_When_Independent_Voters_Are_Fewer_Than_Policy()
    {
        var w = await Build(approvals: 3);
        // Mkopaji ni Mtunza-hazina → wanabaki Mwenyekiti + Katibu (2) < 3: likatae mapema lisikwame
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ApplyAsync(w.GroupId, Req(100_000m), w.U("Treasurer")));
        Assert.Contains("viongozi huru", ex.Message);
        // Ndugu wa mkopaji aliyesajiliwa hapigi kura
        var r = await w.Benefits.AddRelationAsync(w.GroupId, new BenefitService.RelationDto { MemberAId = w.M("Chair"), MemberBId = w.M("M1"), Type = MemberRelationType.Sibling }, w.U("Secretary"));
        await w.Benefits.DecideRelationAsync(w.GroupId, r.Id, true, null, w.U("Treasurer"));
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ApplyAsync(w.GroupId, Req(100_000m), w.U("M1")));
    }

    [Fact]
    public async Task Relative_Cannot_Vote_And_Leader_Requester_Does_Not_Vote()
    {
        var w = await Build(approvals: 1);
        var r = await w.Benefits.AddRelationAsync(w.GroupId, new BenefitService.RelationDto { MemberAId = w.M("Chair"), MemberBId = w.M("M1"), Type = MemberRelationType.Sibling }, w.U("Secretary"));
        await w.Benefits.DecideRelationAsync(w.GroupId, r.Id, true, null, w.U("Treasurer"));
        var app = await w.Loans.ApplyAsync(w.GroupId, Req(100_000m, w.M("M1")), w.U("Secretary")); // kiongozi kwa niaba
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Chair")));     // ndugu
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Secretary"))); // aliyewasilisha
        app = await w.Loans.VoteAsync(w.GroupId, app.Id, true, null, w.U("Treasurer"));
        Assert.Equal(LoanApplicationStatus.Approved, app.Status);
        // Mwanachama wa kawaida hawezi kuomba kwa niaba ya mwingine
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.ApplyAsync(w.GroupId, Req(10_000m, w.M("M1")), w.U("M2")));
    }

    [Fact]
    public async Task Cash_Reserve_And_Policy_Limits_Are_Enforced()
    {
        var w = await Build(balance: 700_000m);
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ApplyAsync(w.GroupId, Req(600_000m), w.U("M1"))); // > LoanMaxAmount 500k
        var a1 = await w.Loans.ApplyAsync(w.GroupId, Req(400_000m), w.U("M1"));
        await w.Loans.VoteAsync(w.GroupId, a1.Id, true, null, w.U("Chair"));
        await w.Loans.VoteAsync(w.GroupId, a1.Id, true, null, w.U("Secretary"));
        // 700k − 400k (imeshikiliwa) = 300k; 300k − 250k = 50k < akiba ya lazima 100k
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ApplyAsync(w.GroupId, Req(250_000m), w.U("M2")));
        Assert.Contains("100,000", ex.Message);
        var preview = await w.Loans.PreviewAsync(w.GroupId, Req(200_000m), w.U("M2"));
        Assert.True(preview.Eligible);
        Assert.Equal(200_000m, preview.Amount);
    }

    [Fact]
    public async Task Member_With_Arrears_Cannot_Borrow_When_Policy_Forbids()
    {
        var w = await Build(fromJanuary: true);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.Loans.ApplyAsync(w.GroupId, Req(50_000m), w.U("M1")));
        Assert.Contains("deni", ex.Message);
    }

    [Fact]
    public async Task Rejections_Close_The_Application_And_Stale_Approval_Expires()
    {
        var w = await Build();
        var a = await w.Loans.ApplyAsync(w.GroupId, Req(100_000m), w.U("M1"));
        await w.Loans.VoteAsync(w.GroupId, a.Id, false, "Hana mpango wa biashara", w.U("Chair"));
        a = await w.Loans.VoteAsync(w.GroupId, a.Id, false, "Kiasi kikubwa", w.U("Treasurer"));
        Assert.Equal(LoanApplicationStatus.Rejected, a.Status); // 3 − 2 = 1 < 2

        var b = await w.Loans.ApplyAsync(w.GroupId, Req(100_000m), w.U("M2"));
        await w.Loans.VoteAsync(w.GroupId, b.Id, true, null, w.U("Chair"));
        await w.Loans.VoteAsync(w.GroupId, b.Id, true, null, w.U("Secretary"));
        b.ApprovalExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => w.Loans.InitiateDisbursementAsync(w.GroupId, b.Id, w.U("Treasurer")));
        Assert.Equal(LoanApplicationStatus.Expired, (await w.Db.LoanApplications.AsNoTracking().SingleAsync(x => x.Id == b.Id)).Status);
        Assert.Equal(0m, (await w.Benefits.CashPositionAsync(w.GroupId, null, 0m)).Reserved);
    }

    [Fact]
    public async Task Writing_Off_A_Loan_Needs_A_Second_Leader()
    {
        var w = await Build();
        var loan = new Loan
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.M("M1"), UserId = w.U("M1"), PrincipalAmount = 50_000m,
            DisbursedAt = DateTime.UtcNow, DueDate = DateTime.UtcNow.AddMonths(2), IssuedByGroupMemberId = w.M("Treasurer")
        };
        w.Db.Loans.Add(loan);
        await w.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.RequestDefaultAsync(w.GroupId, loan.Id, "x", w.U("M2")));
        await w.Loans.RequestDefaultAsync(w.GroupId, loan.Id, "Amehama bila kulipa", w.U("Chair"));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Loans.DecideDefaultAsync(w.GroupId, loan.Id, true, null, w.U("Chair")));
        var l = await w.Loans.DecideDefaultAsync(w.GroupId, loan.Id, true, null, w.U("Secretary"));
        Assert.Equal(LoanStatus.Defaulted, l.Status);
    }
}
