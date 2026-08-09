using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
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

public sealed record FiscalInvoiceExchangeEvidenceV5(
    Guid ExchangeId,
    Guid RequestUuid,
    string CorrelationId,
    Uri Endpoint,
    string SoapAction,
    DateTimeOffset CreatedAt,
    int HttpStatusCode,
    DateTimeOffset ReceivedAt,
    string Iic,
    string InvoiceNumber,
    decimal TotalPrice,
    string RequestXml,
    string ResponseXml);

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

    Task<FiscalInvoiceExchangeEvidenceV5?> ReadSuccessfulInvoiceAsync(
        Guid exchangeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FiscalInvoiceExchangeEvidenceV5>>
        FindSuccessfulInvoicesByIicAsync(
            string iic,
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

    public async Task<FiscalInvoiceExchangeEvidenceV5?> ReadSuccessfulInvoiceAsync(
        Guid exchangeId,
        CancellationToken cancellationToken)
    {
        var directory = GetExchangeDirectory(exchangeId);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var requestPath = Path.Combine(directory, "request.xml");
        var requestMetadataPath = Path.Combine(directory, "request.metadata.json");
        var responsePath = Path.Combine(directory, "response.xml");
        var responseMetadataPath = Path.Combine(directory, "response.metadata.json");
        if (!File.Exists(requestPath) || !File.Exists(requestMetadataPath) ||
            !File.Exists(responsePath) || !File.Exists(responseMetadataPath))
        {
            return null;
        }

        var requestXml = await File.ReadAllTextAsync(requestPath, cancellationToken);
        var responseXml = await File.ReadAllTextAsync(responsePath, cancellationToken);
        var requestMetadata = await ReadJsonAsync<FiscalExchangeRequestV5>(
            requestMetadataPath,
            cancellationToken);
        var responseMetadata = await ReadJsonAsync<FiscalExchangeResponseV5>(
            responseMetadataPath,
            cancellationToken);

        if (requestMetadata.ExchangeId != exchangeId ||
            responseMetadata.ExchangeId != exchangeId)
        {
            throw new InvalidDataException(
                $"Metapodaci fiskalne razmjene {exchangeId} ne odgovaraju direktorijumu.");
        }
        EnsureHash(requestXml, requestMetadata.RequestSha256, exchangeId, "request");
        EnsureHash(responseXml, responseMetadata.ResponseSha256, exchangeId, "response");
        if (responseMetadata.HttpStatusCode is < 200 or >= 300)
        {
            return null;
        }

        var requestDocument = XDocument.Parse(requestXml, LoadOptions.PreserveWhitespace);
        var header = requestDocument.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "RegisterInvoiceRequest")?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Header");
        var invoice = requestDocument.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Invoice");
        var fic = XDocument.Parse(responseXml, LoadOptions.PreserveWhitespace)
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "FIC")?
            .Value;
        if (header is null || invoice is null || string.IsNullOrWhiteSpace(fic))
        {
            return null;
        }

        var requestUuid = ParseGuid(
            header.Attribute("UUID")?.Value,
            "Header.UUID",
            exchangeId);
        if (requestMetadata.RequestUuid != requestUuid)
        {
            throw new InvalidDataException(
                $"Request UUID fiskalne razmjene {exchangeId} nije usklađen sa metapodacima.");
        }

        return new(
            exchangeId,
            requestUuid,
            requestMetadata.CorrelationId,
            requestMetadata.Endpoint,
            requestMetadata.SoapAction,
            requestMetadata.CreatedAt,
            responseMetadata.HttpStatusCode,
            responseMetadata.ReceivedAt,
            RequireAttribute(invoice, "IIC", exchangeId),
            RequireAttribute(invoice, "InvNum", exchangeId),
            ParseDecimal(
                RequireAttribute(invoice, "TotPrice", exchangeId),
                "Invoice.TotPrice",
                exchangeId),
            requestXml,
            responseXml);
    }

    public async Task<IReadOnlyCollection<FiscalInvoiceExchangeEvidenceV5>>
        FindSuccessfulInvoicesByIicAsync(
            string iic,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iic) || !Directory.Exists(_rootPath))
        {
            return [];
        }

        var matches = new List<FiscalInvoiceExchangeEvidenceV5>();
        foreach (var directory in Directory.EnumerateDirectories(_rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var exchangeId))
            {
                continue;
            }

            var requestPath = Path.Combine(directory, "request.xml");
            if (!File.Exists(requestPath))
            {
                continue;
            }

            var requestXml = await File.ReadAllTextAsync(requestPath, cancellationToken);
            var requestIic = XDocument.Parse(requestXml, LoadOptions.PreserveWhitespace)
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Invoice")?
                .Attribute("IIC")?
                .Value;
            if (!string.Equals(requestIic, iic, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var evidence = await ReadSuccessfulInvoiceAsync(exchangeId, cancellationToken);
            if (evidence is not null)
            {
                matches.Add(evidence);
            }
        }

        return matches.OrderBy(match => match.ReceivedAt).ToArray();
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

    private static async Task<T> ReadJsonAsync<T>(
        string path,
        CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<T>(
            await File.ReadAllTextAsync(path, cancellationToken))
        ?? throw new InvalidDataException($"Metapodaci {path} nijesu čitljivi.");

    private static void EnsureHash(
        string value,
        string expected,
        Guid exchangeId,
        string kind)
    {
        if (!string.Equals(Sha256(value), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SHA-256 provjera {kind} zapisa fiskalne razmjene {exchangeId} nije uspjela.");
        }
    }

    private static string RequireAttribute(
        XElement element,
        string attributeName,
        Guid exchangeId) =>
        string.IsNullOrWhiteSpace(element.Attribute(attributeName)?.Value)
            ? throw new InvalidDataException(
                $"Fiskalna razmjena {exchangeId} nema Invoice.{attributeName}.")
            : element.Attribute(attributeName)!.Value;

    private static Guid ParseGuid(string? value, string field, Guid exchangeId) =>
        Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidDataException(
                $"Fiskalna razmjena {exchangeId} ima neispravan {field}.");

    private static decimal ParseDecimal(string value, string field, Guid exchangeId) =>
        decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : throw new InvalidDataException(
                $"Fiskalna razmjena {exchangeId} ima neispravan {field}.");

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
