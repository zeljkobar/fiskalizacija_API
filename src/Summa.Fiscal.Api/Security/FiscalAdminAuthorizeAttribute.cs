using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;

namespace Summa.Fiscal.Api.Security;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FiscalAdminAuthorizeAttribute : TypeFilterAttribute
{
    public FiscalAdminAuthorizeAttribute(string permission, string? companyRouteParameter = null)
        : base(typeof(FiscalAdminAuthorizationFilter))
    {
        Arguments = [permission, companyRouteParameter ?? string.Empty];
    }
}

public sealed class FiscalAdminAuthorizationFilter(
    string permission,
    string? companyRouteParameter,
    IBootstrapAdminAuthorizer authorizer) : IAsyncAuthorizationFilter
{
    public const string AccessItemName = "FiscalAdminAccess";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        Guid? companyId = null;
        if (!string.IsNullOrWhiteSpace(companyRouteParameter))
        {
            var routeValue = context.RouteData.Values[companyRouteParameter]?.ToString();
            if (!Guid.TryParse(routeValue, out var parsed))
            {
                context.Result = Error(context.HttpContext, StatusCodes.Status400BadRequest,
                    "INVALID_COMPANY_ID", "Identifikator firme nije ispravan.");
                return Task.CompletedTask;
            }
            companyId = parsed;
        }

        var access = authorizer.Authorize(context.HttpContext, permission, companyId);
        if (!access.IsAllowed)
        {
            context.Result = access.IsAuthenticated
                ? Error(context.HttpContext, StatusCodes.Status403Forbidden,
                    "ADMIN_PERMISSION_DENIED", "Klijent nema potrebnu dozvolu za ovu operaciju ili firmu.")
                : Error(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "ADMIN_AUTHENTICATION_REQUIRED", "Administratorski pristup nije odobren.");
            return Task.CompletedTask;
        }

        context.HttpContext.Items[AccessItemName] = access;
        return Task.CompletedTask;
    }

    private static ObjectResult Error(HttpContext context, int status, string code, string message)
    {
        var correlationId = context.Items[CorrelationIdMiddleware.ItemName]?.ToString() ?? context.TraceIdentifier;
        return new ObjectResult(ApiResponse<object>.Fail(new(code, message, []), correlationId))
        {
            StatusCode = status
        };
    }
}
