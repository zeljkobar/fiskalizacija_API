using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public static class FiscalInvoiceRequestFactoryV5
{
    public static RegisterInvoiceRequestV5 Build(
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
            AdvanceIic: payment.PaymentType == PaymentType.Advance
                ? payment.AdvanceIic
                : null,
            BankAccount: payment.PaymentType == PaymentType.BankAccount
                ? payment.Reference
                : null)).ToArray();
        var isCash = invoice.Payments.Any(payment =>
            payment.PaymentType is PaymentType.Cash or PaymentType.Card or
                PaymentType.Voucher or PaymentType.Other or PaymentType.Order);

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

        if (invoice.IsInformational)
            taxes = invoice.OrderSummary!.References.SelectMany(x => x.Taxes).GroupBy(x => x.Rate)
                .Select(x => new PuSameTaxV5(x.Sum(t => t.NumberOfInvoices), x.Sum(t => t.NetAmount), x.Key, x.Sum(t => t.VatAmount))).ToArray();

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
                invoice.IsInformational ? [] : items,
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
                    : null,
                InvoiceReferences: invoice.InvoiceType == InvoiceType.Summary && invoice.OrderSummary is { } summary
                    ? summary.References.Select(x => new PuInvoiceReferenceV5(x.Iic, ToMontenegroTime(x.IssuedAt), x.GrossAmount)).ToArray()
                    : invoice.AdvanceSettlementFinalIic is { } finalIic &&
                    invoice.AdvanceSettlementFinalIssuedAt is { } finalIssuedAt
                    ? [new(finalIic, ToMontenegroTime(finalIssuedAt), -invoice.TotalGrossAmount)]
                    : invoice.AdvanceRestorationStornoIic is { } stornoIic && invoice.AdvanceRestorationStornoIssuedAt is { } stornoIssuedAt
                        ? [new(stornoIic, ToMontenegroTime(stornoIssuedAt), -invoice.TotalGrossAmount)]
                        : null));
    }

    public static DateTimeOffset ToMontenegroTime(DateTimeOffset value)
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
        PaymentType.Advance => PuPaymentMethodV5.Advance,
        PaymentType.Order => PuPaymentMethodV5.Order,
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
