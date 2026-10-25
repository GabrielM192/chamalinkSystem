using System.Globalization;
using System.Runtime.CompilerServices;
using ChamaLink.Infrastructure.Services.Common;
using Xunit;

namespace ChamaLink.Tests;

/// <summary>Majaribio yanaendeshwa kwa culture ile ile ambayo API inatumia (Program.cs → AppCulture.UseInvariant).</summary>
internal static class TestCultureInit
{
    [ModuleInitializer]
    internal static void Init() => AppCulture.UseInvariant();
}

/// <summary>
/// SASISHO17 (kosa lililoonekana kwenye Kali ya mtumiaji yenye locale ya nafasi kama kitenganishi cha maelfu):
/// maandishi ya seva lazima yasitegemee locale ya mashine.
/// </summary>
public class CultureIndependenceTests
{
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("sw-TZ")]
    [InlineData("ru-RU")]
    public void Server_Number_And_Date_Formatting_Does_Not_Depend_On_Machine_Locale(string machineLocale)
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(machineLocale);
            AppCulture.UseInvariant();
            Assert.Equal("45,000", $"{45000m:N0}");
            Assert.Equal("1,354,000.50", $"{1354000.5m:N2}");
            Assert.Equal("05/08/2026", $"{new DateTime(2026, 8, 5):dd/MM/yyyy}");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    [Fact]
    public void Api_Startup_Pins_Invariant_Culture_Before_Anything_Else()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "ChamaLink.API", "Program.cs"));
        var pin = program.IndexOf("AppCulture.UseInvariant();", StringComparison.Ordinal);
        Assert.True(pin >= 0, "Program.cs haiiti AppCulture.UseInvariant()");
        Assert.True(pin < program.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal), "Culture iwekwe kabla ya CreateBuilder");
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "ChamaLink.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("ChamaLink.sln haikupatikana");
    }
}

/// <summary>
/// SASISHO17 (mapitio ya nje): uamuzi wa "je ni kiongozi?" lazima upite MemberAccess (hai + kipindi cha kusubiri),
/// si `Role is GroupRole.Treasurer or …` ghafi — hata kwenye njia za legacy (zinaweza kuwashwa kwenye mazingira ya uhamishaji).
/// </summary>
public class NoRawLeaderRoleChecksTests
{
    [Fact]
    public void Leader_Decisions_Use_MemberAccess_Not_Raw_Role()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ChamaLink.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var raw = new System.Text.RegularExpressions.Regex(@"\.Role\s+is\s+GroupRole\.(Treasurer|Chairperson|Secretary)\s+or\s+GroupRole\.");
        var offenders = new[] { "ChamaLink.API", "ChamaLink.Infrastructure" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root!.FullName, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => raw.IsMatch(x.l) && !x.l.TrimStart().StartsWith("//"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "Ukaguzi wa nafasi ghafi (tumia MemberAccess.IsEffectiveLeader): " + string.Join(", ", offenders));
    }
}
