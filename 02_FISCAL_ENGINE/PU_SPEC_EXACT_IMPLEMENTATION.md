# PU_SPEC_EXACT_IMPLEMENTATION.md

**Projekat:** SUMMA FISCAL PLATFORM  
**Modul:** 02_FISCAL_ENGINE  
**Namjena:** precizna implementaciona mapa za C# servis fiskalizacije u Crnoj Gori  
**Status:** v1 — implementaciona mapa po zvaničnoj strukturi PU specifikacije  
**Datum:** 2026-07-03

---

## 0. Važna napomena o izvoru

Ovaj dokument služi kao **radni implementacioni fajl za Codex / AI agenta**.

Zvanični izvor koji se mora smatrati konačnim autoritetom je dokument Poreske uprave:

- **Fiskalni servis - Tehnička specifikacija v5 - final**
- Autor: **Poreska uprava**
- Objavljeno na portalu Vlade Crne Gore / gov.me
- Format: DOCX
- Dokument navodi fiskalni servis, XML/SOAP poruke, XSD šemu, WSDL, potpisivanje, IKOF/IIC i primjere.

U ovom fajlu su izdvojeni konkretni nazivi XML elemenata, atributa, operacija, namespace-ova i implementacionih klasa koje treba koristiti u C# servisu.

**Pravilo za implementaciju:**
Ako postoji razlika između ovog dokumenta i zvaničnog DOCX/XSD fajla Poreske uprave, prednost uvijek ima zvanični DOCX/XSD.

---

## 1. Ključni namespace-ovi i konstante

### 1.1. SOAP Envelope namespace

```xml
http://schemas.xmlsoap.org/soap/envelope/
```

Koristi se u SOAP omotu:

```xml
<SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
  <SOAP-ENV:Header/>
  <SOAP-ENV:Body>
    ...
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>
```

### 1.2. Fiskalni servis namespace

```xml
https://efi.tax.gov.me/fs
```

Koristi se u WSDL-u kao osnovni namespace servisa.

### 1.3. Fiskalna XML šema namespace

```xml
https://efi.tax.gov.me/fs/schema
```

Koristi se u svim root XML porukama:

```xml
<RegisterInvoiceRequest xmlns="https://efi.tax.gov.me/fs/schema" ...>
```

### 1.4. XML Digital Signature namespace

```xml
http://www.w3.org/2000/09/xmldsig#
```

Koristi se za `Signature` element.

### 1.5. XSD fajl

```text
eficg-fiscalization-service.xsd
```

### 1.6. XMLDSIG XSD fajl

```text
xmldsig-core-schema.xsd
```

---

## 2. WSDL operacije fiskalnog servisa

### 2.1. Port type

```xml
<wsdl:portType name="FiscalizationServicePortType">
```

### 2.2. Binding

```xml
<wsdl:binding name="FiscalizationServiceSoap" type="me:FiscalizationServicePortType">
  <soap:binding style="document" transport="http://schemas.xmlsoap.org/soap/http"/>
</wsdl:binding>
```

### 2.3. Service i port

```xml
<wsdl:service name="FiscalizationService">
  <wsdl:port name="FiscalizationServicePort" binding="me:FiscalizationServiceSoap">
    <soap:address location="https://efi.tax.gov.me/fs-v1"/>
  </wsdl:port>
</wsdl:service>
```

> U produkciji i testu endpoint se mora čitati iz konfiguracije. Nikad ne hardkodirati endpoint u kodu.

### 2.4. Operacije

| Operacija | SOAP Action | Request | Response | Napomena |
|---|---|---|---|---|
| `registerInvoice` | `https://efi.tax.gov.me/fs/RegisterInvoice` | `RegisterInvoiceRequest` | `RegisterInvoiceResponse` | Fiskalizacija računa |
| `registerTCR` | `https://efi.tax.gov.me/fs/RegisterTCR` | `RegisterTCRRequest` | `RegisterTCRResponse` | Registracija ENU/TCR uređaja |
| `registerCashDeposit` | `https://efi.tax.gov.me/fs/RegisterCashDeposit` | `RegisterCashDepositRequest` | `RegisterCashDepositResponse` | Registracija početnog / promjene gotovinskog depozita |

---

## 3. Root XML poruke

### 3.1. RegisterInvoiceRequest

```xml
<RegisterInvoiceRequest
    xmlns="https://efi.tax.gov.me/fs/schema"
    xmlns:ns2="http://www.w3.org/2000/09/xmldsig#"
    Id="Request"
    Version="1">
  <Header ... />
  <Invoice ...>
    ...
  </Invoice>
  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
    ...
  </Signature>
</RegisterInvoiceRequest>
```

#### Obavezni elementi

| Element | Kardinalnost | Tip / opis |
|---|---:|---|
| `Header` | `[1,1]` | `RegisterInvoiceRequestHeaderType` |
| `Invoice` | `[1,1]` | `InvoiceType` |
| `Signature` | `[1,1]` | XML Digital Signature |

#### Obavezni atributi root elementa

| Atribut | Vrijednost | Obavezno | Napomena |
|---|---|---:|---|
| `Id` | `Request` | Da | Koristi se za potpisivanje: URI `#Request` |
| `Version` | `1` | Da | Fiksna vrijednost iz dostavljenog zvaničnog XSD-a |

> Napomena: starije XSD verzije u primjerima mogu imati `Version="1"`. Za v5 implementaciju koristiti vrijednost iz zvanične v5 specifikacije.

---

### 3.2. RegisterInvoiceResponse

```xml
<RegisterInvoiceResponse Id="Response" Version="1" xmlns="https://efi.tax.gov.me/fs/schema">
  <Header ... />
  <FIC>...</FIC>
  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
    ...
  </Signature>
</RegisterInvoiceResponse>
```

#### Elementi

| Element | Kardinalnost | Opis |
|---|---:|---|
| `Header` | `[1,1]` | Header odgovora |
| `FIC` | `[1,1]` | Fiscal Invoice Code; u domaćoj terminologiji JIKR/FIC verifikacioni kod |
| `Signature` | `[1,1]` | Digitalni potpis odgovora PU/CIS |

#### Atributi

| Atribut | Vrijednost | Obavezno |
|---|---|---:|
| `Id` | `Response` | Da |
| `Version` | `1` | Da |

---

### 3.3. RegisterTCRRequest

```xml
<RegisterTCRRequest
    xmlns="https://efi.tax.gov.me/fs/schema"
    xmlns:ns2="http://www.w3.org/2000/09/xmldsig#"
    Id="Request"
    Version="1">
  <Header ... />
  <TCR ... />
  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
    ...
  </Signature>
</RegisterTCRRequest>
```

#### Elementi

| Element | Kardinalnost | Tip / opis |
|---|---:|---|
| `Header` | `[1,1]` | `RegisterTCRRequestHeaderType` |
| `TCR` | `[1,1]` | `TCRType` |
| `Signature` | `[1,1]` | XML Digital Signature |

#### Atributi

| Atribut | Vrijednost | Obavezno |
|---|---|---:|
| `Id` | `Request` | Da |
| `Version` | `1` | Da |

---

### 3.4. RegisterTCRResponse

```xml
<RegisterTCRResponse Id="Response" Version="1" xmlns="https://efi.tax.gov.me/fs/schema">
  <Header ... />
  <TCRCode>...</TCRCode>
  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
    ...
  </Signature>
</RegisterTCRResponse>
```

#### Elementi

| Element | Kardinalnost | Opis |
|---|---:|---|
| `Header` | `[1,1]` | Header odgovora |
| `TCRCode` | `[1,1]` | Kod ENU/TCR koji vraća servis |
| `Signature` | `[1,1]` | Digitalni potpis odgovora |

---

### 3.5. RegisterCashDepositRequest

```xml
<RegisterCashDepositRequest
    xmlns="https://efi.tax.gov.me/fs/schema"
    xmlns:ns2="http://www.w3.org/2000/09/xmldsig#"
    Id="Request"
    Version="1">
  <Header ... />
  <CashDeposit ... />
  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
    ...
  </Signature>
</RegisterCashDepositRequest>
```

#### Elementi

| Element | Kardinalnost | Tip / opis |
|---|---:|---|
| `Header` | `[1,1]` | `RegisterCashDepositRequestHeaderType` |
| `CashDeposit` | `[1,1]` | `CashDepositType` |
| `Signature` | `[1,1]` | XML Digital Signature |

---

### 3.6. RegisterCashDepositResponse

```xml
<RegisterCashDepositResponse Id="Response" Version="1" xmlns="https://efi.tax.gov.me/fs/schema">
  <Header ... />
  <FCDC>...</FCDC>
  <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
    ...
  </Signature>
</RegisterCashDepositResponse>
```

#### Elementi

| Element | Kardinalnost | Opis |
|---|---:|---|
| `Header` | `[1,1]` | Header odgovora |
| `FCDC` | `[1,1]` | Fiscalization Cash Deposit Code |
| `Signature` | `[1,1]` | Digitalni potpis odgovora |

---

## 4. Header elementi

### 4.1. Request Header — račun

```xml
<Header
    UUID="8d216f9a-55bb-445a-be32-30137f11b964"
    SendDateTime="2019-12-05T14:30:13+01:00" />
```

Opcioni offline/subsequent delivery atribut:

```xml
<Header
    UUID="8d216f9a-55bb-445a-be32-30137f11b964"
    SendDateTime="2019-12-05T14:30:13+01:00"
    SubseqDelivType="NOINTERNET" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `UUID` | Da | UUID poruke koju generiše ENU/TCR; RFC4122 v4 |
| `SendDateTime` | Da | Datum i vrijeme slanja poruke |
| `SubseqDelivType` | Ne | Tip naknadnog slanja, npr. offline/internet problem |

### 4.2. Response Header

```xml
<Header
    UUID="f8bcb5ae-59fb-41ac-9011-f4db86bbce26"
    RequestUUID="8d216f9a-55bb-445a-be32-30137f11b964"
    SendDateTime="2019-12-05T14:30:15+01:00" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `UUID` | Da | UUID odgovora koji generiše CIS/PU |
| `RequestUUID` | Da | UUID zahtjeva na koji se odgovor odnosi |
| `SendDateTime` | Da | Datum i vrijeme slanja odgovora |

---

## 5. InvoiceType — struktura računa

### 5.1. Elementi unutar `Invoice`

```xml
<Invoice ...>
  <PayMethods>
    <PayMethod Type="BANKNOTE" Amt="20.00" />
  </PayMethods>
  <Seller ... />
  <Buyer ... />
  <Items>
    <I ... />
  </Items>
  <SameTaxes>
    <SameTax ... />
  </SameTaxes>
</Invoice>
```

| Element | Kardinalnost | Obavezno | Opis |
|---|---:|---:|---|
| `SupplyDateOrPeriod` | `[0,1]` | Ne | Datum ili period isporuke ako se razlikuje od datuma izdavanja |
| `CorrectiveInv` | `[0,1]` | Ne | Podaci o originalnom računu za korektivni/storno račun |
| `PayMethods` | `[1,1]` | Da | Lista načina plaćanja |
| `Currency` | `[0,1]` | Ne | Valuta ako iznos nije izražen u osnovnoj valuti |
| `Seller` | `[1,1]` | Da | Podaci o prodavcu |
| `Buyer` | `[0,1]` | Ne | Podaci o kupcu |
| `Items` | `[1,1]` | Da | Stavke računa |
| `SameTaxes` | `[0,1]` | Ne | Agregirani porezi po istoj stopi/oslobođenju |
| `ConsTaxes` | `[0,1]` | Ne | Posebni / potrošački porezi |
| `Fees` | `[0,1]` | Ne | Naknade |
| `SumInvIICRefs` | `[0,1]` | Ne | Reference na IKOF/IIC kodove za zbirni račun |
| `BadDebtInv` | `[0,1]` | Ne | Nenaplativi dug |

### 5.2. Atributi `Invoice`

| Atribut | Obavezno | Tip iz XSD | Opis |
|---|---:|---|---|
| `TypeOfInv` | Da | `InvoiceSType` | Tip računa |
| `IsSimplifiedInv` | Da | `boolean` | Da li je pojednostavljeni račun |
| `TypeOfSelfIss` | Ne | `SelfIssSType` | Tip samoizdavanja |
| `IssueDateTime` | Da | `UTCSType` | Datum i vrijeme izdavanja računa |
| `InvNum` | Da | `InvNumSType` | Broj računa: ordinalni broj / godina / TCR kod |
| `InvOrdNum` | Da | `IntSType` | Redni broj računa |
| `TCRCode` | Ne | `RegistrationCodeSType` | Kod ENU/TCR uređaja koji izdaje račun |
| `IsIssuerInVAT` | Da | `boolean` | Da li je izdavalac u sistemu PDV-a |
| `TaxFreeAmt` | Ne | `DecimalNegSType` | Iznos oslobođen poreza |
| `MarkUpAmt` | Ne | `DecimalNegSType` | Iznos marže / markup |
| `GoodsExAmt` | Ne | `DecimalNegSType` | Iznos izvoza robe bez PDV-a |
| `TotPriceWoVAT` | Da | `DecimalNegSType` | Ukupan iznos bez PDV-a |
| `TotVATAmt` | Ne | `DecimalNegSType` | Ukupan iznos PDV-a |
| `TotPrice` | Da | `DecimalNegSType` | Ukupan iznos sa PDV-om |
| `OperatorCode` | Da | `RegistrationCodeSType` | Kod operatera |
| `BusinUnitCode` | Da | `RegistrationCodeSType` | Kod poslovne jedinice/prostora |
| `SoftCode` | Da | `RegistrationCodeSType` | Kod softvera |
| `ImpCustDecNum` | Ne | `String50SType` | Broj uvozne carinske deklaracije; interni podatak; ne popunjavati iz ENU ako nije dozvoljeno |
| `IIC` | Da | `Hex32SType` | IKOF/IIC — kod računa izdavaoca |
| `IICSignature` | Da | `Hex512SType` | Potpisani povezani parametri za IKOF/IIC |
| `IsReverseCharge` | Da | `boolean` | Obrnuto zaračunavanje / reverse charge |
| `PayDeadline` | Ne | `DateSType` | Rok plaćanja |
| `ParagonBlockNum` | Ne | `String20SType` | Broj paragon bloka |

---

## 6. PayMethods

### 6.1. Struktura

```xml
<PayMethods>
  <PayMethod Type="BANKNOTE" Amt="20.00" />
</PayMethods>
```

| Element | Kardinalnost | Opis |
|---|---:|---|
| `PayMethods` | `[1,1]` | Lista plaćanja |
| `PayMethod` | `[1,10]` | Jedan način plaćanja |

### 6.2. Atributi `PayMethod`

| Atribut | Obavezno | Opis |
|---|---:|---|
| `Type` | Da | Tip načina plaćanja |
| `Amt` | Da | Iznos plaćen ovim načinom |
| `CompCard` | Ne | Broj kompanijske kartice ako se koristi |

### 6.3. Mogući tipovi plaćanja — implementaciona napomena

Tačan šifarnik `PayMethod.Type` preuzeti iz zvaničnog XSD/simpleType definicije.

U primjerima se pojavljuje:

```text
BANKNOTE
```

Za MVP podržati barem mapiranje iz internog modela:

| Interni tip | XML vrijednost | Napomena |
|---|---|---|
| Gotovina | `BANKNOTE` | Potvrđeno u primjeru |
| Kartica | provjeriti XSD | Ne unositi napamet |
| Virman / račun | provjeriti XSD | Ne unositi napamet |
| Kombinovano | više `PayMethod` elemenata | Zbir mora biti jednak `TotPrice` |

---

## 7. Seller i Buyer

### 7.1. Seller

```xml
<Seller
    IDType="PIB"
    IDNum="..."
    Name="..."
    Address="..."
    Town="..."
    Country="MNE" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `IDType` | Da | Tip identifikacionog broja prodavca |
| `IDNum` | Da | Identifikacioni broj prodavca |
| `Name` | Da | Naziv prodavca |
| `Address` | Ne | Adresa |
| `Town` | Ne | Grad |
| `Country` | Ne | Država |

### 7.2. Buyer

```xml
<Buyer
    IDType="PIB"
    IDNum="..."
    Name="..."
    Address="..."
    Town="..."
    Country="MNE" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `IDType` | Ne | Tip identifikacionog broja kupca |
| `IDNum` | Ne | Identifikacioni broj kupca |
| `Name` | Ne | Naziv/ime kupca |
| `Address` | Ne | Adresa |
| `Town` | Ne | Grad |
| `Country` | Ne | Država |

---

## 8. Items i `I` stavke računa

### 8.1. Struktura

```xml
<Items>
  <I
    N="Artikal"
    C="123456"
    U="kom"
    Q="1.00"
    UPB="16.00"
    UPA="20.00"
    PB="16.00"
    VR="25.00"
    VA="4.00"
    PA="20.00" />
</Items>
```

| Element | Kardinalnost | Opis |
|---|---:|---|
| `Items` | `[1,1]` | Lista stavki računa |
| `I` | `[1,1000]` | Jedna stavka računa |

### 8.2. Atributi `I`

| Atribut | Obavezno | Tip iz XSD | Opis |
|---|---:|---|---|
| `N` | Da | `String50SType` | Naziv artikla/usluge |
| `C` | Ne | `String50SType` | Šifra artikla / barkod / interna šifra |
| `U` | Da | `String50SType` | Jedinica mjere |
| `Q` | Da | `DoubleNegForQuantitySType` | Količina; negativne vrijednosti dozvoljene za korektivne/nenaplative situacije |
| `UPB` | Da | `DecimalSType` | Jedinična cijena bez PDV-a |
| `UPA` | Da | `DecimalSType` | Jedinična cijena sa PDV-om |
| `R` | Ne | `DecimalSType` | Popust u procentu |
| `RR` | Ne | `boolean` | Da li popust umanjuje poresku osnovicu |
| `PB` | Da | `DecimalNegSType` | Ukupna cijena prije PDV-a za stavku/grupu |
| `VR` | Ne | `DecimalSType` | Stopa PDV-a |
| `VA` | Ne | `DecimalNegSType` | Iznos PDV-a |
| `IN` | Ne | `boolean` | Investiciona stavka |
| `PA` | Da | `DecimalNegSType` | Cijena poslije PDV-a |
| `EX` | Ne | `ExemptFromVATSType` | Oslobođenje od PDV-a |

---

## 9. SameTaxes

### 9.1. Struktura

```xml
<SameTaxes>
  <SameTax
    NumOfItems="1"
    PriceBefVAT="16.00"
    VATRate="25.00"
    VATAmt="4.00" />
</SameTaxes>
```

| Element | Kardinalnost | Opis |
|---|---:|---|
| `SameTaxes` | `[0,1]` | Agregirani porezi po istoj stopi/oslobođenju |
| `SameTax` | `[1,20]` | Jedan porez / agregirana grupa |

### 9.2. Atributi `SameTax`

| Atribut | Obavezno | Opis |
|---|---:|---|
| `NumOfItems` | Da | Broj stavki u grupi |
| `PriceBefVAT` | Da | Osnovica prije PDV-a |
| `VATRate` | Ne | Stopa PDV-a |
| `ExemptFromVAT` | Ne | Oslobođenje od PDV-a |
| `VATAmt` | Ne | Iznos PDV-a |

---

## 10. CorrectiveInv

```xml
<CorrectiveInv
    IICRef="..."
    IssueDateTime="2019-12-05T14:30:13+01:00"
    Type="..." />
```

| Atribut | Obavezno | Tip iz XSD | Opis |
|---|---:|---|---|
| `IICRef` | Da | `Hex32SType` | IKOF/IIC originalnog računa |
| `IssueDateTime` | Da | `UTCSType` | Datum i vrijeme izdavanja originalnog računa |
| `Type` | Da | `CorrectiveInvTypeSType` | Tip korektivnog računa |

Implementaciono pravilo:

- Nikada ne brisati originalni račun.
- Storno/korekcija se knjiži kao novi dokument sa referencom na originalni `IICRef`.
- Svi negativni iznosi moraju proći validaciju dozvoljenosti za korektivni dokument.

---

## 11. SupplyDateOrPeriod

```xml
<SupplyDateOrPeriod Start="2026-07-01" End="2026-07-31" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `Start` | Da | Početni datum isporuke |
| `End` | Da | Krajnji datum isporuke |

---

## 12. Currency

```xml
<Currency Code="EUR" ExRate="1.0000" IsBuying="false" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `Code` | Da | ISO kod valute |
| `ExRate` | Da | Kurs za preračun |
| `IsBuying` | Ne | Da li je kupovina strane valute |

Napomena: za Crnu Goru i EUR račune ovaj element se najčešće neće koristiti, ali ga treba podržati u modelu radi XSD kompatibilnosti.

---

## 13. ConsTaxes

```xml
<ConsTaxes>
  <ConsTax
    NumOfItems="1"
    PriceBefConsTax="..."
    ConsTaxRate="..."
    ConsTaxAmt="..." />
</ConsTaxes>
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `NumOfItems` | Da | Broj stavki pod potrošačkim porezom |
| `PriceBefConsTax` | Da | Osnovica prije potrošačkog poreza |
| `ConsTaxRate` | Da | Stopa potrošačkog poreza |
| `ConsTaxAmt` | Da | Iznos potrošačkog poreza |

---

## 14. Fees

```xml
<Fees>
  <Fee Type="..." Amt="..." />
</Fees>
```

| Element / atribut | Obavezno | Opis |
|---|---:|---|
| `Fees` | Ne | Lista naknada |
| `Fee` | Da ako postoji `Fees` | Jedna naknada |
| `Fee/@Type` | Da | Tip naknade |
| `Fee/@Amt` | Da | Iznos naknade |

---

## 15. SumInvIICRefs

```xml
<SumInvIICRefs>
  <SumInvIICRef IIC="..." IssueDateTime="2019-12-05T14:30:13+01:00" />
</SumInvIICRefs>
```

| Element / atribut | Kardinalnost | Opis |
|---|---:|---|
| `SumInvIICRefs` | `[0,1]` | Lista računa na koje se poziva zbirni račun |
| `SumInvIICRef` | `[1,1000]` | Jedna referenca |
| `IIC` | `[1,1]` | IKOF/IIC računa na koji se poziva |
| `IssueDateTime` | `[1,1]` | Datum i vrijeme izdavanja referenciranog računa |

---

## 16. BadDebtInv

`BadDebtInv` se koristi za nenaplativi dug. Implementacija mora biti posebno ograničena poslovnim pravilima.

Minimalno čuvati:

- referencu na originalni račun,
- datum originalnog računa,
- tip/razlog,
- vezu sa računovodstvenim knjiženjem.

Tačne atribute potvrditi direktno iz XSD simple/complex type definicije prije implementacije.

---

## 17. TCRType

Primjer:

```xml
<TCR
  BusinUnitCode="bb123bb123"
  IssuerNUIS="L91806031N"
  MaintainerCode="mm123mm123"
  SoftCode="ss123ss123"
  TCRIntID="1"
  ValidFrom="2019-12-05"
  Type="REGULAR" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `BusinUnitCode` | Da | Kod poslovne jedinice/prostora |
| `IssuerNUIS` | Da | PIB/JMB izdavaoca / poreskog obveznika |
| `MaintainerCode` | Da | Kod održavaoca softvera |
| `SoftCode` | Da | Kod softvera |
| `TCRIntID` | Da | Interni ID ENU/TCR uređaja |
| `ValidFrom` | Da | Datum od kada je ENU važeći |
| `ValidTo` | Ne | Datum do kojeg je ENU važeći / deaktivacija |
| `Type` | Da | Tip ENU/TCR uređaja, npr. `REGULAR` |

Implementaciono pravilo:

- Ako se uređaj već registrovao za isti interni ID i poslovnu jedinicu, odgovor može vratiti isti `TCRCode` ili ažurirati važenje, zavisno od pravila PU servisa.
- `TCRCode` iz odgovora mora se trajno čuvati.

---

## 18. CashDepositType

Primjer:

```xml
<CashDeposit
  CashAmt="2000.00"
  ChangeDateTime="2019-12-05T14:35:00+01:00"
  IssuerNUIS="L91806031N"
  Operation="INITIAL"
  TCRCode="cc123cc123" />
```

| Atribut | Obavezno | Opis |
|---|---:|---|
| `CashAmt` | Da | Iznos gotovine |
| `ChangeDateTime` | Da | Datum i vrijeme promjene depozita |
| `IssuerNUIS` | Da | PIB/JMB izdavaoca |
| `Operation` | Da | Operacija depozita, npr. `INITIAL` |
| `TCRCode` | Da | Kod ENU/TCR uređaja |

---

## 19. Digitalni potpis XML poruke

### 19.1. Algoritmi

| Namjena | Vrijednost |
|---|---|
| Vrsta potpisa | `http://www.w3.org/2000/09/xmldsig#enveloped-signature` |
| Canonicalization | `http://www.w3.org/2001/10/xml-exc-c14n#` |
| DigestMethod | `http://www.w3.org/2001/04/xmlenc#sha256` |
| SignatureMethod | `http://www.w3.org/2001/04/xmldsig-more#rsa-sha256` |

### 19.2. Reference URI

Za request:

```text
#Request
```

Za response validaciju:

```text
#Response
```

### 19.3. Struktura potpisa

```xml
<Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
  <SignedInfo>
    <CanonicalizationMethod Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#" />
    <SignatureMethod Algorithm="http://www.w3.org/2001/04/xmldsig-more#rsa-sha256" />
    <Reference URI="#Request">
      <Transforms>
        <Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature" />
        <Transform Algorithm="http://www.w3.org/2001/10/xml-exc-c14n#" />
      </Transforms>
      <DigestMethod Algorithm="http://www.w3.org/2001/04/xmlenc#sha256" />
      <DigestValue>...</DigestValue>
    </Reference>
  </SignedInfo>
  <SignatureValue>...</SignatureValue>
  <KeyInfo>
    <X509Data>
      <X509Certificate>...</X509Certificate>
    </X509Data>
  </KeyInfo>
</Signature>
```

### 19.4. C# konstante

```csharp
public const string FiscalSchemaNs = "https://efi.tax.gov.me/fs/schema";
public const string XmlDsigNs = "http://www.w3.org/2000/09/xmldsig#";
public const string RequestId = "Request";
public const string ResponseId = "Response";
public const string SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
public const string DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256";
public const string EnvelopedSignatureTransform = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
public const string ExclusiveCanonicalization = "http://www.w3.org/2001/10/xml-exc-c14n#";
```

---

## 20. IKOF / IIC algoritam

### 20.1. Nazivi u sistemu

| Lokalni naziv | XML naziv | Opis |
|---|---|---|
| IKOF | `IIC` | Identifikacioni kod računa izdavaoca |
| IKOF potpis | `IICSignature` | Potpisani povezani parametri računa |
| JIKR / FIC | `FIC` | Kod koji vraća PU/CIS za fiskalizovan račun |

### 20.2. Osnovni koraci

1. Spojiti propisane parametre računa u tačnom redosljedu.
2. Potpisati spojeni string privatnim ključem sertifikata.
3. Izračunati digest/hash nad potpisom.
4. Rezultat upisati u `IIC`.
5. Potpisani povezani parametri upisati u `IICSignature`.

### 20.3. Parametri koje algoritam koristi

Prema specifikaciji, IKOF/IIC se dobija spajanjem određenih parametara računa. Implementaciono u modelu obavezno podržati najmanje:

| Parametar | XML / domen |
|---|---|
| PIB/JMB izdavaoca | `Seller.IDNum` ili firma iz sertifikata / poreskog obveznika |
| Datum i vrijeme izdavanja | `Invoice.IssueDateTime` |
| Broj računa | `Invoice.InvNum` / `Invoice.InvOrdNum` |
| Poslovna jedinica | `Invoice.BusinUnitCode` |
| ENU/TCR kod | `Invoice.TCRCode` |
| Softver kod | `Invoice.SoftCode` |
| Ukupan iznos | `Invoice.TotPrice` |

**Pravilo:** tačan redosljed spajanja parametara mora se preuzeti iz poglavlja `Element podatka IKOF` zvanične specifikacije. Ne dozvoliti Codex-u da mijenja redosljed.

---

## 21. SOAP request primjer — račun

```xml
<SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
  <SOAP-ENV:Header/>
  <SOAP-ENV:Body>
    <RegisterInvoiceRequest xmlns="https://efi.tax.gov.me/fs/schema" xmlns:ns2="http://www.w3.org/2000/09/xmldsig#" Id="Request" Version="1">
      <Header SendDateTime="2019-12-05T14:30:13+01:00" UUID="8d216f9a-55bb-445a-be32-30137f11b964" />
      <Invoice
          BusinUnitCode="ab123ab123"
          IssueDateTime="2019-12-05T14:30:13+01:00"
          IIC="4AD5A215BEAF85B0416235736A6DACAB"
          IICSignature="83D728C8E10BA04C430BE6...F20AFBFA0602"
          InvNum="1/2019/cc123cc123"
          InvOrdNum="1"
          IsIssuerInVAT="true"
          IsReverseCharge="false"
          IsSimplifiedInv="false"
          OperatorCode="ab123ab123"
          SoftCode="EXAMPLE KODA SOFTVERA"
          TCRCode="KOD BLAGAJNE"
          TotPrice="20.00"
          TotPriceWoVAT="16.00"
          TotVATAmt="4.00"
          TypeOfInv="CASH">
        <PayMethods>
          <PayMethod Amt="20.00" Type="BANKNOTE" />
        </PayMethods>
        <Seller Address="ADRESA" Country="MNE" IDNum="ID BROJ" IDType="PIB" Name="IME PREZIME" Town="GRAD" />
        <Items>
          <I N="Artikal" U="kom" Q="1.00" UPB="16.00" UPA="20.00" PB="16.00" VR="25.00" VA="4.00" PA="20.00" />
        </Items>
        <SameTaxes>
          <SameTax NumOfItems="1" PriceBefVAT="16.00" VATRate="25.00" VATAmt="4.00" />
        </SameTaxes>
      </Invoice>
      <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
        ...
      </Signature>
    </RegisterInvoiceRequest>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>
```

---

## 22. Implementacioni C# modeli

### 22.1. Request modeli

```csharp
public sealed class RegisterInvoiceRequestXml
{
    public string Id { get; set; } = "Request";
    public int Version { get; set; } = 1;
    public RegisterInvoiceRequestHeaderXml Header { get; set; } = default!;
    public InvoiceXml Invoice { get; set; } = default!;
    public XmlElement Signature { get; set; } = default!;
}
```

```csharp
public sealed class RegisterInvoiceRequestHeaderXml
{
    public Guid UUID { get; set; }
    public DateTimeOffset SendDateTime { get; set; }
    public string? SubseqDelivType { get; set; }
}
```

```csharp
public sealed class InvoiceXml
{
    public string TypeOfInv { get; set; } = default!;
    public bool IsSimplifiedInv { get; set; }
    public string? TypeOfSelfIss { get; set; }
    public DateTimeOffset IssueDateTime { get; set; }
    public string InvNum { get; set; } = default!;
    public int InvOrdNum { get; set; }
    public string? TCRCode { get; set; }
    public bool IsIssuerInVAT { get; set; }
    public decimal? TaxFreeAmt { get; set; }
    public decimal? MarkUpAmt { get; set; }
    public decimal? GoodsExAmt { get; set; }
    public decimal TotPriceWoVAT { get; set; }
    public decimal? TotVATAmt { get; set; }
    public decimal TotPrice { get; set; }
    public string OperatorCode { get; set; } = default!;
    public string BusinUnitCode { get; set; } = default!;
    public string SoftCode { get; set; } = default!;
    public string? ImpCustDecNum { get; set; }
    public string IIC { get; set; } = default!;
    public string IICSignature { get; set; } = default!;
    public bool IsReverseCharge { get; set; }
    public DateOnly? PayDeadline { get; set; }
    public string? ParagonBlockNum { get; set; }
}
```

### 22.2. Response modeli

```csharp
public sealed class RegisterInvoiceResponseXml
{
    public string Id { get; set; } = "Response";
    public int Version { get; set; } = 1;
    public RegisterResponseHeaderXml Header { get; set; } = default!;
    public string FIC { get; set; } = default!;
    public XmlElement Signature { get; set; } = default!;
}
```

```csharp
public sealed class RegisterResponseHeaderXml
{
    public Guid UUID { get; set; }
    public Guid RequestUUID { get; set; }
    public DateTimeOffset SendDateTime { get; set; }
}
```

---

## 23. Minimalni C# servisi koje Codex treba napraviti

```text
Fiscalization.Api
Fiscalization.Application
Fiscalization.Domain
Fiscalization.Infrastructure
Fiscalization.Worker
```

### 23.1. Interface-i

```csharp
public interface IInvoiceXmlBuilder
{
    XmlDocument BuildRegisterInvoiceRequest(FiscalInvoice invoice);
}

public interface IXmlSignatureService
{
    XmlDocument SignRequest(XmlDocument document, X509Certificate2 certificate, string referenceId = "Request");
    bool VerifyResponseSignature(XmlDocument document);
}

public interface IIicGenerator
{
    IicResult Generate(FiscalInvoice invoice, X509Certificate2 certificate);
}

public interface IFiscalSoapClient
{
    Task<RegisterInvoiceResult> RegisterInvoiceAsync(XmlDocument signedRequest, CancellationToken cancellationToken);
    Task<RegisterTcrResult> RegisterTcrAsync(XmlDocument signedRequest, CancellationToken cancellationToken);
    Task<RegisterCashDepositResult> RegisterCashDepositAsync(XmlDocument signedRequest, CancellationToken cancellationToken);
}
```

### 23.2. Klase

```text
InvoiceXmlBuilder
XmlSignatureService
IicGenerator
FiscalSoapClient
FiscalResponseParser
FiscalErrorParser
FiscalRetryService
FiscalAuditLogger
CertificateLoader
CertificateVault
```

---

## 24. Validaciona pravila prije slanja u PU

### 24.1. Root request

- `Id` mora biti `Request`.
- `Version` mora odgovarati zvaničnoj specifikaciji.
- `Header.UUID` mora biti validan UUID v4.
- `Header.SendDateTime` mora biti `DateTimeOffset` sa vremenskom zonom.
- `Signature` mora biti prisutan nakon potpisivanja.

### 24.2. Invoice

- `InvOrdNum` mora biti redni broj računa u okviru ENU/TCR.
- `InvNum` mora biti konzistentan sa `InvOrdNum`, godinom i `TCRCode`.
- `TotPriceWoVAT + TotVATAmt` mora odgovarati `TotPrice`, uz pravila zaokruživanja iz Priloga/izračuna.
- Zbir `PayMethod.Amt` mora odgovarati `TotPrice`.
- `IIC` i `IICSignature` moraju biti generisani prije digitalnog potpisivanja XML poruke.
- `Seller` je obavezan.
- `Items/I` mora imati najmanje jednu stavku.

### 24.3. Signature

- Potpisuje se root element request-a.
- `Reference.Uri` mora biti `#Request`.
- `Id` atribut mora biti registrovan u XML parseru kao ID atribut.
- `Signature` se dodaje kao posljednji element root request-a.

---

## 25. Baza podataka — obavezna polja za čuvanje

### 25.1. fiscal_requests

| Kolona | Opis |
|---|---|
| `id` | Interni UUID |
| `company_id` | Firma |
| `request_uuid` | Header UUID |
| `operation` | `registerInvoice`, `registerTCR`, `registerCashDeposit` |
| `root_element` | `RegisterInvoiceRequest`, itd. |
| `xml_unsigned` | XML prije potpisa |
| `xml_signed` | XML poslije potpisa |
| `soap_action` | SOAP action |
| `status` | pending/sent/success/error |
| `created_at` | Vrijeme kreiranja |
| `sent_at` | Vrijeme slanja |

### 25.2. fiscal_responses

| Kolona | Opis |
|---|---|
| `id` | Interni UUID |
| `request_id` | Veza na zahtjev |
| `response_uuid` | Header UUID odgovora |
| `request_uuid` | Header RequestUUID |
| `fic` | FIC/JIKR za račun |
| `tcr_code` | TCRCode za TCR registraciju |
| `fcdc` | FCDC za cash deposit |
| `xml_response` | XML odgovor |
| `signature_valid` | Da li je potpis odgovora validan |
| `received_at` | Vrijeme prijema |

### 25.3. fiscal_invoices

Čuvati najmanje:

- `invoice_id`
- `company_id`
- `business_unit_code`
- `tcr_code`
- `operator_code`
- `soft_code`
- `inv_num`
- `inv_ord_num`
- `issue_date_time`
- `type_of_inv`
- `is_simplified_inv`
- `is_issuer_in_vat`
- `is_reverse_charge`
- `tot_price_wo_vat`
- `tot_vat_amt`
- `tot_price`
- `iic`
- `iic_signature`
- `fic`
- `status`
- `fiscalized_at`

---

## 26. Codex zadatak za implementaciju

### 26.1. Ne smije raditi

Codex ne smije:

- izmišljati XML tagove,
- mijenjati velika/mala slova atributa,
- mijenjati redosljed IKOF/IIC parametara,
- mijenjati namespace-ove,
- koristiti JSON za komunikaciju sa PU,
- slati nepotpisan XML,
- ignorisati XSD validaciju,
- ignorisati validaciju potpisa odgovora.

### 26.2. Mora raditi

Codex mora:

- generisati XML tačno po XSD-u,
- validirati XML prije slanja,
- generisati IIC i IICSignature,
- potpisati root request element,
- poslati SOAP request sa tačnim SOAP action-om,
- parsirati `FIC`, `TCRCode`, `FCDC`,
- validirati potpis odgovora,
- čuvati request/response za audit,
- podržati retry/offline slanje.

---

## 27. MVP scope za prvi servis

Prva implementacija treba da obuhvati samo:

1. `registerTCR`
2. `registerCashDeposit`
3. `registerInvoice`
4. Gotovinski račun (`TypeOfInv="CASH"`, `PayMethod Type="BANKNOTE"`)
5. Račun sa jednim artiklom
6. Račun sa jednom PDV stopom
7. Digitalni potpis request-a
8. Parsiranje `FIC`
9. Čuvanje XML request/response
10. Mock PU servis za lokalno testiranje

Sve ostalo ide poslije:

- avansi,
- storno,
- korektivni računi,
- više načina plaćanja,
- valute,
- posebni porezi,
- naknade,
- zbirni računi,
- nenaplativi dug,
- accounting integracija.

---

## 28. Open items — obavezno potvrditi direktno iz zvaničnog v5 DOCX/XSD

Prije produkcionog koda treba zaključati sljedeće:

1. Tačna vrijednost `Version` za sve root poruke u v5.
2. Kompletan šifarnik `InvoiceSType`.
3. Kompletan šifarnik `PayMethod.Type`.
4. Kompletan šifarnik `CorrectiveInvTypeSType`.
5. Kompletan šifarnik `ExemptFromVATSType`.
6. Kompletan šifarnik `SubseqDelivTypeSType`.
7. Tačan redosljed IKOF/IIC parametara.
8. Tačna pravila zaokruživanja iz Priloga 1 — izračuni.
9. Tačne greške PU i format fault response-a.
10. Testni i produkcioni endpoint iz aktuelne PU dokumentacije / SEP-a.

---

## 29. Zaključak

Ovaj dokument daje praktičnu osnovu da se napravi C# servis koji generiše i šalje fiskalne XML/SOAP poruke prema Poreskoj upravi.

Za razvoj se može odmah krenuti sa:

- XML modelima,
- SOAP client-om,
- XML signature servisom,
- IIC generatorom,
- bazom za request/response audit,
- lokalnim mock PU servisom.

Za produkciju je obavezno dodatno zaključati sve šifarnike i edge-case pravila direktno iz zvanične v5 specifikacije i XSD fajla.
