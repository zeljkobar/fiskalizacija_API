using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Application.Onboarding;

namespace Summa.Fiscal.Infrastructure.Certificates;

public sealed class FiscalCertificateVaultOptions
{
    public const string SectionName = "Fiscalization:CertificateVault";
    public string RootPath { get; init; } = "App_Data/Certificates";
    public string MasterKeyBase64 { get; init; } = string.Empty;
}

public sealed class EncryptedFileCertificateVault(IOptions<FiscalCertificateVaultOptions> options)
    : IFiscalCertificateVault
{
    private readonly FiscalCertificateVaultOptions _options = options.Value;

    public async Task<string> StoreAsync(Guid companyId, Guid certificateId, byte[] pfxBytes, string password, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(_options.RootPath);
        var directory = Path.Combine(root, companyId.ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, certificateId.ToString("N") + ".pfx.enc");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new VaultPayload(Convert.ToBase64String(pfxBytes), password));
        var encrypted = Encrypt(payload, GetMasterKey());
        await File.WriteAllBytesAsync(path, encrypted, cancellationToken);
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    public async Task<(byte[] PfxBytes, string Password)> LoadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(_options.RootPath);
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new FiscalOnboardingException("CERTIFICATE_STORAGE_KEY_INVALID", "Skladišni ključ sertifikata nije ispravan.");
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        var payload = JsonSerializer.Deserialize<VaultPayload>(Decrypt(encrypted, GetMasterKey()))
            ?? throw new FiscalOnboardingException("CERTIFICATE_STORAGE_CORRUPTED", "Skladišni zapis sertifikata nije čitljiv.");
        return (Convert.FromBase64String(payload.PfxBase64), payload.Password);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(_options.RootPath);
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new FiscalOnboardingException("CERTIFICATE_STORAGE_KEY_INVALID", "Skladišni ključ sertifikata nije ispravan.");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private byte[] GetMasterKey()
    {
        if (string.IsNullOrWhiteSpace(_options.MasterKeyBase64))
            throw new FiscalOnboardingException("CERTIFICATE_VAULT_KEY_MISSING", "Glavni ključ skladišta sertifikata nije konfigurisan.");
        try
        {
            var key = Convert.FromBase64String(_options.MasterKeyBase64);
            if (key.Length != 32) throw new FormatException();
            return key;
        }
        catch (FormatException)
        {
            throw new FiscalOnboardingException("CERTIFICATE_VAULT_KEY_INVALID", "Glavni ključ mora biti Base64 vrijednost od 32 bajta.");
        }
    }

    private static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes("SUMMA-FISCAL-CERT-V1"));
        return [.. nonce, .. tag, .. ciphertext];
    }

    private static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        if (encrypted.Length < 29) throw new FiscalOnboardingException("CERTIFICATE_STORAGE_CORRUPTED", "Skladišni zapis sertifikata nije ispravan.");
        var nonce = encrypted.AsSpan(0, 12);
        var tag = encrypted.AsSpan(12, 16);
        var ciphertext = encrypted.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes("SUMMA-FISCAL-CERT-V1"));
            return plaintext;
        }
        catch (CryptographicException)
        {
            throw new FiscalOnboardingException("CERTIFICATE_STORAGE_DECRYPTION_FAILED", "Sertifikat nije moguće dešifrovati.");
        }
    }

    private sealed record VaultPayload(string PfxBase64, string Password);
}
