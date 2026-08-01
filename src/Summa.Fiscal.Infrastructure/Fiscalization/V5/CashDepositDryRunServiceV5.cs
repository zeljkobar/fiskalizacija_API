using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record CashDepositDryRunResultV5(
    Guid RequestUuid,
    decimal CashAmount,
    string SignedRequestXml,
    bool SignatureVerified,
    bool XsdValid);

public interface ICashDepositDryRunServiceV5
{
    CashDepositDryRunResultV5 CreateInitial(
        decimal cashAmount,
        DateTimeOffset changeDateTime,
        PuFiscalizationOptionsV5 configuration,
        X509Certificate2 certificate,
        string schemaPath);
}

public sealed class CashDepositDryRunServiceV5(
    IRegisterCashDepositXmlBuilderV5 xmlBuilder,
    IFiscalXmlSignerV5 xmlSigner) : ICashDepositDryRunServiceV5
{
    public CashDepositDryRunResultV5 CreateInitial(
        decimal cashAmount,
        DateTimeOffset changeDateTime,
        PuFiscalizationOptionsV5 configuration,
        X509Certificate2 certificate,
        string schemaPath)
    {
        configuration.EnsureReadyForInvoice();
        if (cashAmount < 0)
            throw new ArgumentException("Početni depozit ne može biti negativan.", nameof(cashAmount));

        var requestUuid = Guid.NewGuid();
        var request = new RegisterCashDepositRequestV5(
            new(requestUuid, changeDateTime),
            new(
                changeDateTime,
                PuCashDepositOperationV5.Initial,
                cashAmount,
                configuration.TcrCode,
                configuration.IssuerTin));
        var signed = xmlSigner.SignRequest(xmlBuilder.BuildUnsigned(request), certificate);
        var validation = new FiscalXmlSchemaValidatorV5(schemaPath)
            .Validate(signed.SignedDocument);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Depozit XML nije prošao PU XSD: {string.Join("; ", validation.Errors.Select(error => error.Message))}");
        }

        return new(
            requestUuid,
            cashAmount,
            signed.SignedDocument.ToString(SaveOptions.DisableFormatting),
            signed.SignatureVerified,
            validation.IsValid);
    }
}
