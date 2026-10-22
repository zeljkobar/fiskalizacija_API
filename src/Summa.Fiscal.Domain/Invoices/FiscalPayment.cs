namespace Summa.Fiscal.Domain.Invoices;

public sealed class FiscalPayment
{
    public FiscalPayment(
        PaymentType paymentType,
        decimal amount,
        string? reference = null,
        Guid? advanceInvoiceId = null,
        string? advanceIic = null)
    {
        Id = Guid.NewGuid();
        PaymentType = paymentType;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Reference = reference?.Trim();
        AdvanceInvoiceId = advanceInvoiceId;
        AdvanceIic = string.IsNullOrWhiteSpace(advanceIic) ? null : advanceIic.Trim();
    }

    public Guid Id { get; private set; }
    public PaymentType PaymentType { get; }
    public decimal Amount { get; }
    public string? Reference { get; }
    public Guid? AdvanceInvoiceId { get; }
    public string? AdvanceIic { get; }

    public static FiscalPayment Restore(
        Guid id,
        PaymentType paymentType,
        decimal amount,
        string? reference,
        Guid? advanceInvoiceId,
        string? advanceIic)
    {
        var payment = new FiscalPayment(
            paymentType,
            amount,
            reference,
            advanceInvoiceId,
            advanceIic);
        payment.Id = id;
        return payment;
    }
}
