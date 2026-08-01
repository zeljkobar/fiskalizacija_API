# 08_ERROR_HANDLING.md

## Svrha

Ovaj dokument definiše standard za obradu grešaka u fiskalnom modulu.

## Kategorije grešaka

```text
1. ValidationError - greška prije slanja prema PU
2. CertificateError - greška sertifikata
3. SigningError - greška potpisivanja
4. XmlSchemaError - XML nije validan po XSD
5. TransportError - mrežna/SOAP greška
6. TaxAuthorityBusinessError - PU je odbila poruku iz poslovnog razloga
7. TaxAuthorityTechnicalError - PU/servis tehnički nije obradio poruku
8. PersistenceError - greška čuvanja u bazi
9. UnknownError - neočekivana greška
```

## Interni model greške

```csharp
public sealed class FiscalError
{
    public string Code { get; init; }
    public string Category { get; init; }
    public string Message { get; init; }
    public string UserMessage { get; init; }
    public bool IsRetryable { get; init; }
    public string? TaxAuthorityCode { get; init; }
    public string? TaxAuthorityMessage { get; init; }
    public string CorrelationId { get; init; }
}
```

## API response model

```json
{
  "success": false,
  "error": {
    "code": "FISCAL_XML_SCHEMA_INVALID",
    "message": "XML poruka nije validna prema šemi.",
    "correlationId": "...",
    "retryable": false
  }
}
```

## Pravila

```text
- Korisniku prikazati razumljivu poruku.
- Developeru/logu sačuvati tehnički detalj.
- PU raw odgovor čuvati u bazi.
- Ne otkrivati tajne, lozinke, privatne ključeve.
- Za poslovne greške ne raditi automatski retry.
```

## Početni interni error catalog

```text
FISCAL_VALIDATION_FAILED
FISCAL_CERTIFICATE_NOT_FOUND
FISCAL_CERTIFICATE_INVALID
FISCAL_IIC_GENERATION_FAILED
FISCAL_XML_BUILD_FAILED
FISCAL_XML_SCHEMA_INVALID
FISCAL_XML_SIGNING_FAILED
FISCAL_SOAP_TIMEOUT
FISCAL_SOAP_NETWORK_ERROR
FISCAL_TAX_AUTHORITY_REJECTED
FISCAL_TAX_AUTHORITY_UNAVAILABLE
FISCAL_RESPONSE_PARSE_FAILED
FISCAL_RETRY_EXHAUSTED
```

## Mapping PU grešaka

Nakon čitanja zvanične specifikacije napraviti tabelu:

```text
pu_error_code
pu_error_message
internal_error_code
category
retryable
user_message
recommended_action
```

Ova tabela ide u `fiscal_error_mappings`.
