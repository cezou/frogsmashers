#!/usr/bin/env bash
# Install the FrogSmashers backup system on the OCI VM: OCI CLI (venv),
# backup script, systemd service + timer. Runs as root ON the VM with its
# sibling files in the same directory (shipped by infra/provision.sh).
# Idempotent. Uses a systemd timer — NEVER the user crontab.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! /usr/local/bin/oci --version >/dev/null 2>&1; then
  echo "==> installing OCI CLI in /opt/oci-cli (venv, aarch64 wheels)"
  apt-get update -q
  apt-get install -y -q python3-venv
  python3 -m venv /opt/oci-cli
  /opt/oci-cli/bin/pip install -q --upgrade pip
  /opt/oci-cli/bin/pip install -q oci-cli
  ln -sf /opt/oci-cli/bin/oci /usr/local/bin/oci
else
  echo "==> OCI CLI already installed: $(/usr/local/bin/oci --version)"
fi

echo "==> installing backup script + units"
install -d -o postgres -g postgres -m 700 /var/backups/frogsmashers
install -m 755 "$DIR/pg-backup.sh" /usr/local/bin/frogsmashers-pg-backup
install -m 644 "$DIR/frogsmashers-pg-backup.service" /etc/systemd/system/
install -m 644 "$DIR/frogsmashers-pg-backup.timer" /etc/systemd/system/

install -d -m 755 /etc/frogsmashers
if [ ! -f /etc/frogsmashers/backup.env ]; then
  umask 077
  cat > /etc/frogsmashers/backup.env <<'EOF'
FROG_BACKUP_BUCKET=frogsmashers-db-backups
# Optional healthchecks.io dead-man switch, pinged only on success:
#HEALTHCHECK_URL=
EOF
fi

systemctl daemon-reload
systemctl enable --now frogsmashers-pg-backup.timer
echo "==> done"
systemctl list-timers frogsmashers-pg-backup.timer --no-pager
