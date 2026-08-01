using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Certificates;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlCertificateExpiryRepository(SummaFiscalDbContext dbContext)
    : ICertificateExpiryRepository
{
    public async Task<IReadOnlyCollection<CertificateExpirationSummary>> ListExpiringAsync(int days, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var limit = now.AddDays(days);
        var records = await dbContext.FiscalCertificates.AsNoTracking()
            .Include(x => x.Company)
            .Where(x => x.IsActive && x.Company.IsActive && x.ValidTo <= limit)
            .OrderBy(x => x.ValidTo)
            .ToArrayAsync(cancellationToken);
        return records.Select(x => new CertificateExpirationSummary(
            x.Id, x.CompanyId, x.Company.Tin, x.Company.LegalName, x.FileName,
            x.Thumbprint, x.ValidTo, (int)Math.Ceiling((x.ValidTo - now).TotalDays), x.ValidTo <= now)).ToArray();
    }

    public async Task<IReadOnlyCollection<CertificateExpiryAlertSummary>> ListAlertsAsync(Guid? companyId, bool includeAcknowledged, CancellationToken cancellationToken)
    {
        var query = dbContext.FiscalCertificateAlerts.AsNoTracking()
            .Include(x => x.Company).Include(x => x.Certificate).AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (!includeAcknowledged) query = query.Where(x => !x.IsAcknowledged);
        var records = await query.OrderByDescending(x => x.CreatedAt).ToArrayAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public Task<bool> AlertExistsAsync(Guid certificateId, int thresholdDays, CancellationToken cancellationToken) =>
        dbContext.FiscalCertificateAlerts.AnyAsync(x => x.CertificateId == certificateId && x.ThresholdDays == thresholdDays, cancellationToken);

    public async Task<CertificateExpiryAlertSummary> CreateAlertAsync(CertificateExpirationSummary certificate, int thresholdDays, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var record = new FiscalCertificateAlertRecord
        {
            CertificateId = certificate.CertificateId,
            CompanyId = certificate.CompanyId,
            ThresholdDays = thresholdDays,
            CertificateValidTo = certificate.ValidTo,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.FiscalCertificateAlerts.Add(record);
        dbContext.FiscalAudits.Add(new FiscalAuditRecord
        {
            CompanyId = certificate.CompanyId,
            Action = "CERTIFICATE_EXPIRY_ALERT_CREATED",
            Actor = "certificate-expiry-worker",
            CorrelationId = $"certificate-alert:{record.Id:N}",
            DataJson = JsonSerializer.Serialize(new { certificate.CertificateId, certificate.Thumbprint, thresholdDays, certificate.ValidTo }),
            CreatedAt = now,
            UpdatedAt = now
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.FiscalCertificateAlerts.AsNoTracking()
                .Include(x => x.Company).Include(x => x.Certificate)
                .SingleAsync(x => x.CertificateId == certificate.CertificateId && x.ThresholdDays == thresholdDays, cancellationToken);
            return Map(existing);
        }
        return new(record.Id, record.CertificateId, record.CompanyId, certificate.CompanyTin,
            certificate.CompanyName, certificate.Thumbprint, thresholdDays, certificate.ValidTo,
            record.CreatedAt, false, null, null);
    }

    public async Task<CertificateExpiryAlertSummary?> AcknowledgeAsync(Guid companyId, Guid alertId, string actor, string correlationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalCertificateAlerts.Include(x => x.Company).Include(x => x.Certificate)
            .SingleOrDefaultAsync(x => x.Id == alertId && x.CompanyId == companyId, cancellationToken);
        if (record is null) return null;
        if (!record.IsAcknowledged)
        {
            record.IsAcknowledged = true; record.AcknowledgedAt = now; record.AcknowledgedBy = actor; record.UpdatedAt = now;
            dbContext.FiscalAudits.Add(new FiscalAuditRecord
            {
                CompanyId = companyId, Action = "CERTIFICATE_EXPIRY_ALERT_ACKNOWLEDGED",
                Actor = actor, CorrelationId = correlationId,
                DataJson = JsonSerializer.Serialize(new { alertId, record.CertificateId, record.ThresholdDays }),
                CreatedAt = now, UpdatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return Map(record);
    }

    private static CertificateExpiryAlertSummary Map(FiscalCertificateAlertRecord x) => new(
        x.Id, x.CertificateId, x.CompanyId, x.Company.Tin, x.Company.LegalName,
        x.Certificate.Thumbprint, x.ThresholdDays, x.CertificateValidTo, x.CreatedAt,
        x.IsAcknowledged, x.AcknowledgedAt, x.AcknowledgedBy);
}
