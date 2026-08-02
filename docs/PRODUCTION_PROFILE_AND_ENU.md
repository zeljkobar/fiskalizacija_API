# Produkcioni profil i ENU za bankovne račune

## Razdvajanje okruženja

Test i Production imaju odvojene fiskalne profile, poslovne jedinice, operatere i ENU uređaje. Aktivno okruženje firme određuje koji skup podataka fiskalni engine smije koristiti. Aktivacija ne prepisuje niti briše testne kodove.

Produkcioni endpoint je `https://efi.tax.gov.me/fs-v1` i podešava se serverski. Klijent ga ne može proizvoljno promijeniti kroz zahtjev za konfiguraciju.

## Produkcioni profil firme 02825767

U lokalnoj bazi su potvrđeni sljedeći SEP podaci:

- proizvođač: `gp177if699`
- softver: `Summa-fiscal-API`, verzija `1.0.1`
- kod sertifikovane verzije: `lq099vq111`
- održavalac: `qf401hk617`
- poslovna jedinica: `fx318ob312`
- operater: `zy624eg324`
- politika plaćanja: `BankOnly`

Lični identifikacioni broj operatera se ne čuva jer nije potreban fiskalnom engine-u.

## BankOnly zaštita

Kada je Production profil aktivan, fiskalizacija odbija račun koji sadrži gotovinu, karticu, vaučer ili drugi način plaćanja. Dozvoljen je samo `BankAccount`, a račun se šalje kao `NONCASH`.

## Registracija ENU-a

Zvanična EFI v5 XSD zahtijeva `TCRCode` na računu, pa i API za isključivo bankovne račune mora koristiti registrovan ENU. Interna oznaka je `SUMMA-API-BANK-01`.

Produkcioni ENU je 02.08.2026. uspješno registrovan kod PU i dodijeljen mu je TCR kod `qb854nc171`.

Endpoint:

`POST /api/v1/admin/companies/{companyId}/production-profile/register-enu`

Primjer tijela:

```json
{
  "internalCode": "SUMMA-API-BANK-01",
  "validFrom": "2026-08-02",
  "confirmation": "REGISTER_PRODUCTION_ENU:02825767:SUMMA-API-BANK-01"
}
```

Tok pravi potpisani `RegisterTCRRequest`, validira ga prema zvaničnoj XSD šemi, koristi mTLS, čuva request/response razmjenu i audit događaj, te aktivira uređaj tek kada PU vrati `TCRCode`. Ne registruje početni gotovinski depozit za ovaj bankovni ENU.

## Preduslov za stvarni poziv

Firma mora imati aktivan i važeći PFX/P12 fiskalni sertifikat u šifrovanom certificate vaultu. Privatni ključ i lozinka se nikad ne upisuju u konfiguraciju, bazu ili Git.

Aktivni sertifikat firme važi do 29.05.2027. Validacija traži PIB firme među identifikatorima pravnog lica u Subject-u; lični identifikator ovlašćenog potpisnika ne koristi se kao PIB izdavaoca računa.

Kontrolni bezgotovinski račun od 1,21 EUR uspješno je fiskalizovan na testnom PU sistemu i workflow je potom prebacio firmu u `ProductionActive` stanje.

Nakon ENU registracije i kontrolisane Production aktivacije, prvi stvarni račun se prvo kreira kao nacrt i provjerava. Slanje Poreskoj upravi zahtijeva eksplicitnu potvrdu tačnih stavki, iznosa, PDV-a, kupca, bankovnog računa i datuma.
