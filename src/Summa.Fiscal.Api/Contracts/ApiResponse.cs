namespace Summa.Fiscal.Api.Contracts;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    string CorrelationId)
{
    public static ApiResponse<T> Ok(T data, string correlationId) =>
        new(true, data, null, correlationId);

    public static ApiResponse<T> Fail(ApiError error, string correlationId) =>
        new(false, default, error, correlationId);
}

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyCollection<ApiErrorDetail> Details);

public sealed record ApiErrorDetail(string Code, string? Field, string Message);
