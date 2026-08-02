#!/bin/sh
set -eu
umask 077

mkdir -p /backups
PGPASSWORD=$(cat /run/secrets/postgres_password)
export PGPASSWORD

until pg_isready -q; do
  echo "Waiting for host PostgreSQL..."
  sleep 5
done

while true; do
  timestamp=$(date -u +%Y%m%dT%H%M%SZ)
  target="/backups/$timestamp"
  temporary="$target.tmp"
  mkdir -p "$temporary"

  if pg_dump --format=custom --no-owner --no-privileges --file="$temporary/database.dump"; then
    tar -czf "$temporary/certificate-vault.tar.gz" -C /source certificates
    tar -czf "$temporary/fiscal-exchanges.tar.gz" -C /source exchanges
    mv "$temporary" "$target"
    chown -R "0:${BACKUP_GROUP_ID}" "$target"
    find "$target" -type d -exec chmod 0750 {} +
    find "$target" -type f -exec chmod 0640 {} +
    find /backups -mindepth 1 -maxdepth 1 -type d -mtime "+${BACKUP_RETENTION_DAYS}" -exec rm -rf -- {} +
    echo "Backup completed: $target"
  else
    echo "Database backup failed; incomplete backup retained at $temporary" >&2
  fi

  sleep 86400
done
