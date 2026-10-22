using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Abstractions;

public interface IFiscalInvoiceRepository
{
    Task<IReadOnlyCollection<FiscalInvoice>> GetCorrectionsAsync(Guid originalId, CancellationToken cancellationToken);
    Task<FiscalInvoice?> GetSummaryForOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task CompleteOrderSummaryAsync(FiscalInvoice invoice, AuditEntry audit, CancellationToken cancellationToken);
    Task<FiscalInvoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<FiscalInvoice?> GetByIdempotencyKeyAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<FiscalInvoice?> GetByOriginalInvoiceIdAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken);
    Task<AdvanceUsageSnapshot?> GetAdvanceUsageAsync(
        Guid advanceInvoiceId,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalInvoice>> GetAdvanceSettlementsAsync(Guid advanceInvoiceId, CancellationToken cancellationToken);
    Task CancelUnsubmittedAsync(Guid invoiceId, CancellationToken cancellationToken, AuditEntry? auditEntry = null);
    Task AddAsync(FiscalInvoice invoice, CancellationToken cancellationToken, AuditEntry? auditEntry = null);
    Task UpdateAsync(FiscalInvoice invoice, CancellationToken cancellationToken);
    Task CompleteCorrectiveAsync(
        FiscalInvoice corrective,
        FiscalInvoice original,
        CancellationToken cancellationToken,
        AuditEntry? auditEntry = null);
}

public sealed record AdvanceUsageEntry(
    Guid FinalInvoiceId,
    string FinalInvoiceNumber,
    decimal UsedAmount,
    FiscalStatus FinalInvoiceStatus,
    Guid? SettlementInvoiceId = null,
    FiscalStatus? SettlementStatus = null);

public sealed record AdvanceUsageSnapshot(
    Guid AdvanceInvoiceId,
    string AdvanceIic,
    decimal TotalAmount,
    decimal UsedAmount,
    IReadOnlyCollection<AdvanceUsageEntry> Usages,
    bool IsAvailable = true)
{
    public decimal RemainingAmount => decimal.Round(
        IsAvailable ? TotalAmount - UsedAmount : 0,
        2,
        MidpointRounding.AwayFromZero);
}
