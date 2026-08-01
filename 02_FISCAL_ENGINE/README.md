# 02_FISCAL_ENGINE

## 1. Svrha modula

`Fiscal Engine` je centralni modul SUMMA FISCAL PLATFORM sistema. Njegov zadatak je da obezbijedi pouzdanu, sigurnu i ponovljivu fiskalizaciju računa u Crnoj Gori, nezavisno od toga da li račun dolazi iz POS aplikacije, web fakturisanja, mobilne aplikacije, ERP sistema ili budućeg računovodstvenog modula.

Ovaj modul ne smije biti vezan za jedan korisnički interfejs. On je servisni sloj koji prima strukturisan zahtjev za fiskalizaciju, validira ga, priprema fiskalni dokument, potpisuje ga, šalje prema servisu Poreske uprave, obrađuje odgovor, čuva rezultat i vraća aplikaciji sve podatke potrebne za izdavanje računa.

## 2. Glavna pravila

1. Fiskalizacija mora biti centralizovana.
2. Logika fiskalizacije ne smije biti duplirana u POS-u, web fakturisanju ili mobilnoj aplikaciji.
3. Svaki zahtjev i svaki odgovor moraju biti evidentirani.
4. Fiskalni dokument se nikad ne briše fizički.
5. Greška u komunikaciji ne smije izgubiti račun.
6. Sistem mora podržati offline i retry režim.
7. Sertifikati i privatni ključevi moraju biti tretirani kao visoko osjetljivi podaci.
8. Svaka fiskalna akcija mora imati audit trag.

## 3. Granice modula

### Modul radi

- prijem zahtjeva za fiskalizaciju
- validaciju obaveznih podataka
- generisanje internog broja dokumenta
- generisanje IIC-a
- pripremu XML/SOAP poruke
- digitalno potpisivanje
- komunikaciju sa servisom Poreske uprave
- obradu JIKR odgovora
- generisanje QR podatka
- evidenciju zahtjeva i odgovora
- retry u slučaju greške
- offline queue
- status fiskalizacije
- audit log

### Modul ne radi u MVP fazi

- kompletno knjigovodstveno knjiženje
- obračun plata
- završni račun
- kompleksan magacin
- napredni BI izvještaji
- AI kontrole
- integracije sa webshopovima

Ti moduli će koristiti rezultat fiskalizacije, ali nijesu dio samog `Fiscal Engine` jezgra.

## 4. Interna arhitektura

```text
Fiscal Engine

├── Invoice Intake
├── Fiscal Validation
├── Numbering Engine
├── IIC Generator
├── XML Builder
├── Digital Signing Engine
├── PU Transport Client
├── Response Parser
├── QR Generator
├── Fiscal Status Manager
├── Retry Queue
├── Offline Queue
├── Audit Logger
└── Fiscal Repository
```

## 5. Osnovni tok fiskalizacije

```text
1. Klijentska aplikacija šalje račun na Fiscal API.
2. API provjerava autentifikaciju i pravo pristupa firmi.
3. Invoice Intake prima zahtjev i kreira interni zapis.
4. Fiscal Validation provjerava podatke.
5. Numbering Engine dodjeljuje ili potvrđuje broj računa.
6. IIC Generator kreira IIC.
7. XML Builder kreira fiskalnu XML/SOAP poruku.
8. Digital Signing Engine potpisuje dokument.
9. PU Transport Client šalje zahtjev prema PU.
10. Response Parser obrađuje odgovor.
11. Ako je uspješno, čuva se JIKR i status FISCALIZED.
12. QR Generator priprema QR podatak.
13. Audit Logger bilježi cijeli tok.
14. API vraća rezultat aplikaciji.
```

## 6. Statusi fiskalnog dokumenta

```text
DRAFT
VALIDATED
READY_FOR_FISCALIZATION
FISCALIZATION_PENDING
FISCALIZED
FISCALIZATION_FAILED
OFFLINE_ISSUED
RETRY_PENDING
CANCELLED
STORNO_CREATED
```

### Objašnjenje

- `DRAFT` — dokument je kreiran, ali nije spreman za fiskalizaciju.
- `VALIDATED` — prošao je internu validaciju.
- `READY_FOR_FISCALIZATION` — spreman je za slanje.
- `FISCALIZATION_PENDING` — slanje je u toku.
- `FISCALIZED` — PU je vratila uspješan odgovor i JIKR.
- `FISCALIZATION_FAILED` — došlo je do poslovne ili tehničke greške.
- `OFFLINE_ISSUED` — račun je izdat u offline režimu i čeka naknadno slanje.
- `RETRY_PENDING` — sistem će pokušati ponovno slanje.
- `CANCELLED` — interni dokument je poništen prije fiskalizacije, ako je to dozvoljeno.
- `STORNO_CREATED` — za dokument je kreiran storno dokument.

## 7. Ključni entiteti

### FiscalInvoice

Predstavlja fiskalni dokument.

Minimalna polja:

```text
Id
CompanyId
BusinessUnitId
DeviceId
OperatorId
InvoiceType
InvoiceNumber
InvoiceDateTime
Currency
TotalNetAmount
TotalVatAmount
TotalGrossAmount
Iic
Jikr
QrCodeData
FiscalStatus
CreatedAt
UpdatedAt
FiscalizedAt
```

### FiscalInvoiceItem

Predstavlja stavku računa.

```text
Id
FiscalInvoiceId
ItemCode
ItemName
Quantity
UnitOfMeasure
UnitPrice
DiscountAmount
NetAmount
VatRate
VatAmount
GrossAmount
```

### FiscalPayment

Predstavlja način plaćanja.

```text
Id
FiscalInvoiceId
PaymentType
Amount
Reference
```

### FiscalRequestLog

Čuva svaki tehnički zahtjev poslat prema PU.

```text
Id
FiscalInvoiceId
RequestType
RequestXml
RequestHash
Endpoint
CreatedAt
CorrelationId
```

### FiscalResponseLog

Čuva svaki odgovor od PU.

```text
Id
FiscalInvoiceId
ResponseXml
ResponseCode
ResponseMessage
Jikr
CreatedAt
CorrelationId
```

### FiscalRetryJob

Predstavlja zakazani retry.

```text
Id
FiscalInvoiceId
RetryCount
NextRetryAt
LastErrorCode
LastErrorMessage
Status
CreatedAt
UpdatedAt
```

## 8. API endpointi MVP verzije

### POST /api/v1/fiscal/invoices

Kreira i fiskalizuje račun.

```json
{
  "companyId": "uuid",
  "businessUnitId": "uuid",
  "deviceId": "uuid",
  "operatorId": "uuid",
  "invoiceType": "NORMAL",
  "issueDateTime": "2026-07-03T12:15:00+02:00",
  "currency": "EUR",
  "items": [
    {
      "name": "Usluga knjigovodstva",
      "quantity": 1,
      "unitPrice": 100.00,
      "vatRate": 21.00
    }
  ],
  "payments": [
    {
      "paymentType": "BANK",
      "amount": 121.00
    }
  ]
}
```

Odgovor:

```json
{
  "success": true,
  "invoiceId": "uuid",
  "invoiceNumber": "1/PP1/2026",
  "status": "FISCALIZED",
  "iic": "...",
  "jikr": "...",
  "qrCodeData": "...",
  "fiscalizedAt": "2026-07-03T12:15:04+02:00"
}
```

### GET /api/v1/fiscal/invoices/{id}

Vraća status fiskalnog dokumenta.

### POST /api/v1/fiscal/invoices/{id}/retry

Ručno pokreće ponovno slanje.

### POST /api/v1/fiscal/invoices/{id}/storno

Kreira storno dokument za prethodno fiskalizovan račun.

## 9. Validaciona pravila MVP verzije

Prije slanja prema PU sistem mora provjeriti:

1. Firma postoji i aktivna je.
2. Firma ima podešen sertifikat.
3. Poslovni prostor postoji i aktivan je.
4. Uređaj postoji i vezan je za poslovni prostor.
5. Operater postoji i ima pravo izdavanja računa.
6. Račun ima najmanje jednu stavku.
7. Svaka stavka ima naziv, količinu, cijenu i poresku stopu.
8. Ukupan zbir stavki odgovara ukupnom iznosu računa.
9. Ukupan zbir plaćanja odgovara ukupnom iznosu računa.
10. Datum računa nije nelogičan u odnosu na serversko vrijeme.
11. Broj računa nije već iskorišćen.
12. Ako je storno, originalni račun mora postojati i biti fiskalizovan.

## 10. Greške

Greške se dijele na:

```text
VALIDATION_ERROR
CERTIFICATE_ERROR
SIGNING_ERROR
PU_COMMUNICATION_ERROR
PU_BUSINESS_ERROR
TIMEOUT_ERROR
DATABASE_ERROR
UNKNOWN_ERROR
```

API nikad ne vraća samo tekst greške. Uvijek vraća strukturisan model:

```json
{
  "success": false,
  "error": {
    "code": "CERTIFICATE_EXPIRED",
    "type": "CERTIFICATE_ERROR",
    "message": "Sertifikat je istekao.",
    "details": {},
    "correlationId": "..."
  }
}
```

## 11. Retry pravila

Retry se koristi samo za tehničke greške:

- timeout
- nedostupan servis PU
- mrežna greška
- privremena greška transporta

Retry se ne koristi za poslovne greške, npr. neispravan PIB, neispravan XML, nepostojeći poslovni prostor ili nevažeći sertifikat.

Predloženi raspored:

```text
1. pokušaj odmah
2. retry poslije 1 minut
3. retry poslije 5 minuta
4. retry poslije 15 minuta
5. retry poslije 1 sat
6. retry poslije 3 sata
7. dalje ručna kontrola
```

## 12. Offline režim

Ako fiskalizacija ne može biti završena zbog nedostupnog interneta ili servisa PU, sistem mora omogućiti evidentiranje računa u offline queue, ali samo ako poslovna pravila to dozvoljavaju.

Offline račun mora imati:

```text
InternalInvoiceId
InvoiceNumber
IIC
IssueDateTime
OfflineReason
RetryStatus
CreatedBy
CreatedAt
```

Kada se veza uspostavi, sistem mora automatski pokušati naknadnu fiskalizaciju.

## 13. Sertifikati

Sertifikat mora biti odvojen od poslovne logike.

Predloženi servis:

```text
CertificateService

- LoadCertificate(companyId)
- ValidateCertificate(companyId)
- GetPrivateKey(companyId)
- CheckExpiration(companyId)
- RotateCertificate(companyId)
```

Pravila:

1. Privatni ključ se ne smije logovati.
2. Lozinka sertifikata se ne smije čuvati u plain text obliku.
3. Sertifikat mora imati datum isteka.
4. Sistem mora upozoriti korisnika prije isteka sertifikata.
5. Pristup sertifikatu mora biti auditovan.

## 14. IIC Generator

IIC Generator mora biti deterministički. Za iste ulazne podatke mora vratiti isti rezultat.

Predloženi interfejs:

```csharp
public interface IIicGenerator
{
    Task<IicResult> GenerateAsync(IicInput input, CancellationToken cancellationToken);
}
```

`IicInput` ne smije direktno zavisiti od API DTO modela. Mora biti poseban domen model.

## 15. Digital Signing Engine

Predloženi interfejs:

```csharp
public interface IDigitalSigningService
{
    Task<SignedFiscalDocument> SignAsync(UnsignedFiscalDocument document, CompanyCertificate certificate, CancellationToken cancellationToken);
}
```

Pravila:

1. Potpisivanje je izolovan servis.
2. XML Builder ne smije znati detalje privatnog ključa.
3. Transport Client ne smije znati detalje potpisa.
4. Svaka greška potpisivanja mora biti jasno označena.

## 16. PU Transport Client

Predloženi interfejs:

```csharp
public interface IPuFiscalClient
{
    Task<PuFiscalResponse> SendInvoiceAsync(SignedFiscalDocument document, CancellationToken cancellationToken);
}
```

Transport Client mora imati:

- podešavanje test/prod endpointa
- timeout
- retry policy na nivou transporta
- correlation id
- raw request log
- raw response log
- TLS podešavanja

## 17. QR Generator

QR Generator ne odlučuje da li je račun validan. On samo generiše podatak za QR nakon uspješne fiskalizacije.

Predloženi interfejs:

```csharp
public interface IFiscalQrGenerator
{
    string Generate(FiscalInvoice invoice);
}
```

## 18. Audit pravila

Mora se evidentirati:

```text
Ko je pokrenuo fiskalizaciju
Za koju firmu
Sa kog uređaja
Koji račun
Kada
Koji sertifikat je korišćen
Da li je slanje uspjelo
Koji je odgovor PU
Koliko je trajalo
Da li je bilo retry pokušaja
```

## 19. Minimalni testovi

### Unit testovi

- validacija računa bez stavki
- validacija računa bez plaćanja
- obračun ukupnog iznosa
- obračun PDV-a
- deterministički IIC
- status transition pravila
- retry pravila

### Integration testovi

- fiskalizacija testnog računa prema testnom PU okruženju
- pogrešan sertifikat
- timeout simulacija
- retry queue
- offline queue
- storno fiskalizovanog računa

## 20. Prvi zadaci za Codex

1. Kreirati .NET solution `SummaFiscalPlatform.sln`.
2. Kreirati projekte:
   - `Summa.Fiscal.Api`
   - `Summa.Fiscal.Application`
   - `Summa.Fiscal.Domain`
   - `Summa.Fiscal.Infrastructure`
   - `Summa.Fiscal.Worker`
   - `Summa.Fiscal.Tests`
3. Dodati osnovne entitete za FiscalInvoice, FiscalInvoiceItem i FiscalPayment.
4. Dodati osnovne enum-e za InvoiceType, PaymentType i FiscalStatus.
5. Dodati endpoint `POST /api/v1/fiscal/invoices` sa mock fiskalizacijom.
6. Dodati audit log za svaki zahtjev.
7. Dodati PostgreSQL migraciju.
8. Dodati unit testove za validaciju računa.

## 21. Napomena za implementaciju

U prvoj iteraciji ne implementirati odmah stvarnu komunikaciju sa Poreskom upravom. Prvo napraviti stabilan interni tok sa mock `IPuFiscalClient`. Tek kada su domen, validacije, logovi i statusi stabilni, ubaciti stvarni SOAP/XML transport.

Ovo smanjuje rizik i omogućava da se aplikacija razvija kontrolisano.

---

## Detaljna dokumentacija modula

Ova verzija modula proširena je na osnovu zvanične strukture dokumentacije Poreske uprave za elektronsku fiskalizaciju. Prije implementacije moraju se koristiti zvanični DOCX/XSD/WSDL fajlovi kao izvor istine.

Dokumenti:

```text
01_OFFICIAL_SPEC_MAPPING.md
02_XML_SOAP_MESSAGES.md
03_IIC_IKOF_ALGORITHM.md
04_DIGITAL_SIGNATURE.md
05_CERTIFICATES.md
06_INVOICE_TYPES.md
07_OFFLINE_AND_RETRY.md
08_ERROR_HANDLING.md
09_TEST_SCENARIOS.md
10_CODEX_TASKS.md
```

Glavno pravilo: dokumentacija PU ima prednost nad ovim internim vodičem ako postoji razlika.
