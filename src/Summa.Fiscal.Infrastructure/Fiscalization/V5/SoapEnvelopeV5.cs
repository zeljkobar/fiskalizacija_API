using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public interface ISoapEnvelopeV5
{
    XDocument Wrap(XDocument signedRequest);
}

public sealed class SoapEnvelopeV5 : ISoapEnvelopeV5
{
    public XDocument Wrap(XDocument signedRequest)
    {
        ArgumentNullException.ThrowIfNull(signedRequest);

        var requestRoot = signedRequest.Root
            ?? throw new InvalidOperationException("Potpisani PU zahtjev nema root element.");

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                XNamespace.Get(PuFiscalContractV5.Soap11Namespace) + "Envelope",
                new XAttribute(
                    XNamespace.Xmlns + "soapenv",
                    PuFiscalContractV5.Soap11Namespace),
                new XElement(
                    XNamespace.Get(PuFiscalContractV5.Soap11Namespace) + "Body",
                    new XElement(requestRoot))));
    }
}
