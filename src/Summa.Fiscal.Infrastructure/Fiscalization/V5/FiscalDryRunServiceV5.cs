using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Summa.Fiscal.Infrastructure.Certificates;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record FiscalDryRunInputV5(
    int InvoiceOrdinalNumber,
    DateTimeOffset IssueDateTime,
    string ItemName,
    decimal NetAmount,
    decimal VatRate);

public sealed record FiscalDryRunResultV5(
    Guid RequestUuid,
    string InvoiceNumber,
    string Iic,
    string SignedRequestXml,
    string SoapEnvelopeXml,
    bool SignatureVerified,
    bool XsdValid);

public interface IFiscalDryRunServiceV5
{
    FiscalDryRunResultV5 Create(
        FiscalDryRunInputV5 input,
        PuFiscalizationOptionsV5 configuration,
        X509Certificate2 certificate,
        string schemaPath);
}

public sealed class FiscalDryRunServiceV5(
    IIicGeneratorV5 iicGenerator,
    IRegisterInvoiceXmlBuilderV5 xmlBuilder,
    IFiscalXmlSignerV5 xmlSigner,
    ISoapEnvelopeV5 soapEnvelope) : IFiscalDryRunServiceV5
{
    public FiscalDryRunResultV5 Create(
        FiscalDryRunInputV5 input,
        PuFiscalizationOptionsV5 configuration,
        X509Certificate2 certificate,
        string schemaPath)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(certificate);
        configuration.EnsureReadyForInvoice();

        if (input.InvoiceOrdinalNumber <= 0)
            throw new ArgumentException("Redni broj računa mora biti veći od nule.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.ItemName))
            throw new ArgumentException("Naziv stavke je obavezan.", nameof(input));
        if (input.NetAmount <= 0)
            throw new ArgumentException("Neto iznos mora biti veći od nule.", nameof(input));
        if (input.VatRate < 0)
            throw new ArgumentException("PDV stopa ne može biti negativna.", nameof(input));

        var vatAmount = decimal.Round(
            input.NetAmount * input.VatRate / 100m,
            2,
            MidpointRounding.AwayFromZero);
        var total = input.NetAmount + vatAmount;
        var invoiceNumber =
            $"{configuration.BusinessUnitCode}/{input.InvoiceOrdinalNumber}/{input.IssueDateTime.Year}/{configuration.TcrCode}";
        var requestUuid = Guid.NewGuid();

        var iic = iicGenerator.Generate(
            new(
                configuration.IssuerTin,
                input.IssueDateTime,
                input.InvoiceOrdinalNumber,
                configuration.BusinessUnitCode,
                configuration.TcrCode,
                configuration.SoftwareCode,
                total),
            certificate);

        var request = new RegisterInvoiceRequestV5(
            new(requestUuid, input.IssueDateTime),
            new(
                PuInvoiceTypeV5.Cash,
                input.IssueDateTime,
                invoiceNumber,
                input.InvoiceOrdinalNumber,
                configuration.TcrCode,
                configuration.IsIssuerInVat,
                input.NetAmount,
                vatAmount,
                total,
                configuration.OperatorCode,
                configuration.BusinessUnitCode,
                configuration.SoftwareCode,
                iic.Iic,
                iic.IicSignature,
                new(
                    PuIdTypeV5.Tin,
                    configuration.IssuerTin,
                    configuration.SellerName,
                    configuration.SellerAddress,
                    configuration.SellerTown,
                    configuration.SellerCountry),
                [new(PuPaymentMethodV5.Banknote, total)],
                [
                    new(
                        input.ItemName,
                        "kom",
                        1m,
                        input.NetAmount,
                        total,
                        input.NetAmount,
                        total,
                        VatRate: input.VatRate,
                        VatAmount: vatAmount)
                ],
                [new(1, input.NetAmount, input.VatRate, vatAmount)],
                DocumentType: PuInvoiceDocumentTypeV5.Invoice));

        var signed = xmlSigner.SignRequest(xmlBuilder.BuildUnsigned(request), certificate);
        var schemaValidation = new FiscalXmlSchemaValidatorV5(schemaPath)
            .Validate(signed.SignedDocument);
        if (!schemaValidation.IsValid)
        {
            throw new InvalidOperationException(
                $"Dry-run XML nije prošao PU XSD: {string.Join("; ", schemaValidation.Errors.Select(error => error.Message))}");
        }

        var wrapped = soapEnvelope.Wrap(signed.SignedDocument);
        return new(
            requestUuid,
            invoiceNumber,
            iic.Iic,
            signed.SignedDocument.ToString(SaveOptions.DisableFormatting),
            wrapped.ToString(SaveOptions.DisableFormatting),
            signed.SignatureVerified,
            schemaValidation.IsValid);
    }
}
