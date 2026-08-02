using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Summa.Fiscal.Application.Onboarding;

namespace Summa.Fiscal.Infrastructure.Certificates;

public sealed partial class FiscalCertificateInspector : IFiscalCertificateInspector
{
    public CertificateInspection Inspect(byte[] pfxBytes, string password)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            return new(
                certificate.Thumbprint,
                certificate.SerialNumber,
                certificate.Subject,
                certificate.Issuer,
                certificate.NotBefore.ToUniversalTime(),
                certificate.NotAfter.ToUniversalTime(),
                certificate.HasPrivateKey,
                ExtractTin(certificate.Subject));
        }
        catch (CryptographicException)
        {
            throw new FiscalOnboardingException("CERT_UPLOAD_INVALID_PASSWORD", "PFX/P12 fajl ili njegova lozinka nijesu ispravni.");
        }
    }

    private static string? ExtractTin(string subject)
    {
        var vatMe = VatMeTinPattern().Match(subject);
        if (vatMe.Success) return vatMe.Groups[1].Value;

        return TinPattern().Matches(subject)
            .Select(match => match.Groups[1].Value)
            .FirstOrDefault(value => value.Length == 8);
    }

    [GeneratedRegex("(?:SERIALNUMBER|OID\\.2\\.5\\.4\\.5)\\s*=\\s*VATME-?(\\d{8})", RegexOptions.IgnoreCase)]
    private static partial Regex VatMeTinPattern();

    [GeneratedRegex("(?:SERIALNUMBER|OID\\.2\\.5\\.4\\.5)\\s*=\\s*(?:[A-Z]+-?)?(\\d{8,13})", RegexOptions.IgnoreCase)]
    private static partial Regex TinPattern();
}
