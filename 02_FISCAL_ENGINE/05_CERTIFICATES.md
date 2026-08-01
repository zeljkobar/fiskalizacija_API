# 05_CERTIFICATES.md

## Svrha

Ovaj dokument opisuje upravljanje sertifikatima za elektronsku fiskalizaciju u `SUMMA_FISCAL_PLATFORM`.

## Uloge sertifikata

Sertifikat se koristi za:

```text
- identifikaciju poreskog obveznika
- digitalno potpisivanje poruka
- generisanje/izračun IKOF/IIC ako algoritam koristi privatni ključ
- dokazivanje integriteta poslatih podataka
```

## Podržani modeli

### MVP

```text
- upload PFX/P12 sertifikata
- unos lozinke
- enkriptovano čuvanje
- aktiviranje sertifikata po firmi
```

### Kasnije

```text
- OS certificate store
- smart card / token
- HSM
- eksterni signing servis
```

## Domenski model

```text
Company
    Certificates
        CertificateUsages
        CertificateAccessLogs
```

Jedna firma može imati više sertifikata kroz vrijeme, ali samo jedan aktivan sertifikat za fiskalizaciju u datom trenutku.

## Validacije pri uploadu

```text
1. Fajl mora biti validan PFX/P12.
2. Lozinka mora otključati sertifikat.
3. Sertifikat mora imati privatni ključ.
4. Sertifikat ne smije biti istekao.
5. Subject/issuer podaci moraju biti sačuvani.
6. Thumbprint mora biti jedinstven po firmi.
```

## API endpointi

```text
POST /api/v1/companies/{companyId}/certificates
GET  /api/v1/companies/{companyId}/certificates
GET  /api/v1/companies/{companyId}/certificates/{certificateId}
POST /api/v1/companies/{companyId}/certificates/{certificateId}/activate
POST /api/v1/companies/{companyId}/certificates/{certificateId}/deactivate
```

## Čuvanje

Ne čuvati PFX u bazi kao običan byte array bez enkripcije. Opcije:

```text
1. Encrypted file storage + metadata in DB
2. PostgreSQL bytea encrypted at application level
3. Secret vault / object storage
```

Za MVP:

```text
storage/certificates/{companyId}/{certificateId}.pfx.enc
```

## Rotacija

Sertifikat mora imati:

```text
valid_from
valid_to
is_active
activated_at
deactivated_at
```

Ako se sertifikat promijeni, stari ostaje u sistemu zbog fiskalizovanih računa iz perioda kada je korišćen.

## Upozorenja

Sistem treba da upozori:

```text
- 60 dana prije isteka
- 30 dana prije isteka
- 15 dana prije isteka
- 7 dana prije isteka
- na dan isteka
```

## Greške

```text
CERT_UPLOAD_INVALID_FILE
CERT_UPLOAD_INVALID_PASSWORD
CERT_UPLOAD_NO_PRIVATE_KEY
CERT_UPLOAD_EXPIRED
CERT_UPLOAD_DUPLICATE_THUMBPRINT
CERT_ACTIVATE_NOT_FOUND
CERT_ACTIVATE_EXPIRED
CERT_ACTIVATE_COMPANY_MISMATCH
```
