#!/usr/bin/env bash
# FrogSmashers backend host provisioning wrapper (run from the dev machine).
# Sources the gitignored repo-root .env (OCI_IP / OCI_USER / OCI_SSH_KEY);
# never embeds secrets. See infra/README.md.
#
# Usage: ./infra/provision.sh {postgres|backup|verify}
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE_DIR="$REPO_ROOT/infra/remote"

usage() {
  echo "Usage: $0 {postgres|backup|verify}" >&2
  exit 2
}

[ $# -eq 1 ] || usage
cmd="$1"

[ -f "$REPO_ROOT/.env" ] || {
  echo "error: $REPO_ROOT/.env not found (see AGENTS.md § OCI server)" >&2
  exit 1
}
set -a
# shellcheck disable=SC1091
. "$REPO_ROOT/.env"
set +a
: "${OCI_IP:?missing in .env}" "${OCI_USER:?missing in .env}" \
  "${OCI_SSH_KEY:?missing in .env}"

key="${OCI_SSH_KEY/#\~/$HOME}"
if [ ! -f "$key" ] && [ -n "${OCI_SSH_KEY_B64:-}" ]; then
  echo "recreating SSH key at $key from OCI_SSH_KEY_B64"
  umask 077
  echo "$OCI_SSH_KEY_B64" | base64 -d > "$key"
fi
[ -f "$key" ] || { echo "error: SSH key $key not found" >&2; exit 1; }

SSH=(ssh -i "$key" -o ConnectTimeout=10 "$OCI_USER@$OCI_IP")

case "$cmd" in
postgres)
  "${SSH[@]}" 'sudo bash -s' < "$REMOTE_DIR/install-postgres.sh"
  ;;
backup)
  tar -C "$REMOTE_DIR" -cf - \
    install-backup.sh pg-backup.sh \
    frogsmashers-pg-backup.service frogsmashers-pg-backup.timer |
    "${SSH[@]}" 'd=$(mktemp -d) && tar -xf - -C "$d" &&
      sudo bash "$d/install-backup.sh"; rc=$?; rm -rf "$d"; exit $rc'
  ;;
verify)
  "${SSH[@]}" '
    set -e
    echo "--- postgres service ---"
    systemctl is-active postgresql@17-main
    echo "--- 5432 bound to localhost only ---"
    ss -ltn | awk "\$4 ~ /:5432\$/ {print \$4}"
    echo "--- app-role smoke test ---"
    sudo bash -c ". /etc/frogsmashers/db.env && psql \"\$DATABASE_URL\" \
      -qc \"CREATE TABLE smoke(id int); INSERT INTO smoke VALUES (1);
            SELECT count(*) FROM smoke; DROP TABLE smoke;\""
    echo "--- backup timer ---"
    systemctl list-timers frogsmashers-pg-backup.timer --no-pager || true
    echo "--- last backup run ---"
    journalctl -u frogsmashers-pg-backup -n 20 --no-pager || true
    echo "--- objects in bucket ---"
    OCI_CLI_AUTH=instance_principal /usr/local/bin/oci os object list \
      -bn "${FROG_BACKUP_BUCKET:-frogsmashers-db-backups}" \
      --query "data[].name" --output table 2>&1 | tail -20 || true
    echo "--- pre-existing services untouched ---"
    systemctl is-active nginx docker
    echo "--- disk ---"
    df -h / | tail -1
  '
  ;;
*)
  usage
  ;;
esac
