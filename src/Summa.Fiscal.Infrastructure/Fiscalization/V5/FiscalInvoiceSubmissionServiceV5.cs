using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Certificates;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record FiscalInvoiceSubmissionResultV5(
    Guid InvoiceId,
    Guid ExchangeId,
    int HttpStatusCode,
    bool IsSuccess,
    string InvoiceNumber,
    string Iic,
    string? Jikr,
    string? QrCodeData,
    FiscalStatus Status,
    string? FaultCode,
    string? FaultMessage);

public interface IFiscalInvoiceSubmissionServiceV5
{
    Task<FiscalInvoiceSubmissionResultV5?> SubmitAsync(
        Guid invoiceId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class FiscalInvoiceSubmissionServiceV5(
    IFiscalInvoiceRepository repository,
    IOptions<PuFiscalizationOptionsV5> fiscalOptions,
    IOptions<FiscalDevelopmentCertificateOptions> certificateOptions,
    IPfxCertificateLoader certificateLoader,
    IIicGeneratorV5 iicGenerator,
    IRegisterInvoiceXmlBuilderV5 xmlBuilder,
    IFiscalXmlSignerV5 xmlSigner,
    IFiscalQrCodeGeneratorV5 qrCodeGenerator,
    IServiceProvider serviceProvider) : IFiscalInvoiceSubmissionServiceV5
{
    public async Task<FiscalInvoiceSubmissionResultV5?> SubmitAsync(
        Guid invoiceId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByIdAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var configuration = fiscalOptions.Value;
        configuration.EnsureReadyForInvoice();
        var ordinalNumber = ReadOrdinalNumber(invoice.InvoiceNumber);
        var issueDateTime = ToMontenegroTime(invoice.IssueDateTime);

        if (invoice.Status == FiscalStatus.Fiscalized)
        {
            if (string.IsNullOrWhiteSpace(invoice.QrCodeData))
            {
                invoice.SetQrCodeData(GenerateQrCodeData(
                    invoice,
                    configuration,
                    issueDateTime,
                    ordinalNumber));
                await repository.UpdateAsync(invoice, cancellationToken);
            }

            return new(
                invoice.Id,
                Guid.Empty,
                200,
                true,
                invoice.InvoiceNumber,
                invoice.Iic!,
                invoice.Jikr,
                invoice.QrCodeData,
                invoice.Status,
                null,
                null);
        }

        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("PU endpoint nije ispravan.");
        }

        var certificateConfiguration = certificateOptions.Value;
        using var loadedCertificate = certificateLoader.Load(
            certificateConfiguration.Path,
            certificateConfiguration.Password,
            new(
                RequireCurrentlyValid: true,
                ExpectedIssuerTin: configuration.IssuerTin,
                KeyStorageFlags:
                    X509KeyStorageFlags.MachineKeySet |
                    X509KeyStorageFlags.PersistKeySet |
                    X509KeyStorageFlags.Exportable));

        var officialInvoiceNumber =
            $"{configuration.BusinessUnitCode}/{ordinalNumber}/{issueDateTime.Year}/{configuration.TcrCode}";
        var iic = iicGenerator.Generate(
            new(
                configuration.IssuerTin,
                issueDateTime,
                ordinalNumber,
                configuration.BusinessUnitCode,
                configuration.TcrCode,
                configuration.SoftwareCode,
                invoice.TotalGrossAmount),
            loadedCertificate.Certificate);

        var request = BuildRequest(
            invoice,
            configuration,
            issueDateTime,
            officialInvoiceNumber,
            ordinalNumber,
            iic);
        var signed = xmlSigner.SignRequest(
            xmlBuilder.BuildUnsigned(request),
            loadedCertificate.Certificate);
        if (!signed.SignatureVerified)
        {
            throw new InvalidOperationException("Digitalni potpis PU zahtjeva nije validan.");
        }

        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fiscalization",
            "V5",
            "Schemas",
            "FiscalService_v5_official.xsd");
        var schemaValidation = new FiscalXmlSchemaValidatorV5(schemaPath)
            .Validate(signed.SignedDocument);
        if (!schemaValidation.IsValid)
        {
            throw new InvalidOperationException(
                $"Zahtjev nije prošao PU XSD: {string.Join("; ", schemaValidation.Errors.Select(x => x.Message))}");
        }

        invoice.MarkFiscalizationPending(iic.Iic, iic.IicSignature);
        await repository.UpdateAsync(invoice, cancellationToken);

        try
        {
            var puClient = serviceProvider.GetRequiredService<IPuFiscalSoapClientV5>();
            var transport = await puClient.RegisterInvoiceAsync(
                endpoint,
                signed.SignedDocument,
                correlationId,
                cancellationToken);

            if (transport.Response.IsSuccess && !string.IsNullOrWhiteSpace(transport.Response.Fic))
            {
                var qrCodeData = GenerateQrCodeData(
                    invoice,
                    configuration,
                    issueDateTime,
                    ordinalNumber);
                invoice.MarkFiscalized(transport.Response.Fic, qrCodeData);
            }
            else
            {
                invoice.MarkFiscalizationFailed();
            }

            await repository.UpdateAsync(invoice, cancellationToken);
            return new(
                invoice.Id,
                transport.ExchangeId,
                (int)transport.StatusCode,
                transport.Response.IsSuccess,
                officialInvoiceNumber,
                iic.Iic,
                transport.Response.Fic,
                invoice.QrCodeData,
                invoice.Status,
                transport.Response.Fault?.Code,
                transport.Response.Fault?.Message);
        }
        catch
        {
            invoice.MarkFiscalizationFailed();
            await repository.UpdateAsync(invoice, CancellationToken.None);
            throw;
        }
    }

    private string GenerateQrCodeData(
        FiscalInvoice invoice,
        PuFiscalizationOptionsV5 configuration,
        DateTimeOffset issueDateTime,
        int ordinalNumber) =>
        qrCodeGenerator.GenerateVerificationUrl(new(
            configuration.Environment,
            invoice.Iic ?? throw new InvalidOperationException("IKOF nije generisan."),
            configuration.IssuerTin,
            issueDateTime,
            ordinalNumber,
            configuration.BusinessUnitCode,
            configuration.TcrCode,
            configuration.SoftwareCode,
            invoice.TotalGrossAmount));

    private static RegisterInvoiceRequestV5 BuildRequest(
        FiscalInvoice invoice,
        PuFiscalizationOptionsV5 configuration,
        DateTimeOffset issueDateTime,
        string officialInvoiceNumber,
        int ordinalNumber,
        IicGenerationResultV5 iic)
    {
        var paymentMethods = invoice.Payments.Select(payment => new PuPaymentV5(
            MapPayment(payment.PaymentType),
            payment.Amount,
            BankAccount: payment.PaymentType == PaymentType.BankAccount
                ? payment.Reference
                : null)).ToArray();
        var isCash = invoice.Payments.Any(payment =>
            payment.PaymentType is PaymentType.Cash or PaymentType.Card or
                PaymentType.Voucher or PaymentType.Other);

        var items = invoice.Items.Select(item => new PuInvoiceItemV5(
            item.Name,
            string.IsNullOrWhiteSpace(item.UnitOfMeasure) ? "kom" : item.UnitOfMeasure,
            item.Quantity,
            item.VatRate == 0
                ? item.UnitPrice
                : decimal.Round(item.UnitPrice / (1 + item.VatRate / 100), 4),
            item.UnitPrice,
            item.NetAmount,
            item.GrossAmount,
            item.ItemCode,
            item.DiscountAmount > 0
                ? decimal.Round(item.DiscountAmount / (item.Quantity * item.UnitPrice) * 100, 4)
                : null,
            item.DiscountAmount > 0 ? true : null,
            item.VatRate,
            item.VatAmount)).ToArray();
        var taxes = invoice.Items
            .GroupBy(item => item.VatRate)
            .Select(group => new PuSameTaxV5(
                group.Count(),
                group.Sum(item => item.NetAmount),
                group.Key,
                group.Sum(item => item.VatAmount)))
            .ToArray();

        return new(
            new(Guid.NewGuid(), DateTimeOffset.Now),
            new(
                isCash ? PuInvoiceTypeV5.Cash : PuInvoiceTypeV5.NonCash,
                issueDateTime,
                officialInvoiceNumber,
                ordinalNumber,
                configuration.TcrCode,
                true,
                invoice.TotalNetAmount,
                invoice.TotalVatAmount,
                invoice.TotalGrossAmount,
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
                paymentMethods,
                items,
                taxes,
                MapDocumentType(invoice.InvoiceType)));
    }

    private static int ReadOrdinalNumber(string invoiceNumber) =>
        int.TryParse(
            invoiceNumber.Split('/', StringSplitOptions.TrimEntries)[0],
            out var ordinal) && ordinal > 0
            ? ordinal
            : throw new InvalidOperationException(
                "Broj računa mora početi pozitivnim rednim brojem.");

    private static DateTimeOffset ToMontenegroTime(DateTimeOffset value)
    {
        var zoneId = OperatingSystem.IsWindows()
            ? "Central European Standard Time"
            : "Europe/Podgorica";
        return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(zoneId));
    }

    private static PuPaymentMethodV5 MapPayment(PaymentType paymentType) => paymentType switch
    {
        PaymentType.Cash => PuPaymentMethodV5.Banknote,
        PaymentType.Card => PuPaymentMethodV5.Card,
        PaymentType.BankAccount => PuPaymentMethodV5.Account,
        PaymentType.Voucher => PuPaymentMethodV5.ServiceVoucher,
        PaymentType.Other => PuPaymentMethodV5.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(paymentType), paymentType, null)
    };

    private static PuInvoiceDocumentTypeV5 MapDocumentType(InvoiceType invoiceType) =>
        invoiceType switch
        {
            InvoiceType.Normal => PuInvoiceDocumentTypeV5.Invoice,
            InvoiceType.Advance => PuInvoiceDocumentTypeV5.Advance,
            InvoiceType.Corrective => PuInvoiceDocumentTypeV5.Corrective,
            InvoiceType.Periodic => PuInvoiceDocumentTypeV5.Periodical,
            InvoiceType.Summary => PuInvoiceDocumentTypeV5.Summary,
            _ => throw new ArgumentOutOfRangeException(nameof(invoiceType), invoiceType, null)
        };
}
