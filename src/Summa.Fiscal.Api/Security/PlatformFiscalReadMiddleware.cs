using Summa.Fiscal.Application.Invoices;

namespace Summa.Fiscal.Api.Security;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowPlatformFiscalReaderAttribute : Attribute;

public sealed class PlatformFiscalReadMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (AdminReadAccess.IsPlatformFiscalReader(context.User))
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.Request.Method) ||
                context.GetEndpoint()?.Metadata.GetMetadata<AllowPlatformFiscalReaderAttribute>() is null)
                throw new FiscalInvoiceOperationException("PLATFORM_FISCAL_READ_ONLY",
                    "Ključ je ograničen na odobrene fiskalne preglede.", StatusCodes.Status403Forbidden);
        }

        return next(context);
    }
}
