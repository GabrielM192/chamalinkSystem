using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// Usajili na uanachama: mwanachama (rekodi ya fedha) ni tofauti na akaunti (kuingia). Simu inapendekeza tu,
/// kiongozi ndiye anayeunganisha. Ruhusa za nafasi, manaibu, na namba rasmi zisizobadilika.
/// </summary>
public class MembershipServiceTests
{
    private sealed class NoParser : IMKobaParserService
    {
        public Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream s) => Task.FromResult(new List<MKobaTransactionItemDto>());
        public Task<MKobaParseResult> ParseStatementDetailedAsync(Stream s) => Task.FromResult(new MKobaParseResult());
    }

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required MembershipService Svc;
        public Guid GroupId, ChairUser, TreasurerUser, Frank, Tunganege;
        public string JoinCode = "";
        public int Year => TruthLedgerService.NowEat().Year;
    }

    private static User Account(string name, string phone) => new()
    {
        Id = Guid.NewGuid(), FullName = name, Email = name.Replace(" ", ".").ToLowerInvariant() + "@mfano.tz",
        PhoneNumber = phone, PasswordHash = BCrypt.Net.BCrypt.HashPassword("siri123")
    };

    private static async Task<W> Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var truth = new TruthLedgerService(db);
        var setup = new TruthSetupService(db, new NoParser(), truth);
        var w = new W { Db = db, Svc = new MembershipService(db, setup) };

        var chair = Account("Mwenyekiti Juma", "255711000001");
        var tre = Account("Mtunza Asha", "255711000002");
        db.Users.AddRange(chair, tre);
        w.ChairUser = chair.Id; w.TreasurerUser = tre.Id;
        w.GroupId = Guid.NewGuid(); w.JoinCode = "UMK-ABCDE";
        db.Groups.Add(new Group { Id = w.GroupId, Name = "Ukonga mkoba wing", ShortCode = "UMK", JoinCode = w.JoinCode });
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = w.GroupId, UserId = chair.Id, Role = GroupRole.Chairperson, MemberNumber = "M001" });
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = w.GroupId, UserId = tre.Id, Role = GroupRole.Treasurer });

        // Wanachama walioingizwa na import: HAWANA akaunti (hakuna email wala neno la siri) — kama DB halisi
        foreach (var (name, no, phone, set) in new[] { ("FRANK MKOMA", "14", "255712805984", 0), ("TUNGANEGE", "22", "255742674429", 1) })
        {
            var profile = new User { Id = Guid.NewGuid(), FullName = name, PhoneNumber = phone };
            var m = new GroupMember { Id = Guid.NewGuid(), GroupId = w.GroupId, UserId = profile.Id, MemberNumber = no, Role = GroupRole.Member };
            db.Users.Add(profile);
            db.GroupMembers.Add(m);
            db.MemberPhones.Add(new MemberPhone { Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = m.Id, Phone = phone, AddedByUserId = chair.Id });
            if (set == 0) w.Frank = m.Id; else w.Tunganege = m.Id;
        }
        await db.SaveChangesAsync();
        return w;
    }

    private static async Task<User> Register(W w, string name, string phone)
    {
        var u = Account(name, phone);
        w.Db.Users.Add(u);
        await w.Db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task PhoneMatch_OnlySuggests_NothingIsLinkedUntilALeaderConfirms()
    {
        var w = await Build();
        var frankRecordUser = (await w.Db.GroupMembers.AsNoTracking().FirstAsync(m => m.Id == w.Frank)).UserId;
        var frank = await Register(w, "Frank Mkoma", "0712805984");

        var req = await w.Svc.RequestToJoinAsync(frank.Id, "umk-abcde", GroupRole.Member, "Mimi ni Frank wa Excel namba 14");

        Assert.Equal(w.Frank, req.SuggestedMemberId);                      // pendekezo
        var record = await w.Db.GroupMembers.AsNoTracking().FirstAsync(m => m.Id == w.Frank);
        Assert.Equal(frankRecordUser, record.UserId);                       // bado HAJAUNGANISHWA
        Assert.False(await w.Db.GroupMembers.AnyAsync(m => m.UserId == frank.Id)); // wala si mwanachama bado

        var pending = Assert.Single(await w.Svc.PendingRequestsAsync(w.GroupId, w.TreasurerUser));
        Assert.Equal("FRANK MKOMA", pending.Suggested!.FullName);
        Assert.True(pending.YouCanDecide);
    }

    [Fact]
    public async Task LeaderConfirmsLink_AccountTakesOverTheExistingFinancialRecord_SameIdSameNumber()
    {
        var w = await Build();
        var frank = await Register(w, "Frank Mkoma", "0712805984");
        var req = await w.Svc.RequestToJoinAsync(frank.Id, w.JoinCode, GroupRole.Member, null);

        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, approve: true, linkMemberId: w.Frank, note: "Nimemtambua", w.TreasurerUser);

        var m = await w.Db.GroupMembers.AsNoTracking().SingleAsync(x => x.UserId == frank.Id);
        Assert.Equal(w.Frank, m.Id);                                        // rekodi ile ile ya fedha (historia yote inabaki)
        Assert.Equal($"UMK-{w.Year}-014", m.MemberNumber);                  // namba ya Excel imehifadhiwa
        Assert.NotNull(m.MemberNumberIssuedAt);
        Assert.Equal(MembershipRequestStatus.Approved, (await w.Db.MembershipRequests.FindAsync(req.Id))!.Status);
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.MemberLinkedToAccount && a.EntityId == w.Frank);

        // Rekodi iliyokwisha kuunganishwa haiwezi kuchukuliwa na akaunti nyingine
        var impostor = await Register(w, "Mtu Mwingine", "0655000111");
        var r2 = await w.Svc.RequestToJoinAsync(impostor.Id, w.JoinCode, GroupRole.Member, null);
        await Assert.ThrowsAsync<ConflictException>(() => w.Svc.DecideRequestAsync(w.GroupId, r2.Id, true, w.Frank, null, w.ChairUser));
    }

    [Fact]
    public async Task ApprovingWithoutChoosingALink_CreatesANewMember_TheSuggestionIsNeverAppliedSilently()
    {
        var w = await Build();
        var u = await Register(w, "Frank Mdogo", "0712805984"); // simu ile ile, lakini kiongozi anajua ni mtu mwingine
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Member, null);

        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, linkMemberId: null, note: null, w.ChairUser);

        var created = await w.Db.GroupMembers.AsNoTracking().SingleAsync(x => x.UserId == u.Id);
        Assert.NotEqual(w.Frank, created.Id);
        Assert.Equal($"UMK-{w.Year}-023", created.MemberNumber);             // inayofuata baada ya kubwa zaidi (hakuna rasmi bado → 1..)
        Assert.False(MembershipService.HasLogin((await w.Db.GroupMembers.Include(x => x.User).FirstAsync(x => x.Id == w.Frank)).User));
    }

    [Fact]
    public async Task NewMember_GetsTheRegistrationPhoneAsMkobaPhone_ButAPhoneOwnedBySomeoneElseIsNeverMoved()
    {
        var w = await Build();
        // Mtu mpya kabisa: simu yake ya usajili inakuwa namba yake ya M-Koba (malipo yake yataonekana)
        var neema = await Register(w, "Neema Juma", "0755 111 222");
        var r1 = await w.Svc.RequestToJoinAsync(neema.Id, w.JoinCode, GroupRole.Member, null);
        await w.Svc.DecideRequestAsync(w.GroupId, r1.Id, true, null, null, w.ChairUser);
        var nm = await w.Db.GroupMembers.AsNoTracking().SingleAsync(x => x.UserId == neema.Id);
        var phone = Assert.Single(await w.Db.MemberPhones.AsNoTracking().Where(p => p.GroupMemberId == nm.Id).ToListAsync());
        Assert.Equal("255755111222", phone.Phone);

        // Simu ambayo tayari ni ya Frank: mwanachama mpya anaundwa, lakini simu ya Frank HAIHAMISHWI
        var other = await Register(w, "Frank Mdogo", "0712805984");
        var r2 = await w.Svc.RequestToJoinAsync(other.Id, w.JoinCode, GroupRole.Member, null);
        await w.Svc.DecideRequestAsync(w.GroupId, r2.Id, true, null, null, w.ChairUser);
        var om = await w.Db.GroupMembers.AsNoTracking().SingleAsync(x => x.UserId == other.Id);
        Assert.False(await w.Db.MemberPhones.AnyAsync(p => p.GroupMemberId == om.Id));
        Assert.Equal(w.Frank, (await w.Db.MemberPhones.AsNoTracking().SingleAsync(p => p.Phone == "255712805984")).GroupMemberId);
    }

    [Fact]
    public async Task SecretaryOrTreasurerSeat_OnlyTheChairCanApprove_AndDeputiesAreAllowed()
    {
        var w = await Build();
        var u = await Register(w, "Naibu Hazina", "0655123456");
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Treasurer, "Naibu mtunza-hazina");

        Assert.False(Assert.Single(await w.Svc.PendingRequestsAsync(w.GroupId, w.TreasurerUser)).YouCanDecide);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.TreasurerUser));

        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.ChairUser);
        Assert.Equal(2, await w.Db.GroupMembers.CountAsync(m => m.GroupId == w.GroupId && m.Role == GroupRole.Treasurer)); // manaibu
    }

    [Fact]
    public async Task Rejection_NeedsAReason_AndThePersonDoesNotBecomeAMember()
    {
        var w = await Build();
        var u = await Register(w, "Mgeni", "0655999888");
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Member, null);

        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.DecideRequestAsync(w.GroupId, req.Id, false, null, "  ", w.ChairUser));
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, false, null, "Hajulikani kwa kikundi", w.ChairUser);

        Assert.False(await w.Db.GroupMembers.AnyAsync(m => m.UserId == u.Id));
        var mine = Assert.Single(await w.Svc.MyRequestsAsync(u.Id));
        Assert.Equal("Rejected", mine.Status);
        Assert.Equal("Hajulikani kwa kikundi", mine.DecisionNote);
        await Assert.ThrowsAsync<ConflictException>(() => w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.ChairUser));
    }

    [Fact]
    public async Task PlainMembers_CannotSeeOrDecideRequests_AndCannotRequestTheChairSeat()
    {
        var w = await Build();
        var u = await Register(w, "Mwanachama", "0655111222");
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Chairperson, null));
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Member, null);
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.ChairUser);

        var other = await Register(w, "Mwingine", "0655333444");
        var r2 = await w.Svc.RequestToJoinAsync(other.Id, w.JoinCode, GroupRole.Member, null);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.PendingRequestsAsync(w.GroupId, u.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.DecideRequestAsync(w.GroupId, r2.Id, true, null, null, u.Id));
        await Assert.ThrowsAsync<ConflictException>(() => w.Svc.RequestToJoinAsync(other.Id, w.JoinCode, GroupRole.Member, null)); // ombi moja tu
    }

    [Fact]
    public async Task OfficialNumbers_KeepTheLeadersExcelDigits_AndNeverChangeOnceIssued()
    {
        var w = await Build();
        var first = await w.Svc.IssueMissingNumbersAsync(w.GroupId, w.ChairUser);

        var nums = await w.Db.GroupMembers.AsNoTracking().Include(m => m.User).ToDictionaryAsync(m => m.User!.FullName, m => m.MemberNumber);
        Assert.Equal($"UMK-{w.Year}-014", nums["FRANK MKOMA"]);
        Assert.Equal($"UMK-{w.Year}-022", nums["TUNGANEGE"]);
        Assert.Equal(4, first.Issued);
        Assert.Equal(nums.Values.Count, nums.Values.Distinct().Count());

        var second = await w.Svc.IssueMissingNumbersAsync(w.GroupId, w.ChairUser);
        Assert.Equal(0, second.Issued);
        Assert.Equal(nums, await w.Db.GroupMembers.AsNoTracking().Include(m => m.User).ToDictionaryAsync(m => m.User!.FullName, m => m.MemberNumber));

        await Assert.ThrowsAsync<ConflictException>(() => w.Svc.SetShortCodeAsync(w.GroupId, "UKW", w.ChairUser)); // kifupi kimefungwa
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.IssueMissingNumbersAsync(w.GroupId, w.TreasurerUser)); // Mwenyekiti tu
    }

    [Fact]
    public async Task LeaderCreatedMember_HasNoLogin_CannotHoldALeadershipSeat_AndPhoneCannotBeReused()
    {
        var w = await Build();
        var row = await w.Svc.AddMemberRecordAsync(w.GroupId, "Bibi Mwajuma", "0788 123 456", null, w.TreasurerUser);

        Assert.False(row.HasLogin);
        Assert.Equal(new[] { "255788123456" }, row.Phones);
        Assert.StartsWith($"UMK-{w.Year}-", row.MemberNumber);
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.ChangeRoleAsync(w.GroupId, row.MemberId, GroupRole.Secretary, w.ChairUser));
        await Assert.ThrowsAsync<ConflictException>(() => w.Svc.AddMemberRecordAsync(w.GroupId, "Mtu Mwingine", "0712805984", null, w.ChairUser));
    }

    [Fact]
    public async Task ChangingRoles_IsChairOnly_AndGivingAwayTheChairIsAHandOver()
    {
        var w = await Build();
        var u = await Register(w, "Katibu Mpya", "0655777666");
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Member, null);
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.ChairUser);
        var memberId = (await w.Db.GroupMembers.SingleAsync(m => m.UserId == u.Id)).Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.ChangeRoleAsync(w.GroupId, memberId, GroupRole.Secretary, w.TreasurerUser));
        Assert.Equal("Secretary", (await w.Svc.ChangeRoleAsync(w.GroupId, memberId, GroupRole.Secretary, w.ChairUser)).Role);

        await w.Svc.ChangeRoleAsync(w.GroupId, memberId, GroupRole.Chairperson, w.ChairUser);
        var roles = await w.Db.GroupMembers.AsNoTracking().Where(m => m.GroupId == w.GroupId).ToListAsync();
        Assert.Single(roles, m => m.Role == GroupRole.Chairperson);
        Assert.Equal(GroupRole.Member, roles.Single(m => m.UserId == w.ChairUser).Role);
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.MemberRoleChanged && a.Reason!.Contains("Uenyekiti umekabidhiwa"));
    }

    [Fact]
    public async Task CreatingAGroup_MakesYouTheChair_WithAnOfficialNumberAndAJoinCode()
    {
        var w = await Build();
        var u = await Register(w, "Mwanzilishi", "0655444555");
        var g = await w.Svc.CreateGroupAsync(u.Id, "Vijana Kazi", null, GroupType.MonthlySavings, "vkz");

        Assert.Equal("VKZ", g.ShortCode);
        Assert.Matches("^VKZ-[A-Z2-9]{5}$", g.JoinCode);
        Assert.Equal($"VKZ-{w.Year}-001", g.ChairMemberNumber);
        var chair = await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.GroupId == g.GroupId);
        Assert.Equal(GroupRole.Chairperson, chair.Role);
        var audit = await w.Db.AuditEvents.SingleAsync(a => a.GroupId == g.GroupId);
        Assert.Equal("Chairperson", audit.ActorRole);
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.CreateGroupAsync(u.Id, "Kikundi", null, GroupType.MonthlySavings, "1AB"));
    }
}
