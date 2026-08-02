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
    CreateFiscalBuyer? Buyer,
    DateOnly? SupplyPeriodStart,
    DateOnly? SupplyPeriodEnd,
    DateOnly? PaymentDeadline,
    IReadOnlyCollection<CreateFiscalInvoiceItem> Items,
    IReadOnlyCollection<CreateFiscalPayment> Payments,
    string IdempotencyKey,
    string CorrelationId,
    string Actor);

public sealed record CreateFiscalBuyer(
    BuyerIdentificationType IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Address,
    string? Town,
    string? Country,
    string? TaxIdentificationCode);

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

public sealed record CreateStornoCommand(
    Guid OriginalInvoiceId,
    string InvoiceNumber,
    DateTimeOffset IssueDateTime,
    string Reason,
    string IdempotencyKey,
    string CorrelationId,
    string Actor);

public sealed record FiscalBuyerResult(
    BuyerIdentificationType IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Address,
    string? Town,
    string? Country,
    string? TaxIdentificationCode);

public sealed record FiscalInvoiceItemResult(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    string? ItemCode,
    string? UnitOfMeasure,
    decimal DiscountAmount,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount);

public sealed record FiscalPaymentResult(
    PaymentType PaymentType,
    decimal Amount,
    string? Reference);

public sealed record FiscalInvoiceResult(
    Guid Id,
    Guid CompanyId,
    string InvoiceNumber,
    string? OfficialInvoiceNumber,
    InvoiceType InvoiceType,
    FiscalStatus Status,
    decimal TotalNetAmount,
    decimal TotalVatAmount,
    decimal TotalGrossAmount,
    string Currency,
    string? Iic,
    string? Jikr,
    string? QrCodeData,
    DateTimeOffset IssueDateTime,
    DateOnly? SupplyPeriodStart,
    DateOnly? SupplyPeriodEnd,
    DateOnly? PaymentDeadline,
    FiscalBuyerResult? Buyer,
    Guid? OriginalInvoiceId,
    string? OriginalIic,
    DateTimeOffset? OriginalIssueDateTime,
    CorrectiveInvoiceType? CorrectiveType,
    string? CorrectionReason,
    IReadOnlyCollection<FiscalInvoiceItemResult> Items,
    IReadOnlyCollection<FiscalPaymentResult> Payments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FiscalizedAt);

public sealed record FiscalInvoiceStatusResult(
    Guid Id,
    Guid CompanyId,
    string InvoiceNumber,
    string? OfficialInvoiceNumber,
    FiscalStatus Status,
    string? Iic,
    string? Jikr,
    DateTimeOffset UpdatedAt);

public sealed class FiscalInvoiceOperationException(
    string code,
    string message,
    int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
