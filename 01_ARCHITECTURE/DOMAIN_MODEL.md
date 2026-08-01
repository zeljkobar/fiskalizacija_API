# DOMAIN_MODEL.md

## 1. Svrha

Ovaj dokument definiše osnovne domenske entitete za SUMMA FISCAL PLATFORM.

## 2. Glavni entiteti

### Company

Predstavlja pravno lice ili preduzetnika koji koristi platformu.

Polja:

- Id
- Name
- PIB
- VATNumber
- Address
- City
- Country
- IsVatRegistered
- Status

### BusinessUnit

Predstavlja poslovni prostor.

Polja:

- Id
- CompanyId
- Code
- Name
- Address
- Municipality
- Status

### FiscalDevice / ENU

Predstavlja elektronski naplatni uređaj.

Polja:

- Id
- CompanyId
- BusinessUnitId
- Code
- Name
- SoftwareCode
- MaintainerCode
- Status

### Operator

Predstavlja korisnika/operatera koji izdaje račun.

Polja:

- Id
- CompanyId
- PersonalId ili OperatorCode
- Name
- Status

### Certificate

Predstavlja fiskalni sertifikat firme.

Polja:

- Id
- CompanyId
- CertificateName
- SerialNumber
- ValidFrom
- ValidTo
- EncryptedStoragePath
- Status

### Invoice

Predstavlja fiskalni račun.

Polja:

- Id
- CompanyId
- BusinessUnitId
- DeviceId
- OperatorId
- InvoiceNumber
- IssueDateTime
- InvoiceType
- PaymentType
- TotalAmount
- TotalVat
- IIC
- JIKR
- FiscalizationStatus
- QrCodeUrl

### InvoiceItem

Stavka računa.

Polja:

- Id
- InvoiceId
- ItemName
- Quantity
- UnitPrice
- Discount
- TaxRate
- TaxAmount
- TotalAmount

### InvoicePayment

Način plaćanja.

Polja:

- Id
- InvoiceId
- PaymentMethod
- Amount

### FiscalRequestLog

Čuva zahtjev poslat prema Poreskoj upravi.

### FiscalResponseLog

Čuva odgovor Poreske uprave.

### AuditLog

Čuva poslovni trag svake bitne operacije.

### RetryQueue

Čuva fiskalne zahtjeve koji treba ponovo da se pošalju.

## 3. Osnovna pravila

- Invoice se ne briše.
- Invoice mora imati bar jednu stavku.
- Invoice mora imati bar jedan način plaćanja.
- Suma plaćanja mora odgovarati ukupnom iznosu računa.
- Fiskalizovan račun ne smije se mijenjati direktno.
- Ispravke se rade storno/korektivnim dokumentima.

