using System.Security.Cryptography;
using ChamaLink.Domain.Exceptions;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Benefits;

/// <summary>
/// Hifadhi ya faili za ushahidi (vyeti). Kiolesura hiki kinaruhusu kubadilisha kwenda object storage
/// (S3 / R2 / MinIO) bila kugusa mantiki ya mafao. Faili HAZIHIFADHIWI ndani ya database wala folda ya umma.
/// </summary>
public interface IEvidenceStorage
{
    Task<string> SaveAsync(Guid groupId, string sha256, string extension, byte[] content, CancellationToken ct);
    Task<byte[]> ReadAsync(string storageKey, CancellationToken ct);
}

/// <summary>
/// Hifadhi ya diski ya server. Njia inatoka kwenye config (Benefits:EvidencePath) — hakuna njia iliyoandikwa kwenye code.
/// Jina la faili = SHA-256 yake, kwa hiyo faili haliwezi kubadilishwa kimya kimya (hash ikibadilika, halipatikani).
/// </summary>
public sealed class LocalDiskEvidenceStorage : IEvidenceStorage
{
    private readonly IConfiguration _config;

    public LocalDiskEvidenceStorage(IConfiguration config) => _config = config;

    // Inasomwa wakati wa kutumia (si wakati wa kuundwa) ili sehemu nyingine za API zifanye kazi hata kama bado haijawekwa
    private string _root
    {
        get
        {
            var path = _config["Benefits:EvidencePath"];
            if (string.IsNullOrWhiteSpace(path))
                throw new ConflictException("Hifadhi salama ya faili haijasanidiwa kikamilifu kwenye server. Wasiliana na msimamizi wa server." /* SASISHO17: jina la config halitajwi kwenye jibu */);
            return Path.GetFullPath(path);
        }
    }

    public async Task<string> SaveAsync(Guid groupId, string sha256, string extension, byte[] content, CancellationToken ct)
    {
        var root = _root;
        var dir = Path.Combine(root, groupId.ToString("N"));
        Directory.CreateDirectory(dir);
        var key = $"{groupId:N}/{sha256}{extension}";
        var full = Path.Combine(root, key);
        if (!File.Exists(full)) await File.WriteAllBytesAsync(full, EvidenceCrypto.Protect(_config, key, content), ct);
        return key;
    }

    public async Task<byte[]> ReadAsync(string storageKey, CancellationToken ct)
    {
        var root = _root;
        var full = Path.GetFullPath(Path.Combine(root, storageKey));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ForbiddenException("Njia ya faili si halali.");
        if (!File.Exists(full)) throw new NotFoundException("Faili la ushahidi halipatikani kwenye hifadhi.");
        var bytes = EvidenceCrypto.Unprotect(_config, storageKey, await File.ReadAllBytesAsync(full, ct));
        // Uadilifu: faili lazima liwe na hash ile ile iliyorekodiwa (jina lake)
        var expected = Path.GetFileNameWithoutExtension(full);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Faili la ushahidi limebadilishwa baada ya kupakiwa (hash hailingani). Wajulishe viongozi wote.");
        return bytes;
    }
}

/// <summary>
/// SASISHO15 (P3): usimbaji wa vyeti vikiwa kwenye diski (AES-256-GCM). Ufunguo unatoka config
/// "Benefits:EvidenceEncryptionKey" (base64 ya byte 32 — weka kama environment variable, SI kwenye git).
/// Muundo wa faili: "CLENC1" | nonce(12) | tag(16) | ciphertext; AAD = storage key (faili haliwezi kuhamishwa kwa kesi nyingine).
/// Faili za zamani (bila "CLENC1") zinasomwa kama zilivyo — hakuna uhamisho unaohitajika. Bila ufunguo, faili mpya
/// zinahifadhiwa bila usimbaji (kama zamani) ili mfumo usisimame; ukaguzi wa hash unabaki kwa zote.
/// </summary>
public static class EvidenceCrypto
{
    public static readonly byte[] Magic = "CLENC1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16;

    public static byte[]? Key(IConfiguration config)
    {
        var raw = config["Benefits:EvidenceEncryptionKey"];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        byte[] key;
        try { key = Convert.FromBase64String(raw.Trim()); }
        catch (FormatException) { throw new ConflictException("Hifadhi salama ya faili haijasanidiwa kikamilifu kwenye server. Wasiliana na msimamizi wa server." /* SASISHO17: jina la config halitajwi kwenye jibu */); }
        if (key.Length != 32) throw new ConflictException("Hifadhi salama ya faili haijasanidiwa kikamilifu kwenye server. Wasiliana na msimamizi wa server." /* SASISHO17: jina la config halitajwi kwenye jibu */);
        return key;
    }

    public static bool IsEncrypted(byte[] data) => data.Length >= Magic.Length + NonceSize + TagSize && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static byte[] Protect(IConfiguration config, string storageKey, byte[] plain)
    {
        var key = Key(config);
        if (key is null) return plain;
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag, System.Text.Encoding.UTF8.GetBytes(storageKey));
        var output = new byte[Magic.Length + NonceSize + TagSize + cipher.Length];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, Magic.Length);
        tag.CopyTo(output, Magic.Length + NonceSize);
        cipher.CopyTo(output, Magic.Length + NonceSize + TagSize);
        return output;
    }

    public static byte[] Unprotect(IConfiguration config, string storageKey, byte[] stored)
    {
        if (!IsEncrypted(stored)) return stored; // faili la zamani (kabla ya usimbaji)
        var keys = DecryptionKeys(config);
        if (keys.Count == 0) throw new ConflictException("Hifadhi salama ya faili haijasanidiwa kikamilifu kwenye server. Wasiliana na msimamizi wa server." /* SASISHO17: jina la config halitajwi kwenye jibu */);
        var nonce = stored.AsSpan(Magic.Length, NonceSize);
        var tag = stored.AsSpan(Magic.Length + NonceSize, TagSize);
        var cipher = stored.AsSpan(Magic.Length + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        var aad = System.Text.Encoding.UTF8.GetBytes(storageKey);
        // SASISHO17 (M): kubadilisha ufunguo bila kupoteza faili — ufunguo wa sasa kwanza, kisha wa zamani (PreviousKeys).
        foreach (var key in keys)
        {
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain, aad);
                return plain;
            }
            catch (CryptographicException) { /* jaribu ufunguo unaofuata */ }
        }
        throw new ConflictException("Faili la ushahidi haliwezi kufunguliwa (ufunguo si sahihi au faili limeharibiwa). Wajulishe viongozi wote.");
    }

    /// <summary>Config ya funguo za zamani (zinatumika KUFUNGUA tu, kamwe kusimba): base64 za byte 32, zimetenganishwa kwa koma.</summary>
    public const string PreviousKeysKey = "Benefits:EvidenceEncryptionPreviousKeys";

    public static List<byte[]> DecryptionKeys(IConfiguration config)
    {
        var list = new List<byte[]>();
        var current = Key(config);
        if (current is not null) list.Add(current);
        foreach (var part in (config[PreviousKeysKey] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            byte[] k;
            try { k = Convert.FromBase64String(part); } catch (FormatException) { continue; }
            if (k.Length == 32) list.Add(k);
        }
        return list;
    }
}

/// <summary>Ukaguzi wa faili: aina halisi kwa "magic bytes" (si kwa jina), ukubwa kutoka config.</summary>
public static class EvidenceFileRules
{
    public sealed record Detected(string ContentType, string Extension);

    public static Detected Detect(byte[] b)
    {
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return new("application/pdf", ".pdf");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return new("image/jpeg", ".jpg");
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return new("image/png", ".png");
        throw new ValidationException("Faili la ushahidi liwe PDF, JPG au PNG (picha ya cheti au scan).");
    }

    public static long MaxBytes(IConfiguration config)
    {
        if (!long.TryParse(config["Benefits:MaxEvidenceBytes"], out var max) || max <= 0)
            throw new ConflictException("Hifadhi salama ya faili haijasanidiwa kikamilifu kwenye server. Wasiliana na msimamizi wa server.");
        return max;
    }
}
