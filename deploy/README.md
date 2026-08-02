# SUMMA Fiscal API — produkcijski deployment

Ovaj deployment je prilagođen serveru `185.102.78.178`: Ubuntu 24.04, Docker/Compose, host PostgreSQL 16 na `127.0.0.1:5432` i postojeći Nginx/Certbot. PostgreSQL i HTTPS proxy **nijesu u Dockeru**. U Dockeru rade samo API, certificate-expiry Worker i backup proces.

## Sigurnosna granica

Pokretanje stack-a ne kreira račun. Međutim, prenesena baza sadrži aktivan produkcioni profil. Deployment se provjerava isključivo preko `GET /health` i read-only endpointa. `POST` fiskalizacije, storna, registracije ENU-a i bilo koji PU poziv zabranjeni su bez pregleda nacrta i izričite potvrde korisnika.

## Ciljna topologija

```text
Internet
  -> Nginx + Certbot: https://fiscal.summasummarum.me
  -> 127.0.0.1:8585
  -> API Docker container (host network)
  -> PostgreSQL 16 na hostu: 127.0.0.1:5432

Worker Docker container -> ista host PostgreSQL baza
Backup Docker container -> deploy/backups
```

API je vezan samo za loopback adresu. Port 8585 se ne otvara u UFW-u. `network_mode: host` je namjeran: omogućava kontejnerima pristup postojećem PostgreSQL-u koji bezbjedno sluša samo na `127.0.0.1`.

## 1. Obavezni produkcioni podaci

Ne pokretati prazan sistem umjesto postojećeg produkcionog stanja. Sa lokalnog računara treba prenijeti:

1. PostgreSQL custom dump postojeće SUMMA baze;
2. `App_Data/Certificates` (šifrovani certificate vault);
3. `App_Data/FiscalExchanges` (request/response trag);
4. isti Base64 vault master ključ;
5. fiskalni PFX i njegovu lozinku;
6. novi jaki bootstrap admin ključ.

Lokalna read-only inventura i export su pripremljeni u Git-ignorisanom `deploy/transfer/current/` direktorijumu. On sadrži PostgreSQL custom dump, jedan šifrovani vault fajl i svih 18 fiskalnih exchange direktorijuma, uključujući uspješni test aktivacije i produkcioni račun prema OPTICONNECT-u. Transfer direktorijum namjerno ne sadrži vault master ključ niti čitljivi PFX; oni se prenose zasebnim kontrolisanim korakom.

Produkcioni profil, registrovani ENU, audit i prvi stvarni račun nalaze se u bazi. Vault bez originalnog master ključa nije moguće dešifrovati.

## 2. Serverski direktorijumi

Projekat se postavlja u `/home/deploy/apps/summa-fiscal`. Unutar `deploy/` kreiraju se direktorijumi:

```bash
mkdir -p local-secrets data/certificates data/exchanges backups
chmod 700 local-secrets data data/certificates data/exchanges backups
```

Storage direktorijumi moraju biti dostupni neprivilegovanom UID-u iz API image-a:

```bash
sudo chown -R 10001:10001 data/certificates data/exchanges
```

## 3. Nova host PostgreSQL baza

Baza i korisnik su odvojeni od postojećih aplikacija:

```text
database: summa_fiscal
role:     summa_fiscal_app
host:     127.0.0.1
port:     5432
```

Kreiranje se radi interaktivno na serveru, bez unošenja lozinke u shell history:

```bash
sudo -u postgres createuser --pwprompt summa_fiscal_app
sudo -u postgres createdb --owner=summa_fiscal_app --encoding=UTF8 summa_fiscal
```

Ako se prenosi postojeća baza, dump se vraća prije prvog starta API-ja:

```bash
PGPASSWORD='UNESI_PRIVREMENO_U_TERMINALU' pg_restore \
  --host=127.0.0.1 --username=summa_fiscal_app --dbname=summa_fiscal \
  --no-owner --no-privileges --exit-on-error summa-fiscal.dump
```

Lozinku ne slati u chat. Preporučeno je koristiti privremeni `PGPASSFILE` umjesto primjera iznad kada budemo stvarno radili restore.

## 4. Tajne van Git-a i image-a

`deploy/local-secrets/` je ignorisan u Git-u. Direktorijum mora imati mode `0700`. Kod Docker Compose file-secrets montaže fajlovi imaju mode `0644`, jer kontejner namjerno radi kao neprivilegovan UID `10001`; host direktorijum `0700` i dalje sprječava druge host korisnike da im pristupe:

- `postgres_password.txt` — lozinka role `summa_fiscal_app`;
- `database_connection.txt` — `Host=127.0.0.1;Port=5432;Database=summa_fiscal;Username=summa_fiscal_app;Password=LOZINKA;SSL Mode=Disable`;
- `bootstrap_admin_key.txt` — novi jaki ključ od najmanje 32 nasumična bajta;
- `certificate_vault_key.txt` — originalni Base64 ključ od tačno 32 bajta;
- `fiscal-certificate.pfx` — stvarni fiskalni sertifikat;
- `fiscal_certificate_password.txt` — PFX lozinka.

```bash
chmod 700 local-secrets
chmod 644 local-secrets/*
```

Vault ključ i PFX lozinku dodatno čuvati u odvojenom password/secret manager-u. Privatni ključ nikada ne ide u Git, Docker image, dokumentaciju ili chat.

## 5. Compose provjera i start

Iz `deploy/` direktorijuma:

```bash
cp .env.example .env
docker compose --env-file .env -f compose.production.yml config
docker compose --env-file .env -f compose.production.yml build
docker compose --env-file .env -f compose.production.yml up -d api worker backup
docker compose --env-file .env -f compose.production.yml ps
curl --fail --show-error http://127.0.0.1:8585/health
```

API izvršava samo nedostajuće EF migracije pri startu. Ne izvršavati fiskalizacioni `POST` kao test.

## 6. Nginx i HTTPS

Kopirati `nginx/fiscal.summasummarum.me.conf` u `/etc/nginx/sites-available/`, napraviti symlink u `sites-enabled`, zatim obavezno testirati konfiguraciju prije reload-a:

```bash
sudo nginx -t
sudo systemctl reload nginx
```

Tek kada lokalni `/health` radi i DNS pokazuje na server, izdati HTTPS sertifikat:

```bash
sudo certbot --nginx -d fiscal.summasummarum.me --redirect
```

Završna provjera:

```bash
curl --fail --show-error https://fiscal.summasummarum.me/health
```

## 7. Backup

Backup servis odmah, pa svakih 24 sata, pravi:

- PostgreSQL 16 custom dump;
- arhivu šifrovanog certificate vault-a;
- arhivu fiskalnih request/response fajlova.

Kopije su u `deploy/backups/`, sa podrazumijevanim zadržavanjem 30 dana. Taj direktorijum mora se dodatno kopirati na šifrovanu off-site lokaciju. Snapshot VPS-a je koristan, ali nije zamjena za provjeren PostgreSQL dump i kopiju vault ključa.

Najmanje jednom mjesečno uraditi restore probu u izolovanoj testnoj bazi. Tokom probe ne pokretati API sa produkcionim PU profilom i ne slati zahtjeve prema PU.

## 8. Ažuriranje i restart

Svi servisi koriste `restart: unless-stopped`, pa se automatski pokreću nakon restarta Docker daemon-a/servera. Prije ažuriranja provjeriti najnoviji backup, zatim:

```bash
docker compose --env-file .env -f compose.production.yml build
docker compose --env-file .env -f compose.production.yml up -d
docker compose --env-file .env -f compose.production.yml logs --tail=100 api worker backup
```

Nadzor treba obuhvatiti `/health`, slobodan disk, neuspjele backup-e, Worker greške i istek fiskalnog sertifikata.
