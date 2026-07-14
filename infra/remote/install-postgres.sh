#!/usr/bin/env bash
# Install and configure PostgreSQL 17 for the FrogSmashers backend.
# Runs as root ON the OCI VM (piped over SSH by infra/provision.sh).
# Idempotent: safe to re-run; never regenerates an existing password.
# Localhost-only: no firewall/UFW change, no new inbound port.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

PG_MAJOR=17
APP=frogsmashers
CONF_DIR="/etc/postgresql/$PG_MAJOR/main/conf.d"
DB_ENV=/etc/frogsmashers/db.env

if ! ls /etc/apt/sources.list.d/pgdg* >/dev/null 2>&1; then
  echo "==> adding apt.postgresql.org (PGDG) repository"
  apt-get update -q
  apt-get install -y -q postgresql-common
  /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y
else
  echo "==> PGDG repository already present"
fi

echo "==> installing postgresql-$PG_MAJOR (pinned major, native arm64)"
apt-get update -q
apt-get install -y -q "postgresql-$PG_MAJOR" "postgresql-client-$PG_MAJOR"

echo "==> writing config drop-in (conservative tuning, shared host)"
install -d -m 755 "$CONF_DIR"
cat > "$CONF_DIR/10-frogsmashers.conf" <<'EOF'
# FrogSmashers backend — managed by infra/remote/install-postgres.sh.
# Deliberately conservative: this VM also runs unrelated services.
listen_addresses = 'localhost'
max_connections = 50
shared_buffers = 2GB
effective_cache_size = 6GB
work_mem = 16MB
maintenance_work_mem = 256MB
wal_compression = on
checkpoint_completion_target = 0.9
random_page_cost = 1.1
EOF
chown postgres:postgres "$CONF_DIR/10-frogsmashers.conf"

systemctl enable --now "postgresql@$PG_MAJOR-main" >/dev/null 2>&1 || true
systemctl reload "postgresql@$PG_MAJOR-main" || \
  systemctl restart "postgresql@$PG_MAJOR-main"
pending=$(sudo -u postgres psql -tAc \
  "SELECT count(*) FROM pg_settings WHERE pending_restart" || echo 0)
if [ "$pending" != "0" ]; then
  echo "==> restart-required settings changed (e.g. shared_buffers);" \
    "restarting cluster"
  systemctl restart "postgresql@$PG_MAJOR-main"
fi

role_exists=$(sudo -u postgres psql -tAc \
  "SELECT 1 FROM pg_roles WHERE rolname='$APP'" || true)
if [ "$role_exists" != "1" ]; then
  echo "==> creating role '$APP' (password generated here, stored VM-side)"
  pw=$(openssl rand -hex 24)
  sudo -u postgres psql -v ON_ERROR_STOP=1 -qc \
    "CREATE ROLE $APP LOGIN PASSWORD '$pw' \
     NOSUPERUSER NOCREATEDB NOCREATEROLE;"
  install -d -m 755 /etc/frogsmashers
  umask 077
  printf 'DATABASE_URL=postgres://%s:%s@127.0.0.1:5432/%s\n' \
    "$APP" "$pw" "$APP" > "$DB_ENV"
  echo "==> credentials written to $DB_ENV (root:root, 600)"
else
  echo "==> role '$APP' already exists"
  [ -f "$DB_ENV" ] || echo "WARNING: $DB_ENV missing; reset the password" \
    " manually (ALTER ROLE) and recreate it" >&2
fi

db_exists=$(sudo -u postgres psql -tAc \
  "SELECT 1 FROM pg_database WHERE datname='$APP'" || true)
if [ "$db_exists" != "1" ]; then
  echo "==> creating database '$APP'"
  sudo -u postgres createdb -O "$APP" "$APP"
  sudo -u postgres psql -v ON_ERROR_STOP=1 -qc \
    "REVOKE ALL ON DATABASE $APP FROM PUBLIC;"
else
  echo "==> database '$APP' already exists"
fi

echo "==> done; 5432 listeners:"
ss -ltn | awk '$4 ~ /:5432$/ {print "   " $4}'
