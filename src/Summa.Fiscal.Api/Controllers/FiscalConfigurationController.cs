using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;

namespace Summa.Fiscal.Api.Controllers;

[ApiController]
[Route("api/v1/fiscal/configuration")]
public sealed class FiscalConfigurationController(
    IOptions<PuFiscalizationOptionsV5> options) : ControllerBase
{
    [HttpGet("readiness")]
    [ProducesResponseType(typeof(ApiResponse<FiscalConfigurationReadinessResponse>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<FiscalConfigurationReadinessResponse>> GetReadiness()
    {
        var configuration = options.Value;
        var readiness = configuration.GetReadiness();
        var correlationId =
            HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString()
            ?? HttpContext.TraceIdentifier;

        var response = new FiscalConfigurationReadinessResponse(
            configuration.Environment,
            readiness.IsReady,
            readiness.MissingFields,
            configuration.IssuerTin,
            configuration.TcrCode,
            configuration.SoftwareCode,
            configuration.OperatorCode);

        return Ok(ApiResponse<FiscalConfigurationReadinessResponse>.Ok(response, correlationId));
    }
}

public sealed record FiscalConfigurationReadinessResponse(
    string Environment,
    bool IsReady,
    IReadOnlyCollection<string> MissingFields,
    string IssuerTin,
    string TcrCode,
    string SoftwareCode,
    string OperatorCode);
