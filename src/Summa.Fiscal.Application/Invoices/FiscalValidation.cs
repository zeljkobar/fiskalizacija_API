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

        if (invoice.InvoiceType is not (InvoiceType.Normal or InvoiceType.Corrective))
        {
            errors.Add(new("INVOICE_TYPE_NOT_IMPLEMENTED", "invoiceType", "Ovaj tip računa još nema kompletan poslovni workflow."));
        }

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

        ValidateBuyer(invoice.Buyer, errors);
        ValidateDates(invoice, errors);
        ValidateCorrective(invoice, errors);
        ValidateItems(invoice, errors);
        ValidatePayments(invoice, errors);

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

    private static void ValidateBuyer(FiscalBuyer? buyer, ICollection<FiscalValidationError> errors)
    {
        if (buyer is null) return;
        if (string.IsNullOrWhiteSpace(buyer.IdentificationNumber) || buyer.IdentificationNumber.Length > 20)
            errors.Add(new("BUYER_ID_INVALID", "buyer.identificationNumber", "Identifikator kupca je obavezan i može imati najviše 20 znakova."));
        if (string.IsNullOrWhiteSpace(buyer.Name) || buyer.Name.Length > 100)
            errors.Add(new("BUYER_NAME_INVALID", "buyer.name", "Naziv kupca je obavezan i može imati najviše 100 znakova."));
        if (buyer.Address?.Length > 200)
            errors.Add(new("BUYER_ADDRESS_INVALID", "buyer.address", "Adresa kupca može imati najviše 200 znakova."));
        if (buyer.Town?.Length > 100)
            errors.Add(new("BUYER_TOWN_INVALID", "buyer.town", "Grad kupca može imati najviše 100 znakova."));
        if (buyer.Country is not null && (buyer.Country.Length != 3 || !buyer.Country.All(char.IsLetter)))
            errors.Add(new("BUYER_COUNTRY_INVALID", "buyer.country", "Država kupca mora biti ISO 3166-1 alpha-3 kod."));
        if (buyer.TaxIdentificationCode?.Length > 20)
            errors.Add(new("BUYER_TIC_INVALID", "buyer.taxIdentificationCode", "Poreski identifikacioni kod može imati najviše 20 znakova."));
    }

    private static void ValidateDates(FiscalInvoice invoice, ICollection<FiscalValidationError> errors)
    {
        if (invoice.SupplyPeriodStart.HasValue != invoice.SupplyPeriodEnd.HasValue)
            errors.Add(new("SUPPLY_PERIOD_INCOMPLETE", "supplyPeriod", "Početak i kraj perioda isporuke moraju biti zadati zajedno."));
        if (invoice.SupplyPeriodStart > invoice.SupplyPeriodEnd)
            errors.Add(new("SUPPLY_PERIOD_INVALID", "supplyPeriod", "Početak perioda isporuke ne može biti poslije kraja."));
        if (invoice.PaymentDeadline is { } deadline && deadline < DateOnly.FromDateTime(invoice.IssueDateTime.Date))
            errors.Add(new("PAYMENT_DEADLINE_INVALID", "paymentDeadline", "Rok plaćanja ne može biti prije datuma izdavanja."));
    }

    private static void ValidateCorrective(FiscalInvoice invoice, ICollection<FiscalValidationError> errors)
    {
        if (invoice.InvoiceType == InvoiceType.Corrective)
        {
            if (!invoice.OriginalInvoiceId.HasValue || string.IsNullOrWhiteSpace(invoice.OriginalIic) ||
                !invoice.OriginalIssueDateTime.HasValue || !invoice.CorrectiveType.HasValue)
                errors.Add(new("CORRECTIVE_REFERENCE_REQUIRED", "originalInvoiceId", "Korektivni račun mora imati kompletnu referencu na original."));
            if (string.IsNullOrWhiteSpace(invoice.CorrectionReason) || invoice.CorrectionReason.Length > 500)
                errors.Add(new("CORRECTION_REASON_INVALID", "correctionReason", "Razlog korekcije je obavezan i može imati najviše 500 znakova."));
            if (invoice.TotalGrossAmount >= 0)
                errors.Add(new("CORRECTIVE_TOTAL_INVALID", "totalGrossAmount", "Potpuni storno mora imati negativan ukupan iznos."));
        }
        else if (invoice.OriginalInvoiceId.HasValue)
        {
            errors.Add(new("CORRECTIVE_REFERENCE_NOT_ALLOWED", "originalInvoiceId", "Običan račun ne smije imati korektivnu referencu."));
        }
    }

    private static void ValidateItems(
        FiscalInvoice invoice,
        ICollection<FiscalValidationError> errors)
    {
        var index = 0;
        foreach (var item in invoice.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add(new("ITEM_NAME_REQUIRED", $"items[{index}].name", "Naziv stavke je obavezan."));
            if (invoice.InvoiceType == InvoiceType.Corrective ? item.Quantity >= 0 : item.Quantity <= 0)
                errors.Add(new("ITEM_QUANTITY_INVALID", $"items[{index}].quantity", invoice.InvoiceType == InvoiceType.Corrective ? "Količina potpunog storna mora biti negativna." : "Količina mora biti veća od nule."));
            if (item.UnitPrice < 0)
                errors.Add(new("ITEM_PRICE_INVALID", $"items[{index}].unitPrice", "Cijena ne može biti negativna."));
            if (invoice.InvoiceType == InvoiceType.Corrective
                    ? item.DiscountAmount > 0 || Math.Abs(item.DiscountAmount) > Math.Abs(item.Quantity * item.UnitPrice)
                    : item.DiscountAmount < 0 || item.DiscountAmount > item.Quantity * item.UnitPrice)
                errors.Add(new("ITEM_DISCOUNT_INVALID", $"items[{index}].discountAmount", "Popust nije ispravan."));
            if (item.VatRate < 0 || item.VatRate > 100)
                errors.Add(new("VAT_RATE_INVALID", $"items[{index}].vatRate", "PDV stopa mora biti između 0 i 100."));
            index++;
        }
    }

    private static void ValidatePayments(
        FiscalInvoice invoice,
        ICollection<FiscalValidationError> errors)
    {
        var index = 0;
        foreach (var payment in invoice.Payments)
        {
            if (invoice.InvoiceType == InvoiceType.Corrective ? payment.Amount >= 0 : payment.Amount <= 0)
                errors.Add(new("PAYMENT_AMOUNT_INVALID", $"payments[{index}].amount", invoice.InvoiceType == InvoiceType.Corrective ? "Iznos povrata kod storna mora biti negativan." : "Iznos plaćanja mora biti veći od nule."));
            index++;
        }
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
