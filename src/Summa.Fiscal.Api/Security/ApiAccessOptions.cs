using Microsoft.AspNetCore.Authentication;

namespace Summa.Fiscal.Api.Security;

public sealed class ApiAccessOptions : AuthenticationSchemeOptions
{
    public const string SectionName = "ApiAccess";
    public bool RequireApiKey { get; init; } = true;
    public string BootstrapAdminKey { get; init; } = string.Empty;
}
