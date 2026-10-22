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
    string? Reference,
    Guid? AdvanceInvoiceId = null);

public sealed record CreateStornoCommand(
    Guid OriginalInvoiceId,
    string InvoiceNumber,
    DateTimeOffset IssueDateTime,
    string Reason,
    string IdempotencyKey,
    string CorrelationId,
    string Actor);

public sealed record CreateAdvanceSettlementCommand(
    Guid AdvanceInvoiceId, Guid FinalInvoiceId, DateTimeOffset IssueDateTime,
    IReadOnlyCollection<AdvanceSettlementAllocation> Allocations,
    string IdempotencyKey, string CorrelationId, string Actor);

public sealed record CreateAdvanceRestorationCommand(
    Guid StornoInvoiceId, DateTimeOffset IssueDateTime,
    string IdempotencyKey, string CorrelationId, string Actor);

public sealed record AdvanceStornoWorkflowResult(
    Guid FinalInvoiceId, Guid AdvanceInvoiceId, decimal AdvanceAmount,
    Guid? StornoInvoiceId, FiscalStatus? StornoStatus,
    Guid? SettlementInvoiceId, FiscalStatus? SettlementStatus,
    Guid? RestorationInvoiceId, FiscalStatus? RestorationStatus,
    bool AdvanceReleased)
{
    public string AdvanceTreatment => "ReturnToAdvance";
    public string Stage => StornoInvoiceId is null ? "NotStarted"
        : StornoStatus == FiscalStatus.Cancelled ? "Cancelled"
        : StornoStatus != FiscalStatus.Fiscalized ? "AwaitingStornoFiscalization"
        : AdvanceReleased ? "Completed"
        : RestorationInvoiceId is null ? "AwaitingAdvanceRestoration"
        : "AwaitingAdvanceRestorationFiscalization";
}

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
    decimal GrossAmount,
    Guid Id);

public sealed record FiscalPaymentResult(
    PaymentType PaymentType,
    decimal Amount,
    string? Reference,
    Guid? AdvanceInvoiceId,
    string? AdvanceIic);

public sealed record AdvanceUsageResult(
    Guid FinalInvoiceId,
    string FinalInvoiceNumber,
    decimal UsedAmount,
    FiscalStatus FinalInvoiceStatus,
    Guid? SettlementInvoiceId,
    FiscalStatus? SettlementStatus);

public sealed record AdvanceBalanceResult(
    Guid AdvanceInvoiceId,
    string AdvanceIic,
    decimal TotalAmount,
    decimal UsedAmount,
    decimal RemainingAmount,
    IReadOnlyCollection<AdvanceUsageResult> Usages,
    decimal ReservedAmount,
    bool IsAvailable)
{
    public decimal UnsettledAmount => Usages
        .Where(x => x.FinalInvoiceStatus == FiscalStatus.Fiscalized && x.SettlementStatus != FiscalStatus.Fiscalized)
        .Sum(x => x.UsedAmount);
    public decimal PendingRestorationAmount => Usages
        .Where(x => x.FinalInvoiceStatus == FiscalStatus.StornoCreated).Sum(x => x.UsedAmount);
}

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
    DateTimeOffset? FiscalizedAt,
    Guid? AdvanceSettlementFinalInvoiceId = null,
    string? AdvanceSettlementFinalIic = null,
    IReadOnlyCollection<AdvanceSettlementAllocation>? AdvanceSettlementAllocations = null,
    Guid? AdvanceRestorationStornoInvoiceId = null,
    string? AdvanceRestorationStornoIic = null,
    OrderSummaryData? OrderSummary = null,
    bool IsInformational = false)
{
    public decimal AdvanceAppliedAmount => Payments.Where(x => x.PaymentType == PaymentType.Advance).Sum(x => x.Amount);
    public decimal AmountToPayAfterAdvance => TotalGrossAmount - AdvanceAppliedAmount;
}

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
