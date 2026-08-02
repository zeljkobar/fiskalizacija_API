using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Summa.Fiscal.Application.Onboarding;

namespace Summa.Fiscal.Application.Activation;

public sealed class FiscalActivationService(
    IFiscalActivationRepository repository,
    IFiscalOnboardingService onboarding,
    IFiscalOnboardingRepository onboardingRepository,
    FiscalActivationPolicy policy,
    TimeProvider timeProvider) : IFiscalActivationService
{
    public async Task<FiscalActivationStatus> GetStatusAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await onboarding.GetCompanyAsync(companyId, cancellationToken);
        var readiness = await onboarding.GetReadinessAsync(companyId, cancellationToken);
        var activation = await repository.GetAsync(companyId, cancellationToken);
        var currentHash = readiness.IsReady ? await ConfigurationHashAsync(company, cancellationToken) : null;
        var valid = activation?.TestPassedAt is { } passedAt &&
                    activation.TestConfigurationHash == currentHash &&
                    passedAt >= timeProvider.GetUtcNow().AddDays(-Math.Max(1, policy.TestValidityDays));
        var status = activation?.Status == FiscalActivationStatuses.TestPassed && !valid
            ? FiscalActivationStatuses.RetestRequired
            : activation?.Status ?? FiscalActivationStatuses.NotTested;
        return new(companyId, status,
            company.Environment, company.Endpoint, readiness.IsReady, valid,
            activation?.TestInvoiceId, activation?.TestJikr, activation?.TestPassedAt,
            activation?.TestPassedBy, activation?.ProductionActivatedAt,
            activation?.ProductionActivatedBy, readiness.Issues);
    }

    public async Task<FiscalActivationStatus> ConfirmSuccessfulTestAsync(Guid companyId, Guid invoiceId, string confirmation, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var company = await onboarding.GetCompanyAsync(companyId, cancellationToken);
        RequireConfirmation(confirmation, $"CONFIRM_TEST:{company.Tin}", "TEST_CONFIRMATION_INVALID");
        var readiness = await onboarding.GetReadinessAsync(companyId, cancellationToken);
        if (!readiness.IsReady) throw new FiscalOnboardingException("COMPANY_NOT_READY", "Firma nije spremna za kontrolni test.");
        if (!string.Equals(company.Environment, "Test", StringComparison.Ordinal))
            throw new FiscalOnboardingException("TEST_ENVIRONMENT_REQUIRED", "Kontrolni test se potvrđuje samo u Test okruženju.");
        if (!SameEndpoint(company.Endpoint, policy.TestEndpoint))
            throw new FiscalOnboardingException("TEST_ENDPOINT_INVALID", "Firma nije povezana sa kontrolisanim testnim PU endpointom.");

        var evidence = await repository.GetTestInvoiceEvidenceAsync(companyId, invoiceId, cancellationToken)
            ?? throw new FiscalOnboardingException("TEST_INVOICE_NOT_FISCALIZED", "Račun nije uspješno fiskalizovan za ovu firmu ili nema sačuvan PU odgovor.");
        if (!SameEndpoint(evidence.ExchangeEndpoint, policy.TestEndpoint))
            throw new FiscalOnboardingException("TEST_INVOICE_ENDPOINT_INVALID", "Kontrolni račun nije poslat na odobreni testni PU endpoint.");

        var hash = await ConfigurationHashAsync(company, cancellationToken);
        await repository.SaveTestPassedAsync(companyId, evidence, hash, actor, cancellationToken);
        await AuditAsync(companyId, "FISCAL_CONTROL_TEST_CONFIRMED", actor, correlationId,
            new { evidence.InvoiceId, evidence.InvoiceNumber, evidence.FiscalizedAt, ConfigurationHash = hash }, cancellationToken);
        return await GetStatusAsync(companyId, cancellationToken);
    }

    public async Task<FiscalActivationStatus> ActivateProductionAsync(Guid companyId, string confirmation, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var company = await onboarding.GetCompanyAsync(companyId, cancellationToken);
        RequireConfirmation(confirmation, $"ACTIVATE_PRODUCTION:{company.Tin}", "PRODUCTION_CONFIRMATION_INVALID");
        if (!Uri.TryCreate(policy.ProductionEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new FiscalOnboardingException("PRODUCTION_ENDPOINT_NOT_CONFIGURED", "Produkcioni PU endpoint nije bezbjedno konfigurisan na serveru.");
        var status = await GetStatusAsync(companyId, cancellationToken);
        if (!status.IsReady) throw new FiscalOnboardingException("COMPANY_NOT_READY", "Firma nije spremna za produkciju.");
        if (!status.IsTestValidForCurrentConfiguration)
            throw new FiscalOnboardingException("CONTROL_TEST_REQUIRED", "Uspješan kontrolni test za trenutnu konfiguraciju je obavezan.");
        var production = await onboarding.GetProductionProfileAsync(companyId, cancellationToken);
        if (!production.IsSoftwareCertified)
            throw new FiscalOnboardingException("SOFTWARE_NOT_CERTIFIED", "Produkcijska verzija softvera nije označena kao sertifikovana.");
        if (production.Device is null || production.Device.RegistrationStatus != "Registered" || string.IsNullOrWhiteSpace(production.Device.TcrCode))
            throw new FiscalOnboardingException("PRODUCTION_TCR_REQUIRED", "Prije aktivacije mora biti registrovan produkcioni ENU i dobijen TCRCode od Poreske uprave.");

        await repository.ActivateProductionAsync(companyId, policy.ProductionEndpoint, actor, cancellationToken);
        await AuditAsync(companyId, "FISCAL_PRODUCTION_ACTIVATED", actor, correlationId,
            new { Endpoint = policy.ProductionEndpoint, status.TestInvoiceId, status.TestPassedAt }, cancellationToken);
        return await GetStatusAsync(companyId, cancellationToken);
    }

    public async Task<FiscalActivationStatus> ReturnToTestAsync(Guid companyId, string confirmation, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var company = await onboarding.GetCompanyAsync(companyId, cancellationToken);
        RequireConfirmation(confirmation, $"RETURN_TO_TEST:{company.Tin}", "TEST_MODE_CONFIRMATION_INVALID");
        await repository.ReturnToTestAsync(companyId, policy.TestEndpoint, actor, cancellationToken);
        await AuditAsync(companyId, "FISCAL_RETURNED_TO_TEST", actor, correlationId,
            new { Endpoint = policy.TestEndpoint }, cancellationToken);
        return await GetStatusAsync(companyId, cancellationToken);
    }

    private async Task<string> ConfigurationHashAsync(CompanySummary company, CancellationToken cancellationToken)
    {
        var configuration = await onboardingRepository.GetConfigurationAsync(company.Id, cancellationToken);
        var certificates = await onboardingRepository.ListCertificatesAsync(company.Id, cancellationToken);
        var canonical = JsonSerializer.Serialize(new
        {
            company.Tin, company.LegalName, company.Address, company.Town, company.Country,
            company.Environment, company.Endpoint, company.SoftwareCode, company.MaintainerCode,
            Units = configuration.Units.Where(x => x.IsActive).OrderBy(x => x.Id),
            Devices = configuration.Devices.Where(x => x.IsActive).OrderBy(x => x.Id),
            Operators = configuration.Operators.Where(x => x.IsActive).OrderBy(x => x.Id),
            Certificates = certificates.Where(x => x.IsActive).Select(x => new { x.Id, x.Thumbprint, x.ValidTo }).OrderBy(x => x.Id)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private Task AuditAsync(Guid companyId, string action, string actor, string correlationId, object data, CancellationToken cancellationToken) =>
        onboardingRepository.AddAuditAsync(companyId, action, correlationId, actor, JsonSerializer.Serialize(data), cancellationToken);

    private static bool SameEndpoint(string left, string right) =>
        string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static void RequireConfirmation(string actual, string expected, string code)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new FiscalOnboardingException(code, $"Potvrda nije ispravna. Očekivana vrijednost je {expected}.");
    }
}
