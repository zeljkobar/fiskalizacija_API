using Microsoft.AspNetCore.Mvc;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Globalization;

namespace Summa.Fiscal.Api.Controllers;

[ApiController, Route("api/v1/admin"), RequireRegisteredAdminClient]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminFiscalReadController(IServiceProvider services) : ControllerBase
{
    private IAdminReadStore Store => services.GetService<IAdminReadStore>() ??
        throw new FiscalInvoiceOperationException("ADMIN_INDEX_UNAVAILABLE", "Administratorski indeks nije dostupan.", 503);

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices([FromQuery] InvoiceFilter f, CancellationToken ct)
    {
        var scope = AdminReadAccess.Require(User, f.CompanyId, FiscalApiPermissions.InvoicesRead);
        ValidateRangeSyntax();
        return Result(await Store.InvoicesAsync(scope, f, ct));
    }
    [HttpGet("reports/turnover")]
    public async Task<IActionResult> Turnover([FromQuery] ReportFilter f, CancellationToken ct)
    {
        var scope = AdminReadAccess.Require(User, f.CompanyId, FiscalApiPermissions.ReportsRead);
        return Result(await Store.TurnoverAsync(scope, f, ct));
    }
    [HttpGet("reports/turnover/documents")]
    public async Task<IActionResult> TurnoverDocuments([FromQuery] ReportFilter f, CancellationToken ct)
    {
        var scope = AdminReadAccess.Require(User, f.CompanyId, FiscalApiPermissions.ReportsRead, FiscalApiPermissions.InvoicesRead);
        return Result(await Store.ReportDocumentsAsync(scope, f, ct));
    }
    [HttpGet("exchanges")]
    public async Task<IActionResult> Exchanges([FromQuery] ExchangeFilter f, CancellationToken ct)
    {
        var scope = AdminReadAccess.Require(User, f.CompanyId, FiscalApiPermissions.ExchangesRead);
        ValidateRangeSyntax();
        return Result(await Store.ExchangesAsync(scope, f, ct));
    }
    [HttpGet("exchanges/{exchangeId:guid}")]
    public async Task<IActionResult> Exchange(Guid exchangeId, CancellationToken ct)
    {
        var scope = AdminReadAccess.Require(User, null, FiscalApiPermissions.ExchangesRead);
        return Result(await Store.ExchangeAsync(scope, exchangeId, ct) ?? throw Missing());
    }
    [HttpGet("invoices/{invoiceId:guid}/exchanges")]
    public async Task<IActionResult> InvoiceExchanges(Guid invoiceId, [FromQuery] ExchangeFilter f, CancellationToken ct)
    {
        var scope = AdminReadAccess.Require(User, null, FiscalApiPermissions.ExchangesRead, FiscalApiPermissions.InvoicesRead);
        ValidateRangeSyntax();
        if (f.InvoiceId != null || f.CompanyId != null)
            throw new FiscalInvoiceOperationException("INVALID_FILTER", "Firma i račun određeni su putanjom.", 400);
        var detail = await Store.InvoiceDetailAsync(scope, invoiceId, false, ct) ?? throw Missing();
        f.InvoiceId = invoiceId;
        f.CompanyId = detail.Invoice.CompanyId;
        return Result(await Store.ExchangesAsync(scope, f, ct));
    }
    [HttpGet("api-requests")]
    public async Task<IActionResult> Requests([FromQuery] RequestFilter f, CancellationToken ct)
    {
        var scope = f.Scope == RequestScope.Security
            ? AdminReadAccess.Require(User, null, FiscalApiPermissions.RequestsRead, FiscalApiPermissions.RequestsSecurityRead, FiscalApiPermissions.PlatformAdmin)
            : AdminReadAccess.Require(User, f.CompanyId, FiscalApiPermissions.RequestsRead);
        if (f.Scope == RequestScope.Security && f.CompanyId.HasValue)
            throw new FiscalInvoiceOperationException("INVALID_FILTER", "Sigurnosni pregled ne prihvata companyId.", 400);
        ValidateRangeSyntax();
        return Result(await Store.RequestsAsync(scope, f, ct));
    }
    private IActionResult Result<T>(T value) => Ok(ApiResponse<T>.Ok(value, HttpContext.TraceIdentifier));
    private static FiscalInvoiceOperationException Missing() => new("ADMIN_RESOURCE_NOT_FOUND", "Traženi zapis nije pronađen.", 404);
    private void ValidateRangeSyntax()
    {
        foreach (var name in new[] { "from", "to" })
        {
            if (!Request.Query.TryGetValue(name, out var values)) continue;
            const DateTimeStyles style = DateTimeStyles.AssumeUniversal;
            var value = values.Count == 1 ? values[0] : null;
            if (value == null || !DateTimeOffset.TryParseExact(value,
                ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, style, out _) ||
                !(value.EndsWith('Z') || value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':'))
                throw new FiscalInvoiceOperationException("INVALID_PERIOD", "Vrijeme mora biti ISO 8601 sa vremenskom zonom.", 400);
        }
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireRegisteredAdminClientAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        AdminReadAccess.Require(context.HttpContext.User, null);
        context.HttpContext.Response.Headers.CacheControl = "no-store";
    }
}
