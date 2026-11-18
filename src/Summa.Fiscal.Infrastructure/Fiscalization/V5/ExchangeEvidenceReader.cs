using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Summa.Fiscal.Application.Admin;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public static class ExchangeEvidenceReader
{
    public const int MaxEvidenceBytes = 16 * 1024 * 1024;
    private static readonly XNamespace Soap = PuFiscalContractV5.Soap11Namespace;
    private static readonly XNamespace Fiscal = PuFiscalContractV5.Schema;
    public static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    public static ExchangeOperation Operation(string action) => action switch
    {
        PuFiscalContractV5.RegisterInvoiceSoapAction => ExchangeOperation.RegisterInvoice,
        PuFiscalContractV5.RegisterTcrSoapAction => ExchangeOperation.RegisterTcr,
        PuFiscalContractV5.RegisterCashDepositSoapAction => ExchangeOperation.RegisterCashDeposit,
        _ => ExchangeOperation.Other
    };
    public static XElement Payload(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxEvidenceBytes });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Prazna fiskalna poruka.");
        if (root.Name.Namespace == Fiscal) return root;
        if (root.Name != Soap + "Envelope") throw new InvalidDataException("Nepoznat omotač fiskalne poruke.");
        return root.Element(Soap + "Body")?.Elements().SingleOrDefault() ?? throw new InvalidDataException("Nepotpuna fiskalna poruka.");
    }
    private static string RootName(ExchangeOperation operation) => operation == ExchangeOperation.RegisterTcr ? "RegisterTCR" : operation.ToString();
    public sealed record InvoiceEvidence(Guid Uuid, string Iic, string Tin, string Number, DateTimeOffset IssuedAt, decimal Total,
        string Unit, string Tcr, string Operator, string Software);
    public static Guid ValidateRequest(string xml, ExchangeOperation operation)
    {
        var payload = Payload(xml);
        if (operation == ExchangeOperation.Other || payload.Name != Fiscal + (RootName(operation) + "Request") ||
            !Guid.TryParse((string?)payload.Element(Fiscal + "Header")?.Attribute("UUID"), out var uuid))
            throw new InvalidDataException("Tip ili UUID zahtjeva nije ispravan.");
        return uuid;
    }
    public static InvoiceEvidence Invoice(string xml)
    {
        var payload = Payload(xml);
        var uuid = ValidateRequest(xml, ExchangeOperation.RegisterInvoice);
        var invoice = payload.Element(Fiscal + "Invoice") ?? throw new InvalidDataException("Nema računa.");
        string Required(string name) => (string?)invoice.Attribute(name) ?? throw new InvalidDataException("Nedostaje fiskalni atribut.");
        var tin = (string?)invoice.Element(Fiscal + "Seller")?.Attribute("IDNum") ?? throw new InvalidDataException("Nema PIB-a izdavaoca.");
        return new(uuid, Required("IIC"), tin, Required("InvNum"),
            DateTimeOffset.Parse(Required("IssueDateTime"), CultureInfo.InvariantCulture).ToUniversalTime(),
            decimal.Parse(Required("TotPrice"), CultureInfo.InvariantCulture), Required("BusinUnitCode"), Required("TCRCode"), Required("OperatorCode"), Required("SoftCode"));
    }
    public static ExchangeFinish Response(Guid exchangeId, ExchangeOperation op, Guid? requestUuid, string xml, int status, DateTimeOffset time)
    {
        var hash = Hash(xml);
        ExchangeFinish Unknown() => new(exchangeId, ExchangeOutcome.Unknown, time, status, hash, null, "UNVERIFIED_PU_RESPONSE");
        try
        {
            var payload = Payload(xml);
            if (payload.Name == Soap + "Fault")
            {
                var code = payload.Elements().FirstOrDefault(x => x.Name.LocalName == "faultcode")?.Value;
                return code is null ? Unknown() : new(exchangeId, ExchangeOutcome.Rejected, time, status, hash, null,
                    AdminFormatting.SafeCode(code, "SOAP_FAULT"));
            }
            if (op == ExchangeOperation.Other || payload.Name != Fiscal + (RootName(op) + "Response") ||
                !Guid.TryParse((string?)payload.Element(Fiscal + "Header")?.Attribute("RequestUUID"), out var echoed) ||
                echoed != requestUuid || status is < 200 or > 299) return Unknown();
            var id = payload.Element(Fiscal + (op switch
            { ExchangeOperation.RegisterInvoice => "FIC", ExchangeOperation.RegisterTcr => "TCRCode", _ => "FCDC" }))?.Value;
            if (AdminFormatting.SafeCode(id, "") == "" || op == ExchangeOperation.RegisterInvoice && !Guid.TryParse(id, out _)) return Unknown();
            return new(exchangeId, ExchangeOutcome.Accepted, time, status, hash, id, null);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or InvalidOperationException) { return Unknown(); }
    }
}
