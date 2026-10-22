using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Audit;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Infrastructure.Persistence;

internal static class AdvanceWorkflowChecks
{
    public static async Task RunAsync(string schemaPath)
    {
        var repository = new InMemoryFiscalInvoiceRepository();
        var audit = new InMemoryAuditService();
        var service = new FiscalInvoiceApplicationService(repository, new FiscalInvoiceValidator(), audit, new InMemoryInvoiceNumberSequence());
        var companyId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var issuedAt = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(2));
        var buyer = new CreateFiscalBuyer(BuyerIdentificationType.Tin, "12345678", "Test kupac", "Adresa", "Bar", "MNE", null);
        CreateFiscalInvoiceCommand Command(InvoiceType type, decimal gross, IReadOnlyCollection<CreateFiscalPayment> payments) => new(
            companyId, unitId, deviceId, operatorId, type, "", issuedAt, "EUR", buyer, null, null, null,
            [new("Usluga", 1, gross, 21, null, "kom", 0)], payments, Guid.NewGuid().ToString(), "advance-contract", "test");

        var advanceCommand = Command(InvoiceType.Advance, 242, [new(PaymentType.BankAccount, 242, null)]) with
        { Items = [new("Prva usluga", 1, 121, 21, null, "kom", 0), new("Druga usluga", 1, 121, 21, null, "kom", 0)] };
        var created = await service.CreateAsync(advanceCommand, default);
        Assert(created.InvoiceType == InvoiceType.Advance && created.Status == FiscalStatus.ReadyForFiscalization, "Avans nije pripremljen.");
        var advance = await MarkFiscalizedAsync(repository, created.Id);
        var finalCommand = Command(InvoiceType.Normal, 121,
            [new(PaymentType.Advance, 60.50m, null, advance.Id), new(PaymentType.BankAccount, 60.50m, null)]) with { IssueDateTime = issuedAt.AddHours(1) };
        var final = await service.CreateAsync(finalCommand, default);
        Assert(final.Payments.Single(x => x.PaymentType == PaymentType.Advance).AdvanceIic == advance.Iic, "IKOF mora biti preuzet sa avansa.");
        Assert((await service.CreateAsync(finalCommand, default)).Id == final.Id, "Idempotentni zahtjev rezervisao je novi dokument.");
        var balance = await service.GetAdvanceBalanceAsync(advance.Id, default);
        Assert(balance?.RemainingAmount == 181.50m && balance.ReservedAmount == 60.50m && balance.UsedAmount == 0,
            "Djelimična rezervacija nije ispravna.");
        Assert(final.AdvanceAppliedAmount == 60.50m && final.AmountToPayAfterAdvance == 60.50m,
            "Konačni račun ne prikazuje odvojeno promet, primijenjeni avans i ostatak.");
        await ExpectCodeAsync(() => service.CreateStornoAsync(new(advance.Id, "", issuedAt.AddHours(2), "Povrat", "storno-used", "test", "test"), default), "ADVANCE_ALREADY_USED");

        // A successful final invoice needs its own corrective advance document to avoid double VAT.
        await MarkFiscalizedAsync(repository, final.Id);
        var allocations = new[] { new AdvanceSettlementAllocation(advance.Items.First().Id, 60.50m) };
        var settlementCommand = new CreateAdvanceSettlementCommand(advance.Id, final.Id, issuedAt.AddHours(2), allocations, "settlement-one", "test", "test");
        var settlement = await service.CreateAdvanceSettlementAsync(settlementCommand, default);
        Assert(settlement.TotalGrossAmount == -60.50m && settlement.TotalVatAmount == -10.50m &&
            settlement.AdvanceSettlementFinalIic == (await repository.GetByIdAsync(final.Id, default))!.Iic, "Umanjenje avansa nema tačne iznose i referencu.");
        Assert((await service.CreateAdvanceSettlementAsync(settlementCommand, default)).Id == settlement.Id, "Umanjenje nije idempotentno.");
        await ExpectCodeAsync(() => service.CreateAdvanceSettlementAsync(settlementCommand with { IdempotencyKey = "duplicate-settlement" }, default), "ADVANCE_SETTLEMENT_ALREADY_EXISTS");

        var second = await service.CreateAsync(Command(InvoiceType.Normal, 242,
            [new(PaymentType.Advance, 181.50m, null, advance.Id), new(PaymentType.BankAccount, 60.50m, null)]) with { IssueDateTime = issuedAt.AddHours(3) }, default);
        await MarkFiscalizedAsync(repository, second.Id);
        var secondSettlement = await service.CreateAdvanceSettlementAsync(new(advance.Id, second.Id, issuedAt.AddHours(4),
            [new(advance.Items.First().Id, 60.50m), new(advance.Items.Last().Id, 121)], "settlement-two", "test", "test"), default);
        Assert(secondSettlement.TotalGrossAmount == -181.50m, "Drugo djelimično umanjenje nije ispravno.");
        Assert((await service.GetAdvanceBalanceAsync(advance.Id, default))!.UnsettledAmount == 242m,
            "Saldo mora prikazati umanjenja koja još nijesu fiskalizovana.");
        Assert((await service.GetAdvanceBalanceAsync(advance.Id, default))?.RemainingAmount == 0, "Potpuni iznos avansa nije iskorišćen.");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 1, [new(PaymentType.Advance, 1, null, advance.Id)]), default), "ADVANCE_AMOUNT_EXCEEDED");
        await ExpectCodeAsync(() => service.CreateAsync(finalCommand with { CompanyId = Guid.NewGuid(), IdempotencyKey = "foreign" }, default), "ADVANCE_COMPANY_MISMATCH");
        await ExpectCodeAsync(() => service.CreateAsync(finalCommand with { Buyer = buyer with { IdentificationNumber = "87654321" }, IdempotencyKey = "buyer" }, default), "ADVANCE_BUYER_MISMATCH");
        await ExpectCodeAsync(() => service.CreateAsync(finalCommand with { IssueDateTime = issuedAt.AddMinutes(-1), IdempotencyKey = "date" }, default), "ADVANCE_DATE_INVALID");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 1, [new(PaymentType.Advance, 1, null)]), default), "ADVANCE_REFERENCE_REQUIRED");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 2,
            [new(PaymentType.Advance, 1, null, advance.Id), new(PaymentType.Advance, 1, null, advance.Id)]), default), "MULTIPLE_ADVANCE_PAYMENTS_NOT_SUPPORTED");
        var sourceA = await service.CreateAsync(Command(InvoiceType.Advance, 121, [new(PaymentType.BankAccount, 121, null)]), default);
        var sourceB = await service.CreateAsync(Command(InvoiceType.Advance, 242, [new(PaymentType.BankAccount, 242, null)]), default);
        await MarkFiscalizedAsync(repository, sourceA.Id);
        await MarkFiscalizedAsync(repository, sourceB.Id);
        var multipleAdvances = Command(InvoiceType.Normal, 363,
            [new(PaymentType.Advance, 121, null, sourceA.Id), new(PaymentType.Advance, 242, null, sourceB.Id)]);
        await ExpectCodeAsync(() => service.CreateAsync(multipleAdvances, default), "MULTIPLE_ADVANCE_PAYMENTS_NOT_SUPPORTED");
        Assert(await repository.GetByIdempotencyKeyAsync(companyId, multipleAdvances.IdempotencyKey, default) is null,
            "Nevažeći višestruki avans ne smije sačuvati konačni račun.");
        Assert((await service.GetAdvanceBalanceAsync(sourceA.Id, default))!.RemainingAmount == 121 &&
            (await service.GetAdvanceBalanceAsync(sourceB.Id, default))!.RemainingAmount == 242,
            "Odbijeni višestruki avans ne smije zauzeti saldo nijednog izvora.");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 1, [new(PaymentType.BankAccount, 1, null, advance.Id)]), default), "ADVANCE_REFERENCE_NOT_ALLOWED");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Advance, 1, [new(PaymentType.Cash, 1, null)]), default), "ADVANCE_BANK_RECEIPT_REQUIRED");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 1, [new(PaymentType.Advance, 1, null, final.Id)]), default), "ADVANCE_TYPE_REQUIRED");

        var unused = await service.CreateAsync(Command(InvoiceType.Advance, 121, [new(PaymentType.BankAccount, 121, null)]), default);
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 1, [new(PaymentType.Advance, 1, null, unused.Id)]), default), "ADVANCE_NOT_FISCALIZED");
        var unusedDomain = await MarkFiscalizedAsync(repository, unused.Id);
        var cancelled = await service.CreateAsync(Command(InvoiceType.Normal, 121, [new(PaymentType.Advance, 121, null, unused.Id)]), default);
        await service.CancelUnsubmittedAsync(cancelled.Id, "test", "test", default);
        Assert((await service.GetAdvanceBalanceAsync(unused.Id, default))?.RemainingAmount == 121, "Otkazivanje nije oslobodilo rezervaciju.");
        var stale = await repository.GetByIdAsync(cancelled.Id, default);
        Assert(stale!.Status == FiscalStatus.Cancelled, "Otkazani dokument nije sačuvan.");
        var pending = await service.CreateAsync(Command(InvoiceType.Normal, 121, [new(PaymentType.Advance, 121, null, unused.Id)]), default);
        var pendingDomain = (await repository.GetByIdAsync(pending.Id, default))!;
        pendingDomain.MarkFiscalizationPending(Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('B', 512));
        await repository.UpdateAsync(pendingDomain, default);
        await ExpectCodeAsync(() => service.CancelUnsubmittedAsync(pending.Id, "test", "test", default), "INVOICE_CANNOT_BE_CANCELLED");
        pendingDomain.MarkFiscalizationFailed();
        await repository.UpdateAsync(pendingDomain, default);
        Assert((await service.GetAdvanceBalanceAsync(unused.Id, default))?.RemainingAmount == 0, "Neuspjelo slanje ne smije osloboditi rezervaciju.");
        await ExpectCodeAsync(() => service.CancelUnsubmittedAsync(pending.Id, "test", "test", default), "INVOICE_CANNOT_BE_CANCELLED");

        var refundable = await service.CreateAsync(Command(InvoiceType.Advance, 121, [new(PaymentType.BankAccount, 121, null)]), default);
        await MarkFiscalizedAsync(repository, refundable.Id);
        var refund = await service.CreateStornoAsync(new(refundable.Id, "", issuedAt.AddHours(3), "Povrat neiskorišćenog avansa", "refund", "test", "test"), default);
        Assert(refund.TotalGrossAmount == -121, "Storno neiskorišćenog avansa nije negativan.");
        await ExpectCodeAsync(() => service.CreateAsync(Command(InvoiceType.Normal, 121, [new(PaymentType.Advance, 121, null, refundable.Id)]), default), "ADVANCE_STORNO_PENDING");

        var configuration = new PuFiscalizationOptionsV5
        {
            Environment = "Test", Endpoint = "https://efitest.tax.gov.me/fs-v1", IssuerTin = "12345678",
            BusinessUnitCode = "aa111bb222", TcrCode = "cc333dd444", SoftwareCode = "gg777hh888", OperatorCode = "ee555ff666",
            SellerName = "Test izdavalac", SellerCountry = "MNE", IsIssuerInVat = true
        };
        foreach (var id in new[] { advance.Id, final.Id, settlement.Id, secondSettlement.Id, refund.Id })
            VerifyXml((await repository.GetByIdAsync(id, default))!, configuration, schemaPath);
        VerifyMultipleAdvanceXmlConstraint((await repository.GetByIdAsync(final.Id, default))!, configuration, schemaPath);
        await VerifyRecoveryAsync(repository, service, configuration, settlement.Id, advance, unitId, deviceId, operatorId);
        await ExpectCodeAsync(() => service.CreateStornoAsync(new(settlement.Id, "", issuedAt.AddHours(5),
            "Ne podržavamo storno umanjenja", "storno-settlement", "test", "test"), default), "CORRECTIVE_STORNO_NOT_SUPPORTED");
        await VerifyRecoveryAsync(repository, service, configuration, pending.Id, unusedDomain, unitId, deviceId, operatorId);
        var mixed = await service.CreateAsync(Command(InvoiceType.Advance, 228, [new(PaymentType.BankAccount, 228, null)]) with
        { Items = [new("Stopa 21", 1, 121, 21, null, "kom", 0), new("Stopa 7", 1, 107, 7, null, "kom", 0)] }, default);
        var mixedSource = await MarkFiscalizedAsync(repository, mixed.Id);
        var mixedFinal = await service.CreateAsync(Command(InvoiceType.Normal, 228, [new(PaymentType.Advance, 228, null, mixed.Id)]), default);
        await MarkFiscalizedAsync(repository, mixedFinal.Id);
        var mixedSettlement = await service.CreateAdvanceSettlementAsync(new(mixed.Id, mixedFinal.Id, issuedAt.AddHours(1),
            mixedSource.Items.Select(x => new AdvanceSettlementAllocation(x.Id, x.GrossAmount)).ToArray(), "mixed-settlement", "test", "test"), default);
        Assert(mixedSettlement.TotalNetAmount == -200 && mixedSettlement.TotalVatAmount == -28 && mixedSettlement.TotalGrossAmount == -228,
            "Umanjenje mora sačuvati izvorne poreske stope avansa.");
        VerifyXml((await repository.GetByIdAsync(mixedSettlement.Id, default))!, configuration, schemaPath);
        Assert(audit.Entries.Any(x => x.Action == "FISCAL_ADVANCE_SETTLEMENT_ACCEPTED") &&
            audit.Entries.Any(x => x.Action == "FISCAL_UNSUBMITTED_INVOICE_CANCELLED"), "Nedostaje audit.");
        Assert(repository.AuditEntries.Any(x => x.Action == "FISCAL_ADVANCE_RESERVED") &&
            repository.AuditEntries.Any(x => x.Action == "FISCAL_ADVANCE_RESERVATION_RELEASED"), "Nedostaje atomski audit rezervacija.");
        Console.WriteLine("Avansi: kreiranje, rezervacije, umanjenja, storno, izolacija, otkazivanje, XML/XSD i oporavak su provjereni.");
        Console.WriteLine("Više avansa: zvanični XSD odbija dva ADVANCE načina plaćanja; API odbija zahtjev bez čuvanja i rezervacije salda.");
    }

    private static void VerifyMultipleAdvanceXmlConstraint(FiscalInvoice invoice, PuFiscalizationOptionsV5 configuration, string schemaPath)
    {
        var request = FiscalInvoiceRequestFactoryV5.Build(invoice, configuration, invoice.IssueDateTime,
            $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444", int.Parse(invoice.InvoiceNumber),
            new(new string('A', 32), new string('B', 512), "test", "test", "test"));
        var xml = new RegisterInvoiceXmlBuilderV5().BuildUnsigned(request);
        var methods = xml.Root!.Element(PuFiscalContractV5.Schema + "Invoice")!
            .Element(PuFiscalContractV5.Schema + "PayMethods")!;
        var payment = methods.Elements().Single(x => (string?)x.Attribute("Type") == "ADVANCE");
        var second = new XElement(payment);
        payment.SetAttributeValue("Amt", "30.25");
        second.SetAttributeValue("Amt", "30.25");
        second.SetAttributeValue("AdvIIC", new string('C', 32));
        methods.Add(second);
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=Local XSD constraint test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = new FiscalXmlSignerV5().SignRequest(xml, cert);
        var validation = new FiscalXmlSchemaValidatorV5(schemaPath).Validate(signed.SignedDocument);
        Assert(signed.SignatureVerified && !validation.IsValid &&
            validation.Errors.Any(x => x.Message.Contains("UniquePayMethodType", StringComparison.Ordinal)),
            "Zvanični XSD mora odbiti dva ADVANCE plaćanja čak i kada imaju različite IKOF-e i ispravan zbir.");
    }

    private static void VerifyXml(FiscalInvoice invoice, PuFiscalizationOptionsV5 configuration, string schemaPath)
    {
        var request = FiscalInvoiceRequestFactoryV5.Build(invoice, configuration, invoice.IssueDateTime,
            $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444", int.Parse(invoice.InvoiceNumber),
            new IicGenerationResultV5(new string('A', 32), new string('B', 512), "test", "test", "test"));
        var xml = new RegisterInvoiceXmlBuilderV5().BuildUnsigned(request);
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=Local contract test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = new FiscalXmlSignerV5().SignRequest(xml, cert);
        var validation = new FiscalXmlSchemaValidatorV5(schemaPath).Validate(signed.SignedDocument);
        Assert(signed.SignatureVerified && validation.IsValid,
            "Avansni XML/potpis nije validan: " + string.Join("; ", validation.Errors.Select(x => x.Message)));
        var element = xml.Root!.Element(PuFiscalContractV5.Schema + "Invoice")!;
        if (invoice.InvoiceType == InvoiceType.Advance) Assert(element.Attribute("InvType")!.Value == "ADVANCE", "InvType nije ADVANCE.");
        if (invoice.Payments.Any(x => x.PaymentType == PaymentType.Advance))
            Assert(element.Element(PuFiscalContractV5.Schema + "PayMethods")!.Elements().Single(x => x.Attribute("Type")!.Value == "ADVANCE")
                .Attribute("AdvIIC")!.Value == invoice.Payments.Single(x => x.PaymentType == PaymentType.Advance).AdvanceIic, "Nedostaje AdvIIC.");
        if (invoice.AdvanceSettlementFinalInvoiceId.HasValue)
        {
            var reference = element.Element(PuFiscalContractV5.Schema + "IICRefs")!.Elements().Single();
            Assert(reference.Attribute("IIC")!.Value == invoice.AdvanceSettlementFinalIic &&
                decimal.Parse(reference.Attribute("Amount")!.Value, System.Globalization.CultureInfo.InvariantCulture) == -invoice.TotalGrossAmount,
                "Umanjenje nema referencu na konačni račun.");
            Assert(element.Element(PuFiscalContractV5.Schema + "CorrectiveInv")!.Attribute("IICRef")!.Value == invoice.OriginalIic,
                "Umanjenje nema referencu na avans.");
        }
    }

    private static async Task VerifyRecoveryAsync(InMemoryFiscalInvoiceRepository repository,
        FiscalInvoiceApplicationService service, PuFiscalizationOptionsV5 configuration,
        Guid settlementId, FiscalInvoice advance, Guid unitId, Guid deviceId, Guid operatorId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "summa-advance-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var invoice = (await repository.GetByIdAsync(settlementId, default))!;
            var previousRemaining = (await service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount;
            var iic = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var request = FiscalInvoiceRequestFactoryV5.Build(invoice, configuration, invoice.IssueDateTime,
                $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444", int.Parse(invoice.InvoiceNumber),
                new(iic, new string('B', 512), "test", "test", "test"));
            invoice.MarkFiscalizationPending(iic, new string('B', 512));
            await repository.UpdateAsync(invoice, default);
            var store = new FileFiscalExchangeStoreV5(new() { RootPath = directory });
            var exchangeId = await store.SaveRequestAsync(new RegisterInvoiceXmlBuilderV5().BuildUnsigned(request).ToString(),
                request.Header.Uuid, "test-recovery", new Uri(configuration.Endpoint), PuFiscalContractV5.RegisterInvoiceSoapAction, default);
            var fic = Guid.NewGuid().ToString();
            await store.SaveResponseAsync(exchangeId,
                $"""<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><RegisterInvoiceResponse xmlns="https://efi.tax.gov.me/fs/schema" Id="Response" Version="1"><Header UUID="{Guid.NewGuid()}" RequestUUID="{request.Header.Uuid}" SendDateTime="2026-10-22T14:00:00+02:00"/><FIC>{fic}</FIC></RegisterInvoiceResponse></soap:Body></soap:Envelope>""", 200, default);
            var onboarding = DispatchProxy.Create<IFiscalOnboardingService, AdvanceOnboardingProxy>();
            ((AdvanceOnboardingProxy)(object)onboarding).InvokeMethod = method => method.Name switch
            {
                "GetCompanyAsync" => Task.FromResult(new CompanySummary(advance.CompanyId, configuration.IssuerTin, "Test", null, null, null, "MNE", true, true, "Test", configuration.Endpoint, configuration.SoftwareCode, "hh111ii222", "BankOnly")),
                "GetBusinessUnitAsync" => Task.FromResult(new BusinessUnitSummary(unitId, advance.CompanyId, "Test", configuration.BusinessUnitCode, "Test", null, null, true)),
                "GetDeviceAsync" => Task.FromResult(new FiscalDeviceSummary(deviceId, advance.CompanyId, unitId, configuration.TcrCode, "Test", "Registered", null, true)),
                "GetOperatorAsync" => Task.FromResult(new FiscalOperatorSummary(operatorId, advance.CompanyId, "Test", configuration.OperatorCode, null, null, true)),
                _ => throw new InvalidOperationException("Oporavak ne smije slati PU niti učitavati sertifikat: " + method.Name)
            };
            var submission = new FiscalInvoiceSubmissionServiceV5(repository, onboarding, new IicGeneratorV5(),
                new RegisterInvoiceXmlBuilderV5(), new FiscalXmlSignerV5(), new FiscalQrCodeGeneratorV5(),
                new SoapEnvelopeV5(), new RegisterInvoiceResponseParserV5(), store, new InMemoryAuditService());
            var recovered = await submission.SubmitAsync(invoice.Id, "recovery", default);
            var price = Math.Abs(invoice.TotalGrossAmount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Assert(recovered?.Jikr == fic && recovered.Status == FiscalStatus.Fiscalized && recovered.QrCodeData!.Contains("prc=" + price), "Oporavak računa nije uspio.");
            Assert((await repository.GetByIdAsync(advance.Id, default))!.Status == FiscalStatus.Fiscalized, "Djelimično umanjenje ne smije označiti avans kao storniran.");
            var retry = await submission.RecoverAsync(invoice.Id, exchangeId, "recovery-again", "test", default);
            Assert(retry?.Jikr == fic && (await service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == previousRemaining,
                "Ponovljeni oporavak je promijenio saldo.");
        }
        finally
        {
            if (Path.GetFileName(directory).StartsWith("summa-advance-recovery-", StringComparison.Ordinal) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    internal static async Task<FiscalInvoice> MarkFiscalizedAsync(IFiscalInvoiceRepository repository, Guid id)
    {
        var invoice = (await repository.GetByIdAsync(id, default))!;
        invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('B', 512));
        await repository.UpdateAsync(invoice, default);
        invoice.MarkFiscalized(Guid.NewGuid().ToString(), $"aa111bb222/{invoice.InvoiceNumber}/2026/cc333dd444");
        await repository.UpdateAsync(invoice, default);
        return invoice;
    }

    private static async Task ExpectCodeAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (FiscalInvoiceOperationException exception) when (exception.Code == code) { return; }
        catch (FiscalValidationException exception) when (exception.Errors.Any(x => x.Code == code)) { return; }
        throw new InvalidOperationException("Očekivana greška: " + code);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

public class AdvanceOnboardingProxy : DispatchProxy
{
    public Func<MethodInfo, object?> InvokeMethod { get; set; } = _ => throw new NotImplementedException();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!);
}
