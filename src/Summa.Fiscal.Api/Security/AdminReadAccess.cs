using System.Security.Claims;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;

namespace Summa.Fiscal.Api.Security;

public static class AdminReadAccess
{
    public static string? RegisteredClient(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return user.Identity is { IsAuthenticated: true, AuthenticationType: ApiKeyAuthenticationHandler.SchemeName } &&
            !string.IsNullOrWhiteSpace(id) && id != "development-client" ? id : null;
    }

    public static bool HasPermission(ClaimsPrincipal user, string permission) =>
        RegisteredClient(user) != null && user.HasClaim(ApiKeyAuthenticationHandler.PermissionClaim, permission);

    public static CompanyScope Scope(ClaimsPrincipal user) => new(HasPermission(user, FiscalApiPermissions.PlatformAdmin),
        RegisteredClient(user) == null ? [] : user.FindAll(ApiKeyAuthenticationHandler.CompanyClaim)
            .Select(x => Guid.TryParse(x.Value, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray());

    public static CompanyScope Require(ClaimsPrincipal user, Guid? company, params string[] permissions)
    {
        if (RegisteredClient(user) is null) throw new FiscalInvoiceOperationException("UNAUTHORIZED", "Potreban je važeći ključ registrovanog klijenta.", 401);
        var scope = Scope(user);
        if (permissions.Any(x => !HasPermission(user, x)) || company is Guid id && !scope.Allows(id))
            throw new FiscalInvoiceOperationException("COMPANY_ACCESS_DENIED", "Aplikacija nema dozvolu za ovu firmu ili operaciju.", 403);
        return scope;
    }
}
