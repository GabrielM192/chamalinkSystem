using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Platform;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO20 — MSIMAMIZI WA MFUMO. Kila jaribio linathibitisha KANUNI MOJA ya usalama:
///
///   1. MFA (TOTP) inafuata RFC 6238 — code ya tarakimu 6, dirisha la hatua moja tu.
///   2. Hakuna token kamili ya msimamizi bila MFA (hatua ya kwanza ni token ya dakika 5).
///   3. Mtu asiye msimamizi anapata jibu LILE LILE na neno la siri baya (hakuna uvujaji).
///   4. Mtu aliyeomba HAWEZI kuidhinisha ombi lake (4-eyes halisi).
///   5. Break-glass: SuperAdmin pekee anayeweza kuomba; mwingine anaidhinisha; inaisha saa 24.
///   6. Msimamizi wa mwisho hawezi kuondolewa; support hawezi kuongeza wasimamizi.
///   7. Code ya kureset: matumizi moja, majaribio 5, inafunga vikao vyote vya zamani.
///   8. Taarifa za mwanachama mmoja mmoja zinahitaji break-glass (muhtasari hauhitaji).
///   9. Kikundi kisicho na sera kinaonekana kwenye orodha ya msimamizi (hakuna kikundi "kizima".
/// </summary>
public class Sasisho20PlatformTests
{
    // ------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------
    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required AuthService Auth;
        public required PlatformAuthService PlatformAuth;
        public required PlatformApprovalService Approvals;
        public required PlatformSupportService Support;
        public required PlatformGroupService Groups;
        public required PlatformAuditService Audit;
        public Guid GroupId;

        /// <summary>Msimamizi mpya (User + PlatformAdmin).</summary>
        public async Task<(Guid UserId, Guid AdminId)> AddAdmin(string name, PlatformAdminLevel level, string? password = null, bool mfa = true)
        {
            var userId = Guid.NewGuid();
            var email = $"{name.ToLowerInvariant()}@chamalink.test";
            Db.Users.Add(new User
            {
                Id = userId, FullName = name, Email = email, PhoneNumber = "2557" + Random.Shared.Next(10000000, 99999999),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password ?? "Siri-Nzuri-Sana-123")
            });
            var admin = new PlatformAdmin
            {
                Id = Guid.NewGuid(), UserId = userId, Level = level, IsActive = true,
                MfaEnabledAt = mfa ? DateTime.UtcNow : null
            };
            Db.PlatformAdmins.Add(admin);
            await Db.SaveChangesAsync();
            return (userId, admin.Id);
        }

        public async Task<Guid> AddGroup(string name, bool withPolicy = true)
        {
            var gid = Guid.NewGuid();
            Db.Groups.Add(new Group { Id = gid, Name = name, ShortCode = name[..Math.Min(6, name.Length)].ToUpperInvariant(), Type = GroupType.MonthlySavings });
            if (withPolicy)
                Db.TruthGroupSettings.Add(new TruthGroupSettings
                {
                    GroupId = gid, MonthlyContribution = 10_000m, LateFine = 5_000m, DueDateDay = 5,
                    ContributionStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    JoiningFee = 50_000m, GracePeriodDays = 0
                });
            await Db.SaveChangesAsync();
            return gid;
        }
    }

    private static async Task<W> Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('k', 64),
            ["Jwt:Issuer"] = "chamalink-tests",
            ["Jwt:Audience"] = "chamalink-tests",
            ["Auth:MaxLoginFailures"] = "5",
            ["Auth:MinPasswordLength"] = "8",
            ["Platform:SessionMinutes"] = "60",
            ["Platform:ChallengeMinutes"] = "5",
            ["Platform:BreakGlassHours"] = "24",
            ["Platform:ResetCodeMinutes"] = "30",
            // Siri ya TOTP inasimbwa kwa key hii ndani ya majaribio (production: env var).
            ["Benefits:EvidenceEncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        }).Build();

        var auth = new AuthService(db, cfg);
        var audit = new PlatformAuditService(db);
        var platformAuth = new PlatformAuthService(db, auth, audit, cfg);
        var approvals = new PlatformApprovalService(db, audit, cfg);
        var support = new PlatformSupportService(db, audit, new NotificationService(db), cfg);
        var groups = new PlatformGroupService(db, new TruthLedgerService(db), approvals, cfg);

        return new W
        {
            Db = db, Auth = auth, PlatformAuth = platformAuth, Approvals = approvals,
            Support = support, Groups = groups, Audit = audit
        };
    }

    // ==================================================================
    // 1. TOTP (RFC 6238)
    // ==================================================================
    // Siri ya RFC 6238 Appendix B ni ASCII "12345678901234567890" = Base32 ifuatayo.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private static DateTime Unix(long seconds) => DateTime.UnixEpoch.AddSeconds(seconds);

    [Theory]
    [InlineData(59L, "287082")]             // 94287082 → tarakimu 6 za mwisho
    [InlineData(1111111109L, "081804")]     // 07081804
    [InlineData(1111111111L, "050471")]     // 14050471
    [InlineData(1234567890L, "005924")]     // 89005924
    [InlineData(2000000000L, "279037")]     // 69279037
    [InlineData(20000000000L, "353130")]    // 65353130
    public void Totp_Matches_Rfc6238_Appendix_B(long unixSeconds, string expected)
        => Assert.Equal(expected, TotpService.Code(RfcSecret, Unix(unixSeconds)));

    [Fact]
    public void Totp_Accepts_One_Step_Skew_But_Not_Two()
    {
        var t = Unix(1234567890);
        var secret = TotpService.NewSecret();
        var code = TotpService.Code(secret, t);

        Assert.True(TotpService.Verify(secret, code, t));
        Assert.True(TotpService.Verify(secret, code, t.AddSeconds(30)));    // saa ya simu mbele
        Assert.True(TotpService.Verify(secret, code, t.AddSeconds(-30)));   // saa ya simu nyuma
        Assert.False(TotpService.Verify(secret, code, t.AddSeconds(90)));   // zaidi ya hatua moja
        Assert.False(TotpService.Verify(secret, "000000", t) && code != "000000");
    }

    [Fact]
    public void Totp_Secret_Is_Base32_And_Uri_Is_Scannable()
    {
        var secret = TotpService.NewSecret();
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
        var uri = TotpService.OtpAuthUri("ChamaLink", "msimamizi@chamalink.test", secret);
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={secret}", uri);
        Assert.Contains("issuer=ChamaLink", uri);
    }

    // ==================================================================
    // 2. Kuingia: hatua mbili, hakuna token kamili bila MFA
    // ==================================================================
    [Fact]
    public async Task Seeded_Admin_Must_Enroll_Mfa_And_Only_Then_Gets_Platform_Claims()
    {
        var w = await Build();
        var seeded = await w.PlatformAuth.SeedFirstAdminAsync("mkubwa@chamalink.test", "Neno-Jipya-2026!", "Mkubwa wa Mfumo");
        Assert.Equal(PlatformAdminLevel.SuperAdmin, seeded.Level);

        // Neno la siri tu — bado HAJAFAHAMU MFA.
        var stage1 = await w.PlatformAuth.LoginAsync("mkubwa@chamalink.test", "Neno-Jipya-2026!", "127.0.0.1");
        Assert.Equal("EnrollmentRequired", stage1.Stage);
        Assert.NotNull(stage1.Token);

        var token1 = JwtOf(stage1.Token!);
        Assert.Equal("1", token1["penroll"]);
        Assert.False(token1.ContainsKey(PlatformAuthService.PlatformClaim)); // hana `pa`

        // Kujisajili: siri mpya + uthibitisho wa code.
        var enroll = await w.PlatformAuth.EnrollAsync(seeded.UserId);
        var codeNow = TotpService.Code(enroll.Secret, DateTime.UtcNow);
        var stage2 = await w.PlatformAuth.ConfirmEnrollmentAsync(seeded.UserId, codeNow, "127.0.0.1");
        Assert.Equal("Ready", stage2.Stage);
        Assert.NotNull(stage2.Token);
        var token2 = JwtOf(stage2.Token!);
        Assert.Equal("1", token2[PlatformAuthService.PlatformClaim]);
        Assert.Equal("SuperAdmin", token2[PlatformAuthService.PlatformLevelClaim]);

        // Kuingia tena: sasa ni MFA, si enrollment.
        var stage3 = await w.PlatformAuth.LoginAsync("mkubwa@chamalink.test", "Neno-Jipya-2026!", null);
        Assert.Equal("MfaRequired", stage3.Stage);
        Assert.False(JwtOf(stage3.Token!).ContainsKey(PlatformAuthService.PlatformClaim));

        var stage4 = await w.PlatformAuth.VerifyMfaAsync(seeded.UserId, TotpService.Code(enroll.Secret, DateTime.UtcNow), null);
        Assert.Equal("Ready", stage4.Stage);
        Assert.Equal("1", JwtOf(stage4.Token!)[PlatformAuthService.PlatformClaim]);
    }

    [Fact]
    public async Task Non_Admin_Gets_The_Same_Message_As_A_Wrong_Password()
    {
        var w = await Build();
        await w.AddAdmin("Msimamizi", PlatformAdminLevel.SuperAdmin);

        var userId = Guid.NewGuid();
        w.Db.Users.Add(new User
        {
            Id = userId, FullName = "Kiongozi wa Kawaida", Email = "kiongozi@chamalink.test",
            PhoneNumber = "255712345678", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Siri-Nzuri-Sana-123")
        });
        await w.Db.SaveChangesAsync();

        var wrongPassword = await Assert.ThrowsAsync<ValidationException>(
            () => w.PlatformAuth.LoginAsync("msimamizi@chamalink.test", "si-siri-sahihi", null));
        var notAdmin = await Assert.ThrowsAsync<ValidationException>(
            () => w.PlatformAuth.LoginAsync("kiongozi@chamalink.test", "Siri-Nzuri-Sana-123", null));

        Assert.Equal(wrongPassword.Message, notAdmin.Message);
        // Na jambo limerekodiwa kwa ajili ya mmiliki wa mfumo (bila kumwambia mtu).
        Assert.Contains(w.Db.PlatformAuditEvents, e => e.Action == "login_not_admin");
    }

    // ==================================================================
    // 3. 4-eyes
    // ==================================================================
    [Fact]
    public async Task Requester_Cannot_Approve_Their_Own_Request()
    {
        var w = await Build();
        var (aId, _) = await w.AddAdmin("A", PlatformAdminLevel.SuperAdmin);
        var (bId, _) = await w.AddAdmin("B", PlatformAdminLevel.SuperAdmin);
        var candidate = await AddPlainUser(w, "Msaidizi Mpya", "msaidizi@chamalink.test", "255712399001");

        var approval = await w.Approvals.RequestAdminAsync(aId, PlatformApprovalAction.GrantAdmin, candidate, null, PlatformAdminLevel.Oversight, "Anaenda kusaidia vikundi vya mkoa wa Pwani");
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => w.Approvals.DecideAsync(aId, approval.Id, true, null));
        Assert.Contains("MWINGINE", ex.Message);

        // Mwingine anaweza.
        var done = await w.Approvals.DecideAsync(bId, approval.Id, true, "Nimemchunguza", default);
        Assert.Equal(PlatformApprovalStatus.Approved, done.Status);
        Assert.Contains(w.Db.PlatformAdmins, x => x.UserId == candidate && x.IsActive);
    }

    [Fact]
    public async Task Admin_Changes_Need_A_Super_Admin_As_Second_Pair_Of_Eyes()
    {
        var w = await Build();
        var (supId, _) = await w.AddAdmin("Sup", PlatformAdminLevel.SuperAdmin);
        var (ovsId, _) = await w.AddAdmin("Ovs", PlatformAdminLevel.Oversight);
        var (target, _) = await w.AddAdmin("Target", PlatformAdminLevel.Support);

        var approval = await w.Approvals.RequestAdminAsync(supId, PlatformApprovalAction.ChangeAdminLevel, null, w.Db.PlatformAdmins.Single(a => a.UserId == target).Id, PlatformAdminLevel.SuperAdmin, "Anapandishwa ngazi baada ya mafunzo");

        // Oversight hawezi kuidhinisha mabadiliko ya wasimamizi.
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Approvals.DecideAsync(ovsId, approval.Id, true, null));
    }

    [Fact]
    public async Task Support_Level_Cannot_Request_Admin_Changes()
    {
        var w = await Build();
        var (supportId, _) = await w.AddAdmin("Support", PlatformAdminLevel.Support);
        var (target, _) = await w.AddAdmin("Target", PlatformAdminLevel.Support);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Approvals.RequestAdminAsync(supportId, PlatformApprovalAction.GrantAdmin, target, null, PlatformAdminLevel.SuperAdmin, "Sababu ya kutosha hapa"));
    }

    [Fact]
    public async Task Admin_Cannot_Remove_Themselves_And_The_Last_Two_Cannot_Cancel_Each_Other_Out()
    {
        var w = await Build();
        var (aId, aAdminId) = await w.AddAdmin("A", PlatformAdminLevel.SuperAdmin);
        var (bId, bAdminId) = await w.AddAdmin("B", PlatformAdminLevel.SuperAdmin);
        var (cId, cAdminId) = await w.AddAdmin("C", PlatformAdminLevel.SuperAdmin);

        // 1. Kuomba kuondolewa kwako mwenyewe — hapana. Msimamizi mwingine afanye hivyo.
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Approvals.RequestAdminAsync(aId, PlatformApprovalAction.DeactivateAdmin, null, aAdminId, null, "Nataka kuondoka mfumo leo"));

        // 2. B anaomba kuondoa A; A HAWEZI kuidhinisha kuondolewa kwake (hata akiwa SuperAdmin).
        var p1 = await w.Approvals.RequestAdminAsync(bId, PlatformApprovalAction.DeactivateAdmin, null, aAdminId, null, "Ameacha kazi ya usimamizi wa mfumo");
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Approvals.DecideAsync(aId, p1.Id, true, null));

        // 3. Msimamizi wa tatu (C) anaidhinisha → A anaondolewa; wawili wanabaki.
        Assert.Equal(PlatformApprovalStatus.Approved, (await w.Approvals.DecideAsync(cId, p1.Id, true, "Nimehakiki")).Status);
        Assert.Equal(2, w.Db.PlatformAdmins.Count(x => x.IsActive));

        // 4. Wawili waliobaki hawawezi kumaliza mfumo: kila mmoja ni "requester" au "target"
        //    wa ombi la mwenzake — hakuna wa tatu wa kuidhinisha. Mfumo haubaki bila msimamizi.
        var p2 = await w.Approvals.RequestAdminAsync(cId, PlatformApprovalAction.DeactivateAdmin, null, bAdminId, null, "Kupunguza wasimamizi wawili waliobaki");
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Approvals.DecideAsync(bId, p2.Id, true, null)); // mwenyewe
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Approvals.DecideAsync(cId, p2.Id, true, null)); // aliyeomba
        Assert.Equal(0, w.Db.PlatformAdmins.Count(x => x.Id == cAdminId && !x.IsActive));
        Assert.Equal(2, w.Db.PlatformAdmins.Count(x => x.IsActive));
    }

    // ==================================================================
    // 4. Break-glass
    // ==================================================================
    [Fact]
    public async Task Break_Glass_Needs_Super_Admin_Second_Approval_And_Expires_In_24_Hours()
    {
        var w = await Build();
        var gid = await w.AddGroup("Ukonga Mkoba Wing");
        var (supId, supAdminId) = await w.AddAdmin("Sup", PlatformAdminLevel.SuperAdmin);
        var (ovsId, _) = await w.AddAdmin("Ovs", PlatformAdminLevel.Oversight);
        var (sup2, _) = await w.AddAdmin("Sup2", PlatformAdminLevel.SuperAdmin);

        // 1. Support/Oversight hawawezi kuomba break-glass.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Approvals.RequestBreakGlassAsync(ovsId, gid, "Nahitaji kuona taarifa za mwanachama mmoja mmoja"));

        // 2. SuperAdmin anaomba (sababu inaonekana kwa kikundi — lazima iwe wazi).
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Approvals.RequestBreakGlassAsync(supId, gid, "nataka tu"));
        var req = await w.Approvals.RequestBreakGlassAsync(supId, gid, "Kiongozi amepiga simu: mgogoro wa malipo unahitaji kuchunguzwa kwa kina");

        // 3. Bado hakuna dirisha.
        Assert.False(await w.Approvals.HasBreakGlassAsync(supId, gid));

        // 4. Msimamizi wa pili (SuperAdmin) anaidhinisha → dirisha la saa 24.
        var approved = await w.Approvals.DecideAsync(sup2, req.Id, true, "Nimehakiki sababu na kikundi");
        Assert.Equal(PlatformApprovalStatus.Approved, approved.Status);
        Assert.NotNull(approved.ExpiresAt);
        Assert.InRange(approved.ExpiresAt!.Value, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));
        Assert.True(await w.Approvals.HasBreakGlassAsync(supId, gid));

        // 5. Kumbukumbu: ombi + idhini + (baadaye) matumizi — na kikundi kinaiona.
        Assert.Contains(w.Db.PlatformAuditEvents, e => e.Action == "break_glass_approved" && e.GroupId == gid);
        Assert.Contains(w.Db.AuditEvents, e => e.GroupId == gid && e.Action == AuditAction.PlatformBreakGlassApproved);
    }

    [Fact]
    public async Task Member_Level_Detail_Needs_Break_Glass_But_Summary_Does_Not()
    {
        var w = await Build();
        var gid = await w.AddGroup("J3 Together Forever");
        var (supId, _) = await w.AddAdmin("Sup", PlatformAdminLevel.SuperAdmin);
        var (sup2, _) = await w.AddAdmin("Sup2", PlatformAdminLevel.SuperAdmin);

        // Muhtasari unapatikana bila break-glass (hiyo ndiyo kazi ya kila siku).
        var list = await w.Groups.ListAsync();
        Assert.Contains(list, g => g.Id == gid);
        var detail = await w.Groups.DetailAsync(gid, supId);
        Assert.False(detail.BreakGlassActive);

        // Mwanachama mmoja mmoja — hapana.
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Groups.MembersAsync(gid, supId));

        var req = await w.Approvals.RequestBreakGlassAsync(supId, gid, "Kuchunguza malipo yaliyoripotiwa si sahihi na mwanachama");
        await w.Approvals.DecideAsync(sup2, req.Id, true, "Sawa");

        var members = await w.Groups.MembersAsync(gid, supId);   // sasa inaruhusiwa
        Assert.NotNull(members);
        Assert.Contains(w.Db.PlatformAuditEvents, e => e.Action == "break_glass_used" && e.GroupId == gid);
    }

    // ==================================================================
    // 5. Msaada wa akaunti
    // ==================================================================
    [Fact]
    public async Task Reset_Code_Is_Single_Use_And_Closes_All_Old_Sessions()
    {
        var w = await Build();
        var (adminId, _) = await w.AddAdmin("Msaada", PlatformAdminLevel.Support);
        var leaderUserId = await AddLeader(w, "Juma Kiongozi", "juma@chamalink.test", "255712300001");
        var versionBefore = w.Db.Users.Single(u => u.Id == leaderUserId).TokenVersion;

        var issued = await w.Support.IssueResetCodeAsync(adminId, leaderUserId, null, "Amepiga simu: amesahau neno la siri");
        Assert.Equal(10, issued.Code.Length);

        // Code mpya inafuta ya zamani.
        var issued2 = await w.Support.IssueResetCodeAsync(adminId, leaderUserId, null, "Alipoteza SMS ya kwanza");
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Support.RedeemResetCodeAsync("juma@chamalink.test", issued.Code, "Neno-Jipya-2026!", null));
        Assert.NotEqual(issued.Code, issued2.Code);

        await w.Support.RedeemResetCodeAsync("juma@chamalink.test", issued2.Code, "Neno-Jipya-2026!", "41.0.0.1");

        var user = w.Db.Users.Single(u => u.Id == leaderUserId);
        Assert.True(user.TokenVersion > versionBefore);                       // vikao vya zamani vimefungwa
        Assert.True(BCrypt.Net.BCrypt.Verify("Neno-Jipya-2026!", user.PasswordHash));

        // Mara moja tu.
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Support.RedeemResetCodeAsync("juma@chamalink.test", issued2.Code, "Nyingine-2026!", null));

        // Msimamizi HAJUI neno jipya — hilo linabaki kwa mwenye akaunti.
        Assert.DoesNotContain(w.Db.PlatformAuditEvents, e => e.DetailJson != null && e.DetailJson.Contains("Neno-Jipya-2026!"));
    }

    [Fact]
    public async Task Reset_Code_Dies_After_Five_Wrong_Tries()
    {
        var w = await Build();
        var (adminId, _) = await w.AddAdmin("Msaada", PlatformAdminLevel.Support);
        var leaderUserId = await AddLeader(w, "Asha", "asha@chamalink.test", "255712300002");
        var issued = await w.Support.IssueResetCodeAsync(adminId, leaderUserId, null, "Ameomba msaada wa neno la siri");

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<ValidationException>(() =>
                w.Support.RedeemResetCodeAsync("asha@chamalink.test", "ZZZZZZZZZZ", "Neno-Jipya-2026!", null));

        // Hata code SAHIHI haifanyi kazi tena — imefutwa baada ya majaribio 5.
        await Assert.ThrowsAsync<ValidationException>(() =>
            w.Support.RedeemResetCodeAsync("asha@chamalink.test", issued.Code, "Neno-Jipya-2026!", null));
    }

    [Fact]
    public async Task Unlock_Clears_The_Lockout_And_Leaves_A_Trace()
    {
        var w = await Build();
        var (adminId, _) = await w.AddAdmin("Msaada", PlatformAdminLevel.Support);
        var leaderUserId = await AddLeader(w, "Tito", "tito@chamalink.test", "255712300003");

        var user = w.Db.Users.Single(u => u.Id == leaderUserId);
        user.FailedLoginCount = 7;
        user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(14);
        w.Db.LoginFailures.Add(new LoginFailure
        {
            Id = Guid.NewGuid(), UserId = leaderUserId, IpKey = "test", Count = 7,
            WindowStartUtc = DateTime.UtcNow, LockoutEndUtc = DateTime.UtcNow.AddMinutes(14)
        });
        await w.Db.SaveChangesAsync();

        var cleared = await w.Support.UnlockAsync(adminId, leaderUserId, "Amepiga simu: amefungiwa baada ya kusahau neno");
        Assert.Equal(1, cleared);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Contains(w.Db.PlatformAuditEvents, e => e.Action == "unlock_account" && e.SubjectId == leaderUserId);
    }

    [Fact]
    public async Task Support_Sees_Masked_Contact_Only_And_Cannot_Approve_Anything_Financial()
    {
        var w = await Build();
        var (adminId, _) = await w.AddAdmin("Msaada", PlatformAdminLevel.Support);
        await AddLeader(w, "Neema Mwenyekiti", "neema@chamalink.test", "255712300004");

        var rows = await w.Support.FindPeopleAsync("neema");
        var row = Assert.Single(rows);
        Assert.NotNull(row.Email);
        Assert.Contains("•", row.Email!);                    // barua pepe imefichwa kwa alama
        Assert.NotEqual("neema@chamalink.test", row.Email);
        Assert.Contains("•", row.Phone ?? "•");              // simu imefichwa katikati
        Assert.StartsWith("2557", row.Phone);
        Assert.True(row.HasAccount);

        // Hakuna API ya msimamizi inayogusa fedha — angalia TruthArchitectureTests.Platform_Admin_Cannot_Touch_Money.
        Assert.NotNull(adminId);
    }

    // ==================================================================
    // 6. Orodha ya vikundi: hakuna kikundi kinachopotea
    // ==================================================================
    [Fact]
    public async Task Group_List_Flags_Missing_Policy_Instead_Of_Hiding_The_Group()
    {
        var w = await Build();
        var ok = await w.AddGroup("Ukonga Mkoba Wing", withPolicy: true);
        var broken = await w.AddGroup("Kikundi Kipya", withPolicy: false);

        var rows = await w.Groups.ListAsync();
        Assert.Equal(2, rows.Count);

        var good = rows.Single(r => r.Id == ok);
        var bad = rows.Single(r => r.Id == broken);

        Assert.DoesNotContain("NoPolicy", good.Flags);
        Assert.Contains("NoPolicy", bad.Flags);
        Assert.Equal("Critical", bad.Health);       // sera haipo = hatari, si "sawa"
        Assert.NotEqual("Critical", good.Health);   // vikundi vipya vinaweza kuwa na NoStatements (Warning)
    }

    [Fact]
    public async Task Group_With_Members_But_No_Leader_Is_Critical()
    {
        var w = await Build();
        var gid = await w.AddGroup("Kikundi cha Mwezi");
        w.GroupId = gid;
        await AddLeader(w, "Mwanachama Bila Nafasi", "mwanachama@chamalink.test", "255712399002"); // Role = Member

        var rows = await w.Groups.ListAsync();
        var row = rows.Single(r => r.Id == gid);
        // Wanachama wapo lakini hakuna kiongozi = kikundi hakiwezi kuamua kitu → Critical.
        Assert.Contains("NoLeaders", row.Flags);
        Assert.Equal("Critical", row.Health);
        Assert.Equal(1, row.ActiveMembers);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    private static async Task<Guid> AddPlainUser(W w, string name, string email, string phone)
    {
        var userId = Guid.NewGuid();
        w.Db.Users.Add(new User
        {
            Id = userId, FullName = name, Email = email, PhoneNumber = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Siri-Ya-Zamani-123")
        });
        await w.Db.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> AddLeader(W w, string name, string email, string phone)
    {
        var userId = Guid.NewGuid();
        w.Db.Users.Add(new User
        {
            Id = userId, FullName = name, Email = email, PhoneNumber = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Siri-Ya-Zamani-123")
        });
        if (w.GroupId != Guid.Empty)
        {
            w.Db.GroupMembers.Add(new GroupMember
            {
                Id = Guid.NewGuid(), GroupId = w.GroupId, UserId = userId, MemberNumber = name,
                Role = GroupRole.Member, Status = MemberStatus.Active,
                JoinedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await w.Db.SaveChangesAsync();
        }
        await w.Db.SaveChangesAsync();
        return userId;
    }

    private static Dictionary<string, string> JwtOf(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var json = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
