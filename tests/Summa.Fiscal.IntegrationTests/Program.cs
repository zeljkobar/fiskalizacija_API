using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;

var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var otherCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
var authorizer = new BootstrapAdminAuthorizer(Options.Create(new ApiAccessOptions
{
    BootstrapAdminKey = "integration-bootstrap"
}));

var scoped = Context("client-1", "Admin portal",
    [FiscalApiPermissions.CompaniesRead, FiscalApiPermissions.ActivationRead], [companyId], "admin-42", "Security Check");
Assert(authorizer.Authorize(scoped, FiscalApiPermissions.CompaniesRead, companyId).IsAllowed,
    "Klijent mora imati dozvolu za dodijeljenu firmu.");
Assert(!authorizer.Authorize(scoped, FiscalApiPermissions.CompaniesRead, otherCompanyId).IsAllowed,
    "Klijent ne smije pristupiti drugoj firmi.");
Assert(!authorizer.Authorize(scoped, FiscalApiPermissions.CertificatesManage, companyId).IsAllowed,
    "Klijent ne smije koristiti nedodijeljenu dozvolu.");
Assert(authorizer.Authorize(scoped, FiscalApiPermissions.ActivationRead, companyId).IsAllowed,
    "Klijent mora moći čitati activation status dodijeljene firme.");
Assert(!authorizer.Authorize(scoped, FiscalApiPermissions.ActivationProduction, companyId).IsAllowed,
    "Produkcijska aktivacija mora zahtijevati posebnu dozvolu.");

var access = authorizer.Authorize(scoped, FiscalApiPermissions.CompaniesRead, companyId);
Assert(access.Actor.Contains("api-client:client-1:Admin portal", StringComparison.Ordinal) &&
       access.Actor.Contains("user:admin-42:Security Check", StringComparison.Ordinal),
    "Audit identitet mora sadržati aplikaciju i administratora.");

var development = Context("development-client", "Development", ["*"], [companyId], null, null);
Assert(!authorizer.Authorize(development, FiscalApiPermissions.CompaniesRead, companyId).IsAuthenticated,
    "Development klijent ne smije otvoriti administratorske rute.");

var platform = Context("platform-1", "Platform", [FiscalApiPermissions.PlatformAdmin], [], null, null);
Assert(authorizer.Authorize(platform, FiscalApiPermissions.CertificatesManage, otherCompanyId).IsAllowed,
    "Platform admin mora imati globalni pristup.");

var bootstrap = new DefaultHttpContext();
bootstrap.Request.Headers[BootstrapAdminAuthorizer.HeaderName] = "integration-bootstrap";
Assert(authorizer.Authorize(bootstrap, FiscalApiPermissions.ClientsAdmin).IsBootstrap,
    "Bootstrap pristup mora ostati dostupan za inicijalizaciju i oporavak.");

Console.WriteLine("Granularna administratorska autorizacija i tenant izolacija su provjerene.");

static DefaultHttpContext Context(
    string clientId,
    string clientName,
    IReadOnlyCollection<string> permissions,
    IReadOnlyCollection<Guid> companyIds,
    string? actorId,
    string? actorName)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, clientId),
        new(ClaimTypes.Name, clientName)
    };
    claims.AddRange(permissions.Select(x => new Claim(ApiKeyAuthenticationHandler.PermissionClaim, x)));
    claims.AddRange(companyIds.Select(x => new Claim(ApiKeyAuthenticationHandler.CompanyClaim, x.ToString())));

    var context = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"))
    };
    if (actorId is not null) context.Request.Headers[BootstrapAdminAuthorizer.ActorIdHeaderName] = actorId;
    if (actorName is not null) context.Request.Headers[BootstrapAdminAuthorizer.ActorNameHeaderName] = actorName;
    return context;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
