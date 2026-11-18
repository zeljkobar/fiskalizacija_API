using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Persistence;

namespace Summa.Fiscal.Api.Middleware;

public sealed class AdminDiagnosticsOptions
{
    public bool JournalEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 90;
    public string JournalSpoolPath { get; set; } = "App_Data/ApiRequestSpool";
    public string[] TrustedActorClients { get; set; } = [];
}

public sealed class HttpFiscalAuditIdentity(IHttpContextAccessor accessor, IOptions<AdminDiagnosticsOptions> options) : IFiscalAuditIdentity
{
    public string? ClientId => accessor.HttpContext is { } ctx ? AdminReadAccess.RegisteredClient(ctx.User) : null;
    public string? InitiatorId => ActorHeader(BootstrapAdminAuthorizer.ActorIdHeaderName);
    public string? InitiatorName => ActorHeader(BootstrapAdminAuthorizer.ActorNameHeaderName);
    private string? ActorHeader(string name) => accessor.HttpContext is { } ctx && ClientId is { } client &&
        options.Value.TrustedActorClients.Contains(client, StringComparer.Ordinal)
        ? Clean(ctx.Request.Headers[name].FirstOrDefault()) : null;
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = new string(value.Trim().Where(c => !char.IsControl(c) && c is not (':' or ';')).Take(60).ToArray());
        return result.Length == 0 ? null : result;
    }
}

public sealed class FiscalRequestJournal(RequestDelegate next, TimeProvider clock, IOptions<AdminDiagnosticsOptions> options,
    ILogger<FiscalRequestJournal> logger)
{
    internal const string ErrorKey = "FiscalRequestError";
    internal const string CompanyKey = "FiscalRequestCompany";
    internal const string InvoiceKey = "FiscalRequestInvoice";
    internal const string OutcomeKey = "FiscalRequestOutcome";

    public async Task InvokeAsync(HttpContext context, IServiceProvider services, IFiscalAuditIdentity identity)
    {
        var start = clock.GetUtcNow(); var timer = Stopwatch.GetTimestamp();
        try { await next(context); }
        finally
        {
            if (options.Value.JournalEnabled && services.GetService<IAdminDiagnosticWriter>() is { } writer)
            {
                var status = context.Response.StatusCode;
                var method = context.Request.Method;
                var outcome = context.Items[OutcomeKey] as RequestOutcome? ?? (context.RequestAborted.IsCancellationRequested ? RequestOutcome.Cancelled : status switch
                {
                    400 or 422 => RequestOutcome.ValidationError,
                    401 => RequestOutcome.Unauthorized, 403 => RequestOutcome.Forbidden, 409 => RequestOutcome.Conflict,
                    >= 500 => RequestOutcome.InternalError, >= 400 => RequestOutcome.Unknown, _ => RequestOutcome.Succeeded
                });
                if (outcome != RequestOutcome.Succeeded || identity.ClientId != null && method is "POST" or "PUT" or "PATCH" or "DELETE")
                    await RecordAsync(context, services, writer, identity, start, timer, outcome);
            }
        }
    }

    private async Task RecordAsync(HttpContext ctx, IServiceProvider services, IAdminDiagnosticWriter writer, IFiscalAuditIdentity identity,
        DateTimeOffset start, long timer, RequestOutcome outcome)
    {
        var client = identity.ClientId;
        var status = ctx.Response.StatusCode;
        Guid? company = null, invoice = null;
        var endpoint = ctx.GetEndpoint() as RouteEndpoint;
        var route = endpoint?.RoutePattern.RawText ?? "unmatched";
        var method = ctx.Request.Method.ToUpperInvariant();
        if (method is not ("GET" or "POST" or "DELETE" or "PATCH" or "PUT" or "HEAD" or "OPTIONS")) method = "OTHER";
        if (client != null && status is not (401 or 403))
        {
            using var lookupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var scope = AdminReadAccess.Scope(ctx.User);
                if (ctx.Items[CompanyKey] is Guid verified && scope.Allows(verified)) company = verified;
                if (company != null && ctx.Items[InvoiceKey] is Guid resultId) invoice = resultId;
                if (company == null && services.GetService<IDbContextFactory<SummaFiscalDbContext>>() is { } factory)
                {
                    await using var db = await factory.CreateDbContextAsync(lookupTimeout.Token);
                    if (route.StartsWith("api/v1/fiscal/invoices", StringComparison.OrdinalIgnoreCase) &&
                        Guid.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
                    {
                        var owner = await db.FiscalInvoices.Where(x => x.Id == id).Select(x => (Guid?)x.CompanyId).SingleOrDefaultAsync(lookupTimeout.Token);
                        if (owner.HasValue && scope.Allows(owner.Value)) { company = owner; invoice = id; }
                    }
                    if (company == null && Guid.TryParse(ctx.Request.RouteValues["companyId"]?.ToString(), out var companyId) &&
                        scope.Allows(companyId) && await db.Companies.AnyAsync(x => x.Id == companyId, lookupTimeout.Token)) company = companyId;
                }
            }
            catch (Exception) { logger.LogWarning("API zahtjev nije mogao biti pripisan firmi."); company = null; invoice = null; }
        }
        var end = clock.GetUtcNow();
        var error = outcome == RequestOutcome.Succeeded ? null : new AdminError(outcome.ToString(),
            AdminFormatting.SafeCode(ctx.Items[ErrorKey] as string, outcome.ToString().ToUpperInvariant()), "API operacija nije završena uspješno.", end);
        var entry = new RequestAuditEvent(new(Guid.NewGuid(), start, end, (long)Stopwatch.GetElapsedTime(timer).TotalMilliseconds,
            method, route, status, outcome.ToString(), error, client, identity.InitiatorId, company, invoice, ctx.TraceIdentifier),
            company is null || status is 401 or 403);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await writer.RecordRequestsAsync([entry], deadline.Token); }
        catch (Exception)
        {
            logger.LogWarning("API dnevnik nije dostupan; zapis {RequestId} ide u lokalni red.", entry.Row.RequestId);
            try { await ApiRequestSpool.SaveAsync(options.Value.JournalSpoolPath, entry); }
            catch (Exception) { logger.LogError("API zapis {RequestId} nije sačuvan ni u lokalni red.", entry.Row.RequestId); }
        }
    }
}

public sealed class FiscalRequestResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext c)
    {
        if (c.Result is not ObjectResult result || result.Value is null) return;
        if (result.Value is ApiResponse<FiscalInvoiceResult> { Data: { } doc })
        {
            c.HttpContext.Items[FiscalRequestJournal.CompanyKey] = doc.CompanyId;
            c.HttpContext.Items[FiscalRequestJournal.InvoiceKey] = doc.Id;
        }
        if (result.Value is ApiResponse<FiscalInvoiceSubmissionResultV5> { Data: { IsSuccess: false } outcome })
        {
            c.HttpContext.Items[FiscalRequestJournal.ErrorKey] = outcome.FaultCode;
            c.HttpContext.Items[FiscalRequestJournal.OutcomeKey] = outcome.Status == Summa.Fiscal.Domain.Invoices.FiscalStatus.FiscalizationFailed &&
                outcome.FaultCode != "UNVERIFIED_PU_RESPONSE" ? RequestOutcome.FiscalRejected : RequestOutcome.Unknown;
        }
        if (result.Value is IApiResponse { Error: { } error })
            c.HttpContext.Items[FiscalRequestJournal.ErrorKey] = error.Code;
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}

internal static class ApiRequestSpool
{
    public static async Task SaveAsync(string root, RequestAuditEvent entry)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, entry.Row.RequestId.ToString("N"));
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(entry), deadline.Token);
        File.Move(path + ".tmp", path + ".json");
    }
    public static async Task ReplayAsync(string root, IAdminDiagnosticWriter writer, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*.json").Take(250))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id) ||
                !File.Exists(file) || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(file).Length > 8192) continue;
            RequestAuditEvent? item;
            try { item = JsonSerializer.Deserialize<RequestAuditEvent>(await File.ReadAllTextAsync(file, ct)); }
            catch (Exception ex) when (ex is JsonException or FileNotFoundException) { continue; }
            if (item?.Row.RequestId != id) continue;
            await writer.RecordRequestsAsync([item], ct);
            File.Delete(file);
        }
    }
}

public sealed class FiscalRequestJournalMaintenance(IAdminDiagnosticWriter writer, TimeProvider clock,
    IOptions<AdminDiagnosticsOptions> options, ILogger<FiscalRequestJournalMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var nextPrune = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!options.Value.JournalEnabled) continue;
            try
            {
                await ApiRequestSpool.ReplayAsync(options.Value.JournalSpoolPath, writer, stoppingToken);
                var now = clock.GetUtcNow();
                if (now >= nextPrune)
                {
                    await writer.PruneRequestsAsync(now.AddDays(-Math.Clamp(options.Value.RetentionDays, 1, 3650)), stoppingToken);
                    nextPrune = now.AddDays(1);
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("Održavanje API dnevnika nije završeno."); }
        }
    }
}
