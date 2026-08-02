using System.Security.Cryptography.X509Certificates;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Domain.Invoices;

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
    IFiscalOnboardingService onboardingService,
    IIicGeneratorV5 iicGenerator,
    IRegisterInvoiceXmlBuilderV5 xmlBuilder,
    IFiscalXmlSignerV5 xmlSigner,
    IFiscalQrCodeGeneratorV5 qrCodeGenerator,
    ISoapEnvelopeV5 soapEnvelope,
    IRegisterInvoiceResponseParserV5 responseParser,
    IFiscalExchangeStoreV5 exchangeStore,
    IAuditService auditService) : IFiscalInvoiceSubmissionServiceV5
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

        var fiscalContext = await onboardingService.ResolveContextAsync(
            invoice.CompanyId,
            invoice.BusinessUnitId,
            invoice.DeviceId,
            invoice.OperatorId,
            "fiscalization-engine",
            correlationId,
            cancellationToken);
        var configuration = BuildConfiguration(fiscalContext);
        configuration.EnsureReadyForInvoice();
        if (string.Equals(fiscalContext.Company.Environment, "Production", StringComparison.Ordinal) &&
            string.Equals(fiscalContext.Company.PaymentPolicy, "BankOnly", StringComparison.Ordinal) &&
            invoice.Payments.Any(payment => payment.PaymentType != PaymentType.BankAccount))
        {
            throw new FiscalOnboardingException("PRODUCTION_PAYMENT_POLICY_VIOLATION",
                "Produkcioni profil firme dozvoljava isključivo bezgotovinsko plaćanje preko bankovnog računa.");
        }
        if (!fiscalContext.Company.IsVatPayer &&
            (invoice.TotalVatAmount != 0 || invoice.Items.Any(item => item.VatRate > 0)))
        {
            throw new FiscalOnboardingException(
                "ISSUER_NOT_IN_VAT_CANNOT_CHARGE_VAT",
                "Firma nije označena kao PDV obveznik i ne može poslati račun sa obračunatim PDV-om.");
        }
        var ordinalNumber = ReadOrdinalNumber(invoice.InvoiceNumber);
        var issueDateTime = ToMontenegroTime(invoice.IssueDateTime);

        if (invoice.Status == FiscalStatus.Fiscalized)
        {
            var existingOfficialNumber =
                $"{configuration.BusinessUnitCode}/{ordinalNumber}/{issueDateTime.Year}/{configuration.TcrCode}";
            if (string.IsNullOrWhiteSpace(invoice.OfficialInvoiceNumber))
            {
                invoice.SetOfficialInvoiceNumber(existingOfficialNumber);
                await repository.UpdateAsync(invoice, cancellationToken);
            }
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
                invoice.OfficialInvoiceNumber!,
                invoice.Iic!,
                invoice.Jikr,
                invoice.QrCodeData,
                invoice.Status,
                null,
                null);
        }

        if (invoice.Status is not (FiscalStatus.ReadyForFiscalization or FiscalStatus.FiscalizationFailed))
        {
            throw new FiscalInvoiceOperationException(
                "INVOICE_NOT_READY_FOR_FISCALIZATION",
                "Račun nije u statusu koji dozvoljava slanje Poreskoj upravi.");
        }

        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("PU endpoint nije ispravan.");
        }

        using var certificate = X509CertificateLoader.LoadPkcs12(
            fiscalContext.PfxBytes,
            fiscalContext.Password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

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
            certificate);

        var request = BuildRequest(
            invoice,
            configuration,
            issueDateTime,
            officialInvoiceNumber,
            ordinalNumber,
            iic);
        var signed = xmlSigner.SignRequest(
            xmlBuilder.BuildUnsigned(request),
            certificate);
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
            using var handler = new PuClientCertificateHandlerV5(certificate);
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var puClient = new PuFiscalSoapClientV5(httpClient, soapEnvelope, responseParser, exchangeStore);
            var transport = await puClient.RegisterInvoiceAsync(
                endpoint,
                signed.SignedDocument,
                correlationId,
                cancellationToken);
            var fiscalizationSucceeded =
                transport.Response.IsSuccess &&
                !string.IsNullOrWhiteSpace(transport.Response.Fic);

            if (fiscalizationSucceeded)
            {
                var qrCodeData = GenerateQrCodeData(
                    invoice,
                    configuration,
                    issueDateTime,
                    ordinalNumber);
                invoice.MarkFiscalized(transport.Response.Fic!, officialInvoiceNumber, qrCodeData);
                if (invoice.OriginalInvoiceId is { } originalInvoiceId)
                {
                    var original = await repository.GetByIdAsync(originalInvoiceId, cancellationToken)
                        ?? throw new InvalidOperationException("Originalni račun korektivnog dokumenta nije pronađen.");
                    original.MarkStornoCreated();
                    await repository.CompleteCorrectiveAsync(invoice, original, cancellationToken);
                }
                else
                {
                    await repository.UpdateAsync(invoice, cancellationToken);
                }
            }
            else
            {
                invoice.MarkFiscalizationFailed();
                await repository.UpdateAsync(invoice, cancellationToken);
            }
            await auditService.RecordAsync(
                new(
                    fiscalizationSucceeded ? "FISCAL_INVOICE_FISCALIZED" : "FISCAL_INVOICE_REJECTED",
                    invoice.Id,
                    invoice.CompanyId,
                    correlationId,
                    "fiscalization-engine",
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string?>
                    {
                        ["exchangeId"] = transport.ExchangeId.ToString(),
                        ["invoiceNumber"] = officialInvoiceNumber,
                        ["iic"] = iic.Iic,
                        ["jikr"] = transport.Response.Fic,
                        ["originalInvoiceId"] = invoice.OriginalInvoiceId?.ToString(),
                        ["faultCode"] = transport.Response.Fault?.Code
                    }),
                cancellationToken);
            return new(
                invoice.Id,
                transport.ExchangeId,
                (int)transport.StatusCode,
                fiscalizationSucceeded,
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
            if (invoice.Status == FiscalStatus.FiscalizationPending)
            {
                invoice.MarkFiscalizationFailed();
                await repository.UpdateAsync(invoice, CancellationToken.None);
            }
            throw;
        }
    }

    private static PuFiscalizationOptionsV5 BuildConfiguration(CompanyFiscalContext context) => new()
    {
        Environment = context.Company.Environment,
        Endpoint = context.Company.Endpoint,
        IssuerTin = context.Company.Tin,
        BusinessUnitCode = context.BusinessUnit.Code,
        TcrCode = context.Device.TcrCode ?? throw new InvalidOperationException("ENU nije registrovan kod Poreske uprave."),
        SoftwareCode = context.Company.SoftwareCode,
        OperatorCode = context.Operator.OperatorCode,
        SellerName = context.Company.LegalName,
        SellerAddress = context.Company.Address ?? context.BusinessUnit.Address ?? string.Empty,
        SellerTown = context.Company.Town ?? context.BusinessUnit.Town ?? string.Empty,
        SellerCountry = context.Company.Country,
        IsIssuerInVat = context.Company.IsVatPayer
    };

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
            item.DiscountAmount != 0
                ? decimal.Round(Math.Abs(item.DiscountAmount) / Math.Abs(item.Quantity * item.UnitPrice) * 100, 4)
                : null,
            item.DiscountAmount != 0 ? true : null,
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
            new(Guid.NewGuid(), issueDateTime),
            new(
                isCash ? PuInvoiceTypeV5.Cash : PuInvoiceTypeV5.NonCash,
                issueDateTime,
                officialInvoiceNumber,
                ordinalNumber,
                configuration.TcrCode,
                configuration.IsIssuerInVat,
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
                MapDocumentType(invoice.InvoiceType),
                Buyer: invoice.Buyer is null ? null : new PuBuyerV5(
                    MapBuyerIdentificationType(invoice.Buyer.IdentificationType),
                    invoice.Buyer.IdentificationNumber,
                    invoice.Buyer.Name,
                    invoice.Buyer.Address,
                    invoice.Buyer.Town,
                    invoice.Buyer.Country,
                    invoice.Buyer.TaxIdentificationCode),
                PaymentDeadline: invoice.PaymentDeadline,
                SupplyPeriod: invoice.SupplyPeriodStart.HasValue && invoice.SupplyPeriodEnd.HasValue
                    ? new(invoice.SupplyPeriodStart.Value, invoice.SupplyPeriodEnd.Value)
                    : null,
                CorrectiveInvoice: invoice.OriginalIic is not null && invoice.OriginalIssueDateTime.HasValue && invoice.CorrectiveType.HasValue
                    ? new(
                        invoice.OriginalIic,
                        ToMontenegroTime(invoice.OriginalIssueDateTime.Value),
                        MapCorrectiveType(invoice.CorrectiveType.Value))
                    : null));
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

    private static PuIdTypeV5 MapBuyerIdentificationType(BuyerIdentificationType type) => type switch
    {
        BuyerIdentificationType.Tin => PuIdTypeV5.Tin,
        BuyerIdentificationType.Id => PuIdTypeV5.Id,
        BuyerIdentificationType.Passport => PuIdTypeV5.Passport,
        BuyerIdentificationType.Vat => PuIdTypeV5.Vat,
        BuyerIdentificationType.Tax => PuIdTypeV5.Tax,
        BuyerIdentificationType.Social => PuIdTypeV5.Social,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static PuCorrectiveInvoiceTypeV5 MapCorrectiveType(CorrectiveInvoiceType type) => type switch
    {
        CorrectiveInvoiceType.Corrective => PuCorrectiveInvoiceTypeV5.Corrective,
        CorrectiveInvoiceType.ErrorCorrective => PuCorrectiveInvoiceTypeV5.ErrorCorrective,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
