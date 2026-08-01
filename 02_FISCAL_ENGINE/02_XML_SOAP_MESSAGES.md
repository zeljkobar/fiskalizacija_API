# 02_XML_SOAP_MESSAGES.md

## Svrha

Ovaj dokument definiše kako `SUMMA_FISCAL_PLATFORM` treba da gradi, validira, potpisuje, šalje i čuva XML/SOAP poruke prema fiskalnom servisu Poreske uprave Crne Gore.

## Ključni princip

XML koji se šalje Poreskoj upravi nije samo tehnički payload. On je poreski dokaz. Zato sistem mora čuvati:

```text
- originalni XML prije potpisivanja
- XML nakon potpisivanja
- SOAP envelope koji je poslat
- raw HTTP/SOAP odgovor
- parsirani odgovor
- checksum/hash svake verzije poruke
- vrijeme kreiranja
- vrijeme slanja
- endpoint
- verziju servisa
- sertifikat kojim je poruka potpisana
```

## Arhitekturni slojevi za XML/SOAP

```text
FiscalRequestDto
    ↓
Domain Invoice Aggregate
    ↓
FiscalRequestBuilderV5
    ↓
XmlDocument / XDocument
    ↓
XmlSchemaValidatorV5
    ↓
XmlSigner
    ↓
SoapEnvelopeBuilder
    ↓
FiscalSoapClientV5
    ↓
FiscalResponseParserV5
```

## Zabranjeno

Codex ne smije:

```text
- ručno spajati XML stringove konkatenacijom
- preskakati XSD validaciju
- mijenjati namespace bez posebnog dokumentovanog razloga
- izostaviti raw request/response log
- oslanjati se samo na JSON model, jer PU prima XML/SOAP
- koristiti floating point za iznose
```

Koristiti decimal tip, strogu kulturu i kontrolisano formatiranje.

## Predložene klase

```csharp
public interface IFiscalXmlBuilder
{
    FiscalXmlBuildResult BuildInvoiceRequest(FiscalInvoice invoice);
    FiscalXmlBuildResult BuildCorrectiveInvoiceRequest(FiscalInvoice invoice);
    FiscalXmlBuildResult BuildBusinessUnitRequest(BusinessUnitRegistration registration);
    FiscalXmlBuildResult BuildCashDepositRequest(CashDeposit deposit);
}

public interface IFiscalXmlValidator
{
    FiscalXmlValidationResult Validate(string xml, FiscalMessageType messageType, FiscalServiceVersion version);
}

public interface ISoapEnvelopeBuilder
{
    string Wrap(string signedFiscalXml, FiscalSoapOperation operation);
}

public interface IFiscalSoapClient
{
    Task<FiscalSoapResponse> SendAsync(FiscalSoapRequest request, CancellationToken cancellationToken);
}
```

## Tipovi poruka koje treba podržati

Napomena: tačni nazivi SOAP operacija, root elemenata i namespace vrijednosti se moraju prepisati iz zvanične tehničke specifikacije v5 i XSD/WSDL fajlova. Ovdje se definiše razvojna mapa.

```text
1. Fiskalizacija računa
2. Fiskalizacija korektivnog/storno računa
3. Registracija / promjena ENU, ako se radi kroz fiskalni servis
4. Početni depozit / blagajna, ako je primjenjivo
5. Prateće statusne ili pomoćne poruke definisane specifikacijom
```

## Interni model poruke

Svaki zahtjev prema PU se čuva u tabeli `fiscal_requests`:

```text
id
company_id
invoice_id
message_type
service_version
environment
endpoint_url
correlation_id
idempotency_key
request_xml_unsigned
request_xml_signed
soap_envelope
request_hash
sent_at
received_at
http_status_code
transport_status
business_status
error_code
error_message
created_at
created_by
```

Odgovor se čuva u `fiscal_responses`:

```text
id
fiscal_request_id
raw_response
parsed_status
iic
jikr
received_at_tax_authority
error_code
error_message
response_hash
created_at
```

## Decimalni iznosi

Svi iznosi u domenu su `decimal(18, 6)`, ali XML formatiranje mora poštovati tačan format iz specifikacije. Za računovodstvenu tačnost preporučuje se:

```text
- jedinična cijena: decimal(18, 6)
- količina: decimal(18, 6)
- vrijednosti stavki: decimal(18, 2)
- poreske osnovice: decimal(18, 2)
- PDV iznosi: decimal(18, 2)
- ukupni iznosi: decimal(18, 2)
```

Pravila zaokruživanja moraju biti dokumentovana po tipu dokumenta.

## Datumi i vrijeme

Sistem mora interno čuvati UTC i lokalno vrijeme izdavanja računa. Za slanje prema PU koristiti format propisan specifikacijom. U modelu čuvati:

```text
issued_at_local
issued_at_utc
time_zone
sent_at_utc
received_at_utc
```

Za Crnu Goru podrazumijevana zona je `Europe/Podgorica`.

## Namespace i schema verzije

Ne hardkodovati direktno u više fajlova. Uvesti:

```csharp
public sealed class FiscalSchemaOptions
{
    public string Version { get; init; }
    public string InvoiceNamespace { get; init; }
    public string SoapActionFiscalizeInvoice { get; init; }
    public string InvoiceXsdPath { get; init; }
}
```

U `appsettings`:

```json
{
  "Fiscalization": {
    "ServiceVersion": "v5",
    "Environment": "Test",
    "SchemasPath": "schemas/fiscal/v5",
    "Endpoints": {
      "Test": "TO_BE_FILLED_FROM_OFFICIAL_SPEC",
      "Production": "TO_BE_FILLED_FROM_OFFICIAL_SPEC"
    }
  }
}
```

## XSD validacija

Prije slanja:

```text
1. XML mora biti well-formed.
2. XML mora biti validan prema XSD.
3. Potpis ne smije pokvariti validnost strukture.
4. Svaka greška validacije se mora vratiti korisniku prije slanja prema PU.
```

Rezultat validacije:

```csharp
public sealed class FiscalXmlValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<FiscalXmlValidationError> Errors { get; init; }
}
```

## SOAP transport

Transport mora imati:

```text
- timeout konfiguraciju
- retry samo za tehničke greške
- bez automatskog retry-ja za poslovne greške
- TLS validaciju
- logging bez otkrivanja privatnih ključeva
- correlation id u logovima
```

## Idempotency

Za fiskalizaciju računa uvesti `idempotency_key`:

```text
company_id + business_unit_code + enu_code + invoice_number + fiscal_year
```

Ako korisnik ponovi isti zahtjev, sistem ne smije kreirati novi račun ni novi pokušaj bez provjere prethodnog statusa.

## Response parsing

Parser mora izdvojiti:

```text
- status
- JIKR
- IKOF/IIC koji PU vraća ili potvrđuje, ako je dio odgovora
- vrijeme prijema
- kod greške
- poruku greške
- raw response
```

Nikad se ne oslanjati samo na tekst poruke. Koristiti kodove i XML elemente iz šeme.

## Testovi

Za svaku poruku moraju postojati:

```text
- unit test za XML builder
- unit test za schema validation
- unit test za signed XML structure
- contract test prema sačuvanom primjeru iz PU dokumentacije
- integration test prema testnom okruženju
```

## Potvrđeno mapiranje iz zvaničnog WSDL/XSD-a

| Poruka | SOAP Action | Root element | Namespace | XSD |
|---|---|---|---|---|
| Fiskalizacija računa | `https://efi.tax.gov.me/fs/RegisterInvoice` | `RegisterInvoiceRequest` | `https://efi.tax.gov.me/fs/schema` | `FiscalService_v5_official.xsd` |
| Registracija ENU/TCR | `https://efi.tax.gov.me/fs/RegisterTCR` | `RegisterTCRRequest` | `https://efi.tax.gov.me/fs/schema` | `FiscalService_v5_official.xsd` |
| Registracija depozita | `https://efi.tax.gov.me/fs/RegisterCashDeposit` | `RegisterCashDepositRequest` | `https://efi.tax.gov.me/fs/schema` | `FiscalService_v5_official.xsd` |

Za detaljno kanonsko mapiranje pogledati
`11_PU_XSD_WSDL_V5_CANONICAL_MAPPING.md`.
