namespace Summa.Fiscal.Application.Abstractions;

public static class FiscalApiPermissions
{
    public const string InvoicesCreate = "invoices:create";
    public const string InvoicesRead = "invoices:read";
    public const string InvoicesFiscalize = "invoices:fiscalize";
    public const string ClientsAdmin = "clients:admin";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(
        [InvoicesCreate, InvoicesRead, InvoicesFiscalize, ClientsAdmin],
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
        CancellationToken cancellationToken);

    Task<CreatedApiClient?> RotateKeyAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ApiClientSummary>> ListAsync(CancellationToken cancellationToken);
}
