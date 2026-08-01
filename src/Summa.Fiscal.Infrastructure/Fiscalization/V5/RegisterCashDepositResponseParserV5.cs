using System.Globalization;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record RegisterCashDepositResponseV5(
    bool IsSuccess,
    string? Fcdc,
    Guid? ResponseUuid,
    Guid? RequestUuid,
    DateTimeOffset? SendDateTime,
    PuSoapFaultV5? Fault);

public interface IRegisterCashDepositResponseParserV5
{
    RegisterCashDepositResponseV5 Parse(string soapXml);
}

public sealed class RegisterCashDepositResponseParserV5
    : IRegisterCashDepositResponseParserV5
{
    private static readonly XNamespace Soap = PuFiscalContractV5.Soap11Namespace;
    private static readonly XNamespace Schema = PuFiscalContractV5.SchemaNamespace;

    public RegisterCashDepositResponseV5 Parse(string soapXml)
    {
        if (string.IsNullOrWhiteSpace(soapXml))
            throw new InvalidOperationException("PU je vratila prazan SOAP odgovor.");

        var document = XDocument.Parse(soapXml, LoadOptions.PreserveWhitespace);
        var body = document.Root?.Element(Soap + "Body")
            ?? throw new InvalidOperationException("SOAP odgovor nema Body element.");
        var fault = body.Element(Soap + "Fault");
        if (fault is not null)
        {
            return new(
                false,
                null,
                null,
                null,
                null,
                new(
                    ChildValue(fault, "faultcode") ?? "SOAP_FAULT",
                    ChildValue(fault, "faultstring") ?? "PU je vratila SOAP grešku.",
                    ChildValue(fault, "detail")));
        }

        var response = body.Element(Schema + "RegisterCashDepositResponse")
            ?? throw new InvalidOperationException(
                "SOAP odgovor ne sadrži RegisterCashDepositResponse.");
        var header = response.Element(Schema + "Header")
            ?? throw new InvalidOperationException("PU odgovor nema Header element.");
        var fcdc = response.Element(Schema + "FCDC")?.Value;
        if (string.IsNullOrWhiteSpace(fcdc))
            throw new InvalidOperationException("PU odgovor nema FCDC.");

        return new(
            true,
            fcdc,
            ParseGuid(header.Attribute("UUID")?.Value, "UUID"),
            ParseGuid(header.Attribute("RequestUUID")?.Value, "RequestUUID"),
            ParseDateTime(header.Attribute("SendDateTime")?.Value),
            null);
    }

    private static string? ChildValue(XElement parent, string localName)
    {
        var element = parent.Elements().FirstOrDefault(
            candidate => candidate.Name.LocalName == localName);
        return element is null
            ? null
            : localName == "detail"
                ? string.Concat(element.Nodes())
                : element.Value;
    }

    private static Guid ParseGuid(string? value, string name) =>
        Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"PU odgovor ima neispravan {name}.");

    private static DateTimeOffset ParseDateTime(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                "PU odgovor ima neispravan Header.SendDateTime.");
}
