# FISCAL_ENGINE_IMPLEMENTATION_PLAN

## Cilj

Ovaj dokument definiše redosljed implementacije `Fiscal Engine` modula. Namijenjen je za Codex ili drugog AI agenta koji će pisati kod.

## Iteracija 1: Skeleton

### Zadatak

Kreirati osnovnu .NET solution strukturu.

```text
src/
  Summa.Fiscal.Api/
  Summa.Fiscal.Application/
  Summa.Fiscal.Domain/
  Summa.Fiscal.Infrastructure/
  Summa.Fiscal.Worker/
tests/
  Summa.Fiscal.Tests/
```

### Pravila

- Ne koristiti business logiku u controllerima.
- Controller samo prima zahtjev i poziva Application sloj.
- Domain sloj ne smije referencirati Infrastructure.
- Infrastructure implementira interfejse definisane u Application sloju.

## Iteracija 2: Domain Model

Napraviti osnovne entitete:

```text
FiscalInvoice
FiscalInvoiceItem
FiscalPayment
FiscalRequestLog
FiscalResponseLog
FiscalRetryJob
CompanyCertificate
BusinessUnit
FiscalDevice
FiscalOperator
```

Napraviti enum-e:

```text
FiscalStatus
InvoiceType
PaymentType
VatRateType
FiscalErrorType
RetryStatus
```

## Iteracija 3: Validation Engine

Napraviti servis:

```csharp
public interface IFiscalInvoiceValidator
{
    Task<ValidationResult> ValidateAsync(FiscalInvoice invoice, CancellationToken cancellationToken);
}
```

Validacije:

- invoice nije null
- company postoji
- business unit postoji
- device postoji
- operator postoji
- postoji najmanje jedna stavka
- postoji najmanje jedno plaćanje
- zbir stavki = zbir plaćanja
- PDV obračun ispravan

## Iteracija 4: Mock fiskalizacija

Napraviti `MockPuFiscalClient` koji vraća lažni JIKR.

Svrha je testiranje internog toka bez pozivanja PU.

## Iteracija 5: IIC placeholder

Napraviti `IIicGenerator` sa privremenim algoritmom za development.

Kasnije se zamjenjuje algoritmom iz zvanične tehničke specifikacije.

## Iteracija 6: Persistence

Koristiti PostgreSQL i Entity Framework Core.

Dodati migracije za:

- fiscal_invoices
- fiscal_invoice_items
- fiscal_payments
- fiscal_request_logs
- fiscal_response_logs
- fiscal_retry_jobs

## Iteracija 7: API endpoint

Endpoint:

```text
POST /api/v1/fiscal/invoices
```

Controller poziva command:

```text
CreateAndFiscalizeInvoiceCommand
```

Command handler radi:

```text
validate
calculate totals
generate number
generate iic
send to PU client
save response
return result
```

## Iteracija 8: Logging i Audit

Dodati correlation id u svaki zahtjev.

Svaka fiskalizacija mora imati:

- audit zapis
- request log
- response log
- status transition log

## Iteracija 9: Retry Worker

Dodati background worker koji obrađuje `FiscalRetryJob`.

Pravila:

- retry samo za tehničke greške
- maksimalno 6 pokušaja
- nakon toga status `MANUAL_REVIEW_REQUIRED`

## Iteracija 10: Real PU integration

Tek kada sve prethodno radi, implementirati:

- XML builder
- digital signature
- SOAP client
- real response parser
- real error mapper

## Definition of Done

Modul se smatra spremnim za MVP kada:

1. Može kreirati račun.
2. Može validirati račun.
3. Može mock fiskalizovati račun.
4. Čuva sve podatke u bazu.
5. Ima status lifecycle.
6. Ima audit log.
7. Ima osnovne testove.
8. Ima pripremljen interfejs za stvarnu PU integraciju.
