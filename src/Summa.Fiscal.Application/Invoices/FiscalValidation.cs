using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public interface IFiscalInvoiceValidator
{
    FiscalValidationResult Validate(FiscalInvoice invoice);
}

public sealed record FiscalValidationError(string Code, string Field, string Message);

public sealed record FiscalValidationResult(IReadOnlyCollection<FiscalValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static FiscalValidationResult Success { get; } = new([]);
}

public sealed class FiscalInvoiceValidator : IFiscalInvoiceValidator
{
    public FiscalValidationResult Validate(FiscalInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var errors = new List<FiscalValidationError>();

        ValidateRequiredIds(invoice, errors);

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            errors.Add(new("INVOICE_NUMBER_REQUIRED", "invoiceNumber", "Broj računa je obavezan."));
        }

        if (!string.Equals(invoice.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new("CURRENCY_NOT_SUPPORTED", "currency", "U ovoj fazi podržana je valuta EUR."));
        }

        if (invoice.Items.Count == 0)
        {
            errors.Add(new("ITEMS_REQUIRED", "items", "Račun mora imati najmanje jednu stavku."));
        }

        if (invoice.Payments.Count == 0)
        {
            errors.Add(new("PAYMENTS_REQUIRED", "payments", "Račun mora imati najmanje jedan način plaćanja."));
        }

        ValidateItems(invoice.Items, errors);
        ValidatePayments(invoice.Payments, errors);

        var paymentsTotal = RoundMoney(invoice.Payments.Sum(payment => payment.Amount));
        if (paymentsTotal != invoice.TotalGrossAmount)
        {
            errors.Add(new(
                "PAYMENT_TOTAL_MISMATCH",
                "payments",
                $"Zbir plaćanja ({paymentsTotal:0.00}) mora biti jednak ukupnom iznosu računa ({invoice.TotalGrossAmount:0.00})."));
        }

        return errors.Count == 0 ? FiscalValidationResult.Success : new(errors);
    }

    private static void ValidateRequiredIds(
        FiscalInvoice invoice,
        ICollection<FiscalValidationError> errors)
    {
        if (invoice.CompanyId == Guid.Empty)
            errors.Add(new("COMPANY_REQUIRED", "companyId", "Firma je obavezna."));
        if (invoice.BusinessUnitId == Guid.Empty)
            errors.Add(new("BUSINESS_UNIT_REQUIRED", "businessUnitId", "Poslovni prostor je obavezan."));
        if (invoice.DeviceId == Guid.Empty)
            errors.Add(new("DEVICE_REQUIRED", "deviceId", "ENU uređaj je obavezan."));
        if (invoice.OperatorId == Guid.Empty)
            errors.Add(new("OPERATOR_REQUIRED", "operatorId", "Operater je obavezan."));
    }

    private static void ValidateItems(
        IEnumerable<FiscalInvoiceItem> items,
        ICollection<FiscalValidationError> errors)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add(new("ITEM_NAME_REQUIRED", $"items[{index}].name", "Naziv stavke je obavezan."));
            if (item.Quantity <= 0)
                errors.Add(new("ITEM_QUANTITY_INVALID", $"items[{index}].quantity", "Količina mora biti veća od nule."));
            if (item.UnitPrice < 0)
                errors.Add(new("ITEM_PRICE_INVALID", $"items[{index}].unitPrice", "Cijena ne može biti negativna."));
            if (item.DiscountAmount < 0 || item.DiscountAmount > item.Quantity * item.UnitPrice)
                errors.Add(new("ITEM_DISCOUNT_INVALID", $"items[{index}].discountAmount", "Popust nije ispravan."));
            if (item.VatRate < 0 || item.VatRate > 100)
                errors.Add(new("VAT_RATE_INVALID", $"items[{index}].vatRate", "PDV stopa mora biti između 0 i 100."));
            index++;
        }
    }

    private static void ValidatePayments(
        IEnumerable<FiscalPayment> payments,
        ICollection<FiscalValidationError> errors)
    {
        var index = 0;
        foreach (var payment in payments)
        {
            if (payment.Amount <= 0)
                errors.Add(new("PAYMENT_AMOUNT_INVALID", $"payments[{index}].amount", "Iznos plaćanja mora biti veći od nule."));
            index++;
        }
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
