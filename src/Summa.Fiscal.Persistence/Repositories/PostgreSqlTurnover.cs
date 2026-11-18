using System.Data;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed partial class PostgreSqlAdminReadStore
{
    private static readonly DocumentCategory[] Sales = [DocumentCategory.StandardSale, DocumentCategory.FinalWithAdvance, DocumentCategory.OrderSale];
    private static readonly DocumentCategory[] Corrections = [DocumentCategory.SaleCorrection, DocumentCategory.OrderCorrection];
    private static readonly DocumentCategory[] NetSales = [..Sales, ..Corrections];

    private IQueryable<Classified> ReportQuery(CompanyScope scope, ReportPeriod period, DateOnly? bucket = null)
    {
        var from = period.FromUtc; var to = period.ToUtc;
        if (bucket is { } day)
        {
            if (day != ReportPeriod.Bucket(day, period.GroupBy) || day.Year is < 2 or > 9998)
                throw new FiscalInvoiceOperationException("INVALID_REPORT_BUCKET", "Granica grupe nije ispravna.", 400);
            var next = period.GroupBy switch { ReportGrouping.Year => day.AddYears(1), ReportGrouping.Month => day.AddMonths(1), _ => day.AddDays(1) };
            var start = ReportPeriod.Utc(day); var end = ReportPeriod.Utc(next);
            if (start > from) from = start;
            if (end < to) to = end;
        }
        return Classify(InvoiceQuery(scope, period.CompanyId, period.BusinessUnitId, period.DeviceId, period.OperatorId, period.Environment, from, to));
    }
    private static IQueryable<Classified> Confirmed(IQueryable<Classified> q) =>
        q.Where(x => x.Invoice.Jikr != null && x.Invoice.Jikr != "" && (x.Invoice.Status == "Fiscalized" || x.Invoice.Status == "StornoCreated"));

    public async Task<TurnoverReport> TurnoverAsync(CompanyScope scope, ReportFilter filter, CancellationToken ct)
    {
        var p = ReportPeriod.From(filter);
        var query = ReportQuery(scope, p);
        var confirmed = Confirmed(query);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var evaluatedAt = clock.GetUtcNow();
        var summaries = await confirmed.GroupBy(x => new
        {
            Day = DateOnly.FromDateTime(SummaFiscalDbContext.LocalTime(ReportPeriod.ZoneName, x.Invoice.IssueDateTime)),
            x.Category
        }).Select(g => new
        {
            g.Key.Day, g.Key.Category, Count = g.Count(), PositiveCount = g.Count(x => x.Invoice.TotalAmount > 0),
            Net = g.Sum(x => x.Invoice.NetAmount), Vat = g.Sum(x => x.Invoice.VatAmount), Gross = g.Sum(x => x.Invoice.TotalAmount)
        }).ToArrayAsync(ct);
        var vat = await (from x in confirmed
                         join item in db.FiscalInvoiceItems on x.Invoice.Id equals item.InvoiceId
                         where NetSales.Contains(x.Category)
                         group item by new {
                             Day = DateOnly.FromDateTime(SummaFiscalDbContext.LocalTime(ReportPeriod.ZoneName, x.Invoice.IssueDateTime)),
                             item.VatRate
                         } into g
                         select new { g.Key.Day, g.Key.VatRate, Net = g.Sum(x => x.NetAmount),
                             Vat = g.Sum(x => x.VatAmount), Gross = g.Sum(x => x.TotalAmount) }).ToArrayAsync(ct);
        var payments = await (from x in confirmed
                              join payment in db.FiscalPayments on x.Invoice.Id equals payment.InvoiceId
                              group payment by new {
                                  Day = DateOnly.FromDateTime(SummaFiscalDbContext.LocalTime(ReportPeriod.ZoneName, x.Invoice.IssueDateTime)),
                                  x.Category, payment.PaymentType
                              } into g select new { g.Key.Day, g.Key.Category, g.Key.PaymentType,
                                  Count = g.Select(x => x.InvoiceId).Distinct().Count(), Amount = g.Sum(x => x.Amount) }).ToArrayAsync(ct);
        var status = await query.GroupBy(x => x.Invoice.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToArrayAsync(ct);
        var debts = await (from x in confirmed
                           join debt in db.AdminWorkflowDebts on x.Invoice.Id equals debt.InvoiceId
                           group debt by debt.Kind into g select new { Kind = g.Key, Count = g.Count(), Amount = g.Sum(x => x.Amount) }).ToArrayAsync(ct);
        var since = await db.FiscalExchanges.Where(x => x.CompanyId == p.CompanyId && x.Provenance == "Live")
            .MinAsync(x => (DateTimeOffset?)x.CreatedAt, ct);
        await tx.CommitAsync(ct);

        var byBucket = summaries.ToLookup(x => ReportPeriod.Bucket(x.Day, p.GroupBy));
        var vatByBucket = vat.ToLookup(x => ReportPeriod.Bucket(x.Day, p.GroupBy));
        var paymentsByBucket = payments.ToLookup(x => ReportPeriod.Bucket(x.Day, p.GroupBy));
        ReportTotals Totals(DateOnly? bucket)
        {
            var rows = bucket is { } key ? byBucket[key].ToArray() : summaries;
            var taxRows = bucket is { } taxKey ? vatByBucket[taxKey] : vat;
            var paymentRows = bucket is { } paymentKey ? paymentsByBucket[paymentKey] : payments;
            var positive = rows.Where(x => Sales.Contains(x.Category)).ToArray();
            var correction = rows.Where(x => Corrections.Contains(x.Category)).ToArray();
            var net = positive.Concat(correction).ToArray();
            return new(rows.Sum(x => x.Count), positive.Sum(x => x.PositiveCount),
                ReportAmounts.From(positive.Sum(x => x.Net), positive.Sum(x => x.Vat), positive.Sum(x => x.Gross)),
                ReportAmounts.From(correction.Sum(x => x.Net), correction.Sum(x => x.Vat), correction.Sum(x => x.Gross)),
                ReportAmounts.From(net.Sum(x => x.Net), net.Sum(x => x.Vat), net.Sum(x => x.Gross)),
                taxRows.GroupBy(x => x.VatRate).OrderBy(g => g.Key)
                    .Select(g => new ReportVat(AdminFormatting.Rate(g.Key), AdminFormatting.Money(g.Sum(x => x.Net)),
                        AdminFormatting.Money(g.Sum(x => x.Vat)), AdminFormatting.Money(g.Sum(x => x.Gross)))).ToArray(),
                rows.GroupBy(x => x.Category).OrderBy(g => g.Key).Select(g => new ReportCategory(g.Key, g.Sum(x => x.Count),
                    ReportAmounts.From(g.Sum(x => x.Net), g.Sum(x => x.Vat), g.Sum(x => x.Gross)))).ToArray(),
                paymentRows.GroupBy(x => (x.Category, x.PaymentType)).OrderBy(g => g.Key.Category).ThenBy(g => g.Key.PaymentType)
                    .Select(g => new ReportPayment(g.Key.Category, g.Key.PaymentType, g.Sum(x => x.Count), AdminFormatting.Money(g.Sum(x => x.Amount)))).ToArray(), bucket);
        }
        var groups = byBucket.Select(x => x.Key).Order().Select(x => Totals(x)).ToArray();
        return new(p, ReportPeriod.ZoneName, "turnover-v1", "EUR", p.Environment != FiscalEnvironment.Unknown, evaluatedAt,
            Totals(null), groups, new(status.ToDictionary(x => x.Status, x => x.Count),
                debts.Select(x => new WorkflowTotal(Enum.Parse<OpenWorkflow>(x.Kind), x.Count, AdminFormatting.Money(x.Amount))).ToArray()), new(since, true));
    }

    public async Task<AdminPage<TurnoverDocument>> ReportDocumentsAsync(CompanyScope scope, ReportFilter f, CancellationToken ct)
    {
        var offset = f.Offset();
        var p = ReportPeriod.From(f);
        if ((f.Metric == ReportMetric.Payment) != f.PaymentType.HasValue ||
            (f.Metric == ReportMetric.Status) != f.Status.HasValue ||
            (f.Metric == ReportMetric.OpenWorkflow) != f.WorkflowKind.HasValue ||
            f.Metric is ReportMetric.Category or ReportMetric.Payment && !f.Category.HasValue ||
            f.VatRate.HasValue && (f.VatRate < 0 || f.VatRate > 100 || decimal.Round(f.VatRate.Value, 4) != f.VatRate ||
                f.Metric is not (ReportMetric.SalesPositive or ReportMetric.SalesCorrections or ReportMetric.SalesNet)))
            throw new FiscalInvoiceOperationException("INVALID_REPORT_METRIC", "Filteri ne odgovaraju izabranom redu izvještaja.", 400);
        var q = ReportQuery(scope, p, f.BucketStartDate);
        if (f.Metric != ReportMetric.Status) q = Confirmed(q);
        var category = f.Category;
        if (category.HasValue) q = q.Where(x => x.Category == category);
        if (f.Metric is ReportMetric.SalesPositive or ReportMetric.SaleInvoiceCount) q = q.Where(x => Sales.Contains(x.Category));
        if (f.Metric == ReportMetric.SaleInvoiceCount) q = q.Where(x => x.Invoice.TotalAmount > 0);
        if (f.Metric == ReportMetric.SalesCorrections) q = q.Where(x => Corrections.Contains(x.Category));
        if (f.Metric == ReportMetric.SalesNet) q = q.Where(x => NetSales.Contains(x.Category));
        var rate = f.VatRate;
        if (rate.HasValue) q = q.Where(x => x.Invoice.Items.Any(i => i.VatRate == rate));
        var payment = f.PaymentType?.ToString();
        if (payment != null) q = q.Where(x => x.Invoice.Payments.Any(i => i.PaymentType == payment));
        if (f.Status is { } status) q = q.Where(x => x.Invoice.Status == status.ToString());
        var kind = f.WorkflowKind?.ToString();
        if (kind != null) q = q.Where(x => db.AdminWorkflowDebts.Any(d => d.InvoiceId == x.Invoice.Id && d.Kind == kind));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var count = await q.CountAsync(ct);
        var rows = await Project(q.OrderByDescending(x => x.Invoice.IssueDateTime).ThenByDescending(x => x.Invoice.Id)
            .Skip(offset).Take(f.PageSize)).ToArrayAsync(ct);
        var mapped = await MapRows(rows, ct);
        var ids = mapped.Select(x => x.Id).ToArray();
        var byRate = rate.HasValue ? await db.FiscalInvoiceItems.Where(x => ids.Contains(x.InvoiceId) && x.VatRate == rate)
            .GroupBy(x => x.InvoiceId).Select(g => new { Id = g.Key, Net = g.Sum(x => x.NetAmount), Vat = g.Sum(x => x.VatAmount), Gross = g.Sum(x => x.TotalAmount) })
            .ToDictionaryAsync(x => x.Id, ct) : null;
        var paymentAmounts = payment != null ? await db.FiscalPayments.Where(x => ids.Contains(x.InvoiceId) && x.PaymentType == payment)
            .GroupBy(x => x.InvoiceId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct) : null;
        var workflowAmounts = kind != null ? await db.AdminWorkflowDebts.Where(x => ids.Contains(x.InvoiceId) && x.Kind == kind)
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Amount, ct) : null;
        var result = mapped.Select(x => new TurnoverDocument(x, Contribution(x))).ToArray();
        await tx.CommitAsync(ct);
        return new(result, f.Page, f.PageSize, count);

        ReportContribution Contribution(AdminInvoiceRow x)
        {
            if (byRate?.GetValueOrDefault(x.Id) is { } v) return new(AdminFormatting.Money(v.Net), AdminFormatting.Money(v.Vat), AdminFormatting.Money(v.Gross), null);
            if (paymentAmounts != null) return new(null, null, null, AdminFormatting.Money(paymentAmounts[x.Id]));
            if (workflowAmounts != null) return new(null, null, null, AdminFormatting.Money(workflowAmounts[x.Id]));
            return f.Metric is ReportMetric.Status or ReportMetric.DocumentCount or ReportMetric.SaleInvoiceCount
                ? new(null, null, null, null) : new(x.TotalNetAmount, x.TotalVatAmount, x.TotalGrossAmount, null);
        }
    }
}
