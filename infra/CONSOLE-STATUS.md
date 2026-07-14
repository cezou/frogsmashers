# OCI console checklist — statut (maj 2026-07-18)

Région `eu-paris-1`, compartiment **cezou (root)**.
VM : **`stream-a1flex-working`** (A1.Flex, 4 OCPU / 24 GB, Running).
⚠️ Le nom réel diffère de la checklist (`instance-20250330-1459`) — même VM.
Aucun OCID/IP recopié ici (règle du repo).

| # | Étape | Statut |
|---|-------|--------|
| 1 | Account type payant | ✅ Fait |
| 2 | Budget alert | ✅ Fait (par l'agent) |
| 3 | Bucket `frogsmashers-db-backups` | ✅ Fait |
| 4 | Dynamic group `frogsmashers-backend-dg` | ✅ Fait |
| 5 | IAM policy `frogsmashers-backup-policy` | ✅ Fait |
| 6 | Lifecycle 60j + policy service objectstorage | ✅ Fait |

> **✅ Toutes les étapes sont faites** (2026-07-20, par l'agent après reconnexion).
> Prochaine action : attendre ~10 min (propagation IAM) puis lancer
> `provision.sh backup` + `verify`.

> **Rappel « plafond 10€ » :** un budget OCI **n'est pas un plafond** — il n'arrête
> pas la facturation, il **alerte** seulement. La vraie protection = l'archi gratuite.
> Le budget est réglé pour alerter au **moindre centime**.

---

## ✅ 1. Type de compte
Abonnement **Universal Credits** (Infrastructure) **Active** depuis fév. 2025 =
compte payant commercial, pas un compte d'essai Free Tier. Les règles de
récupération pour inactivité et de suppression A1 (propres au Free Tier) ne
s'appliquent pas. Rien à faire (encore mieux que PAYG).

## ✅ 2. Budget alert — FAIT
Budget préexistant **« FREE »** (Monthly, **€1**, scope cezou root). L'agent a
ajouté la règle d'alerte manquante : **Actual Spend, seuil 1 %, email
cviegas@wiremind.io** (+ une règle *Forecast Spend 1 %* préexistante).
Rien à faire.

## ❌ 3. Bucket — COMMENT FAIRE
Storage → Object Storage → **Create Bucket** (compartiment cezou/root) :
- Name : **`frogsmashers-db-backups`** — Standard — Private (défaut, ne pas rendre public).

## ❌ 4. Dynamic group — COMMENT FAIRE
Identity & Security → Domains → **Default** → onglet **Dynamic groups** → Create :
- Name : `frogsmashers-backend-dg`.
- OCID de la VM : Compute → Instances → `stream-a1flex-working` → Copy OCID.
- Rule : `Any {instance.id = '<OCID VM>'}`

## ❌ 5. IAM policy — COMMENT FAIRE
Identity & Security → **Policies** → Create Policy `frogsmashers-backup-policy`
(compartiment cezou/root), statements :
```
Allow dynamic-group frogsmashers-backend-dg to read buckets in compartment cezou where target.bucket.name='frogsmashers-db-backups'
Allow dynamic-group frogsmashers-backend-dg to manage objects in compartment cezou where target.bucket.name='frogsmashers-db-backups'
Allow dynamic-group frogsmashers-backend-dg to read objectstorage-namespaces in tenancy
```
> Note : si domaine par défaut, préfixer le nom du groupe par `Default/` →
> `dynamic-group Default/frogsmashers-backend-dg`.

## ❌ 6. Lifecycle 60j + policy service — COMMENT FAIRE
D'abord ajouter (même écran Policies, `<region-id>` = `eu-paris-1`) :
```
Allow service objectstorage-eu-paris-1 to manage object-family in compartment cezou
```
Puis (après avoir créé le bucket, étape 3) : bucket `frogsmashers-db-backups` →
**Lifecycle Policy Rules** → Create Rule : action **Delete**, target objects, **60 days**.

---

## Après (rappel checklist)
Propagation IAM ~10 min, puis dire **"console done"** → l'agent lance
`provision.sh backup` + `verify`.
