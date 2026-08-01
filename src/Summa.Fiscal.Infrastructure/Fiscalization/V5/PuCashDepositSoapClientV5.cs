using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record PuCashDepositTransportResultV5(
    Guid ExchangeId,
    HttpStatusCode StatusCode,
    RegisterCashDepositResponseV5 Response,
    string RawResponseXml);

public interface IPuCashDepositSoapClientV5
{
    Task<PuCashDepositTransportResultV5> RegisterAsync(
        Uri endpoint,
        XDocument signedRequest,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class PuCashDepositSoapClientV5(
    HttpClient httpClient,
    ISoapEnvelopeV5 envelope,
    IRegisterCashDepositResponseParserV5 responseParser,
    IFiscalExchangeStoreV5 exchangeStore) : IPuCashDepositSoapClientV5
{
    public async Task<PuCashDepositTransportResultV5> RegisterAsync(
        Uri endpoint,
        XDocument signedRequest,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("PU endpoint mora koristiti HTTPS.");

        var soapXml = envelope
            .Wrap(signedRequest)
            .ToString(SaveOptions.DisableFormatting);
        var requestUuid = Guid.TryParse(
            signedRequest.Root?
                .Element(PuFiscalContractV5.Schema + "Header")?
                .Attribute("UUID")?
                .Value,
            out var parsedUuid)
            ? parsedUuid
            : (Guid?)null;
        var exchangeId = await exchangeStore.SaveRequestAsync(
            soapXml,
            requestUuid,
            correlationId,
            endpoint,
            PuFiscalContractV5.RegisterCashDepositSoapAction,
            cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add(
            "SOAPAction",
            $"\"{PuFiscalContractV5.RegisterCashDepositSoapAction}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Content = new StringContent(soapXml, Encoding.UTF8, "text/xml");

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
            return new(
                exchangeId,
                response.StatusCode,
                responseParser.Parse(responseXml),
                responseXml);
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
}
