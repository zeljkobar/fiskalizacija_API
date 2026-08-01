namespace Summa.Fiscal.Domain.Invoices;

public sealed class FiscalPayment
{
    public FiscalPayment(PaymentType paymentType, decimal amount, string? reference = null)
    {
        Id = Guid.NewGuid();
        PaymentType = paymentType;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Reference = reference?.Trim();
    }

    public Guid Id { get; private set; }
    public PaymentType PaymentType { get; }
    public decimal Amount { get; }
    public string? Reference { get; }

    public static FiscalPayment Restore(
        Guid id,
        PaymentType paymentType,
        decimal amount,
        string? reference)
    {
        var payment = new FiscalPayment(paymentType, amount, reference);
        payment.Id = id;
        return payment;
    }
}
