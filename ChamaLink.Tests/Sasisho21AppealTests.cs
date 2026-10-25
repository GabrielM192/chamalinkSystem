using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Appeals;
using ChamaLink.Infrastructure.Services.Benefits;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO21 (Sehemu ya 3) — RUfAA.
///
/// Kila jaribio linathibitisha kanuni moja:
///   1. Mwanachama anaweza kupinga uamuzi unaomgusa — na rufaa haiondoi deni.
///   2. KIONGOZI MMOJA HAAMUI: rufaa inahitaji viongozi wawili (sera, kamwe chini ya 2).
///   3. Mwombaji hapigani kura kwenye rufaa yake mwenyewe.
///   4. Kiongozi mmoja hapigani kura mara mbili.
///   5. Rufaa ya kubadilisha hali: ombi linalosubiri linafutwa; hali iliyotumika inarudishwa.
///   6. Rufaa ya dai: dai lililokataliwa linafunguliwa upya kwa hazina (pesa haibadiliki).
///   7. Nje ya muda wa sera (AppealWindowDays) rufaa haipokewi.
///   8. Mtu hawezi kupinga uamuzi usiomhusu.
/// </summary>
public class Sasisho21AppealTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required AppealService Appeals;
        public Guid GroupId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId)> P = new();
        public Guid M(string n) => P[n].MemberId;
        public Guid U(string n) => P[n].UserId;
    }

    private static async Task<W> Build(bool enabled = true, int windowDays = 0, int required = 2)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var w = new W { Db = db, Appeals = new AppealService(db), GroupId = Guid.NewGuid() };

        db.Groups.Add(new Group
        {
            Id = w.GroupId, Name = "J3 Together Forever", ShortCode = "J3", Type = GroupType.EventBased, JoinCode = "J3JOIN"
        });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 0m, LateFine = 0m, JoiningFee = 30_000m,
            DueDateDay = 5, GracePeriodDays = 0, ContributionStartDate = Jan1,
            AppealsEnabled = enabled, AppealWindowDays = windowDays, AppealApprovalsRequired = required
        });

        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer),
                     ("M1", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, FullName = name, PhoneNumber = $"2557200000{w.P.Count:00}" });
            db.GroupMembers.Add(new GroupMember
            {
                Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = $"J3-2026-00{w.P.Count + 1}",
                Role = role, Status = MemberStatus.Active, JoinedAt = Jan1
            });
            w.P[name] = (mid, uid);
        }

        await db.SaveChangesAsync();
        return w;
    }

    /// <summary>Ombi la kumfukuza M1 (halali, linasubiri kura).</summary>
    private static async Task<MemberStatusChangeRequest> ExpulsionRequest(W w, MemberStatus to = MemberStatus.Exited)
    {
        var r = new MemberStatusChangeRequest
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.M("M1"),
            FromStatus = MemberStatus.Active, ToStatus = to, IsExpulsion = to == MemberStatus.Exited,
            EffectiveDate = Jan1, Reason = "Ukaidi wa kanuni", RequiredApprovals = 3,
            Status = ProposalStatus.Pending, RequestedByUserId = w.U("Chair"), RequestedAt = DateTime.UtcNow
        };
        w.Db.MemberStatusChangeRequests.Add(r);
        await w.Db.SaveChangesAsync();
        return r;
    }

    // ==================================================================

    [Fact]
    public async Task Mwanachama_Anawasilisha_Rufaa_Na_Inaenda_Kwa_Viongozi_Wawili()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);

        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Sikuitwa kwenye kikao cha kusikilizwa; kanuni 12(3) haikufuatwa.");

        Assert.Equal("Pending", r.Status);
        Assert.Equal(2, r.RequiredApprovals);
        // Ombi la kufukuzwa HALIJAFUTWA bado — linahitaji kura mbili.
        Assert.Equal(ProposalStatus.Pending, (await w.Db.MemberStatusChangeRequests.FirstAsync(x => x.Id == req.Id)).Status);
        // Hali ya mwanachama haijabadilika
        Assert.Equal(MemberStatus.Active, (await w.Db.GroupMembers.FirstAsync(m => m.Id == w.M("M1"))).Status);
    }

    [Fact]
    public async Task Kiongozi_Mmoja_Hawezi_Kuamua_Rufaa()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);
        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Sikutakiwa kuondolewa bila kusikilizwa na viongozi wote.");

        var after1 = await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, "nakubali", w.U("Chair"));

        Assert.Equal("Pending", after1!.Status);
        Assert.Equal(1, after1.Approvals);
        Assert.Equal(ProposalStatus.Pending, (await w.Db.MemberStatusChangeRequests.FirstAsync(x => x.Id == req.Id)).Status);

        // Kiongozi wa PILI anamalizia
        var after2 = await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, "nakubali pia", w.U("Treasurer"));

        Assert.Equal("Granted", after2!.Status);
        Assert.Equal(ProposalStatus.Rejected, (await w.Db.MemberStatusChangeRequests.FirstAsync(x => x.Id == req.Id)).Status);
        Assert.Equal(MemberStatus.Active, (await w.Db.GroupMembers.FirstAsync(m => m.Id == w.M("M1"))).Status);
    }

    [Fact]
    public async Task Mwombaji_Hapigani_Kura_Ya_Rufaa_Yake()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);
        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Sikutakiwa kuondolewa bila kusikilizwa na viongozi wote.");

        // M1 ni Mwanachama, si kiongozi — lakini hata kiongozi asingeweza kupiga kura ya rufaa yake.
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, "naikubali", w.U("M1")));
        Assert.Contains("viongozi", ex.Message);
    }

    [Fact]
    public async Task Kiongozi_Hapigani_Kura_Mara_Mbili()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);
        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Sikutakiwa kuondolewa bila kusikilizwa na viongozi wote.");

        await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, null, w.U("Chair"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, null, w.U("Chair")));
    }

    [Fact]
    public async Task Rufaa_Iliyokubaliwa_Inarudisha_Hali_Iliyotumika()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);
        // Ombi limeshatumika: mwanachama amefukuzwa tayari
        var member = await w.Db.GroupMembers.FirstAsync(m => m.Id == w.M("M1"));
        member.Status = MemberStatus.Exited;
        req.Status = ProposalStatus.Approved;
        req.DecidedAt = DateTime.UtcNow;
        req.AppliedAt = DateTime.UtcNow;
        await w.Db.SaveChangesAsync();

        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Nilifukuzwa bila kusikilizwa; naomba kurudishwa kwenye kikundi.");
        await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, null, w.U("Chair"));
        var done = await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, null, w.U("Treasurer"));

        Assert.Equal("Granted", done!.Status);
        var restored = await w.Db.GroupMembers.FirstAsync(m => m.Id == w.M("M1"));
        Assert.Equal(MemberStatus.Active, restored.Status);
        Assert.Contains("imerudishwa", done.EffectSummary);
    }

    [Fact]
    public async Task Rufaa_Ya_Dai_Inafungua_Dai_Bila_Kuongeza_Pesa()
    {
        var w = await Build();
        var claim = new PaymentClaim
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.M("M1"), Amount = 30_000m,
            Reference = "MK-9911", Status = PaymentClaimStatus.Rejected,
            DecidedAt = DateTime.UtcNow, DecidedByUserId = w.U("Chair"), CreatedAt = DateTime.UtcNow
        };
        w.Db.PaymentClaims.Add(claim);
        await w.Db.SaveChangesAsync();

        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.Claim, claim.Id,
            "Namba ya muamala iko kwenye statement ya M-Koba ya tarehe 12.");
        await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, null, w.U("Chair"));
        var done = await w.Appeals.VoteAsync(w.GroupId, r.AppealId, true, null, w.U("Treasurer"));

        Assert.Equal("Granted", done!.Status);
        var after = await w.Db.PaymentClaims.FirstAsync(c => c.Id == claim.Id);
        Assert.Equal(PaymentClaimStatus.Pending, after.Status);
        Assert.Equal(30_000m, after.Amount); // hakuna pesa iliyoongezwa
        Assert.Null(after.DecidedAt);
    }

    [Fact]
    public async Task Rufaa_Inakataliwa_Kwa_Kura_Mbili_Za_HAPANA()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);
        var r = await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Sikutakiwa kuondolewa bila kusikilizwa na viongozi wote.");

        await w.Appeals.VoteAsync(w.GroupId, r.AppealId, false, "kanuni ilifuatwa", w.U("Chair"));
        var done = await w.Appeals.VoteAsync(w.GroupId, r.AppealId, false, "hakuna ushahidi", w.U("Treasurer"));

        Assert.Equal("Dismissed", done!.Status);
        Assert.Equal(ProposalStatus.Pending, (await w.Db.MemberStatusChangeRequests.FirstAsync(x => x.Id == req.Id)).Status);
        Assert.Contains("unasimama", done.EffectSummary);
    }

    [Fact]
    public async Task Rufaa_Nje_Ya_Muda_Wa_Sera_Haipokewi()
    {
        var w = await Build(windowDays: 7);
        var req = await ExpulsionRequest(w);
        req.RequestedAt = DateTime.UtcNow.AddDays(-30);
        await w.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
                "Nimechelewa lakini naomba msaada wa kusikilizwa."));
        Assert.Contains("umepita", ex.Message);
    }

    [Fact]
    public async Task Mtu_Hawezi_Kupinga_Uamuzi_Wa_Mwenzake()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Appeals.FileAsync(w.GroupId, w.U("Chair"), AppealKind.StatusChange, req.Id,
                "Napinga uamuzi huu kwa niaba ya mwenzangu."));
        Assert.Contains("wewe", ex.Message);
    }

    [Fact]
    public async Task Rufaa_Zikizimwa_Hazipokewi()
    {
        var w = await Build(enabled: false);
        var req = await ExpulsionRequest(w);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
                "Napinga uamuzi wa kufukuzwa kwangu bila haki."));
        Assert.Contains("zimezimwa", ex.Message);
    }

    [Fact]
    public async Task Rufaa_Mbili_Kwa_Jambo_Moja_Haipokewi()
    {
        var w = await Build();
        var req = await ExpulsionRequest(w);
        await w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
            "Sikutakiwa kuondolewa bila kusikilizwa na viongozi wote.");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            w.Appeals.FileAsync(w.GroupId, w.U("M1"), AppealKind.StatusChange, req.Id,
                "Narudia rufaa ile ile kwa sababu bado sijajibiwa."));
        Assert.Contains("Tayari", ex.Message);
    }
}
