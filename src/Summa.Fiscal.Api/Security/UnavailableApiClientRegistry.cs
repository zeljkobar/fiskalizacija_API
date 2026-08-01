using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Api.Security;

public sealed class UnavailableApiClientRegistry : IApiClientRegistry
{
    public Task<AuthenticatedApiClient?> AuthenticateAsync(
        string clientId, string apiKey, CancellationToken cancellationToken) =>
        Task.FromResult<AuthenticatedApiClient?>(null);

    public Task<CreatedApiClient> CreateAsync(
        string name,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<Guid> companyIds,
        DateTimeOffset? expiresAt,
        string actor,
        string correlationId,
        CancellationToken cancellationToken) => throw NoDatabase();

    public Task<CreatedApiClient?> RotateKeyAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken) =>
        throw NoDatabase();

    public Task<bool> DeactivateAsync(Guid id, string actor, string correlationId, CancellationToken cancellationToken) =>
        throw NoDatabase();

    public Task<IReadOnlyCollection<ApiClientSummary>> ListAsync(CancellationToken cancellationToken) =>
        throw NoDatabase();

    private static InvalidOperationException NoDatabase() =>
        new("Administracija API klijenata zahtijeva PostgreSQL bazu.");
}
