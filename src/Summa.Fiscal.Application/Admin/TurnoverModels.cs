using Summa.Fiscal.Application.Invoices;

namespace Summa.Fiscal.Application.Admin;

public sealed record ReportPeriod(Guid CompanyId, FiscalEnvironment Environment, DateOnly FromDate, DateOnly ToDate,
    ReportGrouping GroupBy, DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? BusinessUnitId, Guid? DeviceId, Guid? OperatorId)
{
    public const string ZoneName = "Europe/Podgorica";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneName);

    public static ReportPeriod From(ReportFilter f)
    {
        if (f.CompanyId is not Guid id || id == Guid.Empty || f.Environment is not FiscalEnvironment env ||
            f.GroupBy is not ReportGrouping grouping || f.FromDate is not DateOnly from || f.ToDate is not DateOnly to ||
            to.DayNumber - from.DayNumber is < 1 or > 3660 || from.Year < 2 || to.Year > 9998)
            throw new FiscalInvoiceOperationException("INVALID_REPORT_FILTER", "Firma, okruženje, grupisanje i period do 3660 dana su obavezni.", 400);
        return new(id, env, from, to, grouping, Utc(from), Utc(to), f.BusinessUnitId, f.DeviceId, f.OperatorId);
    }

    public static DateTimeOffset Utc(DateOnly day) => new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Zone));
    public static DateOnly Bucket(DateOnly day, ReportGrouping grouping) => grouping switch
    {
        ReportGrouping.Year => new(day.Year, 1, 1),
        ReportGrouping.Month => new(day.Year, day.Month, 1),
        _ => day
    };
}
public sealed record ReportAmounts(string Net, string Vat, string Gross)
{
    public static ReportAmounts From(decimal net, decimal vat, decimal gross) =>
        new(AdminFormatting.Money(net), AdminFormatting.Money(vat), AdminFormatting.Money(gross));
}
public sealed record ReportVat(string Rate, string Net, string Vat, string Gross);
public sealed record ReportCategory(DocumentCategory Category, int Count, ReportAmounts Amounts);
public sealed record ReportPayment(DocumentCategory Category, string PaymentType, int Count, string Amount);
public sealed record ReportTotals(int DocumentCount, int SaleInvoiceCount, ReportAmounts SalesPositive,
    ReportAmounts SalesCorrections, ReportAmounts SalesNet, IReadOnlyList<ReportVat> VatByRate,
    IReadOnlyList<ReportCategory> Categories, IReadOnlyList<ReportPayment> Payments, DateOnly? BucketStartDate = null);
public sealed record WorkflowTotal(OpenWorkflow Kind, int Count, string Amount);
public sealed record ReportControls(IReadOnlyDictionary<string, int> StatusCounts, IReadOnlyList<WorkflowTotal> OpenWorkflows);
public sealed record TurnoverReport(ReportPeriod Filters, string TimeZone, string RulesVersion, string Currency,
    bool Authoritative, DateTimeOffset EvaluatedAt, ReportTotals Totals, IReadOnlyList<ReportTotals> Groups,
    ReportControls Controls, Coverage Coverage);
public sealed record ReportContribution(string? Net, string? Vat, string? Gross, string? Amount);
public sealed record TurnoverDocument : AdminInvoiceRow
{
    public TurnoverDocument(AdminInvoiceRow invoice, ReportContribution contribution) : base(invoice) => Contribution = contribution;
    public ReportContribution Contribution { get; }
}
