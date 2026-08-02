using System.Collections.Concurrent;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Infrastructure.Persistence;

public sealed class InMemoryFiscalInvoiceRepository : IFiscalInvoiceRepository
{
    private readonly ConcurrentDictionary<Guid, FiscalInvoice> _invoices = new();
    private readonly ConcurrentDictionary<(Guid CompanyId, string Key), Guid> _idempotencyIndex = new();

    public Task<FiscalInvoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _invoices.TryGetValue(id, out var invoice);
        return Task.FromResult(invoice);
    }

    public Task<FiscalInvoice?> GetByIdempotencyKeyAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_idempotencyIndex.TryGetValue((companyId, idempotencyKey), out var invoiceId) &&
            _invoices.TryGetValue(invoiceId, out var invoice))
        {
            return Task.FromResult<FiscalInvoice?>(invoice);
        }

        return Task.FromResult<FiscalInvoice?>(null);
    }

    public Task<FiscalInvoice?> GetByOriginalInvoiceIdAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_invoices.Values.SingleOrDefault(x => x.OriginalInvoiceId == originalInvoiceId));
    }

    public Task AddAsync(FiscalInvoice invoice, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_idempotencyIndex.TryAdd((invoice.CompanyId, invoice.IdempotencyKey), invoice.Id))
        {
            throw new InvalidOperationException("Idempotency key je već iskorišćen.");
        }

        if (!_invoices.TryAdd(invoice.Id, invoice))
        {
            _idempotencyIndex.TryRemove((invoice.CompanyId, invoice.IdempotencyKey), out _);
            throw new InvalidOperationException("Račun sa istim identifikatorom već postoji.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(FiscalInvoice invoice, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_invoices.ContainsKey(invoice.Id))
        {
            throw new InvalidOperationException("Račun koji se ažurira ne postoji.");
        }

        _invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    public Task CompleteCorrectiveAsync(
        FiscalInvoice corrective,
        FiscalInvoice original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_invoices.ContainsKey(corrective.Id) || !_invoices.ContainsKey(original.Id))
            throw new InvalidOperationException("Originalni ili korektivni račun ne postoji.");
        _invoices[corrective.Id] = corrective;
        _invoices[original.Id] = original;
        return Task.CompletedTask;
    }
}
