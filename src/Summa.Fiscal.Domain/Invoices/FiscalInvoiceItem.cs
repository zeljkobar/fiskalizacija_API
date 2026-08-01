namespace Summa.Fiscal.Domain.Invoices;

public sealed class FiscalInvoiceItem
{
    public FiscalInvoiceItem(
        string name,
        decimal quantity,
        decimal unitPrice,
        decimal vatRate,
        string? itemCode = null,
        string? unitOfMeasure = null,
        decimal discountAmount = 0)
    {
        Id = Guid.NewGuid();
        Name = name?.Trim() ?? string.Empty;
        ItemCode = itemCode?.Trim();
        UnitOfMeasure = unitOfMeasure?.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        VatRate = vatRate;
        DiscountAmount = discountAmount;

        var grossBeforeDiscount = quantity * unitPrice;
        GrossAmount = RoundMoney(grossBeforeDiscount - discountAmount);
        NetAmount = vatRate == 0
            ? GrossAmount
            : RoundMoney(GrossAmount / (1 + vatRate / 100));
        VatAmount = RoundMoney(GrossAmount - NetAmount);
    }

    public Guid Id { get; private set; }
    public string? ItemCode { get; }
    public string Name { get; }
    public string? UnitOfMeasure { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal DiscountAmount { get; }
    public decimal VatRate { get; }
    public decimal NetAmount { get; }
    public decimal VatAmount { get; }
    public decimal GrossAmount { get; }

    public static FiscalInvoiceItem Restore(
        Guid id,
        string name,
        decimal quantity,
        decimal unitPrice,
        decimal vatRate,
        string? itemCode,
        string? unitOfMeasure,
        decimal discountAmount)
    {
        var item = new FiscalInvoiceItem(
            name,
            quantity,
            unitPrice,
            vatRate,
            itemCode,
            unitOfMeasure,
            discountAmount);
        item.Id = id;
        return item;
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
