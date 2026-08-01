# DATABASE.md

## 1. Baza

Primarna baza: PostgreSQL.

## 2. Osnovna pravila

- Koristiti UUID kao primarne ključeve gdje ima smisla.
- Svaka tabela ima `created_at`, `updated_at`.
- Kritične tabele imaju `created_by`, `updated_by`.
- Fiskalne tabele ne koristiti hard delete.
- Request/response log mora čuvati originalne XML poruke.

## 3. Početne tabele

```text
companies
business_units
fiscal_devices
operators
certificates
invoices
invoice_items
invoice_payments
invoice_taxes
fiscal_request_logs
fiscal_response_logs
fiscal_errors
retry_queue
audit_logs
users
roles
permissions
api_clients
```

## 4. invoices

Sadrži zaglavlje računa.

Ključna polja:

- id
- company_id
- business_unit_id
- fiscal_device_id
- operator_id
- invoice_number
- issue_datetime
- invoice_type
- payment_type
- total_amount
- total_vat
- iic
- jikr
- fiscalization_status
- qr_code_content
- created_at

## 5. fiscal_request_logs

Mora čuvati:

- invoice_id
- request_type
- xml_content
- signed_xml_content
- endpoint
- sent_at
- correlation_id

## 6. fiscal_response_logs

Mora čuvati:

- invoice_id
- response_xml
- status_code
- jikr
- error_code
- error_message
- received_at
- correlation_id

