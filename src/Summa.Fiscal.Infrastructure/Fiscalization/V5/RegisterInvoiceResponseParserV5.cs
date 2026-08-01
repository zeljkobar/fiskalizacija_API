using System.Globalization;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record RegisterInvoiceResponseV5(
    bool IsSuccess,
    string? Fic,
    Guid? ResponseUuid,
    Guid? RequestUuid,
    DateTimeOffset? SendDateTime,
    PuSoapFaultV5? Fault);

public sealed record PuSoapFaultV5(
    string Code,
    string Message,
    string? Detail);

public interface IRegisterInvoiceResponseParserV5
{
    RegisterInvoiceResponseV5 Parse(string soapXml);
}

public sealed class RegisterInvoiceResponseParserV5 : IRegisterInvoiceResponseParserV5
{
    private static readonly XNamespace Soap = PuFiscalContractV5.Soap11Namespace;
    private static readonly XNamespace Schema = PuFiscalContractV5.SchemaNamespace;

    public RegisterInvoiceResponseV5 Parse(string soapXml)
    {
        if (string.IsNullOrWhiteSpace(soapXml))
        {
            throw new InvalidOperationException("PU je vratila prazan SOAP odgovor.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(soapXml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException)
        {
            throw new InvalidOperationException("PU odgovor nije ispravan XML.", exception);
        }

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
                    Value(fault, "faultcode") ?? "SOAP_FAULT",
                    Value(fault, "faultstring") ?? "PU je vratila SOAP grešku.",
                    Value(fault, "detail")));
        }

        var response = body.Element(Schema + "RegisterInvoiceResponse")
            ?? throw new InvalidOperationException(
                "SOAP odgovor ne sadrži RegisterInvoiceResponse niti SOAP Fault.");
        var header = response.Element(Schema + "Header")
            ?? throw new InvalidOperationException("PU odgovor nema Header element.");
        var fic = response.Element(Schema + "FIC")?.Value;

        if (string.IsNullOrWhiteSpace(fic))
        {
            throw new InvalidOperationException("PU odgovor nema FIC/JIKR.");
        }

        return new(
            true,
            fic,
            ParseGuid(header.Attribute("UUID")?.Value, "UUID"),
            ParseGuid(header.Attribute("RequestUUID")?.Value, "RequestUUID"),
            ParseDateTime(header.Attribute("SendDateTime")?.Value),
            null);
    }

    private static string? Value(XElement parent, string localName)
    {
        var element = parent.Elements().FirstOrDefault(
            candidate => candidate.Name.LocalName == localName);
        if (element is null)
        {
            return null;
        }

        return localName == "detail"
            ? string.Concat(element.Nodes())
            : element.Value;
    }

    private static Guid ParseGuid(string? value, string fieldName) =>
        Guid.TryParse(value, out var result)
            ? result
            : throw new InvalidOperationException($"PU odgovor ima neispravan {fieldName}.");

    private static DateTimeOffset ParseDateTime(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var result)
            ? result
            : throw new InvalidOperationException(
                "PU odgovor ima neispravan Header.SendDateTime.");
}
