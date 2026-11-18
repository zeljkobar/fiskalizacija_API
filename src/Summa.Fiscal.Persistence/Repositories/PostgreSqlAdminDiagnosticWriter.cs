using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlAdminDiagnosticWriter(IDbContextFactory<SummaFiscalDbContext> factory) : IAdminDiagnosticWriter
{
    public async Task StartExchangeAsync(ExchangeStart x, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var context = x.Context;
        var snapshot = context?.Snapshot;
        if (context?.InvoiceId is Guid invoiceId)
        {
            var invoice = await db.FiscalInvoices.FromSqlInterpolated(
                $"SELECT * FROM fiscal.fiscal_invoices WHERE \"Id\"={invoiceId} AND \"CompanyId\"={context.CompanyId} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "Račun nije pronađen.", 404);
            if (invoice.FirstSendContextJson != null && invoice.AdminEnvironment != snapshot!.Environment)
                throw new FiscalInvoiceOperationException("FISCAL_ENVIRONMENT_CONFLICT", "Prethodni pokušaj pripada drugom fiskalnom okruženju.", 409);
            invoice.AdminEnvironment = snapshot!.Environment;
            invoice.AdminEnvironmentSource = "SendSnapshot";
            invoice.FirstSendContextJson ??= JsonSerializer.Serialize(snapshot);
        }
        db.FiscalExchanges.Add(new()
        {
            Id = x.Id, RequestUuid = x.RequestUuid, CorrelationId = x.CorrelationId, CompanyId = context?.CompanyId,
            InvoiceId = context?.InvoiceId, Endpoint = x.Endpoint, SoapAction = x.SoapAction, Operation = x.Operation.ToString(),
            RequestSha256 = x.RequestHash, RequestStoragePath = x.Id.ToString("N") + "/request.xml", CreatedAt = x.StartedAt,
            UpdatedAt = x.StartedAt, Environment = snapshot?.Environment, EnvironmentSource = snapshot == null ? "Unknown" : "SendSnapshot",
            Outcome = "Started", LocalCompletion = context is null ? "NotApplicable" : "Pending", Provenance = "Live",
            EvidenceState = "RequestOnly", ProfileFingerprint = snapshot?.ProfileFingerprint, ContextJson = snapshot == null ? null : JsonSerializer.Serialize(snapshot)
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task FinishExchangeAsync(ExchangeFinish x, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.FiscalExchanges.Where(e => e.Id == x.Id && e.Outcome == "Started").ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Outcome, x.Outcome.ToString()).SetProperty(e => e.CompletedAt, x.CompletedAt).SetProperty(e => e.UpdatedAt, x.CompletedAt)
            .SetProperty(e => e.HttpStatusCode, x.HttpStatus).SetProperty(e => e.ResponseSha256, x.ResponseHash)
            .SetProperty(e => e.ResponseStoragePath, x.ResponseHash == null ? null : x.Id.ToString("N") + "/response.xml")
            .SetProperty(e => e.FaultCode, x.ErrorCode).SetProperty(e => e.FiscalIdentifier, x.FiscalIdentifier)
            .SetProperty(e => e.EvidenceState, x.ResponseHash == null ? "RequestOnly" : x.Outcome == ExchangeOutcome.Unknown ? "Incomplete" : "Complete"), ct);
    }
    public async Task CompleteExchangeAsync(Guid id, LocalCompletion state, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.FiscalExchanges.Where(e => e.Id == id && e.LocalCompletion != "Completed").ExecuteUpdateAsync(s => s
            .SetProperty(e => e.LocalCompletion, state.ToString())
            .SetProperty(e => e.FaultCode, e => state == LocalCompletion.Failed && e.Outcome == "Accepted" ? "LOCAL_COMPLETION_FAILED" :
                state == LocalCompletion.Completed && e.FaultCode == "LOCAL_COMPLETION_FAILED" ? null : e.FaultCode), ct);
    }
    public async Task RecordRequestsAsync(IReadOnlyList<RequestAuditEvent> events, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        foreach (var entry in events)
        {
            var x = entry.Row;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO fiscal.fiscal_api_requests ("Id","CreatedAt","UpdatedAt","CompletedAt","DurationMs","Method","RouteTemplate",
                  "ApiHttpStatus","Outcome","ErrorCode","ClientId","InitiatorId","CompanyId","InvoiceId","CorrelationId","Security")
                VALUES ({x.RequestId},{x.StartedAt},{x.CompletedAt ?? x.StartedAt},{x.CompletedAt},{x.DurationMs},{x.Method},{x.RouteTemplate},
                  {x.ApiHttpStatus},{x.Outcome},{x.Error?.Code},{x.ClientId},{x.InitiatorId},{x.CompanyId},
                  {x.InvoiceId},{x.CorrelationId},{entry.Security}) ON CONFLICT ("Id") DO NOTHING
                """, ct);
        }
    }
    public async Task PruneRequestsAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.FiscalApiRequests.Where(x => x.CreatedAt < before).ExecuteDeleteAsync(ct);
    }
}
