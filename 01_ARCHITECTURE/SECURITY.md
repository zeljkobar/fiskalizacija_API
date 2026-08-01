# SECURITY.md

## 1. Osnovni cilj

Zaštititi fiskalne sertifikate, privatne ključeve, korisničke naloge, API pristup i fiskalne podatke.

## 2. Sertifikati

- Privatni ključ se nikad ne čuva kao običan tekst.
- Sertifikat se čuva šifrovano.
- Lozinka sertifikata se čuva kroz secret manager ili šifrovano polje.
- Pristup sertifikatu se audit-uje.

## 3. Autentifikacija

- Web/mobile: JWT + refresh token.
- Integracije: API key.
- Admin funkcije: dodatne dozvole.

## 4. Autorizacija

Koristiti RBAC:

- SuperAdmin
- CompanyAdmin
- Accountant
- Operator
- ReadOnly
- IntegrationClient

## 5. Audit

Audit log mora evidentirati:

- login
- promjenu sertifikata
- fiskalizaciju
- retry
- storno
- promjene poslovnih prostora
- promjene ENU
- promjene korisnika

## 6. Rate limiting

API mora imati rate limiting po korisniku i API key-u.

