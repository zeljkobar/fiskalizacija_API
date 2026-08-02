using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Api.Contracts;

public sealed record CreateFiscalInvoiceRequest(
    Guid CompanyId,
    Guid BusinessUnitId,
    Guid DeviceId,
    Guid OperatorId,
    InvoiceType InvoiceType,
    string InvoiceNumber,
    DateTimeOffset IssueDateTime,
    string Currency,
    CreateFiscalBuyerRequest? Buyer,
    DateOnly? SupplyPeriodStart,
    DateOnly? SupplyPeriodEnd,
    DateOnly? PaymentDeadline,
    IReadOnlyCollection<CreateFiscalInvoiceItemRequest>? Items,
    IReadOnlyCollection<CreateFiscalPaymentRequest>? Payments);

public sealed record CreateFiscalBuyerRequest(
    BuyerIdentificationType IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Address = null,
    string? Town = null,
    string? Country = null,
    string? TaxIdentificationCode = null);

public sealed record CreateFiscalInvoiceItemRequest(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    string? ItemCode = null,
    string? UnitOfMeasure = null,
    decimal DiscountAmount = 0);

public sealed record CreateFiscalPaymentRequest(
    PaymentType PaymentType,
    decimal Amount,
    string? Reference = null);

public sealed record CreateStornoRequest(
    string InvoiceNumber,
    DateTimeOffset IssueDateTime,
    string Reason,
    string Confirmation);
