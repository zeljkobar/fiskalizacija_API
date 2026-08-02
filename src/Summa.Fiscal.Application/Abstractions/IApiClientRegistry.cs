namespace Summa.Fiscal.Application.Abstractions;

public static class FiscalApiPermissions
{
    public const string InvoicesCreate = "invoices:create";
    public const string InvoicesRead = "invoices:read";
    public const string InvoicesFiscalize = "invoices:fiscalize";
    public const string ClientsAdmin = "clients:admin";
    public const string PlatformAdmin = "platform:admin";
    public const string CompaniesRead = "companies:read";
    public const string CompaniesWrite = "companies:write";
    public const string ConfigurationRead = "configuration:read";
    public const string ConfigurationWrite = "configuration:write";
    public const string CertificatesRead = "certificates:read";
    public const string CertificatesManage = "certificates:manage";
    public const string AuditRead = "audit:read";
    public const string AlertsRead = "alerts:read";
    public const string AlertsManage = "alerts:manage";
    public const string ActivationRead = "activation:read";
    public const string ActivationTest = "activation:test";
    public const string ActivationProduction = "activation:production";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(
        [InvoicesCreate, InvoicesRead, InvoicesFiscalize, ClientsAdmin, PlatformAdmin,
         CompaniesRead, CompaniesWrite, ConfigurationRead, ConfigurationWrite,
         CertificatesRead, CertificatesManage, AuditRead, AlertsRead, AlertsManage,
         ActivationRead, ActivationTest, ActivationProduction],
        StringComparer.Ordinal);
}

public sealed record AuthenticatedApiClient(
    Guid Id,
    string ClientId,
    string Name,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<Guid> CompanyIds);

public sealed record ApiClientSummary(
    Guid Id,
    string ClientId,
    string Name,
    string KeyPrefix,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<Guid> CompanyIds,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt);

public sealed record CreatedApiClient(ApiClientSummary Client, string ApiKey);

public interface IApiClientRegistry
{
    Task<AuthenticatedApiClient?> AuthenticateAsync(
        string clientId,
        string apiKey,
        CancellationToken cancellationToken);

    Task<CreatedApiClient> CreateAsync(
        string name,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<Guid> companyIds,
        DateTimeOffset? expiresAt,
        string actor,
        string correlationId,
        CancellationToken cancellationToken);

    Task<CreatedApiClient?> RotateKeyAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ApiClientSummary>> ListAsync(CancellationToken cancellationToken);
}
