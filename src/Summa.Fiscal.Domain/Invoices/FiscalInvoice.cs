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
        string idempotencyKey,
        FiscalBuyer? buyer = null,
        DateOnly? supplyPeriodStart = null,
        DateOnly? supplyPeriodEnd = null,
        DateOnly? paymentDeadline = null,
        Guid? originalInvoiceId = null,
        string? originalIic = null,
        DateTimeOffset? originalIssueDateTime = null,
        CorrectiveInvoiceType? correctiveType = null,
        string? correctionReason = null)
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
        Buyer = buyer;
        SupplyPeriodStart = supplyPeriodStart;
        SupplyPeriodEnd = supplyPeriodEnd;
        PaymentDeadline = paymentDeadline;
        OriginalInvoiceId = originalInvoiceId;
        OriginalIic = originalIic;
        OriginalIssueDateTime = originalIssueDateTime;
        CorrectiveType = correctiveType;
        CorrectionReason = string.IsNullOrWhiteSpace(correctionReason) ? null : correctionReason.Trim();
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
    public string InvoiceNumber { get; private set; }
    public string? OfficialInvoiceNumber { get; private set; }
    public DateTimeOffset IssueDateTime { get; }
    public string Currency { get; }
    public string IdempotencyKey { get; }
    public FiscalBuyer? Buyer { get; }
    public DateOnly? SupplyPeriodStart { get; }
    public DateOnly? SupplyPeriodEnd { get; }
    public DateOnly? PaymentDeadline { get; }
    public Guid? OriginalInvoiceId { get; }
    public string? OriginalIic { get; }
    public DateTimeOffset? OriginalIssueDateTime { get; }
    public CorrectiveInvoiceType? CorrectiveType { get; }
    public string? CorrectionReason { get; }
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
        string? officialInvoiceNumber,
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
        FiscalBuyer? buyer,
        DateOnly? supplyPeriodStart,
        DateOnly? supplyPeriodEnd,
        DateOnly? paymentDeadline,
        Guid? originalInvoiceId,
        string? originalIic,
        DateTimeOffset? originalIssueDateTime,
        CorrectiveInvoiceType? correctiveType,
        string? correctionReason,
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
            idempotencyKey,
            buyer,
            supplyPeriodStart,
            supplyPeriodEnd,
            paymentDeadline,
            originalInvoiceId,
            originalIic,
            originalIssueDateTime,
            correctiveType,
            correctionReason)
        {
            Id = id,
            OfficialInvoiceNumber = officialInvoiceNumber,
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

    public static FiscalInvoice CreateFullStorno(
        FiscalInvoice original,
        string invoiceNumber,
        DateTimeOffset issueDateTime,
        string idempotencyKey,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Status != FiscalStatus.Fiscalized ||
            string.IsNullOrWhiteSpace(original.Iic) ||
            string.IsNullOrWhiteSpace(original.Jikr))
        {
            throw new InvalidOperationException("Samo uspješno fiskalizovan račun može biti storniran.");
        }
        if (original.InvoiceType == InvoiceType.Corrective)
        {
            throw new InvalidOperationException("Korektivni račun se ne može stornirati ovim workflow-om.");
        }

        var storno = new FiscalInvoice(
            original.CompanyId,
            original.BusinessUnitId,
            original.DeviceId,
            original.OperatorId,
            InvoiceType.Corrective,
            invoiceNumber,
            issueDateTime,
            original.Currency,
            idempotencyKey,
            original.Buyer,
            original.SupplyPeriodStart,
            original.SupplyPeriodEnd,
            original.PaymentDeadline,
            original.Id,
            original.Iic,
            original.IssueDateTime,
            CorrectiveInvoiceType.Corrective,
            reason);

        foreach (var item in original.Items)
        {
            storno.AddItem(new FiscalInvoiceItem(
                item.Name,
                -item.Quantity,
                item.UnitPrice,
                item.VatRate,
                item.ItemCode,
                item.UnitOfMeasure,
                -item.DiscountAmount));
        }

        foreach (var payment in original.Payments)
        {
            storno.AddPayment(new FiscalPayment(
                payment.PaymentType,
                -payment.Amount,
                payment.Reference));
        }

        return storno;
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

    public void MarkFiscalized(
        string jikr,
        string officialInvoiceNumber,
        string? qrCodeData = null)
    {
        if (Status != FiscalStatus.FiscalizationPending)
        {
            throw new InvalidOperationException("Račun prethodno nije označen kao poslat PU.");
        }

        Jikr = string.IsNullOrWhiteSpace(jikr)
            ? throw new ArgumentException("JIKR je obavezan.", nameof(jikr))
            : jikr;
        OfficialInvoiceNumber = string.IsNullOrWhiteSpace(officialInvoiceNumber)
            ? throw new ArgumentException("Zvanični fiskalni broj je obavezan.", nameof(officialInvoiceNumber))
            : officialInvoiceNumber.Trim();
        QrCodeData = qrCodeData;
        Status = FiscalStatus.Fiscalized;
        FiscalizedAt = DateTimeOffset.UtcNow;
        UpdatedAt = FiscalizedAt.Value;
    }

    public void SetOfficialInvoiceNumber(string officialInvoiceNumber)
    {
        if (Status is not (FiscalStatus.Fiscalized or FiscalStatus.StornoCreated))
            throw new InvalidOperationException("Zvanični broj se može dopuniti samo fiskalizovanom dokumentu.");
        OfficialInvoiceNumber = string.IsNullOrWhiteSpace(officialInvoiceNumber)
            ? throw new ArgumentException("Zvanični fiskalni broj je obavezan.", nameof(officialInvoiceNumber))
            : officialInvoiceNumber.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
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

    public void MarkStornoCreated()
    {
        if (Status != FiscalStatus.Fiscalized || InvoiceType == InvoiceType.Corrective)
        {
            throw new InvalidOperationException("Samo originalni fiskalizovani račun može biti označen kao storniran.");
        }

        Status = FiscalStatus.StornoCreated;
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

    public void AssignInvoiceNumber(string invoiceNumber)
    {
        EnsureDraft();
        InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber)
            ? throw new ArgumentException("Broj računa je obavezan.", nameof(invoiceNumber))
            : invoiceNumber.Trim();
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
