using System.Text.RegularExpressions;
using ChamaLink.Domain.Localization;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// SASISHO16: sera ya neno la siri kwa akaunti MPYA (mtindo wa NIST SP 800-63B):
///   urefu wa chini (config Auth:MinPasswordLength, chaguo-msingi 8), urefu wa juu 128 (BCrypt hutumia baiti 72 tu),
///   kukataa maneno ya siri yanayojulikana sana, herufi moja inayojirudia, na neno lenye barua pepe/namba ya simu ya mtumiaji.
///   Hakuna masharti ya "lazima alama/herufi kubwa" — urefu ndio ulinzi halisi. Kuingia kwa akaunti za zamani hakuguswi.
/// Ujumbe ni wa jumla na hauelezi ni orodha gani imekataa.
/// </summary>
public static class PasswordPolicy
{
    public const int MaxLength = 128;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passw0rd", "12345678", "123456789", "1234567890", "87654321",
        "11111111", "00000000", "12341234", "qwertyui", "qwerty123", "qwertyuiop", "asdfghjk", "abcd1234", "abc12345",
        "iloveyou", "letmein1", "welcome1", "welcome123", "admin123", "administrator", "football", "baseball",
        "sunshine", "princess", "trustno1", "superman", "monkey123", "dragon123", "changeme", "default1",
        "chamalink", "chamalink1", "chamalink123", "mkoba123", "m-koba123", "tanzania", "tanzania1", "tanzania123",
        "dar-es-salaam", "daressalaam", "kiswahili", "nenolasiri", "neno1234", "siri1234", "sirisiri", "karibu123",
        "mungu1234", "yesu1234", "jesus123", "allah123", "1q2w3e4r", "zaq12wsx", "q1w2e3r4", "pass1234",
    };

    public static int MinLength(int? configured) => configured is >= 8 and <= 64 ? configured.Value : 8;

    /// <returns>null kama neno linakubalika; vinginevyo ujumbe wa kumwonyesha mtumiaji.</returns>
    public static Loc? Validate(string? password, string? email, string? phone, int minLength)
    {
        if (string.IsNullOrEmpty(password) || password.Length < minLength)
            return L($"Neno la siri liwe angalau herufi {minLength}.");
        if (password.Length > MaxLength)
            return L($"Neno la siri lisizidi herufi {MaxLength}.");
        if (password.Distinct().Count() <= 2 || Common.Contains(password) || IsSequence(password))
            return L("Neno hili la siri ni rahisi kubahatisha. Chagua jingine (mf. sentensi fupi unayoikumbuka).");
        var local = (email ?? "").Split('@')[0];
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return L("Neno la siri lisiwe na barua pepe yako.");
        var digits = Regex.Replace(phone ?? "", "[^0-9]", "");
        if (digits.Length >= 9 && password.Contains(digits[^9..]))
            return L("Neno la siri lisiwe na namba yako ya simu.");
        return null;
    }

    private static Loc L(Loc l) => l;

    // mf. "12345678", "abcdefgh", "87654321"
    private static bool IsSequence(string p)
    {
        if (p.Length < 2) return false;
        int step = p[1] - p[0];
        if (Math.Abs(step) != 1) return false;
        for (int i = 2; i < p.Length; i++) if (p[i] - p[i - 1] != step) return false;
        return true;
    }
}
