namespace Summa.Fiscal.Domain.Invoices;

public sealed record OrderCorrectionAllocation(Guid OrderItemId, decimal Quantity);
public sealed record SummaryInvoiceReference(Guid InvoiceId, string Iic, DateTimeOffset IssuedAt,
    decimal NetAmount, decimal VatAmount, decimal GrossAmount, IReadOnlyCollection<SummaryTax> Taxes);
public sealed record SummaryTax(decimal Rate, int NumberOfInvoices, decimal NetAmount, decimal VatAmount);
public sealed record OrderSummaryData(
    IReadOnlyCollection<Guid> OrderInvoiceIds,
    IReadOnlyCollection<SummaryInvoiceReference> References,
    IReadOnlyCollection<OrderCorrectionAllocation> OrderAllocations,
    IReadOnlyCollection<Guid> AccountedCorrectionIds,
    bool IsCancellation = false);

public sealed partial class FiscalInvoice
{
    public OrderSummaryData? OrderSummary { get; private set; }
    public bool IsOrder => InvoiceType == InvoiceType.Normal && Payments.Count == 1 && Payments.Single().PaymentType == PaymentType.Order;
    public bool IsOrderCorrection => InvoiceType == InvoiceType.Corrective && OrderSummary?.OrderAllocations.Count > 0;
    public bool IsSummaryCorrection => InvoiceType == InvoiceType.Corrective && OrderSummary is { OrderAllocations.Count: 0 };
    public bool IsInformational => InvoiceType == InvoiceType.Summary || IsSummaryCorrection;
    public bool IsSummaryCancellation => IsSummaryCorrection && OrderSummary!.IsCancellation;

    public static SummaryInvoiceReference SummaryReference(FiscalInvoice invoice) => new(
        invoice.Id, invoice.Iic!, invoice.IssueDateTime, invoice.TotalNetAmount, invoice.TotalVatAmount,
        invoice.TotalGrossAmount, invoice.IsInformational
            ? invoice.OrderSummary!.References.SelectMany(x => x.Taxes).GroupBy(x => x.Rate)
                .Select(x => new SummaryTax(x.Key, 1, x.Sum(t => t.NetAmount), x.Sum(t => t.VatAmount))).ToArray()
            : invoice.Items.GroupBy(x => x.VatRate)
                .Select(x => new SummaryTax(x.Key, 1, x.Sum(i => i.NetAmount), x.Sum(i => i.VatAmount))).ToArray());

    public static FiscalInvoice CreateSummary(IReadOnlyCollection<FiscalInvoice> orders,
        IReadOnlyCollection<FiscalInvoice> corrections, Guid deviceId, Guid operatorId,
        string number, DateTimeOffset issuedAt, string key, IReadOnlyCollection<FiscalPayment> payments)
    {
        var first = orders.First();
        var invoice = new FiscalInvoice(first.CompanyId, first.BusinessUnitId, deviceId, operatorId,
            InvoiceType.Summary, number, issuedAt, first.Currency, key, first.Buyer);
        var documents = orders.Concat(corrections).OrderBy(x => x.Id).ToArray();
        invoice.OrderSummary = new(orders.Select(x => x.Id).Order().ToArray(),
            documents.Select(SummaryReference).ToArray(), [], corrections.Select(x => x.Id).Order().ToArray());
        invoice.RecalculateTotals();
        foreach (var payment in payments) invoice.AddPayment(payment);
        return invoice;
    }

    public static FiscalInvoice CreateOrderCorrection(FiscalInvoice order, string number,
        DateTimeOffset issuedAt, string key, string reason, IReadOnlyCollection<OrderCorrectionAllocation> allocations)
    {
        if (!order.IsOrder || order.Status != FiscalStatus.Fiscalized || string.IsNullOrWhiteSpace(order.Jikr))
            throw new InvalidOperationException("Korekcija zahtijeva fiskalizovani ORDER račun.");
        var invoice = new FiscalInvoice(order.CompanyId, order.BusinessUnitId, order.DeviceId, order.OperatorId,
            InvoiceType.Corrective, number, issuedAt, order.Currency, key, order.Buyer,
            originalInvoiceId: order.Id, originalIic: order.Iic, originalIssueDateTime: order.IssueDateTime,
            correctiveType: CorrectiveInvoiceType.Corrective, correctionReason: reason);
        invoice.OrderSummary = new([order.Id], [], allocations.ToArray(), []);
        foreach (var allocation in allocations)
        {
            var item = order.Items.Single(x => x.Id == allocation.OrderItemId);
            var discount = decimal.Round(item.DiscountAmount * allocation.Quantity / item.Quantity, 2, MidpointRounding.AwayFromZero);
            invoice.AddItem(new(item.Name, -allocation.Quantity, item.UnitPrice, item.VatRate, item.ItemCode, item.UnitOfMeasure, -discount));
        }
        invoice.AddPayment(new(PaymentType.Order, invoice.TotalGrossAmount));
        return invoice;
    }

    public static FiscalInvoice CreateSummaryCorrection(FiscalInvoice summary,
        IReadOnlyCollection<FiscalInvoice> corrections, string number, DateTimeOffset issuedAt,
        string key, string reason, IReadOnlyCollection<FiscalPayment> refunds)
    {
        var invoice = new FiscalInvoice(summary.CompanyId, summary.BusinessUnitId, summary.DeviceId, summary.OperatorId,
            InvoiceType.Corrective, number, issuedAt, summary.Currency, key, summary.Buyer,
            originalInvoiceId: summary.Id, originalIic: summary.Iic, originalIssueDateTime: summary.IssueDateTime,
            correctiveType: CorrectiveInvoiceType.Corrective, correctionReason: reason);
        invoice.OrderSummary = new(summary.OrderSummary!.OrderInvoiceIds.ToArray(), corrections.Select(SummaryReference).ToArray(), [], corrections.Select(x => x.Id).Order().ToArray());
        invoice.RecalculateTotals();
        foreach (var payment in refunds) invoice.AddPayment(payment);
        return invoice;
    }

    public static FiscalInvoice CreateSummaryCancellation(FiscalInvoice summary, IReadOnlyCollection<FiscalInvoice> adjustments,
        string number, DateTimeOffset issuedAt, string key, string reason, IReadOnlyCollection<FiscalPayment> payments)
    {
        var invoice = new FiscalInvoice(summary.CompanyId, summary.BusinessUnitId, summary.DeviceId, summary.OperatorId,
            InvoiceType.Corrective, number, issuedAt, summary.Currency, key, summary.Buyer,
            originalInvoiceId: summary.Id, originalIic: summary.Iic, originalIssueDateTime: summary.IssueDateTime,
            correctiveType: CorrectiveInvoiceType.Corrective, correctionReason: reason);
        var documents = new[] { summary }.Concat(adjustments).ToArray();
        var references = documents.Select(SummaryReference).Select(x => x with
        {
            NetAmount = -x.NetAmount, VatAmount = -x.VatAmount, GrossAmount = -x.GrossAmount,
            Taxes = x.Taxes.Select(t => t with { NetAmount = -t.NetAmount, VatAmount = -t.VatAmount }).ToArray()
        }).ToArray();
        invoice.OrderSummary = new(summary.OrderSummary!.OrderInvoiceIds.ToArray(), references, [], [], true);
        invoice.RecalculateTotals();
        foreach (var payment in payments) invoice.AddPayment(payment);
        return invoice;
    }
}
