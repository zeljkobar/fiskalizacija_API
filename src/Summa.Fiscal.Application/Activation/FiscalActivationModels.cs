namespace Summa.Fiscal.Application.Activation;

public static class FiscalActivationStatuses
{
    public const string NotTested = "NotTested";
    public const string TestPassed = "TestPassed";
    public const string RetestRequired = "RetestRequired";
    public const string ProductionActive = "ProductionActive";
}

public sealed class FiscalActivationPolicy
{
    public const string SectionName = "Fiscalization:Activation";
    public string TestEndpoint { get; init; } = "https://efitest.tax.gov.me/fs-v1";
    public string ProductionEndpoint { get; init; } = string.Empty;
    public int TestValidityDays { get; init; } = 30;
}

public sealed record FiscalActivationRecordSummary(
    Guid CompanyId,
    string Status,
    Guid? TestInvoiceId,
    string? TestJikr,
    string? TestConfigurationHash,
    DateTimeOffset? TestPassedAt,
    string? TestPassedBy,
    DateTimeOffset? ProductionActivatedAt,
    string? ProductionActivatedBy);

public sealed record FiscalTestInvoiceEvidence(
    Guid InvoiceId,
    Guid CompanyId,
    string InvoiceNumber,
    string Jikr,
    DateTimeOffset FiscalizedAt,
    string ExchangeEndpoint);

public sealed record FiscalActivationStatus(
    Guid CompanyId,
    string Status,
    string Environment,
    string Endpoint,
    bool IsReady,
    bool IsTestValidForCurrentConfiguration,
    Guid? TestInvoiceId,
    string? TestJikr,
    DateTimeOffset? TestPassedAt,
    string? TestPassedBy,
    DateTimeOffset? ProductionActivatedAt,
    string? ProductionActivatedBy,
    IReadOnlyCollection<Onboarding.ReadinessIssue> Issues);

public interface IFiscalActivationRepository
{
    Task<FiscalActivationRecordSummary?> GetAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalTestInvoiceEvidence?> GetTestInvoiceEvidenceAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken);
    Task<FiscalActivationRecordSummary> SaveTestPassedAsync(Guid companyId, FiscalTestInvoiceEvidence evidence, string configurationHash, string actor, CancellationToken cancellationToken);
    Task<FiscalActivationRecordSummary> ActivateProductionAsync(Guid companyId, string productionEndpoint, string actor, CancellationToken cancellationToken);
    Task<FiscalActivationRecordSummary> ReturnToTestAsync(Guid companyId, string testEndpoint, string actor, CancellationToken cancellationToken);
    Task<bool> IsProductionActiveAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface IFiscalActivationService
{
    Task<FiscalActivationStatus> GetStatusAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FiscalActivationStatus> ConfirmSuccessfulTestAsync(Guid companyId, Guid invoiceId, string confirmation, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalActivationStatus> ActivateProductionAsync(Guid companyId, string confirmation, string actor, string correlationId, CancellationToken cancellationToken);
    Task<FiscalActivationStatus> ReturnToTestAsync(Guid companyId, string confirmation, string actor, string correlationId, CancellationToken cancellationToken);
}
