#!/usr/bin/env bash
# FrogSmashers PostgreSQL backup: pg_dump + globals -> OCI Object Storage.
# Installed as /usr/local/bin/frogsmashers-pg-backup; run as user postgres
# by frogsmashers-pg-backup.service (daily timer). Auth: instance principal
# (no credentials on disk). Config: /etc/frogsmashers/backup.env.
set -euo pipefail

FROG_BACKUP_BUCKET="${FROG_BACKUP_BUCKET:-frogsmashers-db-backups}"
BACKUP_DIR=/var/backups/frogsmashers
DB=frogsmashers
OCI=/usr/local/bin/oci

ts=$(date -u +%Y%m%dT%H%M%SZ)
dump="$BACKUP_DIR/${DB}_${ts}.dump"
globals="$BACKUP_DIR/globals_${ts}.sql.gz"

echo "dumping $DB"
pg_dump -Fc -f "$dump" "$DB"
pg_dumpall --globals-only | gzip > "$globals"

export OCI_CLI_AUTH=instance_principal
ns=$("$OCI" os ns get --query data --raw-output)
for f in "$dump" "$globals"; do
  echo "uploading $(basename "$f") to $FROG_BACKUP_BUCKET"
  "$OCI" os object put -ns "$ns" -bn "$FROG_BACKUP_BUCKET" \
    --file "$f" --name "daily/$(basename "$f")" --force >/dev/null
done

find "$BACKUP_DIR" -type f -mtime +3 -delete

if [ -n "${HEALTHCHECK_URL:-}" ]; then
  curl -fsS --max-time 10 "$HEALTHCHECK_URL" >/dev/null || true
fi
echo "backup OK: daily/$(basename "$dump")"
