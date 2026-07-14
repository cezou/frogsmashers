# infra/ — FrogSmashers backend host (OCI)

Provisioning for the accounts/ELO backend host (issue #26): PostgreSQL 17
on the existing OCI A1 ARM64 VM, plus daily `pg_dump` backups to OCI
Object Storage. Research basis: `docs/research/STEAM.md` §3.

## Architecture

```
dev machine (WSL) ── ssh (.env: OCI_IP/OCI_USER/OCI_SSH_KEY) ──▶ OCI VM
                                                                 │
   future backend API (Node/Go, same host) ── 127.0.0.1:5432 ──▶ PostgreSQL 17
                                                                 │ daily 04:30 UTC
                                          systemd timer ── pg_dump ─▶ Object
                                          (instance principal)       Storage
```

- **PostgreSQL 17** (PGDG, pinned major, native arm64), **localhost-only**
  — no new inbound port, no firewall change. App role/db: `frogsmashers`.
- **Credentials never in the repo**: the VM IP lives only in the gitignored
  root `.env`; the DB password is generated on the VM and stored only in
  `/etc/frogsmashers/db.env` (600) as a ready-to-use `DATABASE_URL` for the
  future API service.
- **Backups**: `frogsmashers-pg-backup.timer` (systemd — the user crontab
  belongs to unrelated services and is never touched) runs `pg_dump -Fc` +
  `pg_dumpall --globals-only`, uploads to the private bucket
  `frogsmashers-db-backups` via **instance principal** (no secrets on the
  VM), keeps 3 days locally (small boot volume), 60 days in the bucket
  (lifecycle rule). Optional healthchecks.io dead-man switch in
  `/etc/frogsmashers/backup.env`.
- The VM is **shared** with unrelated personal services (nginx, Docker,
  pm2, RustDesk): everything here is namespaced `frogsmashers-*`,
  PostgreSQL tuning is deliberately conservative, and scripts never
  restart anything but PostgreSQL itself.

## How to run

Prerequisite: root `.env` present (see `AGENTS.md` § OCI server).

1. **Manual console steps first** (one-time): `CONSOLE-CHECKLIST.md` —
   PAYG upgrade (launch blocker), budget alert, bucket, dynamic group,
   IAM policies, lifecycle rule.
2. `./infra/provision.sh postgres` — install/configure PostgreSQL
   (idempotent; safe to re-run).
3. `./infra/provision.sh backup` — install OCI CLI + backup script +
   timer (idempotent). Allow ~10 min after the console IAM steps before
   the first upload works.
4. First run + checks: `./infra/provision.sh verify`, and run the backup
   once by hand: `ssh … sudo systemctl start frogsmashers-pg-backup`.

## Verification checklist

- `systemctl is-active postgresql@17-main` → `active`; `ss -ltn` shows
  5432 on `127.0.0.1`/`[::1]` **only**.
- Smoke test as the app role (part of `provision.sh verify`).
- `journalctl -u frogsmashers-pg-backup` ends with `backup OK`; the bucket
  lists `daily/frogsmashers_<ts>.dump` + `daily/globals_<ts>.sql.gz`.
- Restore test into a scratch DB: `DR.md` §1 (do it once now, then
  quarterly).
- Pre-existing services untouched: `nginx`/`docker` active, `pm2 ls`
  unchanged, `crontab -l` unchanged, `df -h /` under ~60%.

## Disaster recovery / fallback

See `DR.md`: restore procedure, migration to managed Postgres
(Neon/Supabase) — kept a `pg_dump`/`pg_restore` away by design — and the
2 OCPU/12 GB resize contingency.

## Free-tier context (July 2026)

Oracle halved the Always Free A1 allowance to 2 OCPU/12 GB on 2026-06-15.
This VM predates the cut (4 OCPU/24 GB). Decision on file: keep 4/24,
upgrade the account to **Pay As You Go** (Oracle support states PAYG
tenancies keep 4/24 free; not officially documented), and guard with a
low budget alert — any nonzero bill triggers an email, and the resize
contingency in `DR.md` is the fallback. PAYG also removes idle-reclamation
and the over-provision deletion rule — it is a **launch blocker** together
with these backups.
