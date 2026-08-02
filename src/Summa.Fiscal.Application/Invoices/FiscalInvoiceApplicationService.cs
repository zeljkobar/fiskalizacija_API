using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public interface IFiscalInvoiceApplicationService
{
    Task<FiscalInvoiceResult> CreateAsync(
        CreateFiscalInvoiceCommand command,
        CancellationToken cancellationToken);

    Task<FiscalInvoiceResult?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<FiscalInvoiceStatusResult?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    Task<FiscalInvoiceResult?> GetStornoForOriginalAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken);

    Task<FiscalInvoiceResult> CreateStornoAsync(
        CreateStornoCommand command,
        CancellationToken cancellationToken);
}

public sealed class FiscalInvoiceApplicationService(
    IFiscalInvoiceRepository repository,
    IFiscalInvoiceValidator validator,
    IAuditService auditService,
    IInvoiceNumberSequence invoiceNumberSequence) : IFiscalInvoiceApplicationService
{
    public async Task<FiscalInvoiceResult> CreateAsync(
        CreateFiscalInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdempotencyKeyAsync(
            command.CompanyId,
            command.IdempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
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

        foreach (var payment in command.Payments)
        {
            invoice.AddPayment(new FiscalPayment(
                payment.PaymentType,
                payment.Amount,
                payment.Reference));
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

        await repository.AddAsync(invoice, cancellationToken);
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
                    ["status"] = invoice.Status.ToString(),
                    ["idempotencyKey"] = invoice.IdempotencyKey
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
        await repository.AddAsync(storno, cancellationToken);
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
                item.GrossAmount)).ToArray(),
            invoice.Payments.Select(payment => new FiscalPaymentResult(
                payment.PaymentType,
                payment.Amount,
                payment.Reference)).ToArray(),
            invoice.CreatedAt,
            invoice.FiscalizedAt);
}

public sealed class FiscalValidationException(
    IReadOnlyCollection<FiscalValidationError> errors)
    : Exception("Podaci fiskalnog računa nijesu ispravni.")
{
    public IReadOnlyCollection<FiscalValidationError> Errors { get; } = errors;
}
