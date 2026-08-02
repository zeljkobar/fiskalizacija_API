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
├── docs/
│   ├── CURRENT_STATE.md
│   ├── ROADMAP.md
│   └── README.md
├── 00_BLUEPRINT/
├── 01_ARCHITECTURE/
├── 02_FISCAL_ENGINE/
├── 03_ACCOUNTING_ENGINE/
├── 04_BANK_ENGINE/
├── 05_OCR_ENGINE/
├── 06_HR_ENGINE/
└── 07_AI_ENGINE/
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

## Trenutni status — 02.08.2026.

Osnovni Fiscal API, kupac na računu, atomska numeracija, potpuni storno, testna i produkciona fiskalizacija, šifrovano upravljanje sertifikatom, granularna administracija, produkcioni profil i registracija produkcionog ENU-a su implementirani. Firma je u stanju `ProductionActive`, a prvi stvarni bezgotovinski račun uspješno je fiskalizovan 02.08.2026.

Produkcijski API radi u Dockeru iza Nginx/HTTPS-a na `https://fiscal.summasummarum.me`, koristi PostgreSQL 16 instaliran na host serveru i ima trajno čuvanje fiskalnih razmjena, dnevni backup i automatsko pokretanje nakon restarta. Javna provjera je `GET /health`; početna ruta `/` trenutno nema web interfejs i očekivano vraća `404`.

Precizan pregled završenog rada, provjera i preostalih koraka nalazi se u [`docs/CURRENT_STATE.md`](docs/CURRENT_STATE.md). Operativni plan je u [`docs/ROADMAP.md`](docs/ROADMAP.md), a indeks tehničkih dokumenata u [`docs/README.md`](docs/README.md).
