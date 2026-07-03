# PHASE3.md — Online proof-of-concept

> See `docs/plans/PLANGLOBAL.md` for global context, `AGENTS.md` for methodology.

## Goal

Let **2-6 remote players** play FrogSmashers together over the Internet, with netcode suited to latency-sensitive 2D combat.

## Status

- ✅ **3.0 — Linter** (StyleCop.Analyzers + .editorconfig SquidNorm)
- ✅ **Netcode research** (June 2026) — decision below
- ✅ **3.1→3.11 — Full rollback netcode + playable lobby**: done and pushed (history rewritten per feature, commits `535cc81..4442aa8`). Playable: menu → lobby list → lobby-arena (free brawl, dynamic join) → ready → match → cycling levels. Manually validated on 1 PC (2 instances) + 7 automated gates (see Gates).
- ✅ **3.12 — Network simulator + tuning** (June 2026): receive-side shim in NetMessages (`-simLatency/-simJitter/-simLoss/-simSeed`, see § Simulator). Revealed and fixed 2 real desync bugs (see § Bugs found). `RollbackMetrics` asserted by the gate (p95 rollback < 15 ticks, max < 30, corrections < 2/min). Existing constants needed no changes (measured p95 = 2 ticks at 150 ms + 5% loss).
- ✅ **3.13 — 4 players**: offline gates `-netPlayers 4` (determinism + staggered rollback across 3 delayed slots) and a real 4-instance online match (host + 3 distinct `-authProfile`): 0 desync.
- ◀ **Remaining**: 3.14 (soak on 2 real machines; headless Linux x86_64 + ARM64 builds recompile and boot ✅)

## Notable changes vs initial plan

- **3.11 redesigned** (user request): NO join-code. Lobby list to the right of the menu (name = `Environment.UserName`, 4 s refresh via UGS Sessions with no network module, relay code as a public property). The lobby = the `JoinScreen` scene with the **full rollback sim** (playable frogs); ready = START/Enter, 5 s countdown → match. Color per slot; color choice + online team mode → Phase 4.
- **Dynamic join**: joins/leaves applied at a future tick (host+60) on all peers; late joiner bootstrapped by `LobbyWelcome` (roster + full snapshot + clock catch-up).
- **Session epoch**: all sim traffic carries a generation byte, bumped on each scene transition — in-flight messages from the old scene are dropped (otherwise: input-buffer pollution → permanent desync, seen in testing).
- **Sessions package network module (WithRelayNetwork) broke in 2.2.3** → explicit relay flow (`RelayService.CreateAllocationAsync` + `SetRelayServerData` + `StartHost/Client`). The lobby part of the package works.
- Misc: `Application.runInBackground` required; `Gamepad.current` (not `Gamepad.all[0]`); neutral input when unfocused; light client→host timesync (`PaceBias`); double-Escape to quit.

## Bugs found via simulated latency (June 2026)

1. **Non-deterministic cross-process tongue-vs-tongue clash**: detection used `Physics2D.OverlapCircleAll` on the `TongueTip` collider, positioned by `CharacterAnimator.LateUpdate` (render). Offline gates missed it (same frame/tick interleaving in one process); online, deterministic desync at first clash (tick 840 of the scripted match). Fix: pure-sim analytic test (`Character.TongueClashesWith`, distance 1.5 between tips).
2. **Permanent holes in the input stream**: `LastConfirmedTick` was a max (not contiguous); a UDP loss burst > redundancy window (8) left a hole never recovered, and `SafeTick` let the host broadcast hashes/snapshots built on predictions. Seen in 4 players (720 msg/s → burst drops). Fix: input protocol v2 — grouped packet (all slots) + **per-slot contiguous-tick acks**; sender resends from ack+1 (max window 64), so eventual guaranteed delivery; `SafeTick` only advances on confirmed contiguous input.

## Network simulator & forensics (Windows build CLI)

| Flag | Effect |
|---|---|
| `-simLatency MS` | simulated RTT (each peer delays its receives by MS/2) |
| `-simJitter MS` | ± jitter on delay (reliable never reordered) |
| `-simLoss PCT` | loss on unreliable only (InputMsg) |
| `-simSeed N` | seed for the network System.Random (never the sim) |
| `-hashInterval N` | authority hash cadence (default 30; 1 = per tick) |
| `-dumpTick N` | field-by-field snapshot dump of tick N (host/client diff) |
| `-netPlayers N` | offline/online harness with N slots (default 2) |

`RollbackMetrics` logs at match end: rollbacks (p50/p95/max), pace lead, bias bursts — asserted by the gate under sim flags.

## Automated gates (Windows build, batch)

```
FrogSmashers.exe -batchmode -nographics -<gate> -logFile <log>
```
| Gate | Checks | PASS |
|---|---|---|
| `-determinismTest` | 2 scripted runs → identical per-tick hashes | 1800 ticks |
| `-snapshotTest` | restore tick 600 at t=900 + resim | bit-exact |
| `-inputPipeTest` | pack→buffer→unpack in sim | = direct |
| `-rollbackTest` | slots 1..N-1 delayed 5·N ticks | = ground truth |
| `… -netPlayers 4` | determinism + rollback at 4 slots | same |
| `-netMatchHost/-netMatchJoin -scriptedLocal` | real relay match | 0 desync |
| `… -simLatency 150 -simJitter 30 -simLoss 5` | same under network conditions | 0 desync + healthy metrics |
| `… -netPlayers 4` (4 instances) | real 4-player relay match | 0 desync |
| `… -injectDesync` | tick-700 corruption → repaired | ≤1 cadence |
| `-netLobbyHost/-netLobbyJoin -scriptedLocal` | lobby + dyn. join + transition + match | 0 desync |

Two instances on one PC: the 2nd with `-authProfile <name>` (distinct anonymous identities).

## ✅ DECISION — Chosen netcode (research June 2026)

Deep multi-agent research (22 sources, 25 claims adversarially verified with 3 votes each) + codebase audit. Framing decisions: **4 players max online, authoritative server, full rollback, free, Unity infra**.

### Chosen architecture

**Authoritative client-host on Unity Relay + Lobby (free tier); custom GGPO-style rollback on the existing MonoBehaviour sim; NGO 2.x used ONLY as transport/connection layer** (UnityTransport + Relay + named messages — NO NetworkVariable/NetworkTransform/NetworkObject for gameplay).

- The lobby creator hosts the **authoritative simulation**; clients cannot cheat (the host can — accepted until Phase 7).
- Clients **predict remote inputs** (repeat-last-input), local input applied instantly (zero local input lag), **rollback + resim** on receiving confirmed inputs.
- The host broadcasts a **state hash every tick + periodic full snapshot** → corrects float drift (no fixed-point refactor: same Windows x86_64 binary everywhere + authoritative correction).
- Authority code never assumes the host is also a player → the same code runs as a **dedicated headless build in Phase 7** (real anti-cheat) without rewrite.

### Verified facts that decided it

| Fact | Verdict |
|---|---|
| **Unity Multiplay / Game Server Hosting** | Discontinued by Unity (31 Mar 2026, moved to Rocket Science Group). No free tier before (pay-as-you-go + one-time $800/6mo credit) or after (RSG: no public pricing, no self-serve). **No free Unity-hosted dedicated exists.** |
| **Steam** | SDR = free relay but needs an App ID (~$100, Steam-published — Phase 7+) and for dedicated, YOUR server on AWS/Azure/GCP. Doesn't host either. |
| **Unity Relay + Lobby** | Free: 50 avg CCU/month, 150 GiB/month — plenty for the POC. Only 100%-free path on Unity infra. |
| **NGO alone** | Unfit for fighting: no full prediction/reconciliation (only "anticipation"), no server-side rewind. |
| **Netcode for Entities (DOTS)** | Complete on paper but requires an ECS rewrite of gameplay (`Character.cs` = 1525 LOC custom kinematics) — blows the 30-50h budget. |
| **Codebase** | Ideal for custom rollback: kinematic physics without Rigidbody2D, ~40 state fields/Character (2-4 KB snapshot), `IInputSource` already wired. Solvable blockers: RNG in `Fly.cs`, sim on `Update`/`Time.deltaTime`, global `Time.timeScale` (slow-mo). |

### Packages to add (`Packages/manifest.json`)

`com.unity.netcode.gameobjects` 2.x, `com.unity.transport` (explicit pin), `com.unity.services.core`, `com.unity.services.authentication` (anonymous), `com.unity.services.relay`, `com.unity.services.lobby`.

## Sub-steps (each independently testable)

> ✅ done · ⬜ todo

| ✔ | # | Step | Exit test | Est. |
|:-:|---|---|---|---|
| ✅ | 3.1 | **Tick refactor**: `SimClock`, `ISimTickable`, `SimulationDriver` (60 Hz accumulator). `Character.Update`→`Tick(dt)`, `Fly`, `GameController` sim. Slow-mo (`Timebump`) out of `Time.timeScale` → per-character data | Local 2-player offline match, identical feel | 6-8h |
| ✅ | 3.2 | **Deterministic RNG**: `DeterministicRng` (seeded xorshift) replaces `Random.*` in Fly + fly-spawn; `Time.time`→`SimClock.SimTime`. Replay harness | Same seed + same inputs → identical per-tick hash over 2 runs | 3-4h |
| ✅ | 3.3 | **Snapshots**: `EntityRegistry` (stable IDs), `CharacterSnapshot`/`FlySnapshot`/`MatchSnapshot` (+ RNG state), `SnapshotRingBuffer` (128). Pooled Fly (no Destroy in prediction window) | Save T → +30 ticks → restore T → resim = identical hash | 5-6h |
| ✅ | 3.4 | **Input pipeline**: pack `InputState` ~3 bytes, `InputFrame`, `InputRingBuffer` (`GetOrPredict`=repeat-last), `RollbackInputSource : IInputSource` | Round-trip pack/unpack bit-identical | 3-4h |
| ✅ | 3.5 | **Local rollback loop** (1 process): restore oldest mispredicted tick + resim | 2 virtual slots, inputs delayed 5 ticks → hash = ground-truth | 4-5h |
| ✅ | 3.6 | **UGS plumbing**: `NetBootstrap` (anonymous auth), `RelaySession`, `LobbySession`, `NetConnection` | Hello world: 2 instances, join code, connected slot | 4-5h |
| ✅ | 3.7 | **Input transport**: `InputMsg` unreliable (last 8 frames, redundancy), client→host→fan-out | 2 instances move their frogs via Relay | 3-4h |
| ✅ | 3.8 | **Network rollback**: 3.5 fed by 3.7, instant local input | Local lag-free, remote corrects cleanly | 3-4h |
| ✅ | 3.9 | **Host authority**: `StateHashMsg`/tick + `SnapshotMsg`/30 ticks or on mismatch | Injected divergence corrected in ≤1 cadence | 3-4h |
| ✅ | 3.10 | **Session→match flow**: `MatchStartMsg` (slots, seed, level), per-slot spawn via `OnlineSessionConfig`, locked match | Clients spawn at correct slots, shared seed | 3-4h |
| ✅ | 3.11 | ~~join-code~~ → **redesigned**: lobby list + playable lobby + dynamic join + epoch (see "Notable changes") | Menu → lobby-arena → match, 2 instances | done |
| ✅ | 3.12 | **Network simulator**: ~150 ms RTT + jitter + loss; tune prediction/cadence/redundancy | Correct feel at 150 ms simulated on 1 PC | 3-4h |
| ✅ | 3.13 | **4-slot validation**: buffers sized for 4 | Hashes synced across 4 peers | 2-3h |
| ⬜ | 3.14 | **Soak on 2 real machines** + headless `-server` non-regression + commit/push (no release) | Full match, no desync between 2 remote PCs | 2h |

Total ~46-60h (reducible to ~40h if 3.13 stays structural).

## Reusable existing state

Phase 2 already set up:

- **`Assets/Scripts/Net/ServerMode.cs`** — `IsServer` detection via `UNITY_SERVER` or `-server` CLI arg
- **`Assets/Scripts/Net/IInputSource.cs`** — abstract input interface, already wired in `InputReader.cs`
- **`Assets/Scripts/Net/LocalInputSource.cs`** — local impl (Keyboard + Gamepad), default
- **`Assets/Scripts/Net/RemoteInputSource.cs`** — stub (returns empty `InputState`). Extend in Phase 3 for network inputs.
- **`Assets/Scripts/Net/ServerBootstrap.cs`** — server-mode bootstrap: skip UI, load `1BusStop`, inject 2 mock players
- **`Assets/Editor/BuildLinuxArm64Server.cs`** — ARM64 server build ready (kept compilable, no longer used for gameplay in Phase 3 — see `AGENTS.md` for VM access)

These are reused by custom rollback: `RollbackInputSource : IInputSource` pulls predicted/confirmed inputs from the rollback buffer; `ServerBootstrap`/`RemoteInputSource` stay compilable (Phase 2 guard, reused in Phase 7 for dedicated).

## Out of scope for Phase 3

- **Persistent user accounts** → Phase 5
- **Ranked matchmaking / ELO** → Phase 6
- **Steam integration** → Phase 7+
- **Release / distributed build** → Phase 7 (after Cloudflare front)
- **Strong anti-cheat** → Phase 7
