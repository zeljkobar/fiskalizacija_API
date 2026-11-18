# Lokalna isporuka P1 administratorskih pregleda

P1 je backend dopuna za [API ugovor](ADMIN_PREGLEDI_API_UGOVOR.md). Odvojeno ostaju P2 OpenAPI/katalog, Next.js ekrani, inventar stvarnog servera i puštanje u produkciju. Migracija `20261117164801_AddAdminFiscalReviews` ne mijenja fiskalne statuse, dokumente, numeraciju niti konfiguraciju firme.

## Konfiguracija i redoslijed puštanja

1. Na izolovanoj kopiji prvo primijeniti migracije. Novi kod čita aditivne kolone i upisuje metapodatke; ne postavljati ga na staru šemu. Provjeriti restore baze i fiskalne arhive zajedno.
2. `AdminDiagnostics:ReadEnabled`, `AdminDiagnostics:IndexExchanges` i `AdminDiagnostics:JournalEnabled` upravljaju čitanjem, indeksom razmjena i operativnim dnevnikom (podrazumijevano uključeni uz PostgreSQL). `ReadEnabled=false` čini nove preglede nedostupnim; stare rute detalja rade bez dijagnostičkog proširenja. Ako se privremeno isključi indeks, originalne XML razmjene se čuvaju i pomiruju kasnijim uvozom.
3. `AdminDiagnostics:TrustedActorClients` je početno prazno; navesti samo identifikatore pouzdanih backend klijenata. `AdminDiagnostics:RetentionDays` je početno 90 dana, samo za operativne zapise. `AdminDiagnostics:JournalSpoolPath` usmjeriti na trajni direktorijum dostupan samo servisu. Za Docker eksplicitno planirati volume i ACL; ne miješati ga sa arhivom XML-a. Red se prazni idempotentno po ID-u tek nakon upisa u bazu.
4. Poseban čitajući klijent dobija samo potrebne dozvole i opseg firmi. Security dnevnik traži `platform:admin`, `requests:read` i `requests:security:read`. Ključ ostaje na backend-u. Ni migracija ni uvoz ne dodjeljuju prava.
5. Prije puštanja provjeriti stvarne količine, planove upita i raspored izgradnje indeksa. Deployment i produkcijski uvoz zahtijevaju odvojeno odobrenje.

## Popuna istorije

Koristiti izdvojenu read-only kopiju arhive i izolovanu bazu. Odvojeni režim ne pokreće HTTP server, seed, migracije, periodičnu obradu ili PU klijente. Connection string dati bezbjednom konfiguracijom, ne u argumentima ili shell istoriji.

```powershell
dotnet Summa.Fiscal.Api.dll admin-exchange-backfill --dry-run <kopija-arhive>
dotnet Summa.Fiscal.Api.dll admin-exchange-backfill --apply <kopija-arhive> --expected-database <ime-testne-baze>
dotnet Summa.Fiscal.Api.dll admin-exchange-backfill --apply <kopija-arhive> --expected-database <ime-testne-baze> --after <checkpoint>
```

Dry-run izvještava šta bi bilo dodato, bez upisa. Commit ide u paketima do 100 direktorijuma; checkpoint se ispisuje tek nakon commit-a. Na nepromijenjenoj arhivi drugi puni prolaz ne dodaje duplikate. Checkpoint važi za istu arhivsku kopiju; novi dokaz traži ponovljeni prolaz od početka. Očekivano ime baze je dodatna provjera, ne zamjena za potvrdu odredišnog hosta i naloga.

Uvoz provjerava SHA-256, UUID, tip XML poruke i vezu računa po IKOF-u i fiskalnim podacima. Račun bez dokaza o okruženju ostaje `Unknown`. Starije ENU/depozit razmjene bez pouzdane veze i sporni dokazi ostaju izvan tenant pregleda, a rezultat ih broji kao nepovezane ili sporne. Stari API zahtjevi se ne rekonstruišu iz XML-a. `coverage.partial` označava da arhiva ili dnevnik možda ne pokrivaju cio period.

## Provjere

```powershell
dotnet build Summa.Fiscal.sln --no-restore
dotnet run --project tests/Summa.Fiscal.ContractChecks --no-build
dotnet run --project tests/Summa.Fiscal.IntegrationTests --no-build
```

Za PostgreSQL testove `SUMMA_FISCAL_TEST_ADMIN_CONNECTION` upućuje isključivo na lokalni server koji dozvoljava posebnu privremenu bazu. Testovi kreiraju i uklanjaju samo svoju nasumičnu bazu. PU odgovori su simulirani. Produkcija nije dio provjera.

## Proba na izolovanoj kopiji produkcije

Na lokalno vraćenoj kopiji uparenog backupa od 18.11.2026. P1 migracija nije promijenila nijedan od 14 računa ni njihove stavke, plaćanja ili numeraciju. Probni prolaz bio je samo za čitanje. Od 24 arhivske razmjene 16 je pouzdano povezano i uvezeno, a 8 je ostalo izvan pregleda jer veza s firmom ili računom nije dokazana. Nije bilo konflikata ni oštećenih zapisa. Drugi prolaz nije dodao niti dopunio zapise. Izvorni dump i arhiva ostali su neizmijenjeni.

Provjereni su produkcioni promet i PDV na kopiji, usklađenost sa pojedinačnim računima, istorija potvrđenih pokušaja i izolacija pristupa po firmi. Proba nije deploy: produkciona baza i arhiva nijesu mijenjane i nijedan zahtjev nije poslat PU.
