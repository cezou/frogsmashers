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

## 3. Determinism — ✅ audit pass done (July 2026, issue #25)

Vigilance already present (`TongueClashesWith` dropped a frame-dependent
physics query to avoid desync). The broad determinism audit pass ran in
July 2026 — results in §4.

## 4. Determinism audit pass (July 2026) — findings & fixes

Three sweeps over everything reachable from `SimulationDriver.Step`
(frame-dependent reads; snapshot/hash completeness & stale refs;
side-effects & pooling).

### Verified clean

- **Positions.** Character/Fly positions live in `transform.position`,
  written only by sim (`ApplyMotionVector`, `Fly.RunMotion`);
  `CharacterAnimator.LateUpdate` moves only child transforms (sprite,
  tongueTip). Verified in the prefabs: the colliders sim queries hit
  (`Character`/`Fly` layers) sit on the sim-owned roots; the TongueTip
  collider is on layer 11, which no sim query masks.
- **Physics2D queries** in sim (`RunAttack`, `RunPhysicsBouncing`,
  `RunTongue`, `ClampMotion`, `Fly` raycasts) are reproducible:
  `Physics2D.SyncTransforms()` at the top of every `Step` + sim-owned
  roots. Residual (theoretical) caveat: `OverlapCircleAll`/`CircleCastAll`
  result ordering — stable for identical physics state.
- No `Time.*`, `UnityEngine.Random`, or unordered-collection iteration on
  the shipped sim path. Input, RNG (`DeterministicRng` + snapshotted
  state), and `SimClock` all confirmed deterministic. All ~20
  `Character.cs` sim-path FX route through `SimFx`; KO/victory guards
  intact. Snapshot **capture/restore/wire** coverage complete — no
  missing-field desync found. §1 stale-ref guards all still present.

### Fixed in this pass

- **Authority-hash gaps** (rule: snapshot ⇒ also hash): `winningPlayer`,
  `redTeamScore`/`blueTeamScore` (team-mode scoring was entirely
  unhashed), `Player.roundWins`, `Character.WallSlideSide`,
  `Character.lastHitByPlayer`, `Fly.ingestedBy` — all added to the
  `HashSimState` methods, encoded as in `SaveTo`.
- **Showdown "WIN!" plum** fired unguarded inside `RegisterKill` (sim
  path — duplicated on resim) and was redundant: `PresentRoundWinIfReady`
  already presents the win behind `!IsResimulating` + confirmed-tick
  gating. Direct calls deleted.
- **Editor debug self-hit** (`Character.SimTick`, Y button) used
  `UnityEngine.Random.value` → `DeterministicRng.Match.Value`.
- **Null-owner guards**: team-mode `player.team` derefs in
  `Hit`/`GetTongueHit`/`RunTongue` and the suicide side-plum color now
  tolerate `player == null` (debug ownerless spawns).
- **Pooling rule**: `CheckDeath` no longer `Destroy`s ownerless
  characters (deactivate only).

### Known constraints & accepted debt

- **Showdown mode is offline-only.** Its `activePlayers.Remove(...)`
  inside a sim tick is not snapshot-restorable (`RestoreFrom` refuses
  when `snap.PlayerCount > activePlayers.Count`), so it must never run
  under rollback. `isShowDown` was a stale-leak risk (only reset on the
  offline join screen); it is now also cleared on the online-lobby path.
  The online score flow (`UpdateOnline`) never sets it.
- **SimFx latent traps** (fine today, mind when adding FX): N emits from
  the same (slot, call site) in one tick play N× forward but collapse to
  1× on resim; dedup window is 128 ticks (safe under gate-asserted
  rollback depths).
- `finishDelay` (snapshotted sim field) is reused by frame code for the
  offline JoinScreen countdown — states never overlap, left as-is.
  `roundFinishedTick` is sim-written but unsnapshotted; only read by
  presentation, worst case a one-frame victory-sting misgate after a
  rollback.
- `PoolFly` `Destroy`s a second distinct fly if the pool slot is taken —
  unreachable under the single-fly invariant.

## Rule for future sim code

Any new code run in `SimTick`/`RunTongue`/`RunAttack`/`CheckDeath` must:

1. Dereference an inter-object ref only with a null-guard (refs may be
   stale post-rollback).
2. Guard any non-deterministic sound/particle/`Instantiate` via `SimFx`
   (tick-keyed dedup).
3. Keep snapshotted sim state consistent across linked fields
   (e.g. state↔ref).
