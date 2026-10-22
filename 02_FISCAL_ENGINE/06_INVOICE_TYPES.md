# 06_INVOICE_TYPES.md

## Svrha

Ovaj dokument definiše vrste fiskalnih dokumenata koje `SUMMA_FISCAL_PLATFORM` mora podržati.

Tačni tipovi, šifre i XML vrijednosti moraju biti usklađeni sa funkcionalnom i tehničkom specifikacijom Poreske uprave.

## Minimalni skup za MVP

```text
1. Obični račun
2. Račun sa gotovinskim plaćanjem
3. Račun sa kartičnim plaćanjem
4. Račun sa bezgotovinskim/virman plaćanjem
5. Račun sa kombinovanim plaćanjem
6. Storno / korektivni račun
7. Avansni račun
8. Konačni račun sa vezom na avans
```

## Račun kao agregat

```text
Invoice
    InvoiceItems
    InvoiceTaxes
    InvoicePayments
    RelatedDocuments
    FiscalizationStatus
```

## Obični račun

Obični račun mora imati:

```text
- firmu izdavaoca
- poslovni prostor
- ENU / uređaj
- operatera
- broj računa
- datum i vrijeme izdavanja
- stavke
- poreske stope
- ukupne iznose
- način plaćanja
- IKOF/IIC
- JIKR nakon fiskalizacije
```

## Storno / korektivni račun

Pravila:

```text
- fiskalizovani račun se ne mijenja direktno
- storniranje se radi preko novog dokumenta
- mora postojati veza na originalni račun
- mora se čuvati razlog korekcije/storna
- mora se znati da li je korekcija potpuna ili djelimična
```

Interni model:

```text
CorrectiveInvoice
    original_invoice_id
    correction_type
    reason
    corrected_items
```

## Avansni račun

Lokalni implementirani bankovni workflow od 22.10.2026. opisan je u [`../docs/ADVANCE_INVOICES.md`](../docs/ADVANCE_INVOICES.md). Sljedeći opšti model predstavlja i dugoročnu namjeru; ne označava sve scenarije kao završene. Posebno, više avansa na jednom konačnom računu nije omogućeno zbog `UniquePayMethodType` ograničenja zvaničnog v5 XSD-a. Umanjenje avansa poslije stvarnog prometa implementirano je kao zaseban korektivni dokument.

Avans mora biti posebna vrsta dokumenta. Mora se moći povezati sa konačnim računom.

```text
AdvanceInvoice
    advance_amount
    tax_amount
    remaining_amount
    linked_final_invoice_id
```

## Konačni račun sa avansom

Dugoročni poslovni zahtjev je veza konačnog računa na jedan ili više avansa. Trenutno je implementiran samo jedan izvor: za više izvora potreban je potvrđen PU XML ugovor, bez kršenja jedinstvenog tipa plaćanja ili gubitka obaveznog `AdvIIC`. Ovo nije završena funkcionalnost. Sistem mora spriječiti dvostruko iskorišćenje istog avansa.

Storno konačnog računa sa avansom je lokalno implementiran uz vraćanje na saldo. Ako je umanjenje avansa već fiskalizovano, slijedi posebno pozitivno poništenje tog umanjenja; njegovi iznosi su tačno obrnuti, a saldo se vraća tek nakon oba JIKR-a. Neposlato umanjenje otkazuje se u transakciji pripreme storna. Detalji i potvrde su u `docs/ADVANCE_INVOICES.md`.

```text
FinalInvoice
    linked_advance_invoices[]
    total_before_advance
    advance_deduction
    amount_due
```

## Načini plaćanja

### ORDER i sumarni račun — lokalna implementacija

`Normal` sa jedinim načinom plaćanja `Order` predstavlja fiskalizovani, još nenaplaćeni promet. Posebna ruta priprema `Summary` iz postojećih izvora i njihovih već uspješnih ORDER korekcija, sa serverom izvedenim referencama i tačnim zbirom po poreskim stopama. Sumarni i njegove korekcije su informativni (`isInformational`); ne računati ih ponovo u promet ili PDV.

Implementirani su djelimična/potpuna korekcija ORDER izvora, usklađenje već fiskalizovanog sumarnog i poništenje/zamjena sumarnog zbog pogrešnog načina plaćanja. Zaključavanje, audit i recovery su obavezni. POS stolovi ostaju izvan Fiscal API-ja. Ugovor, migracija, testovi i nepokriveni scenariji nalaze se u [`../docs/ORDER_SUMMARY_WORKFLOW.md`](../docs/ORDER_SUMMARY_WORKFLOW.md). Ovo nije periodični workflow i ne mijenja produkcioni `BankOnly` profil.

Interni enum:

```text
CASH
CARD
BANK_TRANSFER
MIXED
OTHER
```

Tačne PU šifre se ne smiju izmišljati. Moraju se mapirati iz specifikacije u tabelu `payment_method_mappings`.

## Poreske stope

Interno podržati:

```text
STANDARD
REDUCED
ZERO
EXEMPT
NOT_SUBJECT
MARGIN_SCHEME
EXPORT
```

Tačne XML vrijednosti i šifre iz PU specifikacije idu u `tax_category_mappings`.

## Validacije

```text
- zbir stavki mora odgovarati ukupnom iznosu
- zbir plaćanja mora odgovarati ukupnom iznosu
- PDV mora biti obračunat po pravilima
- avans ne smije biti veći od konačnog računa
- storno mora imati originalni račun
- originalni račun za storno mora biti fiskalizovan
- broj računa mora biti jedinstven u okviru definisanog opsega
```
