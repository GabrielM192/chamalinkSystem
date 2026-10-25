using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO21 — mipangilio inajitegemea kwa aina ya kikundi.
///
///   • Kikundi cha "kila mwezi": kikundi kinadai mchango, faini, tarehe ya mwisho, neema n.k.
///   • Kikundi cha "kila tukio": hakidaiwi kitu cha mwezi — kinadai pochi, floor, dirisha la kujaza salio.
///
/// Hii ni kanuni ya biashara, si uzuri wa UI: seva yenyewe haikubali kikundi cha matukio
/// kilazimishwe kuweka mchango wa mwezi, na haikubali kikundi cha mwezi kikose mchango wake.
/// </summary>
public class Sasisho21SettingsByKindTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class NoParser : IMKobaParserService
    {
        public Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream stream) => throw new NotSupportedException();
        public Task<MKobaParseResult> ParseStatementDetailedAsync(Stream stream) => throw new NotSupportedException();
    }

    private sealed class W
    {
        public required ApplicationDbContext Db;
        public required TruthSetupService Setup;
        public Guid GroupId;
        public Guid ChairId;
    }

    private static async Task<W> Build(GroupType type)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new UtcOnlyInterceptor()).Options);

        var truth = new TruthLedgerService(db);
        var chair = new User { Id = Guid.NewGuid(), FullName = "Mwenyekiti", Email = $"c{Guid.NewGuid():N}@t.tz", PasswordHash = "x", PhoneNumber = "255700000001" };
        db.Users.Add(chair);

        var g = new Group { Id = Guid.NewGuid(), Name = "Kikundi cha jaribio", ShortCode = "SK" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), Type = type };
        db.Groups.Add(g);
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = g.Id, UserId = chair.Id, Role = GroupRole.Chairperson, Status = MemberStatus.Active, MemberNumber = "SK-2026-001" });
        await db.SaveChangesAsync();

        return new W { Db = db, Setup = new TruthSetupService(db, new NoParser(), truth), GroupId = g.Id, ChairId = chair.Id };
    }

    [Fact]
    public async Task Kikundi_Cha_Kila_Tukio_Hakidaiwi_Sheria_Za_Mwezi()
    {
        var w = await Build(GroupType.EventBased);

        // Hakuna mchango, faini, tarehe ya mwisho wala neema — ni sheria za mwezi.
        var saved = await w.Setup.UpdateSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto
        {
            ContributionStartDate = Jan1,
            JoiningFee = 0m,
            MemberMinimumBalance = 20_000m,
            EventTopUpWindowDays = 5,
            OverdueEventFineAmount = 5_000m,
            MaxEventsPerMonth = 2,
        }, w.ChairId);

        Assert.Equal(20_000m, saved.MemberMinimumBalance);
        Assert.Equal(5, saved.EventTopUpWindowDays);
        Assert.Equal(5_000m, saved.OverdueEventFineAmount);
        // Sehemu za mwezi zinabaki sifuri — hazikubuniwa.
        Assert.Equal(0m, saved.MonthlyContribution);
        Assert.Equal(0m, saved.LateFine);
    }

    [Fact]
    public async Task Muhtasari_Wa_Kikundi_Cha_Matukio_Hauna_Kosa_Kwa_DueDay_Sifuri()
    {
        // Kikundi cha "kila tukio" hakiwekwi tarehe ya mwezi (DueDay=0). Zamani hii ilisababisha
        // hitilafu 500 kwenye ukurasa wa Muhtasari: "DueDay lazima iwe 1..28".
        var w = await Build(GroupType.EventBased);
        await w.Setup.UpdateSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto
        {
            ContributionStartDate = Jan1,
            JoiningFee = 0m,
            MemberMinimumBalance = 20_000m,
            EventTopUpWindowDays = 5,
            OverdueEventFineAmount = 5_000m,
        }, w.ChairId);

        var ledger = new TruthLedgerService(w.Db);
        var truth = await ledger.GetGroupTruthAsync(w.GroupId);   // haipaswi kutupa

        Assert.NotNull(truth);
        Assert.Single(truth);
    }

    [Fact]
    public async Task Kikundi_Cha_Mwezi_Kinadai_Mchango_Wa_Mwezi()
    {
        var w = await Build(GroupType.MonthlySavings);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.Setup.UpdateSettingsAsync(w.GroupId,
            new TruthSetupService.SettingsDto
            {
                ContributionStartDate = Jan1,
                JoiningFee = 0m,
                LateFine = 0m,
                DueDateDay = 5,
                GracePeriodDays = 0,
                FineOnPartialPayment = false,
                AutoDeductSavings = false,
            }, w.ChairId));

        Assert.Contains("mchango wa mwezi", ex.Message);
    }

    [Fact]
    public async Task Kikundi_Cha_Mwezi_Kinahifadhi_Sheria_Zake_Kikamilifu()
    {
        var w = await Build(GroupType.MonthlySavings);

        var saved = await w.Setup.UpdateSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto
        {
            ContributionStartDate = Jan1,
            MonthlyContribution = 10_000m,
            LateFine = 5_000m,
            JoiningFee = 50_000m,
            DueDateDay = 5,
            GracePeriodDays = 0,
            FineOnPartialPayment = false,
            AutoDeductSavings = true,
        }, w.ChairId);

        Assert.Equal(10_000m, saved.MonthlyContribution);
        Assert.Equal(5_000m, saved.LateFine);
        Assert.Equal(5, saved.DueDateDay);
        Assert.True(saved.AutoDeductSavings);
    }

    [Fact]
    public async Task Kikundi_Cha_Kila_Tukio_Hakiathiriwi_Na_Sheria_Za_Mwezi_Zilizotumwa()
    {
        var w = await Build(GroupType.EventBased);
        var first = await w.Setup.UpdateSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto
        {
            ContributionStartDate = Jan1,
            JoiningFee = 0m,
            MemberMinimumBalance = 20_000m,
        }, w.ChairId);
        Assert.Equal(20_000m, first.MemberMinimumBalance);

        // Mabadiliko ya pili yanagusa floor tu — mchango wa mwezi unabaki sifuri.
        var second = await w.Setup.UpdateSettingsAsync(w.GroupId, new TruthSetupService.SettingsDto
        {
            MemberMinimumBalance = 30_000m,
        }, w.ChairId);

        Assert.Equal(30_000m, second.MemberMinimumBalance);
        Assert.Equal(0m, second.MonthlyContribution);
    }
}
