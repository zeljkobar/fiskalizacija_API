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
    IReadOnlyCollection<CreateFiscalInvoiceItemRequest>? Items,
    IReadOnlyCollection<CreateFiscalPaymentRequest>? Payments);

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
