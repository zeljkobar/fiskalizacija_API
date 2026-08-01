# SYSTEM_ARCHITECTURE.md

## 1. Pregled arhitekture

SUMMA FISCAL PLATFORM koristi modularnu arhitekturu. Svaki modul ima svoju odgovornost, ali svi moduli dijele zajedničke standarde za API, audit, sigurnost i bazu.

## 2. Glavni slojevi

```text
Presentation / API Controllers
        ↓
Application Layer
        ↓
Domain Layer
        ↓
Infrastructure Layer
        ↓
External Services
```

## 3. Projekti u C# rješenju

Predložena struktura:

```text
src/
├── Summa.Fiscal.Api
├── Summa.Fiscal.Application
├── Summa.Fiscal.Domain
├── Summa.Fiscal.Infrastructure
├── Summa.Fiscal.Worker
├── Summa.SharedKernel
└── Summa.Tests
```

## 4. Odgovornosti

### Api

- prima HTTP zahtjeve
- validira osnovni format
- poziva Application sloj
- vraća standardizovan odgovor

### Application

- implementira use-case logiku
- koristi command/query handlere
- poziva domenske servise
- koordinira repository i external service pozive

### Domain

- sadrži poslovna pravila
- sadrži entitete i value objekte
- ne zavisi od baze, HTTP-a ili SOAP-a

### Infrastructure

- baza
- repositories
- SOAP client
- XML builder
- sertifikati
- storage
- logging implementacija

### Worker

- retry queue
- offline slanje
- background provjere
- periodične sinhronizacije

## 5. Ključni tok fiskalizacije

```text
Client App
  ↓
POST /api/v1/invoices/fiscalize
  ↓
Invoice Controller
  ↓
FiscalizeInvoiceCommand
  ↓
Validator
  ↓
Invoice Domain Model
  ↓
IIC Generator
  ↓
XML Builder
  ↓
Digital Signature Service
  ↓
PU SOAP Client
  ↓
Response Parser
  ↓
Database + Audit Log
  ↓
API Response
```

## 6. Princip izolacije

Fiscal Engine ne smije zavisiti od Accounting Engine-a. Accounting Engine može koristiti rezultate Fiscal Engine-a, ali ne obrnuto.

## 7. Vanjski servisi

- Poreska uprava Crne Gore — fiskalni servis
- e-mail servis
- storage servis
- eventualno SMS/Viber notifikacije

