#!/usr/bin/env bash
set -Eeuo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
deploy_dir="$repo_dir/deploy"
compose_file="$deploy_dir/compose.production.yml"
env_file="$deploy_dir/.env"
health_url="http://127.0.0.1:${SUMMA_API_PORT:-8585}/health"

cd "$repo_dir"

for command_name in git docker curl flock; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "Nedostaje obavezna komanda: $command_name" >&2
    exit 1
  }
done

test -d .git || { echo "Direktorijum nije Git repozitorijum: $repo_dir" >&2; exit 1; }
test -f "$compose_file" || { echo "Nedostaje $compose_file" >&2; exit 1; }
test -f "$env_file" || { echo "Nedostaje $env_file" >&2; exit 1; }
test -d "$deploy_dir/local-secrets" || { echo "Nedostaje local-secrets direktorijum" >&2; exit 1; }
test -d "$deploy_dir/data" || { echo "Nedostaje data direktorijum" >&2; exit 1; }
test -d "$deploy_dir/backups" || { echo "Nedostaje backups direktorijum" >&2; exit 1; }

exec 9>"$deploy_dir/.deploy.lock"
flock -n 9 || { echo "Drugi SUMMA deployment je već u toku." >&2; exit 1; }

if [ "$(git branch --show-current)" != "main" ]; then
  echo "Deployment je dozvoljen samo sa main grane." >&2
  exit 1
fi

if [ -n "$(git status --porcelain --untracked-files=normal)" ]; then
  echo "Git radni direktorijum nije čist. Deployment je zaustavljen." >&2
  git status --short
  exit 1
fi

compose() {
  docker compose --env-file "$env_file" -f "$compose_file" "$@"
}

echo "Dohvatam stanje origin/main..."
git fetch origin main

current_commit="$(git rev-parse HEAD)"
target_commit="$(git rev-parse origin/main)"

if [ "$current_commit" = "$target_commit" ]; then
  echo "Server je već na posljednjem commitu: ${current_commit:0:12}"
  compose ps
  curl --fail --silent --show-error "$health_url"
  echo
  exit 0
fi

if ! git merge-base --is-ancestor "$current_commit" "$target_commit"; then
  echo "origin/main nije fast-forward nastavak trenutnog server commita." >&2
  exit 1
fi

echo "Pravim neposredni backup prije deploymenta..."
compose run --rm --no-deps -e BACKUP_ONCE=1 backup

latest_dump="$(find "$deploy_dir/backups" -mindepth 2 -maxdepth 2 -name database.dump -type f -printf '%T@ %p\n' | sort -n | tail -n 1 | cut -d' ' -f2-)"
test -n "$latest_dump" && test -s "$latest_dump" || {
  echo "Novi database.dump nije pronađen. Deployment je zaustavljen." >&2
  exit 1
}
echo "Backup potvrđen: $latest_dump"

echo "Ažuriram kod na ${target_commit:0:12}..."
git pull --ff-only origin main

echo "Provjeravam i gradim Docker image-e..."
compose config --quiet
compose build api worker

echo "Pokrećem API, Worker i backup..."
compose up -d api worker backup

echo "Čekam health provjeru..."
healthy=0
for _ in $(seq 1 45); do
  if curl --fail --silent --show-error "$health_url" >/dev/null 2>&1; then
    healthy=1
    break
  fi
  sleep 2
done

if [ "$healthy" != "1" ]; then
  echo "API nije postao zdrav. Posljednji logovi:" >&2
  compose logs --tail=120 api worker
  exit 1
fi

compose ps
curl --fail --show-error "$health_url"
echo
echo "Deployment završen: $(git rev-parse --short HEAD)"
