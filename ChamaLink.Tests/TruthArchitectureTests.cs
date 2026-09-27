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
        typeof(AllocationEngine), typeof(ObligationEngine), typeof(LedgerReclassificationService),
        typeof(LedgerService), typeof(BusinessRuleEngine), typeof(TreasuryImportService),
        typeof(FinancialPositionService), typeof(HistoricalJoinFeeService),
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
        "MembershipController.AddMember", "MembershipController.ChangeRole", "MembershipController.IssueNumbers",
        // Mikopo: Injini inasoma ratiba kutoka jedwali la Loans; marejesho yanapita M-Koba
        "LoanController.Issue", "LoanController.MarkDefaulted",
        // Maombi ya malipo (utawala); pesa halisi inatoka M-Koba na inaonekana kwenye cashbook
        "WithdrawalController.Create", "WithdrawalController.Decide", "WithdrawalController.MarkPaid",
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
        Assert.Empty(AllowedWriteActions.Where(a => !existing.Contains(a)));
    }

    [Theory]
    [InlineData(typeof(MkobaImportController))]
    [InlineData(typeof(TreasuryImportController))]
    [InlineData(typeof(LedgerController))]
    [InlineData(typeof(HistoricalJoinFeeController))]
    [InlineData(typeof(DataRepairController))]
    [InlineData(typeof(ReportsController))]
    [InlineData(typeof(WelfareController))]
    [InlineData(typeof(DebugController))]
    public void Legacy_Money_Controllers_Are_Gated(Type ctrl)
        => Assert.NotNull(ctrl.GetCustomAttribute<LegacyEndpointAttribute>());

    [Fact]
    public void Loan_Repay_Is_Gated_Because_Engine_Cannot_See_LedgerEntries()
        => Assert.NotNull(typeof(LoanController).GetMethod(nameof(LoanController.Repay))!.GetCustomAttribute<LegacyEndpointAttribute>());

    [Fact]
    public void DebugController_Requires_Authentication_Everywhere()
    {
        var t = typeof(DebugController);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        var anon = t.GetMethods().Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null).Select(m => m.Name).ToList();
        Assert.True(anon.Count == 0, "AllowAnonymous kwenye: " + string.Join(", ", anon));
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
}
