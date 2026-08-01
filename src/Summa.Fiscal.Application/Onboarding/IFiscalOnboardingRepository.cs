namespace Summa.Fiscal.Application.Onboarding;

public interface IFiscalOnboardingRepository
{
    Task<CompanySummary> UpsertCompanyAsync(CompanyOnboardingCommand command, CancellationToken cancellationToken);
    Task<CompanySummary> UpdateCompanyAsync(Guid companyId, CompanyOnboardingCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CompanySummary>> ListCompaniesAsync(CancellationToken cancellationToken);
    Task<CompanySummary?> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanySummary> SetCompanyActiveAsync(Guid companyId, bool active, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> AddBusinessUnitAsync(Guid companyId, BusinessUnitCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BusinessUnitSummary>> ListBusinessUnitsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<BusinessUnitSummary?> GetBusinessUnitAsync(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> UpdateBusinessUnitAsync(Guid companyId, Guid businessUnitId, BusinessUnitCommand command, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> SetBusinessUnitActiveAsync(Guid companyId, Guid businessUnitId, bool active, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> AddDeviceAsync(Guid companyId, FiscalDeviceCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalDeviceSummary>> ListDevicesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary?> GetDeviceAsync(Guid companyId, Guid deviceId, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> UpdateDeviceAsync(Guid companyId, Guid deviceId, FiscalDeviceCommand command, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> SetDeviceActiveAsync(Guid companyId, Guid deviceId, bool active, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> AddOperatorAsync(Guid companyId, FiscalOperatorCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalOperatorSummary>> ListOperatorsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary?> GetOperatorAsync(Guid companyId, Guid operatorId, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> UpdateOperatorAsync(Guid companyId, Guid operatorId, FiscalOperatorCommand command, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> SetOperatorActiveAsync(Guid companyId, Guid operatorId, bool active, CancellationToken cancellationToken);
    Task<FiscalCertificateSummary> AddCertificateAsync(Guid companyId, string fileName, string storageKey, CertificateInspection inspection, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalCertificateSummary>> ListCertificatesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalCertificateSummary?> GetCertificateAsync(Guid companyId, Guid certificateId, CancellationToken cancellationToken);
    Task<string?> GetCertificateStorageKeyAsync(Guid companyId, Guid certificateId, CancellationToken cancellationToken);
    Task<FiscalCertificateSummary> SetCertificateActiveAsync(Guid companyId, Guid certificateId, bool active, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<BusinessUnitSummary> Units, IReadOnlyCollection<FiscalDeviceSummary> Devices, IReadOnlyCollection<FiscalOperatorSummary> Operators)> GetConfigurationAsync(Guid companyId, CancellationToken cancellationToken);
    Task AddAuditAsync(Guid? companyId, string action, string correlationId, string actor, string dataJson, CancellationToken cancellationToken);
    Task<PagedResult<FiscalAuditSummary>> ListAuditAsync(Guid companyId, int page, int pageSize, string? action, string? actor, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

public interface IFiscalCertificateVault
{
    Task<string> StoreAsync(Guid companyId, Guid certificateId, byte[] pfxBytes, string password, CancellationToken cancellationToken);
    Task<(byte[] PfxBytes, string Password)> LoadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IFiscalCertificateInspector
{
    CertificateInspection Inspect(byte[] pfxBytes, string password);
}

public interface IFiscalOnboardingService
{
    Task<CompanySummary> UpsertCompanyAsync(CompanyOnboardingCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CompanySummary>> ListCompaniesAsync(CancellationToken cancellationToken);
    Task<CompanySummary> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanySummary> UpdateCompanyAsync(Guid companyId, CompanyOnboardingCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<CompanySummary> SetCompanyActiveAsync(Guid companyId, bool active, string actor, string correlationId, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> AddBusinessUnitAsync(Guid companyId, BusinessUnitCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BusinessUnitSummary>> ListBusinessUnitsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> GetBusinessUnitAsync(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> UpdateBusinessUnitAsync(Guid companyId, Guid businessUnitId, BusinessUnitCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<BusinessUnitSummary> SetBusinessUnitActiveAsync(Guid companyId, Guid businessUnitId, bool active, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> AddDeviceAsync(Guid companyId, FiscalDeviceCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalDeviceSummary>> ListDevicesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> GetDeviceAsync(Guid companyId, Guid deviceId, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> UpdateDeviceAsync(Guid companyId, Guid deviceId, FiscalDeviceCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalDeviceSummary> SetDeviceActiveAsync(Guid companyId, Guid deviceId, bool active, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> AddOperatorAsync(Guid companyId, FiscalOperatorCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalOperatorSummary>> ListOperatorsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> GetOperatorAsync(Guid companyId, Guid operatorId, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> UpdateOperatorAsync(Guid companyId, Guid operatorId, FiscalOperatorCommand command, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalOperatorSummary> SetOperatorActiveAsync(Guid companyId, Guid operatorId, bool active, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalCertificateSummary> UploadCertificateAsync(Guid companyId, CertificateUpload upload, string actor, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FiscalCertificateSummary>> ListCertificatesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalCertificateSummary> GetCertificateAsync(Guid companyId, Guid certificateId, CancellationToken cancellationToken);
    Task<FiscalCertificateSummary> SetCertificateActiveAsync(Guid companyId, Guid certificateId, bool active, string actor, string correlationId, CancellationToken cancellationToken);
    Task<CompanyReadiness> GetReadinessAsync(Guid companyId, CancellationToken cancellationToken);
    Task<PagedResult<FiscalAuditSummary>> ListAuditAsync(Guid companyId, int page, int pageSize, string? action, string? actor, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<CompanyFiscalContext> ResolveContextAsync(Guid companyId, Guid businessUnitId, Guid deviceId, Guid operatorId, string actor, string correlationId, CancellationToken cancellationToken);
}
