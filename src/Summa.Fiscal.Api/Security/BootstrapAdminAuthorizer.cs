using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Api.Security;

public interface IBootstrapAdminAuthorizer
{
    AdminAccessDecision Authorize(HttpContext context, string permission, Guid? companyId = null);
}

public sealed record AdminAccessDecision(
    bool IsAllowed,
    bool IsAuthenticated,
    bool IsBootstrap,
    bool HasPlatformAccess,
    string Actor,
    IReadOnlySet<Guid> CompanyIds);

public sealed class BootstrapAdminAuthorizer(IOptions<ApiAccessOptions> options) : IBootstrapAdminAuthorizer
{
    public const string HeaderName = "X-Fiscal-Bootstrap-Key";
    public const string ActorIdHeaderName = "X-Fiscal-Actor-Id";
    public const string ActorNameHeaderName = "X-Fiscal-Actor-Name";

    public AdminAccessDecision Authorize(HttpContext context, string permission, Guid? companyId = null)
    {
        var request = context.Request;
        var expected = options.Value.BootstrapAdminKey;
        var supplied = request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(supplied) &&
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
        {
            return new(true, true, true, true, "bootstrap-admin", new HashSet<Guid>());
        }

        var principal = context.User;
        var clientId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(clientId) ||
            string.Equals(clientId, "development-client", StringComparison.Ordinal))
            return new(false, false, false, false, string.Empty, new HashSet<Guid>());

        var permissions = principal.FindAll(ApiKeyAuthenticationHandler.PermissionClaim).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var platform = permissions.Contains("*") || permissions.Contains(FiscalApiPermissions.PlatformAdmin);
        var companyIds = principal.FindAll(ApiKeyAuthenticationHandler.CompanyClaim)
            .Select(x => Guid.TryParse(x.Value, out var id) ? id : (Guid?)null)
            .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var permissionAllowed = platform || permissions.Contains(permission);
        var companyAllowed = companyId is null || platform || companyIds.Contains(companyId.Value);
        var actor = BuildActor(context, clientId, principal.Identity.Name);
        return new(permissionAllowed && companyAllowed, true, false, platform, actor, companyIds);
    }

    private static string BuildActor(HttpContext context, string clientId, string? clientName)
    {
        var applicationActor = $"api-client:{Clean(clientId, 60)}:{Clean(clientName, 60)}";
        var userId = Clean(context.Request.Headers[ActorIdHeaderName].FirstOrDefault(), 60);
        if (string.Equals(userId, "unknown", StringComparison.Ordinal))
            return applicationActor;

        var userName = Clean(context.Request.Headers[ActorNameHeaderName].FirstOrDefault(), 60);
        return $"{applicationActor};user:{userId}:{userName}"[..Math.Min(200, applicationActor.Length + userId.Length + userName.Length + 7)];
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var clean = new string(value.Trim().Where(x => !char.IsControl(x) && x != ';' && x != ':').ToArray());
        if (string.IsNullOrWhiteSpace(clean)) return "unknown";
        return clean[..Math.Min(maxLength, clean.Length)];
    }
}
