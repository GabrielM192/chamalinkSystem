using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Events;
using ChamaLink.Infrastructure.Services.Sms;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO21 (Sehemu ya 2) — LANGO LA KUJIUNGA na SMS ZA NO-REPLY.
///
/// Kila jaribio linathibitisha kanuni moja:
///   1. Kikundi kinachowasha lango hakiundii mtu uanachama bila malipo.
///   2. Ombi lisilolipwa halizai DENI — linaisha tu (Expired).
///   3. Malipo yanathibitishwa kwa kodi yake (J3-JOIN-0001), kisha kiongozi anakamilisha.
///   4. Pesa ya kujiunga inagawanywa: kianzio kwanza, kilichobaki AKIBA (sera ya kikundi).
///   5. SMS haitumiwi nje ya saa za utulivu — inasubiri.
///   6. Salio la credits linapungua kwa kila SMS; lisipotosha, ujumbe unazuiwa.
///   7. Kikomo cha SMS kwa mwanachama kwa tukio kinaheshimiwa.
///   8. Ujumbe hauna kiasi cha mtu mwingine wala jina la marehemu.
/// </summary>
public class Sasisho21JoiningAndSmsTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class NoParser : ChamaLink.Application.Interfaces.IMKobaParserService
    {
        public Task<List<ChamaLink.Application.DTOs.MKobaTransactionItemDto>> ParseStatementAsync(Stream s)
            => Task.FromResult(new List<ChamaLink.Application.DTOs.MKobaTransactionItemDto>());
        public Task<ChamaLink.Application.DTOs.MKobaParseResult> ParseStatementDetailedAsync(Stream s)
            => Task.FromResult(new ChamaLink.Application.DTOs.MKobaParseResult());
    }

    private sealed class FakeGateway : ISmsGateway
    {
        public readonly List<(string Phone, string Body)> Sent = new();
        public bool Fail { get; set; }
        public string Name => "Fake";

        public Task<SmsSendResult> SendAsync(string phone, string body, CancellationToken ct = default)
        {
            if (Fail) return Task.FromResult(SmsSendResult.Fail("mtandao umekataa"));
            Sent.Add((phone, body));
            return Task.FromResult(SmsSendResult.Ok($"REF-{Sent.Count:000}"));
        }
    }

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required MembershipService Membership;
        public required SmsService Sms;
        public required FakeGateway Gateway;
        public required PaymentInboxService Inbox;
        public required EventWalletService Wallet;
        public Guid GroupId;
        public string JoinCode = "J3JOIN";
        public readonly Dictionary<string, (Guid MemberId, Guid UserId, string Phone)> P = new();
        public Guid M(string n) => P[n].MemberId;
        public Guid U(string n) => P[n].UserId;
    }

    private static async Task<W> Build(decimal joiningFee = 30_000m, decimal savingsPortion = 20_000m,
        bool gate = true, int windowDays = 14, bool smsEnabled = true, int credits = 0,
        int quietFrom = 0, int quietTo = 24, int capPerEvent = 0, decimal floor = 20_000m)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var truth = new TruthLedgerService(db);
        var notify = new NotificationService(db);
        var gateway = new FakeGateway();
        var sms = new SmsService(db, gateway, notify, NullLogger<SmsService>.Instance);
        var wallet = new EventWalletService(db, truth, sms);
        var inbox = new PaymentInboxService(db, wallet, notify, sms);
        var setup = new TruthSetupService(db, new NoParser(), truth);

        var w = new W
        {
            Db = db, Sms = sms, Gateway = gateway, Inbox = inbox, Wallet = wallet,
            Membership = new MembershipService(db, setup, inbox, sms),
            GroupId = Guid.NewGuid()
        };

        db.Groups.Add(new Group
        {
            Id = w.GroupId, Name = "J3 Together Forever", ShortCode = "J3", Type = GroupType.EventBased,
            JoinCode = w.JoinCode
        });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 0m, LateFine = 0m, JoiningFee = joiningFee,
            DueDateDay = 5, GracePeriodDays = 0, ContributionStartDate = Jan1,
            MemberMinimumBalance = floor, EventTopUpWindowDays = 5, OverdueEventFineAmount = 5_000m,
            JoiningPaymentRequired = gate, JoiningSavingsPortion = savingsPortion, JoiningPaymentWindowDays = windowDays,
            SmsEnabled = smsEnabled, SmsQuietFromHour = quietFrom, SmsQuietToHour = quietTo,
            SmsMaxPerMemberPerEvent = capPerEvent, SmsLowCreditThreshold = 5
        });
        db.SmsAccounts.Add(new SmsAccount { GroupId = w.GroupId, Credits = credits });

        var i = 0;
        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer),
                     ("Secretary", GroupRole.Secretary), ("M1", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            var phone = $"2557100000{i++:00}";
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = phone });
            db.GroupMembers.Add(new GroupMember
            {
                Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = $"J3-2026-{i:000}",
                Role = role, Status = MemberStatus.Active, JoinedAt = Jan1
            });
            db.MemberPhones.Add(new MemberPhone
            {
                Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = mid, Phone = phone,
                AddedByUserId = uid, Status = TruthApprovalStatus.Approved
            });
            w.P[name] = (mid, uid, phone);
        }

        await db.SaveChangesAsync();
        return w;
    }

    private static async Task<(Guid UserId, string Phone)> NewApplicant(W w, string name, string phone)
    {
        var uid = Guid.NewGuid();
        w.Db.Users.Add(new User
        {
            Id = uid, FullName = name, PhoneNumber = phone,
            Email = $"{name.Replace(' ', '.').ToLowerInvariant()}.{uid:N}@mfano.co.tz",
            PasswordHash = "hash-ya-majaribio"
        });
        await w.Db.SaveChangesAsync();
        return (uid, phone);
    }

    // ==================================================================
    // 1-2. LANGO
    // ==================================================================

    [Fact]
    public async Task Lango_La_Kujiunga_Linatoa_Kodi_Na_Kiasi()
    {
        var w = await Build();
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000001");

        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);

        Assert.Equal(MembershipRequestStatus.AwaitingPayment, req.Status);
        Assert.Equal(30_000m, req.PaymentAmount);
        Assert.Equal("J3-JOIN-0001", req.PaymentCode);
        Assert.NotNull(req.PaymentDeadline);
        // Bado hajasajiliwa kama mwanachama
        Assert.False(await w.Db.GroupMembers.AnyAsync(m => m.UserId == uid));
    }

    [Fact]
    public async Task Kiongozi_Hawezi_Kukubali_Ombi_Lisilolipwa()
    {
        var w = await Build();
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000002");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            w.Membership.DecideRequestAsync(w.GroupId, req.Id, true, null, "sawa", w.U("Chair")));
        Assert.Contains("Malipo", ex.Message);
        Assert.False(await w.Db.GroupMembers.AnyAsync(m => m.UserId == uid));
    }

    [Fact]
    public async Task Ombi_Lisilolipwa_Linaisha_Bila_Deni()
    {
        var w = await Build(windowDays: 14);
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000003");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);

        // Muda umepita
        var tracked = await w.Db.MembershipRequests.FirstAsync(r => r.Id == req.Id);
        tracked.PaymentDeadline = DateTime.UtcNow.AddDays(-1);
        await w.Db.SaveChangesAsync();

        await w.Membership.PendingRequestsAsync(w.GroupId, w.U("Chair"));

        var after = await w.Db.MembershipRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(MembershipRequestStatus.Expired, after.Status);
        // Hakuna deni: hakuna wajibu, hakuna FinancialEvent ya aina yoyote ya deni
        Assert.Equal(0, await w.Db.EventObligations.CountAsync(o => o.GroupMemberId == w.M("Chair")) - await w.Db.EventObligations.CountAsync());
    }

    [Fact]
    public async Task Muombaji_Anaweza_Kufuta_Ombi_Lililopo_Kwenye_Lango_La_Malipo()
    {
        var w = await Build(windowDays: 14);
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000005");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);
        Assert.Equal(MembershipRequestStatus.AwaitingPayment, req.Status);

        await w.Membership.CancelMyRequestAsync(uid, req.Id);

        var after = await w.Db.MembershipRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(MembershipRequestStatus.Cancelled, after.Status);
        // Kufuta ombi HAKUNADI deni — muombaji hakuwa mwanachama bado.
        Assert.Equal(0, await w.Db.EventObligations.CountAsync(o => o.GroupId == w.GroupId));
    }

    [Fact]
    public async Task Muombaji_Hawezi_Kufuta_Ombi_La_Mtu_Mwingine()
    {
        var w = await Build(windowDays: 14);
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000006");
        var (other, _) = await NewApplicant(w, "Mgeni Pili", "255799000007");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);

        await Assert.ThrowsAsync<NotFoundException>(() => w.Membership.CancelMyRequestAsync(other, req.Id));
        var after = await w.Db.MembershipRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(MembershipRequestStatus.AwaitingPayment, after.Status);
    }

    [Fact]
    public async Task Kikundi_Kisichowasha_Lango_Kinaendelea_Kama_Zamani()
    {
        var w = await Build(gate: false);
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000004");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);
        Assert.Equal(MembershipRequestStatus.Pending, req.Status);
        Assert.Null(req.PaymentCode);
    }

    // ==================================================================
    // 3-4. MALIPO YA KUJIUNGA
    // ==================================================================

    [Fact]
    public async Task Malipo_Ya_Kujiunga_Yanathibitishwa_Kwa_Kodi()
    {
        var w = await Build();
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000005");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);

        w.Db.MkobaRawTransactions.Add(new MkobaRawTransaction
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = "JOIN-1", Timestamp = DateTime.UtcNow,
            Phone = "255799000005", PayerName = "Mgeni Mmoja", Amount = 30_000m, IsWithdrawal = false,
            Description = "ADA YA KUJIUNGA J3-JOIN-0001", StatementFileHash = "h"
        });
        await w.Db.SaveChangesAsync();

        await w.Inbox.SyncAsync(w.GroupId);
        var receipt = await w.Db.PaymentReceipts.FirstAsync(r => r.Reference == "JOIN-1");
        Assert.Equal(PaymentKind.Joining, receipt.Kind);
        Assert.Equal("JoinCode", receipt.MatchRule);
        Assert.Equal(req.Id, receipt.MembershipRequestId);

        await w.Inbox.ConfirmAsync(w.GroupId, receipt.Id, Guid.Empty, "ameleta risiti", w.U("Treasurer"));

        var paid = await w.Db.MembershipRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(MembershipRequestStatus.Paid, paid.Status);
        // Bado hajasajiliwa — kiongozi anakamilisha
        Assert.False(await w.Db.GroupMembers.AnyAsync(m => m.UserId == uid));
    }

    [Fact]
    public async Task Baada_Ya_Malipo_Kiongozi_Anakamilisha_Na_Akiba_Inatokea()
    {
        var w = await Build();
        var (uid, _) = await NewApplicant(w, "Mgeni Mmoja", "255799000006");
        var req = await w.Membership.RequestToJoinAsync(uid, w.JoinCode, GroupRole.Member, null);

        w.Db.MkobaRawTransactions.Add(new MkobaRawTransaction
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, Reference = "JOIN-2", Timestamp = DateTime.UtcNow,
            Phone = "255799000006", PayerName = "Mgeni Mmoja", Amount = 30_000m, IsWithdrawal = false,
            Description = "J3-JOIN-0001", StatementFileHash = "h"
        });
        await w.Db.SaveChangesAsync();
        await w.Inbox.SyncAsync(w.GroupId);
        var receipt = await w.Db.PaymentReceipts.FirstAsync(r => r.Reference == "JOIN-2");
        await w.Inbox.ConfirmAsync(w.GroupId, receipt.Id, Guid.Empty, null, w.U("Treasurer"));

        await w.Membership.DecideRequestAsync(w.GroupId, req.Id, true, null, "Karibu", w.U("Chair"));

        var member = await w.Db.GroupMembers.AsNoTracking().FirstAsync(m => m.UserId == uid);
        var approved = await w.Db.MembershipRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(MembershipRequestStatus.Approved, approved.Status);
        Assert.Equal(member.Id, approved.LinkedMemberId);

        // Pesa imehamishiwa kwa rekodi mpya
        var tx = await w.Db.MkobaRawTransactions.AsNoTracking().FirstAsync(t => t.Reference == "JOIN-2");
        Assert.Equal(member.Id, tx.AttributedGroupMemberId);

        // 30,000 = 10,000 kianzio + 20,000 akiba (sera ya kikundi, si namba ndani ya msimbo)
        var t = await new TruthLedgerService(w.Db).GetMemberTruthAsync(member.Id);
        Assert.Equal(20_000m, t.Statement.Savings);
    }

    // ==================================================================
    // 5-8. SMS
    // ==================================================================

    [Fact]
    public async Task Sms_Haitumwi_Kama_Kikundi_Hakijawasha()
    {
        var w = await Build(smsEnabled: false, credits: 100);
        var queued = await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.Reminder,
            new Dictionary<string, string> { ["kikundi"] = "J3", ["siku"] = "2", ["kiasi"] = "10,000", ["kodi"] = "J3-E01-0004", ["tarehe"] = "25/10/2026" });
        Assert.Equal(0, queued);
        Assert.Equal(0, await w.Db.SmsMessages.CountAsync());
    }

    [Fact]
    public async Task Sms_Inatumwa_Na_Salio_Linapungua()
    {
        var w = await Build(credits: 10);
        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.EventAnnouncement,
            new Dictionary<string, string>
            {
                ["kikundi"] = "J3", ["kiasi"] = "10,000", ["tarehe"] = "28/10/2026", ["kodi"] = "J3-E07-0004"
            });

        var r = await w.Sms.DispatchAsync();

        Assert.Equal(1, r.Sent);
        Assert.Single(w.Gateway.Sent);
        var acc = await w.Sms.AccountAsync(w.GroupId);
        Assert.Equal(9, acc.Credits);
        var msg = await w.Db.SmsMessages.AsNoTracking().FirstAsync();
        Assert.Equal(SmsStatus.Sent, msg.Status);
        Assert.NotNull(msg.ProviderReference);
    }

    [Fact]
    public async Task Salio_Lisipotosha_Ujumbe_Unazuiwa()
    {
        var w = await Build(credits: 0);
        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.PaymentConfirmed,
            new Dictionary<string, string>
            {
                ["kikundi"] = "J3", ["kiasi"] = "10,000", ["salio"] = "20,000", ["kodi"] = "-"
            });

        // Salio halitoshi: ujumbe unazuiwa WAKATI WA KUPANGA (haupotei kimya kimya —
        // unaonekana kwenye kumbukumbu na kiongozi anajulishwa).
        var r = await w.Sms.DispatchAsync();

        Assert.Equal(0, r.Sent);
        Assert.Equal(0, r.Attempted);
        var msg = await w.Db.SmsMessages.AsNoTracking().FirstAsync();
        Assert.Equal(SmsStatus.Suppressed, msg.Status);
        Assert.Contains("Salio", msg.SuppressedReason!);
    }

    [Fact]
    public async Task Sms_Haitumwi_Nje_Ya_Saa_Za_Utulivu()
    {
        // Saa za utulivu: 8 hadi 20 EAT. Tunajaribu kwa saa ya sasa ya EAT.
        var w = await Build(credits: 50, quietFrom: 8, quietTo: 20);
        var eatHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MkobaPlatform.ResolveTimeZone()).Hour;

        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.Reminder,
            new Dictionary<string, string>
            {
                ["kikundi"] = "J3", ["siku"] = "2", ["kiasi"] = "10,000", ["kodi"] = "J3-E07-0004", ["tarehe"] = "28/10/2026"
            });

        var msg = await w.Db.SmsMessages.AsNoTracking().FirstAsync();
        var outside = eatHour < 8 || eatHour >= 20;
        Assert.Equal(outside, msg.SendAfter is not null);

        var r = await w.Sms.DispatchAsync();
        if (outside) Assert.Equal(0, r.Sent);   // inasubiri, haikataliwa
        else Assert.Equal(1, r.Sent);
    }

    [Fact]
    public async Task Kikomo_Cha_Sms_Kwa_Tukio_Kinaheshimiwa()
    {
        var w = await Build(credits: 50, capPerEvent: 1);
        var caseId = Guid.NewGuid();
        var values = new Dictionary<string, string>
        {
            ["kikundi"] = "J3", ["kiasi"] = "10,000", ["tarehe"] = "28/10/2026", ["kodi"] = "J3-E07-0004"
        };

        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.EventAnnouncement, values,
            nameof(BenefitCase), caseId);
        var second = await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.EventAnnouncement, values,
            nameof(BenefitCase), caseId);

        Assert.Equal(0, second);
        Assert.Equal(1, await w.Db.SmsMessages.CountAsync());
    }

    [Fact]
    public async Task Ujumbe_Hauna_Jina_La_Marehemu_Wala_Kiasi_Cha_Mwenzake()
    {
        var w = await Build(credits: 10);
        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.EventAnnouncement,
            new Dictionary<string, string>
            {
                ["kikundi"] = "J3 Together Forever", ["kiasi"] = "10,000", ["tarehe"] = "28/10/2026", ["kodi"] = "J3-E07-0004"
            });

        var body = (await w.Db.SmsMessages.AsNoTracking().FirstAsync()).Body;
        Assert.Contains("J3 Together Forever", body);
        Assert.Contains("10,000", body);
        Assert.Contains("J3-E07-0004", body);
        Assert.DoesNotContain("Mama", body);
        Assert.DoesNotContain("kila kikundi", body);
        Assert.True(body.Length <= 360);
    }

    [Fact]
    public async Task Mwanachama_Hana_Namba_Iliyoidhinishwa_Sms_Inarekodiwa_Kama_Iliyozuiwa()
    {
        var w = await Build(credits: 10);
        var phone = await w.Db.MemberPhones.FirstAsync(p => p.GroupMemberId == w.M("M1"));
        phone.Status = TruthApprovalStatus.Pending;
        await w.Db.SaveChangesAsync();

        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.Reminder,
            new Dictionary<string, string> { ["kikundi"] = "J3", ["siku"] = "2", ["kiasi"] = "1,000", ["kodi"] = "-", ["tarehe"] = "28/10/2026" });

        var msg = await w.Db.SmsMessages.AsNoTracking().FirstAsync();
        Assert.Equal(SmsStatus.Suppressed, msg.Status);
        Assert.Contains("namba", msg.SuppressedReason!);
    }

    [Fact]
    public async Task Credits_Zinaongezwa_Na_Kiongozi_Tu()
    {
        var w = await Build(credits: 0);
        var acc = await w.Sms.TopUpAsync(w.GroupId, 500, "Tuliponunua SMS 500", w.U("Treasurer"));
        Assert.Equal(500, acc.Credits);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Sms.TopUpAsync(w.GroupId, 100, null, w.U("M1")));
    }

    [Fact]
    public async Task Mtandao_Ukikataa_Ujumbe_Unajaribiwa_Tena()
    {
        var w = await Build(credits: 10);
        w.Gateway.Fail = true;
        await w.Sms.QueueAsync(w.GroupId, new[] { w.M("M1") }, SmsService.Templates.Reminder,
            new Dictionary<string, string> { ["kikundi"] = "J3", ["siku"] = "2", ["kiasi"] = "10,000", ["kodi"] = "-", ["tarehe"] = "28/10/2026" });

        await w.Sms.DispatchAsync();
        var msg = await w.Db.SmsMessages.AsNoTracking().FirstAsync();
        Assert.Equal(SmsStatus.Queued, msg.Status);   // bado kwenye foleni
        Assert.Equal(1, msg.Attempts);
        Assert.NotNull(msg.SendAfter);                // itajaribiwa tena
    }
}
