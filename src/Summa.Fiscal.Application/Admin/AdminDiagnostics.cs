namespace Summa.Fiscal.Application.Admin;

public sealed record ExchangeContext(Guid CompanyId, Guid? InvoiceId, FiscalContextSnapshot Snapshot);
public sealed record ExchangeStart(Guid Id, Guid? RequestUuid, string CorrelationId, ExchangeOperation Operation,
    string Endpoint, string SoapAction, string RequestHash, DateTimeOffset StartedAt, ExchangeContext? Context);
public sealed record ExchangeFinish(Guid Id, ExchangeOutcome Outcome, DateTimeOffset CompletedAt,
    int? HttpStatus, string? ResponseHash, string? FiscalIdentifier, string? ErrorCode);

public interface IAdminDiagnosticWriter
{
    Task StartExchangeAsync(ExchangeStart start, CancellationToken ct);
    Task FinishExchangeAsync(ExchangeFinish finish, CancellationToken ct);
    Task CompleteExchangeAsync(Guid exchangeId, LocalCompletion state, CancellationToken ct);
    Task RecordRequestsAsync(IReadOnlyList<RequestAuditEvent> events, CancellationToken ct);
    Task PruneRequestsAsync(DateTimeOffset before, CancellationToken ct);
}
