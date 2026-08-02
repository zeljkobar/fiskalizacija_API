namespace Summa.Fiscal.Application.Abstractions;

public interface IInvoiceNumberSequence
{
    Task<int> ReserveNextAsync(
        Guid deviceId,
        int year,
        CancellationToken cancellationToken);
}
