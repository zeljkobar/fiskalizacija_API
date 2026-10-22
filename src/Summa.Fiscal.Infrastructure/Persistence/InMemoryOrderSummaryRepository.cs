using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Infrastructure.Persistence;

public sealed partial class InMemoryFiscalInvoiceRepository
{
    public Task<IReadOnlyCollection<FiscalInvoice>> GetCorrectionsAsync(Guid originalId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<FiscalInvoice>>(_invoices.Values.Where(x => x.OriginalInvoiceId == originalId).Select(Copy).ToArray());
    public Task<FiscalInvoice?> GetSummaryForOrderAsync(Guid orderId, CancellationToken ct)
    {
        var summary = _invoices.Values.SingleOrDefault(x => x.InvoiceType == InvoiceType.Summary &&
            x.Status != FiscalStatus.Cancelled && !IsSummaryCancelled(x.Id) && x.OrderSummary!.OrderInvoiceIds.Contains(orderId));
        return Task.FromResult(summary is null ? null : Copy(summary));
    }

    private FiscalInvoice? PrepareOrderSummary(FiscalInvoice invoice)
    {
        if (invoice.OrderSummary is not { } data) return null;
        var roots = data.OrderInvoiceIds.Select(id => _invoices.TryGetValue(id, out var x) ? x :
            throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "ORDER račun nije pronađen.", 404)).ToArray();
        var corrections = _invoices.Values.Where(x => x.OriginalInvoiceId.HasValue && data.OrderInvoiceIds.Contains(x.OriginalInvoiceId.Value)).ToArray();
        if (invoice.InvoiceType == InvoiceType.Summary)
        {
            if (_invoices.Values.Any(x => x.InvoiceType == InvoiceType.Summary && x.Status != FiscalStatus.Cancelled &&
                !IsSummaryCancelled(x.Id) && x.OrderSummary!.OrderInvoiceIds.Intersect(data.OrderInvoiceIds).Any()))
                throw new FiscalInvoiceOperationException("ORDER_ALREADY_SUMMARIZED", "ORDER je rezervisan ili zatvoren.");
            OrderSummaryRules.EnsureSources(roots, corrections, invoice.IssueDateTime);
            var sources = roots.Concat(corrections.Where(OrderSummaryRules.IsSuccessful)).ToArray();
            if (!sources.Select(x => x.Id).Order().SequenceEqual(data.References.Select(x => x.InvoiceId).Order()) ||
                sources.Sum(x => x.TotalGrossAmount) != invoice.TotalGrossAmount)
                throw new FiscalInvoiceOperationException("SUMMARY_SOURCES_CHANGED", "Izvori sumarnog računa su promijenjeni.");
        }
        else if (invoice.IsOrderCorrection)
        {
            OrderSummaryRules.EnsureOrderCorrection(roots.Single(), invoice, corrections);
            var summary = _invoices.Values.SingleOrDefault(x => x.InvoiceType == InvoiceType.Summary && x.Status != FiscalStatus.Cancelled &&
                !IsSummaryCancelled(x.Id) && x.OrderSummary!.OrderInvoiceIds.Contains(roots.Single().Id));
            OrderSummaryRules.EnsureSummaryResolved(summary);
            if (summary?.Status == FiscalStatus.ReadyForFiscalization)
            { var cancelled = Copy(summary); cancelled.CancelUnsubmitted(); return cancelled; }
        }
        else if (invoice.IsSummaryCorrection)
            OrderSummaryRules.EnsureSummaryCorrection(_invoices[invoice.OriginalInvoiceId!.Value], invoice, corrections,
                _invoices.Values.Where(x => x.OriginalInvoiceId == invoice.OriginalInvoiceId).ToArray());
        return null;
    }

    private bool IsSummaryCancelled(Guid summaryId) => _invoices.Values.Any(x => x.IsSummaryCancellation &&
        x.OriginalInvoiceId == summaryId && OrderSummaryRules.IsSuccessful(x));

    public Task CompleteOrderSummaryAsync(FiscalInvoice invoice, AuditEntry audit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            var stored = _invoices[invoice.Id];
            if (OrderSummaryRules.IsSuccessful(stored))
            {
                if (stored.Jikr != invoice.Jikr) throw new FiscalInvoiceOperationException("FISCAL_RECOVERY_JIKR_CONFLICT", "Dokument već ima drugi JIKR.");
                return Task.CompletedTask;
            }
            if (stored.Status != FiscalStatus.FiscalizationPending) throw new FiscalInvoiceOperationException("INVOICE_STATE_CONFLICT", "Dokument nije preuzet za slanje.");
            if (invoice.IsOrderCorrection || invoice.IsSummaryCorrection)
            {
                var original = Copy(_invoices[invoice.OriginalInvoiceId!.Value]);
                var remaining = original.TotalGrossAmount + invoice.TotalGrossAmount + _invoices.Values.Where(x => x.Id != invoice.Id &&
                    x.OriginalInvoiceId == original.Id && OrderSummaryRules.IsSuccessful(x)).Sum(x => x.TotalGrossAmount);
                if (remaining < 0) throw new FiscalInvoiceOperationException("ORDER_CORRECTION_EXCEEDED", "Korekcija prelazi izvorni iznos.");
                if (remaining == 0 && original.Status != FiscalStatus.StornoCreated) original.MarkStornoCreated();
                _invoices[original.Id] = original;
            }
            _invoices[invoice.Id] = Copy(invoice);
            _auditEntries.Enqueue(audit);
        }
        return Task.CompletedTask;
    }
}
