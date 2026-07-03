# Netcode

GGPO-style **rollback netcode** over **Unity Relay** (free tier), with an
**authoritative client-host**. NGO is used as a transport only — gameplay
is never replicated by Unity, it is *resimulated*.

## Why

| Constraint | Choice |
|---|---|
| Fighting game: 50–200 ms reaction windows, zero added input lag | Rollback (predict remote inputs, resimulate on correction) |
| Free hosting | Unity Relay free tier; the lobby creator hosts the authoritative sim (no free dedicated hosting exists anywhere) |
| Anti-cheat path | Host is authoritative today; the same authority code runs unchanged on a dedicated headless server later |
| Existing codebase | Custom kinematic MonoBehaviour sim (~40 state fields/character, no Rigidbody2D) — snapshot-friendly, no ECS rewrite |
| Float determinism | Not required: same binary everywhere + the host corrects drift with full state snapshots |

## How it works

```
every tick (60 Hz):           on remote input arrival:
  poll local input              if it contradicts a prediction:
  confirm into ring buffer        restore snapshot before that tick
  predict missing remote          resimulate to present (corrected)
  inputs (repeat last)
  simulate, snapshot          host, every 30 confirmed ticks:
  send input window             broadcast state hash; on client
  (8 ticks, unreliable)         mismatch → full snapshot repair
```

- Local input is applied the tick it happens — no added latency.
- A tick is **confirmed** once every player's input for it is known.
- The host's sim is canonical; clients converge to it.
- Membership (join/leave) applies at a host-chosen *future* tick on all
  peers; late joiners bootstrap from a full snapshot and catch up.

## Rules (what keeps it correct)

1. **Fixed tick.** Gameplay runs in `SimTick(dt)` via `SimulationDriver`
   (60 Hz, deterministic order). Never read `Time.*` in sim code — use
   `dt` and `SimClock`.
2. **Seeded randomness only.** `DeterministicRng.Match`, never
   `UnityEngine.Random`, in anything that affects sim state.
3. **Every mutable sim field lives in `MatchSnapshot`.** Add a field to
   the sim → add it to snapshot, restore, hash and wire serialization.
4. **Never `Destroy` sim objects.** Pool them (`SetActive(false)`) so a
   rollback can revive them; clear references at the killing tick
   (Unity's deferred destroy is frame-aligned, not tick-aligned).
5. **Inputs are 9 bits.** All sim input flows through the
   `InputReader`/`IInputSource` chokepoint and packs into 2 bytes.
6. **No NGO replication.** Gameplay objects are plain MonoBehaviours;
   the network layer is custom named messages only.
7. **Session epoch.** Every sim message carries a generation byte,
   bumped at each scene transition; stale in-flight messages are
   dropped (their ticks belong to a dead clock).
8. **Frame code must not mutate sim state.** UI, effects, animation read
   the sim; anything that writes it belongs in a tick (or in a
   host-authoritative message that resolves to a tick).
9. **Gates are the law.** After any sim or netcode change, all automated
   gates must pass (`-determinismTest`, `-snapshotTest`,
   `-inputPipeTest`, `-rollbackTest`, `-netMatchHost/Join`,
   `-netLobbyHost/Join`; details in the harness sources).

## Map

| Layer | Files (`Assets/Scripts/Net/`) |
|---|---|
| Sim core | `Sim/` — SimulationDriver, SimClock, DeterministicRng, MatchSnapshot, SnapshotRingBuffer, MatchHasher, DeterminismHarness |
| Rollback | `Rollback/` — InputRingBuffer, InputPacking, RollbackInputSource, RollbackManager, RollbackNetDriver, AuthoritySync, SnapshotWire |
| Transport | `Transport/` — NetBootstrap, NetSession (relay), LobbyDiscovery, NetMessages, harnesses |
| Session | `OnlineMatch.cs` — lobby/match phases, roster, membership, transitions |
