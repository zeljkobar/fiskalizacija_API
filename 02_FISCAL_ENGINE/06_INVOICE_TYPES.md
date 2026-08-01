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

Avans mora biti posebna vrsta dokumenta. Mora se moći povezati sa konačnim računom.

```text
AdvanceInvoice
    advance_amount
    tax_amount
    remaining_amount
    linked_final_invoice_id
```

## Konačni račun sa avansom

Konačni račun mora imati vezu na jedan ili više avansa. Sistem mora spriječiti dvostruko iskorišćenje istog avansa.

```text
FinalInvoice
    linked_advance_invoices[]
    total_before_advance
    advance_deduction
    amount_due
```

## Načini plaćanja

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
