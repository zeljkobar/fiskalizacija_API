using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed partial class PostgreSqlFiscalInvoiceRepository
{
    public async Task<IReadOnlyCollection<FiscalInvoice>> GetCorrectionsAsync(Guid originalId, CancellationToken ct) =>
        (await Query().Where(x => x.OriginalInvoiceId == originalId).ToArrayAsync(ct)).Select(ToDomain).ToArray();

    public async Task<FiscalInvoice?> GetSummaryForOrderAsync(Guid orderId, CancellationToken ct)
    {
        var id = await dbContext.FiscalSummaryOrders.AsNoTracking().Where(x => x.OrderInvoiceId == orderId && x.ReleasedAt == null)
            .Select(x => (Guid?)x.SummaryInvoiceId).SingleOrDefaultAsync(ct);
        return id is null ? null : await GetByIdAsync(id.Value, ct);
    }

    private async Task PrepareOrderSummaryAsync(FiscalInvoice invoice, AuditEntry? audit, CancellationToken ct)
    {
        if (invoice.OrderSummary is not { } data) return;
        var roots = new List<FiscalInvoice>();
        foreach (var id in data.OrderInvoiceIds.Order()) roots.Add(ToDomain(await LockInvoiceAsync(id, ct)));
        if (invoice.InvoiceType == InvoiceType.Summary)
        {
            var corrections = new List<FiscalInvoice>();
            foreach (var root in roots)
            {
                if (await GetSummaryForOrderAsync(root.Id, ct) is not null)
                    throw new FiscalInvoiceOperationException("ORDER_ALREADY_SUMMARIZED", "ORDER račun je već rezervisan ili zatvoren sumarnim računom.");
                corrections.AddRange(await GetCorrectionsAsync(root.Id, ct));
            }
            OrderSummaryRules.EnsureSources(roots, corrections, invoice.IssueDateTime);
            var references = roots.Concat(corrections.Where(OrderSummaryRules.IsSuccessful)).Select(FiscalInvoice.SummaryReference).ToArray();
            if (!references.Select(x => x.InvoiceId).Order().SequenceEqual(data.References.Select(x => x.InvoiceId).Order()) ||
                references.Sum(x => x.GrossAmount) != invoice.TotalGrossAmount || references.Sum(x => x.NetAmount) != invoice.TotalNetAmount || references.Sum(x => x.VatAmount) != invoice.TotalVatAmount)
                throw new FiscalInvoiceOperationException("SUMMARY_SOURCES_CHANGED", "Izvorni ORDER dokumenti su promijenjeni; ponoviti pripremu sumarnog računa.");
            if (!await dbContext.FiscalDevices.AnyAsync(x => x.Id == invoice.DeviceId && x.BusinessUnitId == invoice.BusinessUnitId, ct) ||
                !await dbContext.FiscalOperators.AnyAsync(x => x.Id == invoice.OperatorId && x.CompanyId == invoice.CompanyId &&
                    x.Environment == dbContext.BusinessUnits.Where(u => u.Id == invoice.BusinessUnitId).Select(u => u.Environment).First(), ct))
                throw new FiscalInvoiceOperationException("SUMMARY_CONTEXT_MISMATCH", "ENU i operater moraju pripadati poslovnom/fiskalnom kontekstu sumarnog računa.");
            foreach (var root in roots) dbContext.FiscalSummaryOrders.Add(new()
            {
                SummaryInvoiceId = invoice.Id, OrderInvoiceId = root.Id,
                CreatedAt = invoice.CreatedAt, UpdatedAt = invoice.UpdatedAt
            });
        }
        else if (invoice.IsOrderCorrection)
        {
            var root = roots.Single();
            OrderSummaryRules.EnsureOrderCorrection(root, invoice, await GetCorrectionsAsync(root.Id, ct));
            var summary = await GetSummaryForOrderAsync(root.Id, ct);
            if (summary is not null)
            {
                var stored = await LockInvoiceAsync(summary.Id, ct);
                summary = ToDomain(stored);
                OrderSummaryRules.EnsureSummaryResolved(summary);
                if (summary.Status == FiscalStatus.ReadyForFiscalization)
                {
                    summary.CancelUnsubmitted();
                    ApplyMutable(stored, summary);
                    await dbContext.FiscalSummaryOrders.Where(x => x.SummaryInvoiceId == summary.Id && x.ReleasedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow), ct);
                    if (audit is not null) AddAudit(new("FISCAL_SUMMARY_CANCELLED_FOR_ORDER_CORRECTION", summary.Id, summary.CompanyId,
                        audit.CorrelationId, audit.Actor, DateTimeOffset.UtcNow,
                        new Dictionary<string, string?> { ["orderCorrectionId"] = invoice.Id.ToString() }));
                }
            }
        }
        else if (invoice.IsSummaryCorrection)
        {
            var summary = ToDomain(await LockInvoiceAsync(invoice.OriginalInvoiceId!.Value, ct));
            var corrections = new List<FiscalInvoice>();
            foreach (var root in roots) corrections.AddRange(await GetCorrectionsAsync(root.Id, ct));
            OrderSummaryRules.EnsureSummaryCorrection(summary, invoice, corrections, await GetCorrectionsAsync(summary.Id, ct));
        }
    }

    public async Task CompleteOrderSummaryAsync(FiscalInvoice invoice, AuditEntry audit, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var roots = new Dictionary<Guid, FiscalInvoiceRecord>();
        foreach (var id in invoice.OrderSummary!.OrderInvoiceIds.Order()) roots[id] = await LockInvoiceAsync(id, ct);
        var record = await LockInvoiceAsync(invoice.Id, ct);
        if (record.Status is "Fiscalized" or "StornoCreated")
        {
            if (record.Jikr != invoice.Jikr) throw new FiscalInvoiceOperationException("FISCAL_RECOVERY_JIKR_CONFLICT", "Dokument već ima drugi JIKR.");
            return;
        }
        if (record.Status != "FiscalizationPending") throw new FiscalInvoiceOperationException("INVOICE_STATE_CONFLICT", "Dokument nije preuzet za fiskalizaciju.");
        if (invoice.IsOrderCorrection || invoice.IsSummaryCorrection)
        {
            var originalRecord = invoice.IsOrderCorrection ? roots[invoice.OriginalInvoiceId!.Value] : await LockInvoiceAsync(invoice.OriginalInvoiceId!.Value, ct);
            var original = ToDomain(originalRecord);
            var corrections = await GetCorrectionsAsync(original.Id, ct);
            var corrected = corrections.Where(x => x.Id != invoice.Id && OrderSummaryRules.IsSuccessful(x)).Sum(x => x.TotalGrossAmount) + invoice.TotalGrossAmount;
            if (original.TotalGrossAmount + corrected < 0) throw new FiscalInvoiceOperationException("ORDER_CORRECTION_EXCEEDED", "Korekcija prelazi izvorni iznos.");
            if (original.TotalGrossAmount + corrected == 0 && original.Status != FiscalStatus.StornoCreated)
            { original.MarkStornoCreated(); ApplyMutable(originalRecord, original); }
        }
        ApplyMutable(record, invoice);
        if (invoice.IsSummaryCancellation)
            await dbContext.FiscalSummaryOrders.Where(x => x.SummaryInvoiceId == invoice.OriginalInvoiceId && x.ReleasedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow), ct);
        AddAudit(audit);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
