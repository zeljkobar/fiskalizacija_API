# 00_SUMMA_BLUEPRINT.md — SUMMA FISCAL PLATFORM

## 1. Vizija

SUMMA FISCAL PLATFORM nije samo aplikacija za fiskalizaciju računa. To je temelj buduće računovodstvene i poslovne platforme za firme i knjigovodstvene agencije u Crnoj Gori.

Prvi zadatak platforme je fiskalizacija računa u skladu sa pravilima Poreske uprave Crne Gore. Dugoročni zadatak je povezivanje fiskalizacije sa računovodstvom, PDV evidencijama, bankovnim izvodima, OCR obradom dokumenata, HR dokumentacijom i AI asistencijom.

## 2. Glavni cilj

Napraviti centralni Fiscal API koji će služiti kao stabilan, siguran i ponovo upotrebljiv servis za fiskalizaciju.

Taj servis mora moći da koristi više aplikacija:

- POS aplikacija
- web fakturisanje
- mobilna aplikacija
- ERP sistem
- računovodstveni modul
- integracije sa webshopovima
- SDK za druge programere

## 3. Šta ulazi u prvu verziju

Prva verzija treba da podrži minimalni, ali ispravan tok fiskalizacije:

- unos podataka o firmi
- poslovni prostor
- ENU / uređaj
- operater
- sertifikat
- račun
- stavke računa
- PDV stope
- načini plaćanja
- IKOF/IIC
- digitalno potpisivanje
- slanje na testni servis Poreske uprave
- prijem JIKR-a
- generisanje QR koda
- evidenciju grešaka
- retry queue
- audit log

## 4. Šta ne ulazi u prvu verziju

U prvu verziju ne ulazi:

- kompletan ERP
- kompletan magacin
- obračun plata
- napredni izvještaji
- AI knjiženje
- kompletan OCR
- webshop integracije
- regionalna fiskalizacija van Crne Gore

Ove stvari se planiraju kasnije.

## 5. Osnovni principi

### 5.1 Dokumentacija prije koda

Prvo se definiše poslovna i tehnička logika, pa se tek onda piše kod.

### 5.2 API first

Svaka funkcionalnost mora biti dostupna kroz API. UI je samo klijent API-ja.

### 5.3 Modularnost

Fiskalizacija, računovodstvo, bankovni izvodi, OCR i HR ne smiju biti jedna velika pomiješana aplikacija. Svaki modul mora imati jasne granice.

### 5.4 Audit everything

Svaka važna operacija mora ostaviti trag:

- ko je uradio
- kada je uradio
- sa koje IP adrese
- šta je poslato
- šta je primljeno
- da li je nastala greška

### 5.5 Nikad ne brisati fiskalne podatke

Fiskalni dokumenti se ne brišu. Koristi se soft delete gdje ima smisla, a za fiskalne dokumente ispravke se rade kroz storno, korekciju ili novi dokument.

### 5.6 Sigurnost od prvog dana

Sertifikati, privatni ključevi, tokeni i lozinke moraju biti tretirani kao osjetljivi podaci.

## 6. Dugoročna vizija

Platforma treba da preraste u:

```text
SUMMA PLATFORM
├── Fiscal Engine
├── Accounting Engine
├── Inventory Engine
├── Bank Engine
├── OCR Engine
├── HR Engine
├── Payroll Engine
├── Reporting Engine
├── AI Engine
└── Developer SDK
```

## 7. Zašto servis, a ne običan POS

Ako se fiskalizacija ugradi direktno u POS, svaka buduća aplikacija mora ponovo implementirati istu logiku. Ako postoji centralni Fiscal Engine, fiskalizacija se implementira jednom, a sve aplikacije ga koriste.

Prednosti:

- lakše održavanje
- manje grešaka
- jedinstven audit
- lakše testiranje
- mogućnost prodaje API-ja drugim programerima
- lakša nadogradnja kada PU promijeni specifikaciju

## 8. Glavni moduli

### Fiscal Engine

Odgovoran za fiskalizaciju, sertifikate, IKOF/IIC, JIKR, XML/SOAP, QR kod, retry i offline režim.

### Accounting Engine

Odgovoran za KIF, KUF, PDV evidencije, knjiženja, avanse, storna i otvorene stavke.

### Bank Engine

Odgovoran za uvoz i automatsko knjiženje bankovnih izvoda.

### OCR Engine

Odgovoran za čitanje ulaznih računa i dokumenata.

### HR Engine

Odgovoran za ugovore, anekse, prijave radnika, odluke, godišnje odmore i podsjetnike.

### AI Engine

Odgovoran za asistenciju, prepoznavanje knjiženja, predloge pravila i kontrolu grešaka.

## 9. Prva tehnička odluka

Backend Fiscal Engine-a pravi se u C# / ASP.NET Core Web API.

Razlog:

- dobra podrška za X509 sertifikate
- dobra podrška za XML
- stabilnost
- enterprise pristup
- dugoročno održavanje
- dobra integracija sa SOAP servisima

## 10. Ključna filozofija

Ovaj projekat ne smije biti samo zbir ekrana i tabela. Mora biti sistem koji razumije poslovnu logiku knjigovodstva.

Najveća prednost platforme nije samo kod, nego ugrađeno znanje iz računovodstva, poreza, fiskalizacije i svakodnevnog rada sa klijentima.

