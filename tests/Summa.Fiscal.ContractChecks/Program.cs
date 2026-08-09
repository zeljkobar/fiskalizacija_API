using Summa.Fiscal.Infrastructure.Certificates;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Application.Certificates;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Domain.Invoices;
using Summa.Fiscal.Infrastructure.Persistence;

var schemaPath = FindOfficialSchema();
var builder = new RegisterInvoiceXmlBuilderV5();
var validator = new FiscalXmlSchemaValidatorV5(schemaPath);
VerifySummaTestConfiguration();
VerifySoapResponseParsing();
VerifyRegisterTcrContract();
VerifyQrCodeGeneration();
await VerifyStoredSuccessfulExchangeRecoveryAsync();
VerifyFullStornoDomain();
VerifyCorrectiveInvoiceContract();
VerifyIssuerVatFlagMapping();
await VerifyConcurrentInvoiceSequenceAsync();
await VerifyEncryptedCertificateVaultAsync();
await VerifyCertificateExpiryAlertsAsync();
var structuralDocument = builder.BuildUnsigned(
    CreateMinimalValidRequest(new string('A', 32), new string('B', 512)));
var result = validator.Validate(structuralDocument);

if (!result.IsValid)
{
    WriteErrors(result);
    return 1;
}

Console.WriteLine("RegisterInvoiceRequest je validan prema zvaničnom PU XSD-u.");

var pfxPath = Environment.GetEnvironmentVariable("SUMMA_FISCAL_DEV_PFX_PATH");
var pfxPassword = Environment.GetEnvironmentVariable("SUMMA_FISCAL_DEV_PFX_PASSWORD");
if (string.IsNullOrWhiteSpace(pfxPath) || pfxPassword is null)
{
    Console.WriteLine("Kriptografska provjera je preskočena jer razvojni PFX nije konfigurisan.");
    return 0;
}

var certificateLoader = new PfxCertificateLoader();
using var loadedCertificate = certificateLoader.Load(
    pfxPath,
    pfxPassword,
    new(RequireCurrentlyValid: false, ExpectedIssuerTin: "02825767"));
using var tlsLoadedCertificate = certificateLoader.Load(
    pfxPath,
    pfxPassword,
    new(
        RequireCurrentlyValid: true,
        ExpectedIssuerTin: "02825767",
        KeyStorageFlags:
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet |
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable));
using var tlsHandler = new PuClientCertificateHandlerV5(tlsLoadedCertificate);
var cashDepositDryRun = new CashDepositDryRunServiceV5(
    new RegisterCashDepositXmlBuilderV5(),
    new FiscalXmlSignerV5());
var cashDepositResult = cashDepositDryRun.CreateInitial(
    10.00m,
    new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(2)),
    new()
    {
        Environment = "Test",
        Endpoint = "https://efitest.tax.gov.me/fs-v1",
        IssuerTin = "02825767",
        BusinessUnitCode = "oo940dt107",
        TcrCode = "wx860oc926",
        SoftwareCode = "zm955pb829",
        OperatorCode = "xg960dc979",
        SellerName = "SUMMA SUMMARUM"
    },
    loadedCertificate.Certificate,
    schemaPath);
if (!cashDepositResult.SignatureVerified || !cashDepositResult.XsdValid)
{
    Console.Error.WriteLine("Početni depozit nije prošao lokalni dry-run.");
    return 1;
}

var iicInput = new IicInputV5(
    loadedCertificate.IssuerTin!,
    new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(2)),
    1,
    "aa111bb222",
    "cc333dd444",
    "gg777hh888",
    121.00m);
var iicGenerator = new IicGeneratorV5();
var firstIic = iicGenerator.Generate(iicInput, loadedCertificate.Certificate);
var secondIic = iicGenerator.Generate(iicInput, loadedCertificate.Certificate);

if (firstIic.Iic != secondIic.Iic ||
    firstIic.IicSignature != secondIic.IicSignature)
{
    Console.Error.WriteLine("IKOF algoritam nije determinističan.");
    return 1;
}

var request = CreateMinimalValidRequest(firstIic.Iic, firstIic.IicSignature);
var unsignedDocument = builder.BuildUnsigned(request);
var signer = new FiscalXmlSignerV5();
var signatureResult = signer.SignRequest(unsignedDocument, loadedCertificate.Certificate);

if (!signatureResult.SignatureVerified)
{
    Console.Error.WriteLine("XML potpis nije prošao lokalnu kriptografsku provjeru.");
    return 1;
}

var signedSchemaResult = validator.Validate(signatureResult.SignedDocument);
if (!signedSchemaResult.IsValid)
{
    WriteErrors(signedSchemaResult);
    return 1;
}

var dryRunService = new FiscalDryRunServiceV5(
    iicGenerator,
    builder,
    signer,
    new SoapEnvelopeV5());
var dryRun = dryRunService.Create(
    new(
        1,
        new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(2)),
        "Testna usluga",
        1.00m,
        21.00m),
    new()
    {
        Environment = "Test",
        Endpoint = "https://efitest.tax.gov.me/fs-v1",
        IssuerTin = "02825767",
        BusinessUnitCode = "oo940dt107",
        TcrCode = "wx860oc926",
        SoftwareCode = "zm955pb829",
        OperatorCode = "xg960dc979",
        SellerName = "SUMMA SUMMARUM",
        SellerAddress = "MAKEDONSKA",
        SellerTown = "Bar",
        SellerCountry = "MNE"
    },
    loadedCertificate.Certificate,
    schemaPath);
if (!dryRun.SignatureVerified ||
    !dryRun.XsdValid ||
    !dryRun.SoapEnvelopeXml.Contains("RegisterInvoiceRequest", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Dry-run fiskalnog zahtjeva nije uspio.");
    return 1;
}

await VerifyTransportPersistenceAsync(dryRun);

var changedDocument = new System.Xml.Linq.XDocument(signatureResult.SignedDocument);
changedDocument.Root?
    .Element(PuFiscalContractV5.Schema + "Invoice")?
    .SetAttributeValue("TotPrice", "122.00");
if (signer.Verify(changedDocument, loadedCertificate.Certificate))
{
    Console.Error.WriteLine("Izmijenjeni XML je pogrešno prihvaćen kao validno potpisan.");
    return 1;
}

Console.WriteLine("Razvojni PFX je uspješno učitan u memoriju.");
Console.WriteLine("IKOF/IIC je deterministički generisan po PU algoritmu.");
Console.WriteLine("XMLDSIG potpis je kreiran i kriptografski verifikovan.");
Console.WriteLine("Naknadna izmjena potpisanog XML-a je uspješno otkrivena.");
Console.WriteLine("Potpisani XML je strukturno validan prema zvaničnom PU XSD-u.");
return 0;

static RegisterInvoiceRequestV5 CreateMinimalValidRequest(
    string iic,
    string iicSignature)
{
    const string businessUnitCode = "aa111bb222";
    const string tcrCode = "cc333dd444";

    return new(
        new(
            Guid.Parse("a597feef-9d61-4d80-b5d3-5ecb61de1682"),
            new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(2))),
        new(
            PuInvoiceTypeV5.Cash,
            new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(2)),
            $"{businessUnitCode}/1/2026/{tcrCode}",
            1,
            tcrCode,
            true,
            100.00m,
            21.00m,
            121.00m,
            "ee555ff666",
            businessUnitCode,
            "gg777hh888",
            iic,
            iicSignature,
            new(
                PuIdTypeV5.Tin,
                "12345678",
                "SUMMA SUMMARUM",
                "Adresa 1",
                "Podgorica",
                "MNE"),
            [new(PuPaymentMethodV5.Banknote, 121.00m)],
            [
                new(
                    "Usluga knjigovodstva",
                    "kom",
                    1m,
                    100.00m,
                    121.00m,
                    100.00m,
                    121.00m,
                    VatRate: 21.00m,
                    VatAmount: 21.00m)
            ],
            [new(1, 100.00m, 21.00m, 21.00m)],
            DocumentType: PuInvoiceDocumentTypeV5.Invoice,
            IsSimplifiedInvoice: false,
            IsReverseCharge: false));
}

static void VerifyFullStornoDomain()
{
    var issuedAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.FromHours(2));
    var original = new FiscalInvoice(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        InvoiceType.Normal,
        "fx318ob312/1/2026/enu-test",
        issuedAt,
        "EUR",
        "original-test",
        new FiscalBuyer(
            BuyerIdentificationType.Tin,
            "02825767",
            "SUMMA SUMMARUM D.O.O",
            "MAKEDONSKA B3",
            "Bar",
            "MNE"),
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 8, 10));
    original.AddItem(new FiscalInvoiceItem("Testna usluga", 1m, 121m, 21m, unitOfMeasure: "kom"));
    original.AddPayment(new FiscalPayment(PaymentType.BankAccount, 121m, "TEST-REF"));
    original.MarkValidated();
    original.MarkReadyForFiscalization();
    original.MarkFiscalizationPending(new string('A', 32), new string('B', 512));
    original.MarkFiscalized(
        "test-jikr",
        "fx318ob312/1/2026/enu-test",
        fiscalizedAt: issuedAt);

    if (original.FiscalizedAt?.Offset != TimeSpan.Zero)
    {
        throw new InvalidOperationException(
            "Vrijeme fiskalizacije iz PU odgovora nije normalizovano na UTC.");
    }

    var storno = FiscalInvoice.CreateFullStorno(
        original,
        "fx318ob312/2/2026/enu-test",
        issuedAt.AddMinutes(5),
        "storno-test",
        "Poništenje testnog računa");
    var validation = new FiscalInvoiceValidator().Validate(storno);

    if (!validation.IsValid ||
        storno.OriginalInvoiceId != original.Id ||
        storno.OriginalIic != original.Iic ||
        storno.TotalGrossAmount != -121m ||
        storno.Items.Single().Quantity != -1m ||
        storno.Payments.Single().Amount != -121m)
    {
        throw new InvalidOperationException(
            "Potpuni storno nije kreirao validan negativni korektivni račun.");
    }

    Console.WriteLine("Domenski workflow potpunog storna je validan.");
}

static void VerifyCorrectiveInvoiceContract()
{
    var request = CreateMinimalValidRequest(new string('A', 32), new string('B', 512));
    var corrected = request with
    {
        Invoice = request.Invoice with
        {
            TotalPriceWithoutVat = -100m,
            TotalVatAmount = -21m,
            TotalPrice = -121m,
            Buyer = new(
                PuIdTypeV5.Tin,
                "12345678",
                "PRIMJER KUPAC D.O.O.",
                "Ulica 1",
                "Podgorica",
                "MNE"),
            PaymentDeadline = new DateOnly(2026, 8, 10),
            SupplyPeriod = new(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CorrectiveInvoice = new(
                new string('C', 32),
                new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(2)),
                PuCorrectiveInvoiceTypeV5.Corrective),
            Payments = [new(PuPaymentMethodV5.Account, -121m, BankAccount: "TEST-IBAN")],
            Items =
            [
                new(
                    "Storno testne usluge",
                    "kom",
                    -1m,
                    100m,
                    121m,
                    -100m,
                    -121m,
                    VatRate: 21m,
                    VatAmount: -21m)
            ],
            SameTaxes = [new(1, -100m, 21m, -21m)]
        }
    };
    var document = new RegisterInvoiceXmlBuilderV5().BuildUnsigned(corrected);
    var validation = new FiscalXmlSchemaValidatorV5(FindOfficialSchema()).Validate(document);
    var correctiveElement = document.Root?
        .Element(PuFiscalContractV5.Schema + "Invoice")?
        .Element(PuFiscalContractV5.Schema + "CorrectiveInv");

    if (!validation.IsValid ||
        correctiveElement?.Attribute("IICRef")?.Value != new string('C', 32) ||
        correctiveElement?.Attribute("Type")?.Value != "CORRECTIVE")
    {
        WriteErrors(validation);
        throw new InvalidOperationException(
            "Korektivni RegisterInvoiceRequest nije validan prema zvaničnom PU XSD-u.");
    }

    Console.WriteLine("Korektivni XML sa kupcem, rokovima i negativnim iznosima je XSD validan.");
}

static async Task VerifyConcurrentInvoiceSequenceAsync()
{
    var sequence = new InMemoryInvoiceNumberSequence();
    var deviceId = Guid.NewGuid();
    var reservations = await Task.WhenAll(
        Enumerable.Range(0, 100).Select(_ =>
            sequence.ReserveNextAsync(deviceId, 2026, CancellationToken.None)));

    if (reservations.Distinct().Count() != 100 ||
        reservations.Min() != 1 ||
        reservations.Max() != 100)
    {
        throw new InvalidOperationException("Paralelna numeracija je proizvela duplikat ili preskočen broj.");
    }

    Console.WriteLine("Paralelna numeracija je rezervisala 100 jedinstvenih brojeva.");
}

static void VerifyIssuerVatFlagMapping()
{
    var request = CreateMinimalValidRequest(new string('A', 32), new string('B', 512));
    var nonVatRequest = request with
    {
        Invoice = request.Invoice with
        {
            IsIssuerInVat = false,
            TotalPriceWithoutVat = 121m,
            TotalVatAmount = null,
            Items =
            [
                new(
                    "Usluga bez PDV-a",
                    "kom",
                    1m,
                    121m,
                    121m,
                    121m,
                    121m)
            ],
            SameTaxes = null
        }
    };
    var document = new RegisterInvoiceXmlBuilderV5().BuildUnsigned(nonVatRequest);
    var invoice = document.Root?.Element(PuFiscalContractV5.Schema + "Invoice");
    var validation = new FiscalXmlSchemaValidatorV5(FindOfficialSchema()).Validate(document);
    if (!validation.IsValid || invoice?.Attribute("IsIssuerInVAT")?.Value != "false")
    {
        WriteErrors(validation);
        throw new InvalidOperationException("PDV status izdavaoca nije ispravno mapiran u PU XML.");
    }

    Console.WriteLine("PDV status izdavaoca se mapira iz konfiguracije u PU XML.");
}

static void VerifyQrCodeGeneration()
{
    var generator = new FiscalQrCodeGeneratorV5();
    var actual = generator.GenerateVerificationUrl(new(
        "Test",
        "EA26D5BE7F45827026108F825A8A512B",
        "91806031",
        new DateTimeOffset(2019, 9, 26, 13, 50, 13, TimeSpan.FromHours(1)),
        6,
        "bg517kw842",
        "xb131ap287",
        "gz434bv927",
        199.00m));
    const string expected =
        "https://efitest.tax.gov.me/ic/#/verify" +
        "?iic=EA26D5BE7F45827026108F825A8A512B" +
        "&tin=91806031&crtd=2019-09-26T13:50:13+01:00" +
        "&ord=6&bu=bg517kw842&cr=xb131ap287&sw=gz434bv927&prc=199.00";

    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"QR URL nije jednak zvaničnom PU primjeru. Dobijeno: {actual}");
    }

    var corrective = generator.GenerateVerificationUrl(new(
        "Test",
        "FF34905809F8B5B8D51D87AC98217BF1",
        "02825767",
        new DateTimeOffset(2026, 8, 9, 21, 30, 29, TimeSpan.FromHours(2)),
        18,
        "oo940dt107",
        "wx860oc926",
        "zm955pb829",
        -242.00m));
    if (!corrective.EndsWith("&prc=242.00", StringComparison.Ordinal) ||
        corrective.Contains("prc=-242.00", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"QR URL korektivnog računa nema apsolutni prc=242.00. Dobijeno: {corrective}");
    }

    Console.WriteLine("QR URL je identičan zvaničnom PU v5 primjeru.");
    Console.WriteLine("QR URL storna od -242,00 koristi prc=242.00.");
}

static async Task VerifyStoredSuccessfulExchangeRecoveryAsync()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        $"summa-fiscal-recovery-{Guid.NewGuid():N}");
    var store = new FileFiscalExchangeStoreV5(new() { RootPath = root });
    const string iic = "FF34905809F8B5B8D51D87AC98217BF1";
    const string invoiceNumber = "oo940dt107/18/2026/wx860oc926";
    var endpoint = new Uri("https://efitest.tax.gov.me/fs-v1");

    try
    {
        var firstRequestUuid = Guid.NewGuid();
        var firstExchangeId = await SaveSuccessfulExchangeAsync(
            store,
            firstRequestUuid,
            Guid.NewGuid(),
            iic,
            invoiceNumber,
            endpoint,
            "recovery-contract-1");
        var first = await store.ReadSuccessfulInvoiceAsync(
            firstExchangeId,
            CancellationToken.None);
        if (first is null ||
            first.RequestUuid != firstRequestUuid ||
            first.Iic != iic ||
            first.InvoiceNumber != invoiceNumber ||
            first.TotalPrice != -242.00m)
        {
            throw new InvalidOperationException(
                "Sačuvani uspješan fiscal exchange nije bezbjedno učitan za oporavak.");
        }

        await SaveSuccessfulExchangeAsync(
            store,
            Guid.NewGuid(),
            Guid.NewGuid(),
            iic,
            invoiceNumber,
            endpoint,
            "recovery-contract-2");
        var matches = await store.FindSuccessfulInvoicesByIicAsync(
            iic,
            CancellationToken.None);
        if (matches.Count != 2 || matches.All(match => match.ExchangeId != firstExchangeId))
        {
            throw new InvalidOperationException(
                "Više uspješnih PU odgovora za isti IKOF nije ispravno otkriveno.");
        }

        Console.WriteLine(
            "Sačuvani uspješan exchange se validira, a više FIC odgovora za isti IKOF se otkriva.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task<Guid> SaveSuccessfulExchangeAsync(
        FileFiscalExchangeStoreV5 store,
        Guid requestUuid,
        Guid fic,
        string iic,
        string invoiceNumber,
        Uri endpoint,
        string correlationId)
    {
        var request =
            $"""
            <RegisterInvoiceRequest xmlns="https://efi.tax.gov.me/fs/schema">
              <Header UUID="{requestUuid}" />
              <Invoice IIC="{iic}" InvNum="{invoiceNumber}" TotPrice="-242.00" />
            </RegisterInvoiceRequest>
            """;
        var response =
            $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                              xmlns:me="https://efi.tax.gov.me/fs/schema">
              <soapenv:Body>
                <me:RegisterInvoiceResponse Id="Response" Version="1">
                  <me:Header UUID="{Guid.NewGuid()}" RequestUUID="{requestUuid}"
                             SendDateTime="2026-08-09T21:30:29+02:00" />
                  <me:FIC>{fic}</me:FIC>
                </me:RegisterInvoiceResponse>
              </soapenv:Body>
            </soapenv:Envelope>
            """;
        var exchangeId = await store.SaveRequestAsync(
            request,
            requestUuid,
            correlationId,
            endpoint,
            PuFiscalContractV5.RegisterInvoiceSoapAction,
            CancellationToken.None);
        await store.SaveResponseAsync(
            exchangeId,
            response,
            200,
            CancellationToken.None);
        return exchangeId;
    }
}

static void VerifySummaTestConfiguration()
{
    var configuredProfile = new PuFiscalizationOptionsV5
    {
        Environment = "Test",
        Endpoint = "https://efitest.tax.gov.me/fs-v1",
        IssuerTin = "02825767",
        BusinessUnitCode = "oo940dt107",
        TcrCode = "wx860oc926",
        SoftwareCode = "zm955pb829",
        OperatorCode = "xg960dc979",
        SellerName = "SUMMA SUMMARUM"
    };

    configuredProfile.EnsureReadyForInvoice();

    var incompleteProfile = new PuFiscalizationOptionsV5
    {
        Environment = "Test",
        Endpoint = "https://efitest.tax.gov.me/fs-v1",
        IssuerTin = "02825767",
        TcrCode = "wx860oc926",
        SoftwareCode = "zm955pb829",
        OperatorCode = "xg960dc979",
        SellerName = "SUMMA SUMMARUM"
    };
    var readiness = incompleteProfile.GetReadiness();

    if (readiness.IsReady ||
        !readiness.MissingFields.Contains(nameof(PuFiscalizationOptionsV5.BusinessUnitCode)))
    {
        throw new InvalidOperationException(
            "Provjera obaveznog koda poslovne jedinice nije ispravna.");
    }
}

static void VerifySoapResponseParsing()
{
    var parser = new RegisterInvoiceResponseParserV5();
    var responseUuid = Guid.Parse("d5c118bd-f2f6-4c70-9f80-99c922c2e479");
    var requestUuid = Guid.Parse("a597feef-9d61-4d80-b5d3-5ecb61de1682");
    var success = parser.Parse(
        $"""
        <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                          xmlns:me="https://efi.tax.gov.me/fs/schema">
          <soapenv:Body>
            <me:RegisterInvoiceResponse Id="Response" Version="1">
              <me:Header UUID="{responseUuid}" RequestUUID="{requestUuid}"
                         SendDateTime="2026-07-30T20:00:01+02:00" />
              <me:FIC>7f179aac-5c43-4df3-8dca-248f4dbdbe2c</me:FIC>
              <Signature xmlns="http://www.w3.org/2000/09/xmldsig#" />
            </me:RegisterInvoiceResponse>
          </soapenv:Body>
        </soapenv:Envelope>
        """);
    if (!success.IsSuccess ||
        success.RequestUuid != requestUuid ||
        string.IsNullOrWhiteSpace(success.Fic))
    {
        throw new InvalidOperationException("Parser uspješnog PU odgovora nije ispravan.");
    }

    var fault = parser.Parse(
        """
        <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/">
          <soapenv:Body>
            <soapenv:Fault>
              <faultcode>soapenv:Client</faultcode>
              <faultstring>Neispravan zahtjev</faultstring>
              <detail><code>VALIDATION_ERROR</code></detail>
            </soapenv:Fault>
          </soapenv:Body>
        </soapenv:Envelope>
        """);
    if (fault.IsSuccess ||
        fault.Fault?.Message != "Neispravan zahtjev")
    {
        throw new InvalidOperationException("Parser SOAP Fault odgovora nije ispravan.");
    }
}

static async Task VerifyTransportPersistenceAsync(FiscalDryRunResultV5 dryRun)
{
    var testRoot = Path.Combine(
        Path.GetTempPath(),
        $"summa-fiscal-contract-{Guid.NewGuid():N}");

    try
    {
        var responseUuid = Guid.NewGuid();
        var responseXml =
            $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                              xmlns:me="https://efi.tax.gov.me/fs/schema">
              <soapenv:Body>
                <me:RegisterInvoiceResponse Id="Response" Version="1">
                  <me:Header UUID="{responseUuid}" RequestUUID="{dryRun.RequestUuid}"
                             SendDateTime="2026-07-30T20:00:01+02:00" />
                  <me:FIC>7f179aac-5c43-4df3-8dca-248f4dbdbe2c</me:FIC>
                  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#" />
                </me:RegisterInvoiceResponse>
              </soapenv:Body>
            </soapenv:Envelope>
            """;
        using var httpClient = new HttpClient(new FakePuHttpMessageHandler(responseXml));
        var exchangeStore = new FileFiscalExchangeStoreV5(
            new() { RootPath = testRoot });
        var client = new PuFiscalSoapClientV5(
            httpClient,
            new SoapEnvelopeV5(),
            new RegisterInvoiceResponseParserV5(),
            exchangeStore);

        var result = await client.RegisterInvoiceAsync(
            new Uri("https://efitest.tax.gov.me/fs-v1"),
            System.Xml.Linq.XDocument.Parse(dryRun.SignedRequestXml),
            "contract-check-correlation",
            CancellationToken.None);
        var exchangeDirectory = Path.Combine(
            testRoot,
            result.ExchangeId.ToString("N"));

        var expectedFiles = new[]
        {
            "request.xml",
            "request.metadata.json",
            "response.xml",
            "response.metadata.json"
        };
        if (!result.Response.IsSuccess ||
            expectedFiles.Any(file => !File.Exists(Path.Combine(exchangeDirectory, file))))
        {
            throw new InvalidOperationException(
                "SOAP transport nije trajno sačuvao kompletnu fiskalnu razmjenu.");
        }


        var storedSuccess = await exchangeStore.ReadSuccessfulInvoiceAsync(
            result.ExchangeId,
            CancellationToken.None);
        var successesByIic = await exchangeStore.FindSuccessfulInvoicesByIicAsync(
            dryRun.Iic,
            CancellationToken.None);
        if (storedSuccess is null ||
            storedSuccess.RequestUuid != dryRun.RequestUuid ||
            storedSuccess.Iic != dryRun.Iic ||
            successesByIic.Count != 1 ||
            successesByIic.Single().ExchangeId != result.ExchangeId)
        {
            throw new InvalidOperationException(
                "Uspješan sačuvani fiscal exchange nije moguće bezbjedno pronaći za oporavak.");
        }

        using var failingHttpClient = new HttpClient(new FailingPuHttpMessageHandler());
        var failingClient = new PuFiscalSoapClientV5(
            failingHttpClient,
            new SoapEnvelopeV5(),
            new RegisterInvoiceResponseParserV5(),
            exchangeStore);
        try
        {
            await failingClient.RegisterInvoiceAsync(
                new Uri("https://efitest.tax.gov.me/fs-v1"),
                System.Xml.Linq.XDocument.Parse(dryRun.SignedRequestXml),
                "contract-check-failure",
                CancellationToken.None);
            throw new InvalidOperationException(
                "Simulirana mrežna greška nije propagirana.");
        }
        catch (HttpRequestException)
        {
            var failureStored = Directory
                .EnumerateFiles(
                    testRoot,
                    "failure.metadata.json",
                    SearchOption.AllDirectories)
                .Any();
            if (!failureStored)
            {
                throw new InvalidOperationException(
                    "Mrežna greška nije trajno evidentirana.");
            }
        }
    }
    finally
    {
        var fullTestRoot = Path.GetFullPath(testRoot);
        var fullTempRoot = Path.GetFullPath(Path.GetTempPath());
        if (Directory.Exists(fullTestRoot) &&
            fullTestRoot.StartsWith(fullTempRoot, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(fullTestRoot, recursive: true);
        }
    }
}

static void VerifyRegisterTcrContract()
{
    var request = new RegisterTcrXmlBuilderV5().BuildUnsigned(new(
        Guid.Parse("11111111-2222-3333-8444-555555555555"),
        new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.FromHours(2)),
        "02825767", "fx318ob312", "SUMMA-API-BANK-01", "lq099vq111", "qf401hk617",
        new DateOnly(2026, 8, 2)));
    var validation = new FiscalXmlSchemaValidatorV5(FindOfficialSchema()).Validate(request);
    if (!validation.IsValid)
        throw new InvalidOperationException("RegisterTCR request nije validan: " + string.Join("; ", validation.Errors.Select(x => x.Message)));

    const string response = """
        <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/">
          <soapenv:Body>
            <RegisterTCRResponse xmlns="https://efi.tax.gov.me/fs/schema" Id="Response" Version="1">
              <Header UUID="aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee" RequestUUID="11111111-2222-3333-8444-555555555555" SendDateTime="2026-08-02T12:00:01+02:00" />
              <TCRCode>aa111bb222</TCRCode>
            </RegisterTCRResponse>
          </soapenv:Body>
        </soapenv:Envelope>
        """;
    var parsed = new RegisterTcrResponseParserV5().Parse(response);
    if (!parsed.IsSuccess || parsed.TcrCode != "aa111bb222")
        throw new InvalidOperationException("RegisterTCR response parser nije vratio TCRCode.");
}

static void WriteErrors(FiscalXmlValidationResultV5 validationResult)
{
    foreach (var error in validationResult.Errors)
    {
        Console.Error.WriteLine(
            $"{error.Severity}: {error.Message} ({error.LineNumber},{error.LinePosition})");
    }
}

static async Task VerifyEncryptedCertificateVaultAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "summa-cert-vault-" + Guid.NewGuid().ToString("N"));
    var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    var vault = new EncryptedFileCertificateVault(Options.Create(new FiscalCertificateVaultOptions
    {
        RootPath = root,
        MasterKeyBase64 = key
    }));
    var companyId = Guid.NewGuid();
    var pfxBytes = "test-pfx-material"u8.ToArray();
    const string password = "test-password-that-must-not-be-plain";
    try
    {
        var storageKey = await vault.StoreAsync(companyId, Guid.NewGuid(), pfxBytes, password, CancellationToken.None);
        var encryptedBytes = await File.ReadAllBytesAsync(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (System.Text.Encoding.UTF8.GetString(encryptedBytes).Contains(password, StringComparison.Ordinal))
            throw new InvalidOperationException("Lozinka sertifikata je pronađena kao običan tekst u skladištu.");
        var loaded = await vault.LoadAsync(storageKey, CancellationToken.None);
        if (!loaded.PfxBytes.SequenceEqual(pfxBytes) || loaded.Password != password)
            throw new InvalidOperationException("Šifrovano skladište nije vratilo originalni sertifikat.");
        await vault.DeleteAsync(storageKey, CancellationToken.None);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static async Task VerifyCertificateExpiryAlertsAsync()
{
    var now = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
    var repository = new FakeCertificateExpiryRepository([
        new(Guid.NewGuid(), Guid.NewGuid(), "11111111", "Firma 7 dana", "a.pfx", "AA", now.AddDays(6), 6, false),
        new(Guid.NewGuid(), Guid.NewGuid(), "22222222", "Istekla firma", "b.pfx", "BB", now.AddHours(-1), 0, true)
    ]);
    var service = new CertificateExpiryService(repository, new FixedTimeProvider(now));
    var first = await service.ScanAsync(CancellationToken.None);
    var second = await service.ScanAsync(CancellationToken.None);
    if (first.AlertsCreated != 2 || second.AlertsCreated != 0 ||
        !repository.CreatedThresholds.Order().SequenceEqual(new[] { 0, 7 }))
        throw new InvalidOperationException("Pragovi ili idempotentnost certificate expiry alertova nijesu ispravni.");
}

static string FindOfficialSchema()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        var candidate = Path.Combine(
            directory.FullName,
            "src",
            "Summa.Fiscal.Infrastructure",
            "Fiscalization",
            "V5",
            "Schemas",
            "FiscalService_v5_official.xsd");

        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    throw new FileNotFoundException("Zvanični PU XSD nije pronađen u projektu.");
}
