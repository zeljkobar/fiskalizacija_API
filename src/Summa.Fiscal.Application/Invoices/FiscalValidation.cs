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

        if (invoice.InvoiceType is not (InvoiceType.Normal or InvoiceType.Advance or InvoiceType.Corrective or InvoiceType.Summary))
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

        if (invoice.Items.Count == 0 && !invoice.IsInformational)
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
        if (invoice.IsInformational && (invoice.OrderSummary is null || invoice.OrderSummary.References.Count == 0 ||
            invoice.OrderSummary.References.Sum(x => x.GrossAmount) != invoice.TotalGrossAmount ||
            invoice.OrderSummary.References.Sum(x => x.NetAmount) != invoice.TotalNetAmount ||
            invoice.OrderSummary.References.Sum(x => x.VatAmount) != invoice.TotalVatAmount))
            errors.Add(new("SUMMARY_REFERENCES_INVALID", "orderSummary", "Iznosi moraju tačno odgovarati sačuvanim izvorima sumarnog dokumenta."));
        if (invoice.InvoiceType == InvoiceType.Summary && invoice.TotalGrossAmount <= 0)
            errors.Add(new("SUMMARY_TOTAL_INVALID", "totalGrossAmount", "Sumarni račun mora imati pozitivan iznos."));
        if (invoice.Payments.Any(x => x.PaymentType == PaymentType.Order) &&
            (invoice.Payments.Count != 1 || !(invoice.IsOrder || invoice.IsOrderCorrection)))
            errors.Add(new("ORDER_PAYMENT_EXCLUSIVE", "payments", "ORDER mora biti jedini način plaćanja na običnom računu ili njegovoj kontrolisanoj korekciji."));
        if (invoice.IsInformational && invoice.Payments.Any(x => x.PaymentType is not (PaymentType.Cash or PaymentType.Card or PaymentType.BankAccount)))
            errors.Add(new("SUMMARY_PAYMENT_INVALID", "payments", "Navesti stvarno plaćanje: Cash, Card ili BankAccount, uključujući kombinacije."));
        if (invoice.Payments.Count > 10 || invoice.Payments.GroupBy(x => x.PaymentType).Any(x => x.Count() > 1))
            errors.Add(new("PAYMENT_TYPES_INVALID", "payments", "Najviše 10 različitih načina plaćanja; isti tip može biti naveden samo jednom."));
        if (invoice.InvoiceType == InvoiceType.Advance &&
            (invoice.Buyer is null || invoice.Payments.Count != 1 || invoice.Payments.Any(x => x.PaymentType != PaymentType.BankAccount)))
            errors.Add(new("ADVANCE_BANK_RECEIPT_REQUIRED", "payments", "Avans zahtijeva identifikovanog kupca i jednu bezgotovinsku uplatu BankAccount."));

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
            if (invoice.IsAdvanceRestoration ? invoice.TotalGrossAmount <= 0 : invoice.TotalGrossAmount >= 0)
                errors.Add(new("CORRECTIVE_TOTAL_INVALID", "totalGrossAmount", "Predznak korektivnog iznosa ne odgovara vrsti operacije."));
            if (invoice.IsAdvanceRestoration && (string.IsNullOrWhiteSpace(invoice.AdvanceRestorationStornoIic) || !invoice.AdvanceRestorationStornoIssuedAt.HasValue))
                errors.Add(new("ADVANCE_RESTORATION_REFERENCE_REQUIRED", "advanceRestorationStornoInvoiceId", "Obnova mora imati kompletnu referencu na storno konačnog računa."));
            if (invoice.OriginalIssueDateTime.HasValue && invoice.IssueDateTime < invoice.OriginalIssueDateTime.Value)
                errors.Add(new("CORRECTIVE_DATE_INVALID", "issueDateTime", "Korektivni račun ne može prethoditi originalu."));
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
            if ((invoice.IsOrder || invoice.IsOrderCorrection) &&
                (decimal.Round(item.Quantity, 4) != item.Quantity || decimal.Round(item.UnitPrice, 4) != item.UnitPrice ||
                 decimal.Round(item.VatRate, 4) != item.VatRate || decimal.Round(item.DiscountAmount, 2) != item.DiscountAmount))
                errors.Add(new("ORDER_ITEM_PRECISION_INVALID", $"items[{index}]", "ORDER količine, cijene i stope imaju do četiri decimale, a popust do dvije."));
            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add(new("ITEM_NAME_REQUIRED", $"items[{index}].name", "Naziv stavke je obavezan."));
            if (!invoice.IsSummaryCancellation && invoice.InvoiceType != InvoiceType.Summary && (invoice.InvoiceType == InvoiceType.Corrective && !invoice.IsAdvanceRestoration ? item.Quantity >= 0 : item.Quantity <= 0))
                errors.Add(new("ITEM_QUANTITY_INVALID", $"items[{index}].quantity", invoice.InvoiceType == InvoiceType.Corrective ? "Količina potpunog storna mora biti negativna." : "Količina mora biti veća od nule."));
            if (item.UnitPrice < 0)
                errors.Add(new("ITEM_PRICE_INVALID", $"items[{index}].unitPrice", "Cijena ne može biti negativna."));
            if ((invoice.InvoiceType == InvoiceType.Corrective && !invoice.IsAdvanceRestoration && !invoice.IsSummaryCancellation) || (invoice.IsInformational && item.Quantity < 0)
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
        var advanceInvoiceIds = new HashSet<Guid>();
        if (invoice.Payments.Count(x => x.PaymentType == PaymentType.Advance) > 1)
            errors.Add(new("MULTIPLE_ADVANCE_PAYMENTS_NOT_SUPPORTED", "payments", "EFI v5 UniquePayMethodType dozvoljava samo jednu ADVANCE stavku plaćanja po računu."));
        foreach (var payment in invoice.Payments)
        {
            if (!Enum.IsDefined(payment.PaymentType) || decimal.Round(payment.Amount, 2) != payment.Amount)
                errors.Add(new("PAYMENT_FORMAT_INVALID", $"payments[{index}]", "Način plaćanja mora biti poznat, a iznos u centima."));
            if (invoice.InvoiceType == InvoiceType.Corrective && !invoice.IsAdvanceRestoration ? payment.Amount >= 0 : payment.Amount <= 0)
                errors.Add(new("PAYMENT_AMOUNT_INVALID", $"payments[{index}].amount", invoice.InvoiceType == InvoiceType.Corrective ? "Iznos povrata kod storna mora biti negativan." : "Iznos plaćanja mora biti veći od nule."));

            if (payment.PaymentType == PaymentType.Advance)
            {
                if (invoice.IsAdvanceRestoration || invoice.InvoiceType is not (InvoiceType.Normal or InvoiceType.Corrective))
                    errors.Add(new("ADVANCE_PAYMENT_NOT_ALLOWED", $"payments[{index}].paymentType", "Avans se može koristiti samo kao način plaćanja konačnog računa."));
                if (!payment.AdvanceInvoiceId.HasValue || string.IsNullOrWhiteSpace(payment.AdvanceIic))
                    errors.Add(new("ADVANCE_REFERENCE_REQUIRED", $"payments[{index}].advanceInvoiceId", "Plaćanje avansom mora imati fiskalizovani avans i njegov IKOF."));
                else if (!advanceInvoiceIds.Add(payment.AdvanceInvoiceId.Value))
                    errors.Add(new("ADVANCE_REFERENCE_DUPLICATE", $"payments[{index}].advanceInvoiceId", "Isti avans može biti naveden samo jednom na konačnom računu."));
            }
            else if (payment.AdvanceInvoiceId.HasValue || !string.IsNullOrWhiteSpace(payment.AdvanceIic))
            {
                errors.Add(new("ADVANCE_REFERENCE_NOT_ALLOWED", $"payments[{index}].advanceInvoiceId", "Referenca na avans dozvoljena je samo za način plaćanja Advance."));
            }

            index++;
        }
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
