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
}
