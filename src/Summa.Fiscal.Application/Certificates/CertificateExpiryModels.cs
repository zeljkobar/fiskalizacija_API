namespace Summa.Fiscal.Application.Certificates;

public sealed record CertificateExpirationSummary(
    Guid CertificateId,
    Guid CompanyId,
    string CompanyTin,
    string CompanyName,
    string FileName,
    string Thumbprint,
    DateTimeOffset ValidTo,
    int DaysRemaining,
    bool IsExpired);

public sealed record CertificateExpiryAlertSummary(
    Guid Id,
    Guid CertificateId,
    Guid CompanyId,
    string CompanyTin,
    string CompanyName,
    string Thumbprint,
    int ThresholdDays,
    DateTimeOffset CertificateValidTo,
    DateTimeOffset CreatedAt,
    bool IsAcknowledged,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy);

public sealed record CertificateExpiryScanResult(
    int CertificatesChecked,
    int AlertsCreated,
    DateTimeOffset ScannedAt);

public interface ICertificateExpiryRepository
{
    Task<IReadOnlyCollection<CertificateExpirationSummary>> ListExpiringAsync(int days, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CertificateExpiryAlertSummary>> ListAlertsAsync(Guid? companyId, bool includeAcknowledged, CancellationToken cancellationToken);
    Task<bool> AlertExistsAsync(Guid certificateId, int thresholdDays, CancellationToken cancellationToken);
    Task<CertificateExpiryAlertSummary> CreateAlertAsync(CertificateExpirationSummary certificate, int thresholdDays, DateTimeOffset now, CancellationToken cancellationToken);
    Task<CertificateExpiryAlertSummary?> AcknowledgeAsync(Guid companyId, Guid alertId, string actor, string correlationId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ICertificateExpiryService
{
    Task<IReadOnlyCollection<CertificateExpirationSummary>> ListExpiringAsync(int days, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CertificateExpiryAlertSummary>> ListAlertsAsync(Guid? companyId, bool includeAcknowledged, CancellationToken cancellationToken);
    Task<CertificateExpiryScanResult> ScanAsync(CancellationToken cancellationToken);
    Task<CertificateExpiryAlertSummary> AcknowledgeAsync(Guid companyId, Guid alertId, string actor, string correlationId, CancellationToken cancellationToken);
}
