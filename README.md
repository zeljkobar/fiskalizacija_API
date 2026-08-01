# SUMMA FISCAL PLATFORM

**SUMMA FISCAL PLATFORM** je dugoročni projekat za izgradnju modularne cloud platforme za fiskalizaciju, fakturisanje, računovodstvo, bankovne izvode, OCR, HR dokumentaciju i AI asistenciju za knjigovodstvene procese u Crnoj Gori.

Prvi modul koji se razvija je **Fiscal Engine** — nezavisan API servis za fiskalizaciju računa u Crnoj Gori.

---

## Osnovna ideja

Ne pravi se samo POS program. Pravi se centralni fiskalni i računovodstveni motor koji kasnije mogu koristiti:

- POS aplikacije
- web fakturisanje
- mobilne aplikacije
- računovodstveni sistem
- magacin
- bankovni izvodi
- OCR za ulazne račune
- HR dokumentacija
- integracije sa webshopovima
- drugi programeri preko API-ja i SDK-a

---

## Predložena tehnologija

- Backend: C# / ASP.NET Core Web API
- Baza: PostgreSQL
- Queue / background jobs: Hangfire ili RabbitMQ
- Cache: Redis
- Reverse proxy: Nginx
- Deployment: Docker + Linux VPS
- Frontend: React / Next.js
- Mobile: Flutter ili React Native

---

## Struktura projekta

```text
SUMMA_FISCAL_PLATFORM/
├── README.md
├── AGENTS.md
├── CURRENT_STATE.md
├── ROADMAP.md
├── 00_BLUEPRINT/
├── 01_ARCHITECTURE/
├── 02_FISCAL_ENGINE/
├── 03_ACCOUNTING_ENGINE/
├── 04_BANK_ENGINE/
├── 05_OCR_ENGINE/
├── 06_HR_ENGINE/
├── 07_AI_ENGINE/
└── docs/
```

---

## Prvi cilj

Napraviti stabilan, testiran i dokumentovan **Fiscal API** koji može:

1. primiti račun iz spoljne aplikacije;
2. validirati podatke;
3. generisati IKOF/IIC;
4. digitalno potpisati XML poruku;
5. poslati zahtjev fiskalnom servisu Poreske uprave;
6. primiti JIKR;
7. generisati QR kod;
8. sačuvati sve zahtjeve, odgovore i greške;
9. podržati retry i offline režim.

