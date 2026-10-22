using Microsoft.EntityFrameworkCore;
using Npgsql;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Audit;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Entities;
using Summa.Fiscal.Persistence.Repositories;

internal static class AdvancePostgreSqlChecks
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("SUMMA_FISCAL_TEST_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("PostgreSQL avansni testovi preskočeni: SUMMA_FISCAL_TEST_ADMIN_CONNECTION nije konfigurisan.");
            return;
        }
        var adminSettings = new NpgsqlConnectionStringBuilder(connection);
        if (adminSettings.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Izolovani PostgreSQL testovi zahtijevaju lokalni server.");
        adminSettings.Database = "postgres";
        var databaseName = "summa_advance_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await command.ExecuteNonQueryAsync();
        var testSettings = new NpgsqlConnectionStringBuilder(connection) { Database = databaseName, Pooling = false };
        var options = new DbContextOptionsBuilder<SummaFiscalDbContext>().UseNpgsql(testSettings.ConnectionString).Options;
        try
        {
            await VerifyAsync(options);
            Console.WriteLine("PostgreSQL: migracije, round-trip avansa/umanjenja, konkurentna potrošnja, storno trka i otkazivanje su provjereni.");
        }
        finally
        {
            if (!databaseName.StartsWith("summa_advance_test_", StringComparison.Ordinal) || databaseName.Length != 51)
                throw new InvalidOperationException("Neočekivano ime izolovane testne baze.");
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyAsync(DbContextOptions<SummaFiscalDbContext> options)
    {
        var companyId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await using (var db = new SummaFiscalDbContext(options))
        {
            await db.Database.MigrateAsync();
            Assert(!db.Database.HasPendingModelChanges(), "EF snapshot nije usklađen.");
            db.Companies.Add(new() { Id = companyId, Tin = "12345678", LegalName = "Local integration test" });
            db.BusinessUnits.Add(new() { Id = unitId, CompanyId = companyId, Code = "aa111bb222", Name = "Test", Environment = "Test" });
            db.FiscalDevices.Add(new() { Id = deviceId, BusinessUnitId = unitId, TcrCode = "cc333dd444", InternalCode = "Local" });
            db.FiscalOperators.Add(new() { Id = operatorId, CompanyId = companyId, OperatorCode = "ee555ff666", Environment = "Test" });
            await db.SaveChangesAsync();
        }
        CreateFiscalInvoiceCommand Command(InvoiceType type, decimal amount, Guid? advance = null) => new(
            companyId, unitId, deviceId, operatorId, type, "", new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero), "EUR",
            new(BuyerIdentificationType.Tin, "87654321", "Kupac", null, "Bar", "MNE", null), null, null, null,
            [new("Test usluga", 1, amount, 21, null, "kom", 0)],
            [new(advance.HasValue ? PaymentType.Advance : PaymentType.BankAccount, amount, null, advance)],
            Guid.NewGuid().ToString(), "pg-advance-test", "test");
        FiscalInvoiceApplicationService Service(SummaFiscalDbContext db) => new(
            new PostgreSqlFiscalInvoiceRepository(db), new FiscalInvoiceValidator(), new PostgreSqlAuditService(db), new PostgreSqlInvoiceNumberSequence(db));
        async Task<FiscalInvoiceResult> CreateAsync(CreateFiscalInvoiceCommand command)
        {
            await using var db = new SummaFiscalDbContext(options);
            return await Service(db).CreateAsync(command, default);
        }
        async Task MarkFiscalizedAsync(Guid id)
        {
            await using var db = new SummaFiscalDbContext(options);
            var repository = new PostgreSqlFiscalInvoiceRepository(db);
            var invoice = (await repository.GetByIdAsync(id, default))!;
            invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('B', 512));
            await repository.UpdateAsync(invoice, default);
            invoice.MarkFiscalized(Guid.NewGuid().ToString(), $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444");
            await repository.UpdateAsync(invoice, default);
        }
        async Task<string> OutcomeAsync(Func<Task> action)
        {
            try { await action(); return "ok"; }
            catch (FiscalInvoiceOperationException exception) { return exception.Code; }
        }

        var advance = await CreateAsync(Command(InvoiceType.Advance, 242));
        await MarkFiscalizedAsync(advance.Id);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => OutcomeAsync(async () =>
            await CreateAsync(Command(InvoiceType.Normal, 200, advance.Id)))));
        Assert(outcomes.Count(x => x == "ok") == 1 && outcomes.Contains("ADVANCE_AMOUNT_EXCEEDED"), "Dva paralelna zahtjeva su prekoračila avans.");
        Guid finalId;
        await using (var db = new SummaFiscalDbContext(options))
        {
            var service = Service(db);
            var balance = (await service.GetAdvanceBalanceAsync(advance.Id, default))!;
            Assert(balance.RemainingAmount == 42 && balance.ReservedAmount == 200 && balance.UsedAmount == 0, "Rezervisani saldo nije tačan.");
            finalId = balance.Usages.Single().FinalInvoiceId;
            var final = (await service.GetAsync(finalId, default))!;
            Assert(final.Payments.Single().AdvanceInvoiceId == advance.Id && final.Payments.Single().AdvanceIic is { Length: 32 }, "Izgubljena je veza plaćanja u bazi.");
        }
        await MarkFiscalizedAsync(finalId);
        var settlementCommand = new CreateAdvanceSettlementCommand(advance.Id, finalId,
            new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.Zero), [new(advance.Items.Single().Id, 200)],
            "pg-settlement", "pg-test", "test");
        FiscalInvoiceResult settlement;
        await using (var db = new SummaFiscalDbContext(options))
            settlement = await Service(db).CreateAdvanceSettlementAsync(settlementCommand, default);
        await using (var db = new SummaFiscalDbContext(options))
        {
            var read = (await Service(db).GetAsync(settlement.Id, default))!;
            Assert(read.AdvanceSettlementFinalInvoiceId == finalId && read.AdvanceSettlementFinalIic is { Length: 32 } &&
                read.AdvanceSettlementAllocations!.Single().GrossAmount == 200 && read.TotalGrossAmount == -200, "Umanjenje nije trajno sačuvano.");
            Assert((await Service(db).CreateAdvanceSettlementAsync(settlementCommand, default)).Id == settlement.Id, "Ponovljeno umanjenje nije idempotentno.");
        }

        // Ready reservations may be cancelled, but a stale sender must not resurrect them.
        var cancellable = await CreateAsync(Command(InvoiceType.Normal, 42, advance.Id));
        await using var senderDb = new SummaFiscalDbContext(options);
        var senderRepository = new PostgreSqlFiscalInvoiceRepository(senderDb);
        var staleInvoice = (await senderRepository.GetByIdAsync(cancellable.Id, default))!;
        await using (var db = new SummaFiscalDbContext(options))
            await Service(db).CancelUnsubmittedAsync(cancellable.Id, "pg-test", "test", default);
        staleInvoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
        Assert(await OutcomeAsync(() => senderRepository.UpdateAsync(staleInvoice, default)) == "INVOICE_STATE_CONFLICT", "Zastarjeli zahtjev je poslao otkazani račun.");
        await using (var db = new SummaFiscalDbContext(options))
        {
            var balance = (await Service(db).GetAdvanceBalanceAsync(advance.Id, default))!;
            Assert(balance.RemainingAmount == 42 && balance.UsedAmount == 200 && balance.ReservedAmount == 0, "Otkazivanje nije vratilo raspoloživi saldo.");
            Assert(await db.FiscalAdvanceUsages.AnyAsync(x => x.FinalInvoiceId == cancellable.Id && x.ReleasedAt != null), "Nema trajnog traga oslobađanja rezervacije.");
            Assert(await db.FiscalAudits.CountAsync(x => x.Action == "FISCAL_ADVANCE_RESERVED" && x.CompanyId == companyId) == 2,
                "Rezervacije nijesu auditovane u transakciji.");
            Assert(await db.FiscalAudits.AnyAsync(x => x.Action == "FISCAL_ADVANCE_RESERVATION_RELEASED" && x.CompanyId == companyId),
                "Oslobađanje rezervacije nije auditovano.");
        }

        // Full storno preparation and advance allocation serialize on the same source row.
        var raceAdvance = await CreateAsync(Command(InvoiceType.Advance, 121));
        await MarkFiscalizedAsync(raceAdvance.Id);
        var race = await Task.WhenAll(
            OutcomeAsync(async () => await CreateAsync(Command(InvoiceType.Normal, 121, raceAdvance.Id))),
            OutcomeAsync(async () =>
            {
                await using var db = new SummaFiscalDbContext(options);
                await Service(db).CreateStornoAsync(new(raceAdvance.Id, "", DateTimeOffset.UtcNow,
                    "Integration race", "race-storno", "pg-test", "test"), default);
            }));
        Assert(race.Count(x => x == "ok") == 1 && race.Any(x => x is "ADVANCE_ALREADY_USED" or "ADVANCE_STORNO_PENDING"),
            "Storno i korišćenje avansa su oba prihvaćena.");
        await AdvanceFinalStornoPostgreSqlChecks.RunAsync(options, companyId, unitId, deviceId, operatorId);
        await OrderSummaryPostgreSqlChecks.RunAsync(options, companyId, unitId, deviceId, operatorId);
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
