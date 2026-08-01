using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public static class PuFiscalContractV5
{
    public const string ServiceNamespace = "https://efi.tax.gov.me/fs";
    public const string SchemaNamespace = "https://efi.tax.gov.me/fs/schema";
    public const string Soap11Namespace = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
    public const string RequestId = "Request";
    public const string ResponseId = "Response";
    public const int SchemaVersion = 1;

    public const string RegisterInvoiceOperation = "registerInvoice";
    public const string RegisterInvoiceSoapAction =
        "https://efi.tax.gov.me/fs/RegisterInvoice";

    public const string RegisterTcrOperation = "registerTCR";
    public const string RegisterTcrSoapAction =
        "https://efi.tax.gov.me/fs/RegisterTCR";

    public const string RegisterCashDepositOperation = "registerCashDeposit";
    public const string RegisterCashDepositSoapAction =
        "https://efi.tax.gov.me/fs/RegisterCashDeposit";

    public static readonly XNamespace Schema = SchemaNamespace;
    public static readonly XNamespace XmlDsig = XmlDsigNamespace;
}

public enum PuInvoiceDocumentTypeV5
{
    Invoice,
    Corrective,
    Summary,
    Periodical,
    Advance,
    CreditNote
}

public enum PuInvoiceTypeV5
{
    Cash,
    NonCash
}

public enum PuPaymentMethodV5
{
    Banknote,
    Card,
    BusinessCard,
    ServiceVoucher,
    Company,
    Order,
    Advance,
    Account,
    Factoring,
    Other,
    OtherCash
}

public enum PuIdTypeV5
{
    Tin,
    Id,
    Passport,
    Vat,
    Tax,
    Social
}

public enum PuSubsequentDeliveryTypeV5
{
    NoInternet,
    BoundBook,
    Service,
    TechnicalError,
    BusinessNeeds
}

public enum PuVatExemptionV5
{
    VatCl17,
    VatCl20,
    VatCl26,
    VatCl27,
    VatCl28,
    VatCl29,
    VatCl30
}

internal static class PuFiscalLexicalValuesV5
{
    public static string ToXmlValue(this PuInvoiceDocumentTypeV5 value) => value switch
    {
        PuInvoiceDocumentTypeV5.Invoice => "INVOICE",
        PuInvoiceDocumentTypeV5.Corrective => "CORRECTIVE",
        PuInvoiceDocumentTypeV5.Summary => "SUMMARY",
        PuInvoiceDocumentTypeV5.Periodical => "PERIODICAL",
        PuInvoiceDocumentTypeV5.Advance => "ADVANCE",
        PuInvoiceDocumentTypeV5.CreditNote => "CREDIT_NOTE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToXmlValue(this PuInvoiceTypeV5 value) => value switch
    {
        PuInvoiceTypeV5.Cash => "CASH",
        PuInvoiceTypeV5.NonCash => "NONCASH",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToXmlValue(this PuPaymentMethodV5 value) => value switch
    {
        PuPaymentMethodV5.Banknote => "BANKNOTE",
        PuPaymentMethodV5.Card => "CARD",
        PuPaymentMethodV5.BusinessCard => "BUSINESSCARD",
        PuPaymentMethodV5.ServiceVoucher => "SVOUCHER",
        PuPaymentMethodV5.Company => "COMPANY",
        PuPaymentMethodV5.Order => "ORDER",
        PuPaymentMethodV5.Advance => "ADVANCE",
        PuPaymentMethodV5.Account => "ACCOUNT",
        PuPaymentMethodV5.Factoring => "FACTORING",
        PuPaymentMethodV5.Other => "OTHER",
        PuPaymentMethodV5.OtherCash => "OTHER-CASH",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToXmlValue(this PuIdTypeV5 value) => value switch
    {
        PuIdTypeV5.Tin => "TIN",
        PuIdTypeV5.Id => "ID",
        PuIdTypeV5.Passport => "PASS",
        PuIdTypeV5.Vat => "VAT",
        PuIdTypeV5.Tax => "TAX",
        PuIdTypeV5.Social => "SOC",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToXmlValue(this PuSubsequentDeliveryTypeV5 value) => value switch
    {
        PuSubsequentDeliveryTypeV5.NoInternet => "NOINTERNET",
        PuSubsequentDeliveryTypeV5.BoundBook => "BOUNDBOOK",
        PuSubsequentDeliveryTypeV5.Service => "SERVICE",
        PuSubsequentDeliveryTypeV5.TechnicalError => "TECHNICALERROR",
        PuSubsequentDeliveryTypeV5.BusinessNeeds => "BUSINESSNEEDS",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToXmlValue(this PuVatExemptionV5 value) => value switch
    {
        PuVatExemptionV5.VatCl17 => "VAT_CL17",
        PuVatExemptionV5.VatCl20 => "VAT_CL20",
        PuVatExemptionV5.VatCl26 => "VAT_CL26",
        PuVatExemptionV5.VatCl27 => "VAT_CL27",
        PuVatExemptionV5.VatCl28 => "VAT_CL28",
        PuVatExemptionV5.VatCl29 => "VAT_CL29",
        PuVatExemptionV5.VatCl30 => "VAT_CL30",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
