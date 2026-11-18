using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed class IndexedFiscalExchangeStoreV5(FileFiscalExchangeStoreV5 files, IAdminDiagnosticWriter writer,
    TimeProvider time, ILogger<IndexedFiscalExchangeStoreV5> logger) : IFiscalExchangeStoreV5
{
    private readonly ConcurrentDictionary<Guid, (Guid? Uuid, ExchangeOperation Operation)> _active = new();
    public Task<Guid> SaveRequestAsync(string xml, Guid? uuid, string correlationId, Uri endpoint, string action, CancellationToken ct) =>
        SaveRequestWithContextAsync(xml, uuid, correlationId, endpoint, action, null, ct);

    public async Task<Guid> SaveRequestWithContextAsync(string xml, Guid? uuid, string correlationId, Uri endpoint, string action, ExchangeContext? context, CancellationToken ct)
    {
        var id = await files.SaveRequestAsync(xml, uuid, correlationId, endpoint, action, ct);
        var op = ExchangeEvidenceReader.Operation(action);
        try { await writer.StartExchangeAsync(new(id, uuid, correlationId, op, endpoint.GetLeftPart(UriPartial.Path), action,
            ExchangeEvidenceReader.Hash(xml), time.GetUtcNow(), context), ct); }
        catch (Exception ex) when (ex is not (OperationCanceledException or FiscalInvoiceOperationException))
        {
            logger.LogError("Fiskalni transport nije započet jer indeks nije upisan; exchange {ExchangeId}.", id);
            throw new FiscalInvoiceOperationException("ADMIN_INDEX_UNAVAILABLE", "Indeks pokušaja nije dostupan; zahtjev nije poslat.", 503);
        }
        _active[id] = (uuid, op);
        return id;
    }
    public async Task SaveResponseAsync(Guid id, string xml, int status, CancellationToken ct)
    {
        using var evidenceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await files.SaveResponseAsync(id, xml, status, evidenceDeadline.Token);
        if (_active.TryRemove(id, out var x))
            await PersistIndex(token => writer.FinishExchangeAsync(ExchangeEvidenceReader.Response(id, x.Operation, x.Uuid, xml, status, time.GetUtcNow()), token), id);
    }
    public async Task SaveFailureAsync(Guid id, Exception exception, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await files.SaveFailureAsync(id, exception, deadline.Token); }
        catch (Exception) { logger.LogWarning("Nedostaje zapis greške fiskalne razmjene {ExchangeId}.", id); }
        if (!_active.TryRemove(id, out _)) return;
        var outcome = exception switch
        {
            TimeoutException => ExchangeOutcome.Timeout,
            OperationCanceledException => ExchangeOutcome.Cancelled,
            HttpRequestException => ExchangeOutcome.TransportError,
            IOException => ExchangeOutcome.LocalError,
            _ => ExchangeOutcome.Unknown
        };
        await PersistIndex(token => writer.FinishExchangeAsync(new(id, outcome, time.GetUtcNow(), null, null, null, outcome.ToString().ToUpperInvariant()), token), id);
    }
    public Task MarkLocalCompletionAsync(Guid id, LocalCompletion state) =>
        id == Guid.Empty ? Task.CompletedTask : PersistIndex(ct => writer.CompleteExchangeAsync(id, state, ct), id);
    private async Task PersistIndex(Func<CancellationToken, Task> update, Guid id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await update(deadline.Token); }
        catch (Exception) { logger.LogWarning("Fiskalni indeks treba pomiriti iz arhive; exchange {ExchangeId}.", id); }
    }
    public Task<FiscalInvoiceExchangeEvidenceV5?> ReadSuccessfulInvoiceAsync(Guid id, CancellationToken ct) => files.ReadSuccessfulInvoiceAsync(id, ct);
    public Task<IReadOnlyCollection<FiscalInvoiceExchangeEvidenceV5>> FindSuccessfulInvoicesByIicAsync(string iic, CancellationToken ct) => files.FindSuccessfulInvoicesByIicAsync(iic, ct);
}
