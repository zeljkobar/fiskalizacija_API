# API_STANDARD.md

## 1. Osnovni standard

API koristi REST pristup sa JSON payload-ima prema klijentima. Interna komunikacija sa Poreskom upravom može koristiti XML/SOAP, ali to ne smije biti izloženo klijentima direktno.

## 2. Verzije

Svi endpointi imaju verziju:

```text
/api/v1/...
```

## 3. Standardni odgovor

```json
{
  "success": true,
  "data": {},
  "error": null,
  "correlationId": "..."
}
```

Za greške:

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "FISCALIZATION_FAILED",
    "message": "Fiskalizacija nije uspjela.",
    "details": []
  },
  "correlationId": "..."
}
```

## 4. Idempotency

Kritični endpointi moraju podržati idempotency key.

Primjer header-a:

```text
Idempotency-Key: 7f6f2a8d-5c5a-4e20-a4c3-...
```

Ovo sprječava duplu fiskalizaciju istog računa kod ponovljenog zahtjeva.

## 5. Correlation ID

Svaki zahtjev mora imati correlation id. Ako ga klijent ne pošalje, API ga generiše.

```text
X-Correlation-Id: ...
```

## 6. Autentifikacija

Za prvu verziju:

- JWT za web/mobile korisnike
- API key za sistemske integracije

Kasnije:

- OAuth2 / OpenID Connect
- per-client permissions
- scoped API keys

## 7. Fiskalni endpointi — prva verzija

```text
POST /api/v1/invoices/fiscalize
GET  /api/v1/invoices/{id}
GET  /api/v1/invoices/{id}/status
POST /api/v1/invoices/{id}/retry
GET  /api/v1/invoices/{id}/qr
```

## 8. Pravila grešaka

Greške moraju biti kodirane, ne samo tekstualne.

Primjeri:

```text
VALIDATION_ERROR
CERTIFICATE_NOT_FOUND
CERTIFICATE_EXPIRED
IIC_GENERATION_FAILED
XML_SIGNING_FAILED
PU_SERVICE_UNAVAILABLE
FISCALIZATION_REJECTED
JIKR_NOT_RECEIVED
DUPLICATE_INVOICE
```

