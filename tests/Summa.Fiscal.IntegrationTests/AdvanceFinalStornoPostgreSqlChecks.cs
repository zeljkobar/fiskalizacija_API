using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Repositories;

internal static class AdvanceFinalStornoPostgreSqlChecks
{
    public static async Task RunAsync(DbContextOptions<SummaFiscalDbContext> options,
        Guid company, Guid unit, Guid device, Guid fiscalOperator)
    {
        FiscalInvoiceApplicationService Service(SummaFiscalDbContext db) => new(
            new PostgreSqlFiscalInvoiceRepository(db), new FiscalInvoiceValidator(), new PostgreSqlAuditService(db), new PostgreSqlInvoiceNumberSequence(db));
        CreateFiscalInvoiceCommand Command(InvoiceType type, Guid? source = null) => new(company, unit, device, fiscalOperator,
            type, "", new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero), "EUR",
            new(BuyerIdentificationType.Tin, "87654321", "Kupac", null, null, "MNE", null), null, null, null,
            [new("Usluga", 1, 242, 21, null, "kom", 0)],
            [new(source.HasValue ? PaymentType.Advance : PaymentType.BankAccount, 242, null, source)],
            Guid.NewGuid().ToString(), "pg-final-storno", "test");
        async Task<FiscalInvoiceResult> CreateAsync(CreateFiscalInvoiceCommand command)
        {
            await using var db = new SummaFiscalDbContext(options);
            return await Service(db).CreateAsync(command, default);
        }
        async Task<FiscalInvoice> MarkFiscalizedAsync(Guid id, bool failCompletion = false)
        {
            await using var db = new SummaFiscalDbContext(options);
            var repo = new PostgreSqlFiscalInvoiceRepository(db);
            var invoice = (await repo.GetByIdAsync(id, default))!;
            if (invoice.Status == FiscalStatus.ReadyForFiscalization)
            {
                invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
                await repo.UpdateAsync(invoice, default);
            }
            invoice.MarkFiscalized(Guid.NewGuid().ToString(), $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444");
            if (invoice.OriginalInvoiceId is { } originalId && invoice.AdvanceSettlementFinalInvoiceId is null)
            {
                var original = (await repo.GetByIdAsync(originalId, default))!;
                if (invoice.IsAdvanceRestoration) original.MarkAdvanceSettlementReversed();
                else original.MarkStornoCreated();
                await repo.CompleteCorrectiveAsync(invoice, original, default,
                    new("PG_TEST_CORRECTIVE_COMPLETED", invoice.Id, invoice.CompanyId, "pg-test",
                        failCompletion ? new string('X', 201) : "test", DateTimeOffset.UtcNow,
                        new Dictionary<string, string?> { ["originalInvoiceId"] = original.Id.ToString() }));
            }
            else await repo.UpdateAsync(invoice, default);
            return invoice;
        }
        async Task<(FiscalInvoiceResult Advance, FiscalInvoiceResult Final)> PairAsync()
        {
            var advance = await CreateAsync(Command(InvoiceType.Advance));
            await MarkFiscalizedAsync(advance.Id);
            var final = await CreateAsync(Command(InvoiceType.Normal, advance.Id));
            await MarkFiscalizedAsync(final.Id);
            return (advance, final);
        }
        async Task<FiscalInvoiceResult> ReductionAsync(FiscalInvoiceResult advance, FiscalInvoiceResult final)
        {
            await using var db = new SummaFiscalDbContext(options);
            return await Service(db).CreateAdvanceSettlementAsync(new(advance.Id, final.Id, final.IssueDateTime.AddHours(1),
                [new(advance.Items.Single().Id, 242)], Guid.NewGuid().ToString(), "pg-test", "test"), default);
        }
        CreateStornoCommand StornoCommand(Guid id) => new(id, "", new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero),
            "Povrat na avans", Guid.NewGuid().ToString(), "pg-test", "test");
        async Task<FiscalInvoiceResult> StornoAsync(Guid id)
        {
            await using var db = new SummaFiscalDbContext(options);
            return await Service(db).CreateStornoAsync(StornoCommand(id), default);
        }
        async Task<string> OutcomeAsync(Func<Task> action)
        {
            try { await action(); return "ok"; }
            catch (FiscalInvoiceOperationException exception) { return exception.Code; }
        }

        // The reduction submission claim and the final-storno preparation compete for the same rows.
        var (raceAdvance, raceFinal) = await PairAsync();
        var readyReduction = await ReductionAsync(raceAdvance, raceFinal);
        var race = await Task.WhenAll(
            OutcomeAsync(async () => await StornoAsync(raceFinal.Id)),
            OutcomeAsync(async () =>
            {
                await using var db = new SummaFiscalDbContext(options);
                var repo = new PostgreSqlFiscalInvoiceRepository(db);
                var invoice = (await repo.GetByIdAsync(readyReduction.Id, default))!;
                // A contender may have already cancelled it by the time this reader starts.
                if (invoice.Status != FiscalStatus.ReadyForFiscalization)
                    throw new FiscalInvoiceOperationException("INVOICE_STATE_CONFLICT", "Već otkazan.");
                invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
                await repo.UpdateAsync(invoice, default);
            }));
        Assert(race.Count(x => x == "ok") == 1 && race.Any(x => x is "INVOICE_STATE_CONFLICT" or "ADVANCE_SETTLEMENT_OUTCOME_UNRESOLVED"),
            "Istovremeni storno i slanje umanjenja moraju imati samo jednog pobjednika.");

        // A prepared reduction is atomically cancelled, then its stale sender is rejected.
        var (unreducedAdvance, unreducedFinal) = await PairAsync();
        var cancelledReduction = await ReductionAsync(unreducedAdvance, unreducedFinal);
        var unreducedStorno = await StornoAsync(unreducedFinal.Id);
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert((await Service(db).GetAsync(cancelledReduction.Id, default))!.Status == FiscalStatus.Cancelled, "Umanjenje nije atomarno otkazano.");
            Assert((await Service(db).GetAdvanceBalanceAsync(unreducedAdvance.Id, default))!.RemainingAmount == 0, "Priprema storna je prerano oslobodila saldo.");
        }
        await MarkFiscalizedAsync(unreducedStorno.Id);
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert((await Service(db).GetAdvanceBalanceAsync(unreducedAdvance.Id, default))!.RemainingAmount == 242,
                "Završetak storna bez fiskalizovanog umanjenja nije vratio avans.");
        }

        var (advance, final) = await PairAsync();
        var reduction = await ReductionAsync(advance, final);
        await MarkFiscalizedAsync(reduction.Id);
        var storno = await StornoAsync(final.Id);
        await MarkFiscalizedAsync(storno.Id);
        var restorationCommand = new CreateAdvanceRestorationCommand(storno.Id,
            new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.Zero), "pg-restoration", "pg-test", "test");
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => OutcomeAsync(async () =>
        {
            await using var db = new SummaFiscalDbContext(options);
            await Service(db).CreateAdvanceRestorationAsync(restorationCommand with { IdempotencyKey = "pg-restoration-" + i }, default);
        })));
        Assert(results.Count(x => x == "ok") == 1 && results.Contains("ADVANCE_RESTORATION_ALREADY_EXISTS"), "Paralelna obnova je napravila duplikate.");
        Guid restorationId;
        await using (var db = new SummaFiscalDbContext(options))
        {
            restorationId = (await Service(db).GetAdvanceStornoWorkflowAsync(final.Id, default)).RestorationInvoiceId!.Value;
            var read = (await Service(db).GetAsync(restorationId, default))!;
            Assert(read.AdvanceRestorationStornoInvoiceId == storno.Id && read.AdvanceRestorationStornoIic is { Length: 32 } &&
                read.TotalGrossAmount == 242 && read.TotalVatAmount == 42, "Veze/iznosi obnove nijesu sačuvani.");
            Assert((await Service(db).GetAdvanceBalanceAsync(advance.Id, default))!.PendingRestorationAmount == 242,
                "Nedovršena obnova mora ostaviti rezervaciju zauzetom.");
        }
        // Fail AFTER updating the ledger, inside the transaction, using an invalid audit field.
        try
        {
            await MarkFiscalizedAsync(restorationId, failCompletion: true);
            throw new InvalidOperationException("Očekivana greška čuvanja audit zapisa.");
        }
        catch (DbUpdateException) { }
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert((await Service(db).GetAsync(restorationId, default))!.Status == FiscalStatus.FiscalizationPending &&
                (await Service(db).GetAsync(reduction.Id, default))!.Status == FiscalStatus.Fiscalized &&
                !await db.FiscalAdvanceUsages.Where(x => x.FinalInvoiceId == final.Id).AnyAsync(x => x.ReleasedAt != null),
                "Rollback mora sačuvati sva prethodna stanja i rezervaciju.");
        }
        await MarkFiscalizedAsync(restorationId);
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert((await Service(db).GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 242 &&
                (await Service(db).GetAdvanceStornoWorkflowAsync(final.Id, default)).Stage == "Completed",
                "Pozitivna obnova nije dovršila saldo i workflow.");
            Assert((await Service(db).GetAsync(reduction.Id, default))!.Status == FiscalStatus.StornoCreated &&
                (await Service(db).GetAsync(advance.Id, default))!.Status == FiscalStatus.Fiscalized, "Obnova je izmijenila pogrešan original.");
        }
        await CreateAsync(Command(InvoiceType.Normal, advance.Id));
        Console.WriteLine("PostgreSQL konačni storno: trka sa slanjem umanjenja, jedinstvena obnova, rollback i ponovno korišćenje avansa su provjereni.");
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
