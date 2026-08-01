using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record PuFiscalTransportResultV5(
    Guid ExchangeId,
    HttpStatusCode StatusCode,
    RegisterInvoiceResponseV5 Response,
    string RawResponseXml);

public interface IPuFiscalSoapClientV5
{
    Task<PuFiscalTransportResultV5> RegisterInvoiceAsync(
        Uri endpoint,
        XDocument signedRequest,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class PuFiscalSoapClientV5(
    HttpClient httpClient,
    ISoapEnvelopeV5 envelope,
    IRegisterInvoiceResponseParserV5 responseParser,
    IFiscalExchangeStoreV5 exchangeStore) : IPuFiscalSoapClientV5
{
    public async Task<PuFiscalTransportResultV5> RegisterInvoiceAsync(
        Uri endpoint,
        XDocument signedRequest,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(signedRequest);

        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("PU endpoint mora koristiti HTTPS.");
        }

        var soapEnvelope = envelope.Wrap(signedRequest);
        var soapXml = soapEnvelope.ToString(SaveOptions.DisableFormatting);
        var requestUuid = TryReadRequestUuid(signedRequest);
        var exchangeId = await exchangeStore.SaveRequestAsync(
            soapXml,
            requestUuid,
            correlationId,
            endpoint,
            PuFiscalContractV5.RegisterInvoiceSoapAction,
            cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add(
            "SOAPAction",
            $"\"{PuFiscalContractV5.RegisterInvoiceSoapAction}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Content = new StringContent(
            soapXml,
            Encoding.UTF8,
            "text/xml");

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
            await exchangeStore.SaveResponseAsync(
                exchangeId,
                responseXml,
                (int)response.StatusCode,
                cancellationToken);
            var parsed = responseParser.Parse(responseXml);

            return new(exchangeId, response.StatusCode, parsed, responseXml);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await exchangeStore.SaveFailureAsync(
                exchangeId,
                exception,
                CancellationToken.None);
            throw;
        }
    }

    private static Guid? TryReadRequestUuid(XDocument signedRequest)
    {
        var header = signedRequest.Root?.Element(PuFiscalContractV5.Schema + "Header");
        return Guid.TryParse(header?.Attribute("UUID")?.Value, out var requestUuid)
            ? requestUuid
            : null;
    }
}
