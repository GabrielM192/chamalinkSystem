using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// JARIBIO LA KULINDA DHIDI YA "SILENT FALLTHROUGH" — kipengele ambacho
/// mtumiaji anakichagua kwenye settings, mfumo unakubali, kisha unafanya
/// KITU KINGINE bila kusema chochote.
///
/// Hii ni hatari zaidi kuliko kosa linaloonekana: mtunza-hazina anaamini
/// namba zinazoonekana kwenye skrini, lakini zimekokotolewa kwa sheria
/// tofauti na katiba ya kikundi chake.
///
/// Matukio mawili yaligunduliwa 2026-09-16:
///
/// 1. LoanInterestType.Reducing
///    GroupService inaruhusu kuichagua; LoanService HAISOMI uwanja huu
///    popote (grep → ApplicationDbContext.cs:134 pekee, yaani ramani ya
///    column tu). Kikundi kilichochagua "Reducing" kilikuwa kinatozwa
///    riba ya FLAT kimya kimya.
///
/// 2. DebtAllocationStrategy
///    Ukaguzi wa nje ulidai "ManualAllocation inanymaza kama
///    CurrentMonthFirst". HIYO ILIKUWA SI KWELI — hali halisi ilikuwa
///    mbaya zaidi:
///        GroupSettingsModules.cs:57 → chaguo-msingi = CurrentMonthFirst
///        DebtService                → OrderBy(d => d.Period) = zamani kwanza
///                                      = OldestDebtFirst
///    Yaani kila kikundi kwa CHAGUO-MSINGI kilipata mgawanyo wa
///    OldestDebtFirst, kinyume kabisa na settings zake.
///
/// Kanuni: mfumo unapaswa KUKATAA kwa ujumbe wazi badala ya kunyamaza.
/// </summary>
public class SilentFallthroughTests
{
    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            // LoanService.IssueLoanAsync inatumia BeginTransactionAsync (fix ya
            // ukaguzi 3.3). InMemory provider haitumii transactions, na kwa
            // chaguo-msingi hupandisha hii kuwa EXCEPTION. Tunaiweka kuwa
            // ilani tu ili tuweze kujaribu njia ya mkopo bila PostgreSQL.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (Group Group, GroupSettings Settings, GroupMember Member, User User) SeedGroup(
        ApplicationDbContext db,
        LoanInterestType interestType = LoanInterestType.Flat,
        DebtAllocationStrategy strategy = DebtAllocationStrategy.CurrentMonthFirst)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Kikundi cha Jaribio",
            Code = $"JAR-{Guid.NewGuid().ToString("N")[..6]}",
            Type = GroupType.MonthlySavings
        };

        var settings = new GroupSettings
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Loan = new LoanSettings { Enabled = true, InterestRate = 10m, InterestType = interestType, RepaymentDays = 30 },
            Contribution = new ContributionSettings
            {
                MonthlyContribution = 10_000m,
                DebtAllocationStrategy = strategy
            }
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Asha Mkopaji",
            Email = $"asha{Guid.NewGuid():N}@test.com",
            PhoneNumber = "0700000001",
            PasswordHash = "hash"
        };

        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            MemberNumber = "M-001",
            Role = GroupRole.Treasurer,
            Status = MemberStatus.Active
        };

        db.Groups.Add(group);
        db.GroupSettings.Add(settings);
        db.Users.Add(user);
        db.GroupMembers.Add(member);
        db.SaveChanges();

        return (group, settings, member, user);
    }

    // ------------------------------------------------------------------
    // LoanInterestType.Reducing
    // ------------------------------------------------------------------

    /// <summary>
    /// Kikundi kilichochagua "Reducing Balance" KILIKUWA kinatozwa riba ya
    /// Flat kimya kimya. Sasa lazima mfumo ukatae kwa ujumbe wazi.
    ///
    /// Kabla ya fix: jaribio hili lingeshindwa (mkopo ungetolewa kwa 200).
    /// </summary>
    [Fact]
    public async Task IssueLoan_WithReducingInterestType_IsRejected_NotSilentlyFlat()
    {
        using var db = NewDb();
        var (group, _, member, user) = SeedGroup(db, interestType: LoanInterestType.Reducing);

        // Akiba ya kutosha ili MaxLoanMultiplier isizuie
        var account = new Account { GroupMemberId = member.Id, Type = AccountType.Savings };
        db.Accounts.Add(account);
        db.SaveChanges();
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), GroupId = group.Id, UserId = user.Id,
            AccountId = account.Id, Amount = 500_000m,
            Type = TransactionType.Contribution, ReferenceNo = "AKIBA"
        });
        await db.SaveChangesAsync();

        var loanService = new LoanService(
            db, new AccountResolverService(db), new GroupAuthorizationService(db));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            loanService.IssueLoanAsync(group.Id, new IssueLoanDto(
                member.Id, 50_000m, null, null, "Jaribio"), user.Id));

        // Ujumbe lazima ueleze kinachotokea na nini cha kufanya
        Assert.Contains("Reducing", ex.Message);
        Assert.Contains("Flat", ex.Message);

        // Na hakuna mkopo uliotengenezwa
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    /// <summary>
    /// Kinyume chake: Flat (ndiyo inayofanya kazi) lazima iendelee kuruhusiwa.
    /// Jaribio hili linalinda dhidi ya kuzuia sana (over-blocking).
    /// </summary>
    [Fact]
    public async Task IssueLoan_WithFlatInterestType_StillWorks()
    {
        using var db = NewDb();
        var (group, _, member, user) = SeedGroup(db, interestType: LoanInterestType.Flat);

        var account = new Account { GroupMemberId = member.Id, Type = AccountType.Savings };
        db.Accounts.Add(account);
        db.SaveChanges();
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), GroupId = group.Id, UserId = user.Id,
            AccountId = account.Id, Amount = 500_000m,
            Type = TransactionType.Contribution, ReferenceNo = "AKIBA"
        });
        await db.SaveChangesAsync();

        var loanService = new LoanService(
            db, new AccountResolverService(db), new GroupAuthorizationService(db));

        var loan = await loanService.IssueLoanAsync(
            group.Id, new IssueLoanDto(member.Id, 50_000m, null, null, "Jaribio"), user.Id);

        Assert.Equal(50_000m, loan.PrincipalAmount);
        // 10% flat = 5,000
        Assert.Equal(5_000m, loan.InterestAmount);
        Assert.Equal(1, await db.Loans.CountAsync());
    }

    // ------------------------------------------------------------------
    // DebtAllocationStrategy
    // ------------------------------------------------------------------

    /// <summary>
    /// ManualAllocation inahitaji UI ya mtunza-hazina kuchagua deni gani.
    /// Haijatekelezwa - lazima mfumo ukatae badala ya kugawa pesa kwa
    /// mpangilio wowote tu.
    /// </summary>
    [Fact]
    public async Task ClearWithPayment_WithManualAllocation_IsRejected()
    {
        using var db = NewDb();
        var (group, _, member, _) = SeedGroup(db, strategy: DebtAllocationStrategy.ManualAllocation);

        var debtService = new DebtService(db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            debtService.ClearWithPaymentAsync(member.Id, 10_000m, group.Id));

        Assert.Contains("ManualAllocation", ex.Message);
        Assert.Contains("CurrentMonthFirst", ex.Message);
    }

    /// <summary>
    /// OldestDebtFirst ingehitaji kubadilisha mpangilio mzima wa "waterfall"
    /// ya MkobaImport. Haijatekelezwa - lazima ikataliwe.
    /// </summary>
    [Fact]
    public async Task ClearWithPayment_WithOldestDebtFirst_IsRejected()
    {
        using var db = NewDb();
        var (group, _, member, _) = SeedGroup(db, strategy: DebtAllocationStrategy.OldestDebtFirst);

        var debtService = new DebtService(db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            debtService.ClearWithPaymentAsync(member.Id, 10_000m, group.Id));

        Assert.Contains("OldestDebtFirst", ex.Message);
    }

    /// <summary>
    /// CurrentMonthFirst ndiyo inayofanya kazi na waterfall iliyopo -
    /// lazima iendelee kuruhusiwa na kufuta deni halisi.
    /// </summary>
    [Fact]
    public async Task ClearWithPayment_WithCurrentMonthFirst_ClearsDebt()
    {
        using var db = NewDb();
        var (group, _, member, user) = SeedGroup(db, strategy: DebtAllocationStrategy.CurrentMonthFirst);

        db.Debts.Add(new Debt
        {
            Id = Guid.NewGuid(), GroupId = group.Id, GroupMemberId = member.Id,
            UserId = user.Id, Amount = 10_000m, Reason = "Jaribio",
            Period = new DateTime(2026, 1, 1), Status = DebtStatus.Outstanding
        });
        await db.SaveChangesAsync();

        var debtService = new DebtService(db);
        var applied = await debtService.ClearWithPaymentAsync(member.Id, 10_000m, group.Id);

        Assert.Equal(10_000m, applied);
        var debt = await db.Debts.FirstAsync(d => d.GroupMemberId == member.Id);
        Assert.Equal(DebtStatus.Cleared, debt.Status);
    }

    /// <summary>
    /// Utangamano wa nyuma: wateja wa zamani wasiopitisha groupId
    /// (parameta ni `Guid?` ya hiari) wasivunjike.
    /// </summary>
    [Fact]
    public async Task ClearWithPayment_WithoutGroupId_StillWorks_BackwardsCompatible()
    {
        using var db = NewDb();
        var (group, _, member, user) = SeedGroup(db, strategy: DebtAllocationStrategy.ManualAllocation);

        db.Debts.Add(new Debt
        {
            Id = Guid.NewGuid(), GroupId = group.Id, GroupMemberId = member.Id,
            UserId = user.Id, Amount = 5_000m, Reason = "Jaribio",
            Period = new DateTime(2026, 2, 1), Status = DebtStatus.Outstanding
        });
        await db.SaveChangesAsync();

        var debtService = new DebtService(db);

        // Hakuna groupId → hakuna ukaguzi wa mkakati (tabia ya zamani)
        var applied = await debtService.ClearWithPaymentAsync(member.Id, 5_000m);

        Assert.Equal(5_000m, applied);
    }
}
