# OCI console checklist (manual, one-time)

Steps only the account owner can do, in the OCI web console
(<https://cloud.oracle.com>). Everything else is scripted (`README.md`).
Replace `<compartment>` with the compartment holding the VM (often the
root compartment). Never paste OCIDs/IPs into the repo.

## 1. Account type → Pay As You Go (LAUNCH BLOCKER)

Billing & Cost Management → **Upgrade and Manage Payment Method**.

- If it already shows **Pay As You Go**: done, skip to step 2.
- Else **Upgrade**: card required, ~$100 temporary authorization hold
  (released, not charged). Always Free resources stay free; PAYG removes
  idle-reclamation, removes the A1 over-provision deletion rule, and (per
  Oracle support, unofficial) keeps this pre-2026 4 OCPU/24 GB VM free.

## 2. Budget alert (bill = anomaly detector)

Billing & Cost Management → **Budgets** → Create Budget:

- Scope: root compartment. Monthly amount: **5** (EUR/USD).
- Alert rule: **1% of actual spend** → email you. On a tenancy meant to
  run free, any nonzero bill must alert immediately.

## 3. Object Storage bucket

Storage → Object Storage → **Create Bucket**:

- Name: **`frogsmashers-db-backups`** — Standard tier — visibility
  **private** (default; never enable public access) — same
  `<compartment>` as the VM.

## 4. Dynamic group (lets the VM authenticate without keys)

Identity & Security → Domains → Default → **Dynamic Groups** → Create:

- Name: `frogsmashers-backend-dg`.
- Matching rule (instance OCID: Compute → Instances →
  `instance-20250330-1459` → copy OCID):

  ```
  Any {instance.id = '<instance OCID>'}
  ```

## 5. IAM policy (scoped to the one bucket)

Identity & Security → **Policies** → Create Policy `frogsmashers-backup-policy`
in `<compartment>`, statements:

```
Allow dynamic-group frogsmashers-backend-dg to read buckets in compartment <compartment> where target.bucket.name='frogsmashers-db-backups'
Allow dynamic-group frogsmashers-backend-dg to manage objects in compartment <compartment> where target.bucket.name='frogsmashers-db-backups'
Allow dynamic-group frogsmashers-backend-dg to read objectstorage-namespaces in tenancy
```

## 6. Bucket lifecycle rule (60-day retention)

First add the policy statement the Object Storage service itself needs
(same Policies screen; `<region-id>` e.g. `eu-paris-1` — shown in the
console URL/top bar):

```
Allow service objectstorage-<region-id> to manage object-family in compartment <compartment>
```

Then: bucket `frogsmashers-db-backups` → **Lifecycle Policy Rules** →
Create Rule: action **Delete**, target objects, **60 days**.

## 7. Done

IAM/dynamic-group changes can take **~10 minutes** to propagate. Then say
"console done" and the agent runs `provision.sh backup` + `verify`.
