using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlApiClientRegistry(SummaFiscalDbContext dbContext)
    : IApiClientRegistry
{
    public async Task<AuthenticatedApiClient?> AuthenticateAsync(
        string clientId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.ApiClients
            .Include(x => x.CompanyAccesses)
            .SingleOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);

        if (record is null ||
            !record.IsActive ||
            (record.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow) ||
            !HashMatches(apiKey, record.ApiKeyHash))
        {
            return null;
        }

        record.LastUsedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = record.LastUsedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new(
            record.Id,
            record.ClientId,
            record.Name,
            ParsePermissions(record.Permissions).ToHashSet(StringComparer.Ordinal),
            record.CompanyAccesses.Select(x => x.CompanyId).ToHashSet());
    }

    public async Task<CreatedApiClient> CreateAsync(
        string name,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<Guid> companyIds,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        Validate(name, permissions, companyIds, expiresAt);
        var existingCompanies = await dbContext.Companies
            .Where(x => companyIds.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (existingCompanies.Count != companyIds.Distinct().Count())
            throw new InvalidOperationException("Jedna ili više firmi ne postoje ili nijesu aktivne.");

        var secret = GenerateSecret();
        var clientId = $"sfc_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var record = new ApiClientRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = name.Trim(),
            ApiKeyHash = Hash(secret),
            ApiKeyPrefix = secret[..12],
            Permissions = string.Join(',', permissions.Distinct(StringComparer.Ordinal).Order()),
            IsActive = true,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
            CompanyAccesses = existingCompanies.Select(companyId => new ApiClientCompanyAccessRecord
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList()
        };

        dbContext.ApiClients.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ToSummary(record), secret);
    }

    public async Task<CreatedApiClient?> RotateKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.ApiClients
            .Include(x => x.CompanyAccesses)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (record is null) return null;

        var secret = GenerateSecret();
        record.ApiKeyHash = Hash(secret);
        record.ApiKeyPrefix = secret[..12];
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ToSummary(record), secret);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.ApiClients.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (record is null) return false;
        record.IsActive = false;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<ApiClientSummary>> ListAsync(CancellationToken cancellationToken) =>
        (await Query().OrderBy(x => x.Name).ToListAsync(cancellationToken))
        .Select(ToSummary)
        .ToArray();

    private IQueryable<ApiClientRecord> Query() =>
        dbContext.ApiClients.AsNoTracking().Include(x => x.CompanyAccesses);

    private static ApiClientSummary ToSummary(ApiClientRecord record) => new(
        record.Id,
        record.ClientId,
        record.Name,
        record.ApiKeyPrefix,
        ParsePermissions(record.Permissions),
        record.CompanyAccesses.Select(x => x.CompanyId).ToArray(),
        record.IsActive,
        record.ExpiresAt,
        record.LastUsedAt,
        record.CreatedAt);

    private static void Validate(
        string name,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<Guid> companyIds,
        DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("Naziv aplikacije je obavezan i može imati najviše 200 znakova.");
        if (permissions.Count == 0 || permissions.Any(x => !FiscalApiPermissions.Allowed.Contains(x)))
            throw new ArgumentException("Dozvole aplikacije nijesu ispravne.");
        if (companyIds.Count == 0 || companyIds.Any(x => x == Guid.Empty))
            throw new ArgumentException("Aplikacija mora imati pristup najmanje jednoj firmi.");
        if (expiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Datum isteka mora biti u budućnosti.");
    }

    private static string GenerateSecret() =>
        $"sfa_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool HashMatches(string value, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(value) || expectedHash.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Hash(value)),
            Convert.FromHexString(expectedHash));
    }

    private static string[] ParsePermissions(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
