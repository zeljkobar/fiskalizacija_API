using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Summa.Fiscal.Infrastructure.Certificates;

public sealed record FiscalCertificateLoadOptions(
    bool RequireCurrentlyValid = true,
    string? ExpectedIssuerTin = null,
    X509KeyStorageFlags KeyStorageFlags = X509KeyStorageFlags.EphemeralKeySet);

public sealed class LoadedFiscalCertificate : IDisposable
{
    internal LoadedFiscalCertificate(X509Certificate2 certificate, string? issuerTin)
    {
        Certificate = certificate;
        IssuerTin = issuerTin;
    }

    public X509Certificate2 Certificate { get; }
    public string? IssuerTin { get; }
    public string Thumbprint => Certificate.Thumbprint;
    public string Subject => Certificate.Subject;
    public string Issuer => Certificate.Issuer;
    public DateTimeOffset ValidFrom => Certificate.NotBefore;
    public DateTimeOffset ValidTo => Certificate.NotAfter;
    public bool IsCurrentlyValid =>
        DateTime.Now >= Certificate.NotBefore && DateTime.Now <= Certificate.NotAfter;

    public RSA GetRequiredRsaPrivateKey() =>
        Certificate.GetRSAPrivateKey()
        ?? throw new FiscalCertificateException(
            "CERTIFICATE_RSA_PRIVATE_KEY_NOT_FOUND",
            "Sertifikat nema dostupan RSA privatni ključ.");

    public void Dispose() => Certificate.Dispose();
}

public interface IPfxCertificateLoader
{
    LoadedFiscalCertificate Load(
        string path,
        ReadOnlySpan<char> password,
        FiscalCertificateLoadOptions options);
}

public sealed partial class PfxCertificateLoader : IPfxCertificateLoader
{
    public LoadedFiscalCertificate Load(
        string path,
        ReadOnlySpan<char> password,
        FiscalCertificateLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FiscalCertificateException(
                "CERTIFICATE_FILE_NOT_FOUND",
                "PFX/P12 sertifikat nije pronađen.");
        }

        X509Certificate2 certificate;
        try
        {
            var pfxBytes = File.ReadAllBytes(path);
#pragma warning disable SYSLIB0057
            certificate = new X509Certificate2(
                pfxBytes,
                new string(password),
                options.KeyStorageFlags);
#pragma warning restore SYSLIB0057
        }
        catch (Exception exception)
            when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new FiscalCertificateException(
                "CERTIFICATE_LOAD_FAILED",
                "Sertifikat nije moguće učitati. Provjerite fajl i lozinku.",
                exception);
        }

        try
        {
            if (!certificate.HasPrivateKey)
            {
                throw new FiscalCertificateException(
                    "CERTIFICATE_PRIVATE_KEY_NOT_FOUND",
                    "Sertifikat ne sadrži privatni ključ.");
            }

            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is null)
            {
                throw new FiscalCertificateException(
                    "CERTIFICATE_RSA_PRIVATE_KEY_NOT_FOUND",
                    "Sertifikat nema RSA privatni ključ potreban za fiskalizaciju.");
            }

            if (options.RequireCurrentlyValid)
            {
                if (DateTime.Now < certificate.NotBefore)
                {
                    throw new FiscalCertificateException(
                        "CERTIFICATE_NOT_YET_VALID",
                        "Sertifikat još nije važeći.");
                }

                if (DateTime.Now > certificate.NotAfter)
                {
                    throw new FiscalCertificateException(
                        "CERTIFICATE_EXPIRED",
                        "Sertifikat je istekao.");
                }
            }

            var issuerTin = ExtractIssuerTin(certificate.Subject);
            if (!string.IsNullOrWhiteSpace(options.ExpectedIssuerTin) &&
                !string.Equals(
                    issuerTin,
                    options.ExpectedIssuerTin,
                    StringComparison.Ordinal))
            {
                throw new FiscalCertificateException(
                    "CERTIFICATE_TIN_MISMATCH",
                    "PIB iz sertifikata ne odgovara očekivanom PIB-u firme.");
            }

            return new LoadedFiscalCertificate(certificate, issuerTin);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    private static string? ExtractIssuerTin(string subject)
    {
        var match = VatMeTinRegex().Match(subject);
        return match.Success ? match.Groups["tin"].Value : null;
    }

    [GeneratedRegex(@"VATME-(?<tin>[0-9]{8})", RegexOptions.CultureInvariant)]
    private static partial Regex VatMeTinRegex();
}

public sealed class FiscalCertificateException : Exception
{
    public FiscalCertificateException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
