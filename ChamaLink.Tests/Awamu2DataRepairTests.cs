using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// 2026-09-19: Ukarabati wa data ya bug-era.
///
/// Malengo:
///   1. Preview ni READ-ONLY (hakuna SaveChanges, counters hazibadiliki)
///   2. RepairCounters: inarekebisha drift na ni idempotent
///   3. RepairSnapshots: inajenga snapshots zilizopungua na ni idempotent
///   4. Loan gaps ni ripoti tu — hatuhamishi pesa
/// </summary>
public class Awamu2DataRepairTests
{
    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Group SeedGroup(ApplicationDbContext db, decimal monthlyContribution = 10000m)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Ukonga Test",
            Type = GroupType.MonthlySavings,
            CreatedAt = DateTime.UtcNow.AddMonths(-6),
            Settings = new GroupSettings
            {
                Id = Guid.NewGuid(),
                Contribution = new ContributionSettings
                {
                    MonthlyContribution = monthlyContribution,
                    DueDateDay = 5,
                    GracePeriodDays = 7,
                    LateFine = 2000m,
                    MaxConsecutiveMissedMonths = 3
                },
                Financial = new FinancialSettings { JoiningFee = 5000m }
            }
        };
        group.Settings.GroupId = group.Id;
        group.Settings.Id = Guid.NewGuid();
        db.Groups.Add(group);
        db.SaveChanges();
        return group;
    }

    private static GroupMember SeedMember(ApplicationDbContext db, Group group, string name = "Test Member", int oldCount = 0, decimal oldBalance = 0m)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = name,
            PhoneNumber = $"2557{Random.Shared.Next(10000000, 99999999)}"
        };
        db.Users.Add(user);

        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            JoinedAt = DateTime.UtcNow.AddMonths(-5),
            Status = MemberStatus.Active,
            TotalContributionsCount = oldCount,
            AdvanceBalance = oldBalance,
            User = user
        };
        db.GroupMembers.Add(member);
        db.SaveChanges();
        return member;
    }

    private static Account SeedSavingsAccount(ApplicationDbContext db, GroupMember member)
    {
        var acc = new Account
        {
            Id = Guid.NewGuid(),
            GroupMemberId = member.Id,
            Type = AccountType.Savings
        };
        db.Accounts.Add(acc);
        db.SaveChanges();
        return acc;
    }

    [Fact]
    public async Task Preview_IsReadOnly_DoesNotChangeCounters()
    {
        using var db = NewDb();
        var group = SeedGroup(db);
        var member = SeedMember(db, group, "Juma", oldCount: 999, oldBalance: 999999m);
        var acc = SeedSavingsAccount(db, member);

        // Ledger ina michango 2 tu — counter ya 999 ni drift
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            AccountId = acc.Id,
            UserId = member.UserId,
            Amount = 10000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "REF-1",
            CreatedAt = DateTime.UtcNow.AddMonths(-2)
        });
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            AccountId = acc.Id,
            UserId = member.UserId,
            Amount = 10000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "REF-2",
            CreatedAt = DateTime.UtcNow.AddMonths(-1)
        });
        db.SaveChanges();

        var counterSync = new MemberCounterSyncService(db);
        var snapshotSvc = new ComplianceSnapshotService();
        var repairSvc = new DataRepairService(db, counterSync, snapshotSvc);

        var preview = await repairSvc.GetPreviewAsync(group.Id, monthsBack: 6);

        // Preview inapaswa kuona drift
        Assert.Equal(1, preview.CounterDriftCount);
        Assert.Single(preview.CounterDrifts);
        Assert.Equal(999, preview.CounterDrifts[0].OldCount);
        Assert.Equal(2, preview.CounterDrifts[0].NewCount);

        // Lakini counters za DB hazijabadilika (READ-ONLY)
        var memberAfter = await db.GroupMembers.FirstAsync(m => m.Id == member.Id);
        Assert.Equal(999, memberAfter.TotalContributionsCount);
        Assert.Equal(999999m, memberAfter.AdvanceBalance);
    }

    [Fact]
    public async Task RepairCounters_FixesDrift_AndIsIdempotent()
    {
        using var db = NewDb();
        var group = SeedGroup(db);
        var member = SeedMember(db, group, "Asha", oldCount: 0, oldBalance: 0m);
        var acc = SeedSavingsAccount(db, member);

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            AccountId = acc.Id,
            UserId = member.UserId,
            Amount = 15000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "REF-A",
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var counterSync = new MemberCounterSyncService(db);
        var snapshotSvc = new ComplianceSnapshotService();
        var repairSvc = new DataRepairService(db, counterSync, snapshotSvc);

        // First repair — should fix
        var result1 = await repairSvc.RepairCountersAsync(group.Id);
        Assert.Equal(1, result1.MembersCorrected);

        var memberAfter1 = await db.GroupMembers.FirstAsync(m => m.Id == member.Id);
        Assert.Equal(1, memberAfter1.TotalContributionsCount);
        Assert.Equal(15000m, memberAfter1.AdvanceBalance);

        // Second repair — should be 0 (idempotent)
        var result2 = await repairSvc.RepairCountersAsync(group.Id);
        Assert.Equal(0, result2.MembersCorrected);
    }

    [Fact]
    public async Task RepairSnapshots_CreatesMissingSnapshots()
    {
        using var db = NewDb();
        var group = SeedGroup(db, monthlyContribution: 10000m);
        var member = SeedMember(db, group, "Baraka");
        SeedSavingsAccount(db, member);

        // Hakuna snapshot yoyote — jedwali tupu (bug-era)
        var existing = await db.ComplianceSnapshots.CountAsync();
        Assert.Equal(0, existing);

        var counterSync = new MemberCounterSyncService(db);
        var snapshotSvc = new ComplianceSnapshotService();
        var repairSvc = new DataRepairService(db, counterSync, snapshotSvc);

        var previewBefore = await repairSvc.GetPreviewAsync(group.Id, monthsBack: 3);
        Assert.True(previewBefore.TotalMissingSnapshots > 0);

        var result = await repairSvc.RepairSnapshotsAsync(group.Id, monthsBack: 3);

        Assert.True(result.SnapshotsCreated > 0);
        var after = await db.ComplianceSnapshots.CountAsync();
        Assert.True(after > 0);

        // Idempotent — second run should create 0 new
        var result2 = await repairSvc.RepairSnapshotsAsync(group.Id, monthsBack: 3);
        Assert.Equal(0, result2.SnapshotsCreated);
        Assert.True(result2.SnapshotsUpdated > 0);
    }

    [Fact]
    public async Task Preview_DetectsLoanRepaymentGaps()
    {
        using var db = NewDb();
        var group = SeedGroup(db);
        var member = SeedMember(db, group, "Frank");
        var savingsAcc = SeedSavingsAccount(db, member);

        var loanAcc = new Account
        {
            Id = Guid.NewGuid(),
            GroupMemberId = member.Id,
            Type = AccountType.Loan
        };
        db.Accounts.Add(loanAcc);

        // Mkopo unaotarajiwa kulipwa mwezi huu
        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            GroupMemberId = member.Id,
            UserId = member.UserId,
            PrincipalAmount = 100000m,
            InterestRate = 10m,
            InterestAmount = 10000m,
            AmountRepaid = 0m,
            DisbursedAt = DateTime.UtcNow.AddMonths(-2),
            DueDate = DateTime.UtcNow.AddMonths(4),
            Status = LoanStatus.Active,
            IssuedByGroupMemberId = member.Id
        };
        db.Loans.Add(loan);

        // Mwezi huu: akiba iliingizwa lakini hakuna rejesho — bug-era pattern
        var now = DateTime.UtcNow;
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            AccountId = savingsAcc.Id,
            UserId = member.UserId,
            Amount = 20000m,
            Type = TransactionType.Contribution,
            ReferenceNo = "BUG-ERA-1",
            CreatedAt = now
        });
        db.SaveChanges();

        var counterSync = new MemberCounterSyncService(db);
        var snapshotSvc = new ComplianceSnapshotService();
        var repairSvc = new DataRepairService(db, counterSync, snapshotSvc);

        var preview = await repairSvc.GetPreviewAsync(group.Id, monthsBack: 3);

        // Inapaswa kuona gap ya rejesho
        Assert.True(preview.LoanRepaymentGaps.Count > 0);
        var gap = preview.LoanRepaymentGaps.First(g => g.GroupMemberId == member.Id);
        Assert.True(gap.ExpectedRepayment > 0);
        Assert.Equal(0m, gap.PaidRepayment);
        Assert.True(gap.SavingsPostedInMonth > 0);
    }
}
