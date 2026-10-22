using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Text.Json;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed partial class PostgreSqlFiscalInvoiceRepository(SummaFiscalDbContext dbContext)
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
            x => x.OriginalInvoiceId == originalInvoiceId && x.AdvanceSettlementFinalInvoiceId == null && !x.IsOrderCorrection && !x.IsSummaryCorrection && x.Status != "Cancelled",
            cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyCollection<FiscalInvoice>> GetAdvanceSettlementsAsync(Guid advanceInvoiceId, CancellationToken cancellationToken) =>
        (await Query().Where(x => x.OriginalInvoiceId == advanceInvoiceId && x.AdvanceSettlementFinalInvoiceId != null)
            .ToArrayAsync(cancellationToken)).Select(ToDomain).ToArray();

    public async Task CancelUnsubmittedAsync(Guid invoiceId, CancellationToken cancellationToken, AuditEntry? auditEntry = null)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var record = await LockInvoiceAsync(invoiceId, cancellationToken);
        if (record.Status == "Cancelled") return;
        var invoice = ToDomain(record);
        try { invoice.CancelUnsubmitted(); }
        catch (InvalidOperationException exception)
        { throw new FiscalInvoiceOperationException("INVOICE_CANNOT_BE_CANCELLED", exception.Message); }
        ApplyMutable(record, invoice);
        AddAudit(auditEntry);
        await dbContext.FiscalAdvanceUsages.Where(x => x.FinalInvoiceId == invoiceId && x.ReleasedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow), cancellationToken);
        await dbContext.FiscalSummaryOrders.Where(x => x.SummaryInvoiceId == invoiceId && x.ReleasedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AdvanceUsageSnapshot?> GetAdvanceUsageAsync(
        Guid advanceInvoiceId,
        CancellationToken cancellationToken)
    {
        var advance = await dbContext.FiscalInvoices
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == advanceInvoiceId, cancellationToken);
        if (advance is null ||
            !string.Equals(advance.InvoiceType, InvoiceType.Advance.ToString(), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(advance.Iic))
        {
            return null;
        }

        var usageRecords = await dbContext.FiscalAdvanceUsages
            .AsNoTracking()
            .Include(x => x.FinalInvoice)
            .Where(x => x.AdvanceInvoiceId == advanceInvoiceId && x.ReleasedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var settlements = await dbContext.FiscalInvoices.AsNoTracking()
            .Where(x => x.OriginalInvoiceId == advanceInvoiceId && x.AdvanceSettlementFinalInvoiceId != null && x.Status != "Cancelled")
            .ToArrayAsync(cancellationToken);
        var usages = usageRecords.Select(x => new AdvanceUsageEntry(
                x.FinalInvoiceId,
                x.FinalInvoice.InvoiceNumber,
                x.UsedAmount,
                Enum.Parse<FiscalStatus>(x.FinalInvoice.Status),
                settlements.SingleOrDefault(s => s.AdvanceSettlementFinalInvoiceId == x.FinalInvoiceId)?.Id,
                settlements.SingleOrDefault(s => s.AdvanceSettlementFinalInvoiceId == x.FinalInvoiceId) is { } settlement
                    ? Enum.Parse<FiscalStatus>(settlement.Status) : null))
            .ToArray();

        var hasFullStorno = await dbContext.FiscalInvoices.AnyAsync(x => x.OriginalInvoiceId == advanceInvoiceId &&
            x.AdvanceSettlementFinalInvoiceId == null && x.Status != "Cancelled", cancellationToken);
        return new(
            advance.Id,
            advance.Iic,
            advance.TotalAmount,
            usages.Sum(x => x.UsedAmount),
            usages,
            advance.Status == "Fiscalized" && !hasFullStorno);
    }

    public async Task AddAsync(FiscalInvoice invoice, CancellationToken cancellationToken, AuditEntry? auditEntry = null)
    {
        var record = ToRecord(invoice);
        var referencedAdvances = invoice.Payments.Where(payment => payment.AdvanceInvoiceId.HasValue)
            .Select(payment => payment.AdvanceInvoiceId!.Value).ToArray();
        var advancePayments = invoice.Payments
            .Where(payment => payment.PaymentType == PaymentType.Advance && invoice.InvoiceType == InvoiceType.Normal)
            .GroupBy(payment => payment.AdvanceInvoiceId)
            .ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await PrepareOrderSummaryAsync(invoice, auditEntry, cancellationToken);
        var restorationSettlement = invoice.IsAdvanceRestoration && invoice.OriginalInvoiceId.HasValue
            ? await GetByIdAsync(invoice.OriginalInvoiceId.Value, cancellationToken) : null;
        var lockIds = referencedAdvances
            .Concat(invoice.OriginalInvoiceId is { } originalId ? new[] { originalId } : [])
            .Concat(invoice.AdvanceSettlementFinalInvoiceId is { } finalIdToLock ? new[] { finalIdToLock } : [])
            .Concat(invoice.AdvanceRestorationStornoInvoiceId is { } stornoToLock ? new[] { stornoToLock } : [])
            .Concat(restorationSettlement?.OriginalInvoiceId is { } restorationAdvanceId ? new[] { restorationAdvanceId } : [])
            .Distinct().OrderBy(x => x).ToArray();
        var locked = new Dictionary<Guid, FiscalInvoiceRecord>();
        foreach (var id in lockIds) locked[id] = await LockInvoiceAsync(id, cancellationToken);
        if (invoice.IsAdvanceRestoration)
        {
            var settlement = ToDomain(locked[invoice.OriginalInvoiceId!.Value]);
            var storno = ToDomain(locked[invoice.AdvanceRestorationStornoInvoiceId!.Value]);
            try { _ = FiscalInvoice.CreateAdvanceRestoration(settlement, storno, invoice.InvoiceNumber, invoice.IssueDateTime, invoice.IdempotencyKey); }
            catch (InvalidOperationException exception)
            { throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_INVALID", exception.Message); }
            if (await dbContext.FiscalInvoices.AnyAsync(x => x.OriginalInvoiceId == settlement.Id && x.Status != "Cancelled", cancellationToken))
                throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_ALREADY_EXISTS", "Obnova avansa već postoji.");
            if (!await dbContext.FiscalAdvanceUsages.AnyAsync(x => x.AdvanceInvoiceId == settlement.OriginalInvoiceId &&
                x.FinalInvoiceId == settlement.AdvanceSettlementFinalInvoiceId && x.ReleasedAt == null, cancellationToken))
                throw new FiscalInvoiceOperationException("ADVANCE_RESERVATION_MISSING", "Nedostaje aktivna rezervacija za obnovu.");
        }
        else if (invoice.OriginalInvoiceId is { } finalSourceId && locked[finalSourceId].InvoiceType == "Normal" && referencedAdvances.Length > 0)
        {
            var final = ToDomain(locked[finalSourceId]);
            AdvanceStornoRules.EnsureFinalStornoReference(invoice, final);
            if (final.Status != FiscalStatus.Fiscalized)
                throw new FiscalInvoiceOperationException("ORIGINAL_INVOICE_NOT_FISCALIZED", "Konačni račun više nije raspoloživ za storno.");
            var settlements = await GetAdvanceSettlementsAsync(referencedAdvances.Single(), cancellationToken);
            var settlement = settlements.SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == final.Id && x.Status != FiscalStatus.Cancelled);
            if (settlement is not null)
            {
                // Serialize cancellation against the separate submission claim on the reduction document.
                settlement = ToDomain(await LockInvoiceAsync(settlement.Id, cancellationToken));
                AdvanceStornoRules.EnsureSettlementResolved(final, settlement);
                if (settlement.Status == FiscalStatus.ReadyForFiscalization)
                {
                    settlement.CancelUnsubmitted();
                    ApplyMutable(await dbContext.FiscalInvoices.SingleAsync(x => x.Id == settlement.Id, cancellationToken), settlement);
                    if (auditEntry is not null)
                        AddAudit(new("FISCAL_ADVANCE_REDUCTION_CANCELLED_FOR_STORNO", settlement.Id, settlement.CompanyId,
                            auditEntry.CorrelationId, auditEntry.Actor, DateTimeOffset.UtcNow,
                            new Dictionary<string, string?> { ["finalInvoiceId"] = final.Id.ToString(), ["stornoInvoiceId"] = invoice.Id.ToString() }));
                }
            }
            if (!await dbContext.FiscalAdvanceUsages.AnyAsync(x => x.FinalInvoiceId == final.Id && x.ReleasedAt == null, cancellationToken))
                throw new FiscalInvoiceOperationException("ADVANCE_RESERVATION_MISSING", "Konačni račun nema aktivnu rezervaciju avansa.");
        }
        foreach (var group in advancePayments.OrderBy(x => x.Key))
        {
            if (!group.Key.HasValue)
            {
                throw new FiscalInvoiceOperationException(
                    "ADVANCE_REFERENCE_REQUIRED",
                    "Plaćanje avansom nema referencu na avansni račun.");
            }

            var advanceId = group.Key.Value;
            var advance = locked[advanceId];
            var alreadyUsed = await dbContext.FiscalAdvanceUsages
                .Where(x => x.AdvanceInvoiceId == advanceId && x.ReleasedAt == null)
                .SumAsync(x => (decimal?)x.UsedAmount, cancellationToken) ?? 0m;
            var requested = group.Sum(payment => payment.Amount);
            AdvanceInvoiceRules.EnsureAvailable(ToDomain(advance), invoice, requested, alreadyUsed);
            if (await dbContext.FiscalInvoices.AnyAsync(x => x.OriginalInvoiceId == advanceId &&
                x.AdvanceSettlementFinalInvoiceId == null && x.Status != "Cancelled", cancellationToken))
                throw new FiscalInvoiceOperationException("ADVANCE_STORNO_PENDING", "Za avans već postoji storno.");
            var finalEnvironment = await dbContext.BusinessUnits.Where(x => x.Id == invoice.BusinessUnitId)
                .Select(x => x.Environment).SingleAsync(cancellationToken);
            var sourceEnvironment = await dbContext.BusinessUnits.Where(x => x.Id == advance.BusinessUnitId)
                .Select(x => x.Environment).SingleAsync(cancellationToken);
            if (finalEnvironment != sourceEnvironment)
                throw new FiscalInvoiceOperationException("ADVANCE_ENVIRONMENT_MISMATCH", "Testni i produkcioni avansi se ne smiju povezivati.");
        }

        if (invoice.OriginalInvoiceId is { } sourceId && locked[sourceId].InvoiceType == "Advance")
        {
            var advance = ToDomain(locked[sourceId]);
            if (invoice.AdvanceSettlementFinalInvoiceId is null)
            {
                if (await dbContext.FiscalAdvanceUsages.AnyAsync(x => x.AdvanceInvoiceId == sourceId && x.ReleasedAt == null, cancellationToken))
                    throw new FiscalInvoiceOperationException("ADVANCE_ALREADY_USED", "Avans je rezervisan ili iskorišćen i ne može se potpuno stornirati.");
            }
            else
            {
                var finalId = invoice.AdvanceSettlementFinalInvoiceId.Value;
                var final = await Query().SingleAsync(x => x.Id == finalId, cancellationToken);
                if (final.Status != "Fiscalized" || advance.Status != FiscalStatus.Fiscalized)
                    throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_INVALID", "Avans i konačni račun moraju biti fiskalizovani.");
                if (await dbContext.FiscalInvoices.AnyAsync(x => x.OriginalInvoiceId == finalId && x.AdvanceSettlementFinalInvoiceId == null && x.Status != "Cancelled", cancellationToken))
                    throw new FiscalInvoiceOperationException("FINAL_STORNO_EXISTS", "Za konačni račun već postoji storno; umanjenje se više ne može pripremiti.");
                var reservation = await dbContext.FiscalAdvanceUsages.SingleOrDefaultAsync(x =>
                    x.AdvanceInvoiceId == sourceId && x.FinalInvoiceId == finalId && x.ReleasedAt == null, cancellationToken);
                if (reservation is null || reservation.UsedAmount != -invoice.TotalGrossAmount)
                    throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_INVALID", "Umanjenje ne odgovara rezervisanom avansu.");
                var previous = await GetAdvanceSettlementsAsync(sourceId, cancellationToken);
                if (previous.Any(x => x.AdvanceSettlementFinalInvoiceId == finalId && x.Status != FiscalStatus.Cancelled))
                    throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_ALREADY_EXISTS", "Umanjenje za ovaj konačni račun već postoji.");
                AdvanceInvoiceRules.EnsureSettlementAllocationAvailable(advance, invoice, previous);
            }
        }

        dbContext.FiscalInvoices.Add(record);
        AddAudit(auditEntry);
        await SaveNewInvoiceAsync(record, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<FiscalInvoiceRecord> LockInvoiceAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalInvoices
            .FromSqlInterpolated($"SELECT * FROM fiscal.fiscal_invoices WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new FiscalInvoiceOperationException("FISCAL_INVOICE_NOT_FOUND", "Račun nije pronađen.", 404);
        await dbContext.Entry(record).ReloadAsync(cancellationToken);
        await dbContext.Entry(record).Collection(x => x.Items).LoadAsync(cancellationToken);
        await dbContext.Entry(record).Collection(x => x.Payments).LoadAsync(cancellationToken);
        return record;
    }

    private void AddAudit(AuditEntry? entry)
    {
        if (entry is null) return;
        dbContext.FiscalAudits.Add(new()
        {
            CompanyId = entry.CompanyId, Action = entry.Action,
            CorrelationId = entry.CorrelationId, Actor = entry.Actor,
            CreatedAt = entry.OccurredAt.ToUniversalTime(), UpdatedAt = entry.OccurredAt.ToUniversalTime(),
            DataJson = JsonSerializer.Serialize(new { entry.InvoiceId, Data = entry.Data })
        });
    }

    private async Task SaveNewInvoiceAsync(
        FiscalInvoiceRecord record,
        CancellationToken cancellationToken)
    {
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var record = await LockInvoiceAsync(invoice.Id, cancellationToken);
        if (invoice.Status == FiscalStatus.FiscalizationPending && record.Status is not ("ReadyForFiscalization" or "FiscalizationFailed"))
            throw new FiscalInvoiceOperationException("INVOICE_STATE_CONFLICT", "Račun je već preuzet za slanje ili otkazan.");

        ApplyMutable(record, invoice);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteCorrectiveAsync(
        FiscalInvoice corrective,
        FiscalInvoice original,
        CancellationToken cancellationToken,
        AuditEntry? auditEntry = null)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sourceAdvanceId = corrective.IsAdvanceRestoration ? original.OriginalInvoiceId
            : corrective.Payments.SingleOrDefault(x => x.PaymentType == PaymentType.Advance)?.AdvanceInvoiceId;
        var ids = new[] { corrective.Id, original.Id }
            .Concat(sourceAdvanceId.HasValue ? new[] { sourceAdvanceId.Value } : [])
            .Concat(corrective.AdvanceRestorationStornoInvoiceId is { } stornoId ? new[] { stornoId } : [])
            .Distinct().OrderBy(x => x).ToArray();
        var locked = new Dictionary<Guid, FiscalInvoiceRecord>();
        foreach (var id in ids) locked[id] = await LockInvoiceAsync(id, cancellationToken);
        var correctiveRecord = locked[corrective.Id];
        var originalRecord = locked[original.Id];
        if (correctiveRecord.Status is "Fiscalized" or "StornoCreated" && correctiveRecord.Jikr != corrective.Jikr)
            throw new FiscalInvoiceOperationException("FISCAL_RECOVERY_JIKR_CONFLICT", "Dokument već ima drugi JIKR.");

        if (sourceAdvanceId.HasValue)
        {
            var finalId = corrective.IsAdvanceRestoration ? original.AdvanceSettlementFinalInvoiceId!.Value : original.Id;
            var release = corrective.IsAdvanceRestoration;
            if (corrective.IsAdvanceRestoration)
            {
                var finalStorno = locked[corrective.AdvanceRestorationStornoInvoiceId!.Value];
                if (finalStorno.Status != "Fiscalized" || finalStorno.OriginalInvoiceId != finalId)
                    throw new FiscalInvoiceOperationException("ADVANCE_RESTORATION_NOT_READY", "Storno konačnog računa nije fiskalizovan.");
            }
            else
            {
                var settlements = await GetAdvanceSettlementsAsync(sourceAdvanceId.Value, cancellationToken);
                var settlement = settlements.SingleOrDefault(x => x.AdvanceSettlementFinalInvoiceId == finalId && x.Status != FiscalStatus.Cancelled);
                AdvanceStornoRules.EnsureSettlementResolved(ToDomain(originalRecord), settlement);
                release = settlement is null;
            }
            if (release)
                await dbContext.FiscalAdvanceUsages.Where(x => x.AdvanceInvoiceId == sourceAdvanceId.Value &&
                    x.FinalInvoiceId == finalId && x.ReleasedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow), cancellationToken);
        }
        ApplyMutable(correctiveRecord, corrective);
        ApplyMutable(originalRecord, original);
        AddAudit(auditEntry);
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
            OrderSummaryJson = invoice.OrderSummary is null ? null : JsonSerializer.Serialize(invoice.OrderSummary),
            IsOrderCorrection = invoice.IsOrderCorrection,
            IsSummaryCorrection = invoice.IsSummaryCorrection,
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
            AdvanceSettlementFinalInvoiceId = invoice.AdvanceSettlementFinalInvoiceId,
            AdvanceSettlementFinalIic = invoice.AdvanceSettlementFinalIic,
            AdvanceSettlementFinalIssuedAt = invoice.AdvanceSettlementFinalIssuedAt?.ToUniversalTime(),
            AdvanceSettlementAllocationsJson = JsonSerializer.Serialize(invoice.AdvanceSettlementAllocations),
            AdvanceRestorationStornoInvoiceId = invoice.AdvanceRestorationStornoInvoiceId,
            AdvanceRestorationStornoIic = invoice.AdvanceRestorationStornoIic,
            AdvanceRestorationStornoIssuedAt = invoice.AdvanceRestorationStornoIssuedAt?.ToUniversalTime(),
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
                AdvanceInvoiceId = payment.AdvanceInvoiceId,
                AdvanceIic = payment.AdvanceIic,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt
            }).ToList(),
            AdvanceUsages = invoice.InvoiceType == InvoiceType.Normal
                ? invoice.Payments
                    .Where(payment => payment.PaymentType == PaymentType.Advance && payment.AdvanceInvoiceId.HasValue)
                    .GroupBy(payment => new { AdvanceInvoiceId = payment.AdvanceInvoiceId!.Value, payment.AdvanceIic })
                    .Select(group => new FiscalAdvanceUsageRecord
                    {
                        Id = Guid.NewGuid(),
                        AdvanceInvoiceId = group.Key.AdvanceInvoiceId,
                        AdvanceIic = group.Key.AdvanceIic!,
                        UsedAmount = group.Sum(payment => payment.Amount),
                        CreatedAt = invoice.CreatedAt,
                        UpdatedAt = invoice.UpdatedAt
                    }).ToList()
                : []
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
                x.Reference,
                x.AdvanceInvoiceId,
                x.AdvanceIic)),
            record.AdvanceSettlementFinalInvoiceId,
            record.AdvanceSettlementFinalIic,
            record.AdvanceSettlementFinalIssuedAt,
            JsonSerializer.Deserialize<AdvanceSettlementAllocation[]>(record.AdvanceSettlementAllocationsJson),
            record.AdvanceRestorationStornoInvoiceId, record.AdvanceRestorationStornoIic, record.AdvanceRestorationStornoIssuedAt,
            record.OrderSummaryJson is null ? null : JsonSerializer.Deserialize<OrderSummaryData>(record.OrderSummaryJson));

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
