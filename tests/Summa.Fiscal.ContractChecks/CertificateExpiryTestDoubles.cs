using Summa.Fiscal.Application.Certificates;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeCertificateExpiryRepository(
    IReadOnlyCollection<CertificateExpirationSummary> certificates) : ICertificateExpiryRepository
{
    private readonly HashSet<(Guid CertificateId, int Threshold)> _alerts = [];
    public List<int> CreatedThresholds { get; } = [];

    public Task<IReadOnlyCollection<CertificateExpirationSummary>> ListExpiringAsync(int days, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(certificates);

    public Task<IReadOnlyCollection<CertificateExpiryAlertSummary>> ListAlertsAsync(Guid? companyId, bool includeAcknowledged, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<CertificateExpiryAlertSummary>>([]);

    public Task<bool> AlertExistsAsync(Guid certificateId, int thresholdDays, CancellationToken cancellationToken) =>
        Task.FromResult(_alerts.Contains((certificateId, thresholdDays)));

    public Task<CertificateExpiryAlertSummary> CreateAlertAsync(CertificateExpirationSummary certificate, int thresholdDays, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _alerts.Add((certificate.CertificateId, thresholdDays));
        CreatedThresholds.Add(thresholdDays);
        return Task.FromResult(new CertificateExpiryAlertSummary(Guid.NewGuid(), certificate.CertificateId,
            certificate.CompanyId, certificate.CompanyTin, certificate.CompanyName, certificate.Thumbprint,
            thresholdDays, certificate.ValidTo, now, false, null, null));
    }

    public Task<CertificateExpiryAlertSummary?> AcknowledgeAsync(Guid companyId, Guid alertId, string actor, string correlationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<CertificateExpiryAlertSummary?>(null);
}
