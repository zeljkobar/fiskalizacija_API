# 10_CODEX_TASKS.md

## Svrha

Ovaj dokument sadrži konkretne zadatke za Codex ili sličnog AI agenta.

## Pravilo

Codex ne smije izmišljati XML strukture, namespace, SOAP action, XSD ni algoritam za IKOF/IIC. Prvo mora koristiti zvaničnu tehničku specifikaciju i lokalno sačuvane XSD/WSDL fajlove.

## Task 001 - Napraviti osnovne projekte

```text
Create .NET solution:
- Summa.Fiscal.Api
- Summa.Fiscal.Application
- Summa.Fiscal.Domain
- Summa.Fiscal.Infrastructure
- Summa.Fiscal.Worker
- Summa.Fiscal.Contracts
- Summa.Fiscal.UnitTests
- Summa.Fiscal.IntegrationTests
```

## Task 002 - Domain entities

Implementirati:

```text
Company
BusinessUnit
FiscalDevice
Operator
Certificate
Invoice
InvoiceItem
InvoicePayment
InvoiceTax
FiscalRequest
FiscalResponse
FiscalRetryAttempt
AuditLog
```

## Task 003 - Certificate upload

Implementirati:

```text
POST /api/v1/companies/{companyId}/certificates
CertificateService
CertificateRepository
CertificateEncryptionService
CertificateValidationService
```

## Task 004 - IIC generator shell

Implementirati interfejs i test skeleton, ali algoritam popuniti tek nakon unosa zvaničnog algoritma.

```text
IIicGenerator
IicGeneratorV5
IicGenerationResult
IicGeneratorV5Tests
```

## Task 005 - XML builder shell

Implementirati infrastrukturu:

```text
IFiscalXmlBuilder
FiscalXmlBuilderV5
IFiscalXmlValidator
FiscalXmlValidatorV5
```

XSD path mora biti konfigurabilan.

## Task 006 - SOAP client

Implementirati:

```text
IFiscalSoapClient
FiscalSoapClientV5
FiscalSoapOptions
FiscalSoapResponse
```

Podržati timeout, logging, correlation id i environment switch.

## Task 007 - Fiscalization use case

Implementirati command:

```text
FiscalizeInvoiceCommand
FiscalizeInvoiceCommandHandler
FiscalizeInvoiceValidator
```

Flow:

```text
Validate → Generate IIC → Build XML → Sign XML → Validate XML → Send SOAP → Parse Response → Save → Return
```

## Task 008 - Retry worker

Implementirati:

```text
FiscalRetryWorker
RetryPolicy
RetryQueueRepository
```

## Task 009 - Test scenarios

Dodati testove iz `09_TEST_SCENARIOS.md`.

## Task 010 - Documentation sync

Svaki put kada se promijeni implementacija, ažurirati odgovarajući `.md` fajl.
