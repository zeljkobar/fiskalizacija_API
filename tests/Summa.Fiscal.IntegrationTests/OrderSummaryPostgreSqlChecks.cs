using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Repositories;

internal static class OrderSummaryPostgreSqlChecks
{
    public static async Task RunAsync(DbContextOptions<SummaFiscalDbContext> options, Guid company, Guid unit, Guid device, Guid op)
    {
        var time = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        FiscalInvoiceApplicationService Service(SummaFiscalDbContext db) => new(new PostgreSqlFiscalInvoiceRepository(db),
            new FiscalInvoiceValidator(), new PostgreSqlAuditService(db), new PostgreSqlInvoiceNumberSequence(db));
        async Task<FiscalInvoiceResult> Order()
        {
            await using var db = new SummaFiscalDbContext(options);
            var invoice = await Service(db).CreateAsync(new(company, unit, device, op, InvoiceType.Normal, "", time, "EUR", null,
                null, null, null, [new("Kafa", 2, 121, 21, null, "kom", 0)], [new(PaymentType.Order, 242, null)],
                Guid.NewGuid().ToString(), "pg-order", "test"), default);
            await Complete(invoice.Id);
            return invoice;
        }
        CreateSummaryCommand SummaryCommand(params Guid[] ids) => new(company, device, op, ids, time.AddHours(1),
            [new(PaymentType.Card, 242, null)], Guid.NewGuid().ToString(), "pg-summary", "test");
        async Task<FiscalInvoiceResult> Summary(CreateSummaryCommand command)
        {
            await using var db = new SummaFiscalDbContext(options);
            return await Service(db).CreateSummaryAsync(command, default);
        }
        async Task<FiscalInvoiceResult> Correction(Guid id, decimal quantity)
        {
            await using var db = new SummaFiscalDbContext(options);
            var order = (await Service(db).GetAsync(id, default))!;
            return await Service(db).CreateOrderCorrectionAsync(new(id, time.AddHours(2), "Povrat", [new(order.Items.Single().Id, quantity)],
                Guid.NewGuid().ToString(), "pg-correction", "test"), default);
        }
        async Task<FiscalInvoice> Complete(Guid id, bool fail = false)
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
            if (invoice.OrderSummary is not null)
                await repo.CompleteOrderSummaryAsync(invoice, new("PG_ORDER_COMPLETED", invoice.Id, company, "pg-test",
                    fail ? new string('X', 201) : "test", DateTimeOffset.UtcNow, new Dictionary<string, string?>()), default);
            else await repo.UpdateAsync(invoice, default);
            return invoice;
        }
        async Task<string> Outcome(Func<Task> action)
        {
            try { await action(); return "ok"; }
            catch (FiscalInvoiceOperationException e) { return e.Code; }
        }

        var order = await Order();
        var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Outcome(async () => await Summary(SummaryCommand(order.Id)))));
        Assert(race.Count(x => x == "ok") == 1 && race.Contains("ORDER_ALREADY_SUMMARIZED"), "Dva sumarna su rezervisala isti ORDER.");
        var idempotentOrder = await Order();
        var idempotentCommand = SummaryCommand(idempotentOrder.Id);
        var idempotent = await Task.WhenAll(Summary(idempotentCommand), Summary(idempotentCommand));
        Assert(idempotent[0].Id == idempotent[1].Id, "Istovremeni isti ključ mora vratiti isti sumarni račun.");
        Guid summaryId;
        await using (var db = new SummaFiscalDbContext(options))
        {
            var stored = await new PostgreSqlFiscalInvoiceRepository(db).GetSummaryForOrderAsync(order.Id, default);
            summaryId = stored!.Id;
            Assert(stored.OrderSummary!.References.Single().Iic is { Length: 32 } && stored.IsInformational && stored.Payments.Single().PaymentType == PaymentType.Card,
                "Sumarni metapodaci nijesu trajno sačuvani.");
            await Service(db).CancelUnsubmittedAsync(summaryId, "pg-test", "test", default);
            Assert(await db.FiscalSummaryOrders.AnyAsync(x => x.SummaryInvoiceId == summaryId && x.ReleasedAt != null), "Otkazivanje nije trajno oslobodilo vezu.");
        }
        var readySummary = await Summary(SummaryCommand(order.Id));
        var claimRace = await Task.WhenAll(
            Outcome(async () => await Correction(order.Id, 1)),
            Outcome(async () =>
            {
                await using var db = new SummaFiscalDbContext(options);
                var repo = new PostgreSqlFiscalInvoiceRepository(db);
                var stored = (await repo.GetByIdAsync(readySummary.Id, default))!;
                if (stored.Status != FiscalStatus.ReadyForFiscalization) throw new FiscalInvoiceOperationException("INVOICE_STATE_CONFLICT", "Otkazan.");
                stored.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
                await repo.UpdateAsync(stored, default);
            }));
        Assert(claimRace.Count(x => x == "ok") == 1 && claimRace.Any(x => x is "SUMMARY_OUTCOME_UNRESOLVED" or "INVOICE_STATE_CONFLICT"),
            "Korekcija izvora i slanje sumarnog nemaju jedinstven ishod.");

        var capped = await Order();
        var capRace = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Outcome(async () => await Correction(capped.Id, 2))));
        Assert(capRace.Count(x => x == "ok") == 1 && capRace.Contains("ORDER_CORRECTION_EXCEEDED"), "Paralelne korekcije prekoračile su izvorne količine.");

        var closed = await Order();
        var closingSummary = await Summary(SummaryCommand(closed.Id));
        await Complete(closingSummary.Id);
        var first = await Correction(closed.Id, 1);
        await Complete(first.Id);
        await using (var db = new SummaFiscalDbContext(options))
            Assert((await Service(db).GetAsync(closed.Id, default))!.Status == FiscalStatus.Fiscalized, "Djelimična korekcija je stornirala cijeli ORDER.");
        var second = await Correction(closed.Id, 1);
        try { await Complete(second.Id, fail: true); throw new InvalidOperationException("Očekivan rollback."); }
        catch (DbUpdateException) { }
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert((await Service(db).GetAsync(second.Id, default))!.Status == FiscalStatus.FiscalizationPending &&
                (await Service(db).GetAsync(closed.Id, default))!.Status == FiscalStatus.Fiscalized, "Pad audita nije rollback-ovao status izvora i korekcije.");
        }
        await Complete(second.Id);
        FiscalInvoiceResult adjustment;
        var adjustmentCommand = new CreateSummaryCorrectionCommand(closingSummary.Id, time.AddHours(3), "Usklađenje", [new(PaymentType.Card, -242, null)],
            "pg-summary-adjust", "pg-test", "test");
        await using (var db = new SummaFiscalDbContext(options))
        {
            var service = Service(db);
            Assert((await service.GetOrderWorkflowAsync(closed.Id, default)).PendingSummaryCorrectionAmount == 242, "Nedostaje dug sumarnog nakon storna ORDER računa.");
            adjustment = await service.CreateSummaryCorrectionAsync(adjustmentCommand, default);
        }
        await using (var db = new SummaFiscalDbContext(options))
            Assert((await Service(db).CreateSummaryCorrectionAsync(adjustmentCommand, default)).Id == adjustment.Id, "Korekcija sumarnog nije idempotentna.");
        try { await Complete(adjustment.Id, fail: true); throw new InvalidOperationException("Očekivan rollback sumarnog."); }
        catch (DbUpdateException) { }
        await using (var db = new SummaFiscalDbContext(options))
            Assert((await Service(db).GetAsync(closingSummary.Id, default))!.Status == FiscalStatus.Fiscalized, "Pad audita promijenio je izvorni sumarni.");
        var completed = await Complete(adjustment.Id);
        await using (var db = new SummaFiscalDbContext(options))
        {
            var repo = new PostgreSqlFiscalInvoiceRepository(db);
            await repo.CompleteOrderSummaryAsync(completed, new("REPEATED", completed.Id, company, "pg-test", "test", DateTimeOffset.UtcNow, new Dictionary<string, string?>()), default);
            var status = await Service(db).GetSummaryWorkflowAsync(closingSummary.Id, default);
            Assert(status.RemainingAmount == 0 && status.PendingCorrectionAmount == 0 &&
                (await repo.GetByIdAsync(closingSummary.Id, default))!.Status == FiscalStatus.StornoCreated, "Završetak nije atomaran/idempotentan.");
            Assert((await repo.GetByIdAsync(adjustment.Id, default))!.OrderSummary!.AccountedCorrectionIds.Count == 2,
                "Izgubljene su trajne veze dvije izvorne korekcije.");
        }
        var paymentOrder = await Order();
        var wrongSummary = await Summary(SummaryCommand(paymentOrder.Id));
        await Complete(wrongSummary.Id);
        FiscalInvoiceResult cancellation;
        await using (var db = new SummaFiscalDbContext(options))
            cancellation = await Service(db).CreateSummaryCancellationAsync(new(wrongSummary.Id, time.AddHours(3), "Ispravka plaćanja",
                [new(PaymentType.Card, -242, null)], "pg-cancel-summary", "pg-test", "test"), default);
        try { await Complete(cancellation.Id, fail: true); throw new InvalidOperationException("Očekivan rollback poništenja."); }
        catch (DbUpdateException) { }
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert(await db.FiscalSummaryOrders.AnyAsync(x => x.SummaryInvoiceId == wrongSummary.Id && x.ReleasedAt == null),
                "Neuspjelo poništenje je oslobodilo izvorne ORDER račune.");
        }
        await Complete(cancellation.Id);
        await using (var db = new SummaFiscalDbContext(options))
        {
            Assert(await db.FiscalSummaryOrders.AnyAsync(x => x.SummaryInvoiceId == wrongSummary.Id && x.ReleasedAt != null) &&
                (await Service(db).GetAsync(paymentOrder.Id, default))!.Status == FiscalStatus.Fiscalized,
                "Poništenje nije atomarno oslobodilo vezu bez izmjene stvarnog prometa.");
        }
        var replacement = await Summary(SummaryCommand(paymentOrder.Id) with { IssueDateTime = time.AddHours(4), Payments = [new(PaymentType.Cash, 242, null)] });
        Assert(replacement.TotalGrossAmount == 242, "Ponovno zatvaranje nakon poništenja nije omogućeno.");
        Console.WriteLine("PostgreSQL ORDER/SUMMARY: jedinstveno zatvaranje, trka slanja/korekcije, rezervacije količina, round-trip, audit rollback, poništenje/zamjena i oporavak su provjereni.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
