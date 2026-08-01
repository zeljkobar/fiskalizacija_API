using System.Globalization;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public interface IRegisterCashDepositXmlBuilderV5
{
    XDocument BuildUnsigned(RegisterCashDepositRequestV5 request);
}

public sealed class RegisterCashDepositXmlBuilderV5 : IRegisterCashDepositXmlBuilderV5
{
    public XDocument BuildUnsigned(RegisterCashDepositRequestV5 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CashDeposit.CashAmount < 0)
        {
            throw new ArgumentException("Iznos gotovine ne može biti negativan.", nameof(request));
        }

        var header = new XElement(
            PuFiscalContractV5.Schema + "Header",
            new XAttribute("UUID", request.Header.Uuid.ToString("D")),
            new XAttribute("SendDateTime", FormatDateTime(request.Header.SendDateTime)));
        if (request.Header.SubsequentDeliveryType is not null)
        {
            header.Add(new XAttribute(
                "SubseqDelivType",
                request.Header.SubsequentDeliveryType.Value.ToXmlValue()));
        }

        var cashDeposit = new XElement(
            PuFiscalContractV5.Schema + "CashDeposit",
            new XAttribute("ChangeDateTime", FormatDateTime(request.CashDeposit.ChangeDateTime)),
            new XAttribute("Operation", request.CashDeposit.Operation.ToXmlValue()),
            new XAttribute(
                "CashAmt",
                request.CashDeposit.CashAmount.ToString("0.00", CultureInfo.InvariantCulture)),
            new XAttribute("TCRCode", request.CashDeposit.TcrCode),
            new XAttribute("IssuerTIN", request.CashDeposit.IssuerTin));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                PuFiscalContractV5.Schema + "RegisterCashDepositRequest",
                new XAttribute("Id", PuFiscalContractV5.RequestId),
                new XAttribute("Version", PuFiscalContractV5.SchemaVersion),
                header,
                cashDeposit,
                new XElement(PuFiscalContractV5.XmlDsig + "Signature")));
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
