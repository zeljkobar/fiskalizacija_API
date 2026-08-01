namespace Summa.Fiscal.Application.Onboarding;

public sealed record CompanyOnboardingCommand(
    string Tin,
    string LegalName,
    string? ShortName,
    string? Address,
    string? Town,
    string Country,
    bool IsVatPayer,
    string Environment,
    string Endpoint,
    string SoftwareCode,
    string MaintainerCode);

public sealed record BusinessUnitCommand(string Code, string Name, string? Address, string? Town);

public sealed record FiscalDeviceCommand(Guid BusinessUnitId, string TcrCode, string InternalCode);

public sealed record FiscalOperatorCommand(string OperatorCode, string? FirstName, string? LastName);

public sealed record CompanySummary(
    Guid Id,
    string Tin,
    string LegalName,
    string? ShortName,
    string? Address,
    string? Town,
    string Country,
    bool IsVatPayer,
    bool IsActive,
    string Environment,
    string Endpoint,
    string SoftwareCode,
    string MaintainerCode);

public sealed record BusinessUnitSummary(
    Guid Id, Guid CompanyId, string Code, string Name, string? Address, string? Town, bool IsActive);

public sealed record FiscalDeviceSummary(
    Guid Id, Guid CompanyId, Guid BusinessUnitId, string TcrCode, string InternalCode, bool IsActive);

public sealed record FiscalOperatorSummary(
    Guid Id, Guid CompanyId, string OperatorCode, string? FirstName, string? LastName, bool IsActive);

public sealed record CertificateUpload(
    string FileName,
    byte[] PfxBytes,
    string Password);

public sealed record CertificateInspection(
    string Thumbprint,
    string SerialNumber,
    string Subject,
    string Issuer,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    bool HasPrivateKey,
    string? SubjectTin);

public sealed record FiscalCertificateSummary(
    Guid Id,
    Guid CompanyId,
    string FileName,
    string Thumbprint,
    string SerialNumber,
    string Subject,
    string Issuer,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    bool IsActive,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt);

public sealed record ReadinessIssue(string Code, string Message);

public sealed record CompanyReadiness(
    Guid CompanyId,
    bool IsReady,
    IReadOnlyCollection<ReadinessIssue> Issues,
    Guid? ActiveCertificateId);

public sealed record FiscalAuditSummary(
    Guid Id,
    Guid? CompanyId,
    string Action,
    string CorrelationId,
    string Actor,
    string DataJson,
    DateTimeOffset OccurredAt);

public sealed record PagedResult<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record CompanyFiscalContext(
    CompanySummary Company,
    BusinessUnitSummary BusinessUnit,
    FiscalDeviceSummary Device,
    FiscalOperatorSummary Operator,
    FiscalCertificateSummary Certificate,
    byte[] PfxBytes,
    string Password);

public sealed class FiscalOnboardingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
