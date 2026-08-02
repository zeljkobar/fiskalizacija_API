#!/bin/sh
set -eu

load_secret() {
  variable_name="$1"
  secret_path="$2"
  if [ ! -r "$secret_path" ]; then
    echo "Required secret is missing or unreadable: $secret_path" >&2
    exit 1
  fi
  secret_value=$(cat "$secret_path")
  export "$variable_name=$secret_value"
}

if [ -n "${SUMMA_LOAD_API_SECRETS:-}" ]; then
  load_secret ConnectionStrings__FiscalDatabase /run/secrets/database_connection
  load_secret ApiAccess__BootstrapAdminKey /run/secrets/bootstrap_admin_key
  load_secret Fiscalization__CertificateVault__MasterKeyBase64 /run/secrets/certificate_vault_key
  load_secret Fiscalization__DevelopmentCertificate__Password /run/secrets/fiscal_certificate_password
fi

if [ -n "${SUMMA_LOAD_WORKER_SECRETS:-}" ]; then
  load_secret ConnectionStrings__FiscalDatabase /run/secrets/database_connection
fi

exec "$@"
