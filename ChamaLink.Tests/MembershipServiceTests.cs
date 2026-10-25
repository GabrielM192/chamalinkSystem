using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Common;
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
            db.MemberPhones.Add(new MemberPhone { Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = m.Id, Phone = phone, AddedByUserId = chair.Id,
                Status = TruthApprovalStatus.Approved });
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
    public async Task S17_Link_Needs_An_Approved_Phone_Except_Bootstrap_With_A_Single_Leader_And_Phone_Stays_Pending()
    {
        var w = await Build();
        var ph = await w.Db.MemberPhones.SingleAsync(p => p.GroupMemberId == w.Frank);
        ph.Status = TruthApprovalStatus.Pending;
        await w.Db.SaveChangesAsync();
        var frank = await Register(w, "Frank Mkoma", "0712805984");
        var req = await w.Svc.RequestToJoinAsync(frank.Id, w.JoinCode, GroupRole.Member, null);
        // Viongozi wawili wapo => namba lazima iidhinishwe kwanza (hakuna njia ya mkato)
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, w.Frank, null, w.ChairUser));

        // Kikundi kinaanzishwa na kiongozi MMOJA: namba inayosubiri inayolingana inatosha kuunganisha
        var t = await w.Db.GroupMembers.SingleAsync(m => m.UserId == w.TreasurerUser);
        t.Role = GroupRole.Member;
        await w.Db.SaveChangesAsync();
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, w.Frank, null, w.ChairUser);
        Assert.Equal(frank.Id, (await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == w.Frank)).UserId);
        Assert.Equal(TruthApprovalStatus.Pending, (await w.Db.MemberPhones.AsNoTracking().SingleAsync(p => p.Id == ph.Id)).Status); // malipo bado hayahesabiwi
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.MemberLinkedToAccount && a.Reason!.Contains("kuanzisha kikundi"));

        // Namba ISIYOLINGANA haitoshi hata wakati wa kuanzisha
        var other = await Register(w, "Mtu Mwingine", "0655000111");
        var r2 = await w.Svc.RequestToJoinAsync(other.Id, w.JoinCode, GroupRole.Member, null);
        await Assert.ThrowsAsync<ValidationException>(() => w.Svc.DecideRequestAsync(w.GroupId, r2.Id, true, w.Tunganege, null, w.ChairUser));
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
    public async Task JoinRequest_ForALeaderSeat_GrantsMemberOnly_SeatNeedsAVote_AndDeputiesAreAllowed()
    {
        // SASISHO17 (NEW-2): ombi la kujiunga "kama Mtunza-hazina" ni NIA tu — idhini inatoa Mwanachama; cheo kinapatikana kwa kura
        var w = await Build();
        var u = await Register(w, "Naibu Hazina", "0655123456");
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Treasurer, "Naibu mtunza-hazina");
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.TreasurerUser);
        var m = await w.Db.GroupMembers.AsNoTracking().SingleAsync(x => x.UserId == u.Id);
        Assert.Equal(GroupRole.Member, m.Role);
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Reason != null && a.Reason.Contains("inahitaji pendekezo na kura"));

        var roles = Roles(w);
        var p = await roles.ProposeAsync(w.GroupId, m.Id, GroupRole.Treasurer, "Naibu mtunza-hazina (mkutano)", w.ChairUser);
        await roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, w.TreasurerUser);
        Assert.Equal(2, await w.Db.GroupMembers.CountAsync(x => x.GroupId == w.GroupId && x.Role == GroupRole.Treasurer)); // manaibu
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
        // Namba iliyowekwa na kiongozi mmoja INASUBIRI kiongozi wa pili — haihesabu malipo wala haionyeshwi kama hai
        Assert.Empty(row.Phones);
        var pending = await w.Db.MemberPhones.SingleAsync(p => p.GroupMemberId == row.MemberId);
        Assert.Equal(TruthApprovalStatus.Pending, pending.Status);
        var setup = new TruthSetupService(w.Db, new NoParser(), new TruthLedgerService(w.Db));
        await Assert.ThrowsAnyAsync<Exception>(() => setup.DecidePhoneAsync(w.GroupId, pending.Id, true, w.TreasurerUser, null));
        await setup.DecidePhoneAsync(w.GroupId, pending.Id, true, w.ChairUser, null);
        row = (await w.Svc.MembersAsync(w.GroupId, w.ChairUser)).Single(m => m.MemberId == row.MemberId);
        Assert.Equal(new[] { "255788123456" }, row.Phones);
        Assert.StartsWith($"UMK-{w.Year}-", row.MemberNumber);
        await Assert.ThrowsAsync<ValidationException>(() => Roles(w).ProposeAsync(w.GroupId, row.MemberId, GroupRole.Secretary, "Uamuzi wa mkutano", w.ChairUser));
        await Assert.ThrowsAsync<ConflictException>(() => w.Svc.AddMemberRecordAsync(w.GroupId, "Mtu Mwingine", "0712805984", null, w.ChairUser));
    }

    private static RoleChangeService Roles(W w) => new(w.Db, new ChamaLink.Infrastructure.Services.Benefits.NotificationService(w.Db));

    [Fact]
    public async Task ChangingRoles_NeedsAVoteBySomeoneElse_AndGivingAwayTheChairIsAHandOver()
    {
        // SASISHO17 (NEW-2): hakuna mtu mmoja — hata Mwenyekiti — anayebadilisha cheo; pendekezo + kura ya mtu mwingine
        var w = await Build();
        var u = await Register(w, "Katibu Mpya", "0655777666");
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Member, null);
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.ChairUser);
        var memberId = (await w.Db.GroupMembers.SingleAsync(m => m.UserId == u.Id)).Id;
        var roles = Roles(w);

        // mwanachama wa kawaida hawezi kujipa cheo wala kupendekeza wakati viongozi wapo
        await Assert.ThrowsAsync<ForbiddenException>(() => roles.ProposeAsync(w.GroupId, memberId, GroupRole.Chairperson, "Najipendekeza mwenyewe", u.Id));
        await Assert.ThrowsAsync<ValidationException>(() => roles.ProposeAsync(w.GroupId, memberId, GroupRole.Secretary, "", w.ChairUser));

        var p = await roles.ProposeAsync(w.GroupId, memberId, GroupRole.Secretary, "Uamuzi wa mkutano wa 01/09", w.ChairUser);
        Assert.False(p.Applied);
        Assert.Equal(GroupRole.Member, (await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == memberId)).Role);   // bado hakuna mabadiliko
        await Assert.ThrowsAsync<ForbiddenException>(() => roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, w.ChairUser)); // aliyependekeza
        await Assert.ThrowsAsync<ForbiddenException>(() => roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, u.Id));        // mhusika
        var done = await roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, w.TreasurerUser);
        Assert.Equal(ProposalStatus.Approved, done.Status);
        Assert.Equal(GroupRole.Secretary, (await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == memberId)).Role);

        // Kukabidhi Uenyekiti: pendekezo la mtunza-hazina, kura ya mwenyekiti aliyepo
        var h = await roles.ProposeAsync(w.GroupId, memberId, GroupRole.Chairperson, "Uchaguzi wa mkutano mkuu", w.TreasurerUser);
        await roles.VoteAsync(w.GroupId, h.RequestId!.Value, true, null, w.ChairUser);
        var all = await w.Db.GroupMembers.AsNoTracking().Where(m => m.GroupId == w.GroupId).ToListAsync();
        Assert.Single(all, m => m.Role == GroupRole.Chairperson);
        Assert.Equal(GroupRole.Member, all.Single(m => m.UserId == w.ChairUser).Role);
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.MemberRoleChanged && a.Reason!.Contains("Uenyekiti umekabidhiwa"));
        Assert.Contains(await w.Db.AuditEvents.ToListAsync(), a => a.Action == AuditAction.RoleChangeApproved);
    }

    [Fact]
    public async Task RoleChange_PolicyApprovals_AndCoolingOff_AreHonoured()
    {
        var w = await Build();
        w.Db.TruthGroupSettings.Add(new TruthGroupSettings { GroupId = w.GroupId, MonthlyContribution = 10_000m, RoleChangeApprovals = 2, LeaderCoolingOffDays = 7 });
        await w.Db.SaveChangesAsync();
        var u = await Register(w, "Katibu Mpya", "0655777666");
        var req = await w.Svc.RequestToJoinAsync(u.Id, w.JoinCode, GroupRole.Member, null);
        await w.Svc.DecideRequestAsync(w.GroupId, req.Id, true, null, null, w.ChairUser);
        var memberId = (await w.Db.GroupMembers.SingleAsync(m => m.UserId == u.Id)).Id;
        // viongozi wapiga kura ni 1 tu (mtunza-hazina) < 2 → wanachama wenye akaunti zilizokomaa; Katibu Mpya ni mhusika → hakuna wa kutosha
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Roles(w).ProposeAsync(w.GroupId, memberId, GroupRole.Secretary, "Uamuzi wa mkutano", w.ChairUser));
        // ujumbe unatafsirika: kiolezo kimoja kamili chenye {0},{1} (si maandishi yaliyokwisha jazwa)
        Assert.StartsWith("Hakuna wapiga kura wa kutosha: wanaoruhusiwa {0}, wanaohitajika {1}. Aliyependekeza", ex.Template);
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

    // =====================================================================================================
    // SASISHO17 (pentest NEW-1/NEW-2, mapitio ya nje): mnyororo KAMILI wa Sybil kama jaribio la kudumu.
    // Mwenyekiti → akaunti bandia → anaziidhinisha kujiunga → anapendekeza bandia kuwa kiongozi → bandia nyingine inapiga kura.
    // =====================================================================================================

    private async Task<(Guid Sock1, Guid Sock1User, Guid Sock2User)> SocksJoin(W w, Guid groupId, string joinCode, Guid chairUser)
    {
        var s1 = await Register(w, "Bandia Moja", "0655100001");
        var s2 = await Register(w, "Bandia Mbili", "0655100002");
        foreach (var u in new[] { s1, s2 })
        {
            var req = await w.Svc.RequestToJoinAsync(u.Id, joinCode, GroupRole.Member, null);
            await w.Svc.DecideRequestAsync(groupId, req.Id, true, null, null, chairUser);   // mwenyekiti anaidhinisha mwenyewe
        }
        var m1 = await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.GroupId == groupId && m.UserId == s1.Id);
        return (m1.Id, s1.Id, s2.Id);
    }

    [Fact]
    public async Task Sybil_Chain_Fails_When_The_Group_Has_A_Real_Second_Leader()
    {
        var w = await Build();                                   // Mwenyekiti + Mtunza-hazina halisi
        var (sock1, sock1User, sock2User) = await SocksJoin(w, w.GroupId, w.JoinCode, w.ChairUser);
        var roles = Roles(w);

        var p = await roles.ProposeAsync(w.GroupId, sock1, GroupRole.Treasurer, "Naibu mtunza-hazina", w.ChairUser);
        Assert.False(p.Applied);                                 // pendekezo si mabadiliko
        // Kiongozi halisi yupo → kura ni za VIONGOZI tu: bandia ya pili haiwezi kupiga kura, wala bandia ya kwanza (mhusika)
        await Assert.ThrowsAsync<ForbiddenException>(() => roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, sock2User));
        await Assert.ThrowsAsync<ForbiddenException>(() => roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, sock1User));
        await Assert.ThrowsAsync<ForbiddenException>(() => roles.VoteAsync(w.GroupId, p.RequestId!.Value, true, null, w.ChairUser));
        var s1 = await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == sock1);
        Assert.Equal(GroupRole.Member, s1.Role);
        Assert.False(MemberAccess.IsEffectiveLeader(s1));       // hawezi kuwa "mkaguzi wa pili" wa malipo ya mwenyekiti
        // Kiongozi halisi akikataa → pendekezo limekufa
        var done = await roles.VoteAsync(w.GroupId, p.RequestId!.Value, false, "Sijui mtu huyu", w.TreasurerUser);
        Assert.Equal(ProposalStatus.Rejected, done.Status);
    }

    [Fact]
    public async Task Sybil_Chain_At_Bootstrap_Is_Blocked_By_The_CoolingOff_Policy()
    {
        var w = await Build();
        var founder = await Register(w, "Mwanzilishi Pekee", "0655100009");
        var g = await w.Svc.CreateGroupAsync(founder.Id, "Kikundi Kipya", null, GroupType.MonthlySavings, "KPY");
        w.Db.TruthGroupSettings.Add(new TruthGroupSettings { GroupId = g.GroupId, MonthlyContribution = 10_000m, LeaderCoolingOffDays = 7 });
        await w.Db.SaveChangesAsync();
        var (sock1, _, _) = await SocksJoin(w, g.GroupId, g.JoinCode, founder.Id);

        // Hakuna kiongozi mwingine → kura za wanachama; akaunti zilizounganishwa leo hazijakomaa (siku 7) → hakuna wapiga kura
        await Assert.ThrowsAsync<ConflictException>(() => Roles(w).ProposeAsync(g.GroupId, sock1, GroupRole.Treasurer, "Mtunza-hazina wa kwanza", founder.Id));
        Assert.Equal(GroupRole.Member, (await w.Db.GroupMembers.AsNoTracking().SingleAsync(m => m.Id == sock1)).Role);
    }

    [Fact]
    public async Task Sybil_Chain_At_Bootstrap_Without_CoolingOff_Is_The_Documented_Residual_Risk_And_Is_Loud()
    {
        // HATARI ILIYOBAKI (B-1 kwenye ripoti): mwenyekiti PEKEE + sera ya kusubiri = 0 → bandia inaweza kupigiwa kura na bandia.
        // Jaribio hili linaonyesha hali halisi ili isisahaulike; likibadilika (kinga mpya), lisasishwe kwa makusudi.
        var w = await Build();
        var founder = await Register(w, "Mwanzilishi Pekee", "0655100009");
        var g = await w.Svc.CreateGroupAsync(founder.Id, "Kikundi Kipya", null, GroupType.MonthlySavings, "KPY");
        var (sock1, _, sock2User) = await SocksJoin(w, g.GroupId, g.JoinCode, founder.Id);
        var roles = Roles(w);

        var p = await roles.ProposeAsync(g.GroupId, sock1, GroupRole.Treasurer, "Mtunza-hazina wa kwanza", founder.Id);
        var done = await roles.VoteAsync(g.GroupId, p.RequestId!.Value, true, null, sock2User);
        Assert.Equal(ProposalStatus.Approved, done.Status);
        // Kinga zilizopo: kila mwanachama anaarifiwa na ukaguzi hauwezi kufutwa
        Assert.Contains(await w.Db.AuditEvents.Where(a => a.GroupId == g.GroupId).ToListAsync(), a => a.Action == AuditAction.RoleChangeApproved);
        Assert.True(await w.Db.MemberNotifications.AnyAsync(n => n.GroupId == g.GroupId && n.Kind == "RoleProposed"));
    }
}
