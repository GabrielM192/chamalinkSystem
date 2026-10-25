using System.Text;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO21 (Sehemu ya 3) — PDF ZA HAZINA.
///
///   1. Risiti inatoka kwa mwanachama na ina kiasi, namba ya mwanachama na deni.
///   2. Mwanachama HAWEZI kupakua risiti ya mtu mwingine.
///   3. Ripoti ya sanduku ni ya viongozi pekee.
///   4. PDF ni halali: inaanza na %PDF, ina xref na inaisha na %%EOF (inafunguka
///      kwenye printa na kifaa chochote).
///   5. Mistari mingi inajenga kurasa zaidi ya moja (hakuna kukatwa).
/// </summary>
public class Sasisho21PdfTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required PaymentDocumentsService Docs;
        public Guid GroupId;
        public readonly Dictionary<string, (Guid MemberId, Guid UserId)> P = new();
        public Guid M(string n) => P[n].MemberId;
        public Guid U(string n) => P[n].UserId;
    }

    private static async Task<W> Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var w = new W { Db = db, Docs = new PaymentDocumentsService(db), GroupId = Guid.NewGuid() };

        db.Groups.Add(new Group { Id = w.GroupId, Name = "J3 Together Forever", ShortCode = "J3", Type = GroupType.EventBased, JoinCode = "J3JOIN" });
        db.TruthGroupSettings.Add(new TruthGroupSettings
        {
            GroupId = w.GroupId, MonthlyContribution = 0m, LateFine = 0m, JoiningFee = 30_000m,
            DueDateDay = 5, GracePeriodDays = 0, ContributionStartDate = Jan1, MemberMinimumBalance = 20_000m
        });

        foreach (var (name, role) in new[]
                 {
                     ("Chair", GroupRole.Chairperson), ("Treasurer", GroupRole.Treasurer),
                     ("M1", GroupRole.Member), ("M2", GroupRole.Member)
                 })
        {
            var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, FullName = name + " Mwenyewe", PhoneNumber = $"2557300000{w.P.Count:00}" });
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

    private static PaymentReceipt Receipt(W w, Guid? memberId, decimal amount, string reference,
        PaymentReceiptStatus status = PaymentReceiptStatus.Confirmed, DateTime? at = null)
        => new()
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, Amount = amount, Reference = reference,
            OccurredAt = at ?? DateTime.UtcNow, Status = status,
            ConfirmedGroupMemberId = status == PaymentReceiptStatus.Confirmed ? memberId : null,
            SuggestedGroupMemberId = status != PaymentReceiptStatus.Confirmed ? memberId : null,
            Phone = "255730000001", Description = "Malipo ya tukio", PaymentCodeSeen = "J3-E07-0147",
            ConfirmedAt = status == PaymentReceiptStatus.Confirmed ? DateTime.UtcNow : null,
            ConfirmedByUserId = status == PaymentReceiptStatus.Confirmed ? w.U("Chair") : null
        };

    [Fact]
    public async Task Risiti_Ina_Maandishi_Yote_Muhimu()
    {
        var w = await Build();
        var r = Receipt(w, w.M("M1"), 30_000m, "MK-8831");
        w.Db.PaymentReceipts.Add(r);
        await w.Db.SaveChangesAsync();

        var (bytes, name) = await w.Docs.ReceiptAsync(w.GroupId, r.Id, w.U("M1"));

        Assert.True(bytes.Length > 800);
        Assert.EndsWith(".pdf", name);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("RISITI YA MALIPO", text);
        Assert.Contains("J3-2026-003", text);
        Assert.Contains("30,000", text);
        Assert.Contains("MK-8831", text);
        Assert.Contains("%%EOF", text);
    }

    [Fact]
    public async Task Mwanachama_Hawezi_Kupakua_Risiti_Ya_Mwenzake()
    {
        var w = await Build();
        var r = Receipt(w, w.M("M2"), 15_000m, "MK-7744");
        w.Db.PaymentReceipts.Add(r);
        await w.Db.SaveChangesAsync();

        // M1 hawezi kuona risiti ya M2
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Docs.ReceiptAsync(w.GroupId, r.Id, w.U("M1")));

        // Mwenyewe anaweza
        var own = await w.Docs.ReceiptAsync(w.GroupId, r.Id, w.U("M2"));
        Assert.True(own.Bytes.Length > 0);

        // Kiongozi anaweza (anahitaji kuthibitisha malipo ya mwanachama)
        var leader = await w.Docs.ReceiptAsync(w.GroupId, r.Id, w.U("Treasurer"));
        Assert.True(leader.Bytes.Length > 0);
    }

    [Fact]
    public async Task Ripoti_Ya_Sanduku_Ni_Ya_Viongozi_Pekee()
    {
        var w = await Build();
        w.Db.PaymentReceipts.Add(Receipt(w, w.M("M1"), 20_000m, "MK-1"));
        w.Db.PaymentReceipts.Add(Receipt(w, null!, 5_000m, "MK-2", PaymentReceiptStatus.Unmatched));
        await w.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Docs.InboxReportAsync(w.GroupId, w.U("M1")));

        var (bytes, name) = await w.Docs.InboxReportAsync(w.GroupId, w.U("Chair"));
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("RIPOTI YA SANDUKU LA MALIPO", text);
        Assert.Contains("MUHTASARI", text);
        Assert.Contains("Haijatambuliwa", text);
        Assert.StartsWith("Ripoti-Ya-Malipo-", name);
    }

    [Fact]
    public async Task Mistari_Mingi_Inajenga_Kurasa_Zaidi_Ya_Moja()
    {
        var w = await Build();
        for (var i = 0; i < 90; i++)
            w.Db.PaymentReceipts.Add(Receipt(w, i % 2 == 0 ? w.M("M1") : null, 1000m + i, $"MK-{i:0000}",
                i % 2 == 0 ? PaymentReceiptStatus.Confirmed : PaymentReceiptStatus.Unmatched,
                DateTime.UtcNow.AddMinutes(-i)));
        await w.Db.SaveChangesAsync();

        var (bytes, _) = await w.Docs.InboxReportAsync(w.GroupId, w.U("Chair"));
        var text = Encoding.Latin1.GetString(bytes);

        Assert.Contains("/Type /Pages", text);
        var count = text.Substring(text.IndexOf("/Count", StringComparison.Ordinal) + 6, 3).Trim();
        Assert.True(int.Parse(count) >= 2, $"Kurasa zilizotarajiwa >= 2, zilizopatikana {count}");
        Assert.Contains("%%EOF", text);
    }
}
