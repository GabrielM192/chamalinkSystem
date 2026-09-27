using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChamaLink.Tests;

public class FinancialAccuracyRegressionTests
{
    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static GroupPolicy Policy(Guid groupId, int dueDateDay = 17) => new()
    {
        Id = Guid.NewGuid(),
        GroupId = groupId,
        Version = 1,
        EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        MonthlyContribution = 10_000m,
        JoiningFee = 50_000m,
        DueDateDay = dueDateDay,
        GracePeriodDays = 5,
        LateFine = 5_000m
    };

    private static (Group Group, GroupMember Member, User User) SeedMember(
        ApplicationDbContext db,
        DateTime? joinedAt = null)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Accuracy Test Group",
            Code = "ACC",
            Type = GroupType.MonthlySavings
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Accuracy Test Member",
            Email = $"accuracy-{Guid.NewGuid():N}@test.com",
            PhoneNumber = "255700000001",
            PasswordHash = "hash"
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            JoinedAt = joinedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = MemberStatus.Active
        };

        db.Groups.Add(group);
        db.Users.Add(user);
        db.GroupMembers.Add(member);
        db.SaveChanges();
        return (group, member, user);
    }

    [Fact]
    public async Task DueDateComesFromPolicy_AndFallsInFollowingMonth()
    {
        using var db = NewDb();
        var (group, member, _) = SeedMember(db);
        var policy = Policy(group.Id, dueDateDay: 17);
        var service = new ObligationLedgerService(db, new BusinessRuleEngine(db));

        var queue = await service.GenerateRawQueueWithPolicyAsync(
            member.Id,
            policy,
            new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc));

        var january = Assert.Single(queue);
        Assert.Equal(2026, january.Year);
        Assert.Equal(1, january.Month);
        Assert.Equal(new DateTime(2026, 2, 17, 23, 59, 59, DateTimeKind.Utc), january.DueDate);
    }

    [Fact]
    public async Task SavingsLedgerEntry_DoesNotPayContributionObligation()
    {
        using var db = NewDb();
        var (group, member, user) = SeedMember(db);
        var policy = Policy(group.Id);

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = Guid.NewGuid(),
            Amount = 20_000m,
            Type = TransactionType.Savings,
            ReferenceNo = "SAVINGS-1",
            CreatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)
        });
        db.SaveChanges();

        var service = new ObligationLedgerService(db, new BusinessRuleEngine(db));
        var queue = await service.GetOutstandingQueueWithPolicyAsync(
            member.Id,
            policy,
            new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc));

        var january = Assert.Single(queue);
        Assert.Equal(10_000m, january.ContributionOutstanding);
        Assert.Equal(50_000m, january.JoinFeeOutstanding);
    }

    [Fact]
    public void OldestFirstAllocation_UpdatesQueueForTheNextPayment()
    {
        var groupId = Guid.NewGuid();
        var policy = Policy(groupId);
        var first = new ObligationItem
        {
            Year = 2026,
            Month = 1,
            DueDate = new DateTime(2026, 2, 17, 23, 59, 59, DateTimeKind.Utc),
            ContributionDue = 10_000m
        };
        var second = new ObligationItem
        {
            Year = 2026,
            Month = 2,
            DueDate = new DateTime(2026, 3, 17, 23, 59, 59, DateTimeKind.Utc),
            ContributionDue = 10_000m
        };
        var queue = new List<ObligationItem> { first, second };
        var engine = new AllocationEngine(new BusinessRuleEngine(NewDb()));

        var result = engine.AllocateOldestFirst(
            15_000m,
            new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            queue,
            policy,
            new List<Loan>(),
            2026,
            1);

        Assert.Equal(10_000m, first.ContributionPaid);
        Assert.Equal(5_000m, second.ContributionPaid);
        Assert.Equal(15_000m, result.Allocations.Where(a => a.Target == "Contribution").Sum(a => a.Amount));
        Assert.DoesNotContain(result.Allocations, a => a.Target == "Savings");
    }
}