namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record RegisterInvoiceRequestV5(
    RegisterInvoiceHeaderV5 Header,
    PuInvoiceV5 Invoice);

public sealed record RegisterInvoiceHeaderV5(
    Guid Uuid,
    DateTimeOffset SendDateTime,
    PuSubsequentDeliveryTypeV5? SubsequentDeliveryType = null);

public sealed record PuInvoiceV5(
    PuInvoiceTypeV5 TypeOfInvoice,
    DateTimeOffset IssueDateTime,
    string InvoiceNumber,
    int InvoiceOrdinalNumber,
    string TcrCode,
    bool IsIssuerInVat,
    decimal TotalPriceWithoutVat,
    decimal? TotalVatAmount,
    decimal TotalPrice,
    string OperatorCode,
    string BusinessUnitCode,
    string SoftwareCode,
    string Iic,
    string IicSignature,
    PuSellerV5 Seller,
    IReadOnlyCollection<PuPaymentV5> Payments,
    IReadOnlyCollection<PuInvoiceItemV5> Items,
    IReadOnlyCollection<PuSameTaxV5>? SameTaxes = null,
    PuInvoiceDocumentTypeV5? DocumentType = null,
    bool? IsSimplifiedInvoice = null,
    bool? IsReverseCharge = null,
    decimal? TotalPriceToPay = null,
    PuBuyerV5? Buyer = null);

public sealed record PuSellerV5(
    PuIdTypeV5 IdType,
    string IdNumber,
    string Name,
    string? Address = null,
    string? Town = null,
    string? Country = null);

public sealed record PuBuyerV5(
    PuIdTypeV5? IdType = null,
    string? IdNumber = null,
    string? Name = null,
    string? Address = null,
    string? Town = null,
    string? Country = null,
    string? TaxIdentificationCode = null);

public sealed record PuPaymentV5(
    PuPaymentMethodV5 Type,
    decimal Amount,
    string? CompanyCard = null,
    string? AdvanceIic = null,
    string? BankAccount = null);

public sealed record PuInvoiceItemV5(
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitPriceBeforeVat,
    decimal UnitPriceAfterVat,
    decimal PriceBeforeVat,
    decimal PriceAfterVat,
    string? Code = null,
    decimal? Rebate = null,
    bool? RebateReducesTaxBase = null,
    decimal? VatRate = null,
    decimal? VatAmount = null,
    bool? IsInvestment = null,
    PuVatExemptionV5? VatExemption = null);

public sealed record PuSameTaxV5(
    int NumberOfItems,
    decimal PriceBeforeVat,
    decimal VatRate,
    decimal VatAmount,
    PuVatExemptionV5? VatExemption = null);
