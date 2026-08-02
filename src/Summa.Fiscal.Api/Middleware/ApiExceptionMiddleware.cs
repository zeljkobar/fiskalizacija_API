using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;

namespace Summa.Fiscal.Api.Middleware;

public sealed class ApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (FiscalValidationException exception)
        {
            await WriteValidationErrorAsync(context, exception);
        }
        catch (FiscalInvoiceOperationException exception)
        {
            var correlationId = GetCorrelationId(context);
            await WriteAsync(context, exception.StatusCode, ApiResponse<object>.Fail(
                new(exception.Code, exception.Message, []), correlationId));
        }
        catch (FiscalOnboardingException exception)
        {
            var correlationId = GetCorrelationId(context);
            var statusCode = exception.Code.EndsWith("NOT_FOUND", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            await WriteAsync(context, statusCode, ApiResponse<object>.Fail(
                new(exception.Code, exception.Message, []), correlationId));
        }
        catch (Exception exception)
        {
            var correlationId = GetCorrelationId(context);
            logger.LogError(
                exception,
                "Neočekivana API greška. CorrelationId: {CorrelationId}",
                correlationId);

            var error = new ApiError(
                "INTERNAL_SERVER_ERROR",
                "Došlo je do neočekivane greške.",
                []);

            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail(error, correlationId));
        }
    }

    private static Task WriteValidationErrorAsync(
        HttpContext context,
        FiscalValidationException exception)
    {
        var correlationId = GetCorrelationId(context);
        var details = exception.Errors
            .Select(error => new ApiErrorDetail(error.Code, error.Field, error.Message))
            .ToArray();
        var apiError = new ApiError(
            "VALIDATION_ERROR",
            exception.Message,
            details);

        return WriteAsync(
            context,
            StatusCodes.Status400BadRequest,
            ApiResponse<object>.Fail(apiError, correlationId));
    }

    private static async Task WriteAsync<T>(HttpContext context, int statusCode, T payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(payload);
    }

    private static string GetCorrelationId(HttpContext context) =>
        context.Items[CorrelationIdMiddleware.ItemName]?.ToString()
        ?? context.TraceIdentifier;
}
