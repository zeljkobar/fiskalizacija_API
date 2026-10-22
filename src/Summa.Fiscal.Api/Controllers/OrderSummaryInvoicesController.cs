using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Api.Controllers;

public sealed record CreateSummaryRequest(Guid CompanyId, Guid DeviceId, Guid OperatorId,
    IReadOnlyCollection<Guid>? OrderInvoiceIds, DateTimeOffset IssueDateTime,
    IReadOnlyCollection<CreateFiscalPayment>? Payments, string Confirmation);
public sealed record CreateOrderCorrectionRequest(DateTimeOffset IssueDateTime, string Reason,
    IReadOnlyCollection<OrderCorrectionAllocation>? Allocations, string Confirmation);
public sealed record CreateSummaryCorrectionRequest(DateTimeOffset IssueDateTime, string Reason,
    IReadOnlyCollection<CreateFiscalPayment>? Payments, string Confirmation);

[ApiController, Authorize, Route("api/v1/fiscal/invoices")]
public sealed class OrderSummaryInvoicesController(IFiscalInvoiceApplicationService application,
    IFiscalAccessAuthorizer authorizer) : ControllerBase
{
    [HttpPost("summaries")]
    public async Task<IActionResult> CreateSummary(CreateSummaryRequest request, CancellationToken ct)
    {
        var error = MutationError(request.CompanyId, FiscalApiPermissions.InvoicesCreate, request.Confirmation, $"CREATE_SUMMARY:{request.CompanyId:D}");
        if (error is not null) return error;
        var result = await application.CreateSummaryAsync(new(request.CompanyId, request.DeviceId, request.OperatorId,
            request.OrderInvoiceIds ?? [], request.IssueDateTime, request.Payments ?? [], Key(), Correlation(), Actor()), ct);
        return Accepted($"/api/v1/fiscal/invoices/{result.Id:D}", ApiResponse<FiscalInvoiceResult>.Ok(result, Correlation()));
    }

    [HttpPost("{id:guid}/order-corrections")]
    public async Task<IActionResult> CorrectOrder(Guid id, CreateOrderCorrectionRequest request, CancellationToken ct)
    {
        var invoice = await application.GetAsync(id, ct);
        if (invoice is null) return Missing();
        var error = MutationError(invoice.CompanyId, FiscalApiPermissions.InvoicesStorno, request.Confirmation, $"CREATE_ORDER_CORRECTION:{id:D}");
        if (error is not null) return error;
        var result = await application.CreateOrderCorrectionAsync(new(id, request.IssueDateTime, request.Reason,
            request.Allocations ?? [], Key(), Correlation(), Actor()), ct);
        return Accepted($"/api/v1/fiscal/invoices/{result.Id:D}", ApiResponse<FiscalInvoiceResult>.Ok(result, Correlation()));
    }

    [HttpPost("{id:guid}/summary-corrections")]
    public async Task<IActionResult> CorrectSummary(Guid id, CreateSummaryCorrectionRequest request, CancellationToken ct)
    {
        var invoice = await application.GetAsync(id, ct);
        if (invoice is null) return Missing();
        var error = MutationError(invoice.CompanyId, FiscalApiPermissions.InvoicesStorno, request.Confirmation, $"CREATE_SUMMARY_CORRECTION:{id:D}");
        if (error is not null) return error;
        var result = await application.CreateSummaryCorrectionAsync(new(id, request.IssueDateTime, request.Reason,
            request.Payments ?? [], Key(), Correlation(), Actor()), ct);
        return Accepted($"/api/v1/fiscal/invoices/{result.Id:D}", ApiResponse<FiscalInvoiceResult>.Ok(result, Correlation()));
    }

    [HttpPost("{id:guid}/summary-cancellations")]
    public async Task<IActionResult> CancelSummary(Guid id, CreateSummaryCorrectionRequest request, CancellationToken ct)
    {
        var invoice = await application.GetAsync(id, ct);
        if (invoice is null) return Missing();
        var error = MutationError(invoice.CompanyId, FiscalApiPermissions.InvoicesStorno, request.Confirmation, $"CANCEL_SUMMARY:{id:D}");
        if (error is not null) return error;
        var result = await application.CreateSummaryCancellationAsync(new(id, request.IssueDateTime, request.Reason,
            request.Payments ?? [], Key(), Correlation(), Actor()), ct);
        return Accepted($"/api/v1/fiscal/invoices/{result.Id:D}", ApiResponse<FiscalInvoiceResult>.Ok(result, Correlation()));
    }

    [HttpGet("{id:guid}/order-workflow")]
    public async Task<IActionResult> OrderWorkflow(Guid id, CancellationToken ct)
    {
        var invoice = await application.GetAsync(id, ct);
        if (invoice is null) return Missing();
        if (!authorizer.HasAccess(User, FiscalApiPermissions.InvoicesRead, invoice.CompanyId)) return Forbidden();
        return Ok(ApiResponse<OrderWorkflowResult>.Ok(await application.GetOrderWorkflowAsync(id, ct), Correlation()));
    }

    [HttpGet("{id:guid}/summary-workflow")]
    public async Task<IActionResult> SummaryWorkflow(Guid id, CancellationToken ct)
    {
        var invoice = await application.GetAsync(id, ct);
        if (invoice is null) return Missing();
        if (!authorizer.HasAccess(User, FiscalApiPermissions.InvoicesRead, invoice.CompanyId)) return Forbidden();
        return Ok(ApiResponse<SummaryWorkflowResult>.Ok(await application.GetSummaryWorkflowAsync(id, ct), Correlation()));
    }

    private IActionResult? MutationError(Guid companyId, string permission, string actual, string expected)
    {
        if (!authorizer.HasAccess(User, permission, companyId)) return Forbidden();
        if (string.IsNullOrWhiteSpace(Key()) || Key().Length > 200)
            return BadRequest(ApiResponse<object>.Fail(new("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key je obavezan, do 200 znakova.", []), Correlation()));
        if (actual != expected)
            return BadRequest(ApiResponse<object>.Fail(new("CONFIRMATION_REQUIRED", $"Potvrda mora biti {expected}.", []), Correlation()));
        return null;
    }
    private IActionResult Missing() => NotFound(ApiResponse<object>.Fail(new("FISCAL_INVOICE_NOT_FOUND", "Račun nije pronađen.", []), Correlation()));
    private IActionResult Forbidden() => StatusCode(403, ApiResponse<object>.Fail(new("FORBIDDEN", "Nemate pristup ovoj operaciji ili firmi.", []), Correlation()));
    private string Key() => Request.Headers["Idempotency-Key"].FirstOrDefault() ?? "";
    private string Correlation() => HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString() ?? HttpContext.TraceIdentifier;
    private string Actor() => $"api-client:{User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown"}:{User.Identity?.Name ?? "unknown"}";
}
