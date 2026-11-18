namespace Summa.Fiscal.Api.Contracts;

public interface IApiResponse { ApiError? Error { get; } }

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    string CorrelationId) : IApiResponse
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
