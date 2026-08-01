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
}

public sealed class FiscalInvoiceApplicationService(
    IFiscalInvoiceRepository repository,
    IFiscalInvoiceValidator validator,
    IAuditService auditService) : IFiscalInvoiceApplicationService
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

        var invoice = new FiscalInvoice(
            command.CompanyId,
            command.BusinessUnitId,
            command.DeviceId,
            command.OperatorId,
            command.InvoiceType,
            command.InvoiceNumber,
            command.IssueDateTime,
            command.Currency,
            command.IdempotencyKey);

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

        invoice.MarkValidated();
        invoice.MarkReadyForFiscalization();

        await repository.AddAsync(invoice, cancellationToken);
        await auditService.RecordAsync(
            new AuditEntry(
                "FISCAL_INVOICE_ACCEPTED",
                invoice.Id,
                command.CorrelationId,
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
                invoice.Status,
                invoice.Iic,
                invoice.Jikr,
                invoice.UpdatedAt);
    }

    private static FiscalInvoiceResult Map(FiscalInvoice invoice) =>
        new(
            invoice.Id,
            invoice.CompanyId,
            invoice.InvoiceNumber,
            invoice.Status,
            invoice.TotalNetAmount,
            invoice.TotalVatAmount,
            invoice.TotalGrossAmount,
            invoice.Currency,
            invoice.Iic,
            invoice.Jikr,
            invoice.QrCodeData,
            invoice.CreatedAt,
            invoice.FiscalizedAt);
}

public sealed class FiscalValidationException(
    IReadOnlyCollection<FiscalValidationError> errors)
    : Exception("Podaci fiskalnog računa nijesu ispravni.")
{
    public IReadOnlyCollection<FiscalValidationError> Errors { get; } = errors;
}
