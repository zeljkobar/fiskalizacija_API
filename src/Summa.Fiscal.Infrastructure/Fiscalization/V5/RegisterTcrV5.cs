using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record RegisterTcrRequestV5(Guid Uuid, DateTimeOffset SendDateTime, string IssuerTin,
    string BusinessUnitCode, string InternalCode, string SoftwareCode, string MaintainerCode, DateOnly ValidFrom);

public interface IRegisterTcrXmlBuilderV5 { XDocument BuildUnsigned(RegisterTcrRequestV5 request); }

public sealed class RegisterTcrXmlBuilderV5 : IRegisterTcrXmlBuilderV5
{
    public XDocument BuildUnsigned(RegisterTcrRequestV5 r)
    {
        var ns = PuFiscalContractV5.Schema;
        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "RegisterTCRRequest",
                new XAttribute("Id", PuFiscalContractV5.RequestId), new XAttribute("Version", PuFiscalContractV5.SchemaVersion),
                new XElement(ns + "Header", new XAttribute("UUID", r.Uuid),
                    new XAttribute("SendDateTime", r.SendDateTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))),
                new XElement(ns + "TCR", new XAttribute("IssuerTIN", r.IssuerTin),
                    new XAttribute("BusinUnitCode", r.BusinessUnitCode), new XAttribute("TCRIntID", r.InternalCode),
                    new XAttribute("SoftCode", r.SoftwareCode), new XAttribute("MaintainerCode", r.MaintainerCode),
                    new XAttribute("ValidFrom", r.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XAttribute("Type", "REGULAR")),
                new XElement(PuFiscalContractV5.XmlDsig + "Signature")));
    }
}

public sealed record RegisterTcrResponseV5(bool IsSuccess, string? TcrCode, PuSoapFaultV5? Fault);
public interface IRegisterTcrResponseParserV5 { RegisterTcrResponseV5 Parse(string soapXml); }
public sealed class RegisterTcrResponseParserV5 : IRegisterTcrResponseParserV5
{
    public RegisterTcrResponseV5 Parse(string soapXml)
    {
        var doc = XDocument.Parse(soapXml, LoadOptions.PreserveWhitespace);
        var soap = XNamespace.Get(PuFiscalContractV5.Soap11Namespace);
        var body = doc.Root?.Element(soap + "Body") ?? throw new InvalidOperationException("SOAP odgovor nema Body element.");
        var fault = body.Element(soap + "Fault");
        if (fault is not null)
        {
            string? Value(string name) => fault.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
            return new(false, null, new(Value("faultcode") ?? "SOAP_FAULT", Value("faultstring") ?? "PU je odbila ENU registraciju.", Value("detail")));
        }
        var response = body.Element(PuFiscalContractV5.Schema + "RegisterTCRResponse")
            ?? throw new InvalidOperationException("PU odgovor nema RegisterTCRResponse.");
        var code = response.Element(PuFiscalContractV5.Schema + "TCRCode")?.Value;
        return !string.IsNullOrWhiteSpace(code) ? new(true, code, null)
            : throw new InvalidOperationException("PU odgovor nema TCRCode.");
    }
}

public sealed record RegisterTcrTransportResultV5(Guid ExchangeId, HttpStatusCode StatusCode, RegisterTcrResponseV5 Response);

public sealed class PuTcrSoapClientV5(HttpClient httpClient, ISoapEnvelopeV5 envelope,
    IRegisterTcrResponseParserV5 parser, IFiscalExchangeStoreV5 exchangeStore)
{
    public async Task<RegisterTcrTransportResultV5> RegisterAsync(Uri endpoint, XDocument signedRequest, string correlationId, CancellationToken ct)
    {
        var soapXml = envelope.Wrap(signedRequest).ToString(SaveOptions.DisableFormatting);
        var uuidText = signedRequest.Root?.Element(PuFiscalContractV5.Schema + "Header")?.Attribute("UUID")?.Value;
        Guid? uuid = Guid.TryParse(uuidText, out var parsed) ? parsed : null;
        var exchangeId = await exchangeStore.SaveRequestAsync(soapXml, uuid, correlationId, endpoint, PuFiscalContractV5.RegisterTcrSoapAction, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("SOAPAction", $"\"{PuFiscalContractV5.RegisterTcrSoapAction}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Content = new StringContent(soapXml, Encoding.UTF8, "text/xml");
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var xml = await response.Content.ReadAsStringAsync(ct);
            await exchangeStore.SaveResponseAsync(exchangeId, xml, (int)response.StatusCode, ct);
            return new(exchangeId, response.StatusCode, parser.Parse(xml));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await exchangeStore.SaveFailureAsync(exchangeId, ex, CancellationToken.None); throw;
        }
    }
}
