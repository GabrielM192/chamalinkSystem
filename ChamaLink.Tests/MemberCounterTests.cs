using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// JARIBIO LA KULINDA DHIDI YA BUG ILIYOGUNDULIWA 2026-09-16.
///
/// GroupMember ina counter mbili zilizohifadhiwa (denormalized):
///     TotalContributionsCount   - "Michango: Mara N" kwenye ripoti ya WhatsApp
///     AdvanceBalance            - akiba ya mwanachama
///
/// Mfumo una njia TATU zinazoandika mchango. Mbili za import zilikuwa
/// zinabadilisha counter hizo; njia ya mchango wa mkono (LedgerService)
/// HAIKUWA inazibadilisha. Matokeo: ripoti ya WhatsApp ilionyesha
/// "Michango: Mara 7" kwa mwanachama mwenye michango 12.
///
/// Suluhisho: counter sasa zinahesabiwa kutoka kwenye LEDGER (chanzo cha
/// ukweli) kupitia MemberCounterSyncService, badala ya kubadilishwa moja
/// kwa moja kwenye kila njia.
///
/// Majaribio haya yanahakikisha:
///   1. Mchango wa mkono unabadilisha counter (bug ya awali)
///   2. Counter zinaweza kuhesabiwa upya kutoka kwenye ledger
///   3. Ukarabati ni idempotent (kuuita mara mbili haubadilishi kitu)
///   4. Mwanachama asiye na akaunti ya Savings anapata sifuri
/// </summary>
public class MemberCounterTests
{
    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (Group Group, GroupMember Member, User User) SeedMember(ApplicationDbContext db)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Kikundi cha Jaribio",
            Code = "JAR-1",
            Type = GroupType.MonthlySavings,
            Settings = new GroupSettings
            {
                Id = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                Financial = new FinancialSettings { JoiningFee = 50_000m },
                Contribution = new ContributionSettings
                {
                    MonthlyContribution = 10_000m,
                    DueDateDay = 5,
                    GracePeriodDays = 5,
                    LateFine = 5_000m
                }
            }
        };
        group.Settings!.GroupId = group.Id;

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Juma Mwanachama",
            Email = $"juma{Guid.NewGuid():N}@test.com",
            PhoneNumber = "0700000000",
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
        db.Users.Add(user);
        db.GroupMembers.Add(member);
        db.SaveChanges();

        return (group, member, user);
    }

    private static Account SeedSavingsAccount(ApplicationDbContext db, Guid groupMemberId)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            GroupMemberId = groupMemberId,
            Type = AccountType.Savings
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    /// <summary>
    /// HII NDIO BUG YENYEWE: mchango wa mkono lazima ubadilishe counter.
    /// Kabla ya fix, jaribio hili lingeshindwa (counter zingeendelea kuwa 0).
    /// </summary>
    [Fact]
    public async Task RecordContribution_UpdatesBothCounters()
    {
        using var db = NewDb();
        var (group, member, user) = SeedMember(db);

        var resolver = new AccountResolverService(db);
        var counterSync = new MemberCounterSyncService(db);
        var businessRuleEngine = new BusinessRuleEngine(db);
        var obligationLedgerService = new ObligationLedgerService(db, businessRuleEngine);
        var allocationEngine = new AllocationEngine(businessRuleEngine);
        var financialPositionService = new FinancialPositionService(db, businessRuleEngine, obligationLedgerService);
        var auditService = new AuditService(db);
        var ledger = new LedgerService(db, resolver, counterSync, allocationEngine, businessRuleEngine, financialPositionService, obligationLedgerService, auditService);

        // Hali ya awali
        Assert.Equal(0, member.TotalContributionsCount);
        Assert.Equal(0m, member.AdvanceBalance);

        // Michango mitatu ya mkono
        for (var i = 1; i <= 3; i++)
        {
            await ledger.RecordContributionAsync(new RecordContributionDto(
                group.Id, user.Id, 10_000m, $"REF-{i}", $"Mchango #{i}"));
        }

        // Somwa upya kutoka kwenye database
        var fresh = await db.GroupMembers.AsNoTracking()
            .FirstAsync(m => m.Id == member.Id);

        Assert.Equal(3, fresh.TotalContributionsCount);
        Assert.Equal(30_000m, fresh.AdvanceBalance);
    }

    /// <summary>
    /// Ukarabati unahesabu upya kutoka kwenye ledger - hata kama counter
    /// zilizohifadhiwa zimevurugika vibaya sana.
    /// </summary>
    [Fact]
    public async Task Reconcile_RecomputesFromLedger_NotFromStoredValues()
    {
        using var db = NewDb();
        var (group, member, user) = SeedMember(db);
        var account = SeedSavingsAccount(db, member.Id);

        // Michango miwili halisi kwenye ledger
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = account.Id,
            Amount = 10_000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "R1"
        });
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = account.Id,
            Amount = 5_000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "R2"
        });

        // Counter zilizovurugika (kama data ya zamani kabla ya fix)
        member.TotalContributionsCount = 99;
        member.AdvanceBalance = 123.45m;
        await db.SaveChangesAsync();

        var sync = new MemberCounterSyncService(db);
        var result = await sync.ReconcileGroupAsync(group.Id);
        await db.SaveChangesAsync();

        Assert.True(result.HadDrift);
        Assert.Equal(1, result.MembersChecked);
        Assert.Equal(1, result.MembersCorrected);

        var fresh = await db.GroupMembers.AsNoTracking().FirstAsync(m => m.Id == member.Id);
        Assert.Equal(2, fresh.TotalContributionsCount);
        Assert.Equal(15_000m, fresh.AdvanceBalance);
    }

    /// <summary>
    /// Ukarabati ni idempotent: kuuita mara ya pili hakuripoti drift tena.
    /// Muhimu kwa sababu endpoint hii inaruhusiwa kuitwa mara nyingi.
    /// </summary>
    [Fact]
    public async Task Reconcile_IsIdempotent()
    {
        using var db = NewDb();
        var (group, member, _) = SeedMember(db);
        SeedSavingsAccount(db, member.Id);

        var sync = new MemberCounterSyncService(db);

        var first = await sync.ReconcileGroupAsync(group.Id);
        await db.SaveChangesAsync();

        var second = await sync.ReconcileGroupAsync(group.Id);
        await db.SaveChangesAsync();

        Assert.Equal(0, second.MembersCorrected);
        Assert.False(second.HadDrift);
        Assert.Equal(0, second.CountDrift);
        Assert.Equal(0m, second.BalanceDrift);
    }

    /// <summary>
    /// Mwanachama asiye na akaunti ya Savings hajachangia kamwe -
    /// counter lazima ziwe sifuri, siyo thamani za zamani zilizobaki.
    /// </summary>
    [Fact]
    public async Task Reconcile_MemberWithoutSavingsAccount_GetsZeroes()
    {
        using var db = NewDb();
        var (group, member, _) = SeedMember(db);

        // Hakuna akaunti ya Savings - lakini counter zina thamani za zamani
        member.TotalContributionsCount = 7;
        member.AdvanceBalance = 500m;
        await db.SaveChangesAsync();

        var sync = new MemberCounterSyncService(db);
        await sync.ReconcileMemberAsync(member.Id);
        await db.SaveChangesAsync();

        var fresh = await db.GroupMembers.AsNoTracking().FirstAsync(m => m.Id == member.Id);
        Assert.Equal(0, fresh.TotalContributionsCount);
        Assert.Equal(0m, fresh.AdvanceBalance);
    }

    /// <summary>
    /// Ukarabati unapuuza aina nyingine za miamala - ni michango (Contribution)
    /// pekee inayohesabiwa, siyo faini wala mikopo.
    /// </summary>
    [Fact]
    public async Task Reconcile_OnlyCountsContributions_NotOtherTransactionTypes()
    {
        using var db = NewDb();
        var (group, member, user) = SeedMember(db);
        var account = SeedSavingsAccount(db, member.Id);

        void AddEntry(decimal amount, TransactionType type) => db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = account.Id,
            Amount = amount,
            Type = type,
            ReferenceNo = Guid.NewGuid().ToString("N")[..8]
        });

        AddEntry(10_000m, TransactionType.Contribution);      // huhesabiwa
        AddEntry(5_000m, TransactionType.FineIssue);          // haiesabiwi
        AddEntry(2_000m, TransactionType.LoanDisbursement);   // haiesabiwi
        AddEntry(1_500m, TransactionType.FinePayment);        // haiesabiwi
        await db.SaveChangesAsync();

        var sync = new MemberCounterSyncService(db);
        await sync.ReconcileMemberAsync(member.Id);
        await db.SaveChangesAsync();

        var fresh = await db.GroupMembers.AsNoTracking().FirstAsync(m => m.Id == member.Id);
        Assert.Equal(1, fresh.TotalContributionsCount);
        Assert.Equal(10_000m, fresh.AdvanceBalance);
    }
}
