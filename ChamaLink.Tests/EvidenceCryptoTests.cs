using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Tests;

public class EvidenceCryptoTests
{
    private static IConfiguration Cfg(string dir, string? key) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Benefits:EvidencePath"] = dir, ["Benefits:EvidenceEncryptionKey"] = key
    }).Build();

    [Fact]
    public async Task Evidence_Is_Encrypted_At_Rest_And_Legacy_Plain_Files_Still_Read()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clenc-" + Guid.NewGuid().ToString("N"));
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nCHETI CHA KIFO\n%%EOF");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf)).ToLowerInvariant();
        var gid = Guid.NewGuid();
        try
        {
            // Faili la zamani (bila usimbaji) lililohifadhiwa kabla ya ufunguo kuwekwa
            var legacy = new LocalDiskEvidenceStorage(Cfg(dir, null));
            var k0 = await legacy.SaveAsync(gid, sha, ".pdf", pdf, default);
            Assert.Equal(pdf, await File.ReadAllBytesAsync(Path.Combine(dir, k0)));

            var storage = new LocalDiskEvidenceStorage(Cfg(dir, key));
            Assert.Equal(pdf, await storage.ReadAsync(k0, default)); // bado linasomeka

            var pdf2 = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nCHETI CHA NDOA\n%%EOF");
            var sha2 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf2)).ToLowerInvariant();
            var k1 = await storage.SaveAsync(gid, sha2, ".pdf", pdf2, default);
            var onDisk = await File.ReadAllBytesAsync(Path.Combine(dir, k1));
            Assert.True(EvidenceCrypto.IsEncrypted(onDisk));
            Assert.DoesNotContain("CHETI", System.Text.Encoding.ASCII.GetString(onDisk));
            Assert.Equal(pdf2, await storage.ReadAsync(k1, default));

            // Ufunguo mwingine / bila ufunguo → haifunguki (si data iliyoharibika kimya kimya)
            var other = new LocalDiskEvidenceStorage(Cfg(dir, Convert.ToBase64String(new byte[32])));
            await Assert.ThrowsAsync<ConflictException>(() => other.ReadAsync(k1, default));
            await Assert.ThrowsAsync<ConflictException>(() => legacy.ReadAsync(k1, default));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
