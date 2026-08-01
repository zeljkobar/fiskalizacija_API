using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public interface IRegisterInvoiceXmlBuilderV5
{
    XDocument BuildUnsigned(RegisterInvoiceRequestV5 request);
}

public sealed class RegisterInvoiceXmlBuilderV5 : IRegisterInvoiceXmlBuilderV5
{
    public XDocument BuildUnsigned(RegisterInvoiceRequestV5 request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var root = new XElement(
            PuFiscalContractV5.Schema + "RegisterInvoiceRequest",
            new XAttribute("Id", PuFiscalContractV5.RequestId),
            new XAttribute("Version", PuFiscalContractV5.SchemaVersion),
            BuildHeader(request.Header),
            BuildInvoice(request.Invoice),
            new XElement(PuFiscalContractV5.XmlDsig + "Signature"));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement BuildHeader(RegisterInvoiceHeaderV5 header)
    {
        var element = new XElement(
            PuFiscalContractV5.Schema + "Header",
            new XAttribute("UUID", header.Uuid.ToString("D")),
            new XAttribute("SendDateTime", FormatDateTime(header.SendDateTime)));

        AddOptional(
            element,
            "SubseqDelivType",
            header.SubsequentDeliveryType?.ToXmlValue());

        return element;
    }

    private static XElement BuildInvoice(PuInvoiceV5 invoice)
    {
        var element = new XElement(
            PuFiscalContractV5.Schema + "Invoice",
            new XAttribute("TypeOfInv", invoice.TypeOfInvoice.ToXmlValue()),
            new XAttribute("IssueDateTime", FormatDateTime(invoice.IssueDateTime)),
            new XAttribute("InvNum", invoice.InvoiceNumber),
            new XAttribute("InvOrdNum", invoice.InvoiceOrdinalNumber),
            new XAttribute("TCRCode", invoice.TcrCode),
            new XAttribute("IsIssuerInVAT", XmlConvert.ToString(invoice.IsIssuerInVat)),
            new XAttribute("TotPriceWoVAT", FormatDecimal2(invoice.TotalPriceWithoutVat)),
            new XAttribute("TotPrice", FormatDecimal2(invoice.TotalPrice)),
            new XAttribute("OperatorCode", invoice.OperatorCode),
            new XAttribute("BusinUnitCode", invoice.BusinessUnitCode),
            new XAttribute("SoftCode", invoice.SoftwareCode),
            new XAttribute("IIC", invoice.Iic),
            new XAttribute("IICSignature", invoice.IicSignature));

        AddOptional(element, "InvType", invoice.DocumentType?.ToXmlValue());
        AddOptional(element, "IsSimplifiedInv", FormatBoolean(invoice.IsSimplifiedInvoice));
        AddOptional(element, "TotVATAmt", FormatDecimal2(invoice.TotalVatAmount));
        AddOptional(element, "TotPriceToPay", FormatDecimal2(invoice.TotalPriceToPay));
        AddOptional(element, "IsReverseCharge", FormatBoolean(invoice.IsReverseCharge));

        element.Add(BuildPayments(invoice.Payments));
        element.Add(BuildSeller(invoice.Seller));

        if (invoice.Buyer is not null)
        {
            element.Add(BuildBuyer(invoice.Buyer));
        }

        if (invoice.Items.Count > 0)
        {
            element.Add(new XElement(
                PuFiscalContractV5.Schema + "Items",
                invoice.Items.Select(BuildItem)));
        }

        if (invoice.SameTaxes is { Count: > 0 })
        {
            element.Add(new XElement(
                PuFiscalContractV5.Schema + "SameTaxes",
                invoice.SameTaxes.Select(BuildSameTax)));
        }

        return element;
    }

    private static XElement BuildPayments(IEnumerable<PuPaymentV5> payments) =>
        new(
            PuFiscalContractV5.Schema + "PayMethods",
            payments.Select(payment =>
            {
                var element = new XElement(
                    PuFiscalContractV5.Schema + "PayMethod",
                    new XAttribute("Type", payment.Type.ToXmlValue()),
                    new XAttribute("Amt", FormatDecimal2(payment.Amount)));
                AddOptional(element, "CompCard", payment.CompanyCard);
                AddOptional(element, "AdvIIC", payment.AdvanceIic);
                AddOptional(element, "BankAcc", payment.BankAccount);
                return element;
            }));

    private static XElement BuildSeller(PuSellerV5 seller)
    {
        var element = new XElement(
            PuFiscalContractV5.Schema + "Seller",
            new XAttribute("IDType", seller.IdType.ToXmlValue()),
            new XAttribute("IDNum", seller.IdNumber),
            new XAttribute("Name", seller.Name));
        AddOptional(element, "Address", seller.Address);
        AddOptional(element, "Town", seller.Town);
        AddOptional(element, "Country", seller.Country);
        return element;
    }

    private static XElement BuildBuyer(PuBuyerV5 buyer)
    {
        var element = new XElement(PuFiscalContractV5.Schema + "Buyer");
        AddOptional(element, "IDType", buyer.IdType?.ToXmlValue());
        AddOptional(element, "IDNum", buyer.IdNumber);
        AddOptional(element, "Name", buyer.Name);
        AddOptional(element, "Address", buyer.Address);
        AddOptional(element, "Town", buyer.Town);
        AddOptional(element, "Country", buyer.Country);
        AddOptional(element, "TIC", buyer.TaxIdentificationCode);
        return element;
    }

    private static XElement BuildItem(PuInvoiceItemV5 item)
    {
        var element = new XElement(
            PuFiscalContractV5.Schema + "I",
            new XAttribute("N", item.Name),
            new XAttribute("U", item.Unit),
            new XAttribute("Q", FormatQuantity(item.Quantity)),
            new XAttribute("UPB", FormatDecimal4(item.UnitPriceBeforeVat)),
            new XAttribute("UPA", FormatDecimal4(item.UnitPriceAfterVat)),
            new XAttribute("PB", FormatDecimal4(item.PriceBeforeVat)),
            new XAttribute("PA", FormatDecimal4(item.PriceAfterVat)));
        AddOptional(element, "C", item.Code);
        AddOptional(element, "R", FormatDecimal4(item.Rebate));
        AddOptional(element, "RR", FormatBoolean(item.RebateReducesTaxBase));
        AddOptional(element, "VR", FormatDecimal4(item.VatRate));
        AddOptional(element, "VA", FormatDecimal4(item.VatAmount));
        AddOptional(element, "IN", FormatBoolean(item.IsInvestment));
        AddOptional(element, "EX", item.VatExemption?.ToXmlValue());
        return element;
    }

    private static XElement BuildSameTax(PuSameTaxV5 tax)
    {
        var element = new XElement(
            PuFiscalContractV5.Schema + "SameTax",
            new XAttribute("NumOfItems", tax.NumberOfItems),
            new XAttribute("PriceBefVAT", FormatDecimal2(tax.PriceBeforeVat)),
            new XAttribute("VATRate", FormatDecimal2(tax.VatRate)),
            new XAttribute("VATAmt", FormatDecimal2(tax.VatAmount)));
        AddOptional(element, "ExemptFromVAT", tax.VatExemption?.ToXmlValue());
        return element;
    }

    private static void AddOptional(XElement element, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            element.Add(new XAttribute(name, value));
        }
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string FormatDecimal2(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? FormatDecimal2(decimal? value) =>
        value.HasValue ? FormatDecimal2(value.Value) : null;

    private static string FormatDecimal4(decimal value) =>
        value.ToString("0.00##", CultureInfo.InvariantCulture);

    private static string? FormatDecimal4(decimal? value) =>
        value.HasValue ? FormatDecimal4(value.Value) : null;

    private static string FormatQuantity(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string? FormatBoolean(bool? value) =>
        value.HasValue ? XmlConvert.ToString(value.Value) : null;
}
