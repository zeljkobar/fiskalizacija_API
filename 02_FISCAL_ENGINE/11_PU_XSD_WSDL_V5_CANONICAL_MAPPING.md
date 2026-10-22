# PU XSD/WSDL v5 — kanonsko mapiranje

**Status:** potvrđeno direktno iz dostavljenih zvaničnih fajlova  
**Izvor istine:** `docs/official_pu_v5/FiscalService_v5_official.xsd` i `FiscalService_v5_official.wsdl`

Ovaj dokument ima prednost nad ranijim internim primjerima kada postoji razlika.

## Namespace i verzija

| Stavka | Tačna vrijednost |
|---|---|
| WSDL namespace | `https://efi.tax.gov.me/fs` |
| XSD namespace | `https://efi.tax.gov.me/fs/schema` |
| SOAP 1.1 namespace | `http://schemas.xmlsoap.org/soap/envelope/` |
| XMLDSIG namespace | `http://www.w3.org/2000/09/xmldsig#` |
| Request `Id` | `Request` |
| Response `Id` | `Response` |
| XSD `Version` | `1` |

Važno: broj v5 u nazivu tehničke dokumentacije nije vrijednost XML atributa `Version`.
Zvanični dostavljeni XSD za svih šest root poruka propisuje fiksnu vrijednost
`Version="1"`.

## WSDL operacije

| Operacija | SOAPAction | Request | Response |
|---|---|---|---|
| `registerInvoice` | `https://efi.tax.gov.me/fs/RegisterInvoice` | `RegisterInvoiceRequest` | `RegisterInvoiceResponse` |
| `registerTCR` | `https://efi.tax.gov.me/fs/RegisterTCR` | `RegisterTCRRequest` | `RegisterTCRResponse` |
| `registerCashDeposit` | `https://efi.tax.gov.me/fs/RegisterCashDeposit` | `RegisterCashDepositRequest` | `RegisterCashDepositResponse` |

WSDL sadrži adresu `https://efi.tax.gov.me/fs-v1`. Operativni testni i produkcioni
endpoint moraju biti konfiguracija, a ne konstanta u poslovnom kodu.

## Root poruke

Svaki request sadrži `Header`, poslovni element (`Invoice`, `TCR` ili
`CashDeposit`) i obavezni `ds:Signature`. Svaki response sadrži `Header`,
rezultat (`FIC`, `TCRCode` ili `FCDC`) i obavezni `ds:Signature`.

## RegisterInvoiceRequest/Header

| Atribut | Tip | Upotreba |
|---|---|---|
| `UUID` | `UUIDSType` | obavezan |
| `SendDateTime` | `UTCSType` | obavezan |
| `SubseqDelivType` | `SubseqDelivTypeSType` | opcioni |

`SubseqDelivType`: `NOINTERNET`, `BOUNDBOOK`, `SERVICE`, `TECHNICALERROR`,
`BUSINESSNEEDS`.

## Invoice

Obavezni child elementi: `PayMethods`, `Seller`. Element `Items` je po XSD-u
opcioni, ali ga SUMMA poslovna validacija zahtijeva za standardni račun.

Obavezni atributi:

`TypeOfInv`, `IssueDateTime`, `InvNum`, `InvOrdNum`, `TCRCode`,
`IsIssuerInVAT`, `TotPriceWoVAT`, `TotPrice`, `OperatorCode`,
`BusinUnitCode`, `SoftCode`, `IIC`, `IICSignature`.

Opcioni atributi:

`InvType`, `IsSimplifiedInv`, `TypeOfSelfIss`, `TaxFreeAmt`, `MarkUpAmt`,
`GoodsExAmt`, `TotVATAmt`, `TotPriceToPay`, `IsReverseCharge`, `PayDeadline`,
`ParagonBlockNum`, `TaxPeriod`.

Opcioni child elementi:

`SupplyDateOrPeriod`, `CorrectiveInv`, `Currency`, `Buyer`, `Items`,
`SameTaxes`, `Approvals`, `Fees`, `IICRefs`, `SumInvIICRefs`, `BadDebtInv`.

## Zvanični šifarnici

### InvoiceTSType

`INVOICE`, `CORRECTIVE`, `SUMMARY`, `PERIODICAL`, `ADVANCE`, `CREDIT_NOTE`

### InvoiceSType

Lokalni ORDER/sumarni workflow mapira `Normal`/`Order` na `INVOICE`/`CASH` sa jedinim `ORDER` plaćanjem. Sumarni koristi `SUMMARY`, stvarno plaćanje i `IICRefs` svih referenciranih ORDER dokumenata sa njihovim sačuvanim potpisanim iznosima. `SameTax/@NumOfItems` broji izvorne dokumente po stopi, ne artikle. `Items` se ne šalje za informativni sumarni i njegove korekcije. ORDER korekcija koristi `CORRECTIVE` i `CorrectiveInv` na izvorni ORDER, a sumarna korekcija/poništenje `CorrectiveInv` na izvorni sumarni. Granice lokalne provjere i ugovor su u `docs/ORDER_SUMMARY_WORKFLOW.md`; live PU potvrda je poseban korak.

`CASH`, `NONCASH`

### PaymentMethodTypeSType

`BANKNOTE`, `CARD`, `BUSINESSCARD`, `SVOUCHER`, `COMPANY`, `ORDER`,
`ADVANCE`, `ACCOUNT`, `FACTORING`, `OTHER`, `OTHER-CASH`

`Invoice/PayMethods` ima XSD ograničenje `UniquePayMethodType`: tip plaćanja je jedinstven unutar jednog računa. Trenutni API zato dozvoljava samo jednu `ADVANCE` stavku sa jednim `AdvIIC` na konačnom računu. Umanjenje avansa koristi `CorrectiveInv` za avans i `IICRefs/IICRef` za konačni račun, prema funkcionalnoj specifikaciji v5, poglavlje 5.8. Implementacioni ugovor je u [`../docs/ADVANCE_INVOICES.md`](../docs/ADVANCE_INVOICES.md).

### CorrectiveInvTypeSType

`CORRECTIVE`, `ERROR_CORRECTIVE`

Kontrolisano poništenje prethodnog negativnog umanjenja avansa ostaje `InvType="CORRECTIVE"` i `CorrectiveInv/@Type="CORRECTIVE"`. `CorrectiveInv/@IICRef` referencira negativno umanjenje, iznosi/stavke/plaćanje su njegova tačna pozitivna inverzija, a `IICRefs/IICRef` referencira storno konačnog računa sa negativnim iznosom njegovog avansnog plaćanja. Osnova je funkcionalna specifikacija v5 §5.5 (korektivni račun za prethodni korektivni račun), povezana sa §5.8. Obični puni storno i dalje ima negativne iznose. XSD/potpis su lokalno provjereni; live prihvatanje novog toka nije provjeravano ovim zadatkom.

### ExemptFromVATSType

`VAT_CL17`, `VAT_CL20`, `VAT_CL26`, `VAT_CL27`, `VAT_CL28`, `VAT_CL29`,
`VAT_CL30`

### IDTypeSType

`TIN`, `ID`, `PASS`, `VAT`, `TAX`, `SOC`

### TCRSType

`REGULAR`, `VENDING`

### CashDepositOperationSType

`INITIAL`, `WITHDRAW`

## Stavka računa (`Items/I`)

Obavezni atributi:

`N`, `U`, `Q`, `UPB`, `UPA`, `PB`, `PA`.

Opcioni atributi:

`C`, `R`, `RR`, `VR`, `VA`, `IN`, `EX`, `VD`, `VSN`.

`Items/I` ima kardinalnost 1–1000. `PayMethods/PayMethod` ima kardinalnost
1–10. `SameTaxes/SameTax` ima kardinalnost 1–20.

## Pravilo implementacije

Transportni modeli v5 koriste isključivo XML nazive i leksičke vrijednosti iz
ovog dokumenta. Domaći domen (`FiscalInvoice`) se mapira na njih u posebnom
mapperu. Transportni modeli ne smiju sadržati poslovna nagađanja niti
automatski mijenjati vrijednosti prije slanja.
