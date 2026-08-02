using Microsoft.EntityFrameworkCore;
using Npgsql;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlFiscalInvoiceRepository(SummaFiscalDbContext dbContext)
    : IFiscalInvoiceRepository
{
    public async Task<FiscalInvoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<FiscalInvoice?> GetByIdempotencyKeyAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var record = await Query().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<FiscalInvoice?> GetByOriginalInvoiceIdAsync(
        Guid originalInvoiceId,
        CancellationToken cancellationToken)
    {
        var record = await Query().SingleOrDefaultAsync(
            x => x.OriginalInvoiceId == originalInvoiceId,
            cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task AddAsync(FiscalInvoice invoice, CancellationToken cancellationToken)
    {
        var record = ToRecord(invoice);
        dbContext.FiscalInvoices.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.Entry(record).State = EntityState.Detached;
            var postgres = (PostgresException)exception.InnerException!;
            if (string.Equals(
                    postgres.ConstraintName,
                    "IX_fiscal_invoices_OriginalInvoiceId",
                    StringComparison.Ordinal))
            {
                throw new FiscalInvoiceOperationException(
                    "STORNO_ALREADY_EXISTS",
                    "Za originalni račun već postoji storno dokument.");
            }

            throw new FiscalInvoiceOperationException(
                "INVOICE_CONFLICT",
                "Račun sa istim identifikatorom ili idempotency ključem već postoji.");
        }
    }

    public async Task UpdateAsync(FiscalInvoice invoice, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalInvoices
            .SingleOrDefaultAsync(x => x.Id == invoice.Id, cancellationToken)
            ?? throw new InvalidOperationException("Račun koji se ažurira ne postoji.");

        ApplyMutable(record, invoice);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteCorrectiveAsync(
        FiscalInvoice corrective,
        FiscalInvoice original,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var correctiveRecord = await dbContext.FiscalInvoices.SingleAsync(
            x => x.Id == corrective.Id,
            cancellationToken);
        var originalRecord = await dbContext.FiscalInvoices.SingleAsync(
            x => x.Id == original.Id,
            cancellationToken);
        ApplyMutable(correctiveRecord, corrective);
        ApplyMutable(originalRecord, original);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<FiscalInvoiceRecord> Query() =>
        dbContext.FiscalInvoices
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Items)
            .Include(x => x.Payments);

    private static FiscalInvoiceRecord ToRecord(FiscalInvoice invoice)
    {
        var ordinalNumber = int.TryParse(
            invoice.InvoiceNumber.Split('/', StringSplitOptions.TrimEntries)[0],
            out var parsedOrdinal)
            ? parsedOrdinal
            : 0;

        return new FiscalInvoiceRecord
        {
            Id = invoice.Id,
            CompanyId = invoice.CompanyId,
            BusinessUnitId = invoice.BusinessUnitId,
            DeviceId = invoice.DeviceId,
            OperatorId = invoice.OperatorId,
            InvoiceType = invoice.InvoiceType.ToString(),
            InvoiceOrdinalNumber = ordinalNumber,
            InvoiceNumber = invoice.InvoiceNumber,
            OfficialInvoiceNumber = invoice.OfficialInvoiceNumber,
            IssueDateTime = invoice.IssueDateTime.ToUniversalTime(),
            SupplyPeriodStart = invoice.SupplyPeriodStart,
            SupplyPeriodEnd = invoice.SupplyPeriodEnd,
            PaymentDeadline = invoice.PaymentDeadline,
            Currency = invoice.Currency,
            BuyerIdentificationType = invoice.Buyer?.IdentificationType.ToString(),
            BuyerIdentificationNumber = invoice.Buyer?.IdentificationNumber,
            BuyerName = invoice.Buyer?.Name,
            BuyerAddress = invoice.Buyer?.Address,
            BuyerTown = invoice.Buyer?.Town,
            BuyerCountry = invoice.Buyer?.Country,
            BuyerTaxIdentificationCode = invoice.Buyer?.TaxIdentificationCode,
            OriginalInvoiceId = invoice.OriginalInvoiceId,
            OriginalIic = invoice.OriginalIic,
            OriginalIssueDateTime = invoice.OriginalIssueDateTime,
            CorrectiveType = invoice.CorrectiveType?.ToString(),
            CorrectionReason = invoice.CorrectionReason,
            NetAmount = invoice.TotalNetAmount,
            VatAmount = invoice.TotalVatAmount,
            TotalAmount = invoice.TotalGrossAmount,
            Iic = invoice.Iic,
            IicSignature = invoice.IicSignature,
            Jikr = invoice.Jikr,
            QrCodeData = invoice.QrCodeData,
            Status = invoice.Status.ToString(),
            IdempotencyKey = invoice.IdempotencyKey,
            RequestUuid = Guid.NewGuid(),
            FiscalizedAt = invoice.FiscalizedAt,
            CreatedAt = invoice.CreatedAt,
            UpdatedAt = invoice.UpdatedAt,
            Items = invoice.Items.Select((item, index) => new FiscalInvoiceItemRecord
            {
                Id = item.Id,
                LineNumber = index + 1,
                Code = item.ItemCode,
                Name = item.Name,
                Unit = item.UnitOfMeasure,
                Quantity = item.Quantity,
                UnitPriceBeforeVat = item.VatRate == 0
                    ? item.UnitPrice
                    : decimal.Round(item.UnitPrice / (1 + item.VatRate / 100), 4),
                UnitPriceAfterVat = item.UnitPrice,
                RebateRate = 0,
                DiscountAmount = item.DiscountAmount,
                VatRate = item.VatRate,
                VatAmount = item.VatAmount,
                NetAmount = item.NetAmount,
                TotalAmount = item.GrossAmount,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt
            }).ToList(),
            Payments = invoice.Payments.Select(payment => new FiscalPaymentRecord
            {
                Id = payment.Id,
                PaymentType = payment.PaymentType.ToString(),
                Amount = payment.Amount,
                Reference = payment.Reference,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt
            }).ToList()
        };
    }

    private static FiscalInvoice ToDomain(FiscalInvoiceRecord record) =>
        FiscalInvoice.Restore(
            record.Id,
            record.CompanyId,
            record.BusinessUnitId,
            record.DeviceId,
            record.OperatorId,
            Enum.Parse<InvoiceType>(record.InvoiceType),
            record.InvoiceNumber,
            record.OfficialInvoiceNumber,
            record.IssueDateTime,
            record.Currency,
            record.IdempotencyKey,
            Enum.Parse<FiscalStatus>(record.Status),
            record.Iic,
            record.IicSignature,
            record.Jikr,
            record.QrCodeData,
            record.CreatedAt,
            record.UpdatedAt,
            record.FiscalizedAt,
            record.BuyerIdentificationType is null ? null : new FiscalBuyer(
                Enum.Parse<BuyerIdentificationType>(record.BuyerIdentificationType),
                record.BuyerIdentificationNumber!,
                record.BuyerName!,
                record.BuyerAddress,
                record.BuyerTown,
                record.BuyerCountry,
                record.BuyerTaxIdentificationCode),
            record.SupplyPeriodStart,
            record.SupplyPeriodEnd,
            record.PaymentDeadline,
            record.OriginalInvoiceId,
            record.OriginalIic,
            record.OriginalIssueDateTime,
            record.CorrectiveType is null ? null : Enum.Parse<CorrectiveInvoiceType>(record.CorrectiveType),
            record.CorrectionReason,
            record.Items.OrderBy(x => x.LineNumber).Select(x => (
                x.Id,
                x.Name,
                x.Quantity,
                x.UnitPriceAfterVat,
                x.VatRate,
                x.Code,
                x.Unit,
                x.DiscountAmount)),
            record.Payments.Select(x => (
                x.Id,
                Enum.Parse<PaymentType>(x.PaymentType),
                x.Amount,
                x.Reference)));

    private static void ApplyMutable(FiscalInvoiceRecord record, FiscalInvoice invoice)
    {
        record.Status = invoice.Status.ToString();
        record.Iic = invoice.Iic;
        record.IicSignature = invoice.IicSignature;
        record.Jikr = invoice.Jikr;
        record.QrCodeData = invoice.QrCodeData;
        record.OfficialInvoiceNumber = invoice.OfficialInvoiceNumber;
        record.FiscalizedAt = invoice.FiscalizedAt;
        record.UpdatedAt = invoice.UpdatedAt;
    }
}
