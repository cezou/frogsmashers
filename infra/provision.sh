#!/usr/bin/env bash
# FrogSmashers backend host provisioning wrapper (run from the dev machine).
# Sources the gitignored repo-root .env (OCI_IP / OCI_USER / OCI_SSH_KEY);
# never embeds secrets. See infra/README.md.
#
# Usage: ./infra/provision.sh {postgres|backup|api|verify}
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE_DIR="$REPO_ROOT/infra/remote"

usage() {
  echo "Usage: $0 {postgres|backup|api|verify}" >&2
  exit 2
}

# Cross-compile the accounts API to a static linux/arm64 binary in
# infra/remote/ (gitignored). Uses the local Go toolchain, falling
# back to the golang docker image — nothing is ever built on the VM.
build_api() {
  local go_bin
  go_bin="$(command -v go || true)"
  [ -z "$go_bin" ] && [ -x "$HOME/.local/go/bin/go" ] &&
    go_bin="$HOME/.local/go/bin/go"
  echo "==> building frogsmashers-api (linux/arm64)"
  if [ -n "$go_bin" ]; then
    (cd "$REPO_ROOT/api" &&
      CGO_ENABLED=0 GOOS=linux GOARCH=arm64 "$go_bin" build \
        -trimpath -ldflags='-s -w' \
        -o "$REMOTE_DIR/frogsmashers-api" ./cmd/api)
  else
    docker run --rm -v "$REPO_ROOT/api":/src -w /src \
      -e CGO_ENABLED=0 -e GOOS=linux -e GOARCH=arm64 \
      golang:1.26 go build -trimpath -ldflags='-s -w' \
      -o /src/frogsmashers-api ./cmd/api
    mv "$REPO_ROOT/api/frogsmashers-api" "$REMOTE_DIR/frogsmashers-api"
  fi
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
api)
  build_api
  ugs_project_id="$(grep -oE 'cloudProjectId: [0-9a-f-]+' \
    "$REPO_ROOT/HohimBrueh/ProjectSettings/ProjectSettings.asset" |
    awk '{print $2}')"
  [ -n "$ugs_project_id" ] || {
    echo "error: cloudProjectId not found in ProjectSettings.asset" >&2
    exit 1
  }
  tar -C "$REMOTE_DIR" -cf - \
    frogsmashers-api install-api.sh frogsmashers-api.service |
    "${SSH[@]}" "d=\$(mktemp -d) && tar -xf - -C \"\$d\" &&
      sudo FROG_UGS_PROJECT_ID=$ugs_project_id \
        bash \"\$d/install-api.sh\"; rc=\$?; rm -rf \"\$d\"; exit \$rc"
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
    echo "--- api service ---"
    systemctl is-active frogsmashers-api || true
    echo "--- api health (localhost only) ---"
    sudo bash -c ". /etc/frogsmashers/api.env 2>/dev/null;
      curl -fsS \"http://\${LISTEN_ADDR:-127.0.0.1:8080}/healthz\"" \
      && echo || true
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
