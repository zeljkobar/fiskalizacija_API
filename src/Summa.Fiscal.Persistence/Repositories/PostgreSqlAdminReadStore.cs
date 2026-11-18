using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed partial class PostgreSqlAdminReadStore(SummaFiscalDbContext db, TimeProvider clock) : IAdminReadStore
{
    private sealed class Classified
    {
        public required FiscalInvoiceRecord Invoice { get; init; }
        public DocumentCategory Category { get; init; }
    }
    private IQueryable<Classified> Classify(IQueryable<FiscalInvoiceRecord> query) => query.Select(x => new Classified
    {
        Invoice = x,
        Category = x.IsSummaryCorrection ? (x.OrderSummaryJson != null && EF.Functions.JsonContains(x.OrderSummaryJson, """{"IsCancellation":true}""")
            ? DocumentCategory.SummaryCancellation : DocumentCategory.SummaryCorrection) :
        x.AdvanceRestorationStornoInvoiceId != null ? DocumentCategory.AdvanceRestoration :
        x.AdvanceSettlementFinalInvoiceId != null ? DocumentCategory.AdvanceSettlement :
        x.IsOrderCorrection ? DocumentCategory.OrderCorrection :
        x.InvoiceType == "Corrective" ? (x.OriginalInvoice == null || x.OriginalInvoice.CompanyId != x.CompanyId ? DocumentCategory.Other :
            x.OriginalInvoice.InvoiceType == "Advance" ? DocumentCategory.AdvanceStorno :
            x.OriginalInvoice.InvoiceType == "Normal" ? DocumentCategory.SaleCorrection : DocumentCategory.Other) :
        x.InvoiceType == "Advance" ? DocumentCategory.AdvanceReceipt :
        x.InvoiceType == "Summary" ? DocumentCategory.Summary :
        x.InvoiceType != "Normal" ? DocumentCategory.Other :
        x.Payments.Count() == 1 && x.Payments.Any(p => p.PaymentType == "Order") ? DocumentCategory.OrderSale :
        x.Payments.Any(p => p.PaymentType == "Advance") ? DocumentCategory.FinalWithAdvance : DocumentCategory.StandardSale
    });

    private IQueryable<FiscalInvoiceRecord> InvoiceQuery(CompanyScope scope, Guid? companyId, Guid? unitId = null,
        Guid? deviceId = null, Guid? operatorId = null, FiscalEnvironment? environment = null, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var query = db.FiscalInvoices.AsNoTracking();
        if (!scope.IsPlatform) query = query.Where(x => scope.CompanyIds.Contains(x.CompanyId));
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId);
        if (unitId.HasValue) query = query.Where(x => x.BusinessUnitId == unitId);
        if (deviceId.HasValue) query = query.Where(x => x.DeviceId == deviceId);
        if (operatorId.HasValue) query = query.Where(x => x.OperatorId == operatorId);
        if (environment.HasValue)
        {
            var env = environment.ToString();
            query = environment == FiscalEnvironment.Unknown ? query.Where(x => x.AdminEnvironment == null || x.AdminEnvironment == "Unknown")
                : query.Where(x => x.AdminEnvironment == env);
        }
        if (from.HasValue) query = query.Where(x => x.IssueDateTime >= from);
        if (to.HasValue) query = query.Where(x => x.IssueDateTime < to);
        return query;
    }

    private sealed record InvoiceProjection(Classified Document, string Tin, string CompanyName, string UnitCode, string UnitName,
        string? Tcr, string DeviceName, string OperatorCode, string? FirstName, string? LastName);
    private static IQueryable<InvoiceProjection> Project(IQueryable<Classified> query) => query.Select(d => new InvoiceProjection(d,
        d.Invoice.Company.Tin, d.Invoice.Company.LegalName, d.Invoice.BusinessUnit.Code, d.Invoice.BusinessUnit.Name,
        d.Invoice.Device.TcrCode, d.Invoice.Device.InternalCode, d.Invoice.Operator.OperatorCode, d.Invoice.Operator.FirstName, d.Invoice.Operator.LastName));

    public async Task<AdminPage<AdminInvoiceRow>> InvoicesAsync(CompanyScope scope, InvoiceFilter f, CancellationToken ct)
    {
        var offset = f.Offset(); var (from, to) = f.Range();
        var query = InvoiceQuery(scope, f.CompanyId, f.BusinessUnitId, f.DeviceId, f.OperatorId, f.Environment, from, to);
        if (f.Status is { } status) query = query.Where(x => x.Status == status.ToString());
        if (f.InvoiceType is { } type) query = query.Where(x => x.InvoiceType == type.ToString());
        if (f.PaymentType is { } method) query = query.Where(x => x.Payments.Any(p => p.PaymentType == method.ToString()));
        if (AdminFormatting.Filter(f.InvoiceNumber) is { } number) query = query.Where(x => x.InvoiceNumber == number);
        if (AdminFormatting.Filter(f.OfficialInvoiceNumber)?.ToUpperInvariant() is { } official) query = query.Where(x => x.OfficialInvoiceNumber!.ToUpper() == official);
        if (AdminFormatting.Filter(f.Iic)?.ToUpperInvariant() is { } iic) query = query.Where(x => x.Iic!.ToUpper() == iic);
        if (AdminFormatting.Filter(f.Jikr)?.ToUpperInvariant() is { } jikr) query = query.Where(x => x.Jikr!.ToUpper() == jikr);
        if (AdminFormatting.Filter(f.BuyerId) is { } buyerId) query = query.Where(x => x.BuyerIdentificationNumber == buyerId);
        if (AdminFormatting.Filter(f.BuyerName, 3) is { } name)
        {
            var pattern = "%" + name.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.BuyerName!, pattern, "\\"));
        }
        var documents = Classify(query);
        if (f.Category is { } category) documents = documents.Where(x => x.Category == category);
        var asc = f.Direction == SortDirection.Asc;
        var ordered = (f.Sort, asc) switch
        {
            (InvoiceSort.CreatedAt, true) => documents.OrderBy(x => x.Invoice.CreatedAt),
            (InvoiceSort.CreatedAt, false) => documents.OrderByDescending(x => x.Invoice.CreatedAt),
            (InvoiceSort.TotalGrossAmount, true) => documents.OrderBy(x => x.Invoice.TotalAmount),
            (InvoiceSort.TotalGrossAmount, false) => documents.OrderByDescending(x => x.Invoice.TotalAmount),
            (_, true) => documents.OrderBy(x => x.Invoice.IssueDateTime),
            _ => documents.OrderByDescending(x => x.Invoice.IssueDateTime)
        };
        ordered = asc ? ordered.ThenBy(x => x.Invoice.Id) : ordered.ThenByDescending(x => x.Invoice.Id);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var count = await documents.CountAsync(ct);
        var rows = await Project(ordered.Skip(offset).Take(f.PageSize)).ToArrayAsync(ct);
        var mapped = await MapRows(rows, ct);
        await tx.CommitAsync(ct);
        return new(mapped, f.Page, f.PageSize, count);
    }

    private async Task<AdminInvoiceRow[]> MapRows(InvoiceProjection[] rows, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Document.Invoice.Id).ToArray();
        var last = await db.FiscalExchanges.AsNoTracking().Where(x => x.InvoiceId.HasValue && ids.Contains(x.InvoiceId.Value) &&
            x.Provenance != null && x.CompanyId != null)
            .GroupBy(x => new { x.InvoiceId, x.CompanyId }).Select(g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First()).ToArrayAsync(ct);
        var latest = last.ToDictionary(x => (x.InvoiceId!.Value, x.CompanyId!.Value));
        return rows.Select(x => MapInvoice(x, latest.GetValueOrDefault((x.Document.Invoice.Id, x.Document.Invoice.CompanyId)))).ToArray();
    }

    private static FiscalContextSnapshot? ReadContext(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<FiscalContextSnapshot>(json);

    private static AdminInvoiceRow MapInvoice(InvoiceProjection row, FiscalExchangeRecord? attempt)
    {
        var x = row.Document.Invoice;
        var snapshot = ReadContext(x.FirstSendContextJson ?? x.PreparationContextJson);
        var source = x.FirstSendContextJson != null ? "SendSnapshot" : x.PreparationContextJson != null ? "PreparationSnapshot" : "CurrentDisplay";
        AdminLabel Label(Guid id, string? code, string? currentCode, string? name) =>
            new(id, code ?? currentCode, code != null && code != currentCode ? null : name, code == null ? "CurrentDisplay" : source);
        return new(x.Id, x.CompanyId, snapshot?.Tin ?? row.Tin, row.CompanyName, x.AdminEnvironment ?? "Unknown",
            x.AdminEnvironmentSource ?? "Unknown", x.BusinessUnitId, x.DeviceId, x.OperatorId,
            Label(x.BusinessUnitId, snapshot?.BusinessUnitCode, row.UnitCode, row.UnitName),
            Label(x.DeviceId, snapshot?.TcrCode, row.Tcr, row.DeviceName),
            Label(x.OperatorId, snapshot?.OperatorCode, row.OperatorCode, string.Join(" ", new[] { row.FirstName, row.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)))),
            x.InvoiceNumber, x.OfficialInvoiceNumber, x.IssueDateTime, x.CreatedAt, x.FiscalizedAt,
            x.BuyerName == null ? null : new(x.BuyerIdentificationType, x.BuyerIdentificationNumber, x.BuyerName),
            x.InvoiceType, row.Document.Category, x.Status, x.Currency, AdminFormatting.Money(x.NetAmount), AdminFormatting.Money(x.VatAmount),
            AdminFormatting.Money(x.TotalAmount), x.Iic, x.Jikr, x.InvoiceType == "Summary" || x.IsSummaryCorrection,
            x.OriginalInvoiceId, x.AdvanceSettlementFinalInvoiceId, x.AdvanceRestorationStornoInvoiceId, ExchangeError(attempt));
    }

    public async Task<InvoiceAdminDetail?> InvoiceDetailAsync(CompanyScope scope, Guid id, bool includeExchangeIds, CancellationToken ct)
    {
        var row = await Project(Classify(InvoiceQuery(scope, null).Where(x => x.Id == id))).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var x = row.Document.Invoice;
        var query = db.FiscalExchanges.AsNoTracking().Where(e => e.InvoiceId == id && e.CompanyId == x.CompanyId && e.Provenance != null)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id);
        var last = await query.FirstOrDefaultAsync(ct);
        var rejected = await query.FirstOrDefaultAsync(e => e.Outcome == "Rejected", ct);
        return new(MapInvoice(row, last), ReadContext(x.PreparationContextJson), ReadContext(x.FirstSendContextJson),
            last is null ? null : MapExchange(last) with
            { ExchangeId = includeExchangeIds ? last.Id : Guid.Empty, CorrelationId = includeExchangeIds ? last.CorrelationId : "" },
            ExchangeError(rejected), last is null ? "NotRecorded" : last.EvidenceState == "Complete" ? "Indexed" : "Partial");
    }

    private IQueryable<FiscalExchangeRecord> ExchangeQuery(CompanyScope scope)
    {
        var query = db.FiscalExchanges.AsNoTracking().Where(x => x.CompanyId.HasValue && x.Provenance != null);
        return scope.IsPlatform ? query : query.Where(x => scope.CompanyIds.Contains(x.CompanyId!.Value));
    }
    public async Task<AdminExchangeRow?> ExchangeAsync(CompanyScope scope, Guid id, CancellationToken ct) =>
        await ExchangeQuery(scope).SingleOrDefaultAsync(x => x.Id == id, ct) is { } row ? MapExchange(row) : null;

    public async Task<AdminPage<AdminExchangeRow>> ExchangesAsync(CompanyScope scope, ExchangeFilter f, CancellationToken ct)
    {
        var q = ExchangeQuery(scope);
        if (f.CompanyId is { } company) q = q.Where(x => x.CompanyId == company);
        if (f.InvoiceId is { } invoice) q = q.Where(x => x.InvoiceId == invoice);
        if (f.Environment is { } env) q = q.Where(x => (x.Environment ?? "Unknown") == env.ToString());
        if (f.Operation is { } op) q = q.Where(x => x.Operation == op.ToString());
        if (f.Outcome is { } outcome) q = q.Where(x => x.Outcome == outcome.ToString());
        if (f.LocalCompletion is { } local) q = q.Where(x => x.LocalCompletion == local.ToString());
        if (f.PuHttpStatus is { } status) q = q.Where(x => x.HttpStatusCode == status);
        var correlation = AdminFormatting.Filter(f.CorrelationId);
        if (correlation != null) q = q.Where(x => x.CorrelationId == correlation);
        var (from, to) = OperationalRange(f, f.InvoiceId.HasValue || correlation != null);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from);
        if (to.HasValue) q = q.Where(x => x.CreatedAt < to);
        var earliestLive = await ExchangeQuery(scope).Where(x => x.Provenance == "Live" &&
            (f.CompanyId == null || x.CompanyId == f.CompanyId)).MinAsync(x => (DateTimeOffset?)x.CreatedAt, ct);
        return await PageAsync(q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id), f, MapExchange, new(earliestLive, true), ct);
    }

    public async Task<AdminPage<AdminRequestRow>> RequestsAsync(CompanyScope scope, RequestFilter f, CancellationToken ct)
    {
        var q = db.FiscalApiRequests.AsNoTracking().Where(x => x.Security == (f.Scope == RequestScope.Security));
        if (f.Scope == RequestScope.Security && !scope.IsPlatform) q = q.Where(x => false);
        if (f.Scope == RequestScope.Tenant)
        {
            q = q.Where(x => x.CompanyId.HasValue);
            if (!scope.IsPlatform) q = q.Where(x => scope.CompanyIds.Contains(x.CompanyId!.Value));
        }
        if (f.CompanyId is { } company) q = q.Where(x => x.CompanyId == company);
        if (f.InvoiceId is { } invoice) q = q.Where(x => x.InvoiceId == invoice);
        if (f.ApiHttpStatus is { } status) q = q.Where(x => x.ApiHttpStatus == status);
        if (f.Outcome is { } outcome) q = q.Where(x => x.Outcome == outcome.ToString());
        if (AdminFormatting.Filter(f.ClientId) is { } client) q = q.Where(x => x.ClientId == client);
        if (AdminFormatting.Filter(f.Method)?.ToUpperInvariant() is { } method) q = q.Where(x => x.Method == method);
        if (AdminFormatting.Filter(f.RouteTemplate) is { } route) q = q.Where(x => x.RouteTemplate == route);
        if (AdminFormatting.Filter(f.ErrorCode) is { } code) q = q.Where(x => x.ErrorCode == code);
        var correlation = AdminFormatting.Filter(f.CorrelationId);
        if (correlation != null) q = q.Where(x => x.CorrelationId == correlation);
        var (from, to) = OperationalRange(f, f.InvoiceId.HasValue || correlation != null);
        var earliest = await q.MinAsync(x => (DateTimeOffset?)x.CreatedAt, ct);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from);
        if (to.HasValue) q = q.Where(x => x.CreatedAt < to);
        return await PageAsync(q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id), f, x =>
            new AdminRequestRow(x.Id, x.CreatedAt, x.CompletedAt, x.DurationMs, x.Method, x.RouteTemplate, x.ApiHttpStatus, x.Outcome,
                x.ErrorCode == null ? null : new(x.Outcome, x.ErrorCode, "API operacija nije završena uspješno.", x.CompletedAt ?? x.CreatedAt),
                x.ClientId, x.InitiatorId, x.CompanyId, x.InvoiceId, x.CorrelationId), new(earliest, true), ct);
    }

    public async Task<AdminPage<FiscalAuditSummary>> AuditAsync(CompanyScope scope, Guid companyId, TimeFilter f,
        string? action, string? actor, string? correlationId, Guid? invoiceId, CancellationToken ct)
    {
        var q = db.FiscalAudits.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!scope.IsPlatform) q = q.Where(x => scope.CompanyIds.Contains(x.CompanyId!.Value));
        if (AdminFormatting.Filter(correlationId) is { } corr) q = q.Where(x => x.CorrelationId == corr);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(actor)) q = q.Where(x => x.Actor == actor);
        if (f.From is { } from) q = q.Where(x => x.CreatedAt >= from.ToUniversalTime());
        if (f.To is { } to) q = q.Where(x => x.CreatedAt <= to.ToUniversalTime());
        if (invoiceId is Guid id)
        {
            var legacy = JsonSerializer.Serialize(new { InvoiceId = id });
            q = q.Where(x => db.FiscalInvoices.Any(i => i.Id == id && i.CompanyId == companyId) &&
                (x.InvoiceId == id || x.InvoiceId == null && EF.Functions.JsonContains(x.DataJson, legacy)));
        }
        return await PageAsync(q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id), f,
            x => new FiscalAuditSummary(x.Id, x.CompanyId, x.Action, x.CorrelationId, x.Actor, x.DataJson, x.CreatedAt)
            { InvoiceId = x.InvoiceId ?? invoiceId, ClientId = x.ClientId, InitiatorId = x.InitiatorId, InitiatorName = x.InitiatorName, Executor = x.Executor }, null, ct);
    }

    private (DateTimeOffset? From, DateTimeOffset? To) OperationalRange(TimeFilter f, bool exact)
    {
        var (from, to) = f.Range(366);
        return (from ?? (exact ? null : clock.GetUtcNow().AddDays(-30)), to);
    }
    private async Task<AdminPage<T>> PageAsync<TRecord, T>(IQueryable<TRecord> query, PageFilter f, Func<TRecord, T> map, Coverage? coverage, CancellationToken ct)
    {
        var offset = f.Offset();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var count = await query.CountAsync(ct);
        var rows = await query.Skip(offset).Take(f.PageSize).ToArrayAsync(ct);
        await tx.CommitAsync(ct);
        return new(rows.Select(map).ToArray(), f.Page, f.PageSize, count, coverage);
    }
    private static AdminError? ExchangeError(FiscalExchangeRecord? x) => x == null ? null : x.Outcome == "Rejected"
        ? AdminFormatting.PuError(x.FaultCode, x.CompletedAt ?? x.CreatedAt)
        : x.FaultCode == null ? null : new(x.LocalCompletion == "Failed" ? "LocalError" : x.Outcome ?? "Unknown",
            AdminFormatting.SafeCode(x.FaultCode, "EXCHANGE_ERROR"), "Pokušaj nije lokalno završen bez greške.", x.CompletedAt ?? x.CreatedAt);
    private static AdminExchangeRow MapExchange(FiscalExchangeRecord x) => new(x.Id, x.InvoiceId, x.CompanyId,
        x.Environment ?? "Unknown", x.EnvironmentSource ?? "Unknown", x.Operation, x.CreatedAt, x.CompletedAt,
        x.CompletedAt.HasValue ? (long)(x.CompletedAt.Value - x.CreatedAt).TotalMilliseconds : null,
        x.CorrelationId, x.RequestUuid, x.HttpStatusCode, x.Outcome ?? "Unknown", ExchangeError(x), x.LocalCompletion ?? "Unknown",
        x.ProfileFingerprint, x.Provenance ?? "Backfill", x.EvidenceState ?? "Incomplete");
}
