using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Application.Certificates;
using Summa.Fiscal.Application.Activation;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/admin/companies")]
public sealed class CompanyOnboardingController(
    IFiscalOnboardingService service,
    ICertificateExpiryService certificateExpiryService,
    IFiscalActivationService activationService,
    IFiscalTcrRegistrationService tcrRegistrationService) : ControllerBase
{
    [HttpGet]
    [FiscalAdminAuthorize(FiscalApiPermissions.CompaniesRead)]
    public async Task<IActionResult> ListCompanies(CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = FilterCompanies(await service.ListCompaniesAsync(cancellationToken));
        return Ok(ApiResponse<IReadOnlyCollection<CompanySummary>>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CompaniesRead, "companyId")]
    public async Task<IActionResult> GetCompany(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetCompanyAsync(companyId, cancellationToken);
        return Ok(ApiResponse<CompanySummary>.Ok(result, CorrelationId()));
    }

    [HttpPost]
    [FiscalAdminAuthorize(FiscalApiPermissions.PlatformAdmin)]
    public async Task<IActionResult> Upsert([FromBody] CompanyOnboardingCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.UpsertCompanyAsync(command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<CompanySummary>.Ok(result, CorrelationId()));
    }

    [HttpPut("{companyId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CompaniesWrite, "companyId")]
    public async Task<IActionResult> UpdateCompany(Guid companyId, [FromBody] CompanyOnboardingCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.UpdateCompanyAsync(companyId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<CompanySummary>.Ok(result, CorrelationId()));
    }

    [HttpPut("{companyId:guid}/fiscal-identity")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CompaniesWrite, "companyId")]
    public async Task<IActionResult> UpdateFiscalIdentity(
        Guid companyId,
        [FromBody] CompanyFiscalIdentityCommand command,
        CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.UpdateFiscalIdentityAsync(
            companyId,
            command,
            Actor(),
            CorrelationId(),
            cancellationToken);
        return Ok(ApiResponse<CompanySummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/activate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CompaniesWrite, "companyId")]
    public Task<IActionResult> ActivateCompany(Guid companyId, CancellationToken cancellationToken) =>
        SetCompanyActive(companyId, true, cancellationToken);

    [HttpPost("{companyId:guid}/deactivate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CompaniesWrite, "companyId")]
    public Task<IActionResult> DeactivateCompany(Guid companyId, CancellationToken cancellationToken) =>
        SetCompanyActive(companyId, false, cancellationToken);

    [HttpGet("{companyId:guid}/production-profile")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> GetProductionProfile(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetProductionProfileAsync(companyId, cancellationToken);
        return Ok(ApiResponse<ProductionProfileSummary>.Ok(result, CorrelationId()));
    }

    [HttpPut("{companyId:guid}/production-profile")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> ConfigureProduction(Guid companyId, [FromBody] ProductionProfileCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.ConfigureProductionAsync(companyId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<ProductionProfileSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/production-profile/register-enu")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ActivationProduction, "companyId")]
    public async Task<IActionResult> RegisterProductionEnu(Guid companyId, [FromBody] RegisterProductionTcrCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await tcrRegistrationService.RegisterProductionAsync(companyId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<RegisterProductionTcrResult>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/business-units")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> ListBusinessUnits(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.ListBusinessUnitsAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<BusinessUnitSummary>>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/business-units/{businessUnitId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> GetBusinessUnit(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetBusinessUnitAsync(companyId, businessUnitId, cancellationToken);
        return Ok(ApiResponse<BusinessUnitSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/business-units")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> AddBusinessUnit(Guid companyId, [FromBody] BusinessUnitCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.AddBusinessUnitAsync(companyId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<BusinessUnitSummary>.Ok(result, CorrelationId()));
    }

    [HttpPut("{companyId:guid}/business-units/{businessUnitId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> UpdateBusinessUnit(Guid companyId, Guid businessUnitId, [FromBody] BusinessUnitCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.UpdateBusinessUnitAsync(companyId, businessUnitId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<BusinessUnitSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/business-units/{businessUnitId:guid}/activate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public Task<IActionResult> ActivateBusinessUnit(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken) =>
        SetBusinessUnitActive(companyId, businessUnitId, true, cancellationToken);

    [HttpPost("{companyId:guid}/business-units/{businessUnitId:guid}/deactivate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public Task<IActionResult> DeactivateBusinessUnit(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken) =>
        SetBusinessUnitActive(companyId, businessUnitId, false, cancellationToken);

    [HttpGet("{companyId:guid}/devices")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> ListDevices(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.ListDevicesAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<FiscalDeviceSummary>>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/devices/{deviceId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> GetDevice(Guid companyId, Guid deviceId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetDeviceAsync(companyId, deviceId, cancellationToken);
        return Ok(ApiResponse<FiscalDeviceSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/devices")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> AddDevice(Guid companyId, [FromBody] FiscalDeviceCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.AddDeviceAsync(companyId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalDeviceSummary>.Ok(result, CorrelationId()));
    }

    [HttpPut("{companyId:guid}/devices/{deviceId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> UpdateDevice(Guid companyId, Guid deviceId, [FromBody] FiscalDeviceCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.UpdateDeviceAsync(companyId, deviceId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalDeviceSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/devices/{deviceId:guid}/activate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public Task<IActionResult> ActivateDevice(Guid companyId, Guid deviceId, CancellationToken cancellationToken) =>
        SetDeviceActive(companyId, deviceId, true, cancellationToken);

    [HttpPost("{companyId:guid}/devices/{deviceId:guid}/deactivate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public Task<IActionResult> DeactivateDevice(Guid companyId, Guid deviceId, CancellationToken cancellationToken) =>
        SetDeviceActive(companyId, deviceId, false, cancellationToken);

    [HttpGet("{companyId:guid}/operators")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> ListOperators(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.ListOperatorsAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<FiscalOperatorSummary>>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/operators/{operatorId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> GetOperator(Guid companyId, Guid operatorId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetOperatorAsync(companyId, operatorId, cancellationToken);
        return Ok(ApiResponse<FiscalOperatorSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/operators")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> AddOperator(Guid companyId, [FromBody] FiscalOperatorCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.AddOperatorAsync(companyId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalOperatorSummary>.Ok(result, CorrelationId()));
    }

    [HttpPut("{companyId:guid}/operators/{operatorId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public async Task<IActionResult> UpdateOperator(Guid companyId, Guid operatorId, [FromBody] FiscalOperatorCommand command, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.UpdateOperatorAsync(companyId, operatorId, command, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalOperatorSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/operators/{operatorId:guid}/activate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public Task<IActionResult> ActivateOperator(Guid companyId, Guid operatorId, CancellationToken cancellationToken) =>
        SetOperatorActive(companyId, operatorId, true, cancellationToken);

    [HttpPost("{companyId:guid}/operators/{operatorId:guid}/deactivate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationWrite, "companyId")]
    public Task<IActionResult> DeactivateOperator(Guid companyId, Guid operatorId, CancellationToken cancellationToken) =>
        SetOperatorActive(companyId, operatorId, false, cancellationToken);

    [HttpPost("{companyId:guid}/certificates")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CertificatesManage, "companyId")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadCertificate(
        Guid companyId,
        [FromForm] IFormFile file,
        [FromForm] string password,
        CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        var result = await service.UploadCertificateAsync(
            companyId, new(file.FileName, stream.ToArray(), password), Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalCertificateSummary>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/certificates")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CertificatesRead, "companyId")]
    public async Task<IActionResult> ListCertificates(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.ListCertificatesAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<FiscalCertificateSummary>>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/certificates/{certificateId:guid}")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CertificatesRead, "companyId")]
    public async Task<IActionResult> GetCertificate(Guid companyId, Guid certificateId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetCertificateAsync(companyId, certificateId, cancellationToken);
        return Ok(ApiResponse<FiscalCertificateSummary>.Ok(result, CorrelationId()));
    }

    [HttpGet("~/api/v1/admin/certificate-expirations")]
    [FiscalAdminAuthorize(FiscalApiPermissions.AlertsRead)]
    public async Task<IActionResult> ListCertificateExpirations([FromQuery] int days = 60, CancellationToken cancellationToken = default)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = FilterCompanies(await certificateExpiryService.ListExpiringAsync(days, cancellationToken));
        return Ok(ApiResponse<IReadOnlyCollection<CertificateExpirationSummary>>.Ok(result, CorrelationId()));
    }

    [HttpPost("~/api/v1/admin/certificate-expirations/scan")]
    [FiscalAdminAuthorize(FiscalApiPermissions.AlertsManage)]
    public async Task<IActionResult> ScanCertificateExpirations(CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await certificateExpiryService.ScanAsync(cancellationToken);
        return Ok(ApiResponse<CertificateExpiryScanResult>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/certificate-alerts")]
    [FiscalAdminAuthorize(FiscalApiPermissions.AlertsRead, "companyId")]
    public async Task<IActionResult> ListCertificateAlerts(Guid companyId, [FromQuery] bool includeAcknowledged = false, CancellationToken cancellationToken = default)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await certificateExpiryService.ListAlertsAsync(companyId, includeAcknowledged, cancellationToken);
        return Ok(ApiResponse<IReadOnlyCollection<CertificateExpiryAlertSummary>>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/certificate-alerts/{alertId:guid}/acknowledge")]
    [FiscalAdminAuthorize(FiscalApiPermissions.AlertsManage, "companyId")]
    public async Task<IActionResult> AcknowledgeCertificateAlert(Guid companyId, Guid alertId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await certificateExpiryService.AcknowledgeAsync(companyId, alertId, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<CertificateExpiryAlertSummary>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/certificates/{certificateId:guid}/activate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CertificatesManage, "companyId")]
    public Task<IActionResult> Activate(Guid companyId, Guid certificateId, CancellationToken cancellationToken) =>
        SetCertificateActive(companyId, certificateId, true, cancellationToken);

    [HttpPost("{companyId:guid}/certificates/{certificateId:guid}/deactivate")]
    [FiscalAdminAuthorize(FiscalApiPermissions.CertificatesManage, "companyId")]
    public Task<IActionResult> Deactivate(Guid companyId, Guid certificateId, CancellationToken cancellationToken) =>
        SetCertificateActive(companyId, certificateId, false, cancellationToken);

    [HttpGet("{companyId:guid}/readiness")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ConfigurationRead, "companyId")]
    public async Task<IActionResult> Readiness(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.GetReadinessAsync(companyId, cancellationToken);
        return Ok(ApiResponse<CompanyReadiness>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/activation")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ActivationRead, "companyId")]
    public async Task<IActionResult> ActivationStatus(Guid companyId, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await activationService.GetStatusAsync(companyId, cancellationToken);
        return Ok(ApiResponse<FiscalActivationStatus>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/activation/confirm-test")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ActivationTest, "companyId")]
    public async Task<IActionResult> ConfirmControlTest(Guid companyId, [FromBody] ConfirmFiscalTestRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await activationService.ConfirmSuccessfulTestAsync(
            companyId, request.InvoiceId, request.Confirmation, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalActivationStatus>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/activation/production")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ActivationProduction, "companyId")]
    public async Task<IActionResult> ActivateProduction(Guid companyId, [FromBody] FiscalActivationConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await activationService.ActivateProductionAsync(
            companyId, request.Confirmation, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalActivationStatus>.Ok(result, CorrelationId()));
    }

    [HttpPost("{companyId:guid}/activation/return-to-test")]
    [FiscalAdminAuthorize(FiscalApiPermissions.ActivationProduction, "companyId")]
    public async Task<IActionResult> ReturnToTest(Guid companyId, [FromBody] FiscalActivationConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await activationService.ReturnToTestAsync(
            companyId, request.Confirmation, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalActivationStatus>.Ok(result, CorrelationId()));
    }

    [HttpGet("{companyId:guid}/audit")]
    [FiscalAdminAuthorize(FiscalApiPermissions.AuditRead, "companyId")]
    public async Task<IActionResult> ListAudit(
        Guid companyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? action = null,
        [FromQuery] string? actor = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.ListAuditAsync(companyId, page, pageSize, action, actor, from, to, cancellationToken);
        return Ok(ApiResponse<PagedResult<FiscalAuditSummary>>.Ok(result, CorrelationId()));
    }

    private async Task<IActionResult> SetCompanyActive(Guid companyId, bool active, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.SetCompanyActiveAsync(companyId, active, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<CompanySummary>.Ok(result, CorrelationId()));
    }

    private async Task<IActionResult> SetBusinessUnitActive(Guid companyId, Guid businessUnitId, bool active, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.SetBusinessUnitActiveAsync(companyId, businessUnitId, active, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<BusinessUnitSummary>.Ok(result, CorrelationId()));
    }

    private async Task<IActionResult> SetDeviceActive(Guid companyId, Guid deviceId, bool active, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.SetDeviceActiveAsync(companyId, deviceId, active, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalDeviceSummary>.Ok(result, CorrelationId()));
    }

    private async Task<IActionResult> SetOperatorActive(Guid companyId, Guid operatorId, bool active, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.SetOperatorActiveAsync(companyId, operatorId, active, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalOperatorSummary>.Ok(result, CorrelationId()));
    }

    private async Task<IActionResult> SetCertificateActive(Guid companyId, Guid certificateId, bool active, CancellationToken cancellationToken)
    {
        if (!Authorized()) return UnauthorizedResponse();
        var result = await service.SetCertificateActiveAsync(companyId, certificateId, active, Actor(), CorrelationId(), cancellationToken);
        return Ok(ApiResponse<FiscalCertificateSummary>.Ok(result, CorrelationId()));
    }

    private bool Authorized() => Access().IsAllowed;
    private string Actor() => Access().Actor;
    private AdminAccessDecision Access() =>
        HttpContext.Items[FiscalAdminAuthorizationFilter.AccessItemName] as AdminAccessDecision
        ?? new(false, false, false, false, string.Empty, new HashSet<Guid>());
    private IReadOnlyCollection<CompanySummary> FilterCompanies(IReadOnlyCollection<CompanySummary> companies) =>
        Access().HasPlatformAccess ? companies : companies.Where(x => Access().CompanyIds.Contains(x.Id)).ToArray();
    private IReadOnlyCollection<CertificateExpirationSummary> FilterCompanies(IReadOnlyCollection<CertificateExpirationSummary> certificates) =>
        Access().HasPlatformAccess ? certificates : certificates.Where(x => Access().CompanyIds.Contains(x.CompanyId)).ToArray();
    private string CorrelationId() => HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString() ?? HttpContext.TraceIdentifier;
    private IActionResult UnauthorizedResponse() => StatusCode(StatusCodes.Status403Forbidden,
        ApiResponse<object>.Fail(new("ADMIN_PERMISSION_DENIED", "Klijent nema potrebnu administratorsku dozvolu.", []), CorrelationId()));
}

public sealed record ConfirmFiscalTestRequest(Guid InvoiceId, string Confirmation);
public sealed record FiscalActivationConfirmationRequest(string Confirmation);
