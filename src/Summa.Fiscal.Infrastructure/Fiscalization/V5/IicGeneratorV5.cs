using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record IicInputV5(
    string IssuerTin,
    DateTimeOffset IssueDateTime,
    int InvoiceOrdinalNumber,
    string BusinessUnitCode,
    string TcrCode,
    string SoftwareCode,
    decimal TotalPrice);

public sealed record IicGenerationResultV5(
    string Iic,
    string IicSignature,
    string CanonicalInput,
    string CertificateThumbprint,
    string Algorithm);

public interface IIicGeneratorV5
{
    IicGenerationResultV5 Generate(IicInputV5 input, X509Certificate2 certificate);
}

public sealed class IicGeneratorV5 : IIicGeneratorV5
{
    public const string AlgorithmName = "UTF8|SHA256withRSA-PKCS1-v1_5|MD5";

    public IicGenerationResultV5 Generate(
        IicInputV5 input,
        X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(certificate);
        Validate(input);

        using var privateKey = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException(
                "Sertifikat nema RSA privatni ključ potreban za IKOF.");

        var canonicalInput = BuildCanonicalInput(input);
        var inputBytes = Encoding.UTF8.GetBytes(canonicalInput);
        var signatureBytes = privateKey.SignData(
            inputBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var iicSignature = Convert.ToHexString(signatureBytes);
        var iic = Convert.ToHexString(MD5.HashData(signatureBytes));

        return new(
            iic,
            iicSignature,
            canonicalInput,
            certificate.Thumbprint,
            AlgorithmName);
    }

    public static string BuildCanonicalInput(IicInputV5 input) =>
        string.Join(
            '|',
            input.IssuerTin,
            FormatDateTime(input.IssueDateTime),
            input.InvoiceOrdinalNumber.ToString(CultureInfo.InvariantCulture),
            input.BusinessUnitCode,
            input.TcrCode,
            input.SoftwareCode,
            input.TotalPrice.ToString("0.00", CultureInfo.InvariantCulture));

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static void Validate(IicInputV5 input)
    {
        if (input.IssuerTin.Length is not (8 or 13) ||
            !input.IssuerTin.All(char.IsAsciiDigit))
            throw new ArgumentException("PIB/JMB izdavaoca nije ispravan.", nameof(input));
        if (input.InvoiceOrdinalNumber <= 0)
            throw new ArgumentException("Redni broj računa mora biti veći od nule.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.BusinessUnitCode))
            throw new ArgumentException("Kod poslovne jedinice je obavezan.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.TcrCode))
            throw new ArgumentException("Kod ENU/TCR je obavezan.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.SoftwareCode))
            throw new ArgumentException("Kod softvera je obavezan.", nameof(input));
    }
}
