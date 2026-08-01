namespace Summa.Fiscal.Domain.Invoices;

public sealed class FiscalInvoice
{
    private readonly List<FiscalInvoiceItem> _items = [];
    private readonly List<FiscalPayment> _payments = [];

    public FiscalInvoice(
        Guid companyId,
        Guid businessUnitId,
        Guid deviceId,
        Guid operatorId,
        InvoiceType invoiceType,
        string invoiceNumber,
        DateTimeOffset issueDateTime,
        string currency,
        string idempotencyKey)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        BusinessUnitId = businessUnitId;
        DeviceId = deviceId;
        OperatorId = operatorId;
        InvoiceType = invoiceType;
        InvoiceNumber = invoiceNumber?.Trim() ?? string.Empty;
        IssueDateTime = issueDateTime;
        Currency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        IdempotencyKey = idempotencyKey;
        Status = FiscalStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; }
    public Guid BusinessUnitId { get; }
    public Guid DeviceId { get; }
    public Guid OperatorId { get; }
    public InvoiceType InvoiceType { get; }
    public string InvoiceNumber { get; }
    public DateTimeOffset IssueDateTime { get; }
    public string Currency { get; }
    public string IdempotencyKey { get; }
    public FiscalStatus Status { get; private set; }
    public decimal TotalNetAmount { get; private set; }
    public decimal TotalVatAmount { get; private set; }
    public decimal TotalGrossAmount { get; private set; }
    public string? Iic { get; private set; }
    public string? IicSignature { get; private set; }
    public string? Jikr { get; private set; }
    public string? QrCodeData { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? FiscalizedAt { get; private set; }
    public IReadOnlyCollection<FiscalInvoiceItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<FiscalPayment> Payments => _payments.AsReadOnly();

    public static FiscalInvoice Restore(
        Guid id,
        Guid companyId,
        Guid businessUnitId,
        Guid deviceId,
        Guid operatorId,
        InvoiceType invoiceType,
        string invoiceNumber,
        DateTimeOffset issueDateTime,
        string currency,
        string idempotencyKey,
        FiscalStatus status,
        string? iic,
        string? iicSignature,
        string? jikr,
        string? qrCodeData,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? fiscalizedAt,
        IEnumerable<(Guid Id, string Name, decimal Quantity, decimal UnitPrice,
            decimal VatRate, string? ItemCode, string? UnitOfMeasure, decimal DiscountAmount)> items,
        IEnumerable<(Guid Id, PaymentType PaymentType, decimal Amount, string? Reference)> payments)
    {
        var invoice = new FiscalInvoice(
            companyId,
            businessUnitId,
            deviceId,
            operatorId,
            invoiceType,
            invoiceNumber,
            issueDateTime,
            currency,
            idempotencyKey)
        {
            Id = id,
            Status = FiscalStatus.Draft,
            Iic = iic,
            IicSignature = iicSignature,
            Jikr = jikr,
            QrCodeData = qrCodeData,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            FiscalizedAt = fiscalizedAt
        };

        foreach (var item in items)
        {
            invoice._items.Add(FiscalInvoiceItem.Restore(
                item.Id,
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.VatRate,
                item.ItemCode,
                item.UnitOfMeasure,
                item.DiscountAmount));
        }

        foreach (var payment in payments)
        {
            invoice._payments.Add(FiscalPayment.Restore(
                payment.Id,
                payment.PaymentType,
                payment.Amount,
                payment.Reference));
        }

        invoice.RecalculateTotals();
        invoice.Status = status;
        invoice.UpdatedAt = updatedAt;
        return invoice;
    }

    public void MarkFiscalizationPending(string iic, string iicSignature)
    {
        if (Status is not (FiscalStatus.ReadyForFiscalization or FiscalStatus.FiscalizationFailed))
        {
            throw new InvalidOperationException("Račun nije spreman za slanje Poreskoj upravi.");
        }

        Iic = string.IsNullOrWhiteSpace(iic)
            ? throw new ArgumentException("IKOF je obavezan.", nameof(iic))
            : iic;
        IicSignature = string.IsNullOrWhiteSpace(iicSignature)
            ? throw new ArgumentException("IKOF potpis je obavezan.", nameof(iicSignature))
            : iicSignature;
        Status = FiscalStatus.FiscalizationPending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFiscalized(string jikr, string? qrCodeData = null)
    {
        if (Status != FiscalStatus.FiscalizationPending)
        {
            throw new InvalidOperationException("Račun prethodno nije označen kao poslat PU.");
        }

        Jikr = string.IsNullOrWhiteSpace(jikr)
            ? throw new ArgumentException("JIKR je obavezan.", nameof(jikr))
            : jikr;
        QrCodeData = qrCodeData;
        Status = FiscalStatus.Fiscalized;
        FiscalizedAt = DateTimeOffset.UtcNow;
        UpdatedAt = FiscalizedAt.Value;
    }

    public void SetQrCodeData(string qrCodeData)
    {
        if (Status != FiscalStatus.Fiscalized || string.IsNullOrWhiteSpace(Jikr))
        {
            throw new InvalidOperationException(
                "QR podatak se može dodati samo uspješno fiskalizovanom računu.");
        }

        QrCodeData = string.IsNullOrWhiteSpace(qrCodeData)
            ? throw new ArgumentException("QR podatak je obavezan.", nameof(qrCodeData))
            : qrCodeData;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFiscalizationFailed()
    {
        if (Status != FiscalStatus.FiscalizationPending)
        {
            throw new InvalidOperationException("Samo poslati račun može biti označen kao neuspješan.");
        }

        Status = FiscalStatus.FiscalizationFailed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddItem(FiscalInvoiceItem item)
    {
        EnsureDraft();
        _items.Add(item ?? throw new ArgumentNullException(nameof(item)));
        RecalculateTotals();
    }

    public void AddPayment(FiscalPayment payment)
    {
        EnsureDraft();
        _payments.Add(payment ?? throw new ArgumentNullException(nameof(payment)));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkValidated()
    {
        EnsureDraft();
        Status = FiscalStatus.Validated;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkReadyForFiscalization()
    {
        if (Status != FiscalStatus.Validated)
        {
            throw new InvalidOperationException("Samo validiran račun može biti spreman za fiskalizaciju.");
        }

        Status = FiscalStatus.ReadyForFiscalization;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void RecalculateTotals()
    {
        TotalNetAmount = RoundMoney(_items.Sum(item => item.NetAmount));
        TotalVatAmount = RoundMoney(_items.Sum(item => item.VatAmount));
        TotalGrossAmount = RoundMoney(_items.Sum(item => item.GrossAmount));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != FiscalStatus.Draft)
        {
            throw new InvalidOperationException("Račun se više ne može mijenjati nakon validacije.");
        }
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
