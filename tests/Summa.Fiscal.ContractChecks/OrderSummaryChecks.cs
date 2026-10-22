using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Audit;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Infrastructure.Persistence;

internal static class OrderSummaryChecks
{
    public static async Task RunAsync(string schemaPath)
    {
        var repository = new InMemoryFiscalInvoiceRepository();
        var service = new FiscalInvoiceApplicationService(repository, new FiscalInvoiceValidator(), new InMemoryAuditService(), new InMemoryInvoiceNumberSequence());
        var company = Guid.NewGuid(); var unit = Guid.NewGuid(); var device = Guid.NewGuid(); var op = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.FromHours(2));
        CreateFiscalInvoiceCommand Order(decimal amount = 242) => new(company, unit, device, op, InvoiceType.Normal, "", time, "EUR",
            null, null, null, null, [new("Kafa", 2, amount / 2, 21, null, "kom", 0)], [new(PaymentType.Order, amount, null)],
            Guid.NewGuid().ToString(), "order-contract", "test");
        CreateSummaryCommand Summary(params Guid[] ids) => new(company, device, op, ids, time.AddHours(1),
            [new(PaymentType.Card, 242, null)], Guid.NewGuid().ToString(), "summary-contract", "test");
        CreateOrderCorrectionCommand Correction(Guid id, params OrderCorrectionAllocation[] allocations) => new(id, time.AddHours(2), "Vraćena stavka",
            allocations, Guid.NewGuid().ToString(), "order-correction", "test");
        async Task<FiscalInvoiceResult> FiscalizedOrder(CreateFiscalInvoiceCommand? command = null)
        {
            var result = await service.CreateAsync(command ?? Order(), default);
            await AdvanceWorkflowChecks.MarkFiscalizedAsync(repository, result.Id);
            return result;
        }
        async Task Complete(Guid id)
        {
            var invoice = (await repository.GetByIdAsync(id, default))!;
            invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
            await repository.UpdateAsync(invoice, default);
            invoice.MarkFiscalized(Guid.NewGuid().ToString(), $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444");
            await repository.CompleteOrderSummaryAsync(invoice, new("TEST_COMPLETED", id, company, "test", "test", DateTimeOffset.UtcNow, new Dictionary<string, string?>()), default);
        }

        await Expect(() => service.CreateAsync(Order() with { Payments = [new(PaymentType.Order, 121, null), new(PaymentType.Cash, 121, null)] }, default), "ORDER_PAYMENT_EXCLUSIVE");
        var draft = await service.CreateAsync(Order(), default);
        await Expect(() => service.CreateSummaryAsync(Summary(draft.Id), default), "SUMMARY_ORDER_REQUIRED");
        await Expect(() => service.CreateSummaryAsync(Summary(draft.Id, draft.Id), default), "SUMMARY_SOURCES_INVALID");
        await Expect(() => service.CreateAsync(Order() with { InvoiceType = InvoiceType.Summary }, default), "SUMMARY_WORKFLOW_REQUIRED");
        var original = await FiscalizedOrder();
        var cancellableOrder = await FiscalizedOrder();
        var cancelledCorrection = await service.CreateOrderCorrectionAsync(Correction(cancellableOrder.Id,
            new OrderCorrectionAllocation(cancellableOrder.Items.Single().Id, 1)), default);
        await service.CancelUnsubmittedAsync(cancelledCorrection.Id, "test", "test", default);
        var afterCancellation = await service.CreateOrderCorrectionAsync(Correction(cancellableOrder.Id), default);
        Assert(afterCancellation.TotalGrossAmount == -242, "Otkazivanje neposlate ORDER korekcije mora osloboditi njene količine.");
        var rounded = await FiscalizedOrder(Order(0.03m) with { Items = [new("Zaokruživanje", 3, 0.01m, 21, null, "kom", 0)] });
        await Expect(() => service.CreateOrderCorrectionAsync(Correction(rounded.Id, new OrderCorrectionAllocation(rounded.Items.Single().Id, 1)), default),
            "ORDER_TAX_ALLOCATION_INVALID");
        var command = Summary(original.Id);
        var summary = await service.CreateSummaryAsync(command, default);
        Assert(summary.IsInformational && summary.TotalGrossAmount == 242 && summary.TotalVatAmount == 42 &&
            summary.OrderSummary!.References.Single().InvoiceId == original.Id, "Sumarni iznosi i reference nijesu izvedeni iz izvora.");
        Assert((await service.CreateSummaryAsync(command, default)).Id == summary.Id, "Sumarni nije idempotentan.");
        await Expect(() => service.CreateSummaryAsync(Summary(original.Id), default), "ORDER_ALREADY_SUMMARIZED");
        await service.CancelUnsubmittedAsync(summary.Id, "test", "test", default);
        Assert((await service.GetOrderWorkflowAsync(original.Id, default)).Stage == "Open", "Otkazivanje nije oslobodilo ORDER.");
        var cancelledByCorrection = await service.CreateSummaryAsync(Summary(original.Id), default);
        var partial = await service.CreateOrderCorrectionAsync(Correction(original.Id, new OrderCorrectionAllocation(original.Items.Single().Id, 1)), default);
        Assert((await service.GetAsync(cancelledByCorrection.Id, default))!.Status == FiscalStatus.Cancelled,
            "Korekcija nije atomarno otkazala neposlati sumarni račun.");
        await Expect(() => service.CreateSummaryAsync(Summary(original.Id), default), "ORDER_CORRECTION_UNRESOLVED");
        await Expect(() => service.CreateOrderCorrectionAsync(Correction(original.Id, new OrderCorrectionAllocation(original.Items.Single().Id, 2)), default), "ORDER_CORRECTION_EXCEEDED");
        await Complete(partial.Id);
        Assert((await service.GetAsync(original.Id, default))!.Status == FiscalStatus.Fiscalized, "Djelimična korekcija ne smije potpuno stornirati izvor.");
        var reduced = await service.CreateSummaryAsync(Summary(original.Id) with { IssueDateTime = time.AddHours(3), Payments = [new(PaymentType.Cash, 121, null)] }, default);
        Assert(reduced.TotalGrossAmount == 121 && reduced.OrderSummary!.References.Count == 2 && reduced.TotalVatAmount == 21,
            "Sumarni nakon korekcije mora referencirati izvor i njegovu negativnu ORDER korekciju.");
        await Complete(reduced.Id);
        Assert((await service.GetAsync(original.Id, default))!.Status == FiscalStatus.Fiscalized, "Sumarni račun ne smije mijenjati fiskalni status ORDER izvora.");
        var full = await service.CreateStornoAsync(new(original.Id, "", time.AddHours(4), "Vraćen ostatak", "order-full-storno", "test", "test"), default);
        Assert(full.TotalGrossAmount == -121 && full.Payments.Single().PaymentType == PaymentType.Order,
            "Potpuni storno nakon djelimične korekcije mora vratiti samo preostali dio.");
        await Complete(full.Id);
        Assert((await service.GetAsync(original.Id, default))!.Status == FiscalStatus.StornoCreated, "Potpuno korigovan ORDER nije označen.");
        Assert((await service.GetSummaryWorkflowAsync(reduced.Id, default)).PendingCorrectionAmount == 121,
            "Nedostaje dug korekcije sumarnog računa.");
        var adjustCommand = new CreateSummaryCorrectionCommand(reduced.Id, time.AddHours(5), "Usklađenje povrata",
            [new(PaymentType.Cash, -121, null)], "summary-adjust", "test", "test");
        var adjustment = await service.CreateSummaryCorrectionAsync(adjustCommand, default);
        Assert(adjustment.IsInformational && adjustment.OriginalInvoiceId == reduced.Id && adjustment.TotalGrossAmount == -121 &&
            adjustment.OrderSummary!.AccountedCorrectionIds.Single() == full.Id, "Korekcija sumarnog mora odgovarati samo novim korekcijama.");
        Assert((await service.CreateSummaryCorrectionAsync(adjustCommand, default)).Id == adjustment.Id, "Korekcija sumarnog nije idempotentna.");
        await service.CancelUnsubmittedAsync(adjustment.Id, "test", "test", default);
        Assert((await service.GetSummaryWorkflowAsync(reduced.Id, default)).PendingCorrectionAmount == 121, "Otkazana korekcija ne smije sakriti dug.");
        adjustment = await service.CreateSummaryCorrectionAsync(adjustCommand with { IdempotencyKey = "summary-adjust-retry" }, default);
        await Recover(repository, adjustment.Id, company, unit, device, op, failCompletion: true);
        Assert((await service.GetAsync(reduced.Id, default))!.Status == FiscalStatus.StornoCreated &&
            (await service.GetSummaryWorkflowAsync(reduced.Id, default)).PendingCorrectionAmount == 0, "Oporavak nije atomarno zatvorio sumarni.");
        await Expect(() => service.CreateSummaryCorrectionAsync(adjustCommand with { IdempotencyKey = "none-left" }, default), "SUMMARY_ALREADY_RECONCILED");

        var a = await FiscalizedOrder(Order(121));
        var b = await FiscalizedOrder(Order(107) with { Items = [new("Stopa 7", 1, 107, 7, null, "kom", 0)] });
        var mixed = await service.CreateSummaryAsync(Summary(a.Id, b.Id) with { Payments = [new(PaymentType.Cash, 100, null), new(PaymentType.Card, 128, null)] }, default);
        Assert(mixed.TotalNetAmount == 200 && mixed.TotalVatAmount == 28 && mixed.TotalGrossAmount == 228, "Mješoviti PDV nije očuvan.");
        await Complete(mixed.Id);
        var foreign = await FiscalizedOrder(Order() with { CompanyId = Guid.NewGuid() });
        await Expect(() => service.CreateSummaryAsync(Summary(foreign.Id), default), "SUMMARY_CONTEXT_MISMATCH");
        var otherUnit = await FiscalizedOrder(Order() with { BusinessUnitId = Guid.NewGuid() });
        await Expect(() => service.CreateSummaryAsync(Summary(a.Id, otherUnit.Id), default), "SUMMARY_CONTEXT_MISMATCH");
        var buyer = new CreateFiscalBuyer(BuyerIdentificationType.Tin, "87654321", "Firma", null, "Bar", "MNE", null);
        var identified = await FiscalizedOrder(Order() with { Buyer = buyer });
        await Expect(() => service.CreateSummaryAsync(Summary(a.Id, identified.Id), default), "SUMMARY_BUYER_MISMATCH");
        await Expect(() => service.CreateSummaryAsync(Summary(identified.Id) with { IssueDateTime = time.AddMinutes(-1) }, default), "SUMMARY_DATE_INVALID");
        await Expect(() => service.CreateSummaryAsync(Summary(identified.Id) with { Payments = [new(PaymentType.Order, 242, null)] }, default), "SUMMARY_PAYMENT_INVALID");
        await Expect(() => service.CreateSummaryAsync(Summary(identified.Id) with { Payments = [new(PaymentType.BankAccount, 1, null)] }, default), "PAYMENT_TOTAL_MISMATCH");

        var unresolved = await FiscalizedOrder();
        var unresolvedSummary = await service.CreateSummaryAsync(Summary(unresolved.Id), default);
        var pending = (await repository.GetByIdAsync(unresolvedSummary.Id, default))!;
        pending.MarkFiscalizationPending(new string('D', 32), new string('B', 512));
        await repository.UpdateAsync(pending, default);
        await Expect(() => service.CreateOrderCorrectionAsync(Correction(unresolved.Id), default), "SUMMARY_OUTCOME_UNRESOLVED");
        await Recover(repository, unresolvedSummary.Id, company, unit, device, op);
        await Expect(() => service.CreateStornoAsync(new(mixed.Id, "", time.AddHours(6), "Pogrešan promet", "no-generic", "test", "test"), default), "SUMMARY_CORRECTION_WORKFLOW_REQUIRED");
        var mixedPartial = await service.CreateOrderCorrectionAsync(Correction(a.Id, new OrderCorrectionAllocation(a.Items.Single().Id, 1)), default);
        await Complete(mixedPartial.Id);
        var mixedAdjustment = await service.CreateSummaryCorrectionAsync(new(mixed.Id, time.AddHours(3), "Povrat jedne stavke",
            [new(PaymentType.Card, -60.50m, null)], "mixed-summary-adjustment", "test", "test"), default);
        await Complete(mixedAdjustment.Id);
        var cancellationCommand = new CreateSummaryCorrectionCommand(mixed.Id, time.AddHours(4), "Pogrešan način plaćanja",
            [new(PaymentType.Cash, -100, null), new(PaymentType.Card, -67.50m, null)], "summary-void", "test", "test");
        var cancellation = await service.CreateSummaryCancellationAsync(cancellationCommand, default);
        Assert(cancellation.TotalGrossAmount == -167.50m && cancellation.OrderSummary!.IsCancellation, "Poništenje mora vratiti samo preostalo sumarnog stanje.");
        await Expect(() => service.CreateSummaryAsync(Summary(a.Id, b.Id) with
            { IssueDateTime = time.AddHours(5), Payments = [new(PaymentType.BankAccount, 167.50m, null)] }, default), "ORDER_ALREADY_SUMMARIZED");
        await Recover(repository, cancellation.Id, company, unit, device, op, failCompletion: true);
        Assert((await service.GetSummaryWorkflowAsync(mixed.Id, default)).Stage == "Cancelled" &&
            (await service.GetAsync(b.Id, default))!.Status == FiscalStatus.Fiscalized &&
            (await service.GetOrderWorkflowAsync(a.Id, default)).Stage == "Open", "Poništenje sumarnog ne smije poništiti stvarni promet.");
        var replacement = await service.CreateSummaryAsync(Summary(a.Id, b.Id) with
            { IssueDateTime = time.AddHours(5), Payments = [new(PaymentType.BankAccount, 167.50m, null)] }, default);
        Assert(replacement.TotalGrossAmount == 167.50m, "Novi sumarni ne uzima preostali stvarni promet.");
        foreach (var id in new[] { original.Id, partial.Id, full.Id, reduced.Id, adjustment.Id, mixed.Id, mixedAdjustment.Id, cancellation.Id, replacement.Id })
            VerifyXml((await repository.GetByIdAsync(id, default))!, schemaPath);
        Assert(repository.AuditEntries.Any(x => x.Action == "FISCAL_SUMMARY_CANCELLED_FOR_ORDER_CORRECTION") &&
            repository.AuditEntries.Any(x => x.Action == "FISCAL_SUMMARY_CORRECTION_PREPARED"), "Nedostaje audit ORDER/sumarnog workflow-a.");
        Console.WriteLine("ORDER/SUMMARY: izvori, kupac, PDV, negativne reference, djelimične/potpune korekcije, praćenje, XSD/potpis i oporavak bez ponovnog slanja su provjereni.");
    }

    private static PuFiscalizationOptionsV5 Configuration() => new()
    {
        Environment = "Test", Endpoint = "https://efitest.tax.gov.me/fs-v1", IssuerTin = "12345678", BusinessUnitCode = "aa111bb222",
        TcrCode = "cc333dd444", SoftwareCode = "gg777hh888", OperatorCode = "ee555ff666", SellerName = "Test", SellerCountry = "MNE", IsIssuerInVat = true
    };
    private static RegisterInvoiceRequestV5 Request(FiscalInvoice invoice, string iic) => FiscalInvoiceRequestFactoryV5.Build(invoice,
        Configuration(), invoice.IssueDateTime, $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444", int.Parse(invoice.InvoiceNumber),
        new(iic, new string('B', 512), "test", "test", "test"));
    private static void VerifyXml(FiscalInvoice invoice, string schemaPath)
    {
        var request = Request(invoice, new string('A', 32));
        var document = new RegisterInvoiceXmlBuilderV5().BuildUnsigned(request);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Local ORDER contract", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = new FiscalXmlSignerV5().SignRequest(document, cert);
        var result = new FiscalXmlSchemaValidatorV5(schemaPath).Validate(signed.SignedDocument);
        Assert(signed.SignatureVerified && result.IsValid, "ORDER/sumarni XML nije validan: " + string.Join(";", result.Errors.Select(x => x.Message)));
        if (invoice.IsOrder || invoice.IsOrderCorrection)
            Assert(request.Invoice.Payments.Single().Type == PuPaymentMethodV5.Order && request.Invoice.TypeOfInvoice == PuInvoiceTypeV5.Cash, "ORDER mapiranje nije CASH/ORDER.");
        if (invoice.IsInformational)
        {
            Assert(request.Invoice.Items.Count == 0, "Informativni sumarni ne smije slati nove stavke prometa.");
            Assert(request.Invoice.SameTaxes!.Sum(x => x.VatAmount) == invoice.TotalVatAmount, "Poreska agregacija nije očuvana.");
            if (invoice.InvoiceType == InvoiceType.Summary)
                Assert(request.Invoice.InvoiceReferences!.Sum(x => x.Amount) == invoice.TotalGrossAmount &&
                    request.Invoice.SameTaxes!.All(t => t.NumberOfItems == invoice.OrderSummary!.References.Count(r => r.Taxes.Any(x => x.Rate == t.VatRate))),
                    "Reference i broj računa po stopi nijesu tačni.");
        }
    }

    private static async Task Recover(InMemoryFiscalInvoiceRepository repository, Guid id, Guid company, Guid unit, Guid device, Guid op, bool failCompletion = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "summa-order-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var invoice = (await repository.GetByIdAsync(id, default))!;
            if (invoice.Status == FiscalStatus.ReadyForFiscalization)
            {
                invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('B', 512));
                await repository.UpdateAsync(invoice, default);
            }
            var request = Request(invoice, invoice.Iic!);
            var store = new FileFiscalExchangeStoreV5(new() { RootPath = directory });
            var exchange = await store.SaveRequestAsync(new RegisterInvoiceXmlBuilderV5().BuildUnsigned(request).ToString(), request.Header.Uuid,
                "order-recovery", new Uri(Configuration().Endpoint), PuFiscalContractV5.RegisterInvoiceSoapAction, default);
            var fic = Guid.NewGuid().ToString();
            await store.SaveResponseAsync(exchange,
                $"""<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><RegisterInvoiceResponse xmlns="https://efi.tax.gov.me/fs/schema" Id="Response" Version="1"><Header UUID="{Guid.NewGuid()}" RequestUUID="{request.Header.Uuid}" SendDateTime="2026-10-22T16:00:00+02:00"/><FIC>{fic}</FIC></RegisterInvoiceResponse></soap:Body></soap:Envelope>""", 200, default);
            var onboarding = DispatchProxy.Create<IFiscalOnboardingService, AdvanceOnboardingProxy>();
            ((AdvanceOnboardingProxy)(object)onboarding).InvokeMethod = method => method.Name switch
            {
                "GetCompanyAsync" => Task.FromResult(new CompanySummary(company, "12345678", "Test", null, null, null, "MNE", true, true, "Test", Configuration().Endpoint, "gg777hh888", "hh111ii222", "All")),
                "GetBusinessUnitAsync" => Task.FromResult(new BusinessUnitSummary(unit, company, "Test", "aa111bb222", "Test", null, null, true)),
                "GetDeviceAsync" => Task.FromResult(new FiscalDeviceSummary(device, company, unit, "cc333dd444", "Test", "Registered", null, true)),
                "GetOperatorAsync" => Task.FromResult(new FiscalOperatorSummary(op, company, "Test", "ee555ff666", null, null, true)),
                _ => throw new InvalidOperationException("Oporavak ne smije slati PU: " + method.Name)
            };
            var proxy = DispatchProxy.Create<IFiscalInvoiceRepository, OrderSummaryRepositoryFailureProxy>();
            var failure = (OrderSummaryRepositoryFailureProxy)(object)proxy;
            failure.Target = repository; failure.Fail = failCompletion;
            var submission = new FiscalInvoiceSubmissionServiceV5(proxy, onboarding, new IicGeneratorV5(), new RegisterInvoiceXmlBuilderV5(),
                new FiscalXmlSignerV5(), new FiscalQrCodeGeneratorV5(), new SoapEnvelopeV5(), new RegisterInvoiceResponseParserV5(), store, new InMemoryAuditService());
            if (failCompletion)
            {
                try { await submission.SubmitAsync(id, "test", default); throw new InvalidOperationException("Očekivana greška čuvanja."); }
                catch (IOException) { }
                Assert((await repository.GetByIdAsync(id, default))!.Status == FiscalStatus.FiscalizationPending, "Pad čuvanja mora ostaviti dokument Pending.");
            }
            var recovered = await Task.WhenAll(submission.SubmitAsync(id, "test", default), submission.SubmitAsync(id, "test", default));
            Assert(recovered.All(x => x!.Jikr == fic && x.Status == FiscalStatus.Fiscalized) &&
                (await store.FindSuccessfulInvoicesByIicAsync(invoice.Iic!, default)).Count == 1, "Oporavak je promijenio JIKR ili stvorio novu razmjenu.");
        }
        finally { if (Directory.Exists(directory) && Path.GetFileName(directory).StartsWith("summa-order-contract-", StringComparison.Ordinal)) Directory.Delete(directory, true); }
    }
    private static async Task Expect(Func<Task> action, string code)
    {
        try { await action(); }
        catch (FiscalInvoiceOperationException x) when (x.Code == code) { return; }
        catch (FiscalValidationException x) when (x.Errors.Any(e => e.Code == code)) { return; }
        throw new InvalidOperationException("Očekivana greška: " + code);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public class OrderSummaryRepositoryFailureProxy : DispatchProxy
{
    public IFiscalInvoiceRepository Target { get; set; } = null!;
    public bool Fail { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IFiscalInvoiceRepository.CompleteOrderSummaryAsync) && Fail)
        { Fail = false; return Task.FromException(new IOException("Simulirana greška čuvanja ORDER/sumarne fiskalizacije.")); }
        try { return method.Invoke(Target, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }
}
