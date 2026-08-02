using System.Collections.Concurrent;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Infrastructure.Persistence;

public sealed class InMemoryInvoiceNumberSequence : IInvoiceNumberSequence
{
    private readonly ConcurrentDictionary<(Guid DeviceId, int Year), int> _numbers = new();

    public Task<int> ReserveNextAsync(
        Guid deviceId,
        int year,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = _numbers.AddOrUpdate((deviceId, year), 1, static (_, current) => checked(current + 1));
        return Task.FromResult(next);
    }
}
