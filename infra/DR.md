# Disaster recovery & fallbacks

All commands run on the VM unless noted. Auth for `oci`:
`export OCI_CLI_AUTH=instance_principal`. Bucket:
`frogsmashers-db-backups` (private, 60-day retention).

## 1. Restore procedure (test once at setup, then quarterly)

Fetch the latest dump and restore it into a scratch database:

```bash
export OCI_CLI_AUTH=instance_principal
ns=$(oci os ns get --query data --raw-output)
latest=$(oci os object list -ns "$ns" -bn frogsmashers-db-backups \
  --prefix daily/frogsmashers_ \
  --query 'sort_by(data,&"time-created")[-1].name' --raw-output)
oci os object get -ns "$ns" -bn frogsmashers-db-backups \
  --name "$latest" --file /tmp/restore-test.dump

sudo -u postgres createdb -O frogsmashers frogsmashers_restore_test
sudo -u postgres pg_restore --no-owner --role=frogsmashers \
  -d frogsmashers_restore_test /tmp/restore-test.dump
sudo -u postgres psql -d frogsmashers_restore_test -c '\dt'  # sanity
sudo -u postgres dropdb frogsmashers_restore_test
rm /tmp/restore-test.dump
```

**Full-loss scenario** (VM reclaimed/rebuilt): re-run
`provision.sh postgres`, then restore the **globals first** (recreates the
role), then the dump:

```bash
oci os object get … --name daily/globals_<ts>.sql.gz --file - |
  gunzip | sudo -u postgres psql
sudo -u postgres pg_restore --no-owner --role=frogsmashers \
  -d frogsmashers /tmp/…dump
```

Note: `/etc/frogsmashers/db.env` is not backed up (secret). After a full
loss, set a fresh password: `ALTER ROLE frogsmashers PASSWORD '…'` and
rewrite `db.env`.

## 2. Fallback: managed Postgres (Neon / Supabase free tier)

Kept open by design — plain PostgreSQL means migrating off OCI is one
dump/restore, no lock-in (`docs/research/STEAM.md` §3.2):

1. Create a Neon or Supabase project with PostgreSQL **≥ 17** (restore
   target major must be ≥ the dump's source major; use a `pg_restore`
   binary ≥ both).
2. `pg_restore --no-owner --no-privileges -d "$MANAGED_URL" <latest.dump>`
   (run from any machine that can reach both the dump and the provider).
3. Repoint the API: replace `DATABASE_URL` in `/etc/frogsmashers/db.env`
   with the provider's URL. The app never knows anything OCI-specific.

## 3. Contingency: resize to 2 OCPU / 12 GB

Only if the PAYG "keep 4/24 free" arrangement fails (budget alert fires)
or Oracle forces the post-2026-06-15 allowance:

1. Console → Compute → Instances → `instance-20250330-1459` → **Edit** →
   shape A1.Flex, **2 OCPU / 12 GB** → save. One reboot, ~2–5 min
   downtime — the unrelated personal services (nginx, pm2, RustDesk,
   Docker) go down with it, plan accordingly.
2. Halve the PostgreSQL tuning in
   `/etc/postgresql/17/main/conf.d/10-frogsmashers.conf`:
   `shared_buffers = 1GB`, `effective_cache_size = 3GB`, then
   `systemctl restart postgresql@17-main`.
3. ⚠️ Irreversible in practice: once at 2/12, recreating anything above
   the new Always Free allowance may be impossible on a free-quota basis.
