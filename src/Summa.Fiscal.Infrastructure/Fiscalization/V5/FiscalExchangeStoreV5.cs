using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed class FiscalExchangeStorageOptionsV5
{
    public const string SectionName = "Fiscalization:ExchangeStorage";

    public string RootPath { get; init; } = "App_Data/FiscalExchanges";
}

public sealed record FiscalExchangeRequestV5(
    Guid ExchangeId,
    Guid? RequestUuid,
    string CorrelationId,
    Uri Endpoint,
    string SoapAction,
    DateTimeOffset CreatedAt,
    string RequestSha256);

public sealed record FiscalExchangeResponseV5(
    Guid ExchangeId,
    int HttpStatusCode,
    DateTimeOffset ReceivedAt,
    string ResponseSha256);

public sealed record FiscalExchangeFailureV5(
    Guid ExchangeId,
    DateTimeOffset FailedAt,
    string ExceptionType,
    string Message,
    IReadOnlyCollection<string> ExceptionChain);

public interface IFiscalExchangeStoreV5
{
    Task<Guid> SaveRequestAsync(
        string soapRequestXml,
        Guid? requestUuid,
        string correlationId,
        Uri endpoint,
        string soapAction,
        CancellationToken cancellationToken);

    Task SaveResponseAsync(
        Guid exchangeId,
        string soapResponseXml,
        int httpStatusCode,
        CancellationToken cancellationToken);

    Task SaveFailureAsync(
        Guid exchangeId,
        Exception exception,
        CancellationToken cancellationToken);
}

public sealed class FileFiscalExchangeStoreV5(
    FiscalExchangeStorageOptionsV5 options) : IFiscalExchangeStoreV5
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _rootPath = Path.GetFullPath(options.RootPath);

    public async Task<Guid> SaveRequestAsync(
        string soapRequestXml,
        Guid? requestUuid,
        string correlationId,
        Uri endpoint,
        string soapAction,
        CancellationToken cancellationToken)
    {
        var exchangeId = Guid.NewGuid();
        var directory = GetExchangeDirectory(exchangeId);
        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "request.xml"),
            soapRequestXml,
            new UTF8Encoding(false),
            cancellationToken);

        var metadata = new FiscalExchangeRequestV5(
            exchangeId,
            requestUuid,
            correlationId,
            endpoint,
            soapAction,
            DateTimeOffset.UtcNow,
            Sha256(soapRequestXml));
        await WriteJsonAsync(
            Path.Combine(directory, "request.metadata.json"),
            metadata,
            cancellationToken);

        return exchangeId;
    }

    public async Task SaveResponseAsync(
        Guid exchangeId,
        string soapResponseXml,
        int httpStatusCode,
        CancellationToken cancellationToken)
    {
        var directory = RequireExchangeDirectory(exchangeId);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "response.xml"),
            soapResponseXml,
            new UTF8Encoding(false),
            cancellationToken);

        var metadata = new FiscalExchangeResponseV5(
            exchangeId,
            httpStatusCode,
            DateTimeOffset.UtcNow,
            Sha256(soapResponseXml));
        await WriteJsonAsync(
            Path.Combine(directory, "response.metadata.json"),
            metadata,
            cancellationToken);
    }

    public Task SaveFailureAsync(
        Guid exchangeId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var directory = RequireExchangeDirectory(exchangeId);
        var failure = new FiscalExchangeFailureV5(
            exchangeId,
            DateTimeOffset.UtcNow,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            ReadExceptionChain(exception));
        return WriteJsonAsync(
            Path.Combine(directory, "failure.metadata.json"),
            failure,
            cancellationToken);
    }

    private string GetExchangeDirectory(Guid exchangeId) =>
        Path.Combine(_rootPath, exchangeId.ToString("N"));

    private string RequireExchangeDirectory(Guid exchangeId)
    {
        var directory = GetExchangeDirectory(exchangeId);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Fiskalna razmjena {exchangeId} nije prethodno evidentirana.");
        }

        return directory;
    }

    private static Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static IReadOnlyCollection<string> ReadExceptionChain(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(
                $"{current.GetType().FullName ?? current.GetType().Name}: {current.Message}");
        }

        return messages;
    }
}
