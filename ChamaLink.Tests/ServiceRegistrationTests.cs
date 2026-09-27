using System.Net;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// JARIBIO LA KULINDA DHIDI YA BUG ILIYORUDIA-RUDIA:
/// "class ipo, `dotnet build` inapita, lakini service haijasajiliwa kwenye DI".
///
/// Hili lilivunja mfumo mara tatu:
///   1. AccountResolverService / IEventService / LoanService
///      (zilizokuwa zimerekebishwa tayari - angalia comments kwenye Program.cs)
///   2. ComplianceReportService        -> ReportsController endpoints 16 ZOTE
///                                         zilirudisha HTTP 400
///   3. TreasuryImportService +
///      ITreasuryExcelParserService    -> TreasuryImportController ilishindwa
///
/// `dotnet build` haiwezi KAMWE kugundua hili: ASP.NET Core huunda controller
/// kwa kutumia DI wakati wa OMBI, si wakati wa compile. Ndiyo maana build
/// ilikuwa inasema "0 Errors, 0 Warnings" huku endpoints 18 zikiwa zimevunjika.
///
/// Jaribio hapa linajenga **API halisi** (WebApplicationFactory = Program.cs
/// yako yenyewe, si nakala) na linajaribu kuunda kila controller. Ukisahau
/// `AddScoped<>` moja tu, jaribio hili linashindwa mara moja.
/// </summary>
public class ServiceRegistrationTests : IClassFixture<ServiceRegistrationTests.TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ServiceRegistrationTests(TestApiFactory factory) => _factory = factory;

    /// <summary>
    /// Program.cs halisi, lakini:
    ///   * ValidateOnBuild = true  -> DI inajikagua yenyewe YOTE wakati wa kuanza
    ///   * siri za uongo (jaribio halihitaji JWT/DB halisi; EF huunda DbContext
    ///     bila kuunganisha mpaka query ya kwanza itumwe)
    /// </summary>
    public class TestApiFactory : WebApplicationFactory<Program>
    {


        // MUHIMU: Program.cs inasoma `builder.Configuration["Jwt:Key"]`
        // WAKATI WA KUJENGA (kabla ya Build()). `builder.UseSetting(...)` na
        // `builder.ConfigureAppConfiguration(...)` ndani ya ConfigureWebHost
        // zinatekelezwa BAADA ya hapo, kwa hiyo hazifiki kwa wakati.
        //
        // CreateHost() ndiyo inatekelezwa KABLA ya Program.cs kufika kwenye
        // Build() - kwa hiyo ni mahali pekee ambapo tunaweza kuingiza siri za
        // majaribio na ziwe sehemu ya ConfigurationRoot halisi.
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "test-key-" + new string('x', 64),
                    ["Jwt:ExpiryDays"] = "1",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;Port=1;Database=no_db;Username=none;Password=none"
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // HII NDIYO FUNGUO. Chaguo-msingi la WebApplicationFactory ni
                // ValidateOnBuild = false - ndiyo sababu bug ya DI ilijificha
                // (endpoints 18 zilikuwa zimevunjika huku build ikisema "0 Errors").
                //
                // Tunabandika IServiceProviderFactory<IServiceCollection> yetu
                // wenyewe ili container ijikague YOTE wakati wa kuanza.
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IServiceProviderFactory<IServiceCollection>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IServiceProviderFactory<IServiceCollection>>(
                    _ => new DefaultServiceProviderFactory(new ServiceProviderOptions
                    {
                        ValidateOnBuild = true,
                        ValidateScopes = true
                    }));
            });
        }
    }

    [Fact]
    public void Kila_Controller_Inaweza_Kuundwa_Kutoka_DI()
    {
        // Kuanza API halisi. Ikiwa kuna service yoyote isiyosajiliwa,
        // hapa ndipo itakapotupa - kwa sababu ValidateOnBuild = true.
        using var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();

        var controllers = typeof(Program).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name)
            .ToList();

        Assert.True(controllers.Count >= 10,
            $"Nimepata controllers {controllers.Count} tu - labda namespace/references zimebadilika.");

        var failures = new List<string>();
        foreach (var controller in controllers)
        {
            try
            {
                var instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller);
                Assert.NotNull(instance);
            }
            catch (Exception ex)
            {
                failures.Add($"{controller.Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Controllers hizi HAZIWEZI kuundwa - kuna service isiyojisajiliwa kwenye Program.cs. " +
            "Hii HAIONEKANI kwa `dotnet build`; endpoint zake zote zingeleta HTTP 400/500 " +
            "kwa kila ombi.\n\n  - " + string.Join("\n  - ", failures));
    }

    [Fact]
    public void Kila_Service_Ya_Biashara_Inaweza_Kuundwa()
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();

        // Baadhi ya "services" ni static helper classes - hazipaswi kusajiliwa.
        var staticOnly = new[]
        {
            nameof(ApprovalPolicyResolver),
            nameof(MemberNameMatcher),
            nameof(ContributionExpectationCalculator)
        };

        var serviceTypes = typeof(ComplianceReportService).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                        && t.Namespace == "ChamaLink.Infrastructure.Services"
                        && t.Name.EndsWith("Service")
                        && !staticOnly.Contains(t.Name))
            .OrderBy(t => t.Name)
            .ToList();

        Assert.True(serviceTypes.Count >= 15,
            $"Nimepata services {serviceTypes.Count} tu - labda namespace imebadilika.");

        var failures = new List<string>();
        foreach (var t in serviceTypes)
        {
            try { Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, t)); }
            catch (Exception ex) { failures.Add($"{t.Name}: {ex.Message}"); }
        }

        Assert.True(failures.Count == 0,
            "Services hizi hazijajisajiliwa au zina utegemezi unaokosekana:\n  - " +
            string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Ulinzi wa ziada: API lazima ianze na kurudisha 401 (si 500) kwa endpoint
    /// inayohitaji token. Hii inathibitisha kuwa pipeline nzima ya
    /// Authentication + Authorization + middleware imeunganishwa.
    /// </summary>
    [Fact]
    public async Task Endpoint_Ya_Ripoti_Bila_Token_Inarudisha_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Reports/member-registry/" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Ulinzi wa usalama (C-3): mfumo LAZIMA ukatae kuanza ukiwa na JWT key
    /// ile ile iliyoingia kwenye GitHub ya umma. Hii inazuia mtu kurudisha
    /// key iliyovuja kwa bahati mbaya.
    /// </summary>
    [Fact]
    public void Mfumo_Unakataa_Key_Iliyoingia_Kwenye_GitHub()
    {
        using var bad = new BadKeyFactory();

        var ex = Assert.ThrowsAny<Exception>(() => bad.CreateClient());

        // Kusanya ujumbe wote wa nested exceptions
        var all = new List<string>();
        for (Exception? e = ex; e != null; e = e.InnerException) all.Add(e.Message);
        if (ex is AggregateException agg)
            foreach (var inner in agg.Flatten().InnerExceptions) all.Add(inner.Message);
        var combined = string.Join(" | ", all);

        Assert.Contains("GitHub", combined);
    }

    private class BadKeyFactory : WebApplicationFactory<Program>
    {
        // Kumbuka: hii inaweka JWT key ILE ILE iliyoingia kwenye GitHub ya umma
        // (tangu commit 43b80e3). Program.cs lazima IKATAE kuanza - ndiyo
        // ulinzi wa mwisho dhidi ya mtu kuirudisha key iliyovuja kwa bahati mbaya.
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "ChamaLinkSandboxSecretKey2024ForTestingOnlyMinimum32Bytes!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;Port=1;Database=no_db;Username=none;Password=none"
                }));
            return base.CreateHost(builder);
        }
    }
}
