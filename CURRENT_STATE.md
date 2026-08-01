# CURRENT STATE

## Datum
2026-07-03

## Trenutno stanje projekta

Kreirana je početna struktura projekta `SUMMA_FISCAL_PLATFORM` i dopunjen je modul `02_FISCAL_ENGINE`.

Dodati su zvanični dokumenti Poreske uprave / UPC v5:

- Tehnička specifikacija v5
- Funkcionalna specifikacija v5
- WSDL v1
- XSD v1

Glavni novi dokument:

```text
02_FISCAL_ENGINE/SUMMA_FISCAL_ENGINE.md
```

Ovaj dokument predstavlja kompletnu arhitekturu Fiscal Engine modula, uključujući:

- registraciju ENU,
- registraciju softvera,
- gotovinski depozit,
- fiskalizaciju računa,
- korektivne račune,
- avansne račune,
- knjižna odobrenja,
- periodične i sumarne račune,
- QR kod,
- offline i retry,
- IKOF/IIC,
- XML builder,
- XSD validator,
- digitalni potpis,
- SOAP klijent,
- audit log,
- lokalnu bazu,
- REST API,
- desktop/web/mobile adaptere,
- C# implementacioni plan.

## Sljedeći preporučeni korak

Implementirati baznu C# solution strukturu:

```text
src/Summa.Fiscal.Domain
src/Summa.Fiscal.Application
src/Summa.Fiscal.Xml
src/Summa.Fiscal.Security
src/Summa.Fiscal.Soap
src/Summa.Fiscal.Api
```

Prvo raditi:

1. Domain modele.
2. XML modele iz XSD-a.
3. XSD validator.
4. Digital signature service.
5. IKOF generator.
