using System.Reflection;
using ChamaLink.API.Controllers;
using ChamaLink.API.Filters;
using ChamaLink.Domain.Ledger;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChamaLink.Tests;

/// <summary>
/// Ulinzi wa "chanzo kimoja cha ukweli" (TATHMINI 2026-09-26 #1, #6):
///  1. Huduma za Truth hazitegemei injini za zamani (V1/V2) kabisa.
///  2. Kila endpoint inayoandika (POST/PUT/PATCH/DELETE) lazima iwe ama kwenye orodha
///     iliyoidhinishwa hapa chini, ama iwe na [LegacyEndpoint] (imefungwa kwa default).
///     Mtu akiongeza endpoint mpya ya kuandika pesa bila kufikiria, test hii inafeli.
///  3. DebugController haipatikani bila kuingia (ilikuwa [AllowAnonymous] kila mahali).
/// </summary>
public class TruthArchitectureTests
{
    private static readonly Type[] LegacyEngines =
    {
        typeof(AllocationEngine),
        typeof(LedgerService), typeof(BusinessRuleEngine), typeof(TreasuryImportService),
        typeof(FinancialPositionService),
    };

    [Theory]
    [InlineData(typeof(TruthLedgerService))]
    [InlineData(typeof(TruthSetupService))]
    [InlineData(typeof(TruthController))]
    public void Truth_Components_Do_Not_Depend_On_Legacy_Engines(Type component)
    {
        var deps = component.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
        var bad = deps.Intersect(LegacyEngines).ToList();
        Assert.True(bad.Count == 0, $"{component.Name} inategemea injini ya zamani: {string.Join(", ", bad.Select(b => b.Name))}");

        // Na hakuna field ya aina hizo iliyofichwa (mf. kupitia service locator)
        var fields = component.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType);
        Assert.Empty(fields.Intersect(LegacyEngines));
    }

    [Fact]
    public void Truth_Engine_Lives_In_Domain_Without_Infrastructure()
    {
        var refs = typeof(ContributionTruthEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(refs, r => r.StartsWith("ChamaLink.Infrastructure") || r.StartsWith("Microsoft.EntityFrameworkCore"));
    }

    /// Endpoints za kuandika zilizoidhinishwa - HAZIGUSI pesa za wanachama nje ya Injini ya Ukweli.
    private static readonly HashSet<string> AllowedWriteActions = new()
    {
        // Utambulisho
        "AuthController.Register", "AuthController.Login",
        // Kikundi (uanachama; si pesa)
        "GroupController.CreateGroup", "GroupController.AddMember", "GroupController.UpdateSettings",
        // Usajili na uanachama (AWAMU I): utambulisho, nafasi, namba za uanachama — HAKUNA pesa.
        // Mwanachama mpya au aliyeunganishwa anapata ukweli wake wa fedha kutoka Injini ile ile (kwa GroupMember.Id).
        "MembershipController.CreateGroup", "MembershipController.Join", "MembershipController.CancelMyRequest",
        "MembershipController.SetShortCode", "MembershipController.RegenerateJoinCode", "MembershipController.Decide",
        "MembershipController.AddMember", "MembershipController.IssueNumbers",
        // SASISHO17 (NEW-2): vyeo kwa pendekezo + kura tu (RoleChangeService)
        "MembershipController.ProposeRole", "MembershipController.VoteRole", "MembershipController.CancelRole",
        // Mikopo (SASISHO15): ombi → kura za viongozi huru → kuanzisha → uthibitisho kwa rejea ya M-Koba (maker-checker).
        // Loan inazaliwa tu kukiwa na utoaji halisi wa M-Koba; Injini inasoma ratiba kutoka Loans, marejesho yanapita M-Koba.
        // LoanController.Issue/MarkDefaulted sasa ni [LegacyEndpoint].
        "LoanApplicationsController.Preview", "LoanApplicationsController.Apply", "LoanApplicationsController.Vote",
        "LoanApplicationsController.InitiateDisbursement", "LoanApplicationsController.ConfirmDisbursement", "LoanApplicationsController.Cancel",
        "LoanApplicationsController.RequestDefault", "LoanApplicationsController.DecideDefault",
        // Vikao (SASISHO15): refresh token ya kuzungushwa + kutoka
        "AuthController.Refresh", "AuthController.Logout", "AuthController.RevokeAll",
        // (WithdrawalController sasa ni [LegacyEndpoint]: utoaji wa pesa unapitia Mafao + uthibitisho wa M-Koba.)
        // MAFAO: hayaandiki malipo ya wanachama. Pesa halisi inatoka M-Koba (utoaji unaolinganishwa kwa reference);
        // makato ya Kianzio/deni yanaingia Injini ya Ukweli tu kesi ikiwa "Paid" (TruthLedgerService inasoma BenefitCases).
        // Kila hatua ni maker-checker + audit ndani ya BenefitService.
        "BenefitsController.ProposeGovernance", "BenefitsController.DecideGovernance", "BenefitsController.CancelGovernance",
        "BenefitsController.ProposeRate", "BenefitsController.DecideRate", "BenefitsController.CancelRate",
        "BenefitsController.Preview", "BenefitsController.Open", "BenefitsController.Historical", "BenefitsController.DecideHistorical",
        "BenefitsController.Upload", "BenefitsController.Verify", "BenefitsController.Vote", "BenefitsController.ConfirmPayment",
        "BenefitsController.Cancel", "BenefitsController.Explain", "BenefitsController.DecideExplanation",
        // SASISHO13: kuanzisha malipo (hatua ya 1; pesa bado inathibitishwa na M-Koba hatua ya 2) + uhusiano (mgongano wa maslahi)
        "BenefitsController.InitiatePayment", "BenefitsController.RecheckFunds", "BenefitsController.AddRelation", "BenefitsController.RequestEndRelation", "BenefitsController.DecideEndRelation", "BenefitsController.DecideRelation",
        // Hali ya mwanachama kwa kura (Katiba 12(3)) — si pesa; Injini inasimamisha wajibu mpya kuanzia EffectiveFrom
        "GovernanceController.SubmitRequest", "GovernanceController.Vote", "GovernanceController.Cancel",
        // Arifa: kuweka "imesomwa" tu
        "NotificationsController.Read", "NotificationsController.ReadAll",
        // SASISHO18: sweep haitengenezi pesa — inatumia amana ZILIZOKWISHA kukubaliwa na Injini ya Ukweli
        // kulipa deni la tukio lililopo (EventObligation). Kiasi hicho kimeshahesabiwa kama akiba ya mwanachama;
        // sweep inabadilisha tu "akiba" kuwa "malipo ya deni", ndani ya floor, kuanzia deni la zamani.
        "EventWalletController.Sweep",
        // SASISHO19: kuondolewa kwa mwanachama hanaandiki pesa — anafungua ombi la hali ya mwanachama
        // (MemberStatusChangeRequest) linalohitaji kura za viongozi ZAIDI ya wawili. Rekodi ya fedha
        // inabaki ile ile; salio linabaki kwa mwanachama aliyeondolewa hadi arudi kwa masharti.
        "EventWalletController.ProposeRemoval",
        // ============================================================================================
        // SASISHO20 — MSIMAMIZI WA MFUMO (platform admin). HAANDIKI FEDHA KABISA.
        // Kila kimoja ni uthibitisho (neno la siri + MFA), msaada wa akaunti (kufungua lock,
        // code ya kureset — hash pekee; msimamizi haoni neno la siri jipya), au uamuzi wa
        // watu wawili unaohusu WASIMAMIZI / dirisha la kuona (break-glass). Hakuna hata moja
        // inayogusa LedgerEntry, malipo, deni, au kiasi. Test `Platform_Admin_Cannot_Touch_Money`
        // inahakikisha hilo lisivunjwe wakati ujao.
        // ============================================================================================
        "AuthController.ResetWithCode",
        "PlatformController.Login", "PlatformController.Enroll", "PlatformController.ConfirmEnrollment",
        "PlatformController.VerifyMfa", "PlatformController.Logout",
        "PlatformController.SendNotice", "PlatformController.RotateJoinCode",
        "PlatformController.Unlock", "PlatformController.IssueResetCode",
        "PlatformController.RequestBreakGlass", "PlatformController.RequestAdminChange",
        "PlatformController.RequestMfaReset", "PlatformController.Decide", "PlatformController.Cancel",
        // ============================================================================================
        // SASISHO21 — SANDUKU LA MALIPO (kodi ya malipo + madai ya wanachama).
        // Hakuna endpoint inayoandika kiasi cha fedha:
        //   • Sync inasoma mistari iliyokwisha kuidhinishwa (MkobaRawTransactions) na kuunda
        //     RISITI — mstari wa kazi, si pesa. Haiongezi wala haipunguzi salio.
        //   • Confirm inabadilisha MMILIKI wa mstari MMOJA (AttributedGroupMemberId), kisha
        //     inaita SweepAsync — waterfall ile ile ya injini, ndani ya floor.
        //   • Reject ni uamuzi wa rekodi; haugusi salio.
        //   • Claim ni DAI la mwanachama: halibadilishi deni wala salio; DecideClaim
        //     haliwezi kuthibitisha malipo bila mstari wa statement (service inakataa).
        // ============================================================================================
        "PaymentInboxController.Sync", "PaymentInboxController.Confirm", "PaymentInboxController.Reject",
        "PaymentInboxController.Claim", "PaymentInboxController.DecideClaim",
        // SASISHO21 (Sehemu ya 2) — SMS. Hakuna endpoint inayogusa fedha za kikundi:
        //   • TopUp inaongeza CREDITS za ujumbe (salio la mtoa-huduma), si salio la mwanachama;
        //   • Dispatch inatuma ujumbe uliopangwa tu (hakuna kuandika deni wala malipo);
        //   • Delivery ni taarifa ya mtandao iliyolindwa kwa siri (X-Sms-Callback).
        "SmsController.TopUp", "SmsController.Dispatch", "SmsController.Delivery",
        // SASISHO21 (Sehemu ya 3) — RUfAA. Rufaa haibadilishi fedha:
        //   • File     inaandika ombi la mwanachama (hakuna pesa inayogusa);
        //   • Vote     ni kura ya kiongozi; uamuzi unaweza kubadilisha HALI ya mwanachama
        //              au kufungua dai — lakini hakuna shilingi inayoongezwa au kupunguzwa;
        //   • Withdraw ni mwombaji kufuta ombi lake mwenyewe.
        "AppealsController.File", "AppealsController.Vote", "AppealsController.Withdraw",
    };

    private static IEnumerable<(Type Ctrl, MethodInfo Action)> WriteActions() =>
        typeof(TruthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>()
                    .Any(a => a.HttpMethods.Any(h => h is "POST" or "PUT" or "PATCH" or "DELETE")))
                .Select(m => (t, m)));

    [Fact]
    public void Every_Write_Endpoint_Is_Truth_Or_Allowlisted_Or_Legacy_Gated()
    {
        var offenders = new List<string>();
        foreach (var (ctrl, action) in WriteActions())
        {
            if (ctrl == typeof(TruthController)) continue;
            var gated = ctrl.GetCustomAttribute<LegacyEndpointAttribute>() != null
                        || action.GetCustomAttribute<LegacyEndpointAttribute>() != null;
            var name = $"{ctrl.Name}.{action.Name}";
            if (!gated && !AllowedWriteActions.Contains(name)) offenders.Add(name);
        }
        Assert.True(offenders.Count == 0,
            "Endpoint hizi zinaandika bila kupita Injini ya Ukweli (ongeza [LegacyEndpoint] au zihalalishe hapa kwa sababu): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Allowlist_Has_No_Stale_Entries()
    {
        var existing = WriteActions().Select(x => $"{x.Ctrl.Name}.{x.Action.Name}").ToHashSet();
        Assert.All(AllowedWriteActions, a => Assert.Contains(a, existing));
    }

    [Theory]
    [InlineData(typeof(MkobaImportController))]
    [InlineData(typeof(TreasuryImportController))]
    [InlineData(typeof(LedgerController))]
    [InlineData(typeof(DataRepairController))]
    [InlineData(typeof(ReportsController))]
    [InlineData(typeof(WelfareController))]
    public void Legacy_Money_Controllers_Are_Gated(Type ctrl)
        => Assert.NotNull(ctrl.GetCustomAttribute<LegacyEndpointAttribute>());

    [Fact]
    public void Loan_Repay_Is_Gated_Because_Engine_Cannot_See_LedgerEntries()
        => Assert.NotNull(typeof(LoanController).GetMethod(nameof(LoanController.Repay))!.GetCustomAttribute<LegacyEndpointAttribute>());

    [Fact]
    public void Debug_Na_HistoricalJoinFee_Zimefutwa_Kabisa()
    {
        // SASISHO17 (M): hakuna controller ya uchunguzi wala njia ya pili ya Kianzio kwenye assembly
        var names = typeof(TruthController).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("DebugController", names);
        Assert.DoesNotContain("HistoricalJoinFeeController", names);
        Assert.DoesNotContain("DebugAccessAttribute", names);
        var infra = typeof(TruthLedgerService).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("ObligationEngine", infra);
        Assert.DoesNotContain("HistoricalJoinFeeService", infra);
    }

    // ---------- Tabia ya filter yenyewe ----------

    private static (ResourceExecutingContext Ctx, Func<bool> NextCalled, ResourceExecutionDelegate Next) MakeContext(bool? enabled)
    {
        var cfg = new Dictionary<string, string?>();
        if (enabled.HasValue) cfg[LegacyEndpointAttribute.ConfigKey] = enabled.Value ? "true" : "false";
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(cfg).Build())
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var actionCtx = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var ctx = new ResourceExecutingContext(actionCtx, new List<IFilterMetadata>(), new List<IValueProviderFactory>());
        var called = false;
        ResourceExecutionDelegate next = () =>
        {
            called = true;
            return Task.FromResult(new ResourceExecutedContext(actionCtx, new List<IFilterMetadata>()));
        };
        return (ctx, () => called, next);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Filter_Returns_410_By_Default(bool? enabled)
    {
        var (ctx, called, next) = MakeContext(enabled);
        await new LegacyEndpointAttribute("/api/truth/x").OnResourceExecutionAsync(ctx, next);
        Assert.False(called());
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);
    }

    [Fact]
    public async Task Filter_Allows_When_Explicitly_Enabled()
    {
        var (ctx, called, next) = MakeContext(true);
        await new LegacyEndpointAttribute("/api/truth/x").OnResourceExecutionAsync(ctx, next);
        Assert.True(called());
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void TruthAudit_IsWrittenInOnePlace_InsideTheSameSaveChanges()
    {
        // Kumbukumbu za ukaguzi za Injini ya Ukweli zinaandikwa na TruthSetupService pekee (pamoja na kitendo, atomiki).
        // Controller ikiandika tena, kila kitendo kinaonekana mara mbili na nakala ya pili inaweza kukosekana kitendo kikifaulu.
        var ctor = typeof(ChamaLink.API.Controllers.TruthController).GetConstructors().Single();
        Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType == typeof(ChamaLink.Infrastructure.Services.AuditService));
    }

    /// <summary>
    /// SASISHO20 — msimamizi wa mfumo ni "macho na mikono ya msaada", SI mhasibu.
    /// Test inasoma msimbo halisi wa /api/platform na huduma zake na kukataa:
    ///   (a) kuandika rekodi yoyote ya fedha (Add/Update/Remove/ExecuteUpdate/ExecuteDelete), na
    ///   (b) parameter yoyote ya kiasi kwenye action (amount/kiasi/kiasi).
    /// Kusoma muhtasari (TruthLedgerService, MkobaRawTransactions) kunaruhusiwa — ni dirisha la kuona.
    /// </summary>
    [Fact]
    public void Platform_Admin_Cannot_Touch_Money()
    {
        var root = NoHardcodedRulesRoot();
        var files = new List<string> { Path.Combine(root, "ChamaLink.API", "Controllers", "PlatformController.cs") };
        var platformDir = Path.Combine(root, "ChamaLink.Infrastructure", "Services", "Platform");
        if (Directory.Exists(platformDir)) files.AddRange(Directory.GetFiles(platformDir, "*.cs"));

        // Rekodi za fedha: msimamizi haziandiki. (Kusoma ni sawa.)
        string[] moneyEntities =
        {
            "LedgerEntry", "Payment", "Contribution", "Fine", "Loan", "BenefitCase",
            "MkobaRawTransaction", "MemberBalance", "Savings", "Debt", "Obligation", "Ledger"
        };
        string[] writeOps = { ".Add(", ".AddRange(", ".Update(", ".Remove(", ".RemoveRange(", "ExecuteUpdate", "ExecuteDelete" };

        var offenders = new List<string>();
        foreach (var f in files.Where(File.Exists))
        {
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                // Ni uandikaji wa EF tu (kupitia DbContext) unaogusa rekodi; `flags.Add(...)` si rekodi.
                if (!line.Contains("_context") || !writeOps.Any(line.Contains)) continue;
                var hit = moneyEntities.FirstOrDefault(line.Contains);
                if (hit is not null) offenders.Add($"{Path.GetFileName(f)}:{i + 1}  [{hit}]  {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0,
            "Msimamizi wa mfumo hapaswi kuandika rekodi za fedha. Vitu vilivyokutwa:\n" + string.Join("\n", offenders));

        // Hakuna action inayopokea kiasi (msimamizi haweki/kuhamisha pesa kwa mkono).
        var badParams = typeof(PlatformController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .Where(p => p.Name is not null && (p.Name.Contains("amount", StringComparison.OrdinalIgnoreCase)
                                                || p.Name.Contains("kiasi", StringComparison.OrdinalIgnoreCase)
                                                || p.Name.Contains("debt", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name!)
            .ToList();
        Assert.True(badParams.Count == 0, "Action ya msimamizi inapokea kiasi: " + string.Join(", ", badParams));
    }

    private static string NoHardcodedRulesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ChamaLink.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ChamaLink.sln haikupatikana.");
    }

}
