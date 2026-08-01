# 07_OFFLINE_AND_RETRY.md

## Svrha

Ovaj dokument definiše offline režim i retry logiku.

## Razlika između offline i retry

```text
Offline režim = račun je izdat kada fiskalni servis / internet nije dostupan i mora se naknadno prijaviti.
Retry režim = sistem je pokušao da pošalje poruku, ali je došlo do tehničke greške, timeout-a ili privremenog problema.
```

Ne smiju se miješati poslovne greške i tehničke greške.

## Poslovna greška

Ako PU odbije račun zbog neispravnih podataka, automatski retry je zabranjen.

Primjeri:

```text
- nevalidan XML
- pogrešan PIB
- pogrešan format računa
- neispravan poslovni prostor
- neispravna poreska stopa
```

## Tehnička greška

Retry je dozvoljen za:

```text
- timeout
- privremeni network error
- HTTP 5xx
- DNS problem
- servis nedostupan
```

## Offline statusi

```text
ONLINE
OFFLINE_ISSUED
OFFLINE_PENDING_SYNC
OFFLINE_SYNC_IN_PROGRESS
OFFLINE_SYNC_SUCCESS
OFFLINE_SYNC_FAILED
OFFLINE_DEADLINE_RISK
```

## Retry statusi

```text
PENDING
IN_PROGRESS
SUCCESS
FAILED_TEMPORARY
FAILED_PERMANENT
RETRY_EXHAUSTED
```

## Tabele

`offline_invoices`:

```text
id
invoice_id
company_id
reason
offline_started_at
issued_at
sync_deadline_at
synced_at
status
created_at
```

`fiscal_retry_queue`:

```text
id
fiscal_request_id
invoice_id
company_id
attempt_number
max_attempts
next_attempt_at
last_error_code
last_error_message
status
created_at
updated_at
```

## Retry algoritam

```text
1. Pokušaj odmah.
2. Ako je tehnička greška, zakazati retry.
3. Koristiti exponential backoff.
4. Nakon maksimalnog broja pokušaja, označiti kao RETRY_EXHAUSTED.
5. Administrator mora imati dashboard za ručni pregled.
```

Primjer:

```text
1. retry: 1 minut
2. retry: 5 minuta
3. retry: 15 minuta
4. retry: 1 sat
5. retry: 6 sati
```

## Idempotency u retry režimu

Retry nikad ne smije proizvesti drugi poslovni račun. Ponovno slanje mora koristiti isti interni račun i istu idempotency logiku.

## Worker

Koristiti `Summa.Fiscal.Worker`:

```text
- Hangfire za MVP
- kasnije RabbitMQ / MassTransit ako sistem poraste
```

## Audit

Za svaki retry čuvati:

```text
- attempt number
- vrijeme pokušaja
- raw request hash
- raw response
- grešku
- trajanje
```
