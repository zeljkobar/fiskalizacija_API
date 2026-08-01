# Više aplikacija i firmi — autentifikacija Fiscal API-ja

Status: implementirano kao osnovni bezbjednosni sloj.

## Cilj

Jedan Summa Fiscal API može koristiti više nezavisnih sajtova i aplikacija. Svaka
aplikacija dobija sopstveni identitet i tajni API ključ, a pristup joj se odobrava
samo za izabrane firme i operacije.

- Jedna aplikacija može imati pristup većem broju firmi.
- Jedna firma može dozvoliti pristup većem broju aplikacija.
- Svaki zahtjev za račun sadrži `companyId`.
- API provjerava da li prijavljena aplikacija smije koristiti taj `companyId`.

## Zaglavlja svakog zahtjeva

Produkcijski klijent šalje:

```http
X-Fiscal-Client-Id: sfc_...
X-Fiscal-Api-Key: sfa_...
```

Ključ se prikazuje samo prilikom kreiranja ili rotacije. Baza čuva samo njegov
SHA-256 otisak, nikada čitljiv tajni ključ.

## Dozvole

- `invoices:create` — kreiranje računa u fiskalnom motoru;
- `invoices:read` — čitanje računa, statusa i QR podatka;
- `invoices:fiscalize` — slanje računa Poreskoj upravi;
- `clients:admin` — rezervisano za administrativne funkcije.

Tipičnom sajtu koji kreira i fiskalizuje račune dodjeljuju se prve tri dozvole.

## Administrativni API

Administrativne rute koristi samo backend postojećeg Summa sajta. Browser nikada
ne smije direktno sadržati administratorski ili klijentski ključ.

Administrativni pozivi trenutno koriste:

```http
X-Fiscal-Bootstrap-Key: <tajna iz server konfiguracije>
```

Rute:

```text
GET    /api/v1/admin/api-clients
POST   /api/v1/admin/api-clients
POST   /api/v1/admin/api-clients/{id}/rotate-key
DELETE /api/v1/admin/api-clients/{id}
```

Primjer kreiranja aplikacije:

```json
{
  "name": "Knjigovodstvo Online",
  "permissions": [
    "invoices:create",
    "invoices:read",
    "invoices:fiscalize"
  ],
  "companyIds": ["00000000-0000-0000-0000-000000000000"],
  "expiresAt": null
}
```

Odgovor sadrži `clientId` i `apiKey`. `apiKey` odmah treba smjestiti u bezbjedno
čuvanje tajni sajta. Ne može se kasnije pročitati iz baze; po potrebi se rotira.

## Podešavanje produkcije

```text
ApiAccess__RequireApiKey=true
ApiAccess__BootstrapAdminKey=<duga nasumična tajna>
```

Razvojno okruženje dozvoljava lokalne pozive bez ključa radi lakšeg testiranja.
To se ne smije prenijeti u produkciju.

## Baza

Migracija `AddApiClientsAndTenantAccess` dodaje:

- `fiscal.api_clients` — identitet aplikacije, hash ključa, dozvole i status;
- `fiscal.api_client_company_access` — firme kojima aplikacija smije pristupiti.

Idempotency ključ računa je izolovan po firmi. Dvije različite firme mogu koristiti
isti idempotency ključ bez međusobnog konflikta.

## Šta još nije riješeno ovim slojem

Ovaj modul potvrđuje identitet aplikacije i pravo pristupa firmi. Za potpuno
uvođenje više poreskih obveznika treba još implementirati:

1. administraciju firmi, ENU, uređaja i operatera;
2. bezbjedan unos i čuvanje PFX sertifikata za svaku firmu;
3. izbor PU profila i sertifikata prema `companyId` pri fiskalizaciji;
4. audit zapis koja aplikacija je pokrenula svaku operaciju;
5. zamjenu bootstrap pristupa punom administratorskom prijavom sajta.

Dok se to ne uradi, fiskalizacija prema PU i dalje koristi jedan razvojno
konfigurisan profil i jedan sertifikat, iako je API pristup već razdvojen po
aplikacijama i firmama.

## Pravila za sajt

- Browser poziva svoj backend; backend poziva Fiscal API.
- API ključ nikada ne slati JavaScript klijentu niti upisivati u Git.
- Svaki sajt dobija poseban ključ.
- Testni i produkcijski ključ moraju biti različiti.
- Kod sumnje na kompromitovanje ključ odmah rotirati ili deaktivirati.
- `companyId` uzeti iz autorizovanog korisničkog konteksta, ne vjerovati
  proizvoljnoj vrijednosti poslatoj iz browsera.
