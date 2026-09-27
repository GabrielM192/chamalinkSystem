using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// RECONCILIATION ENGINE (Ulinganisho) — majaribio ya P0, 2026-09-19.
///
/// Lengo: M-Koba PDF = Excel ya viongozi = Ledger ya ChamaLink
/// kwa kila mwanachama × kila mwezi.
///
/// Majaribio haya yanatumia STUB parsers (hakuna PDF/Excel halisi) ili
/// kupima LOGIC ya ulinganisho pekee:
///   1. Vyanzo vyote vinapatana → Sawa
///   2. PDF vs Excel hazilingani (tofauti = faini) → NyarakaHazilingani
///   3. Mchango haujaingizwa mfumo → MfumoChini
///   4. Import mara mbili → MfumoZiada (dalili ya marudio)
///   5. KIANZIO: ziada ya waterfall inaonekana na inaelezwa
///   6. Miamala ya M-Koba isiyolingana → onyo (warning)
/// </summary>
public class ReconciliationTests
{
    // ── Stub parsers ──────────────────────────────────────────────────
    private sealed class StubMkobaParser : IMKobaParserService
    {
        private readonly List<MKobaTransactionItemDto> _tx;
        public StubMkobaParser(List<MKobaTransactionItemDto> tx) => _tx = tx;
        public Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream stream)
            => Task.FromResult(_tx);
    }

    private sealed class StubExcelParser : ITreasuryExcelParserService
    {
        private readonly List<TreasuryExcelRowDto> _rows;
        public StubExcelParser(List<TreasuryExcelRowDto> rows) => _rows = rows;
        public Task<List<TreasuryExcelRowDto>> ParseAsync(Stream stream)
            => Task.FromResult(_rows);
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (Group Group, GroupMember Member, User User) Seed(
        ApplicationDbContext db,
        string phone = "255700111222",
        string name = "Amina Hassan")
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Ukonga Mkoba Wing",
            Code = "UKW",
            Type = GroupType.MonthlySavings,
            Settings = new GroupSettings
            {
                Id = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                Financial = new FinancialSettings { JoiningFee = 50_000m },
                Contribution = new ContributionSettings
                {
                    MonthlyContribution = 10_000m,
                    LateFine = 5_000m
                }
            }
        };
        group.Settings!.GroupId = group.Id;

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = name,
            Email = $"u{Guid.NewGuid():N}@test.com",
            PhoneNumber = phone,
            PasswordHash = "hash"
        };

        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            MemberNumber = "UKW-001",
            JoinedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = MemberStatus.Active
        };

        db.Groups.Add(group);
        db.Users.Add(user);
        db.GroupMembers.Add(member);
        db.SaveChanges();
        return (group, member, user);
    }

    private static void AddLedger(
        ApplicationDbContext db, Group group, User user,
        int month, decimal amount, TransactionType type = TransactionType.Contribution)
    {
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = user.Id,
            AccountId = Guid.NewGuid(),
            Amount = amount,
            Type = type,
            ReferenceNo = $"TEST-{month}-{type}",
            Description = "jaribio",
            CreatedAt = new DateTime(2026, month, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        db.SaveChanges();
    }

    private static MKobaTransactionItemDto MkobaTx(
        string phone, decimal amount, int month, bool withdrawal = false)
        => new()
        {
            ReferenceNumber = $"RX{month}{Guid.NewGuid():N}"[..12],
            PhoneNumber = phone,
            MemberName = "",
            Amount = amount,
            TransactionDate = new DateTime(2026, month, 5, 10, 0, 0),
            IsWithdrawal = withdrawal
        };

    private static TreasuryExcelRowDto ExcelRow(
        string name, params decimal?[] months)
    {
        var row = new TreasuryExcelRowDto { ExcelName = name, RowNumber = 2 };
        for (int i = 0; i < 12; i++)
            row.MonthlyAmounts[i] = i < months.Length ? months[i] : null;
        return row;
    }

    private static Stream Dummy() => new MemoryStream(new byte[] { 9 });

    private static ReconciliationService Svc(
        ApplicationDbContext db,
        List<MKobaTransactionItemDto>? mkoba = null,
        List<TreasuryExcelRowDto>? excel = null)
        => new(
            db,
            new StubMkobaParser(mkoba ?? new()),
            new StubExcelParser(excel ?? new()));

    // ════════════════════════════════════════════════════════════════
    // 1. Vyanzo vyote vinapatana → Sawa
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task AllSourcesAgree_StatusSawa()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);

        AddLedger(db, group, user, 1, 10_000m);
        AddLedger(db, group, user, 1, 5_000m, TransactionType.FinePayment);

        var svc = Svc(db,
            mkoba: new() { MkobaTx("0700111222", 15_000m, 1) },   // 0→255 normalization
            excel: new() { ExcelRow("Amina Hassan", 15_000m) });

        var result = await svc.GetReconciliationAsync(group.Id, 2026, Dummy(), Dummy());

        var row = result.Rows.Single(r => r.Month == 1);
        Assert.Equal(15_000m, row.PdfAmount);
        Assert.Equal(15_000m, row.ExcelAmount);
        Assert.Equal(15_000m, row.LedgerAmount);
        Assert.Equal(0m, row.Difference);
        Assert.Equal(ReconciliationStatus.Sawa, row.Status);
        Assert.Equal(1, result.CountSawa);
    }

    // ════════════════════════════════════════════════════════════════
    // 2. PDF vs Excel hazilingani; tofauti = faini → eleza sababu
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task PdfVsExcel_DifferByFine_NyarakaHazilingani()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);

        AddLedger(db, group, user, 3, 15_000m);

        var svc = Svc(db,
            mkoba: new() { MkobaTx("255700111222", 10_000m, 3) },  // M-Koba: 10k tu
            excel: new() { ExcelRow("Amina Hassan", null, null, 15_000m) }); // Excel: 15k

        var result = await svc.GetReconciliationAsync(group.Id, 2026, Dummy(), Dummy());

        var row = result.Rows.Single(r => r.Month == 3);
        Assert.Equal(ReconciliationStatus.NyarakaHazilingani, row.Status);
        Assert.Contains("faini", row.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════
    // 3. Mchango haujaingizwa mfumo → MfumoChini
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task MissingFromLedger_MfumoChini()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);
        // Hakuna ledger entry kwa Februari

        var svc = Svc(db,
            mkoba: new() { MkobaTx("255700111222", 10_000m, 2) },
            excel: new() { ExcelRow("Amina Hassan", null, 10_000m) });

        var result = await svc.GetReconciliationAsync(group.Id, 2026, Dummy(), Dummy());

        var row = result.Rows.Single(r => r.Month == 2);
        Assert.Equal(ReconciliationStatus.MfumoChini, row.Status);
        Assert.Equal(-10_000m, row.Difference);
        Assert.Contains("CHINI", row.Reason);
    }

    // ════════════════════════════════════════════════════════════════
    // 4. Import mara mbili → MfumoZiada + dalili ya marudio
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task DoubleImport_MfumoZiada_WithDuplicateHint()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);

        // Ledger ina 10k mara MBILI (M-Koba import + Excel import)
        AddLedger(db, group, user, 4, 10_000m);
        AddLedger(db, group, user, 4, 10_000m);

        var svc = Svc(db,
            mkoba: new() { MkobaTx("255700111222", 10_000m, 4) },
            excel: new() { ExcelRow("Amina Hassan", null, null, null, 10_000m) });

        var result = await svc.GetReconciliationAsync(group.Id, 2026, Dummy(), Dummy());

        var row = result.Rows.Single(r => r.Month == 4);
        Assert.Equal(ReconciliationStatus.MfumoZiada, row.Status);
        Assert.Equal(10_000m, row.Difference);
        Assert.Contains("MARA MBILI", row.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════
    // 5. KIANZIO: ziada ya waterfall inaonekana + inaelezwa
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task JoiningFeeRow_WaterfallExcessExplained()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);

        // Excel: KIANZIO column = 30,000. Ledger: 30,000 (column) + 2,000
        // (ziada ya mchango iliyopelekwa KIANZIO na waterfall).
        AddLedger(db, group, user, 1, 30_000m, TransactionType.JoiningFee);
        AddLedger(db, group, user, 1, 2_000m, TransactionType.JoiningFee);

        var excelRow = ExcelRow("Amina Hassan", 12_000m);
        excelRow.JoiningFee = 30_000m;

        var svc = Svc(db, excel: new() { excelRow });

        var result = await svc.GetReconciliationAsync(group.Id, 2026, null, Dummy());

        Assert.True(result.HasExcel);
        Assert.False(result.HasPdf);

        var kf = result.Rows.Single(r => r.Month == 0);
        Assert.Equal("KIANZIO", kf.MonthLabel);
        Assert.Equal(30_000m, kf.ExcelAmount);
        Assert.Equal(32_000m, kf.LedgerAmount);
        Assert.Equal(ReconciliationStatus.MfumoZiada, kf.Status);
        Assert.Contains("ziada ya michango", kf.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════
    // 6. Miamala ya M-Koba isiyolingana → onyo
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task UnmatchedMkobaTransaction_ProducesWarning()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);

        var svc = Svc(db,
            mkoba: new()
            {
                MkobaTx("255700111222", 10_000m, 1),   // inalingana
                MkobaTx("255799999999", 7_000m, 1),   // HAILINGANI
                MkobaTx("255700111222", 3_000m, 5, withdrawal: true) // withdrawal
            });

        var result = await svc.GetReconciliationAsync(group.Id, 2026, Dummy(), null);

        Assert.Contains(result.Warnings, w =>
            w.Contains("haikulingana", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w =>
            w.Contains("withdrawals", StringComparison.OrdinalIgnoreCase));

        // Withdrawal HAIONGEZWI kwenye pdf ya mwezi
        var jan = result.Rows.Single(r => r.Month == 1);
        Assert.Equal(10_000m, jan.PdfAmount);
    }

    // ════════════════════════════════════════════════════════════════
    // 7. Excel pekee (hakuna PDF) — inalinganishwa na ledger
    // ════════════════════════════════════════════════════════════════
    [Fact]
    public async Task ExcelOnly_ComparesAgainstLedger()
    {
        using var db = NewDb();
        var (group, member, user) = Seed(db);

        AddLedger(db, group, user, 6, 10_000m);

        var svc = Svc(db, excel: new() { ExcelRow("Amina Hassan", null, null, null, null, null, 10_000m) });

        var result = await svc.GetReconciliationAsync(group.Id, 2026, null, Dummy());

        Assert.False(result.HasPdf);
        Assert.True(result.HasExcel);

        var row = result.Rows.Single(r => r.Month == 6);
        Assert.Equal(ReconciliationStatus.Sawa, row.Status);
        Assert.Equal(0m, row.Difference);
    }
}
