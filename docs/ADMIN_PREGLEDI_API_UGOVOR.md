# Fiscal API ugovor za administratorske preglede

Verzija ugovora: **1.0 · 17.11.2026.** Ovo je specifikacija za implementaciju, ne opis dostupnih ruta. Osnova su [zahtjevi](../ZAHTJEVI_ZA_DOPUNU_FISCAL_API_ADMIN_PREGLEDI.md) i lokalni kod na commitu `c6e7d55`. Fiskalna baza, XML dokazi, fiskalizacija i oporavak ostaju odgovornost Fiscal API-ja. Next.js čita podatke isključivo preko svog backend-a. Nove rute su samo za čitanje i ne kontaktiraju Poresku upravu (PU), ne pokreću oporavak i ne mijenjaju fiskalno stanje.

Ugovor pokriva četiri ekrana: **Fiskalni računi** (lista i detalj), **Izvještaji** (zbir i dokumenti iza njega), **API i greške** (zahtjevi, PU razmjene, audit i katalog), **Pregled podataka** (postojeće konfiguracione rute). Obuhvata samo podatke ovog API-ja, ne sve račune u SEP-u. Izvještaj je operativni fiskalni pregled; računovođa potvrđuje tumačenje prije upotrebe za poslovne odluke. Nije PDV prijava, KIF niti evidencija stvarnih uplata.

Za način pokretanja i granice lokalne P1 isporuke vidjeti [operativno uputstvo](ADMIN_PREGLEDI_LOKALNA_ISPORUKA.md). Ugovor ne potvrđuje produkcijsku dostupnost.

## 1. Rute i dozvole

Sve putanje počinju sa `/api/v1`. **Postojeća** znači da se ruta zadržava uz kompatibilnu dopunu gdje je navedeno. Nove dozvole moraju se pojedinačno dodijeliti; migracija ih ne upisuje postojećim klijentima.

| Faza | GET putanja | Dozvola i podaci |
|---|---|---|
| P1 nova | `/admin/invoices` | `invoices:read`; lista računa |
| P1 postojeća | `/fiscal/invoices/{id}`, `/fiscal/invoices/{id}/status` | `invoices:read`; aditivni kontekst i sažetak greške po pravilima ispod |
| P1 nova | `/admin/reports/turnover` | **`reports:read`**; zbir po periodu |
| P1 nova | `/admin/reports/turnover/documents` | `reports:read` **i** `invoices:read`; dokumenti iza zbira |
| P1 nova | `/admin/invoices/{invoiceId}/exchanges` | **`exchanges:read`** i `invoices:read`; pokušaji konkretnog računa |
| P1 nova | `/admin/exchanges`, `/admin/exchanges/{exchangeId}` | `exchanges:read`; bezbjedni metapodaci PU razmjene |
| P1 nova | `/admin/api-requests` | **`requests:read`**; pripisani API zahtjevi |
| P1 nova | `/admin/api-requests?scope=security` | `requests:read`, **`requests:security:read`** i `platform:admin`; odbijeni pristup i nepripisani zapisi |
| P1 postojeća | `/admin/companies/{companyId}/audit` | `audit:read`; postojeći filteri plus `correlationId`, `invoiceId` |
| postojeća | `/admin/companies` i `/{companyId}` | `companies:read`; firme |
| postojeća | `/admin/companies/{companyId}/business-units`, `/devices`, `/operators`, `/production-profile`, `/readiness` | `configuration:read`; sufiksi su pod istom firmom |
| postojeća | `/admin/companies/{companyId}/certificates`, `/certificate-alerts`, `/activation` | Redom `certificates:read`, `alerts:read`, `activation:read`; samo prikaz |
| P2 nova | `/admin/openapi.json`, `/admin/route-catalog` | **`catalog:read`**; ugovor i opis stvarno registrovanih ruta, ne pristup podacima |

Za **nove rute** i nove audit filtere: validan registrovani klijent preko `X-Fiscal-Client-Id` + `X-Fiscal-Api-Key`, konkretna dozvola **i** provjeren opseg firme. `platform:admin` proširuje opseg firmi, ali ne zamjenjuje konkretnu dozvolu. Bootstrap ključ, razvojni klijent i implicitni wildcard ne otvaraju nove preglede. Osnovno ponašanje starih ruta ostaje kompatibilno; postojeće authorizere ne mijenjati globalno u ovoj isporuci. Novi prikaz konfiguracije koristi eksplicitno ovlašćen, po mogućnosti samo-čitajući klijent. Administracija API ključeva ostaje zaseban privilegovan posao; katalog ne daje tu dozvolu.

- Liste: `companyId` izostavljen znači **samo dodijeljene firme** (sve potvrđeno pripisane firme za platformskog klijenta). Scope je dio svakog SQL upita, uključujući `totalCount`, zbir, istoriju i drill-down. Eksplicitno nedodijeljena firma vraća `403`. Čitanje pojedinačnog resursa na novoj ruti van opsega vraća isti `404` kao nepostojeći ID.
- Izvještaj uvijek zahtijeva jednu firmu. Nepoznato okruženje nije sinonim za nedodijeljenu firmu: `Unknown` je vidljiv samo kad je firma pouzdano poznata.
- `scope=tenant` (podrazumijevano) ne vraća `401/403`, nepripisane ni višeznačne zapise. Tekst iz URL-a ili zahtjeva nije dokaz pripadnosti firmi. Za `scope=security` ne primati `companyId`; nepripisana razmjena se ne prikazuje ni na običnoj platformskoj listi, već se obrađuje offline u karantinu backfill-a.
- Redovni detalj računa ne smije zaobići `exchanges:read`: osnovni bezbjedan razlog i vrijeme ostaju vidljivi sa `invoices:read`; ID razmjene, Correlation ID pokušaja i link ka istoriji vidljivi su tek sa obje dozvole. Ne izlagati audit kroz ove odgovore.
- Identitet se čuva strukturisano: `clientId` iz autentifikacije, `initiatorId`/opciono ime iz postojećih `X-Fiscal-Actor-Id`/`X-Fiscal-Actor-Name` samo za registrovani backend iz izričite liste povjerljivih klijenata, i `executor` (npr. fiskalni motor ili recovery). UI backend provjerava sesiju i sam postavlja zaglavlja; iz browsera ih ne prosljeđuje. Ograničiti dužinu, ukloniti kontrolne znakove; delegirani identitet je audit podatak, ne dozvola za pristup. Neautentifikovani zahtjev nema klijenta ni korisnika.

### Globalni fiskalni pregled bez prava upisa

`platform:fiscal-read` daje opseg svih sadašnjih i budućih firmi samo za odobrene GET preglede: lista, detalj/status/QR računa, izvještaj i njegov detalj, fiskalne razmjene i pripisani API dnevnik. Konkretna dozvola je i dalje obavezna. Ne otvara stari audit, konfiguraciju, sertifikate, ključeve ni sigurnosni dnevnik. Svaki drugi zahtjev ovog klijenta je odbijen prije izvršavanja akcije.

Globalni čitač mora imati prazne `companyIds` i najmanje jednu od `invoices:read`, `reports:read`, `exchanges:read` i `requests:read`. API odbija kombinovanje s drugim pravima, uključujući upis ili `platform:admin`. Samo platformski administrator (ili postojeći bootstrap pristup za kontrolisanu inicijalizaciju/oporavak) može izdati platformski ključ.

Primjer tijela `POST /api/v1/admin/api-clients` za listu i detalj računa u `knjigovodstvoonline`:

```json
{
  "name": "Knjigovodstvo Online - globalni fiskalni pregled",
  "permissions": ["platform:fiscal-read", "invoices:read"],
  "companyIds": [],
  "expiresAt": null
}
```

Potrebno je izdati nov ključ i držati ga samo na backendu sajta, iza provjere uloge `admin`, odvojeno od postojećeg ključa za fiskalizaciju. Sajt koristi `FISCAL_API_READ_CLIENT_ID` i `FISCAL_API_READ_KEY`; nema fallback-a na postojeći ključ ako čitač nije konfigurisan. Ovo ne traži migraciju baze ni ponavljanje P1 popune istorije.

## 2. Zajednički format

Nastaviti `{success,data,error,correlationId}`, zaglavlje `X-Correlation-Id` i camelCase. Greška: `{code,message,details:[{code,field,message}]}`. Statusi: `400` neispravan filter/raspon, `401` nema važećeg ključa, `403` nema dozvole ili eksplicitno traži tuđu firmu, `404` nedostupan pojedinačni resurs, `503` indeks ili log nisu dostupni. Prazan rezultat je `200` sa praznim nizom; nedostatak istorije označiti, ne predstavljati kao uspjeh ili kompletan prazan period. Za osjetljive preglede `Cache-Control: no-store`.

- Liste: `data={items,page,pageSize,totalCount}`; `page=1`, `pageSize=50`, maksimum 200. Loše vrijednosti su `400`, nema tihog ispravljanja. Stabilan poredak: izabrana kolona pa `id`, oba u traženom smjeru. Broj i strana iz istog DB snapshot-a; novi upis između zahtjeva može pomjeriti narednu stranu.
- Trenuci u ISO 8601 sa zonom, odgovor u UTC. Granice `from` uključivo, `to` isključivo. Operativne liste koriste UTC trenutke; izvještaj koristi lokalne datume. Nepoznata vrijednost je `null`, ne prazna niska, nula ili lažna oznaka uspjeha. Dodatni enum-i su navedeni ispod; postojeći enum-i ostaju kompatibilni.
- Nova lista, izvještaj i drill-down vraćaju novac kao decimalni string sa dvije decimale, npr. `"121.00"`; SQL/.NET koriste decimal, sabiraju sačuvane centne iznose i ne izvode ponovo PDV iz bruto iznosa. Next.js formatira string ili ga pretvara u cente/decimalnu biblioteku; ne računa zbir kroz JavaScript `Number`. Postojeći detalj zadržava numerička polja; njegova nova novčana polja prate novi format.
- Filteri imaju whitelist i parametre u SQL-u. Limit teksta 100 znakova; naziv kupca za pretragu najmanje tri znaka. Pretrage nisu regularni izrazi. `correlationId` je tačna podudarnost i nije jedinstven.

Primjer prazne stranice:

```json
{
  "success": true,
  "data": { "items": [], "page": 1, "pageSize": 50, "totalCount": 0 },
  "error": null,
  "correlationId": "admin-pregled-001"
}
```

## 3. Računi i fiskalni kontekst

`GET /admin/invoices` podržava:

| Filter | Pravilo |
|---|---|
| `companyId,businessUnitId,deviceId,operatorId` | Tačan UUID; istovremeni filteri se spajaju sa AND |
| `environment` | `Test\|Production\|Unknown`; izostavljen = sva dozvoljena okruženja |
| `from,to` | Po `issueDateTime`; oba ili nijedan; bez raspona je najnovija paginirana istorija |
| `status,invoiceType,paymentType,category` | Tačni enum-i; tip plaćanja znači da račun sadrži taj tip |
| `invoiceNumber,officialInvoiceNumber,iic,jikr,buyerId` | Tačno, obrezan unos; fiskalni kodovi bez razlike veličine slova, vodeće nule se čuvaju |
| `buyerName` | Dio naziva, bez razlike veličine slova, literalni `%`/`_` |
| `sort,direction` | `issueDateTime\|createdAt\|totalGrossAmount`, `asc\|desc`; početno `issueDateTime desc` |

Red liste: `id,companyId,companyTin,companyName,environment,environmentSource,businessUnitId,deviceId,operatorId,invoiceNumber,officialInvoiceNumber,issueDateTime,createdAt,fiscalizedAt,buyer,invoiceType,category,status,currency,totalNetAmount,totalVatAmount,totalGrossAmount,iic,jikr,isInformational,originalInvoiceId,advanceSettlementFinalInvoiceId,advanceRestorationStornoInvoiceId,lastError`. `buyer` je `null` ili `{identificationType,identificationNumber,name}`; unit/ENU/operator imaju sažetke `businessUnit,device,operator` kao `{id,code,label,source}`. `source` je `SendSnapshot\|PreparationSnapshot\|CurrentDisplay\|Unknown`; kod/trenutni naziv pada na `null` ako nema izvora, a trenutni label se prikazuje kao takav. Stavke, XML, putanje i certifikatski podaci nisu u listi.

Kategorije (iz serverovih veza, ne iz teksta računa): `StandardSale,FinalWithAdvance,OrderSale,SaleCorrection,OrderCorrection,AdvanceReceipt,AdvanceStorno,AdvanceSettlement,AdvanceRestoration,Summary,SummaryCorrection,SummaryCancellation,Other`. Za korekcije prvo provjeriti oznaku sumarnog poništenja/korekcije, vezu obnove/umanjenja avansa i oznaku ORDER korekcije; tek zatim izvor: `Advance` daje `AdvanceStorno`, a obična prodaja ili konačni račun sa avansom daje `SaleCorrection`. Za originalne račune `Normal` sa jedinim `Order` daje `OrderSale`, sa `Advance` daje `FinalWithAdvance`, ostali obični daju `StandardSale`. `Advance` daje `AdvanceReceipt`, a `Summary` daje `Summary`. Ostalo je `Other` i nije dio neto prodaje.

Detalj i status aditivno dobijaju `environment,environmentSource,businessUnitId,deviceId,operatorId,lastAttempt,lastError,lastPuError,diagnosticsAvailability`; detalj dodatno sažetke, `category,isInformational` (gdje već postoji ostaje nepromijenjen), `fiscalContext={preparation,firstSend}` i `links` ka dozvoljenoj istoriji i postojećim vezama. Snapshot konteksta čuva samo fiskalna polja: firma/PIB, okruženje, šifru jedinice, TCR, operatera, softvera, identifikator/verziju ili fingerprint profila bez tajni. Ne vraća upload podatke, vault putanju ili sertifikat. `firstSend` je prvi pouzdano pripisan kontekst slanja; svaki pokušaj ima svoj snapshot. `environmentSource=SendSnapshot|VerifiedExchange|VerifiedHistoricalBinding|PreparationSnapshot|Unknown`. `PreparationSnapshot` klasifikuje samo neposlati dokument; kod ranije slatog računa bez potvrđenog istorijskog konteksta polje je `Unknown`. Promjena režima firme, naziva ili konfiguracije ne prepisuje istoriju; nesaglasni dokazi idu na pregled. Nastavak već započetog slanja ne smije prebaciti dokument u drugo okruženje; neslaganje blokira novi transport, ali ne mijenja postojeće pravilo oporavka iz provjerenog odgovora.

`lastAttempt={startedAt,completedAt,outcome,exchangeId,correlationId}`. Posljednji pokušaj bira se po `startedAt desc,exchangeId desc`. `lastError` je bezbjedna greška tog pokušaja ako je evidentirana (ne stara greška poslije uspješnog oporavka); `lastPuError` je posljednje evidentirano potvrđeno PU odbijanje, i poslije kasnijeg uspjeha. Sažetak greške: `{category,code,message,occurredAt}`. Dodatna polja pokušaja uslovljena su dozvolom iz §1. `diagnosticsAvailability=Indexed|Partial|NotRecorded` opisuje pokrivenost, ne fiskalni ishod.

## 4. Izvještaj prometa

Primjer: `GET /api/v1/admin/reports/turnover?companyId=<uuid>&environment=Production&fromDate=2026-10-01&toDate=2026-11-01&groupBy=Day`.

Obavezni `companyId,environment,fromDate,toDate,groupBy`; `environment=Test|Production|Unknown`, `groupBy=Day|Month|Year`, maksimum 3660 dana i rastući raspon. Opcioni `businessUnitId,deviceId,operatorId`. Datumi `YYYY-MM-DD` po `Europe/Podgorica`; [početak prvog dana, početak završnog dana). Lokalna ponoć se konvertuje u UTC za svaki kraj posebno, bez pretpostavke o 24 sata. Grupisanje po lokalnom `issueDateTime`; dokument ide u period izdavanja, ne u period čuvanja JIKR-a ili izdavanja originala. Vratiti i konkretne UTC granice. `Unknown` je odvojeni dijagnostički pogled, sa `authoritative=false`, nikad dio potvrđenog produkcionog/testnog zbira.

`data={filters,timeZone,rulesVersion,currency,authoritative,evaluatedAt,totals,groups,controls,coverage}`. `filters` vraća normalizovane filtere i granice; `rulesVersion="turnover-v1"`, `currency="EUR"`. `totals` i svaki `groups[]` imaju `documentCount,saleInvoiceCount,salesPositive,salesCorrections,salesNet,vatByRate,categories,payments`; grupa ima još `bucketStartDate`. Iznosi su `{net,vat,gross}`; `vatByRate[]` je `{rate,net,vat,gross}` za stvarni neto promet, `categories[]` je `{category,count,amounts}`, `payments[]` je `{category,paymentType,count,amount}`. Stope su decimalni stringovi sa do četiri decimale. Kategorije i razrada plaćanja su odvojene, a ne dodatni promet.

- `documentCount`: svi dokumenti sa JIKR-om i statusom `Fiscalized` ili `StornoCreated` u filteru, uključujući informativne i avansne. `saleInvoiceCount`: samo pozitivni potvrđeni `StandardSale,FinalWithAdvance,OrderSale`. Original ostaje u svojoj istoriji i kad je kasnije storniran. `salesCorrections` su potpisani negativni iznosi `SaleCorrection,OrderCorrection`. Neto je zbir pozitivnog i negativnog, po stopama i ukupno.
- Avans, njegov storno, umanjenje i obnova vode se sa svojim potpisanim iznosima u posebnim kategorijama. Nisu drugi promet prodaje. Informativni sumarni i njegove korekcije/poništenje nisu novi promet ni PDV. `payments` opisuje oznake na fiskalnim dokumentima, ne tok novca: `Order` nije naplata, `Advance` nije nova uplata, zbirni način plaćanja je u sumarnom, a obnova nije bankovna isplata. Ne sabirati sve redove plaćanja kao jedan iznos uplate.
- `controls.statusCounts` pokriva i neuspjele, nacrte, neposlate otkazane i nerazriješene (pending) bez uključivanja u potvrđen promet. `controls.openWorkflows[]` je `{kind,count,amount}` za `AdvanceSettlementDue,AdvanceRestorationDue,SummaryCorrectionDue`: trenutno stanje procesa nad relevantnim **konačnim odnosno sumarnim** računima iz perioda, koristeći postojeća pravila salda i praćenja. `evaluatedAt` je vrijeme DB snapshot-a, ne rekonstrukcija stanja na kraj perioda. `coverage` uključuje vrijeme početka indeksiranja i upozorenja o djelimičnoj istoriji; ne miješa nepoznato okruženje sa potvrđenim.
- `/turnover/documents` koristi istu baznu selekciju i parametre, uz `bucketStartDate`, `category`, `vatRate` i `metric=DocumentCount|SaleInvoiceCount|SalesPositive|SalesCorrections|SalesNet|Category|Payment|Status|OpenWorkflow` (default `DocumentCount`). Za `Category/Payment/Status/OpenWorkflow` obavezan je odgovarajući `category`/`category+paymentType`/`status`/`workflowKind`. Nevažeće kombinacije vraćaju `400`. Red ima podatke sa liste i `contribution={net,vat,gross,amount}`: samo doprinos odabranom redu/stopi, ne cijeli iznos kad je odabrana stopa ili plaćanje. Neprimjenljivi iznos je `null`; za workflow je `amount` dug konkretnog izvora. Isti dokument se u jednoj selekciji broji jednom. `totalCount` i zbir nisu zavisni od stranice; isti filteri daju isti skup za istu verziju podataka. Pozivi nijesu zamrznuti između zahtjeva; UI osvježava zbir i drill-down ako se podaci promijene.

Primjer prihvatanja: prodaja `121.00` u oktobru, storno `-121.00` u novembru; oktobar ostaje `121.00`, novembar `-121.00` i zbir oba mjeseca `0.00`. ORDER od `20.00` + njegov sumarni od `20.00` daju `20.00` prometa, ne `40.00`; način naplate je u sumarnom.

## 5. PU razmjene i dnevnik API zahtjeva

Razmjene su jedna stavka po `exchangeId`, različita od API zahtjeva i poslovnog audita. V1 vraća `exchangeId,invoiceId,companyId,environment,environmentSource,operation,startedAt,completedAt,durationMs,correlationId,requestUuid,puHttpStatus,outcome,error,localCompletion,profileFingerprint,provenance,evidenceState`. `operation=RegisterInvoice|RegisterTcr|RegisterCashDeposit|Other`. `provenance=Live|Backfill`, `evidenceState=Complete|RequestOnly|Incomplete`. `localCompletion=NotApplicable|Pending|Completed|Failed|Unknown` je odvojen od PU ishoda. `outcome=Started|Accepted|Rejected|Timeout|Cancelled|TransportError|LocalError|Unknown`. `Accepted` zahtijeva validan odgovarajući PU odgovor i fiskalni identifikator za operaciju; sam HTTP 200 nije dokaz. Nečitljiv odgovor nije potvrđeno odbijanje. Timeout/prekid ne dokazuje da PU nije prihvatila. `Accepted+Pending/Failed` znači PU odgovor postoji, a lokalni završetak nije potvrđen. Ne pretvarati to u automatski retry.

`/admin/exchanges`: filteri `companyId,invoiceId,environment,operation,outcome,localCompletion,puHttpStatus,correlationId,from,to`; vrijeme je `startedAt`. `/{invoiceId}/exchanges` fiksira račun i firmu; prihvata samo operativne filtere osim `companyId,invoiceId`. Poredak `startedAt desc,exchangeId desc`. Operativne liste zahtijevaju oba vremenska parametra ili nijedan; bez njih posljednjih 30 dana. Ako je zadat tačan `invoiceId`, `correlationId` ili se koristi pod-ruta računa, izostavljanje datuma pretražuje svu zadržanu istoriju. Eksplicitni raspon najviše 366 dana.

Dnevnik zahtjeva vraća `requestId,startedAt,completedAt,durationMs,method,routeTemplate,apiHttpStatus,outcome,error,clientId,initiatorId,companyId,invoiceId,correlationId`. Filteri: `scope,from,to,companyId,invoiceId,clientId,method,routeTemplate,apiHttpStatus,outcome,errorCode,correlationId`; isto vremensko pravilo i poredak `startedAt desc,requestId desc`. `outcome=Succeeded|ValidationError|Unauthorized|Forbidden|Conflict|FiscalRejected|TransportError|InternalError|Cancelled|Unknown`. Dnevnik bilježi sve zahtjeve prema API-ju sa neuspjehom (uključujući semantički neuspjeh sa HTTP 200) i uspješne autentifikovane upisne operacije; uspješni GET, health i katalog se ne čuvaju. Paginirane operativne liste dopunjuju `coverage={availableSince,partial}`. Za čuvanje koristiti konfigurabilnu politiku sa početnom vrijednošću 90 dana, uz odobrenje prije produkcije. Cleanup se odnosi samo na dnevnik API zahtjeva, nikada na XML, fiskalni indeks ili audit.

Bilježiti normalizovan šablon rute ili `unmatched`, bez raw URL-a, query sadržaja, headera, tijela, stack trace-a, putanja i tajni. `puHttpStatus` i `apiHttpStatus` su odvojeni. Validne PU kodove sačuvati; poruke prikazati samo nakon provjerenog maskiranja i ograničenja dužine (maks. 240 znakova), uz generičku poruku ako sadržaj nije bezbjedan. Nikad vraćati raw exception/XML. Strukturno evidentirati `error` na izvoru poslovne odluke, posebno kada status ne otkriva neuspjeh.

Postojeći audit prihvata i `correlationId` i `invoiceId` kao tačne filtere; postojeću semantiku vremena, paginacije i autentifikacije bez novih filtera ne mijenjati. Invoice ID se veže samo iz tipizovanog polja ili provjerenog postojećeg audit JSON ključa, uz provjeru firme. Više zapisa po istom Correlation ID-u je očekivano; veza sa konkretnim pokušajem koristi njegov ID, ne sam Correlation ID.

P2: zaštititi generisani OpenAPI i verzionisati ga uz isporuku. `route-catalog` vraća `{method,path,description,permissions,effect}`, sa `effect=ReadOnly|LocalWrite|SendsToPu`. Dokumentovati postojeće potvrde, ali bez pokretanja upisnih ruta ili automatskog odobrenja. Ne otvarati javni Swagger UI kao uslov.

## 6. Migracija, popuna istorije i puštanje

1. **Inventar i sigurnosna kopija**, tek uz odobrenje pristupa: provjeriti stvarni commit/image, migracije, broj redova `fiscal_exchanges`, skladišta/volume, raspone datuma, kapacitet i kompatibilnost. U pregledanom kodu aktivan je `FileFiscalExchangeStoreV5`; postojanje tabele nije dokaz da sadrži istoriju. Usklađena kopija baze i arhive razmjena, provjereno vraćanje na izolovanom okruženju. Bez brisanja ili pomjeranja originalnih dokaza.
2. **Aditivna migracija** poslije postojećih migracija. Dodati nullable kontekst pripreme/istorijski snapshot i porijeklo na računu, tipizovani `invoiceId` u auditu i novi operativni dnevnik. Proširiti `fiscal_exchanges` za pretražive događaje i snapshot pokušaja, s jedinstvenim postojećim ID-em razmjene. Očuvati eventualne postojeće redove i mapirati ih tek poslije inventara. Indeksi: račun `(companyId,environment,issueDateTime,id)`; fiskalni brojevi/IKOF/JIKR i payment veza; razmjene `(invoiceId,startedAt,id)`, `(companyId,environment,startedAt,id)`, `(correlationId,id)`; audit i zahtjevi po firmi, vremenu i Correlation ID-u. Za pretragu po dijelu naziva planirati odgovarajući indeks poslije `EXPLAIN` na realističnom skupu; veće indekse graditi bez dugog zaključavanja. Nema izmjene fiskalnog statusa ili mehanizma numeracije.
3. **Indeksiranje novih pokušaja** od kontrolisane tačke. Sačuvati XML + hash i zapis početka sa istim exchange ID prije transporta, prenositi poznat račun/kontekst iz pozivaoca. Ako osnovni dokaz ili obavezni upis početka ne uspije, ne započinjati slanje. Prvo sačuvati odgovor/grešku u trajnom skladištu, zatim idempotentno ažurirati indeks i odvojeno lokalni završetak. Kod timeout-a/prekida upis raditi kratkim nezavisnim tokenom; ako proces nestane ostaje otvoren zapis, ne lažno odbijanje. DB i fajlovi nisu jedna transakcija: pomiriti neusaglašene zapise istim offline alatom, bez ponovnog slanja. Operativni dnevnik mora obuhvatiti autentifikaciju, MVC validaciju, middleware i semantički ishod. Ako njegov upis padne, alarmirati i odložiti samo sanitizovani zapis u trajni lokalni red; nikada promijeniti rezultat već izvršene fiskalne operacije. Nove GET rute samo čitaju DB indeks.
4. **Ponovljivi backfill** kao zaseban interni alat, prvo `--dry-run`, pa eksplicitni `--apply` sa potvrđenom bazom i read-only arhivom. Pročitati metapodatke i XML u ograničenim paketima; provjeriti SHA-256 request/response, UUID para, tip poruke i fiskalne identifikatore. Siguran XML parser bez DTD-a/mreže, ograničenja veličine i putanje ispod korijena. Za `RegisterInvoice` vezu dokazati jedinstvenim IKOF-om i slaganjima PIB-a, fiskalnog broja/vremena i iznosa gdje su podaci raspoloživi; neslaganje ili više kandidata → karantin. Za TCR/depozit povezati firmu samo iz tačne provjerene operacije/konteksta, ne iz ID-a u URL-u. Correlation ID je samo pomoć. Okruženje: pouzdan snapshot ili potvrđen endpoint iz evidencije sa tačnim povezivanjem, alternativno dokazano istorijsko mapiranje jedinice/ENU-a; trenutna konfiguracija sama nije dokaz. Ne postoje istorijski API logovi za događaje koji nijesu sačuvani.
5. **Kontrola uvoza.** Identitet je `exchangeId`: upsert samo dopunjava potvrđene podatke, nikad ne prepisuje jači dokaz; duplikat sa drugim hash-om je konflikt za ručni pregled. Checkpoint poslije commitovanog paketa; restart od checkpoint-a plus kontrolni drugi prolaz od početka očekuje nula novih redova bez promjena izvora. Izvještaj: skenirano, validno, povezano/nepovezano, nepoznato okruženje, nepotpuno, korumpirano, konflikt, dodato/dopunjeno/preskočeno; bez XML-a i tajni. Nepotpuni zapis sa provjerenim requestom ostaje `RequestOnly` ili `Incomplete`; sumnjivi se izdvajaju bez tenant prikaza. Ne popravljati JIKR/status, ne okidati recovery, ne prepisivati originalne fajlove. Za live/backfill cutoff usaglasiti preklapanje po istom ključu prije uključivanja čitanja.
6. **Aktivacija po fazama.** Izolovana kopija → provjera migracije i dva backfill prolaza → autorizacioni i regresioni testovi → računovođa potvrđuje uzorak izvještaja → odvojeno odobren deploy, backfill i dodjela dozvola → tek onda UI. Ako treba povratak, isključiti čitanje/pozadinsko indeksiranje feature zastavom i vratiti kompatibilan servis; zadržati aditivnu šemu, logove, indeks i dokaze. Nikad destruktivni `Down` ili obnova stare baze preko novijih fiskalnih računa kao rutinski rollback.

## 7. Uslovi za prihvatanje

- Dvije firme i platformski klijent: provjeriti izostavljen `companyId`, direktne ID-eve, count, agregacije, audit, pokušaje, odbijene i nepripisane zahtjeve. Provjeriti da `platform:admin` bez konkretne dozvole ne otvara novu rutu.
- Filtri, stabilne stranice, prazni rezultati, ograničenja i realistične količine; zbir/drill-down po stopama do centa, ponoć, prelaz godine i oba DST prelaza. Prelaz okruženja ne mijenja klasifikaciju istorije.
- Prodaja i storno kroz mjesece; avans, konačni, umanjenje, obnova, nezavršen saldo; ORDER, sumarni, korekcija, poništenje/zamjena i nedovršeno usklađenje. Ne brojati status ili dokument dvaput.
- PU odbijanje sa HTTP 200, timeout, cancel, pad poslije uspjeha i recovery; bez sporednih efekata GET-a. Simulirati 400/401/403/409/500 i semantički neuspjeh, potvrditi redakciju tajni, tenant pripadnost i povezivanje Correlation ID-a.
- Dry-run bez upisa; ponovljen backfill i restart poslije pada; neslaganje hash-a, višeznačna veza, nepotpuni metapodaci, nepoznato okruženje i očuvanje dokaza. Provjeriti kompatibilnost starih API klijenata, troškove upita i migraciju na privremenom PostgreSQL-u. Live PU i produkcija nisu dio ovih provjera.
