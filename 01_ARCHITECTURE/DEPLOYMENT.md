# DEPLOYMENT.md

## 1. Preporučeni deployment

- Linux VPS
- Docker
- PostgreSQL
- Redis
- Nginx reverse proxy
- HTTPS obavezno

## 2. Okruženja

```text
Development
Testing
Staging
Production
```

## 3. Varijable okruženja

- DATABASE_URL
- REDIS_URL
- JWT_SECRET
- CERTIFICATE_STORAGE_KEY
- PU_TEST_ENDPOINT
- PU_PRODUCTION_ENDPOINT

## 4. Backup

- dnevni backup baze
- backup sertifikata
- backup request/response logova
- odvojeno čuvanje produkcionih tajni

