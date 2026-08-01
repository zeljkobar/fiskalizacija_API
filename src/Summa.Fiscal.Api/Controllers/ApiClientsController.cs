using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/admin/api-clients")]
public sealed class ApiClientsController(
    IApiClientRegistry registry,
    IBootstrapAdminAuthorizer adminAuthorizer) : ControllerBase
{

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var access = Access();
        if (!access.IsAllowed) return DeniedResponse(access);
        var result = await registry.ListAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<ApiClientSummary>>.Ok(result, CorrelationId()));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateApiClientRequest request,
        CancellationToken cancellationToken)
    {
        var access = Access();
        if (!access.IsAllowed) return DeniedResponse(access);
        try
        {
            var result = await registry.CreateAsync(
                request.Name,
                request.Permissions ?? [],
                request.CompanyIds ?? [],
                request.ExpiresAt,
                access.Actor,
                CorrelationId(),
                cancellationToken);
            return CreatedAtAction(
                nameof(List),
                ApiResponse<CreatedApiClient>.Ok(result, CorrelationId()));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object>.Fail(
                new("INVALID_API_CLIENT", exception.Message, []), CorrelationId()));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(ApiResponse<object>.Fail(
                new("INVALID_COMPANY_ACCESS", exception.Message, []), CorrelationId()));
        }
    }

    [HttpPost("{id:guid}/rotate-key")]
    public async Task<IActionResult> RotateKey(Guid id, CancellationToken cancellationToken)
    {
        var access = Access();
        if (!access.IsAllowed) return DeniedResponse(access);
        var result = await registry.RotateKeyAsync(id, access.Actor, CorrelationId(), cancellationToken);
        return result is null
            ? NotFound(ApiResponse<object>.Fail(
                new("API_CLIENT_NOT_FOUND", "Aplikacija nije pronađena.", []), CorrelationId()))
            : Ok(ApiResponse<CreatedApiClient>.Ok(result, CorrelationId()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var access = Access();
        if (!access.IsAllowed) return DeniedResponse(access);
        var deactivated = await registry.DeactivateAsync(id, access.Actor, CorrelationId(), cancellationToken);
        return deactivated
            ? NoContent()
            : NotFound(ApiResponse<object>.Fail(
                new("API_CLIENT_NOT_FOUND", "Aplikacija nije pronađena.", []), CorrelationId()));
    }

    private AdminAccessDecision Access() =>
        adminAuthorizer.Authorize(HttpContext, FiscalApiPermissions.ClientsAdmin);

    private IActionResult DeniedResponse(AdminAccessDecision access) => access.IsAuthenticated
        ? StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(
            new("ADMIN_PERMISSION_DENIED", "Klijent nema potrebnu administratorsku dozvolu.", []), CorrelationId()))
        : Unauthorized(ApiResponse<object>.Fail(
            new("ADMIN_AUTHENTICATION_REQUIRED", "Administratorski pristup nije odobren.", []), CorrelationId()));

    private string CorrelationId() =>
        HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString()
        ?? HttpContext.TraceIdentifier;
}

public sealed record CreateApiClientRequest(
    string Name,
    IReadOnlyCollection<string>? Permissions,
    IReadOnlyCollection<Guid>? CompanyIds,
    DateTimeOffset? ExpiresAt);
