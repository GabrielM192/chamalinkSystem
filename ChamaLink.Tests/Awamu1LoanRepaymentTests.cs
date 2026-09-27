using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// JARIBIO LA (2026-09-19): MAREJESHO YA MIKOPO KATIKA IMPORT.
///
/// BUG ILIYOREKEBISHWA (chanzo kikuu cha "taarifa zisizo za kweli"):
///   Pesa ya rejesho la mkopo iliyotumwa kupitia M-Koba/Excel ilikuwa
///   inapelekwa KIANZIO/akiba — mkopo unabaki "Active" milele na akiba
///   inavimbishwa. Sasa import inakata KWANZA rejesho linalostahili
///   mwezi huo (installment ya LoanSchedule rahisi), KABLA ya KIANZIO.
///
/// Majaribio haya yanahakikisha:
///   1. LoanSchedule rahisi: installment = TotalPayable / miezi
///   2. Mwezi wa mwisho: salio lote (siyo installment ya kawaida)
///   3. Nje ya kipindi: 0
///   4. ApplyRepaymentFromImportAsync: FIFO (mkopo mzee kwanza)
///   5. Kikomo: haizidi installment ya mwezi
///   6. Mkopo ukikamilika: Status -> Repaid
///   7. LedgerEntry ya LoanRepayment inaandikwa
///   8. MemberStatement: Lengo = mchango + rejesho; Hali sahihi
/// </summary>
public class Awamu1LoanRepaymentTests
{
    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (Group Group, GroupMember Member, User User) SeedGroup(
        ApplicationDbContext db, decimal monthlyContribution = 10_000m)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Ukonga Mkoba Wing",
            Code = "UKW",
            Type = GroupType.MonthlySavings,
            Settings = new GroupSettings
            {
                Id = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                Contribution = new ContributionSettings
                {
                    MonthlyContribution = monthlyContribution,
                    LateFine = 5_000m,
                    DueDateDay = 5
                }
            }
        };
        group.Settings!.GroupId = group.Id;

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Frank Mushi",
            Email = $"frank{Guid.NewGuid():N}@test.com",
            PhoneNumber = "0700000001",
            PasswordHash = "hash"
        };

        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            MemberNumber = "UKW-001",
            JoinedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = MemberStatus.Active
        };

        db.Groups.Add(group);
        db.Users.Add(user);
        db.GroupMembers.Add(member);
        db.SaveChanges();
        return (group, member, user);
    }

    private static Loan SeedLoan(
        ApplicationDbContext db, Group group, GroupMember member,
        decimal principal, decimal rate, DateTime disbursed, DateTime due,
        LoanStatus status = LoanStatus.Active, decimal amountRepaid = 0m)
    {
        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            GroupMemberId = member.Id,
            UserId = member.UserId,
            PrincipalAmount = principal,
            InterestRate = rate,
            // Flat interest: riba huhesabiwa mara moja kwenye InterestAmount
            // (TotalPayable = PrincipalAmount + InterestAmount).
            InterestAmount = Math.Round(principal * rate / 100m, 0, MidpointRounding.AwayFromZero),
            DisbursedAt = disbursed,
            DueDate = due,
            Status = status,
            AmountRepaid = amountRepaid
        };
        db.Loans.Add(loan);
        db.SaveChanges();
        return loan;
    }

    // ── 1. LoanSchedule rahisi: installment = TotalPayable / miezi ──

    [Fact]
    public void Installment_IsUniform_OverLoanPeriod()
    {
        var loan = new Loan
        {
            PrincipalAmount = 200_000m,
            InterestAmount = 20_000m,   // flat 10% -> TotalPayable = 220,000
            DisbursedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = LoanStatus.Active
        };
        // Miezi: Jan..Des = 12. Installment = 220,000 / 12 = 18,333 (rounded).
        decimal installment = LoanService.GetExpectedRepaymentForMonth(loan, 2026, 3);
        Assert.Equal(18_333m, installment);
    }

    [Fact]
    public void FinalMonth_TakesRemainingBalance_NotRegularInstallment()
    {
        var loan = new Loan
        {
            PrincipalAmount = 200_000m,
            InterestAmount = 20_000m,
            DisbursedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = LoanStatus.Active,
            // 11 installments zimeshalipwa: 11 * 18,333 = 201,663
            AmountRepaid = 201_663m
        };
        // Mwezi wa mwisho (Des): salio = 220,000 - 201,663 = 18,337
        decimal final = LoanService.GetExpectedRepaymentForMonth(loan, 2026, 12);
        Assert.Equal(18_337m, final);
    }

    [Fact]
    public void OutsideLoanWindow_ReturnsZero()
    {
        var loan = new Loan
        {
            PrincipalAmount = 200_000m,
            InterestAmount = 20_000m,
            DisbursedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = LoanStatus.Active
        };
        // Kabla ya kutolewa (Jan, Feb) -> 0
        Assert.Equal(0m, LoanService.GetExpectedRepaymentForMonth(loan, 2026, 1));
        Assert.Equal(0m, LoanService.GetExpectedRepaymentForMonth(loan, 2026, 2));
        // Baada ya DueDate (mwaka ujao) -> 0
        Assert.Equal(0m, LoanService.GetExpectedRepaymentForMonth(loan, 2027, 1));
    }

    [Fact]
    public void RepaidOrZeroBalance_ReturnsZero()
    {
        var loan = new Loan
        {
            PrincipalAmount = 200_000m,
            InterestAmount = 20_000m,
            DisbursedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = LoanStatus.Repaid
        };
        Assert.Equal(0m, LoanService.GetExpectedRepaymentForMonth(loan, 2026, 6));
    }

    // ── 2. ApplyRepaymentFromImportAsync: FIFO + kikomo + Repaid ──

    [Fact]
    public async Task ApplyRepayment_OldestLoanFirst_AndCappedAtInstallment()
    {
        using var db = NewDb();
        var (group, member, user) = SeedGroup(db);
        var resolver = new AccountResolverService(db);
        var loanService = new LoanService(db, resolver, new GroupAuthorizationService(db));

        // Mkopo mzee (Jan): installment ~18,333
        var oldLoan = SeedLoan(db, group, member,
            200_000m, 10m,
            new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        // Mkopo mpya (Machi): installment ~11,000
        var newLoan = SeedLoan(db, group, member,
            100_000m, 10m,
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        // Pesa nyingi (50,000) zinapatikana mwezi Juni.
        decimal applied = await loanService.ApplyRepaymentFromImportAsync(
            member.Id, user.Id, group.Id, 50_000m,
            new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), "TEST-REF");

        // Inapaswa kukata KWA MKOPO MZEE KWANZA, na KIKOMO cha installment
        // ya mwezi huo kwa kila mkopo:
        //   oldLoan Juni installment = 220,000/12 = 18,333
        //   newLoan Juni installment = 110,000/10 = 11,000
        //   jumla inayostahili = 29,333 (50,000 haizidi hii)
        Assert.Equal(29_333m, applied);

        // Apply HAIFANYI SaveChanges (caller ndiye anayefanya) - lazima
        // tufanye hapa KABLA ya query, maana EF query haiwezi kuona
        // entries ambazo hazijahifadhiwa (lesson ya SumAsync gotcha).
        db.SaveChanges();

        // LedgerEntry ya LoanRepayment iliandikwa
        var entries = await db.LedgerEntries
            .Where(l => l.Type == TransactionType.LoanRepayment)
            .ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.ReferenceNo!.Contains("-REJESHO-"));

        // Mikopo bado Active (haijakamilika)
        Assert.Equal(LoanStatus.Active, oldLoan.Status);
        Assert.Equal(LoanStatus.Active, newLoan.Status);
        Assert.Equal(18_333m, oldLoan.AmountRepaid);
        Assert.Equal(11_000m, newLoan.AmountRepaid);
    }

    [Fact]
    public async Task ApplyRepayment_CompletesLoan_SetsRepaidStatus()
    {
        using var db = NewDb();
        var (group, member, user) = SeedGroup(db);
        var resolver = new AccountResolverService(db);
        var loanService = new LoanService(db, resolver, new GroupAuthorizationService(db));

        // Mkopo mdogo unaokamilika mwezi huu (mwezi wa mwisho).
        // TotalPayable = 110,000; miezi Jan..Jun = 6; installment = 18,333.
        // 5 installments zimeshalipwa = 91,665; salio = 18,335.
        var loan = SeedLoan(db, group, member,
            100_000m, 10m,
            new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            amountRepaid: 91_665m);

        // Mwezi wa mwisho (Juni): salio lote = 110,000 - 91,665 = 18,335
        decimal applied = await loanService.ApplyRepaymentFromImportAsync(
            member.Id, user.Id, group.Id, 50_000m,
            new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), "TEST-REF");

        db.SaveChanges();

        Assert.Equal(18_335m, applied);
        Assert.Equal(LoanStatus.Repaid, loan.Status);
        Assert.NotNull(loan.RepaidAt);
    }

    [Fact]
    public async Task ApplyRepayment_NoActiveLoans_ReturnsZero()
    {
        using var db = NewDb();
        var (group, member, user) = SeedGroup(db);
        var resolver = new AccountResolverService(db);
        var loanService = new LoanService(db, resolver, new GroupAuthorizationService(db));

        decimal applied = await loanService.ApplyRepaymentFromImportAsync(
            member.Id, user.Id, group.Id, 50_000m,
            new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), "TEST-REF");

        Assert.Equal(0m, applied);
    }

    // ── 3. MemberStatement: Lengo + Hali ──

    [Fact]
    public async Task MemberStatement_LengoIncludesRepayment_AndStatusCorrect()
    {
        using var db = NewDb();
        var (group, member, user) = SeedGroup(db, monthlyContribution: 10_000m);
        var resolver = new AccountResolverService(db);

        // Mkopo unaostahili rejesho mwezi Juni
        SeedLoan(db, group, member,
            200_000m, 10m,
            new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        // Mwanachama alilipa mchango (10,000) + rejesho (18,333) mwezi Juni
        var savingsAccount = await resolver.GetOrCreateAccountAsync(member.Id, AccountType.Savings);
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = savingsAccount.Id,
            Amount = 10_000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "MKOBA-TEST-1",
            Description = "Mchango wa mwezi",
            CreatedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = savingsAccount.Id,
            Amount = 18_333m,
            Type = TransactionType.LoanRepayment,
            ReferenceNo = "MKOBA-TEST-2-REJESHO-ABC",
            Description = "Rejesho la mkopo",
            CreatedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        db.SaveChanges();

        var bre = new BusinessRuleEngine(db);
        var ols = new ObligationLedgerService(db, bre);
        var fps = new FinancialPositionService(db, bre, ols);
        var service = new MemberStatementService(db, bre, ols, fps);
        var statement = await service.GetMonthlyStatementAsync(group.Id, 2026, 6);

        Assert.Single(statement.Rows);
        var row = statement.Rows[0];

        // Lengo = mchango (10,000) + rejesho linalostahili (18,333)
        Assert.Equal(28_333m, row.Lengo);
        Assert.Equal(10_000m, row.ExpectedContribution);
        Assert.Equal(18_333m, row.ExpectedRepayment);

        // Ametoa = 10,000 + 18,333 = 28,333
        Assert.Equal(28_333m, row.Ametoa);

        // Alilipa kikamilifu -> "Amelipa"
        Assert.Equal("Amelipa", row.MonthStatus);
        Assert.Equal(0m, row.Upungufu);
    }

    [Fact]
    public async Task MemberStatement_PartialPayment_ShowsSehemu()
    {
        using var db = NewDb();
        var (group, member, user) = SeedGroup(db, monthlyContribution: 10_000m);
        var resolver = new AccountResolverService(db);

        // Hakuna mkopo -> Lengo = 10,000 tu
        // Mwanachama alilipa 5,000 tu (sehemu)
        var savingsAccount = await resolver.GetOrCreateAccountAsync(member.Id, AccountType.Savings);
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = savingsAccount.Id,
            Amount = 5_000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "MKOBA-TEST-3",
            Description = "Mchango wa sehemu",
            CreatedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        db.SaveChanges();

        var bre = new BusinessRuleEngine(db);
        var ols = new ObligationLedgerService(db, bre);
        var fps = new FinancialPositionService(db, bre, ols);
        var service = new MemberStatementService(db, bre, ols, fps);
        var statement = await service.GetMonthlyStatementAsync(group.Id, 2026, 6);

        var row = statement.Rows[0];
        Assert.Equal(10_000m, row.Lengo);
        Assert.Equal(5_000m, row.Ametoa);
        Assert.Equal("Amelipa Sehemu", row.MonthStatus);
        Assert.Equal(5_000m, row.Upungufu);
    }

    [Fact]
    public async Task MemberStatement_NoPayment_ShowsHajalipa()
    {
        using var db = NewDb();
        var (group, member, user) = SeedGroup(db, monthlyContribution: 10_000m);

        var bre = new BusinessRuleEngine(db);
        var ols = new ObligationLedgerService(db, bre);
        var fps = new FinancialPositionService(db, bre, ols);
        var service = new MemberStatementService(db, bre, ols, fps);
        var statement = await service.GetMonthlyStatementAsync(group.Id, 2026, 6);

        var row = statement.Rows[0];
        Assert.Equal(10_000m, row.Lengo);
        Assert.Equal(0m, row.Ametoa);
        Assert.Equal("Hajalipa", row.MonthStatus);
        Assert.Equal(1, statement.CountHajalipa);
    }
}
