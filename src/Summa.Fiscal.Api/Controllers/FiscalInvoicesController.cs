using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;

namespace Summa.Fiscal.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/fiscal/invoices")]
public sealed class FiscalInvoicesController(
    IFiscalInvoiceApplicationService applicationService,
    IFiscalInvoiceSubmissionServiceV5 submissionService,
    IFiscalAccessAuthorizer accessAuthorizer,
    IHostEnvironment hostEnvironment) : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<FiscalInvoiceResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFiscalInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = GetCorrelationId();
        if (!accessAuthorizer.HasAccess(User, FiscalApiPermissions.InvoicesCreate, request.CompanyId))
        {
            return ForbiddenResponse(correlationId);
        }

        var idempotencyKey = Request.Headers[IdempotencyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            var error = new ApiError(
                "IDEMPOTENCY_KEY_REQUIRED",
                $"Header {IdempotencyHeader} je obavezan i može imati najviše 200 znakova.",
                []);
            return BadRequest(ApiResponse<object>.Fail(error, correlationId));
        }

        var command = new CreateFiscalInvoiceCommand(
            request.CompanyId,
            request.BusinessUnitId,
            request.DeviceId,
            request.OperatorId,
            request.InvoiceType,
            request.InvoiceNumber,
            request.IssueDateTime,
            request.Currency,
            (request.Items ?? [])
                .Select(item => new CreateFiscalInvoiceItem(
                    item.Name,
                    item.Quantity,
                    item.UnitPrice,
                    item.VatRate,
                    item.ItemCode,
                    item.UnitOfMeasure,
                    item.DiscountAmount))
                .ToArray(),
            (request.Payments ?? [])
                .Select(payment => new CreateFiscalPayment(
                    payment.PaymentType,
                    payment.Amount,
                    payment.Reference))
                .ToArray(),
            idempotencyKey,
            correlationId);

        var result = await applicationService.CreateAsync(command, cancellationToken);
        return AcceptedAtAction(
            nameof(GetById),
            new { id = result.Id },
            ApiResponse<FiscalInvoiceResult>.Ok(result, correlationId));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FiscalInvoiceResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var correlationId = GetCorrelationId();
        var result = await applicationService.GetAsync(id, cancellationToken);

        if (result is null)
        {
            return NotFound(NotFoundResponse(correlationId));
        }

        if (!accessAuthorizer.HasAccess(User, FiscalApiPermissions.InvoicesRead, result.CompanyId))
        {
            return ForbiddenResponse(correlationId);
        }

        return Ok(ApiResponse<FiscalInvoiceResult>.Ok(result, correlationId));
    }

    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<FiscalInvoiceStatusResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var correlationId = GetCorrelationId();
        var result = await applicationService.GetStatusAsync(id, cancellationToken);

        if (result is null)
        {
            return NotFound(NotFoundResponse(correlationId));
        }

        if (!accessAuthorizer.HasAccess(User, FiscalApiPermissions.InvoicesRead, result.CompanyId))
        {
            return ForbiddenResponse(correlationId);
        }

        return Ok(ApiResponse<FiscalInvoiceStatusResult>.Ok(result, correlationId));
    }

    [HttpGet("{id:guid}/qr")]
    [ProducesResponseType(typeof(ApiResponse<FiscalInvoiceQrResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetQrCodeData(Guid id, CancellationToken cancellationToken)
    {
        var correlationId = GetCorrelationId();
        var result = await applicationService.GetAsync(id, cancellationToken);

        if (result is null)
        {
            return NotFound(NotFoundResponse(correlationId));
        }

        if (!accessAuthorizer.HasAccess(User, FiscalApiPermissions.InvoicesRead, result.CompanyId))
        {
            return ForbiddenResponse(correlationId);
        }

        if (result.Status != FiscalStatus.Fiscalized ||
            string.IsNullOrWhiteSpace(result.QrCodeData))
        {
            return Conflict(ApiResponse<object>.Fail(
                new(
                    "FISCAL_QR_NOT_AVAILABLE",
                    "QR podatak još nije dostupan za ovaj račun.",
                    []),
                correlationId));
        }

        return Ok(ApiResponse<FiscalInvoiceQrResult>.Ok(
            new(result.Id, result.Iic!, result.Jikr!, result.QrCodeData),
            correlationId));
    }

    [HttpPost("{id:guid}/fiscalize")]
    [ProducesResponseType(
        typeof(ApiResponse<FiscalInvoiceSubmissionResultV5>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Fiscalize(
        Guid id,
        [FromBody] FiscalizeStoredInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = GetCorrelationId();
        var existing = await applicationService.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound(NotFoundResponse(correlationId));
        }
        if (!accessAuthorizer.HasAccess(
                User,
                FiscalApiPermissions.InvoicesFiscalize,
                existing.CompanyId))
        {
            return ForbiddenResponse(correlationId);
        }

        if (hostEnvironment.IsDevelopment() &&
            !string.Equals(request.Confirmation, "SEND_TO_PU_TEST", StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse<object>.Fail(
                new(
                    "TEST_SEND_CONFIRMATION_REQUIRED",
                    "Za testno slanje confirmation mora biti SEND_TO_PU_TEST.",
                    []),
                correlationId));
        }

        var result = await submissionService.SubmitAsync(id, correlationId, cancellationToken);
        if (result is null)
        {
            return NotFound(NotFoundResponse(correlationId));
        }

        return Ok(ApiResponse<FiscalInvoiceSubmissionResultV5>.Ok(result, correlationId));
    }

    private string GetCorrelationId() =>
        HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString()
        ?? HttpContext.TraceIdentifier;

    private static ApiResponse<object> NotFoundResponse(string correlationId)
    {
        var error = new ApiError(
            "FISCAL_INVOICE_NOT_FOUND",
            "Fiskalni račun nije pronađen.",
            []);
        return ApiResponse<object>.Fail(error, correlationId);
    }

    private ObjectResult ForbiddenResponse(string correlationId) => StatusCode(
        StatusCodes.Status403Forbidden,
        ApiResponse<object>.Fail(
            new(
                "COMPANY_ACCESS_DENIED",
                "Aplikacija nema dozvolu za ovu firmu ili operaciju.",
                []),
            correlationId));
}

public sealed record FiscalizeStoredInvoiceRequest(string Confirmation);

public sealed record FiscalInvoiceQrResult(
    Guid InvoiceId,
    string Iic,
    string Jikr,
    string VerificationUrl);
