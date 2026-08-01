# 01_OFFICIAL_SPEC_MAPPING.md

## Svrha dokumenta

Ovaj dokument je mapa između modula `02_FISCAL_ENGINE` i zvanične dokumentacije Poreske uprave Crne Gore za elektronsku fiskalizaciju.

Cilj nije da se prepriča dokumentacija, nego da se jasno definiše kako se svaka obaveza iz zvanične dokumentacije prevodi u module, klase, tabele, servise, validacije i testove u okviru `SUMMA_FISCAL_PLATFORM`.

## Zvanični izvori koje treba tretirati kao izvor istine

Za implementaciju se kao primarni izvori koriste:

1. Zvanična stranica Poreske uprave: `Elektronska fiskalizacija`.
2. `Fiskalni servis - Funkcionalna specifikacija v5 - final.docx`.
3. `Fiskalni servis - Tehnička specifikacija v5 - final.docx`.
4. Prilozi / primjeri XML poruka, ako su dostupni na zvaničnoj stranici.
5. Pravilnici objavljeni na istoj stranici, posebno pravilnik o obliku i strukturi poruka i sigurnosnim mehanizmima.
6. Testni SEP i produkcioni SEP kao operativna okruženja.

> Napomena za Codex: ako postoji neslaganje između ovog dokumenta i zvanične dokumentacije PU, zvanična dokumentacija uvijek ima prednost. Ovaj dokument je razvojni vodič, a ne pravni izvor.

## Status verzije fiskalnog servisa

Prema zvaničnim objavama, fiskalni servis je unapređivan do verzije v5. Posebno je važno da sistem bude verzionisan tako da sutra može podržati novu verziju bez lomljenja postojećeg koda.

Zato se u kodu ne smije hardkodovati logika kao `FiscalService`, već se uvodi verzionisani sloj:

```text
IFiscalServiceClient
    FiscalServiceClientV5

IFiscalRequestBuilder
    FiscalRequestBuilderV5

IFiscalSchemaValidator
    FiscalSchemaValidatorV5

IIicGenerator
    IicGeneratorV5
```

## Glavne cjeline koje proizilaze iz dokumentacije

Zvanična dokumentacija fiskalnog servisa se praktično prevodi u sljedeće cjeline sistema:

```text
1. Registracija / preduslovi
2. Sertifikati i digitalni potpis
3. IKOF / IIC generisanje
4. XML poruke
5. SOAP servis
6. Fiskalizacija računa
7. Fiskalizacija korektivnih računa
8. Avansni i konačni računi
9. Načini plaćanja
10. Poreske stope i poreski elementi
11. Poslovni prostor, ENU i operator
12. Offline režim
13. Greške i validacije
14. QR kod i provjera računa
15. Testiranje na testnom okruženju
16. Logovanje i audit
```

## Mapiranje na foldere u projektu

Predložena struktura u C# rješenju:

```text
src/
  Summa.Fiscal.Api/
  Summa.Fiscal.Application/
  Summa.Fiscal.Domain/
  Summa.Fiscal.Infrastructure/
  Summa.Fiscal.Worker/
  Summa.Fiscal.Contracts/

tests/
  Summa.Fiscal.UnitTests/
  Summa.Fiscal.IntegrationTests/
  Summa.Fiscal.ContractTests/
```

## Mapiranje na ključne module

| PU oblast | SUMMA modul | Primarna odgovornost |
|---|---|---|
| Sertifikati | Certificate Engine | Učitavanje, čuvanje, validacija i korišćenje sertifikata |
| IKOF/IIC | IIC Engine | Generisanje identifikacionog koda računa prema zvaničnom algoritmu |
| XML poruke | XML Builder | Formiranje validne XML poruke prema XSD šemi |
| Digitalni potpis | Signing Engine | Potpisivanje XML-a i/ili vrijednosti za IIC |
| SOAP komunikacija | Transport Engine | Slanje zahtjeva fiskalnom servisu PU |
| JIKR odgovor | Response Parser | Obrada odgovora PU i čuvanje statusa |
| QR kod | QR Engine | Generisanje QR linka / podataka za provjeru računa |
| Offline | Offline Engine | Evidencija računa izdatih bez pristupa servisu i naknadna fiskalizacija |
| Retry | Retry Engine | Kontrolisano ponovno slanje tehnički neuspjelih zahtjeva |
| Validacije | Validation Engine | Provjere prije slanja prema PU |
| Greške | Error Catalog | Normalizacija grešaka PU i internih grešaka |
| Audit | Audit Engine | Dokazni trag za svaki korak |

## Minimalni operativni tok za običan račun

```text
1. Klijent poziva SUMMA API: POST /api/v1/invoices/fiscalize
2. API validira JSON model.
3. Application sloj kreira CreateFiscalInvoiceCommand.
4. Domain sloj validira poslovna pravila računa.
5. Certificate Engine učitava aktivni sertifikat firme.
6. IIC Engine generiše IKOF/IIC.
7. XML Builder formira XML zahtjev prema PU šemi.
8. Signing Engine digitalno potpisuje poruku.
9. XML Schema Validator validira XML prema XSD.
10. Transport Engine šalje SOAP zahtjev na testni ili produkcioni endpoint.
11. Response Parser čita odgovor.
12. Ako je uspješno: čuva JIKR, vrijeme prijema, status FISCALIZED.
13. QR Engine generiše QR podatke.
14. Audit Engine čuva kompletan trag.
15. API vraća odgovor klijentu.
```

## Obavezna pravila implementacije

1. Nikad ne slati račun prema PU ako nije prošao internu validaciju.
2. Nikad ne fiskalizovati račun bez audit loga.
3. Nikad ne izgubiti originalni XML zahtjev i odgovor PU.
4. Nikad ne čuvati privatni ključ u čistom tekstu.
5. Nikad ne mijenjati fiskalizovan račun; svaka izmjena ide kroz korektivni/storno dokument.
6. Nikad ne ponavljati fiskalizaciju istog poslovnog računa bez idempotency kontrole.
7. Svaki request mora imati `CorrelationId`.
8. Svaki račun mora imati interni statusni tok.

## Statusni model računa

```text
DRAFT
VALIDATED
IIC_GENERATED
XML_CREATED
SIGNED
SENT_TO_TAX_AUTHORITY
FISCALIZED
FAILED_VALIDATION
FAILED_TRANSPORT
FAILED_TAX_AUTHORITY
OFFLINE_ISSUED
QUEUED_FOR_RETRY
CANCELLED_BY_CORRECTIVE_DOCUMENT
```

## Statusni model zahtjeva prema PU

```text
CREATED
SIGNED
SENT
RECEIVED_SUCCESS
RECEIVED_BUSINESS_ERROR
RECEIVED_TECHNICAL_ERROR
TIMEOUT
NETWORK_ERROR
RETRY_SCHEDULED
RETRY_EXHAUSTED
```

## Šta mora biti završeno prije kodiranja

Prije implementacije svake poruke potrebno je iz zvanične tehničke specifikacije prepisati u posebnu internu tabelu:

```text
- naziv poruke
- SOAP operacija
- XML namespace
- root element
- obavezni elementi
- opcioni elementi
- tipovi podataka
- format datuma i vremena
- decimalni format
- ograničenja dužine
- šifarnici
- XSD fajl
- očekivani odgovor
- moguće greške
```

## Lista dokumenata koje ovaj modul mora imati

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
