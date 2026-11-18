using System.Globalization;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Admin;

public enum FiscalEnvironment { Unknown, Test, Production }
public enum DocumentCategory { StandardSale, FinalWithAdvance, OrderSale, SaleCorrection, OrderCorrection, AdvanceReceipt, AdvanceStorno, AdvanceSettlement, AdvanceRestoration, Summary, SummaryCorrection, SummaryCancellation, Other }
public enum ReportGrouping { Day, Month, Year }
public enum ReportMetric { DocumentCount, SaleInvoiceCount, SalesPositive, SalesCorrections, SalesNet, Category, Payment, Status, OpenWorkflow }
public enum OpenWorkflow { AdvanceSettlementDue, AdvanceRestorationDue, SummaryCorrectionDue }
public enum InvoiceSort { IssueDateTime, CreatedAt, TotalGrossAmount }
public enum SortDirection { Asc, Desc }
public enum RequestScope { Tenant, Security }
public enum ExchangeOperation { RegisterInvoice, RegisterTcr, RegisterCashDeposit, Other }
public enum ExchangeOutcome { Started, Accepted, Rejected, Timeout, Cancelled, TransportError, LocalError, Unknown }
public enum LocalCompletion { NotApplicable, Pending, Completed, Failed, Unknown }
public enum RequestOutcome { Succeeded, ValidationError, Unauthorized, Forbidden, Conflict, FiscalRejected, TransportError, InternalError, Cancelled, Unknown }

public sealed record CompanyScope(bool IsPlatform, Guid[] CompanyIds)
{
    public bool Allows(Guid id) => IsPlatform || CompanyIds.Contains(id);
}

public class PageFilter
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int Offset()
    {
        if (Page < 1 || PageSize is < 1 or > 200 || ((long)Page - 1) * PageSize > int.MaxValue)
            throw new FiscalInvoiceOperationException("INVALID_PAGE", "Neispravna stranica ili veličina stranice (1–200).", 400);
        return (Page - 1) * PageSize;
    }
}
public class TimeFilter : PageFilter
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public (DateTimeOffset? From, DateTimeOffset? To) Range(int? maxDays = null)
    {
        if (From.HasValue != To.HasValue || From >= To || maxDays is int max && To - From > TimeSpan.FromDays(max))
            throw new FiscalInvoiceOperationException("INVALID_PERIOD", "Navesti obje granice i ispravan vremenski period.", 400);
        return (From?.ToUniversalTime(), To?.ToUniversalTime());
    }
}
public sealed class InvoiceFilter : TimeFilter
{
    public Guid? CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? OperatorId { get; set; }
    public FiscalEnvironment? Environment { get; set; }
    public FiscalStatus? Status { get; set; }
    public InvoiceType? InvoiceType { get; set; }
    public PaymentType? PaymentType { get; set; }
    public DocumentCategory? Category { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? OfficialInvoiceNumber { get; set; }
    public string? Iic { get; set; }
    public string? Jikr { get; set; }
    public string? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public InvoiceSort Sort { get; set; }
    public SortDirection Direction { get; set; } = SortDirection.Desc;
}
public sealed class ReportFilter : PageFilter
{
    public Guid? CompanyId { get; set; }
    public FiscalEnvironment? Environment { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public ReportGrouping? GroupBy { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? OperatorId { get; set; }
    public DateOnly? BucketStartDate { get; set; }
    public DocumentCategory? Category { get; set; }
    public decimal? VatRate { get; set; }
    public ReportMetric Metric { get; set; }
    public PaymentType? PaymentType { get; set; }
    public FiscalStatus? Status { get; set; }
    public OpenWorkflow? WorkflowKind { get; set; }
}
public sealed class ExchangeFilter : TimeFilter
{
    public Guid? CompanyId { get; set; }
    public Guid? InvoiceId { get; set; }
    public FiscalEnvironment? Environment { get; set; }
    public ExchangeOperation? Operation { get; set; }
    public ExchangeOutcome? Outcome { get; set; }
    public LocalCompletion? LocalCompletion { get; set; }
    public int? PuHttpStatus { get; set; }
    public string? CorrelationId { get; set; }
}
public sealed class RequestFilter : TimeFilter
{
    public RequestScope Scope { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? ClientId { get; set; }
    public string? Method { get; set; }
    public string? RouteTemplate { get; set; }
    public int? ApiHttpStatus { get; set; }
    public RequestOutcome? Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed record Coverage(DateTimeOffset? AvailableSince, bool Partial);
public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, Coverage? Coverage = null);
public sealed record AdminError(string Category, string Code, string Message, DateTimeOffset OccurredAt);
public sealed record FiscalContextSnapshot(string Environment, string Tin, string BusinessUnitCode, string TcrCode, string OperatorCode, string? SoftwareCode, string? ProfileFingerprint = null);
public sealed record AdminLabel(Guid Id, string? Code, string? Label, string Source);
public sealed record AdminBuyer(string? IdentificationType, string? IdentificationNumber, string Name);
public record AdminInvoiceRow(Guid Id, Guid CompanyId, string CompanyTin, string CompanyName, string Environment,
    string EnvironmentSource, Guid BusinessUnitId, Guid DeviceId, Guid OperatorId, AdminLabel BusinessUnit,
    AdminLabel Device, AdminLabel Operator, string InvoiceNumber, string? OfficialInvoiceNumber, DateTimeOffset IssueDateTime,
    DateTimeOffset CreatedAt, DateTimeOffset? FiscalizedAt, AdminBuyer? Buyer, string InvoiceType, DocumentCategory Category,
    string Status, string Currency, string TotalNetAmount, string TotalVatAmount, string TotalGrossAmount, string? Iic,
    string? Jikr, bool IsInformational, Guid? OriginalInvoiceId, Guid? AdvanceSettlementFinalInvoiceId,
    Guid? AdvanceRestorationStornoInvoiceId, AdminError? LastError);
public sealed record AdminExchangeRow(Guid ExchangeId, Guid? InvoiceId, Guid? CompanyId, string Environment,
    string EnvironmentSource, string Operation, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long? DurationMs,
    string CorrelationId, Guid? RequestUuid, int? PuHttpStatus, string Outcome, AdminError? Error, string LocalCompletion,
    string? ProfileFingerprint, string Provenance, string EvidenceState);
public sealed record AdminRequestRow(Guid RequestId, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long? DurationMs,
    string Method, string RouteTemplate, int ApiHttpStatus, string Outcome, AdminError? Error, string? ClientId,
    string? InitiatorId, Guid? CompanyId, Guid? InvoiceId, string CorrelationId);
public sealed record RequestAuditEvent(AdminRequestRow Row, bool Security);

public interface IAdminReadStore
{
    Task<AdminPage<AdminInvoiceRow>> InvoicesAsync(CompanyScope scope, InvoiceFilter filter, CancellationToken ct);
    Task<TurnoverReport> TurnoverAsync(CompanyScope scope, ReportFilter filter, CancellationToken ct);
    Task<AdminPage<TurnoverDocument>> ReportDocumentsAsync(CompanyScope scope, ReportFilter filter, CancellationToken ct);
    Task<AdminPage<AdminExchangeRow>> ExchangesAsync(CompanyScope scope, ExchangeFilter filter, CancellationToken ct);
    Task<AdminExchangeRow?> ExchangeAsync(CompanyScope scope, Guid id, CancellationToken ct);
    Task<AdminPage<AdminRequestRow>> RequestsAsync(CompanyScope scope, RequestFilter filter, CancellationToken ct);
    Task<InvoiceAdminDetail?> InvoiceDetailAsync(CompanyScope scope, Guid id, bool includeExchangeIds, CancellationToken ct);
    Task<AdminPage<FiscalAuditSummary>> AuditAsync(CompanyScope scope, Guid companyId, TimeFilter page, string? action,
        string? actor, string? correlationId, Guid? invoiceId, CancellationToken ct);
}
public sealed record InvoiceAdminDetail(AdminInvoiceRow Invoice, FiscalContextSnapshot? Preparation, FiscalContextSnapshot? FirstSend,
    AdminExchangeRow? LastAttempt, AdminError? LastPuError, string DiagnosticsAvailability);

public static class AdminFormatting
{
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    public static string Rate(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    public static string? Filter(string? value, int min = 1) => value is null ? null :
        value.Trim().Length >= min && value.Trim().Length <= 100 ? value.Trim() :
        throw new FiscalInvoiceOperationException("INVALID_FILTER", "Neispravna dužina tekstualnog filtera.", 400);
    public static string SafeCode(string? value, string fallback) =>
        value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? value : fallback;
    public static AdminError PuError(string? code, DateTimeOffset time) =>
        new("PuRejection", SafeCode(code, "PU_REJECTED"), "Poreska uprava je odbila fiskalni zahtjev.", time);
}
