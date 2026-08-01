using System.Collections.Concurrent;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Infrastructure.Audit;

public sealed class InMemoryAuditService : IAuditService
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyCollection<AuditEntry> Entries => _entries.ToArray();

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
