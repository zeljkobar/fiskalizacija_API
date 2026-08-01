using System.Globalization;
using System.Text.RegularExpressions;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record FiscalQrCodeInputV5(
    string Environment,
    string Iic,
    string IssuerTin,
    DateTimeOffset IssueDateTime,
    int InvoiceOrdinalNumber,
    string BusinessUnitCode,
    string TcrCode,
    string SoftwareCode,
    decimal TotalPrice);

public interface IFiscalQrCodeGeneratorV5
{
    string GenerateVerificationUrl(FiscalQrCodeInputV5 input);
}

public sealed partial class FiscalQrCodeGeneratorV5 : IFiscalQrCodeGeneratorV5
{
    public const string TestVerificationBaseUrl = "https://efitest.tax.gov.me/ic/#/verify";
    public const string ProductionVerificationBaseUrl = "https://mapr.tax.gov.me/ic/#/verify";

    public string GenerateVerificationUrl(FiscalQrCodeInputV5 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        var baseUrl = input.Environment.Trim().ToUpperInvariant() switch
        {
            "TEST" => TestVerificationBaseUrl,
            "PRODUCTION" or "PROD" => ProductionVerificationBaseUrl,
            _ => throw new ArgumentException(
                "Fiskalno okruženje mora biti Test ili Production.",
                nameof(input))
        };
        var created = input.IssueDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:sszzz",
            CultureInfo.InvariantCulture);
        var price = input.TotalPrice.ToString("0.00", CultureInfo.InvariantCulture);

        return $"{baseUrl}?iic={input.Iic}&tin={input.IssuerTin}" +
               $"&crtd={created}&ord={input.InvoiceOrdinalNumber}" +
               $"&bu={input.BusinessUnitCode}&cr={input.TcrCode}" +
               $"&sw={input.SoftwareCode}&prc={price}";
    }

    private static void Validate(FiscalQrCodeInputV5 input)
    {
        if (!IicPattern().IsMatch(input.Iic))
            throw new ArgumentException("IKOF mora imati 32 heksadecimalna znaka.", nameof(input));
        if (!TinPattern().IsMatch(input.IssuerTin))
            throw new ArgumentException("PIB/JMB izdavaoca nije ispravan.", nameof(input));
        if (input.InvoiceOrdinalNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Redni broj mora biti veći od nule.");

        ValidateCode(input.BusinessUnitCode, "Kod poslovne jedinice");
        ValidateCode(input.TcrCode, "Kod ENU");
        ValidateCode(input.SoftwareCode, "Kod softvera");

        if (input.TotalPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Ukupna cijena ne može biti negativna.");
    }

    private static void ValidateCode(string value, string fieldName)
    {
        if (!CodePattern().IsMatch(value))
            throw new ArgumentException($"{fieldName} nije ispravan.", nameof(value));
    }

    [GeneratedRegex("^[0-9A-Fa-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IicPattern();

    [GeneratedRegex("^[0-9]{8,13}$", RegexOptions.CultureInvariant)]
    private static partial Regex TinPattern();

    [GeneratedRegex("^[A-Za-z0-9]{1,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
