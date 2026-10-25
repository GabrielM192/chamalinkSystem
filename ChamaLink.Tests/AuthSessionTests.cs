using ChamaLink.Application.DTOs;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Tests;

/// <summary>SASISHO15 (P2): usajili usiofichua, kufungiwa kwenye DB, refresh token inayozungushwa + kugundua wizi.</summary>
public class AuthSessionTests
{
    private static (ApplicationDbContext Db, AuthService Auth) Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('k', 64), ["Jwt:Issuer"] = "i", ["Jwt:Audience"] = "a", ["Auth:MaxLoginFailures"] = "3"
        }).Build();
        return (db, new AuthService(db, cfg));
    }

    private static RegisterDto Reg(string email, string pw = "Siri-Nzuri-123") => new("Asha Juma", email, "0712345678", pw);

    [Fact]
    public async Task Register_Returns_Same_Generic_Answer_For_New_And_Existing_Email_And_No_Token()
    {
        var (db, auth) = Build();
        var a = await auth.RegisterAsync(Reg("asha@example.com"));
        var b = await auth.RegisterAsync(Reg("ASHA@example.com", "Nyingine-456"));
        Assert.Equal(a, b);
        Assert.Equal(1, await db.Users.CountAsync());
        // Neno la siri la akaunti halikubadilishwa na usajili wa pili
        await auth.LoginAsync(new LoginDto("asha@example.com", "Siri-Nzuri-123"));
        await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("asha@example.com", "Nyingine-456")));
    }

    [Fact]
    public async Task Lockout_Is_Stored_In_Database_And_Blocks_Even_Correct_Password()
    {
        var (db, auth) = Build();
        await auth.RegisterAsync(Reg("b@example.com"));
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("b@example.com", "kosa"), "10.0.0.1"));
        // SASISHO17: kufungiwa ni kwa (akaunti + IP), kumehifadhiwa kwenye DB (IP ikiwa hash, si wazi)
        var lf = await db.LoginFailures.AsNoTracking().SingleAsync();
        Assert.NotNull(lf.LockoutEndUtc);
        Assert.DoesNotContain("10.0.0.1", lf.IpKey);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("b@example.com", "Siri-Nzuri-123"), "10.0.0.1"));
        // Ujumbe ULE ULE kwa neno baya / imefungwa / email isiyojulikana (hakuna kufichua dakika wala kuwepo kwa akaunti)
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("hayupo@example.com", "x"), "10.0.0.1"));
        Assert.Equal(AuthService.BadCredentialsMessage, ex.Message);
        Assert.Equal(AuthService.BadCredentialsMessage, unknown.Message);

        // Mwenye akaunti kutoka IP NYINGINE hafungiwi na mshambuliaji wa IP ya kwanza
        await auth.LoginAsync(new LoginDto("b@example.com", "Siri-Nzuri-123"), "10.9.9.9");
    }

    [Fact]
    public async Task S17_Attempts_Against_A_Locked_Ip_Do_Not_Count_Toward_Account_Lock_And_Are_Not_Verified()
    {
        var (db, auth) = Build();   // MaxLoginFailures 3 → AccountWideMaxFailures 12
        await auth.RegisterAsync(Reg("e@example.com"));
        // Mshambuliaji wa IP moja: majaribio 50. Ni 3 tu yanayokaguliwa (na kuhesabiwa kwa akaunti); mengine yanakataliwa mapema.
        for (var i = 0; i < 50; i++)
            await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("e@example.com", "kosa"), "10.3.0.1"));
        var u = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(3, u.FailedLoginCount);
        Assert.Null(u.LockoutEndUtc);   // akaunti nzima HAIJAFUNGWA → mwenye akaunti hajafungiwa na mshambuliaji (NEW-5)
        await auth.LoginAsync(new LoginDto("e@example.com", "Siri-Nzuri-123"), "10.4.0.1");
        Assert.Equal(0, (await db.Users.AsNoTracking().SingleAsync()).FailedLoginCount);
    }

    [Fact]
    public async Task AccountWide_Limit_Locks_Distributed_Guessing_From_Many_Ips()
    {
        var (db, auth) = Build();   // MaxLoginFailures 3 → AccountWideMaxFailures (default) 12
        await auth.RegisterAsync(Reg("d@example.com"));
        for (var i = 0; i < 12; i++)
            await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("d@example.com", "kosa"), $"10.1.0.{i}"));
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync()).LockoutEndUtc);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => auth.LoginAsync(new LoginDto("d@example.com", "Siri-Nzuri-123"), "10.2.0.1"));
        Assert.Equal(AuthService.BadCredentialsMessage, ex.Message);
    }

    [Fact]
    public async Task Refresh_Rotates_And_Reuse_Of_Old_Token_Revokes_The_Whole_Session_Family()
    {
        var (db, auth) = Build();
        await auth.RegisterAsync(Reg("c@example.com"));
        var s1 = await auth.LoginAsync(new LoginDto("c@example.com", "Siri-Nzuri-123"));
        Assert.Equal(15 * 60, s1.Auth.ExpiresInSeconds); // access token ya muda mfupi
        var s2 = await auth.RefreshAsync(s1.RefreshToken);
        Assert.NotEqual(s1.RefreshToken, s2.RefreshToken);
        // Mwizi anatumia token ya zamani → familia nzima inafungwa, hata token mpya ya mwenye akaunti
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync(s1.RefreshToken));
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync(s2.RefreshToken));
        Assert.DoesNotContain(await db.RefreshTokens.AsNoTracking().ToListAsync(), t => t.RevokedAt == null);
        Assert.DoesNotContain(await db.RefreshTokens.AsNoTracking().ToListAsync(), t => t.TokenHash == s2.RefreshToken); // hash tu
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync("hakuna-kitu"));
    }

    [Fact]
    public async Task Logout_And_Revoke_All_End_Sessions()
    {
        var (db, auth) = Build();
        await auth.RegisterAsync(Reg("d@example.com"));
        var s1 = await auth.LoginAsync(new LoginDto("d@example.com", "Siri-Nzuri-123"));
        await auth.LogoutAsync(s1.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync(s1.RefreshToken));

        var s2 = await auth.LoginAsync(new LoginDto("d@example.com", "Siri-Nzuri-123"));
        var s3 = await auth.LoginAsync(new LoginDto("d@example.com", "Siri-Nzuri-123"));
        var user = await db.Users.AsNoTracking().SingleAsync();
        await auth.RevokeAllSessionsAsync(user.Id);
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync(s2.RefreshToken));
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync(s3.RefreshToken));
        Assert.Equal(user.TokenVersion + 1, (await db.Users.AsNoTracking().SingleAsync()).TokenVersion);
    }
}
