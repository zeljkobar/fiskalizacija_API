using Summa.Fiscal.Infrastructure.Certificates;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Application.Certificates;

var schemaPath = FindOfficialSchema();
var builder = new RegisterInvoiceXmlBuilderV5();
var validator = new FiscalXmlSchemaValidatorV5(schemaPath);
VerifySummaTestConfiguration();
VerifySoapResponseParsing();
VerifyQrCodeGeneration();
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

    Console.WriteLine("QR URL je identičan zvaničnom PU v5 primjeru.");
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
