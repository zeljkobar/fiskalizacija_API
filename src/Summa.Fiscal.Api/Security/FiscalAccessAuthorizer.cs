using System.Security.Claims;

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
            .Select(x => x.Value);
        var companies = principal.FindAll(ApiKeyAuthenticationHandler.CompanyClaim)
            .Select(x => x.Value);

        return permissions.Any(x => x == "*" || x == permission) &&
               companies.Any(x => x == "*" ||
                   Guid.TryParse(x, out var allowedCompanyId) && allowedCompanyId == companyId);
    }
}
