# Rollback-safety audit (June 2026) — bug class & rules

After the "last round" crash (NRE `RunTongue` during resim, fixed v0.4,
commit `5b2d29d`), audit of the class: *an element of the rollback-replayed
simulation path that is not rollback-safe*. Netcode rules: `docs/NETCODE.md`.

## 1. Stale inter-object references at `RestoreFrom` — ✅ under control

Any reference rebuilt by lookup on restore can be null/stale then
dereferenced in resim.

| Reference | State | Protection |
|---|---|---|
| `Character.ingestingFly` → fly | ✅ fixed (v0.4) | null-guards + `tongueState`↔`ingestingFly` consistency in `RestoreFrom` |
| `Fly.ingestedBy` → character | ✅ safe | **watchdog** `Fly.SimTick` (frees if owner null/dead) + bounded restore index |
| `Character.lastHitByPlayer` | ✅ safe | bounded-index restore |
| `GameController.winningPlayer` | ✅ safe | bounded-index restore |
| Character/Fly instances (death/respawn) | ✅ safe | pooling + symmetric revive on restore |

Root cause of the crash = **asymmetry**: `Fly` had a watchdog for
`ingestedBy==null`, `Character` had none for `ingestingFly`. Filled. No
other crash of this type found.

## 2. Side-effects replayed during resimulation — ✅ fixed (SimFx)

Each rollback replays ticks → sounds/particles triggered in those ticks
replayed, causing duplicated/stuttering SFX under latency. Fixed: `SimFx`
(keyed by slot + call site) plays effects on the forward pass and only
re-fires in resim for ticks not yet played — no duplication, without
muting a first-applied remote action (the naive `!IsResimulating` gate,
commit `f765390`, muted remote actions and was reverted in `2975803`).
Applied to the previously unguarded `Character.cs` sim-path SFX/particles;
KO/victory guards kept.

## 3. Determinism — note

Vigilance already present (`TongueClashesWith` dropped a frame-dependent
physics query to avoid desync). Eventually: broad determinism audit pass
(positions read from render/LateUpdate, `Physics2D` state-dependence) —
tracked as a GitHub issue. No crash, not urgent.

## Rule for future sim code

Any new code run in `SimTick`/`RunTongue`/`RunAttack`/`CheckDeath` must:

1. Dereference an inter-object ref only with a null-guard (refs may be
   stale post-rollback).
2. Guard any non-deterministic sound/particle/`Instantiate` via `SimFx`
   (tick-keyed dedup).
3. Keep snapshotted sim state consistent across linked fields
   (e.g. state↔ref).
