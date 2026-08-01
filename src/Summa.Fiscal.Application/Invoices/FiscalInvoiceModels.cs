using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public sealed record CreateFiscalInvoiceCommand(
    Guid CompanyId,
    Guid BusinessUnitId,
    Guid DeviceId,
    Guid OperatorId,
    InvoiceType InvoiceType,
    string InvoiceNumber,
    DateTimeOffset IssueDateTime,
    string Currency,
    IReadOnlyCollection<CreateFiscalInvoiceItem> Items,
    IReadOnlyCollection<CreateFiscalPayment> Payments,
    string IdempotencyKey,
    string CorrelationId);

public sealed record CreateFiscalInvoiceItem(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    string? ItemCode,
    string? UnitOfMeasure,
    decimal DiscountAmount);

public sealed record CreateFiscalPayment(
    PaymentType PaymentType,
    decimal Amount,
    string? Reference);

public sealed record FiscalInvoiceResult(
    Guid Id,
    Guid CompanyId,
    string InvoiceNumber,
    FiscalStatus Status,
    decimal TotalNetAmount,
    decimal TotalVatAmount,
    decimal TotalGrossAmount,
    string Currency,
    string? Iic,
    string? Jikr,
    string? QrCodeData,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FiscalizedAt);

public sealed record FiscalInvoiceStatusResult(
    Guid Id,
    Guid CompanyId,
    string InvoiceNumber,
    FiscalStatus Status,
    string? Iic,
    string? Jikr,
    DateTimeOffset UpdatedAt);
