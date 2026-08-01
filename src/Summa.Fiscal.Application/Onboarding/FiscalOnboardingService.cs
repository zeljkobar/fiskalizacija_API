using System.Text.Json;

namespace Summa.Fiscal.Application.Onboarding;

public sealed class FiscalOnboardingService(
    IFiscalOnboardingRepository repository,
    IFiscalCertificateVault vault,
    IFiscalCertificateInspector inspector) : IFiscalOnboardingService
{
    public async Task<CompanySummary> UpsertCompanyAsync(CompanyOnboardingCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        ValidateCompany(command);
        var result = await repository.UpsertCompanyAsync(command, cancellationToken);
        await AuditAsync(result.Id, "COMPANY_UPSERTED", actor, correlationId, new { result.Tin, result.Environment }, cancellationToken);
        return result;
    }

    public Task<IReadOnlyCollection<CompanySummary>> ListCompaniesAsync(CancellationToken cancellationToken) =>
        repository.ListCompaniesAsync(cancellationToken);

    public async Task<CompanySummary> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await repository.GetCompanyAsync(companyId, cancellationToken)
        ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");

    public async Task<CompanySummary> UpdateCompanyAsync(Guid companyId, CompanyOnboardingCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        ValidateCompany(command);
        var result = await repository.UpdateCompanyAsync(companyId, command, cancellationToken);
        await AuditAsync(companyId, "COMPANY_UPDATED", actor, correlationId, new { result.Tin, result.Environment }, cancellationToken);
        return result;
    }

    public async Task<CompanySummary> SetCompanyActiveAsync(Guid companyId, bool active, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var result = await repository.SetCompanyActiveAsync(companyId, active, cancellationToken);
        await AuditAsync(companyId, active ? "COMPANY_ACTIVATED" : "COMPANY_DEACTIVATED", actor, correlationId, new { result.Id }, cancellationToken);
        return result;
    }

    public async Task<BusinessUnitSummary> AddBusinessUnitAsync(Guid companyId, BusinessUnitCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        RequireText(command.Code, "BUSINESS_UNIT_CODE_REQUIRED", "Kod poslovne jedinice je obavezan.");
        RequireText(command.Name, "BUSINESS_UNIT_NAME_REQUIRED", "Naziv poslovne jedinice je obavezan.");
        var result = await repository.AddBusinessUnitAsync(companyId, command, cancellationToken);
        await AuditAsync(companyId, "BUSINESS_UNIT_CREATED", actor, correlationId, new { result.Id, result.Code }, cancellationToken);
        return result;
    }

    public Task<IReadOnlyCollection<BusinessUnitSummary>> ListBusinessUnitsAsync(Guid companyId, CancellationToken cancellationToken) =>
        repository.ListBusinessUnitsAsync(companyId, cancellationToken);

    public async Task<BusinessUnitSummary> GetBusinessUnitAsync(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken) =>
        await repository.GetBusinessUnitAsync(companyId, businessUnitId, cancellationToken)
        ?? throw new FiscalOnboardingException("BUSINESS_UNIT_NOT_FOUND", "Poslovna jedinica ne postoji ili ne pripada firmi.");

    public async Task<BusinessUnitSummary> UpdateBusinessUnitAsync(Guid companyId, Guid businessUnitId, BusinessUnitCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        ValidateBusinessUnit(command);
        var result = await repository.UpdateBusinessUnitAsync(companyId, businessUnitId, command, cancellationToken);
        await AuditAsync(companyId, "BUSINESS_UNIT_UPDATED", actor, correlationId, new { result.Id, result.Code }, cancellationToken);
        return result;
    }

    public async Task<BusinessUnitSummary> SetBusinessUnitActiveAsync(Guid companyId, Guid businessUnitId, bool active, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var result = await repository.SetBusinessUnitActiveAsync(companyId, businessUnitId, active, cancellationToken);
        await AuditAsync(companyId, active ? "BUSINESS_UNIT_ACTIVATED" : "BUSINESS_UNIT_DEACTIVATED", actor, correlationId, new { result.Id }, cancellationToken);
        return result;
    }

    public async Task<FiscalDeviceSummary> AddDeviceAsync(Guid companyId, FiscalDeviceCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        RequireText(command.TcrCode, "TCR_CODE_REQUIRED", "ENU/TCR kod je obavezan.");
        RequireText(command.InternalCode, "DEVICE_INTERNAL_CODE_REQUIRED", "Interni kod uređaja je obavezan.");
        var result = await repository.AddDeviceAsync(companyId, command, cancellationToken);
        await AuditAsync(companyId, "FISCAL_DEVICE_CREATED", actor, correlationId, new { result.Id, result.TcrCode }, cancellationToken);
        return result;
    }

    public Task<IReadOnlyCollection<FiscalDeviceSummary>> ListDevicesAsync(Guid companyId, CancellationToken cancellationToken) =>
        repository.ListDevicesAsync(companyId, cancellationToken);

    public async Task<FiscalDeviceSummary> GetDeviceAsync(Guid companyId, Guid deviceId, CancellationToken cancellationToken) =>
        await repository.GetDeviceAsync(companyId, deviceId, cancellationToken)
        ?? throw new FiscalOnboardingException("FISCAL_DEVICE_NOT_FOUND", "ENU uređaj ne postoji ili ne pripada firmi.");

    public async Task<FiscalDeviceSummary> UpdateDeviceAsync(Guid companyId, Guid deviceId, FiscalDeviceCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        ValidateDevice(command);
        var result = await repository.UpdateDeviceAsync(companyId, deviceId, command, cancellationToken);
        await AuditAsync(companyId, "FISCAL_DEVICE_UPDATED", actor, correlationId, new { result.Id, result.TcrCode }, cancellationToken);
        return result;
    }

    public async Task<FiscalDeviceSummary> SetDeviceActiveAsync(Guid companyId, Guid deviceId, bool active, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var result = await repository.SetDeviceActiveAsync(companyId, deviceId, active, cancellationToken);
        await AuditAsync(companyId, active ? "FISCAL_DEVICE_ACTIVATED" : "FISCAL_DEVICE_DEACTIVATED", actor, correlationId, new { result.Id }, cancellationToken);
        return result;
    }

    public async Task<FiscalOperatorSummary> AddOperatorAsync(Guid companyId, FiscalOperatorCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        RequireText(command.OperatorCode, "OPERATOR_CODE_REQUIRED", "Kod operatera je obavezan.");
        var result = await repository.AddOperatorAsync(companyId, command, cancellationToken);
        await AuditAsync(companyId, "FISCAL_OPERATOR_CREATED", actor, correlationId, new { result.Id, result.OperatorCode }, cancellationToken);
        return result;
    }

    public Task<IReadOnlyCollection<FiscalOperatorSummary>> ListOperatorsAsync(Guid companyId, CancellationToken cancellationToken) =>
        repository.ListOperatorsAsync(companyId, cancellationToken);

    public async Task<FiscalOperatorSummary> GetOperatorAsync(Guid companyId, Guid operatorId, CancellationToken cancellationToken) =>
        await repository.GetOperatorAsync(companyId, operatorId, cancellationToken)
        ?? throw new FiscalOnboardingException("FISCAL_OPERATOR_NOT_FOUND", "Operater ne postoji ili ne pripada firmi.");

    public async Task<FiscalOperatorSummary> UpdateOperatorAsync(Guid companyId, Guid operatorId, FiscalOperatorCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        ValidateOperator(command);
        var result = await repository.UpdateOperatorAsync(companyId, operatorId, command, cancellationToken);
        await AuditAsync(companyId, "FISCAL_OPERATOR_UPDATED", actor, correlationId, new { result.Id, result.OperatorCode }, cancellationToken);
        return result;
    }

    public async Task<FiscalOperatorSummary> SetOperatorActiveAsync(Guid companyId, Guid operatorId, bool active, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var result = await repository.SetOperatorActiveAsync(companyId, operatorId, active, cancellationToken);
        await AuditAsync(companyId, active ? "FISCAL_OPERATOR_ACTIVATED" : "FISCAL_OPERATOR_DEACTIVATED", actor, correlationId, new { result.Id }, cancellationToken);
        return result;
    }

    public async Task<FiscalCertificateSummary> UploadCertificateAsync(Guid companyId, CertificateUpload upload, string actor, string correlationId, CancellationToken cancellationToken)
    {
        if (upload.PfxBytes.Length == 0) throw new FiscalOnboardingException("CERT_UPLOAD_INVALID_FILE", "PFX/P12 fajl je prazan.");
        if (upload.PfxBytes.Length > 5 * 1024 * 1024) throw new FiscalOnboardingException("CERT_UPLOAD_FILE_TOO_LARGE", "Sertifikat ne smije biti veći od 5 MB.");
        var company = await repository.GetCompanyAsync(companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        var inspection = inspector.Inspect(upload.PfxBytes, upload.Password);
        if (!inspection.HasPrivateKey) throw new FiscalOnboardingException("CERT_UPLOAD_NO_PRIVATE_KEY", "Sertifikat nema privatni ključ.");
        if (inspection.ValidTo <= DateTimeOffset.UtcNow) throw new FiscalOnboardingException("CERT_UPLOAD_EXPIRED", "Sertifikat je istekao.");
        if (!string.IsNullOrWhiteSpace(inspection.SubjectTin) && !string.Equals(inspection.SubjectTin, company.Tin, StringComparison.Ordinal))
            throw new FiscalOnboardingException("CERT_ACTIVATE_COMPANY_MISMATCH", "PIB u sertifikatu ne odgovara firmi.");

        var certificateId = Guid.NewGuid();
        var storageKey = await vault.StoreAsync(companyId, certificateId, upload.PfxBytes, upload.Password, cancellationToken);
        FiscalCertificateSummary result;
        try
        {
            result = await repository.AddCertificateAsync(companyId, upload.FileName, storageKey, inspection, cancellationToken);
        }
        catch
        {
            await vault.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }
        await AuditAsync(companyId, "CERTIFICATE_UPLOADED", actor, correlationId, new { result.Id, result.Thumbprint, result.ValidTo }, cancellationToken);
        return result;
    }

    public Task<IReadOnlyCollection<FiscalCertificateSummary>> ListCertificatesAsync(Guid companyId, CancellationToken cancellationToken) =>
        repository.ListCertificatesAsync(companyId, cancellationToken);

    public async Task<FiscalCertificateSummary> GetCertificateAsync(Guid companyId, Guid certificateId, CancellationToken cancellationToken) =>
        await repository.GetCertificateAsync(companyId, certificateId, cancellationToken)
        ?? throw new FiscalOnboardingException("CERTIFICATE_NOT_FOUND", "Sertifikat ne postoji ili ne pripada firmi.");

    public async Task<FiscalCertificateSummary> SetCertificateActiveAsync(Guid companyId, Guid certificateId, bool active, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var certificate = await repository.GetCertificateAsync(companyId, certificateId, cancellationToken)
            ?? throw new FiscalOnboardingException("CERT_ACTIVATE_NOT_FOUND", "Sertifikat ne postoji.");
        if (active && certificate.ValidTo <= DateTimeOffset.UtcNow)
            throw new FiscalOnboardingException("CERT_ACTIVATE_EXPIRED", "Istekli sertifikat se ne može aktivirati.");
        var result = await repository.SetCertificateActiveAsync(companyId, certificateId, active, cancellationToken);
        await AuditAsync(companyId, active ? "CERTIFICATE_ACTIVATED" : "CERTIFICATE_DEACTIVATED", actor, correlationId, new { result.Id, result.Thumbprint }, cancellationToken);
        return result;
    }

    public async Task<CompanyReadiness> GetReadinessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        var configuration = await repository.GetConfigurationAsync(companyId, cancellationToken);
        var certificates = await repository.ListCertificatesAsync(companyId, cancellationToken);
        var activeCertificate = certificates.SingleOrDefault(x => x.IsActive);
        var issues = new List<ReadinessIssue>();
        if (!company.IsActive) issues.Add(new("COMPANY_INACTIVE", "Firma nije aktivna."));
        if (string.IsNullOrWhiteSpace(company.SoftwareCode)) issues.Add(new("SOFTWARE_CODE_MISSING", "Nedostaje kod softvera."));
        if (string.IsNullOrWhiteSpace(company.MaintainerCode)) issues.Add(new("MAINTAINER_CODE_MISSING", "Nedostaje kod održavaoca."));
        if (!configuration.Units.Any(x => x.IsActive)) issues.Add(new("BUSINESS_UNIT_MISSING", "Nema aktivne poslovne jedinice."));
        if (!configuration.Devices.Any(x => x.IsActive)) issues.Add(new("FISCAL_DEVICE_MISSING", "Nema aktivnog ENU uređaja."));
        if (!configuration.Operators.Any(x => x.IsActive)) issues.Add(new("FISCAL_OPERATOR_MISSING", "Nema aktivnog operatera."));
        if (activeCertificate is null) issues.Add(new("ACTIVE_CERTIFICATE_MISSING", "Nema aktivnog fiskalnog sertifikata."));
        else if (activeCertificate.ValidTo <= DateTimeOffset.UtcNow) issues.Add(new("ACTIVE_CERTIFICATE_EXPIRED", "Aktivni sertifikat je istekao."));
        return new(companyId, issues.Count == 0, issues, activeCertificate?.Id);
    }

    public Task<PagedResult<FiscalAuditSummary>> ListAuditAsync(Guid companyId, int page, int pageSize, string? action, string? actor, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (page < 1) throw new FiscalOnboardingException("AUDIT_PAGE_INVALID", "Broj stranice mora biti najmanje 1.");
        if (pageSize is < 1 or > 200) throw new FiscalOnboardingException("AUDIT_PAGE_SIZE_INVALID", "Veličina stranice mora biti između 1 i 200.");
        if (from.HasValue && to.HasValue && from > to) throw new FiscalOnboardingException("AUDIT_PERIOD_INVALID", "Početak perioda mora biti prije kraja perioda.");
        return repository.ListAuditAsync(companyId, page, pageSize, action, actor, from, to, cancellationToken);
    }

    public async Task<CompanyFiscalContext> ResolveContextAsync(Guid companyId, Guid businessUnitId, Guid deviceId, Guid operatorId, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var readiness = await GetReadinessAsync(companyId, cancellationToken);
        if (!readiness.IsReady) throw new FiscalOnboardingException("COMPANY_NOT_READY", "Firma nije spremna za fiskalizaciju.");
        var company = (await repository.GetCompanyAsync(companyId, cancellationToken))!;
        var configuration = await repository.GetConfigurationAsync(companyId, cancellationToken);
        var unit = configuration.Units.SingleOrDefault(x => x.Id == businessUnitId && x.IsActive)
            ?? throw new FiscalOnboardingException("BUSINESS_UNIT_INVALID", "Poslovna jedinica nije aktivna ili ne pripada firmi.");
        var device = configuration.Devices.SingleOrDefault(x => x.Id == deviceId && x.BusinessUnitId == unit.Id && x.IsActive)
            ?? throw new FiscalOnboardingException("FISCAL_DEVICE_INVALID", "ENU uređaj nije aktivan ili ne pripada poslovnoj jedinici.");
        var fiscalOperator = configuration.Operators.SingleOrDefault(x => x.Id == operatorId && x.IsActive)
            ?? throw new FiscalOnboardingException("FISCAL_OPERATOR_INVALID", "Operater nije aktivan ili ne pripada firmi.");
        var certificate = (await repository.GetCertificateAsync(companyId, readiness.ActiveCertificateId!.Value, cancellationToken))!;
        var storageKey = await repository.GetCertificateStorageKeyAsync(companyId, certificate.Id, cancellationToken)
            ?? throw new FiscalOnboardingException("CERTIFICATE_STORAGE_NOT_FOUND", "Skladišni zapis sertifikata ne postoji.");
        var material = await vault.LoadAsync(storageKey, cancellationToken);
        await AuditAsync(companyId, "CERTIFICATE_ACCESSED_FOR_FISCALIZATION", actor, correlationId, new { certificate.Id, certificate.Thumbprint }, cancellationToken);
        return new(company, unit, device, fiscalOperator, certificate, material.PfxBytes, material.Password);
    }

    private Task AuditAsync(Guid? companyId, string action, string actor, string correlationId, object data, CancellationToken cancellationToken) =>
        repository.AddAuditAsync(companyId, action, correlationId, actor, JsonSerializer.Serialize(data), cancellationToken);

    private static void ValidateCompany(CompanyOnboardingCommand command)
    {
        RequireText(command.Tin, "TIN_REQUIRED", "PIB je obavezan.");
        if (command.Tin.Any(c => !char.IsDigit(c))) throw new FiscalOnboardingException("TIN_INVALID", "PIB mora sadržati samo cifre.");
        RequireText(command.LegalName, "LEGAL_NAME_REQUIRED", "Pravni naziv firme je obavezan.");
        RequireText(command.Country, "COUNTRY_REQUIRED", "Država firme je obavezna.");
        if (command.Environment is not ("Test" or "Production")) throw new FiscalOnboardingException("ENVIRONMENT_INVALID", "Okruženje mora biti Test ili Production.");
        if (!Uri.TryCreate(command.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new FiscalOnboardingException("ENDPOINT_INVALID", "PU endpoint mora biti apsolutna HTTPS adresa.");
    }

    private static void ValidateBusinessUnit(BusinessUnitCommand command)
    {
        RequireText(command.Code, "BUSINESS_UNIT_CODE_REQUIRED", "Kod poslovne jedinice je obavezan.");
        RequireText(command.Name, "BUSINESS_UNIT_NAME_REQUIRED", "Naziv poslovne jedinice je obavezan.");
    }

    private static void ValidateDevice(FiscalDeviceCommand command)
    {
        RequireText(command.TcrCode, "TCR_CODE_REQUIRED", "ENU/TCR kod je obavezan.");
        RequireText(command.InternalCode, "DEVICE_INTERNAL_CODE_REQUIRED", "Interni kod uređaja je obavezan.");
    }

    private static void ValidateOperator(FiscalOperatorCommand command) =>
        RequireText(command.OperatorCode, "OPERATOR_CODE_REQUIRED", "Kod operatera je obavezan.");

    private static void RequireText(string value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FiscalOnboardingException(code, message);
    }
}
