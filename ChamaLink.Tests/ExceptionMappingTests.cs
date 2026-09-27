using System.Net;
using ChamaLink.API.Middleware;
using ChamaLink.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>
/// Jaribio la GlobalExceptionMiddleware.
///
/// Hapa ndipo makosa yote ya biashara yanapogeuzwa kuwa HTTP status. Zamani
/// controllers zilikuwa na `catch (Exception) -> 400` ambazo zilizuia
/// middleware hii kufanya kazi kabisa, na zilibadilisha makosa ya server
/// kuwa "400 Bad Request". Sasa hii ndiyo njia moja ya kuamua status, kwa
/// hiyo ni muhimu kuiweka chini ya jaribio.
/// </summary>
public class ExceptionMappingTests
{
    private static async Task<(int Status, string Body)> RunAsync(Exception ex, bool isDevelopment = false)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var env = new StubEnv(isDevelopment);

        var middleware = new GlobalExceptionMiddleware(
            _ => throw ex,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            env);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        return (context.Response.StatusCode, body);
    }

    private sealed class StubEnv : IHostEnvironment
    {
        public StubEnv(bool dev) { EnvironmentName = dev ? "Development" : "Production"; }
        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "/tmp";
    }

    // ---------------------------------------------------------------------
    // FIX H-4: UnauthorizedAccessException lazima iwe 403, SI 401.
    // Kwa nini ni muhimu: chamalink-web-new/src/api/client.ts humrudisha
    // mtumiaji kwenye login kwa 401 yoyote. Mwanachama anayepigwa 401 kwa
    // "huna ruhusa" alikuwa anafukuzwa kwenye mfumo mzima.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task UnauthorizedAccessException_Inarudisha_403_Si_401()
    {
        var (status, _) = await RunAsync(new UnauthorizedAccessException("Wewe si mwanachama wa kikundi hiki."));

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.NotEqual((int)HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task ValidationException_Inarudisha_400()
    {
        var (status, body) = await RunAsync(new ValidationException("Kiasi cha mkopo lazima kiwe zaidi ya sifuri."));

        Assert.Equal(400, status);
        Assert.Contains("Kiasi cha mkopo", body);
    }

    [Fact]
    public async Task NotFoundException_Inarudisha_404()
    {
        var (status, _) = await RunAsync(new NotFoundException("Mkopo haukupatikana."));
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task ConflictException_Inarudisha_409()
    {
        var (status, _) = await RunAsync(new ConflictException("Nafasi ya Chairperson tayari ina mwanachama."));
        Assert.Equal(409, status);
    }

    [Fact]
    public async Task ForbiddenException_Inarudisha_403()
    {
        var (status, _) = await RunAsync(new ForbiddenException("Huna ruhusa."));
        Assert.Equal(403, status);
    }

    // ---------------------------------------------------------------------
    // Hii ndiyo ilificha bug ya ReportsController: InvalidOperationException
    // (ndiyo exception ya .NET DI wakati service haipo) ilikuwa inarudisha
    // 400 - yaani kosa la SERVER lilionekana kama kosa la MTUMIAJI.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task InvalidOperationException_Sasa_Inarudisha_500_Si_400()
    {
        var (status, _) = await RunAsync(
            new InvalidOperationException("Unable to resolve service for type 'ComplianceReportService'"));

        Assert.Equal(500, status);
        Assert.NotEqual(400, status);
    }

    [Fact]
    public async Task NullReferenceException_Inarudisha_500_Bila_Kuvujisha_Maelezo()
    {
        var (status, body) = await RunAsync(
            new NullReferenceException("Object reference not set - SecretTableName.PasswordHash"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("SecretTableName", body);   // maelezo ya ndani hayavuji
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_Inarudisha_409()
    {
        var (status, _) = await RunAsync(
            new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("row changed"));

        Assert.Equal(409, status);
    }

    // ---------------------------------------------------------------------
    // Ulinganifu na frontend: faili 7 kwenye frontend zinasoma
    // `err.response.data.message` (MkobaPage.tsx, MkobaUpload.tsx, n.k.).
    // Kwa kuwa sasa makosa yote yanapita kwenye middleware hii, lazima
    // iweke `message` pamoja na `detail` ya RFC 7807.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Jibu_Lina_Maeneo_Yote_Mawili_Message_Na_Detail()
    {
        var (_, body) = await RunAsync(new ValidationException("Ujumbe wa mtumiaji."));

        Assert.Contains("\"message\":\"Ujumbe wa mtumiaji.\"", body);
        Assert.Contains("\"detail\":\"Ujumbe wa mtumiaji.\"", body);
    }

    [Fact]
    public async Task StackTrace_Inaonekana_Katika_Development_Pekee()
    {
        var ex = new NullReferenceException("boom");

        var (_, prodBody) = await RunAsync(ex, isDevelopment: false);
        var (_, devBody) = await RunAsync(ex, isDevelopment: true);

        Assert.DoesNotContain("boom", prodBody);
        Assert.Contains("boom", devBody);
    }
}
