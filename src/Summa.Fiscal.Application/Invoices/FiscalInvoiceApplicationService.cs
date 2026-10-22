using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public interface IFiscalInvoiceApplicationService
{
    Task<FiscalInvoiceResult> CreateSummaryAsync(CreateSummaryCommand command, CancellationToken cancellationToken);
    Task<FiscalInvoiceResult> CreateOrderCorrectionAsync(CreateOrderCorrectionCommand command, CancellationToken cancellationToken);
    Task<FiscalInvoiceResult> CreateSummaryCorrectionAsync(CreateSummaryCorrectionCommand command, CancellationToken cancellationToken);
    Task<FiscalInvoiceResult> CreateSummaryCancellationAsync(CreateSummaryCorrectionCommand command, CancellationToken cancellationToken);
    Task<OrderWorkflowResult> GetOrderWorkflowAsync(Guid id, CancellationToken cancellationToken);
    Task<SummaryWorkflowResult> GetSummaryWorkflowAsync(Guid id, CancellationToken cancellationToken);
    Task<FiscalInvoiceResult> CreateAsync(
        CreateFiscalInvoiceCommand command,
        CancellationToken cancellationToken);

    Task<FiscalInvoiceResult?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<FiscalInvoiceStatusResult?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    Task<FiscalInvoiceResult?> GetStornoForOriginalAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken);

    Task<AdvanceBalanceResult?> GetAdvanceBalanceAsync(
        Guid advanceInvoiceId,
        CancellationToken cancellationToken);
    Task<FiscalInvoiceResult> CreateAdvanceSettlementAsync(CreateAdvanceSettlementCommand command, CancellationToken cancellationToken);
    Task CancelUnsubmittedAsync(Guid id, string correlationId, string actor, CancellationToken cancellationToken);
    Task<FiscalInvoiceResult> CreateAdvanceRestorationAsync(CreateAdvanceRestorationCommand command, CancellationToken cancellationToken);
    Task<AdvanceStornoWorkflowResult> GetAdvanceStornoWorkflowAsync(Guid finalInvoiceId, CancellationToken cancellationToken);

    Task<FiscalInvoiceResult> CreateStornoAsync(
        CreateStornoCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class FiscalInvoiceApplicationService(
    IFiscalInvoiceRepository repository,
    IFiscalInvoiceValidator validator,
    IAuditService auditService,
    IInvoiceNumberSequence invoiceNumberSequence) : IFiscalInvoiceApplicationService
{
    public async Task<FiscalInvoiceResult> CreateAsync(
        CreateFiscalInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        if (command.InvoiceType == InvoiceType.Summary)
            throw new FiscalInvoiceOperationException("SUMMARY_WORKFLOW_REQUIRED", "Sumarni račun se priprema kroz /summaries iz postojećih ORDER računa.");
        var existing = await repository.GetByIdempotencyKeyAsync(
            command.CompanyId,
            command.IdempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.InvoiceType != command.InvoiceType)
                throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Idempotency ključ je iskorišćen za drugi tip dokumenta.");
            return Map(existing);
        }

        var requiresAutomaticNumber = string.IsNullOrWhiteSpace(command.InvoiceNumber);
        var invoice = new FiscalInvoice(
            command.CompanyId,
            command.BusinessUnitId,
            command.DeviceId,
            command.OperatorId,
            command.InvoiceType,
            requiresAutomaticNumber ? "PENDING" : command.InvoiceNumber,
            command.IssueDateTime,
            command.Currency,
            command.IdempotencyKey,
            command.Buyer is null ? null : new FiscalBuyer(
                command.Buyer.IdentificationType,
                command.Buyer.IdentificationNumber,
                command.Buyer.Name,
                command.Buyer.Address,
                command.Buyer.Town,
                command.Buyer.Country,
                command.Buyer.TaxIdentificationCode),
            command.SupplyPeriodStart,
            command.SupplyPeriodEnd,
            command.PaymentDeadline);

        foreach (var item in command.Items)
        {
            invoice.AddItem(new FiscalInvoiceItem(
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.VatRate,
                item.ItemCode,
                item.UnitOfMeasure,
                item.DiscountAmount));
        }

        var paymentErrors = new List<FiscalValidationError>();
        var paymentIndex = 0;
        foreach (var payment in command.Payments)
        {
            FiscalInvoice? advance = null;
            if (payment.PaymentType == PaymentType.Advance)
            {
                if (!payment.AdvanceInvoiceId.HasValue)
                {
                    paymentErrors.Add(new(
                        "ADVANCE_REFERENCE_REQUIRED",
                        $"payments[{paymentIndex}].advanceInvoiceId",
                        "Za plaćanje avansom mora biti naveden advanceInvoiceId."));
                }
                else
                {
                    advance = await repository.GetByIdAsync(
                        payment.AdvanceInvoiceId.Value,
                        cancellationToken);
                    ValidateAdvanceReference(
                        advance,
                        command.CompanyId,
                        command.Currency,
                        paymentIndex,
                        paymentErrors);
                    if (advance is not null && paymentErrors.Count == 0)
                        AdvanceInvoiceRules.EnsureAvailable(advance, invoice, payment.Amount, 0);
                }
            }
            else if (payment.AdvanceInvoiceId.HasValue)
            {
                paymentErrors.Add(new("ADVANCE_REFERENCE_NOT_ALLOWED", $"payments[{paymentIndex}].advanceInvoiceId", "Referenca na avans dozvoljena je samo za plaćanje Advance."));
            }

            invoice.AddPayment(new FiscalPayment(
                payment.PaymentType,
                payment.Amount,
                payment.Reference,
                advance?.Id,
                advance?.Iic));
            paymentIndex++;
        }

        if (paymentErrors.Count > 0)
        {
            throw new FiscalValidationException(paymentErrors);
        }

        var validation = validator.Validate(invoice);
        if (!validation.IsValid)
        {
            throw new FiscalValidationException(validation.Errors);
        }

        if (requiresAutomaticNumber)
        {
            invoice.AssignInvoiceNumber(await ReserveInvoiceNumberAsync(
                command.DeviceId,
                command.IssueDateTime,
                cancellationToken));
        }

        invoice.MarkValidated();
        invoice.MarkReadyForFiscalization();

        var advanceAmount = invoice.Payments.Where(x => x.PaymentType == PaymentType.Advance).Sum(x => x.Amount);
        await repository.AddAsync(invoice, cancellationToken, advanceAmount > 0
            ? new AuditEntry("FISCAL_ADVANCE_RESERVED", invoice.Id, invoice.CompanyId,
                command.CorrelationId, command.Actor, DateTimeOffset.UtcNow,
                new Dictionary<string, string?>
                {
                    ["advanceInvoiceId"] = invoice.Payments.Single(x => x.PaymentType == PaymentType.Advance).AdvanceInvoiceId!.Value.ToString(),
                    ["amount"] = advanceAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })
            : null);
        await auditService.RecordAsync(
            new AuditEntry(
                "FISCAL_INVOICE_ACCEPTED",
                invoice.Id,
                invoice.CompanyId,
                command.CorrelationId,
                command.Actor,
                DateTimeOffset.UtcNow,
                new Dictionary<string, string?>
                {
                    ["invoiceNumber"] = invoice.InvoiceNumber,
                    ["invoiceType"] = invoice.InvoiceType.ToString(),
                    ["status"] = invoice.Status.ToString(),
                    ["idempotencyKey"] = invoice.IdempotencyKey,
                    ["advanceInvoiceIds"] = string.Join(
                        ',',
                        invoice.Payments
                            .Where(payment => payment.AdvanceInvoiceId.HasValue)
                            .Select(payment => payment.AdvanceInvoiceId!.Value))
                }),
            cancellationToken);

        return Map(invoice);
    }

    public async Task<FiscalInvoiceResult> CreateStornoAsync(
        CreateStornoCommand command,
        CancellationToken cancellationToken)
    {
        var original = await repository.GetByIdAsync(command.OriginalInvoiceId, cancellationToken)
            ?? throw new FiscalInvoiceOperationException(
                "ORIGINAL_INVOICE_NOT_FOUND",
                "Originalni račun nije pronađen.",
                404);

        if (original.IsOrder)
            return await CreateOrderCorrectionAsync(new(original.Id, command.IssueDateTime, command.Reason,
                [], command.IdempotencyKey, command.CorrelationId, command.Actor), cancellationToken);
        if (original.IsInformational)
            throw new FiscalInvoiceOperationException("SUMMARY_CORRECTION_WORKFLOW_REQUIRED", "Sumarni račun uskladiti kroz /summary-corrections poslije korekcije izvornog ORDER računa.");

        var idempotent = await repository.GetByIdempotencyKeyAsync(
            original.CompanyId,
            command.IdempotencyKey,
            cancellationToken);
        if (idempotent is not null)
        {
            if (idempotent.OriginalInvoiceId != original.Id)
            {
                throw new FiscalInvoiceOperationException(
                    "IDEMPOTENCY_KEY_CONFLICT",
                    "Idempotency ključ je već iskorišćen za drugi račun.");
            }
            return Map(idempotent);
        }

        if (original.Status != FiscalStatus.Fiscalized ||
            string.IsNullOrWhiteSpace(original.Iic) ||
            string.IsNullOrWhiteSpace(original.Jikr))
        {
            throw new FiscalInvoiceOperationException(
                "ORIGINAL_INVOICE_NOT_FISCALIZED",
                "Storno se može napraviti samo za uspješno fiskalizovan originalni račun.");
        }

        if (original.InvoiceType == InvoiceType.Advance)
        {
            var usage = await repository.GetAdvanceUsageAsync(original.Id, cancellationToken);
            if (usage is { UsedAmount: > 0 })
            {
                throw new FiscalInvoiceOperationException(
                    "ADVANCE_ALREADY_USED",
                    "Avans koji je već iskorišćen na konačnom računu ne može se stornirati.");
            }
        }

        if (original.InvoiceType == InvoiceType.Corrective)
        {
            throw new FiscalInvoiceOperationException(
                "CORRECTIVE_STORNO_NOT_SUPPORTED",
                "Korektivni dokument se ne može ponovo stornirati ovim workflow-om.");
        }

        if (original.Payments.Any(payment => payment.PaymentType == PaymentType.Advance))
        {
            var payment = original.Payments.Single(x => x.PaymentType == PaymentType.Advance);
            var settlement = (await repository.GetAdvanceSettlementsAsync(payment.AdvanceInvoiceId!.Value, cancellationToken))
                .SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == original.Id && x.Status != FiscalStatus.Cancelled);
            AdvanceStornoRules.EnsureSettlementResolved(original, settlement);
        }

        var existingStorno = await repository.GetByOriginalInvoiceIdAsync(
            original.Id,
            cancellationToken);
        if (existingStorno is not null)
        {
            throw new FiscalInvoiceOperationException(
                "STORNO_ALREADY_EXISTS",
                "Za originalni račun već postoji storno dokument.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new FiscalValidationException([
                new("CORRECTION_REASON_REQUIRED", "reason", "Razlog storna je obavezan.")]);
        }

        var requiresAutomaticNumber = string.IsNullOrWhiteSpace(command.InvoiceNumber);
        var storno = FiscalInvoice.CreateFullStorno(
            original,
            requiresAutomaticNumber ? "PENDING" : command.InvoiceNumber,
            command.IssueDateTime,
            command.IdempotencyKey,
            command.Reason);
        var validation = validator.Validate(storno);
        if (!validation.IsValid)
        {
            throw new FiscalValidationException(validation.Errors);
        }

        if (requiresAutomaticNumber)
        {
            storno.AssignInvoiceNumber(await ReserveInvoiceNumberAsync(
                original.DeviceId,
                command.IssueDateTime,
                cancellationToken));
        }

        storno.MarkValidated();
        storno.MarkReadyForFiscalization();
        await repository.AddAsync(storno, cancellationToken,
            new("FISCAL_STORNO_PREPARED", storno.Id, storno.CompanyId, command.CorrelationId, command.Actor,
                DateTimeOffset.UtcNow, new Dictionary<string, string?> { ["originalInvoiceId"] = original.Id.ToString() }));
        await auditService.RecordAsync(
            new(
                "FISCAL_STORNO_ACCEPTED",
                storno.Id,
                storno.CompanyId,
                command.CorrelationId,
                command.Actor,
                DateTimeOffset.UtcNow,
                new Dictionary<string, string?>
                {
                    ["originalInvoiceId"] = original.Id.ToString(),
                    ["originalIic"] = original.Iic,
                    ["invoiceNumber"] = storno.InvoiceNumber,
                    ["reason"] = storno.CorrectionReason
                }),
            cancellationToken);
        return Map(storno);
    }

    public async Task<FiscalInvoiceResult> CreateAdvanceRestorationAsync(
        CreateAdvanceRestorationCommand command, CancellationToken cancellationToken)
    {
        var storno = await repository.GetByIdAsync(command.StornoInvoiceId, cancellationToken)
            ?? throw new FiscalInvoiceOperationException("STORNO_NOT_FOUND", "Storno nije pronađen.", 404);
        var existing = await repository.GetByIdempotencyKeyAsync(storno.CompanyId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.AdvanceRestorationStornoInvoiceId != storno.Id)
                throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Ključ pripada drugom dokumentu.");
            return Map(existing);
        }
        var final = storno.OriginalInvoiceId.HasValue
            ? await repository.GetByIdAsync(storno.OriginalInvoiceId.Value, cancellationToken) : null;
        if (final is null || final.InvoiceType != InvoiceType.Normal || final.Status != FiscalStatus.StornoCreated ||
            final.CompanyId != storno.CompanyId || !final.Payments.Any(x => x.PaymentType == PaymentType.Advance))
            throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_NOT_READY", "Prvo fiskalizovati storno konačnog računa plaćenog avansom.");
        AdvanceStornoRules.EnsureFinalStornoReference(storno, final);
        var advanceId = final.Payments.Single(x => x.PaymentType == PaymentType.Advance).AdvanceInvoiceId!.Value;
        var settlement = (await repository.GetAdvanceSettlementsAsync(advanceId, cancellationToken))
            .SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == final.Id && x.Status != FiscalStatus.Cancelled)
            ?? throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_NOT_REQUIRED", "Nema fiskalizovanog umanjenja koje treba poništiti.");
        if (await repository.GetByOriginalInvoiceIdAsync(settlement.Id, cancellationToken) is not null)
            throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_ALREADY_EXISTS", "Obnova avansa već postoji; nastaviti isti dokument.");

        FiscalInvoice restoration;
        try
        {
            restoration = FiscalInvoice.CreateAdvanceRestoration(settlement, storno, "PENDING", command.IssueDateTime, command.IdempotencyKey);
        }
        catch (InvalidOperationException exception)
        { throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_INVALID", exception.Message, 400); }
        var validation = validator.Validate(restoration);
        if (!validation.IsValid) throw new FiscalValidationException(validation.Errors);
        restoration.AssignInvoiceNumber(await ReserveInvoiceNumberAsync(restoration.DeviceId, command.IssueDateTime, cancellationToken));
        restoration.MarkValidated();
        restoration.MarkReadyForFiscalization();
        await repository.AddAsync(restoration, cancellationToken,
            new("FISCAL_ADVANCE_RESTORATION_PREPARED", restoration.Id, restoration.CompanyId,
                command.CorrelationId, command.Actor, DateTimeOffset.UtcNow,
                new Dictionary<string, string?>
                {
                    ["advanceInvoiceId"] = advanceId.ToString(), ["finalInvoiceId"] = final.Id.ToString(),
                    ["stornoInvoiceId"] = storno.Id.ToString(), ["settlementInvoiceId"] = settlement.Id.ToString()
                }));
        return Map(restoration);
    }

    public async Task<AdvanceStornoWorkflowResult> GetAdvanceStornoWorkflowAsync(Guid finalInvoiceId, CancellationToken cancellationToken)
    {
        var final = await repository.GetByIdAsync(finalInvoiceId, cancellationToken)
            ?? throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "Konačni račun nije pronađen.", 404);
        var payment = final.Payments.SingleOrDefault(x => x.PaymentType == PaymentType.Advance);
        if (final.InvoiceType != InvoiceType.Normal || payment?.AdvanceInvoiceId is not { } advanceId)
            throw new FiscalInvoiceOperationException("ADVANCE_FINAL_INVOICE_REQUIRED", "Račun mora biti konačni račun plaćen avansom.");
        var storno = await repository.GetByOriginalInvoiceIdAsync(final.Id, cancellationToken);
        var settlement = (await repository.GetAdvanceSettlementsAsync(advanceId, cancellationToken))
            .SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == final.Id && x.Status != FiscalStatus.Cancelled);
        var restoration = settlement is null ? null : await repository.GetByOriginalInvoiceIdAsync(settlement.Id, cancellationToken);
        var balance = await repository.GetAdvanceUsageAsync(advanceId, cancellationToken);
        var released = storno?.Status == FiscalStatus.Fiscalized && balance is not null &&
            !balance.Usages.Any(x => x.FinalInvoiceId == final.Id);
        return new(final.Id, advanceId, payment.Amount, storno?.Id, storno?.Status,
            settlement?.Id, settlement?.Status, restoration?.Id, restoration?.Status, released);
    }

    public async Task<FiscalInvoiceResult> CreateAdvanceSettlementAsync(
        CreateAdvanceSettlementCommand command, CancellationToken cancellationToken)
    {
        var advance = await repository.GetByIdAsync(command.AdvanceInvoiceId, cancellationToken)
            ?? throw new FiscalInvoiceOperationException("ADVANCE_NOT_FOUND", "Avans nije pronađen.", 404);
        var existing = await repository.GetByIdempotencyKeyAsync(advance.CompanyId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.OriginalInvoiceId != advance.Id || existing.AdvanceSettlementFinalInvoiceId != command.FinalInvoiceId)
                throw new FiscalInvoiceOperationException("IDEMPOTENCY_KEY_CONFLICT", "Ključ pripada drugom dokumentu.");
            return Map(existing);
        }
        var final = await repository.GetByIdAsync(command.FinalInvoiceId, cancellationToken)
            ?? throw new FiscalInvoiceOperationException("FINAL_INVOICE_NOT_FOUND", "Konačni račun nije pronađen.", 404);
        if (final.CompanyId != advance.CompanyId)
            throw new FiscalInvoiceOperationException("ADVANCE_CONTEXT_MISMATCH", "Konačni račun mora pripadati istoj firmi.");
        if (command.IssueDateTime < final.IssueDateTime)
            throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_DATE_INVALID", "Umanjenje ne može prethoditi konačnom računu.");
        FiscalInvoice settlement;
        try
        {
            settlement = FiscalInvoice.CreateAdvanceSettlement(advance, final, "PENDING",
                command.IssueDateTime, command.IdempotencyKey, command.Allocations);
        }
        catch (InvalidOperationException exception)
        {
            throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_INVALID", exception.Message, 400);
        }
        AdvanceInvoiceRules.EnsureSettlementAllocationAvailable(advance, settlement,
            await repository.GetAdvanceSettlementsAsync(advance.Id, cancellationToken));
        var validation = validator.Validate(settlement);
        if (!validation.IsValid) throw new FiscalValidationException(validation.Errors);
        settlement.AssignInvoiceNumber(await ReserveInvoiceNumberAsync(advance.DeviceId, command.IssueDateTime, cancellationToken));
        settlement.MarkValidated();
        settlement.MarkReadyForFiscalization();
        await repository.AddAsync(settlement, cancellationToken,
            new("FISCAL_ADVANCE_REDUCTION_RESERVED", settlement.Id, settlement.CompanyId,
                command.CorrelationId, command.Actor, DateTimeOffset.UtcNow,
                new Dictionary<string, string?> { ["advanceInvoiceId"] = advance.Id.ToString(), ["finalInvoiceId"] = final.Id.ToString() }));
        await auditService.RecordAsync(new("FISCAL_ADVANCE_SETTLEMENT_ACCEPTED", settlement.Id,
            settlement.CompanyId, command.CorrelationId, command.Actor, DateTimeOffset.UtcNow,
            new Dictionary<string, string?>
            {
                ["advanceInvoiceId"] = advance.Id.ToString(),
                ["finalInvoiceId"] = final.Id.ToString(),
                ["amount"] = settlement.TotalGrossAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }), cancellationToken);
        return Map(settlement);
    }

    public async Task CancelUnsubmittedAsync(Guid id, string correlationId, string actor, CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "Račun nije pronađen.", 404);
        await repository.CancelUnsubmittedAsync(id, cancellationToken,
            new("FISCAL_ADVANCE_RESERVATION_RELEASED", id, invoice.CompanyId, correlationId, actor,
                DateTimeOffset.UtcNow, new Dictionary<string, string?> { ["reason"] = "Unsubmitted invoice cancelled" }));
        await auditService.RecordAsync(new("FISCAL_UNSUBMITTED_INVOICE_CANCELLED", id,
            invoice.CompanyId, correlationId, actor, DateTimeOffset.UtcNow,
            new Dictionary<string, string?> { ["idempotencyKey"] = invoice.IdempotencyKey }), cancellationToken);
    }

    private async Task<string> ReserveInvoiceNumberAsync(
        Guid deviceId,
        DateTimeOffset issueDateTime,
        CancellationToken cancellationToken)
    {
        var next = await invoiceNumberSequence.ReserveNextAsync(
            deviceId,
            issueDateTime.Year,
            cancellationToken);
        return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<FiscalInvoiceResult?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByIdAsync(id, cancellationToken);
        return invoice is null ? null : Map(invoice);
    }

    public async Task<FiscalInvoiceStatusResult?> GetStatusAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByIdAsync(id, cancellationToken);
        return invoice is null
            ? null
            : new(
                invoice.Id,
                invoice.CompanyId,
                invoice.InvoiceNumber,
                invoice.OfficialInvoiceNumber,
                invoice.Status,
                invoice.Iic,
                invoice.Jikr,
                invoice.UpdatedAt);
    }

    public async Task<FiscalInvoiceResult?> GetStornoForOriginalAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByOriginalInvoiceIdAsync(
            originalInvoiceId,
            cancellationToken);
        return invoice is null ? null : Map(invoice);
    }

    public async Task<AdvanceBalanceResult?> GetAdvanceBalanceAsync(
        Guid advanceInvoiceId,
        CancellationToken cancellationToken)
    {
        var snapshot = await repository.GetAdvanceUsageAsync(
            advanceInvoiceId,
            cancellationToken);
        return snapshot is null
            ? null
            : new(
                snapshot.AdvanceInvoiceId,
                snapshot.AdvanceIic,
                snapshot.TotalAmount,
                snapshot.Usages.Where(x => x.FinalInvoiceStatus is FiscalStatus.Fiscalized or FiscalStatus.StornoCreated)
                    .Sum(x => x.UsedAmount),
                snapshot.RemainingAmount,
                snapshot.Usages.Select(usage => new AdvanceUsageResult(
                    usage.FinalInvoiceId,
                    usage.FinalInvoiceNumber,
                    usage.UsedAmount,
                    usage.FinalInvoiceStatus,
                    usage.SettlementInvoiceId,
                    usage.SettlementStatus)).ToArray(),
                snapshot.Usages.Where(x => x.FinalInvoiceStatus is not (FiscalStatus.Fiscalized or FiscalStatus.StornoCreated))
                    .Sum(x => x.UsedAmount),
                snapshot.IsAvailable);
    }

    private static void ValidateAdvanceReference(
        FiscalInvoice? advance,
        Guid companyId,
        string currency,
        int paymentIndex,
        ICollection<FiscalValidationError> errors)
    {
        var field = $"payments[{paymentIndex}].advanceInvoiceId";
        if (advance is null)
        {
            errors.Add(new("ADVANCE_NOT_FOUND", field, "Navedeni avansni račun nije pronađen."));
            return;
        }
        if (advance.InvoiceType != InvoiceType.Advance)
            errors.Add(new("ADVANCE_TYPE_REQUIRED", field, "Navedeni račun nije avansni račun."));
        if (advance.CompanyId != companyId)
            errors.Add(new("ADVANCE_COMPANY_MISMATCH", field, "Avans i konačni račun moraju pripadati istoj firmi."));
        if (!string.Equals(advance.Currency, currency, StringComparison.OrdinalIgnoreCase))
            errors.Add(new("ADVANCE_CURRENCY_MISMATCH", field, "Avans i konačni račun moraju imati istu valutu."));
        if (advance.Status != FiscalStatus.Fiscalized || string.IsNullOrWhiteSpace(advance.Iic))
            errors.Add(new("ADVANCE_NOT_FISCALIZED", field, "Avans mora biti uspješno fiskalizovan prije korišćenja."));
    }

    private static FiscalInvoiceResult Map(FiscalInvoice invoice) =>
        new(
            invoice.Id,
            invoice.CompanyId,
            invoice.InvoiceNumber,
            invoice.OfficialInvoiceNumber,
            invoice.InvoiceType,
            invoice.Status,
            invoice.TotalNetAmount,
            invoice.TotalVatAmount,
            invoice.TotalGrossAmount,
            invoice.Currency,
            invoice.Iic,
            invoice.Jikr,
            invoice.QrCodeData,
            invoice.IssueDateTime,
            invoice.SupplyPeriodStart,
            invoice.SupplyPeriodEnd,
            invoice.PaymentDeadline,
            invoice.Buyer is null ? null : new(
                invoice.Buyer.IdentificationType,
                invoice.Buyer.IdentificationNumber,
                invoice.Buyer.Name,
                invoice.Buyer.Address,
                invoice.Buyer.Town,
                invoice.Buyer.Country,
                invoice.Buyer.TaxIdentificationCode),
            invoice.OriginalInvoiceId,
            invoice.OriginalIic,
            invoice.OriginalIssueDateTime,
            invoice.CorrectiveType,
            invoice.CorrectionReason,
            invoice.Items.Select(item => new FiscalInvoiceItemResult(
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.VatRate,
                item.ItemCode,
                item.UnitOfMeasure,
                item.DiscountAmount,
                item.NetAmount,
                item.VatAmount,
                item.GrossAmount,
                item.Id)).ToArray(),
            invoice.Payments.Select(payment => new FiscalPaymentResult(
                payment.PaymentType,
                payment.Amount,
                payment.Reference,
                payment.AdvanceInvoiceId,
                payment.AdvanceIic)).ToArray(),
            invoice.CreatedAt,
            invoice.FiscalizedAt,
            invoice.AdvanceSettlementFinalInvoiceId,
            invoice.AdvanceSettlementFinalIic,
            invoice.AdvanceSettlementAllocations,
            invoice.AdvanceRestorationStornoInvoiceId,
            invoice.AdvanceRestorationStornoIic,
            invoice.OrderSummary, invoice.IsInformational);
}

public sealed class FiscalValidationException(
    IReadOnlyCollection<FiscalValidationError> errors)
    : Exception("Podaci fiskalnog računa nijesu ispravni.")
{
    public IReadOnlyCollection<FiscalValidationError> Errors { get; } = errors;
}
