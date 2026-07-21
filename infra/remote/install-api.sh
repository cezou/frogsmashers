#!/usr/bin/env bash
# Install the FrogSmashers accounts API on the OCI VM: static ARM64
# binary (cross-compiled on the dev machine), dedicated system user,
# secrets env file, systemd service bound to localhost. Runs as root
# ON the VM with its sibling files in the same directory (shipped by
# infra/provision.sh). Idempotent; JWT_SECRET is generated once and
# never regenerated.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
: "${FROG_UGS_PROJECT_ID:?missing (exported by provision.sh)}"

[ -f /etc/frogsmashers/db.env ] || {
  echo "error: /etc/frogsmashers/db.env missing — run" \
    "'provision.sh postgres' first" >&2
  exit 1
}

if ! id -u frogsmashers-api >/dev/null 2>&1; then
  echo "==> creating system user frogsmashers-api"
  useradd --system --no-create-home --shell /usr/sbin/nologin \
    frogsmashers-api
fi

echo "==> installing binary + unit"
install -m 755 "$DIR/frogsmashers-api" /usr/local/bin/frogsmashers-api
install -m 644 "$DIR/frogsmashers-api.service" /etc/systemd/system/

install -d -m 755 /etc/frogsmashers
if [ ! -f /etc/frogsmashers/api.env ]; then
  echo "==> generating /etc/frogsmashers/api.env"
  umask 077
  cat > /etc/frogsmashers/api.env <<EOF
JWT_SECRET=$(openssl rand -hex 32)
UGS_PROJECT_ID=$FROG_UGS_PROJECT_ID
LISTEN_ADDR=127.0.0.1:8080
EOF
fi

systemctl daemon-reload
systemctl enable frogsmashers-api
systemctl restart frogsmashers-api

# shellcheck disable=SC1091
addr="$(. /etc/frogsmashers/api.env && echo "${LISTEN_ADDR:-127.0.0.1:8080}")"
echo "==> waiting for http://$addr/healthz"
for _ in $(seq 1 15); do
  if curl -fsS "http://$addr/healthz" 2>/dev/null; then
    echo
    echo "==> done"
    exit 0
  fi
  sleep 1
done

echo "error: API did not become healthy" >&2
systemctl status frogsmashers-api --no-pager || true
journalctl -u frogsmashers-api -n 30 --no-pager || true
exit 1
