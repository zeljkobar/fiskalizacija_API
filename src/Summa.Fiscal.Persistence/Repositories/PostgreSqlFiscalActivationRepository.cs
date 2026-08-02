using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Activation;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlFiscalActivationRepository(SummaFiscalDbContext dbContext)
    : IFiscalActivationRepository
{
    public async Task<FiscalActivationRecordSummary?> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalActivations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        return record is null ? null : Map(record);
    }

    public async Task<FiscalTestInvoiceEvidence?> GetTestInvoiceEvidenceAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.FiscalInvoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invoiceId && x.CompanyId == companyId &&
                                       x.Status == "Fiscalized" && x.Jikr != null && x.FiscalizedAt != null,
                cancellationToken);
        if (invoice is null) return null;
        var exchange = await dbContext.FiscalExchanges.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId && x.CompanyId == companyId &&
                        x.CompletedAt != null && x.HttpStatusCode >= 200 && x.HttpStatusCode < 300 &&
                        x.FaultCode == null && x.ResponseStoragePath != null)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var endpoint = exchange?.Endpoint ?? await dbContext.FiscalProfiles.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Environment == "Test" && x.IsActive)
            .Select(x => x.Endpoint)
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(endpoint) ? null : new(invoice.Id, companyId, invoice.InvoiceNumber,
            invoice.Jikr!, invoice.FiscalizedAt!.Value, endpoint);
    }

    public async Task<FiscalActivationRecordSummary> SaveTestPassedAsync(Guid companyId, FiscalTestInvoiceEvidence evidence, string configurationHash, string actor, CancellationToken cancellationToken)
    {
        var record = await GetOrCreateAsync(companyId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        record.Status = FiscalActivationStatuses.TestPassed;
        record.TestInvoiceId = evidence.InvoiceId;
        record.TestJikr = evidence.Jikr;
        record.TestConfigurationHash = configurationHash;
        record.TestPassedAt = now;
        record.TestPassedBy = actor;
        record.ProductionActivatedAt = null;
        record.ProductionActivatedBy = null;
        record.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(record);
    }

    public async Task<FiscalActivationRecordSummary> ActivateProductionAsync(Guid companyId, string productionEndpoint, string actor, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var company = await dbContext.Companies.Include(x => x.FiscalProfiles)
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        var record = await GetOrCreateAsync(companyId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var productionProfile = company.FiscalProfiles.SingleOrDefault(x => x.Environment == "Production")
            ?? throw new FiscalOnboardingException("PRODUCTION_PROFILE_NOT_FOUND", "Produkcioni fiskalni profil nije podešen.");
        if (!string.Equals(productionProfile.Endpoint.TrimEnd('/'), productionEndpoint.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new FiscalOnboardingException("PRODUCTION_ENDPOINT_MISMATCH", "Produkcioni profil nije vezan za odobreni PU endpoint.");
        company.ActiveEnvironment = "Production";
        company.UpdatedAt = now;
        record.Status = FiscalActivationStatuses.ProductionActive;
        record.ProductionActivatedAt = now;
        record.ProductionActivatedBy = actor;
        record.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(record);
    }

    public async Task<FiscalActivationRecordSummary> ReturnToTestAsync(Guid companyId, string testEndpoint, string actor, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var company = await dbContext.Companies.Include(x => x.FiscalProfiles)
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        var record = await GetOrCreateAsync(companyId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var testProfile = company.FiscalProfiles.SingleOrDefault(x => x.Environment == "Test")
            ?? throw new FiscalOnboardingException("TEST_PROFILE_NOT_FOUND", "Testni fiskalni profil nije podešen.");
        if (!string.Equals(testProfile.Endpoint.TrimEnd('/'), testEndpoint.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new FiscalOnboardingException("TEST_ENDPOINT_MISMATCH", "Testni profil nije vezan za odobreni PU endpoint.");
        company.ActiveEnvironment = "Test";
        company.UpdatedAt = now;
        record.Status = FiscalActivationStatuses.NotTested;
        record.TestInvoiceId = null; record.TestJikr = null; record.TestConfigurationHash = null;
        record.TestPassedAt = null; record.TestPassedBy = null;
        record.ProductionActivatedAt = null; record.ProductionActivatedBy = null;
        record.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Map(record);
    }

    public Task<bool> IsProductionActiveAsync(Guid companyId, CancellationToken cancellationToken) =>
        dbContext.FiscalActivations.AsNoTracking().AnyAsync(
            x => x.CompanyId == companyId && x.Status == FiscalActivationStatuses.ProductionActive,
            cancellationToken);

    private async Task<FiscalActivationRecord> GetOrCreateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalActivations.SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (record is not null) return record;
        record = new FiscalActivationRecord { CompanyId = companyId };
        dbContext.FiscalActivations.Add(record);
        return record;
    }

    private static FiscalActivationRecordSummary Map(FiscalActivationRecord x) => new(
        x.CompanyId, x.Status, x.TestInvoiceId, x.TestJikr, x.TestConfigurationHash,
        x.TestPassedAt, x.TestPassedBy, x.ProductionActivatedAt, x.ProductionActivatedBy);
}
