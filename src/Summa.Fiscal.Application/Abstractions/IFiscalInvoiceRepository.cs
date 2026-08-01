using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Abstractions;

public interface IFiscalInvoiceRepository
{
    Task<FiscalInvoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<FiscalInvoice?> GetByIdempotencyKeyAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task AddAsync(FiscalInvoice invoice, CancellationToken cancellationToken);
    Task UpdateAsync(FiscalInvoice invoice, CancellationToken cancellationToken);
}
