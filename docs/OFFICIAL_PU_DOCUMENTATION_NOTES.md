# OFFICIAL_PU_DOCUMENTATION_NOTES.md

## Zvanični izvori

Za razvoj fiskalnog modula koriste se zvanični izvori Poreske uprave / gov.me:

- Elektronska fiskalizacija - Poreska uprava
- Testni SEP
- Produkcioni SEP
- Funkcionalna specifikacija v5
- Tehnička specifikacija v5
- Pravilnici objavljeni u sekciji Legislativa

## Važna napomena

Ovaj repozitorijum trenutno sadrži razvojnu dokumentaciju. Za implementaciju XML/SOAP detalja obavezno je lokalno preuzeti zvanične DOCX/XSD/WSDL fajlove i staviti ih u:

```text
docs/official/pu/fiscalization/v5/
```

Predloženi fajlovi:

```text
functional-spec-v5-final.docx
technical-spec-v5-final.docx
examples-v4-or-v5.docx
xsd/
wsdl/
```

## Pravilo za Codex

Codex ne smije izmisliti nijedan tehnički detalj koji pripada zvaničnoj specifikaciji. Ako nema lokalnog XSD/WSDL fajla, mora napraviti TODO i ostaviti jasnu oznaku `TO_BE_FILLED_FROM_OFFICIAL_SPEC`.
