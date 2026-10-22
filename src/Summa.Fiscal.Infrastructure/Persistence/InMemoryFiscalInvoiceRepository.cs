using System.Collections.Concurrent;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Infrastructure.Persistence;

public sealed partial class InMemoryFiscalInvoiceRepository : IFiscalInvoiceRepository
{
    private readonly ConcurrentDictionary<Guid, FiscalInvoice> _invoices = new();
    private readonly ConcurrentDictionary<(Guid CompanyId, string Key), Guid> _idempotencyIndex = new();
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<(Guid AdvanceId, Guid FinalId), bool> _released = new();
    private readonly ConcurrentQueue<AuditEntry> _auditEntries = new();
    public IReadOnlyCollection<AuditEntry> AuditEntries => _auditEntries.ToArray();

    public Task<FiscalInvoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _invoices.TryGetValue(id, out var invoice);
        return Task.FromResult(invoice is null ? null : Copy(invoice));
    }

    public Task<FiscalInvoice?> GetByIdempotencyKeyAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_idempotencyIndex.TryGetValue((companyId, idempotencyKey), out var invoiceId) &&
            _invoices.TryGetValue(invoiceId, out var invoice))
        {
            return Task.FromResult<FiscalInvoice?>(Copy(invoice));
        }

        return Task.FromResult<FiscalInvoice?>(null);
    }

    public Task<FiscalInvoice?> GetByOriginalInvoiceIdAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invoice = _invoices.Values.SingleOrDefault(x => x.OriginalInvoiceId == originalInvoiceId &&
            x.AdvanceSettlementFinalInvoiceId is null && !x.IsOrderCorrection && !x.IsSummaryCorrection && x.Status != FiscalStatus.Cancelled);
        return Task.FromResult(invoice is null ? null : Copy(invoice));
    }

    public Task<IReadOnlyCollection<FiscalInvoice>> GetAdvanceSettlementsAsync(Guid advanceInvoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<FiscalInvoice>>(_invoices.Values
            .Where(x => x.OriginalInvoiceId == advanceInvoiceId && x.AdvanceSettlementFinalInvoiceId.HasValue)
            .Select(Copy).ToArray());

    public Task CancelUnsubmittedAsync(Guid invoiceId, CancellationToken cancellationToken, AuditEntry? auditEntry = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            if (!_invoices.TryGetValue(invoiceId, out var stored))
                throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "Račun nije pronađen.", 404);
            if (stored.Status == FiscalStatus.Cancelled) return Task.CompletedTask;
            var invoice = Copy(stored);
            try { invoice.CancelUnsubmitted(); }
            catch (InvalidOperationException exception)
            { throw new FiscalInvoiceOperationException("INVOICE_CANNOT_BE_CANCELLED", exception.Message); }
            _invoices[invoiceId] = invoice;
            if (auditEntry is not null) _auditEntries.Enqueue(auditEntry);
        }
        return Task.CompletedTask;
    }

    public Task<AdvanceUsageSnapshot?> GetAdvanceUsageAsync(
        Guid advanceInvoiceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_invoices.TryGetValue(advanceInvoiceId, out var advance) ||
            advance.InvoiceType != InvoiceType.Advance ||
            string.IsNullOrWhiteSpace(advance.Iic))
        {
            return Task.FromResult<AdvanceUsageSnapshot?>(null);
        }

        var usages = _invoices.Values.Where(x => x.InvoiceType == InvoiceType.Normal && x.Status != FiscalStatus.Cancelled &&
                !_released.ContainsKey((advanceInvoiceId, x.Id)))
            .SelectMany(final => final.Payments
                .Where(payment => payment.PaymentType == PaymentType.Advance &&
                    payment.AdvanceInvoiceId == advanceInvoiceId)
                .Select(payment => new AdvanceUsageEntry(
                    final.Id,
                    final.InvoiceNumber,
                    payment.Amount,
                    final.Status,
                    _invoices.Values.SingleOrDefault(x => x.OriginalInvoiceId == advanceInvoiceId &&
                        x.AdvanceSettlementFinalInvoiceId == final.Id && x.Status != FiscalStatus.Cancelled)?.Id,
                    _invoices.Values.SingleOrDefault(x => x.OriginalInvoiceId == advanceInvoiceId &&
                        x.AdvanceSettlementFinalInvoiceId == final.Id && x.Status != FiscalStatus.Cancelled)?.Status)))
            .ToArray();
        return Task.FromResult<AdvanceUsageSnapshot?>(new(
            advance.Id,
            advance.Iic!,
            advance.TotalGrossAmount,
            usages.Sum(x => x.UsedAmount),
            usages,
            advance.Status == FiscalStatus.Fiscalized && !_invoices.Values.Any(x => x.OriginalInvoiceId == advance.Id &&
                x.AdvanceSettlementFinalInvoiceId is null && x.Status != FiscalStatus.Cancelled)));
    }

    public Task AddAsync(FiscalInvoice invoice, CancellationToken cancellationToken, AuditEntry? auditEntry = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            FiscalInvoice? cancelledSettlement = null;
            var cancelledSummary = PrepareOrderSummary(invoice);
            foreach (var group in invoice.Payments
                .Where(payment => payment.PaymentType == PaymentType.Advance && invoice.InvoiceType == InvoiceType.Normal)
                .GroupBy(payment => payment.AdvanceInvoiceId))
            {
                if (!group.Key.HasValue ||
                    !_invoices.TryGetValue(group.Key.Value, out var advance) ||
                    advance.InvoiceType != InvoiceType.Advance ||
                    advance.Status != FiscalStatus.Fiscalized ||
                    string.IsNullOrWhiteSpace(advance.Iic))
                {
                    throw new FiscalInvoiceOperationException(
                        "ADVANCE_NOT_AVAILABLE",
                        "Navedeni avans nije fiskalizovan ili više nije raspoloživ.");
                }
                var alreadyUsed = _invoices.Values.Where(x => x.InvoiceType == InvoiceType.Normal && x.Status != FiscalStatus.Cancelled &&
                    !_released.ContainsKey((advance.Id, x.Id))).Sum(final => final.Payments
                    .Where(payment => payment.PaymentType == PaymentType.Advance &&
                        payment.AdvanceInvoiceId == advance.Id)
                    .Sum(payment => payment.Amount));
                var requested = group.Sum(payment => payment.Amount);
                AdvanceInvoiceRules.EnsureAvailable(advance, invoice, requested, alreadyUsed);
                if (_invoices.Values.Any(x => x.OriginalInvoiceId == advance.Id && x.AdvanceSettlementFinalInvoiceId is null && x.Status != FiscalStatus.Cancelled))
                    throw new FiscalInvoiceOperationException("ADVANCE_STORNO_PENDING", "Za avans postoji storno.");
            }

            if (invoice.OriginalInvoiceId is { } originalId && _invoices.TryGetValue(originalId, out var original))
            {
                if (invoice.IsAdvanceRestoration)
                {
                    if (!_invoices.TryGetValue(invoice.AdvanceRestorationStornoInvoiceId!.Value, out var finalStorno))
                        throw new FiscalInvoiceOperationException("STORNO_NOT_FOUND", "Storno nije pronađen.");
                    try { _ = FiscalInvoice.CreateAdvanceRestoration(original, finalStorno, invoice.InvoiceNumber, invoice.IssueDateTime, invoice.IdempotencyKey); }
                    catch (InvalidOperationException exception)
                    { throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_INVALID", exception.Message); }
                    if (_released.ContainsKey((original.OriginalInvoiceId!.Value, original.AdvanceSettlementFinalInvoiceId!.Value)))
                        throw new FiscalInvoiceOperationException("ADVANCE_RESERVATION_MISSING", "Avans je već oslobođen.");
                }
                else if (original.InvoiceType == InvoiceType.Normal && invoice.Payments.Any(x => x.PaymentType == PaymentType.Advance))
                {
                    AdvanceStornoRules.EnsureFinalStornoReference(invoice, original);
                    var settlement = _invoices.Values.SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == original.Id && x.Status != FiscalStatus.Cancelled);
                    AdvanceStornoRules.EnsureSettlementResolved(original, settlement);
                    if (settlement?.Status == FiscalStatus.ReadyForFiscalization)
                    {
                        cancelledSettlement = Copy(settlement);
                        cancelledSettlement.CancelUnsubmitted();
                    }
                }
                if (original.InvoiceType == InvoiceType.Advance)
                {
                    if (invoice.AdvanceSettlementFinalInvoiceId is { } finalId)
                    {
                        var settlements = _invoices.Values.Where(x => x.OriginalInvoiceId == originalId &&
                            x.AdvanceSettlementFinalInvoiceId.HasValue && x.Status != FiscalStatus.Cancelled).ToArray();
                        if (settlements.Any(x => x.AdvanceSettlementFinalInvoiceId == finalId))
                            throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_ALREADY_EXISTS", "Umanjenje već postoji.");
                        if (!_invoices.TryGetValue(finalId, out var final) || final.Status != FiscalStatus.Fiscalized || original.Status != FiscalStatus.Fiscalized)
                            throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_INVALID", "Konačni račun i avans moraju biti fiskalizovani.");
                        if (_invoices.Values.Any(x => x.OriginalInvoiceId == finalId && x.AdvanceSettlementFinalInvoiceId is null && x.Status != FiscalStatus.Cancelled))
                            throw new FiscalInvoiceOperationException("FINAL_STORNO_EXISTS", "Za konačni račun postoji storno.");
                        AdvanceInvoiceRules.EnsureSettlementAllocationAvailable(original, invoice, settlements);
                    }
                    else if (_invoices.Values.Where(x => x.InvoiceType == InvoiceType.Normal && x.Status != FiscalStatus.Cancelled &&
                        !_released.ContainsKey((originalId, x.Id))).Any(x => x.Payments.Any(p => p.AdvanceInvoiceId == originalId)))
                        throw new FiscalInvoiceOperationException("ADVANCE_ALREADY_USED", "Avans je rezervisan ili iskorišćen.");
                }
                if (!invoice.IsOrderCorrection && !invoice.IsSummaryCorrection && invoice.AdvanceSettlementFinalInvoiceId is null && _invoices.Values.Any(x => x.OriginalInvoiceId == originalId &&
                    x.AdvanceSettlementFinalInvoiceId is null && x.Status != FiscalStatus.Cancelled))
                    throw new FiscalInvoiceOperationException("STORNO_ALREADY_EXISTS", "Storno već postoji.");
            }

            if (!_idempotencyIndex.TryAdd((invoice.CompanyId, invoice.IdempotencyKey), invoice.Id))
            {
                throw new InvalidOperationException("Idempotency key je već iskorišćen.");
            }

            if (!_invoices.TryAdd(invoice.Id, Copy(invoice)))
            {
                _idempotencyIndex.TryRemove((invoice.CompanyId, invoice.IdempotencyKey), out _);
                throw new InvalidOperationException("Račun sa istim identifikatorom već postoji.");
            }
            if (cancelledSettlement is not null)
            {
                _invoices[cancelledSettlement.Id] = cancelledSettlement;
                if (auditEntry is not null)
                    _auditEntries.Enqueue(new("FISCAL_ADVANCE_REDUCTION_CANCELLED_FOR_STORNO", cancelledSettlement.Id, cancelledSettlement.CompanyId,
                        auditEntry.CorrelationId, auditEntry.Actor, DateTimeOffset.UtcNow,
                        new Dictionary<string, string?> { ["finalInvoiceId"] = invoice.OriginalInvoiceId?.ToString(), ["stornoInvoiceId"] = invoice.Id.ToString() }));
            }
            if (auditEntry is not null) _auditEntries.Enqueue(auditEntry);
            if (cancelledSummary is not null)
            {
                _invoices[cancelledSummary.Id] = cancelledSummary;
                if (auditEntry is not null) _auditEntries.Enqueue(new("FISCAL_SUMMARY_CANCELLED_FOR_ORDER_CORRECTION", cancelledSummary.Id,
                    cancelledSummary.CompanyId, auditEntry.CorrelationId, auditEntry.Actor, DateTimeOffset.UtcNow,
                    new Dictionary<string, string?> { ["orderCorrectionId"] = invoice.Id.ToString() }));
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(FiscalInvoice invoice, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_writeLock)
        {
            if (!_invoices.TryGetValue(invoice.Id, out var stored))
                throw new InvalidOperationException("Račun koji se ažurira ne postoji.");
            if (invoice.Status == FiscalStatus.FiscalizationPending && stored.Status is not (FiscalStatus.ReadyForFiscalization or FiscalStatus.FiscalizationFailed))
                throw new FiscalInvoiceOperationException("INVOICE_STATE_CONFLICT", "Račun je već preuzet za slanje ili otkazan.");
            _invoices[invoice.Id] = Copy(invoice);
        }
        return Task.CompletedTask;
    }

    public Task CompleteCorrectiveAsync(
        FiscalInvoice corrective,
        FiscalInvoice original,
        CancellationToken cancellationToken,
        AuditEntry? auditEntry = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_invoices.ContainsKey(corrective.Id) || !_invoices.ContainsKey(original.Id))
            throw new InvalidOperationException("Originalni ili korektivni račun ne postoji.");
        lock (_writeLock)
        {
            if (corrective.IsAdvanceRestoration)
            {
                var finalStorno = _invoices[corrective.AdvanceRestorationStornoInvoiceId!.Value];
                if (finalStorno.Status != FiscalStatus.Fiscalized || finalStorno.OriginalInvoiceId != original.AdvanceSettlementFinalInvoiceId)
                    throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_NOT_READY", "Storno konačnog računa nije fiskalizovan.");
                _released[(original.OriginalInvoiceId!.Value, original.AdvanceSettlementFinalInvoiceId!.Value)] = true;
            }
            else if (original.InvoiceType == InvoiceType.Normal && original.Payments.Any(x => x.PaymentType == PaymentType.Advance))
            {
                var settlement = _invoices.Values.SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == original.Id && x.Status != FiscalStatus.Cancelled);
                AdvanceStornoRules.EnsureSettlementResolved(original, settlement);
                if (settlement is null)
                    _released[(original.Payments.Single(x => x.PaymentType == PaymentType.Advance).AdvanceInvoiceId!.Value, original.Id)] = true;
            }
            _invoices[corrective.Id] = Copy(corrective);
            _invoices[original.Id] = Copy(original);
            if (auditEntry is not null) _auditEntries.Enqueue(auditEntry);
        }
        return Task.CompletedTask;
    }

    private static FiscalInvoice Copy(FiscalInvoice invoice) => FiscalInvoice.Restore(
        invoice.Id, invoice.CompanyId, invoice.BusinessUnitId, invoice.DeviceId, invoice.OperatorId,
        invoice.InvoiceType, invoice.InvoiceNumber, invoice.OfficialInvoiceNumber, invoice.IssueDateTime,
        invoice.Currency, invoice.IdempotencyKey, invoice.Status, invoice.Iic, invoice.IicSignature,
        invoice.Jikr, invoice.QrCodeData, invoice.CreatedAt, invoice.UpdatedAt, invoice.FiscalizedAt,
        invoice.Buyer, invoice.SupplyPeriodStart, invoice.SupplyPeriodEnd, invoice.PaymentDeadline,
        invoice.OriginalInvoiceId, invoice.OriginalIic, invoice.OriginalIssueDateTime, invoice.CorrectiveType,
        invoice.CorrectionReason,
        invoice.Items.Select(x => (x.Id, x.Name, x.Quantity, x.UnitPrice, x.VatRate, x.ItemCode, x.UnitOfMeasure, x.DiscountAmount)),
        invoice.Payments.Select(x => (x.Id, x.PaymentType, x.Amount, x.Reference, x.AdvanceInvoiceId, x.AdvanceIic)),
        invoice.AdvanceSettlementFinalInvoiceId, invoice.AdvanceSettlementFinalIic,
        invoice.AdvanceSettlementFinalIssuedAt, invoice.AdvanceSettlementAllocations,
        invoice.AdvanceRestorationStornoInvoiceId, invoice.AdvanceRestorationStornoIic, invoice.AdvanceRestorationStornoIssuedAt, invoice.OrderSummary);
}
