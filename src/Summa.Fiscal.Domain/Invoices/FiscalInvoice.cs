namespace Summa.Fiscal.Domain.Invoices;

public sealed partial class FiscalInvoice
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
    public Guid? AdvanceSettlementFinalInvoiceId { get; private set; }
    public string? AdvanceSettlementFinalIic { get; private set; }
    public DateTimeOffset? AdvanceSettlementFinalIssuedAt { get; private set; }
    public Guid? AdvanceRestorationStornoInvoiceId { get; private set; }
    public string? AdvanceRestorationStornoIic { get; private set; }
    public DateTimeOffset? AdvanceRestorationStornoIssuedAt { get; private set; }
    public bool IsAdvanceRestoration => AdvanceRestorationStornoInvoiceId.HasValue;
    public IReadOnlyCollection<AdvanceSettlementAllocation> AdvanceSettlementAllocations { get; private set; } = [];
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
        IEnumerable<(Guid Id, PaymentType PaymentType, decimal Amount, string? Reference,
            Guid? AdvanceInvoiceId, string? AdvanceIic)> payments,
        Guid? advanceSettlementFinalInvoiceId = null,
        string? advanceSettlementFinalIic = null,
        DateTimeOffset? advanceSettlementFinalIssuedAt = null,
        IReadOnlyCollection<AdvanceSettlementAllocation>? advanceSettlementAllocations = null,
        Guid? advanceRestorationStornoInvoiceId = null,
        string? advanceRestorationStornoIic = null,
        DateTimeOffset? advanceRestorationStornoIssuedAt = null,
        OrderSummaryData? orderSummary = null)
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
        invoice.AdvanceSettlementFinalInvoiceId = advanceSettlementFinalInvoiceId;
        invoice.AdvanceSettlementFinalIic = advanceSettlementFinalIic;
        invoice.AdvanceSettlementFinalIssuedAt = advanceSettlementFinalIssuedAt;
        invoice.AdvanceSettlementAllocations = advanceSettlementAllocations ?? [];
        invoice.AdvanceRestorationStornoInvoiceId = advanceRestorationStornoInvoiceId;
        invoice.AdvanceRestorationStornoIic = advanceRestorationStornoIic;
        invoice.AdvanceRestorationStornoIssuedAt = advanceRestorationStornoIssuedAt;
        invoice.OrderSummary = orderSummary;

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
                payment.Reference,
                payment.AdvanceInvoiceId,
                payment.AdvanceIic));
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
            null,
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
                payment.Reference,
                payment.AdvanceInvoiceId,
                payment.AdvanceIic));
        }

        return storno;
    }

    public static FiscalInvoice CreateAdvanceSettlement(
        FiscalInvoice advance,
        FiscalInvoice final,
        string invoiceNumber,
        DateTimeOffset issuedAt,
        string idempotencyKey,
        IReadOnlyCollection<AdvanceSettlementAllocation> allocations)
    {
        if (advance.InvoiceType != InvoiceType.Advance || advance.Status != FiscalStatus.Fiscalized ||
            final.InvoiceType != InvoiceType.Normal || final.Status != FiscalStatus.Fiscalized ||
            string.IsNullOrWhiteSpace(final.Iic) || string.IsNullOrWhiteSpace(final.Jikr) ||
            advance.CompanyId != final.CompanyId)
            throw new InvalidOperationException("Umanjenje zahtijeva fiskalizovani avans i konačni račun iste firme.");
        var payment = final.Payments.SingleOrDefault(x => x.AdvanceInvoiceId == advance.Id);
        if (payment is null || allocations.Count == 0 ||
            allocations.Any(x => x.GrossAmount <= 0 || decimal.Round(x.GrossAmount, 2) != x.GrossAmount) ||
            allocations.Select(x => x.AdvanceItemId).Distinct().Count() != allocations.Count ||
            allocations.Sum(x => x.GrossAmount) != payment.Amount)
            throw new InvalidOperationException("Raspodjela umanjenja mora odgovarati iznosu avansa korišćenom na konačnom računu.");

        var correction = new FiscalInvoice(
            advance.CompanyId, advance.BusinessUnitId, advance.DeviceId, advance.OperatorId,
            InvoiceType.Corrective, invoiceNumber, issuedAt, advance.Currency, idempotencyKey,
            advance.Buyer, originalInvoiceId: advance.Id, originalIic: advance.Iic,
            originalIssueDateTime: advance.IssueDateTime, correctiveType: CorrectiveInvoiceType.Corrective,
            correctionReason: "Umanjenje avansa po konačnom računu " + (final.OfficialInvoiceNumber ?? final.InvoiceNumber));
        correction.AdvanceSettlementFinalInvoiceId = final.Id;
        correction.AdvanceSettlementFinalIic = final.Iic;
        correction.AdvanceSettlementFinalIssuedAt = final.IssueDateTime;
        correction.AdvanceSettlementAllocations = allocations.ToArray();
        foreach (var allocation in allocations)
        {
            var source = advance.Items.SingleOrDefault(x => x.Id == allocation.AdvanceItemId)
                ?? throw new InvalidOperationException("Raspodjela navodi stavku koja ne pripada avansu.");
            if (allocation.GrossAmount > source.GrossAmount)
                throw new InvalidOperationException("Iznos umanjenja prelazi iznos izvorne stavke avansa.");
            correction.AddItem(new FiscalInvoiceItem(
                source.Name, -1, allocation.GrossAmount, source.VatRate, source.ItemCode, source.UnitOfMeasure));
        }
        // This is a tax reduction of the original bank receipt, not a second cash refund.
        foreach (var group in advance.Payments.GroupBy(x => x.PaymentType))
        {
            if (advance.Payments.Count != 1 || group.Key != PaymentType.BankAccount)
                throw new InvalidOperationException("Umanjenje avansa trenutno podržava avans uplaćen jednim bankovnim plaćanjem.");
            correction.AddPayment(new FiscalPayment(PaymentType.BankAccount, -payment.Amount));
        }
        return correction;
    }

    public void CancelUnsubmitted()
    {
        if (Status != FiscalStatus.ReadyForFiscalization || !string.IsNullOrWhiteSpace(Iic) || !string.IsNullOrWhiteSpace(Jikr))
            throw new InvalidOperationException("Može se otkazati samo račun koji nikada nije poslat Poreskoj upravi.");
        Status = FiscalStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static FiscalInvoice CreateAdvanceRestoration(
        FiscalInvoice settlement, FiscalInvoice finalStorno,
        string invoiceNumber, DateTimeOffset issuedAt, string idempotencyKey)
    {
        if (settlement.InvoiceType != InvoiceType.Corrective || settlement.AdvanceSettlementFinalInvoiceId is null ||
            settlement.Status != FiscalStatus.Fiscalized || string.IsNullOrWhiteSpace(settlement.Iic) ||
            string.IsNullOrWhiteSpace(settlement.Jikr) || settlement.TotalGrossAmount >= 0 ||
            finalStorno.InvoiceType != InvoiceType.Corrective || finalStorno.Status != FiscalStatus.Fiscalized ||
            string.IsNullOrWhiteSpace(finalStorno.Iic) || string.IsNullOrWhiteSpace(finalStorno.Jikr) ||
            finalStorno.OriginalInvoiceId != settlement.AdvanceSettlementFinalInvoiceId ||
            finalStorno.CompanyId != settlement.CompanyId || finalStorno.Currency != settlement.Currency ||
            issuedAt < finalStorno.IssueDateTime || issuedAt < settlement.IssueDateTime)
            throw new InvalidOperationException("Obnova avansa zahtijeva fiskalizovano umanjenje i pripadajući fiskalizovani storno konačnog računa.");

        var payment = finalStorno.Payments.SingleOrDefault(x => x.PaymentType == PaymentType.Advance);
        if (payment is null || payment.AdvanceInvoiceId != settlement.OriginalInvoiceId || payment.Amount != settlement.TotalGrossAmount)
            throw new InvalidOperationException("Storno i umanjenje ne odnose se na isti avans i iznos.");

        var restoration = new FiscalInvoice(settlement.CompanyId, settlement.BusinessUnitId,
            settlement.DeviceId, settlement.OperatorId, InvoiceType.Corrective, invoiceNumber, issuedAt,
            settlement.Currency, idempotencyKey, settlement.Buyer,
            originalInvoiceId: settlement.Id, originalIic: settlement.Iic,
            originalIssueDateTime: settlement.IssueDateTime, correctiveType: CorrectiveInvoiceType.Corrective,
            correctionReason: "Poništenje umanjenja avansa nakon storna konačnog računa " + finalStorno.OfficialInvoiceNumber)
        {
            AdvanceRestorationStornoInvoiceId = finalStorno.Id,
            AdvanceRestorationStornoIic = finalStorno.Iic,
            AdvanceRestorationStornoIssuedAt = finalStorno.IssueDateTime,
            AdvanceSettlementAllocations = settlement.AdvanceSettlementAllocations.ToArray()
        };
        foreach (var item in settlement.Items)
            restoration.AddItem(new(item.Name, -item.Quantity, item.UnitPrice, item.VatRate,
                item.ItemCode, item.UnitOfMeasure, -item.DiscountAmount));
        foreach (var method in settlement.Payments)
            restoration.AddPayment(new(method.PaymentType, -method.Amount, method.Reference));
        return restoration;
    }

    public void MarkAdvanceSettlementReversed()
    {
        if (Status != FiscalStatus.Fiscalized || InvoiceType != InvoiceType.Corrective || AdvanceSettlementFinalInvoiceId is null)
            throw new InvalidOperationException("Samo fiskalizovano umanjenje avansa može biti označeno kao poništeno obnovom.");
        Status = FiscalStatus.StornoCreated;
        UpdatedAt = DateTimeOffset.UtcNow;
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
        string? qrCodeData = null,
        DateTimeOffset? fiscalizedAt = null)
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
        FiscalizedAt = (fiscalizedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
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
        // Informational documents sum immutable fiscal source snapshots, never recompute tax from a synthetic item.
        TotalNetAmount = RoundMoney(IsInformational && OrderSummary is not null ? OrderSummary.References.Sum(x => x.NetAmount) : _items.Sum(item => item.NetAmount));
        TotalVatAmount = RoundMoney(IsInformational && OrderSummary is not null ? OrderSummary.References.Sum(x => x.VatAmount) : _items.Sum(item => item.VatAmount));
        TotalGrossAmount = RoundMoney(IsInformational && OrderSummary is not null ? OrderSummary.References.Sum(x => x.GrossAmount) : _items.Sum(item => item.GrossAmount));
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

public sealed record AdvanceSettlementAllocation(Guid AdvanceItemId, decimal GrossAmount);
