using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Api;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Entities;
using Summa.Fiscal.Persistence.Repositories;

internal static class AdminReviewChecks
{
    public static async Task RequestJournalAsync()
    {
        const string secret = "must-not-be-stored";
        var writer = new MemoryJournal();
        await using var services = new ServiceCollection().AddSingleton<IAdminDiagnosticWriter>(writer).BuildServiceProvider();
        var options = Options.Create(new AdminDiagnosticsOptions { TrustedActorClients = ["trusted-backend"] });
        var companyId = Guid.NewGuid();
        var companyClaim = new Claim(ApiKeyAuthenticationHandler.CompanyClaim, companyId.ToString());
        var checks = new (int Status, RequestOutcome Outcome, Exception? Error)[]
        {
            (400, RequestOutcome.ValidationError, new FiscalInvoiceOperationException("INVALID_FILTER", secret, 400)),
            (401, RequestOutcome.Unauthorized, new FiscalInvoiceOperationException("UNAUTHORIZED", secret, 401)),
            (403, RequestOutcome.Forbidden, new FiscalInvoiceOperationException("COMPANY_ACCESS_DENIED", secret, 403)),
            (409, RequestOutcome.Conflict, new FiscalInvoiceOperationException("FISCAL_CONFLICT", secret, 409)),
            (500, RequestOutcome.InternalError, new InvalidOperationException(secret)),
            (500, RequestOutcome.TransportError, new TimeoutException(secret)),
            (200, RequestOutcome.FiscalRejected, null),
            (200, RequestOutcome.Succeeded, null)
        };
        foreach (var check in checks)
        {
            var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "journal-test" };
            context.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,
                check.Status == 403 ? "untrusted-backend" : "trusted-backend"), companyClaim], ApiKeyAuthenticationHandler.SchemeName));
            context.Request.Method = check.Outcome == RequestOutcome.Succeeded ? "GET" : "POST";
            context.Request.Path = "/" + secret;
            context.Request.QueryString = new("?secret=" + secret);
            context.Request.Headers[BootstrapAdminAuthorizer.ActorIdHeaderName] = check.Status == 403 ? secret : "actor-1";
            using var body = new MemoryStream();
            context.Response.Body = body;
            var middleware = new ApiExceptionMiddleware(ctx =>
            {
                if (check.Error != null) throw check.Error;
                if (check.Outcome == RequestOutcome.FiscalRejected)
                {
                    var submission = new FiscalInvoiceSubmissionResultV5(Guid.NewGuid(), Guid.NewGuid(), 200,
                        false, "1", "iic", null, null, Summa.Fiscal.Domain.Invoices.FiscalStatus.FiscalizationFailed, "PU_FAULT", secret);
                    var action = new ActionContext(ctx, new RouteData(), new ActionDescriptor());
                    new FiscalRequestResultFilter().OnResultExecuting(new ResultExecutingContext(action, [],
                        new OkObjectResult(ApiResponse<FiscalInvoiceSubmissionResultV5>.Ok(submission, ctx.TraceIdentifier)), new object()));
                }
                return Task.CompletedTask;
            }, NullLogger<ApiExceptionMiddleware>.Instance);
            var identity = new HttpFiscalAuditIdentity(new HttpContextAccessor { HttpContext = context }, options);
            var journal = new FiscalRequestJournal(middleware.InvokeAsync, TimeProvider.System, options, NullLogger<FiscalRequestJournal>.Instance);
            var count = writer.Events.Count;
            await journal.InvokeAsync(context, services, identity);
            if (check.Outcome == RequestOutcome.Succeeded)
            {
                Check(writer.Events.Count == count, "Uspješan GET ne ide u dnevnik.");
                continue;
            }
            var entry = writer.Events.Last();
            Check(writer.Events.Count == count + 1 && entry.Row.ApiHttpStatus == check.Status &&
                entry.Row.Outcome == check.Outcome.ToString() && entry.Row.CorrelationId == "journal-test", "Pogrešan metapodatak dnevnika.");
            Check(entry.Security && entry.Row.CompanyId == null &&
                (check.Status != 403 || entry.Row.InitiatorId == null), "Nedokazani tenant ili delegirani korisnik.");
            Check(!JsonSerializer.Serialize(entry).Contains(secret, StringComparison.Ordinal), "Osjetljivi podaci u dnevniku.");
        }
        new HttpContextAccessor().HttpContext = null;
        Console.WriteLine("Admin P1 dnevnik: HTTP 400/401/403/409/500, timeout, semantički HTTP 200, korelacija i redakcija.");
    }

    private sealed class MemoryJournal : IAdminDiagnosticWriter
    {
        public List<RequestAuditEvent> Events { get; } = [];
        public Task RecordRequestsAsync(IReadOnlyList<RequestAuditEvent> events, CancellationToken ct)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }
        public Task StartExchangeAsync(ExchangeStart start, CancellationToken ct) => throw new NotSupportedException();
        public Task FinishExchangeAsync(ExchangeFinish finish, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteExchangeAsync(Guid exchangeId, LocalCompletion state, CancellationToken ct) => throw new NotSupportedException();
        public Task PruneRequestsAsync(DateTimeOffset before, CancellationToken ct) => throw new NotSupportedException();
    }

    public static void AuthorizationAndCalendar()
    {
        ClaimsPrincipal Principal(string client, string[] permissions, params Guid[] companies) => new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, client) }
                .Concat(permissions.Select(x => new Claim(ApiKeyAuthenticationHandler.PermissionClaim, x)))
                .Concat(companies.Select(x => new Claim(ApiKeyAuthenticationHandler.CompanyClaim, x.ToString()))),
            ApiKeyAuthenticationHandler.SchemeName));
        var own = Guid.NewGuid(); var other = Guid.NewGuid();
        var tenant = Principal("api-test", [FiscalApiPermissions.ReportsRead], own);
        Check(AdminReadAccess.Require(tenant, null, FiscalApiPermissions.ReportsRead).Allows(own), "Nije uzet tenant claim.");
        try { AdminReadAccess.Require(tenant, other, FiscalApiPermissions.ReportsRead); throw new Exception("Tenant escape."); }
        catch (FiscalInvoiceOperationException e) when (e.StatusCode == 403) { }
        var platform = Principal("platform", [FiscalApiPermissions.PlatformAdmin]);
        Check(AdminReadAccess.Scope(platform).Allows(other) && !AdminReadAccess.HasPermission(platform, FiscalApiPermissions.ReportsRead),
            "Platform nije automatska dozvola.");
        Check(AdminReadAccess.RegisteredClient(Principal("development-client", ["*"])) is null, "Razvojni ključ nije registrovan.");
        Check((ReportPeriod.Utc(new(2026, 3, 30)) - ReportPeriod.Utc(new(2026, 3, 29))).TotalHours == 23, "Proljećni DST.");
        Check((ReportPeriod.Utc(new(2026, 10, 26)) - ReportPeriod.Utc(new(2026, 10, 25))).TotalHours == 25, "Jesenji DST.");
        Check(ReportPeriod.Utc(new(2027, 1, 1)) == new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero), "Prelaz godine.");
    }

    public static async Task PostgreSqlAsync(DbContextOptions<SummaFiscalDbContext> options)
    {
        var ct = CancellationToken.None;
        await using var db = new SummaFiscalDbContext(options);
        var company = new CompanyRecord { Tin = "73629104", LegalName = "Admin checks" };
        var another = new CompanyRecord { Tin = "73629105", LegalName = "Druga firma" };
        var unit = new BusinessUnitRecord { CompanyId = company.Id, Code = "aa111bb333", Name = "Test", Environment = "Test" };
        var device = new FiscalDeviceRecord { BusinessUnitId = unit.Id, TcrCode = "cc333dd555", InternalCode = "Admin" };
        var op = new FiscalOperatorRecord { CompanyId = company.Id, OperatorCode = "ee555ff777", Environment = "Test" };
        db.Companies.AddRange(company, another); db.BusinessUnits.Add(unit); db.FiscalDevices.Add(device); db.FiscalOperators.Add(op);
        var midnight = new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero);
        var sale = Make(company.Id, "Normal", "StornoCreated", 121, midnight.AddSeconds(-1));
        var storno = Make(company.Id, "Corrective", "Fiscalized", -121, midnight);
        storno.OriginalInvoiceId = sale.Id;
        var order = Make(company.Id, "Normal", "Fiscalized", 121, midnight.AddHours(1), "Order");
        var summary = Make(company.Id, "Summary", "Fiscalized", 121, midnight.AddHours(2));
        var orderCorrection = Make(company.Id, "Corrective", "Fiscalized", -121, midnight.AddHours(3), "Order");
        orderCorrection.OriginalInvoiceId = order.Id; orderCorrection.IsOrderCorrection = true;
        db.FiscalSummaryOrders.Add(new() { OrderInvoiceId = order.Id, SummaryInvoiceId = summary.Id });
        var advance = Make(company.Id, "Advance", "Fiscalized", 242, midnight.AddHours(4));
        var final = Make(company.Id, "Normal", "Fiscalized", 242, midnight.AddHours(5), "Advance");
        db.FiscalAdvanceUsages.Add(new() { AdvanceInvoiceId = advance.Id, AdvanceIic = advance.Iic!, FinalInvoiceId = final.Id, UsedAmount = 242 });
        var pending = Make(company.Id, "Normal", "FiscalizationPending", 121, midnight.AddHours(6));
        pending.Jikr = null;
        var unknown = Make(company.Id, "Normal", "Fiscalized", 121, midnight.AddHours(7));
        unknown.AdminEnvironment = null; unknown.AdminEnvironmentSource = null;
        var foreign = Make(another.Id, "Normal", "Fiscalized", 121, midnight);
        var label = company.Id;
        db.FiscalAudits.Add(new() { CompanyId = label, Action = "TEST", Actor = "test", CorrelationId = "admin-it",
            DataJson = JsonSerializer.Serialize(new { InvoiceId = sale.Id }) });
        await db.SaveChangesAsync(ct);
        var reader = new PostgreSqlAdminReadStore(db, TimeProvider.System);
        var scope = new CompanyScope(false, [company.Id]);
        var listed = await reader.InvoicesAsync(scope, new() { PageSize = 1, BuyerName = "a%b", PaymentType = Summa.Fiscal.Domain.Invoices.PaymentType.BankAccount }, ct);
        Check(listed.TotalCount == 6 && listed.Items.Count == 1, "Tenant/stranica/literalni procenat.");
        Check(await reader.InvoiceDetailAsync(scope, foreign.Id, false, ct) is null, "Tuđi detalj vidljiv.");
        var reportFilter = new ReportFilter { CompanyId = company.Id, Environment = FiscalEnvironment.Test, GroupBy = ReportGrouping.Month,
            FromDate = new(2026, 10, 1), ToDate = new(2026, 12, 1), PageSize = 1 };
        var report = await reader.TurnoverAsync(scope, reportFilter, ct);
        Check(report.Totals.DocumentCount == 7 && report.Totals.SalesNet.Gross == "242.00" &&
              report.Groups[0].SalesNet.Gross == "121.00" && report.Groups[1].SalesNet.Gross == "121.00",
            $"Sumarni, storno i avans ne smiju duplirati promet. Broj={report.Totals.DocumentCount}, neto={report.Totals.SalesNet.Gross}, " +
            $"grupe={string.Join(',', report.Groups.Select(x => $"{x.BucketStartDate:yyyy-MM-dd}:{x.SalesNet.Gross}"))}.");
        Check(report.Totals.VatByRate.Single().Vat == "42.00" && report.Controls.StatusCounts["FiscalizationPending"] == 1 &&
            report.Controls.OpenWorkflows.Any(x => x.Kind == OpenWorkflow.AdvanceSettlementDue && x.Amount == "242.00") &&
            report.Controls.OpenWorkflows.Any(x => x.Kind == OpenWorkflow.SummaryCorrectionDue && x.Amount == "121.00"), "PDV/status/dug procesa.");
        reportFilter.Metric = ReportMetric.SalesNet; reportFilter.VatRate = 21;
        var drill = await reader.ReportDocumentsAsync(scope, reportFilter, ct);
        Check(drill.TotalCount == 5 && drill.Items.Count == 1 && drill.Items[0].Contribution.Gross == "242.00", "Drill-down mora odgovarati selekciji prometa.");
        reportFilter.VatRate = null; reportFilter.Metric = ReportMetric.OpenWorkflow; reportFilter.WorkflowKind = OpenWorkflow.AdvanceSettlementDue;
        var debt = await reader.ReportDocumentsAsync(scope, reportFilter, ct);
        Check(debt.Items.Single().Contribution.Amount == "242.00", "Drill-down mora vratiti dug avansa.");
        var audit = await reader.AuditAsync(scope, company.Id, new(), null, null, "admin-it", sale.Id, ct);
        Check(audit.Items.Single().InvoiceId == sale.Id, "Audit nije povezao tipizovan legacy JSON.");

        var writer = new PostgreSqlAdminDiagnosticWriter(new TestFactory(options));
        var id = Guid.NewGuid();
        await writer.StartExchangeAsync(new(id, Guid.NewGuid(), "admin-it", ExchangeOperation.RegisterInvoice, "https://efitest.tax.gov.me/fs-v1",
            PuFiscalContractV5.RegisterInvoiceSoapAction, new string('A', 64), midnight, new(company.Id, pending.Id,
                new("Test", company.Tin, unit.Code, device.TcrCode!, op.OperatorCode, "gg777hh999"))), ct);
        await writer.FinishExchangeAsync(new(id, ExchangeOutcome.Rejected, midnight.AddSeconds(2), 200, new string('B', 64), null, "42"), ct);
        var detail = await reader.InvoiceDetailAsync(scope, pending.Id, true, ct);
        Check(detail?.LastPuError?.Code == "42" && detail.LastAttempt?.PuHttpStatus == 200 &&
            detail.LastAttempt.ExchangeId == id && detail.FirstSend?.Environment == "Test", "PU 200 sa odbijanjem nije uspjeh.");
        Check(await reader.ExchangeAsync(new(false, [another.Id]), id, ct) is null, "Tuđa razmjena vidljiva.");
        var attempts = await reader.ExchangesAsync(scope, new() { InvoiceId = pending.Id, CorrelationId = "admin-it" }, ct);
        Check(attempts.Items.Single().ExchangeId == id, "Pretraga razmjene.");
        var entry = new AdminRequestRow(Guid.NewGuid(), midnight, midnight, 0, "POST", "api/v1/fiscal/invoices/{id:guid}/fiscalize", 200,
            "FiscalRejected", AdminFormatting.PuError("42", midnight), "test", null, company.Id, pending.Id, "admin-it");
        var denied = entry with { RequestId = Guid.NewGuid(), ApiHttpStatus = 403, CompanyId = null, InvoiceId = null, Outcome = "Forbidden" };
        await writer.RecordRequestsAsync([new(entry, false), new(denied, true), new(entry, false)], ct);
        var requests = await reader.RequestsAsync(scope, new() { CorrelationId = "admin-it" }, ct);
        Check(requests.TotalCount == 1 && requests.Items[0].RequestId == entry.RequestId, "Dnevnik je duplirao ili otkrio security zapis.");
        await BackfillAsync(new TestFactory(options), db.Database.GetDbConnection().Database, unknown, company, unit, device, op);
        var restored = await reader.InvoiceDetailAsync(scope, unknown.Id, true, ct);
        Check(restored?.Invoice.EnvironmentSource == "VerifiedExchange" && restored.LastAttempt?.Outcome == "Accepted" &&
            restored.FirstSend?.SoftwareCode == "gh777hh999", "Backfill nije obnovio hronološki prvi pouzdani kontekst.");
        Console.WriteLine("Admin P1 PostgreSQL: tenant, paginacija, promet/PDV, drill-down, pokušaji, dnevnik, audit i ponovljivi backfill.");

        FiscalInvoiceRecord Make(Guid owner, string type, string status, decimal gross, DateTimeOffset issue, string method = "BankAccount")
        {
            var net = decimal.Round(gross / 1.21m, 2);
            var doc = new FiscalInvoiceRecord
            {
                CompanyId = owner, BusinessUnitId = unit.Id, DeviceId = device.Id, OperatorId = op.Id, InvoiceType = type, Status = status,
                InvoiceOrdinalNumber = 9000 + db.ChangeTracker.Entries<FiscalInvoiceRecord>().Count(), IdempotencyKey = Guid.NewGuid().ToString(),
                AdminEnvironment = "Test", AdminEnvironmentSource = "VerifiedExchange", IssueDateTime = issue, FiscalizedAt = issue,
                Jikr = Guid.NewGuid().ToString(), Iic = Guid.NewGuid().ToString("N"), NetAmount = net, VatAmount = gross - net, TotalAmount = gross,
                BuyerName = "Cafe A%B"
            };
            doc.InvoiceNumber = doc.InvoiceOrdinalNumber.ToString();
            doc.OfficialInvoiceNumber = $"{unit.Code}/{doc.InvoiceNumber}/2026/{device.TcrCode}";
            if (type != "Summary") doc.Items.Add(new() { Name = "Stavka", LineNumber = 1, VatRate = 21, NetAmount = net, VatAmount = gross - net, TotalAmount = gross });
            doc.Payments.Add(new() { PaymentType = method, Amount = gross });
            db.FiscalInvoices.Add(doc);
            return doc;
        }
    }

    private static async Task BackfillAsync(TestFactory factory, string database, FiscalInvoiceRecord invoice,
        CompanyRecord company, BusinessUnitRecord unit, FiscalDeviceRecord device, FiscalOperatorRecord op)
    {
        var root = Directory.CreateTempSubdirectory("summa-admin-review-").FullName;
        try
        {
            var files = new FileFiscalExchangeStoreV5(new() { RootPath = root });
            var uuid = Guid.NewGuid();
            var xml = $"""<RegisterInvoiceRequest xmlns="https://efi.tax.gov.me/fs/schema"><Header UUID="{uuid:D}" /><Invoice IIC="{invoice.Iic}" InvNum="{invoice.OfficialInvoiceNumber}" IssueDateTime="{invoice.IssueDateTime:O}" TotPrice="121.00" BusinUnitCode="{unit.Code}" TCRCode="{device.TcrCode}" OperatorCode="{op.OperatorCode}" SoftCode="gg777hh999"><Seller IDNum="{company.Tin}" /></Invoice></RegisterInvoiceRequest>""";
            var endpoint = new Uri("https://efitest.tax.gov.me/fs-v1");
            var exchange = await files.SaveRequestAsync(xml, uuid, "backfill-it", endpoint, PuFiscalContractV5.RegisterInvoiceSoapAction, default);
            await files.SaveResponseAsync(exchange, $"""<RegisterInvoiceResponse xmlns="https://efi.tax.gov.me/fs/schema"><Header RequestUUID="{uuid:D}" /><FIC>{invoice.Jikr}</FIC></RegisterInvoiceResponse>""", 200, default);
            var corrupt = await files.SaveRequestAsync(xml, uuid, "bad", endpoint, PuFiscalContractV5.RegisterInvoiceSoapAction, default);
            await File.AppendAllTextAsync(Path.Combine(root, corrupt.ToString("N"), "request.xml"), "invalid");
            var backfill = new AdminExchangeBackfill(factory);
            var preview = await backfill.RunAsync(root, false, null, null, null, default);
            Check(preview.Inserted == 1 && preview.Conflict == 1, "Dry-run nije razlikovao valjanu i izmijenjenu razmjenu.");
            await using var fresh = factory.CreateDbContext();
            Check(!await fresh.FiscalExchanges.AnyAsync(x => x.Id == exchange), "Dry-run je zapisao razmjenu.");
            string? checkpoint = null;
            var imported = await backfill.RunAsync(root, true, database, null, c => checkpoint = c, default);
            var resumed = await backfill.RunAsync(root, true, database, checkpoint, null, default);
            var replayed = await backfill.RunAsync(root, true, database, null, null, default);
            Check(imported.Inserted == 1 && resumed.Scanned == 0 && replayed.Inserted == 0 && replayed.Skipped == 1 &&
                await File.ReadAllTextAsync(Path.Combine(root, exchange.ToString("N"), "request.xml")) == xml, "Backfill nije ponovljiv ili mijenja dokaz.");
            var older = await files.SaveRequestAsync(xml.Replace("gg777hh999", "gh777hh999", StringComparison.Ordinal),
                uuid, "backfill-older", endpoint, PuFiscalContractV5.RegisterInvoiceSoapAction, default);
            var metadataPath = Path.Combine(root, older.ToString("N"), "request.metadata.json");
            var metadata = JsonSerializer.Deserialize<FiscalExchangeRequestV5>(await File.ReadAllTextAsync(metadataPath))!;
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata with { CreatedAt = metadata.CreatedAt.AddDays(-1) }));
            var olderImport = await backfill.RunAsync(root, true, database, null, null, default);
            Check(olderImport.Inserted == 1 && olderImport.Incomplete == 1, "Naknadno nađeni stariji zahtjev nije uvezen.");
        }
        finally
        {
            if (Path.GetFileName(root).StartsWith("summa-admin-review-", StringComparison.Ordinal)) Directory.Delete(root, true);
        }
    }
    private sealed class TestFactory(DbContextOptions<SummaFiscalDbContext> options) : IDbContextFactory<SummaFiscalDbContext>
    {
        public SummaFiscalDbContext CreateDbContext() => new(options);
    }
    private static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
