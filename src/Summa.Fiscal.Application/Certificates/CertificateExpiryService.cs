namespace Summa.Fiscal.Application.Certificates;

using Summa.Fiscal.Application.Onboarding;

public sealed class CertificateExpiryService(
    ICertificateExpiryRepository repository,
    TimeProvider timeProvider) : ICertificateExpiryService
{
    private static readonly int[] Thresholds = [0, 7, 15, 30, 60];

    public Task<IReadOnlyCollection<CertificateExpirationSummary>> ListExpiringAsync(int days, CancellationToken cancellationToken)
    {
        if (days is < 0 or > 365) throw new FiscalOnboardingException("CERTIFICATE_EXPIRATION_DAYS_INVALID", "Broj dana mora biti između 0 i 365.");
        return repository.ListExpiringAsync(days, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<IReadOnlyCollection<CertificateExpiryAlertSummary>> ListAlertsAsync(Guid? companyId, bool includeAcknowledged, CancellationToken cancellationToken) =>
        repository.ListAlertsAsync(companyId, includeAcknowledged, cancellationToken);

    public async Task<CertificateExpiryScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var certificates = await repository.ListExpiringAsync(60, now, cancellationToken);
        var created = 0;
        foreach (var certificate in certificates)
        {
            var threshold = SelectThreshold(certificate.DaysRemaining);
            if (await repository.AlertExistsAsync(certificate.CertificateId, threshold, cancellationToken)) continue;
            await repository.CreateAlertAsync(certificate, threshold, now, cancellationToken);
            created++;
        }
        return new(certificates.Count, created, now);
    }

    public async Task<CertificateExpiryAlertSummary> AcknowledgeAsync(Guid companyId, Guid alertId, string actor, string correlationId, CancellationToken cancellationToken) =>
        await repository.AcknowledgeAsync(companyId, alertId, actor, correlationId, timeProvider.GetUtcNow(), cancellationToken)
        ?? throw new FiscalOnboardingException("CERTIFICATE_ALERT_NOT_FOUND", "Upozorenje ne postoji ili ne pripada firmi.");

    private static int SelectThreshold(int daysRemaining) =>
        Thresholds.FirstOrDefault(threshold => daysRemaining <= threshold, 60);
}
