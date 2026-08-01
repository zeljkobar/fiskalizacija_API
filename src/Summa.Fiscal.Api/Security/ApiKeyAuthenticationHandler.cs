using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiAccessOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiClientRegistry registry)
    : AuthenticationHandler<ApiAccessOptions>(options, logger, encoder)
{
    public const string SchemeName = "FiscalApiKey";
    public const string ClientIdHeader = "X-Fiscal-Client-Id";
    public const string ApiKeyHeader = "X-Fiscal-Api-Key";
    public const string PermissionClaim = "fiscal_permission";
    public const string CompanyClaim = "fiscal_company";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var clientId = Request.Headers[ClientIdHeader].FirstOrDefault();
        var apiKey = Request.Headers[ApiKeyHeader].FirstOrDefault();

        if (!Options.RequireApiKey &&
            string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(apiKey))
        {
            return SuccessPrincipal("development-client", "Development client", ["*"], ["*"]);
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.Fail("Nedostaju identitet aplikacije ili API ključ.");

        var client = await registry.AuthenticateAsync(clientId, apiKey, Context.RequestAborted);
        if (client is null)
            return AuthenticateResult.Fail("Identitet aplikacije ili API ključ nijesu ispravni.");

        return SuccessPrincipal(
            client.ClientId,
            client.Name,
            client.Permissions,
            client.CompanyIds.Select(x => x.ToString()));
    }

    private static AuthenticateResult SuccessPrincipal(
        string clientId,
        string name,
        IEnumerable<string> permissions,
        IEnumerable<string> companies)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, clientId),
            new(ClaimTypes.Name, name)
        };
        claims.AddRange(permissions.Select(x => new Claim(PermissionClaim, x)));
        claims.AddRange(companies.Select(x => new Claim(CompanyClaim, x)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
