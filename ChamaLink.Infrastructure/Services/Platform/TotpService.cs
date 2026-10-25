using System.Security.Cryptography;
using System.Text;

namespace ChamaLink.Infrastructure.Services.Platform;

/// <summary>
/// SASISHO20 — TOTP (RFC 6238) kwa MFA ya msimamizi wa mfumo.
///
/// Kwa nini tumeandika wenyewe badala ya kuongeza maktaba? Mfumo huu unapaswa
/// kujengwa bila kutegemea mtandao wakati wa ufungaji (sandbox, PC ya ofisi yenye
/// intaneti hafifu). Algorithm ni ndogo na imepimwa na vectors rasmi za RFC 6238
/// (angalia Sasisho20PlatformTests).
///
/// Siri huhifadhiwa kwa Base32 (kama Google Authenticator inavyotarajia) na
/// kusimbwa kabla ya kuandikwa DB (EvidenceCrypto — AES-GCM).
/// </summary>
public static class TotpService
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; // Base32 (RFC 4648)
    private const int OtpModulus = 1000000; // RFC 6238: code ya tarakimu 6 — SI kiasi cha fedha

    /// <summary>Siri mpya: bytes 20 (= 160 bit, kiwango cha RFC 4226) → herufi 32 za Base32.</summary>
    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string s)
    {
        var clean = s.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) throw new ArgumentException("Siri ya MFA si sahihi (Base32).");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>Code ya tarakimu 6 kwa hatua ya sekunde 30 (kama simu inavyoonyesha).</summary>
    public static string Code(string secretBase32, DateTime? utcNow = null, int step = 30)
    {
        var counter = (long)((utcNow ?? DateTime.UtcNow) - DateTime.UnixEpoch).TotalSeconds / step;
        return CodeForCounter(Base32Decode(secretBase32), counter);
    }

    private static string CodeForCounter(byte[] key, long counter)
    {
        Span<byte> msg = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { msg[i] = (byte)(counter & 0xFF); counter >>= 8; }
        var hash = HMACSHA1.HashData(key, msg);
        var offset = hash[^1] & 0x0F;
        var bin = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (bin % OtpModulus).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Uthibitisho unaokubali hatua moja nyuma/mbele (saa ya simu haiko sawa kabisa).
    /// Ulinganisho ni wa muda thabiti (hashing constant time).
    /// </summary>
    public static bool Verify(string secretBase32, string code, DateTime? utcNow = null, int window = 1, int step = 30)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var entered = new string(code.Where(char.IsDigit).ToArray());
        if (entered.Length != 6) return false;
        var key = Base32Decode(secretBase32);
        var nowCounter = (long)((utcNow ?? DateTime.UtcNow) - DateTime.UnixEpoch).TotalSeconds / step;
        for (var d = -window; d <= window; d++)
        {
            var expected = Encoding.ASCII.GetBytes(CodeForCounter(key, nowCounter + d));
            var given = Encoding.ASCII.GetBytes(entered);
            if (CryptographicOperations.FixedTimeEquals(expected, given)) return true;
        }
        return false;
    }

    /// <summary>URI ya otpauth:// — unaipachika kwenye Google Authenticator/Authy kwa QR au mkono.</summary>
    public static string OtpAuthUri(string issuer, string account, string secretBase32)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
           $"?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
}
