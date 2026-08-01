# 09_TEST_SCENARIOS.md

## Svrha

Ovaj dokument definiše test strategiju za fiskalni modul.

## Nivoi testiranja

```text
1. Unit testovi
2. XML contract testovi
3. Signature testovi
4. Integration testovi prema testnom okruženju PU
5. End-to-end testovi kroz SUMMA API
6. Regression testovi nakon svake izmjene
```

## Testno okruženje

Za sve testove prema PU koristi se testno okruženje / Testni SEP. Produkciono okruženje se ne koristi za razvojne testove.

## Minimalni test scenariji

### Obični računi

```text
TC-001 Gotovinski račun sa jednom stavkom i standardnom PDV stopom
TC-002 Kartični račun sa jednom stavkom
TC-003 Virmanski račun
TC-004 Kombinovano plaćanje gotovina + kartica
TC-005 Račun sa više stavki i više poreskih stopa
TC-006 Račun sa oslobođenjem / bez PDV-a, ako je primjenjivo
TC-007 Račun sa iznosom 0 nije dozvoljen, osim ako specifikacija dozvoljava poseban slučaj
```

### Avansi

```text
TC-020 Avansni račun
TC-021 Konačni račun povezan sa jednim avansom
TC-022 Konačni račun povezan sa više avansa
TC-023 Pokušaj dvostrukog korišćenja istog avansa
```

### Storno / korekcije

```text
TC-030 Potpuno storniranje fiskalizovanog računa
TC-031 Djelimična korekcija računa
TC-032 Pokušaj storna nefiskalizovanog računa
TC-033 Pokušaj storna nepostojećeg računa
```

### Sertifikati

```text
TC-040 Važeći sertifikat
TC-041 Istečen sertifikat
TC-042 Sertifikat bez privatnog ključa
TC-043 Pogrešna lozinka
TC-044 Sertifikat druge firme
```

### XML/SOAP

```text
TC-050 XML validan po XSD
TC-051 XML nevalidan po XSD
TC-052 SOAP timeout
TC-053 SOAP 5xx
TC-054 PU vraća poslovnu grešku
TC-055 PU vraća uspješan odgovor sa JIKR
```

### Offline/retry

```text
TC-060 Internet nije dostupan
TC-061 Račun ide u offline queue
TC-062 Naknadna fiskalizacija uspješna
TC-063 Retry nakon timeout-a
TC-064 Poslovna greška ne ide u retry
TC-065 Retry exhausted nakon maksimalnog broja pokušaja
```

## Definition of Done za fiskalizaciju računa

Račun se smatra pravilno fiskalizovanim ako:

```text
- ima interni invoice id
- ima IKOF/IIC
- XML zahtjev je sačuvan
- potpisani XML je sačuvan
- SOAP request je sačuvan
- PU odgovor je sačuvan
- JIKR je sačuvan
- status je FISCALIZED
- QR podaci su generisani
- audit log postoji
- API odgovor klijentu sadrži sve potrebne podatke
```
