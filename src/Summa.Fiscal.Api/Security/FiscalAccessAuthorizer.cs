using System.Security.Claims;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Api.Security;

public interface IFiscalAccessAuthorizer
{
    bool HasAccess(ClaimsPrincipal principal, string permission, Guid companyId);
}

public sealed class FiscalAccessAuthorizer : IFiscalAccessAuthorizer
{
    public bool HasAccess(ClaimsPrincipal principal, string permission, Guid companyId)
    {
        var permissions = principal.FindAll(ApiKeyAuthenticationHandler.PermissionClaim)
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);
        var companies = principal.FindAll(ApiKeyAuthenticationHandler.CompanyClaim)
            .Select(x => x.Value);

        var hasPermission = permissions.Contains("*") || permissions.Contains(permission);
        if (!hasPermission)
        {
            return false;
        }

        // Centralni platformski klijent upravlja svim sadašnjim i budućim firmama.
        // I dalje mora imati konkretnu dozvolu za samu fiskalnu operaciju.
        if (permissions.Contains("*") || permissions.Contains(FiscalApiPermissions.PlatformAdmin))
        {
            return true;
        }

        return companies.Any(x => x == "*" ||
            Guid.TryParse(x, out var allowedCompanyId) && allowedCompanyId == companyId);
    }
}
