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

`CASH`, `NONCASH`

### PaymentMethodTypeSType

`BANKNOTE`, `CARD`, `BUSINESSCARD`, `SVOUCHER`, `COMPANY`, `ORDER`,
`ADVANCE`, `ACCOUNT`, `FACTORING`, `OTHER`, `OTHER-CASH`

### CorrectiveInvTypeSType

`CORRECTIVE`, `ERROR_CORRECTIVE`

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
