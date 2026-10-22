using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Audit;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Infrastructure.Persistence;

internal static class AdvanceFinalStornoChecks
{
    public static async Task RunAsync(string schemaPath)
    {
        var arbitraryPositiveCorrection = new FiscalInvoice(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            InvoiceType.Corrective, "1", DateTimeOffset.UtcNow, "EUR", "invalid-positive", originalInvoiceId: Guid.NewGuid(),
            originalIic: new string('A', 32), originalIssueDateTime: DateTimeOffset.UtcNow.AddDays(-1),
            correctiveType: CorrectiveInvoiceType.Corrective, correctionReason: "Nedozvoljeni pozitivni obični storno");
        arbitraryPositiveCorrection.AddItem(new("Test", 1, 121, 21));
        arbitraryPositiveCorrection.AddPayment(new(PaymentType.BankAccount, 121));
        Assert(new FiscalInvoiceValidator().Validate(arbitraryPositiveCorrection).Errors.Any(x => x.Code == "CORRECTIVE_TOTAL_INVALID"),
            "Pozitivni iznosi smiju proći samo za kontrolisanu obnovu prethodnog umanjenja.");
        foreach (var prepareReduction in new[] { false, true })
        {
            using var fixture = new Fixture(schemaPath);
            var (advance, final) = await fixture.CreatePairAsync(242, 363, 242);
            FiscalInvoiceResult? reduction = prepareReduction ? await fixture.ReduceAsync(advance, final, 242) : null;
            var staleReduction = reduction is null ? null : await fixture.Repository.GetByIdAsync(reduction.Id, default);
            var command = fixture.StornoCommand(final.Id);
            var storno = await fixture.Service.CreateStornoAsync(command, default);
            Assert(storno.TotalGrossAmount == -363 && storno.TotalVatAmount == -63 &&
                storno.Payments.Single(x => x.PaymentType == PaymentType.Advance).Amount == -242 &&
                storno.Payments.Single(x => x.PaymentType == PaymentType.BankAccount).Amount == -121,
                "Storno mora tačno obrnuti avans i bankovni ostatak, stavke i porez.");
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 0, "Priprema storna ne smije osloboditi avans.");
            Assert((await fixture.Service.CreateStornoAsync(command, default)).Id == storno.Id, "Storno mora biti idempotentan.");
            await ExpectAsync(() => fixture.Service.CreateStornoAsync(command with { IdempotencyKey = "duplicate" }, default), "STORNO_ALREADY_EXISTS");
            if (staleReduction is not null)
            {
                Assert((await fixture.Repository.GetByIdAsync(staleReduction.Id, default))!.Status == FiscalStatus.Cancelled, "Neposlato umanjenje mora biti otkazano uz storno.");
                staleReduction.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
                await ExpectAsync(() => fixture.Repository.UpdateAsync(staleReduction, default), "INVOICE_STATE_CONFLICT");
            }
            await ExpectAsync(() => fixture.ReduceAsync(advance, final, 242), "FINAL_STORNO_EXISTS");
            await fixture.Service.CancelUnsubmittedAsync(storno.Id, "test", "test", default);
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 0, "Otkazivanje neposlatog storna ne poništava konačni račun.");
            storno = await fixture.Service.CreateStornoAsync(fixture.StornoCommand(final.Id), default);
            var success = await fixture.SavePuSuccessAsync(storno.Id);
            var recovered = await fixture.Submission().SubmitAsync(storno.Id, "recover", default);
            Assert(recovered!.Status == FiscalStatus.Fiscalized && recovered.QrCodeData!.Contains("prc=363.00"), "Storno konačnog računa nije završen.");
            Assert((await fixture.Repository.GetByIdAsync(final.Id, default))!.Status == FiscalStatus.StornoCreated, "Konačni račun nije označen kao storniran.");
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 242, "Avans bez fiskalizovanog umanjenja mora biti ponovo dostupan.");
            Assert((await fixture.Service.GetAdvanceStornoWorkflowAsync(final.Id, default)).Stage == "Completed", "Tok bez umanjenja mora biti dovršen.");
            await fixture.Submission().RecoverAsync(storno.Id, success.ExchangeId, "again", "test", default);
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 242, "Ponovljeni oporavak ne smije dvaput vratiti saldo.");
            await ExpectAsync(() => fixture.Service.CreateAdvanceRestorationAsync(fixture.RestorationCommand(storno.Id), default), "ADVANCE_RESTORATION_NOT_REQUIRED");
            await fixture.FinalAsync(advance, 242, 242);
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 0, "Vraćeni avans mora se moći ponovo koristiti.");
        }

        using (var fixture = new Fixture(schemaPath))
        {
            var (advance, final) = await fixture.CreatePairAsync(484, 363, 242);
            await fixture.FinalAsync(advance, 121, 121); // Unrelated reservation must survive the storno.
            var reduction = await fixture.ReduceAsync(advance, final, 242);
            await AdvanceWorkflowChecks.MarkFiscalizedAsync(fixture.Repository, reduction.Id);
            var storno = await fixture.Service.CreateStornoAsync(fixture.StornoCommand(final.Id), default);
            await ExpectAsync(() => fixture.Service.CreateAdvanceRestorationAsync(fixture.RestorationCommand(storno.Id), default), "ADVANCE_RESTORATION_NOT_READY");
            await fixture.SavePuSuccessAsync(storno.Id);
            await fixture.Submission().SubmitAsync(storno.Id, "storno", default);
            var balance = (await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!;
            Assert(balance.RemainingAmount == 121 && balance.PendingRestorationAmount == 242, "Fiskalizovano umanjenje mora zadržati rezervaciju do obnove.");
            Assert((await fixture.Service.GetAdvanceStornoWorkflowAsync(final.Id, default)).Stage == "AwaitingAdvanceRestoration", "Nedovršena obnova mora biti vidljiva.");
            var restorationCommand = fixture.RestorationCommand(storno.Id);
            var restoration = await fixture.Service.CreateAdvanceRestorationAsync(restorationCommand, default);
            Assert(restoration.TotalGrossAmount == 242 && restoration.TotalVatAmount == 42 && restoration.TotalNetAmount == 200 &&
                restoration.OriginalInvoiceId == reduction.Id && restoration.AdvanceRestorationStornoInvoiceId == storno.Id,
                "Obnova mora biti tačna pozitivna inverzija umanjenja sa obje reference.");
            Assert((await fixture.Service.CreateAdvanceRestorationAsync(restorationCommand, default)).Id == restoration.Id, "Obnova mora biti idempotentna.");
            await ExpectAsync(() => fixture.Service.CreateAdvanceRestorationAsync(restorationCommand with { IdempotencyKey = "another" }, default), "ADVANCE_RESTORATION_ALREADY_EXISTS");
            await fixture.Service.CancelUnsubmittedAsync(restoration.Id, "test", "test", default);
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 121, "Otkazivanje obnove ne oslobađa avans.");
            restoration = await fixture.Service.CreateAdvanceRestorationAsync(fixture.RestorationCommand(storno.Id), default);
            var pending = (await fixture.Repository.GetByIdAsync(restoration.Id, default))!;
            pending.MarkFiscalizationPending(Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('B', 512));
            await fixture.Repository.UpdateAsync(pending, default);
            await ExpectAsync(() => fixture.Submission().SubmitAsync(restoration.Id, "unknown", default), "FISCALIZATION_OUTCOME_UNKNOWN");
            await ExpectAsync(() => fixture.Service.CancelUnsubmittedAsync(restoration.Id, "test", "test", default), "INVOICE_CANNOT_BE_CANCELLED");
            var evidence = await fixture.SavePuSuccessAsync(restoration.Id);
            var faulting = DispatchProxy.Create<IFiscalInvoiceRepository, AdvanceRepositoryFailureProxy>();
            var faultProxy = (AdvanceRepositoryFailureProxy)(object)faulting;
            faultProxy.Inner = fixture.Repository;
            faultProxy.FailNextCompletion = true;
            try
            {
                await fixture.Submission(faulting).SubmitAsync(restoration.Id, "save-failure", default);
                throw new InvalidOperationException("Očekivana simulirana greška čuvanja.");
            }
            catch (IOException) { }
            Assert((await fixture.Repository.GetByIdAsync(restoration.Id, default))!.Status == FiscalStatus.FiscalizationPending &&
                (await fixture.Repository.GetByIdAsync(reduction.Id, default))!.Status == FiscalStatus.Fiscalized &&
                (await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 121,
                "Greška čuvanja ne smije ostaviti djelimično obnovljen saldo.");
            var parallelRecovery = await Task.WhenAll(
                fixture.Submission().SubmitAsync(restoration.Id, "recover-restoration-one", default),
                fixture.Submission().SubmitAsync(restoration.Id, "recover-restoration-two", default));
            Assert(parallelRecovery.All(x => x!.Jikr == evidence.Jikr), "Paralelni oporavci moraju završiti istim JIKR-om.");
            var recovered = parallelRecovery[0];
            Assert(recovered!.Jikr == evidence.Jikr && recovered.Status == FiscalStatus.Fiscalized, "Obnova mora koristiti postojeći JIKR.");
            Assert((await fixture.Repository.GetByIdAsync(reduction.Id, default))!.Status == FiscalStatus.StornoCreated &&
                (await fixture.Repository.GetByIdAsync(advance.Id, default))!.Status == FiscalStatus.Fiscalized,
                "Poništeno umanjenje i izvorni avans imaju pogrešne statuse.");
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 363, "Obnova je promijenila tuđu rezervaciju.");
            Assert((await fixture.Service.GetAdvanceStornoWorkflowAsync(final.Id, default)).Stage == "Completed", "Obnovljeni workflow nije dovršen.");
            await fixture.Submission().RecoverAsync(restoration.Id, evidence.ExchangeId, "again", "test", default);
            Assert((await fixture.Service.GetAdvanceBalanceAsync(advance.Id, default))!.RemainingAmount == 363, "Ponavljanje je dvaput obnovilo avans.");
            Assert((await fixture.Store.FindSuccessfulInvoicesByIicAsync(recovered.Iic, default)).Count == 1, "Oporavak ne smije kreirati novu fiskalnu razmjenu.");
            await fixture.FinalAsync(advance, 300, 300);
        }

        using (var fixture = new Fixture(schemaPath))
        {
            var (advance, final) = await fixture.CreatePairAsync(242, 242, 242);
            var reduction = await fixture.ReduceAsync(advance, final, 242);
            var pending = (await fixture.Repository.GetByIdAsync(reduction.Id, default))!;
            pending.MarkFiscalizationPending(Guid.NewGuid().ToString("N"), new string('B', 512));
            await fixture.Repository.UpdateAsync(pending, default);
            await ExpectAsync(() => fixture.Service.CreateStornoAsync(fixture.StornoCommand(final.Id), default), "ADVANCE_SETTLEMENT_OUTCOME_UNRESOLVED");
            pending.MarkFiscalizationFailed();
            await fixture.Repository.UpdateAsync(pending, default);
            await ExpectAsync(() => fixture.Service.CreateStornoAsync(fixture.StornoCommand(final.Id), default), "ADVANCE_SETTLEMENT_OUTCOME_UNRESOLVED");
        }
        using (var fixture = new Fixture(schemaPath))
        {
            var (advance, final) = await fixture.CreateMixedPairAsync();
            var reduction = await fixture.ReduceMixedAsync(advance, final);
            await AdvanceWorkflowChecks.MarkFiscalizedAsync(fixture.Repository, reduction.Id);
            var storno = await fixture.Service.CreateStornoAsync(fixture.StornoCommand(final.Id), default);
            await fixture.SavePuSuccessAsync(storno.Id);
            await fixture.Submission().SubmitAsync(storno.Id, "mixed-storno", default);
            var command = fixture.RestorationCommand(storno.Id);
            await ExpectAsync(() => fixture.Service.CreateAdvanceRestorationAsync(command with { IssueDateTime = command.IssueDateTime.AddDays(-1) }, default), "ADVANCE_RESTORATION_INVALID");
            var restoration = await fixture.Service.CreateAdvanceRestorationAsync(command, default);
            Assert(restoration.TotalNetAmount == 200 && restoration.TotalVatAmount == 28 && restoration.Items.Select(x => x.VatRate).Order().SequenceEqual(new[] { 7m, 21m }),
                "Obnova mora sačuvati odvojene izvorne PDV stope i iznose.");
            await fixture.SavePuSuccessAsync(restoration.Id);
            await fixture.Submission().SubmitAsync(restoration.Id, "mixed-restoration", default);
            var nextFinal = await fixture.FinalAsync(advance, 228, 228);
            var nextReduction = await fixture.ReduceMixedAsync(advance, nextFinal);
            Assert(nextReduction.TotalVatAmount == -28, "Poništena stara raspodjela ne smije blokirati naredno zakonito korišćenje istih stavki avansa.");
        }
        Console.WriteLine("Konačni avansni storno: negativno plaćanje, obnova PDV-a/salda, otkazivanje, idempotency, nepoznat ishod i oporavak nakon greške čuvanja su provjereni.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _schemaPath;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "summa-final-storno-" + Guid.NewGuid().ToString("N"));
        private readonly Guid _company = Guid.NewGuid(), _unit = Guid.NewGuid(), _device = Guid.NewGuid(), _operator = Guid.NewGuid();
        public InMemoryFiscalInvoiceRepository Repository { get; } = new();
        public FiscalInvoiceApplicationService Service { get; }
        public FileFiscalExchangeStoreV5 Store { get; }
        private readonly PuFiscalizationOptionsV5 _configuration = new()
        {
            Environment = "Test", Endpoint = "https://efitest.tax.gov.me/fs-v1", IssuerTin = "12345678",
            BusinessUnitCode = "aa111bb222", TcrCode = "cc333dd444", SoftwareCode = "gg777hh888", OperatorCode = "ee555ff666",
            SellerName = "Test", SellerCountry = "MNE", IsIssuerInVat = true
        };
        public Fixture(string schemaPath)
        {
            _schemaPath = schemaPath;
            Service = new(Repository, new FiscalInvoiceValidator(), new InMemoryAuditService(), new InMemoryInvoiceNumberSequence());
            Store = new(new() { RootPath = _root });
        }
        private CreateFiscalInvoiceCommand Command(InvoiceType type, decimal gross, IReadOnlyCollection<CreateFiscalPayment> payments) => new(
            _company, _unit, _device, _operator, type, "", new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero), "EUR",
            new(BuyerIdentificationType.Tin, "87654321", "Kupac", null, null, "MNE", null), null, null, null,
            [new("Usluga", 1, gross, 21, null, "kom", 0)], payments, Guid.NewGuid().ToString(), "test", "test");
        public async Task<(FiscalInvoiceResult Advance, FiscalInvoiceResult Final)> CreatePairAsync(decimal advanceAmount, decimal gross, decimal applied)
        {
            var advance = await Service.CreateAsync(Command(InvoiceType.Advance, advanceAmount, [new(PaymentType.BankAccount, advanceAmount, null)]), default);
            await AdvanceWorkflowChecks.MarkFiscalizedAsync(Repository, advance.Id);
            return (advance, await FinalAsync(advance, gross, applied));
        }
        public async Task<(FiscalInvoiceResult Advance, FiscalInvoiceResult Final)> CreateMixedPairAsync()
        {
            var advance = await Service.CreateAsync(Command(InvoiceType.Advance, 228, [new(PaymentType.BankAccount, 228, null)]) with
            { Items = [new("Stopa 21", 1, 121, 21, null, "kom", 0), new("Stopa 7", 1, 107, 7, null, "kom", 0)] }, default);
            await AdvanceWorkflowChecks.MarkFiscalizedAsync(Repository, advance.Id);
            return (advance, await FinalAsync(advance, 363, 228));
        }
        public Task<FiscalInvoiceResult> ReduceMixedAsync(FiscalInvoiceResult advance, FiscalInvoiceResult final) =>
            Service.CreateAdvanceSettlementAsync(new(advance.Id, final.Id, new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero),
                advance.Items.Select(x => new AdvanceSettlementAllocation(x.Id, x.GrossAmount)).ToArray(), Guid.NewGuid().ToString(), "test", "test"), default);
        public async Task<FiscalInvoiceResult> FinalAsync(FiscalInvoiceResult advance, decimal gross, decimal applied)
        {
            var payments = new List<CreateFiscalPayment> { new(PaymentType.Advance, applied, null, advance.Id) };
            if (gross > applied) payments.Add(new(PaymentType.BankAccount, gross - applied, null));
            var final = await Service.CreateAsync(Command(InvoiceType.Normal, gross, payments), default);
            await AdvanceWorkflowChecks.MarkFiscalizedAsync(Repository, final.Id);
            return final;
        }
        public Task<FiscalInvoiceResult> ReduceAsync(FiscalInvoiceResult advance, FiscalInvoiceResult final, decimal amount) =>
            Service.CreateAdvanceSettlementAsync(new(advance.Id, final.Id, new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero),
                [new(advance.Items.Single().Id, amount)], Guid.NewGuid().ToString(), "test", "test"), default);
        public CreateStornoCommand StornoCommand(Guid finalId) => new(finalId, "", new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero),
            "Vraćanje avansa za budući promet", Guid.NewGuid().ToString(), "test", "test");
        public CreateAdvanceRestorationCommand RestorationCommand(Guid stornoId) => new(stornoId,
            new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.Zero), Guid.NewGuid().ToString(), "test", "test");

        public FiscalInvoiceSubmissionServiceV5 Submission(IFiscalInvoiceRepository? repository = null)
        {
            var onboarding = DispatchProxy.Create<IFiscalOnboardingService, AdvanceOnboardingProxy>();
            ((AdvanceOnboardingProxy)(object)onboarding).InvokeMethod = method => method.Name switch
            {
                "GetCompanyAsync" => Task.FromResult(new CompanySummary(_company, "12345678", "Test", null, null, null, "MNE", true, true, "Test", _configuration.Endpoint, _configuration.SoftwareCode, "hh111ii222", "BankOnly")),
                "GetBusinessUnitAsync" => Task.FromResult(new BusinessUnitSummary(_unit, _company, "Test", _configuration.BusinessUnitCode, "Test", null, null, true)),
                "GetDeviceAsync" => Task.FromResult(new FiscalDeviceSummary(_device, _company, _unit, _configuration.TcrCode, "Test", "Registered", null, true)),
                "GetOperatorAsync" => Task.FromResult(new FiscalOperatorSummary(_operator, _company, "Test", _configuration.OperatorCode, null, null, true)),
                _ => throw new InvalidOperationException("Oporavak ne smije pokrenuti transport: " + method.Name)
            };
            return new(repository ?? Repository, onboarding, new IicGeneratorV5(), new RegisterInvoiceXmlBuilderV5(),
                new FiscalXmlSignerV5(), new FiscalQrCodeGeneratorV5(), new SoapEnvelopeV5(), new RegisterInvoiceResponseParserV5(), Store, new InMemoryAuditService());
        }

        public async Task<(Guid ExchangeId, string Jikr)> SavePuSuccessAsync(Guid id)
        {
            var invoice = (await Repository.GetByIdAsync(id, default))!;
            if (invoice.Status != FiscalStatus.FiscalizationPending)
            {
                invoice.MarkFiscalizationPending(Guid.NewGuid().ToString("N").ToUpperInvariant(), new string('B', 512));
                await Repository.UpdateAsync(invoice, default);
            }
            var ordinal = int.Parse(invoice.InvoiceNumber);
            var request = FiscalInvoiceRequestFactoryV5.Build(invoice, _configuration, invoice.IssueDateTime,
                $"aa111bb222/{ordinal}/2026/cc333dd444", ordinal, new(invoice.Iic!, invoice.IicSignature!, "test", "test", "test"));
            var xml = new RegisterInvoiceXmlBuilderV5().BuildUnsigned(request);
            using var rsa = RSA.Create(2048);
            using var certificate = new CertificateRequest("CN=Local storno test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var signed = new FiscalXmlSignerV5().SignRequest(xml, certificate);
            var validation = new FiscalXmlSchemaValidatorV5(_schemaPath).Validate(signed.SignedDocument);
            Assert(signed.SignatureVerified && validation.IsValid, string.Join("; ", validation.Errors.Select(x => x.Message)));
            if (invoice.IsAdvanceRestoration)
            {
                Assert(request.Invoice.CorrectiveInvoice!.OriginalIic == invoice.OriginalIic &&
                    request.Invoice.InvoiceReferences!.Single().Iic == invoice.AdvanceRestorationStornoIic &&
                    request.Invoice.InvoiceReferences!.Single().Amount == -invoice.TotalGrossAmount,
                    "Pozitivna obnova mora referencirati umanjenje i negativni storno konačnog računa.");
            }
            var exchange = await Store.SaveRequestAsync(signed.SignedDocument.ToString(), request.Header.Uuid,
                "test-storno", new Uri(_configuration.Endpoint), PuFiscalContractV5.RegisterInvoiceSoapAction, default);
            var jikr = Guid.NewGuid().ToString();
            await Store.SaveResponseAsync(exchange,
                $"""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><RegisterInvoiceResponse xmlns="https://efi.tax.gov.me/fs/schema" Id="Response" Version="1"><Header UUID="{Guid.NewGuid()}" RequestUUID="{request.Header.Uuid}" SendDateTime="2026-10-22T14:00:00+02:00"/><FIC>{jikr}</FIC></RegisterInvoiceResponse></s:Body></s:Envelope>""", 200, default);
            return (exchange, jikr);
        }
        public void Dispose()
        {
            if (Path.GetFileName(_root).StartsWith("summa-final-storno-", StringComparison.Ordinal) && Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
    private static async Task ExpectAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (FiscalInvoiceOperationException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException("Očekivana greška: " + code);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

public class AdvanceRepositoryFailureProxy : DispatchProxy
{
    public IFiscalInvoiceRepository Inner { get; set; } = null!;
    public bool FailNextCompletion { get; set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == nameof(IFiscalInvoiceRepository.CompleteCorrectiveAsync) && FailNextCompletion)
        {
            FailNextCompletion = false;
            return Task.FromException(new IOException("Simulirana greška čuvanja poslije sačuvanog uspješnog PU odgovora."));
        }
        try { return targetMethod.Invoke(Inner, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
}
