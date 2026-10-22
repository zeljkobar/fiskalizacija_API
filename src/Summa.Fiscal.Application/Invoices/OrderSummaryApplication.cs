using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public sealed record CreateSummaryCommand(Guid CompanyId, Guid DeviceId, Guid OperatorId,
    IReadOnlyCollection<Guid> OrderInvoiceIds, DateTimeOffset IssueDateTime,
    IReadOnlyCollection<CreateFiscalPayment> Payments, string IdempotencyKey, string CorrelationId, string Actor);
public sealed record CreateOrderCorrectionCommand(Guid OrderInvoiceId, DateTimeOffset IssueDateTime, string Reason,
    IReadOnlyCollection<OrderCorrectionAllocation> Allocations, string IdempotencyKey, string CorrelationId, string Actor);
public sealed record CreateSummaryCorrectionCommand(Guid SummaryInvoiceId, DateTimeOffset IssueDateTime, string Reason,
    IReadOnlyCollection<CreateFiscalPayment> Payments, string IdempotencyKey, string CorrelationId, string Actor);
public sealed record OrderWorkflowResult(Guid OrderInvoiceId, decimal RemainingAmount,
    Guid? SummaryInvoiceId, FiscalStatus? SummaryStatus, decimal PendingSummaryCorrectionAmount,
    IReadOnlyCollection<Guid> CorrectionInvoiceIds, string Stage);
public sealed record SummaryWorkflowResult(Guid SummaryInvoiceId, decimal RemainingAmount,
    decimal PendingCorrectionAmount, IReadOnlyCollection<Guid> PendingOrderCorrectionIds,
    IReadOnlyCollection<Guid> AdjustmentInvoiceIds, string Stage);

public sealed partial class FiscalInvoiceApplicationService
{
    public async Task<FiscalInvoiceResult> CreateSummaryAsync(CreateSummaryCommand command, CancellationToken cancellationToken)
    {
        if (command.OrderInvoiceIds.Count is < 1 or > 1000 || command.OrderInvoiceIds.Distinct().Count() != command.OrderInvoiceIds.Count)
            throw new FiscalInvoiceOperationException("SUMMARY_SOURCES_INVALID", "Navesti 1–1000 različitih ORDER računa.");
        var existing = await repository.GetByIdempotencyKeyAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.InvoiceType != InvoiceType.Summary || !existing.OrderSummary!.OrderInvoiceIds.Order().SequenceEqual(command.OrderInvoiceIds.Order()))
                throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Ključ pripada drugom sumarnom dokumentu.");
            return Map(existing);
        }
        var orders = new List<FiscalInvoice>();
        var corrections = new List<FiscalInvoice>();
        foreach (var id in command.OrderInvoiceIds.Order())
        {
            var order = await RequireWorkflowInvoiceAsync(id, cancellationToken);
            if (order.CompanyId != command.CompanyId) throw new FiscalInvoiceOperationException("SUMMARY_CONTEXT_MISMATCH", "ORDER račun pripada drugoj firmi.");
            orders.Add(order);
            corrections.AddRange(await repository.GetCorrectionsAsync(id, cancellationToken));
        }
        OrderSummaryRules.EnsureSources(orders, corrections, command.IssueDateTime);
        var invoice = FiscalInvoice.CreateSummary(orders, corrections.Where(OrderSummaryRules.IsSuccessful).ToArray(),
            command.DeviceId, command.OperatorId, "PENDING", command.IssueDateTime, command.IdempotencyKey, SummaryPayments(command.Payments));
        return await SaveWorkflowInvoiceAsync(invoice, command.CorrelationId, command.Actor, "FISCAL_SUMMARY_PREPARED", cancellationToken);
    }

    public async Task<FiscalInvoiceResult> CreateOrderCorrectionAsync(CreateOrderCorrectionCommand command, CancellationToken cancellationToken)
    {
        var order = await RequireWorkflowInvoiceAsync(command.OrderInvoiceId, cancellationToken);
        var existing = await repository.GetByIdempotencyKeyAsync(order.CompanyId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsOrderCorrection || existing.OriginalInvoiceId != order.Id) throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Ključ pripada drugom dokumentu.");
            return Map(existing);
        }
        var previous = await repository.GetCorrectionsAsync(order.Id, cancellationToken);
        var allocations = command.Allocations.Count > 0 ? command.Allocations : order.Items.Select(item =>
            new OrderCorrectionAllocation(item.Id, item.Quantity - previous.Where(x => x.Status != FiscalStatus.Cancelled)
                .SelectMany(x => x.OrderSummary?.OrderAllocations ?? []).Where(x => x.OrderItemId == item.Id).Sum(x => x.Quantity)))
            .Where(x => x.Quantity > 0).ToArray();
        if (allocations.Count == 0 || allocations.Any(x => x.Quantity <= 0 || !order.Items.Any(i => i.Id == x.OrderItemId)))
            throw new FiscalInvoiceOperationException("ORDER_ALLOCATION_INVALID", "Nema preostalih količina ili raspodjela nije ispravna.");
        FiscalInvoice correction;
        try { correction = FiscalInvoice.CreateOrderCorrection(order, "PENDING", command.IssueDateTime, command.IdempotencyKey, command.Reason, allocations); }
        catch (InvalidOperationException ex) { throw new FiscalInvoiceOperationException("ORDER_CORRECTION_INVALID", ex.Message); }
        OrderSummaryRules.EnsureOrderCorrection(order, correction, previous);
        OrderSummaryRules.EnsureSummaryResolved(await repository.GetSummaryForOrderAsync(order.Id, cancellationToken));
        return await SaveWorkflowInvoiceAsync(correction, command.CorrelationId, command.Actor, "FISCAL_ORDER_CORRECTION_PREPARED", cancellationToken);
    }

    public async Task<FiscalInvoiceResult> CreateSummaryCorrectionAsync(CreateSummaryCorrectionCommand command, CancellationToken cancellationToken)
    {
        var summary = await RequireWorkflowInvoiceAsync(command.SummaryInvoiceId, cancellationToken);
        var existing = await repository.GetByIdempotencyKeyAsync(summary.CompanyId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsSummaryCorrection || existing.IsSummaryCancellation || existing.OriginalInvoiceId != summary.Id) throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Ključ pripada drugom dokumentu.");
            return Map(existing);
        }
        if (summary.InvoiceType != InvoiceType.Summary || summary.OrderSummary is null)
            throw new FiscalInvoiceOperationException("SUMMARY_REQUIRED", "Očekivan je sumarni račun.");
        var sourceCorrections = await SourceCorrectionsAsync(summary, cancellationToken);
        var previous = await repository.GetCorrectionsAsync(summary.Id, cancellationToken);
        var pending = OrderSummaryRules.PendingSummaryCorrections(summary, sourceCorrections, previous);
        if (pending.Count == 0) throw new FiscalInvoiceOperationException("SUMMARY_ALREADY_RECONCILED", "Nema novih fiskalizovanih korekcija za usklađivanje.");
        var invoice = FiscalInvoice.CreateSummaryCorrection(summary, pending, "PENDING", command.IssueDateTime,
            command.IdempotencyKey, command.Reason, SummaryPayments(command.Payments));
        OrderSummaryRules.EnsureSummaryCorrection(summary, invoice, sourceCorrections, previous);
        return await SaveWorkflowInvoiceAsync(invoice, command.CorrelationId, command.Actor, "FISCAL_SUMMARY_CORRECTION_PREPARED", cancellationToken);
    }

    public async Task<FiscalInvoiceResult> CreateSummaryCancellationAsync(CreateSummaryCorrectionCommand command, CancellationToken ct)
    {
        var summary = await RequireWorkflowInvoiceAsync(command.SummaryInvoiceId, ct);
        var existing = await repository.GetByIdempotencyKeyAsync(summary.CompanyId, command.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (!existing.IsSummaryCancellation || existing.OriginalInvoiceId != summary.Id)
                throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Ključ pripada drugom dokumentu.");
            return Map(existing);
        }
        if (summary.InvoiceType != InvoiceType.Summary || summary.OrderSummary is null)
            throw new FiscalInvoiceOperationException("SUMMARY_REQUIRED", "Očekivan je sumarni račun.");
        var previous = await repository.GetCorrectionsAsync(summary.Id, ct);
        var invoice = FiscalInvoice.CreateSummaryCancellation(summary, previous.Where(OrderSummaryRules.IsSuccessful).ToArray(),
            "PENDING", command.IssueDateTime, command.IdempotencyKey, command.Reason, SummaryPayments(command.Payments));
        OrderSummaryRules.EnsureSummaryCorrection(summary, invoice, [], previous);
        return await SaveWorkflowInvoiceAsync(invoice, command.CorrelationId, command.Actor, "FISCAL_SUMMARY_CANCELLATION_PREPARED", ct);
    }

    public async Task<OrderWorkflowResult> GetOrderWorkflowAsync(Guid id, CancellationToken ct)
    {
        var order = await RequireWorkflowInvoiceAsync(id, ct);
        if (!order.IsOrder) throw new FiscalInvoiceOperationException("ORDER_REQUIRED", "Očekivan je ORDER račun.");
        var corrections = await repository.GetCorrectionsAsync(id, ct);
        var summary = await repository.GetSummaryForOrderAsync(id, ct);
        var remaining = order.TotalGrossAmount + corrections.Where(OrderSummaryRules.IsSuccessful).Sum(x => x.TotalGrossAmount);
        var pending = 0m;
        if (summary is not null && OrderSummaryRules.IsSuccessful(summary))
        {
            var accounted = summary.OrderSummary!.AccountedCorrectionIds.Concat((await repository.GetCorrectionsAsync(summary.Id, ct))
                .Where(OrderSummaryRules.IsSuccessful).SelectMany(x => x.OrderSummary!.AccountedCorrectionIds)).ToHashSet();
            pending = -corrections.Where(x => OrderSummaryRules.IsSuccessful(x) && !accounted.Contains(x.Id)).Sum(x => x.TotalGrossAmount);
        }
        return new(id, remaining, summary?.Id, summary?.Status, pending, corrections.Select(x => x.Id).ToArray(),
            pending > 0 ? "AwaitingSummaryCorrection" : summary is not null && OrderSummaryRules.IsSuccessful(summary) ? "Closed"
            : remaining == 0 ? "FullyCorrected" : summary is not null ? "AwaitingSummaryFiscalization" : "Open");
    }

    public async Task<SummaryWorkflowResult> GetSummaryWorkflowAsync(Guid id, CancellationToken ct)
    {
        var summary = await RequireWorkflowInvoiceAsync(id, ct);
        if (summary.InvoiceType != InvoiceType.Summary || summary.OrderSummary is null) throw new FiscalInvoiceOperationException("SUMMARY_REQUIRED", "Očekivan je sumarni račun.");
        var previous = await repository.GetCorrectionsAsync(id, ct);
        if (previous.Any(x => x.IsSummaryCancellation && OrderSummaryRules.IsSuccessful(x)))
            return new(id, 0, 0, [], previous.Select(x => x.Id).ToArray(), "Cancelled");
        var accounted = summary.OrderSummary.AccountedCorrectionIds.Concat(previous.Where(OrderSummaryRules.IsSuccessful)
            .SelectMany(x => x.OrderSummary!.AccountedCorrectionIds)).ToHashSet();
        var pending = (await SourceCorrectionsAsync(summary, ct)).Where(x => OrderSummaryRules.IsSuccessful(x) && !accounted.Contains(x.Id)).ToArray();
        return new(id, summary.TotalGrossAmount + previous.Where(OrderSummaryRules.IsSuccessful).Sum(x => x.TotalGrossAmount),
            -pending.Sum(x => x.TotalGrossAmount), pending.Select(x => x.Id).ToArray(), previous.Select(x => x.Id).ToArray(),
            pending.Length > 0 ? "AwaitingSummaryCorrection" : OrderSummaryRules.IsSuccessful(summary) ? "Reconciled" : "AwaitingSummaryFiscalization");
    }

    private async Task<IReadOnlyCollection<FiscalInvoice>> SourceCorrectionsAsync(FiscalInvoice summary, CancellationToken ct)
    {
        var result = new List<FiscalInvoice>();
        foreach (var id in summary.OrderSummary!.OrderInvoiceIds) result.AddRange(await repository.GetCorrectionsAsync(id, ct));
        return result;
    }
    private async Task<FiscalInvoice> RequireWorkflowInvoiceAsync(Guid id, CancellationToken ct) =>
        await repository.GetByIdAsync(id, ct) ?? throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "Račun nije pronađen.", 404);
    private static FiscalPayment[] SummaryPayments(IReadOnlyCollection<CreateFiscalPayment> payments)
    {
        if (payments.Any(x => x.AdvanceInvoiceId.HasValue)) throw new FiscalInvoiceOperationException("SUMMARY_PAYMENT_INVALID", "Sumarni workflow ne koristi avansne reference.");
        return payments.Select(x => new FiscalPayment(x.PaymentType, x.Amount, x.Reference)).ToArray();
    }
    private async Task<FiscalInvoiceResult> SaveWorkflowInvoiceAsync(FiscalInvoice invoice, string correlationId, string actor, string action, CancellationToken ct)
    {
        var validation = validator.Validate(invoice);
        if (!validation.IsValid) throw new FiscalValidationException(validation.Errors);
        invoice.AssignInvoiceNumber(await ReserveInvoiceNumberAsync(invoice.DeviceId, invoice.IssueDateTime, ct));
        invoice.MarkValidated(); invoice.MarkReadyForFiscalization();
        try
        {
            await repository.AddAsync(invoice, ct, new(action, invoice.Id, invoice.CompanyId, correlationId, actor, DateTimeOffset.UtcNow,
                new Dictionary<string, string?> { ["originalInvoiceId"] = invoice.OriginalInvoiceId?.ToString(),
                    ["orderInvoiceIds"] = string.Join(',', invoice.OrderSummary!.OrderInvoiceIds), ["informational"] = invoice.IsInformational.ToString() }));
        }
        catch (Exception ex) when (ex is FiscalInvoiceOperationException or InvalidOperationException)
        {
            // The competing transaction may have committed the same idempotency key while we waited on its ORDER locks.
            var existing = await repository.GetByIdempotencyKeyAsync(invoice.CompanyId, invoice.IdempotencyKey, ct);
            if (existing is not null && existing.InvoiceType == invoice.InvoiceType && existing.OriginalInvoiceId == invoice.OriginalInvoiceId &&
                existing.IsSummaryCancellation == invoice.IsSummaryCancellation && existing.OrderSummary is not null &&
                existing.OrderSummary.OrderInvoiceIds.Order().SequenceEqual(invoice.OrderSummary!.OrderInvoiceIds.Order())) return Map(existing);
            throw;
        }
        return Map(invoice);
    }
}
