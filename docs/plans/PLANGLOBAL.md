# PLANGLOBAL.md — FrogSmashers Online Roadmap

Overall plan to turn FrogSmashers into a ranked online game. Each phase runs in a dedicated session and has its own detailed `docs/plans/PHASEN.md`.

## Long-term vision

```
┌──────────────────┐  inputs/snapshots (UTP)   ┌──────────────────────┐
│  Unity Client    │ ────────────────────────▶ │  Unity Relay (free)  │
│  (Windows .exe)  │ ◀──────────────────────── │  + Lobby (join code) │
│  rollback GGPO   │                           └──────────┬───────────┘
└────────┬─────────┘                                      │
         │                              ┌─────────────────▼──────────┐
         │                              │  Authoritative client-HOST │
         │                              │  (sim authority = lobby    │
         │                              │  creator; Phase 7 → paid   │
         │                              │  dedicated headless build) │
         │                              └─────────────────┬──────────┘
         │ REST/WebSocket (HTTPS via Cloudflare)          │ match results
         ▼                                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  Backend API (Oracle OCI VM, Node or Go) — Phase 5               │
│  - /auth (login, register, JWT)                                  │
│  - /matchmaking (ranked queue, ELO)                             │
│  - /leaderboard, /profile/{id}                                  │
└──────────────────────────────────┬───────────────────────────────┘
                                   ▼
                          ┌────────────────┐
                          │  PostgreSQL    │
                          │  users, elo,   │
                          │  matches, ...  │
                          └────────────────┘
```

## Phase status

| # | Phase | Status | Deliverable |
|---|---|---|---|
| **0** | Upgrade Unity 2019.2 → Unity 6 | ✅ DONE | commit `a81191a`, release v0.1. Modern InputSystem (Xbox + PS + Keyboard). |
| **1** | Main Menu + online UI stubs | ✅ DONE | commit `666a913`. MainMenu scene, 5 buttons, Coming Soon panels. Local Game runs the existing flow. |
| **2** | Client/server refactor (headless-ready) | ✅ DONE | commits `c1500e7` (split) + `e728017` (ARM64 build). Linux ARM64 server runs on OCI without crashing. |
| **3** | Online proof-of-concept | ◀ **NEARLY DONE** | GGPO-style authoritative client-host rollback on Unity Relay, **end-to-end playable**: lobby list → lobby-arena (playable frogs, dynamic join) → match → level cycle. 7 automated gates green. Remaining: 3.14 soak on two real machines (see `docs/plans/PHASE3.md`). |
| **4** | Netcode/lobby improvements | ◀ **IN PROGRESS** | Rollback hardening + multi-match lobbies. See `docs/plans/PHASE4.md`. |
| **5** | User accounts | TODO | Auth API + Postgres DB on OCI. Login required for online. |
| **6** | Ranked Queue + ELO | TODO | ELO matchmaking, leaderboard. |
| **7** | Polish, anti-cheat, scaling, Cloudflare front | Ongoing | Hardening. **First public release only from this phase** (distributed build OK once server IP is masked by Cloudflare). |

## Why this order

- **Phase 0 first**: no modern netcode installs without Unity 6. ✅
- **Phase 1**: MainMenu, low risk, clean UI base. ✅
- **Phase 2 before netcode**: split render/sim/input and have a headless binary before the network layer. ✅
- **Phase 3 = technical proof**: validate the chosen online stack works end-to-end before building layers on top.
- **Phase 4 (lobbies / netcode improvements)**: solidify rollback and expand multi-match lobby features.
- **Phase 5 (accounts) before ranked**: ELO = persistent per-player score = needs accounts.
- **Phase 6 (ranked) before polish**: complete the gameplay loop before investing in monitoring/anti-cheat.
- **Phase 7 (polish + Cloudflare) unlocks the public release**.

## Global constraints (every phase)

1. **No manual Unity UI** — everything via Editor scripts (see `AGENTS.md`)
2. **SquidNorm + hybrid C# standard** — enforced by StyleCop.Analyzers (see `AGENTS.md` § Code standard)
3. **No distributed release/build** before Cloudflare (Phase 7)
4. **OCI IP confidential** — strict gitignore, replaced by Cloudflare hostname before any distributed build
5. **Automated workflow** — chain Generate → Build → Launch after every Unity-side change (Editor closed)

## Per-phase references

| Phase | Detailed file |
|---|---|
| Phase 4 (active) | `docs/plans/PHASE4.md` |
| Phase 3 (nearly done) | `docs/plans/PHASE3.md` |
| Historical phases | Git history (Phase 0, 1, 2 commits) |
| Future phases | Created when each phase starts |

## Architectural decisions made

- **Unity 6** (LTS) — Phase 0
- **InputSystem 1.19** — Phase 0
- **Windows client + headless server** — Phase 2 (Linux ARM64 IL2CPP)
- **Netcode (Phase 3)**: **custom GGPO-style rollback, authoritative client-host on Unity Relay + Lobby (free)**. NGO = transport/connection only. No fixed-point (authoritative snapshots correct float drift). Details: `docs/plans/PHASE3.md`.
- **OCI ARM64**: no longer used for gameplay (replaced by client-host Relay). Still a candidate for the Phase 5 backend API.
- **Cloudflare in front of the backend** — Phase 7 (before any release)
- **C#/Unity 6 retained** (verified Jul 2026): not outdated. Watch Unity's CoreCLR migration (6.7 experimental → 6.8 drops Mono, .NET 10 BCL, source-compatible) as a routine version upgrade — **not** a rewrite. No engine/language migration (a Godot/Bevy rewrite would discard the custom rollback layer).

## Market context (verified June 2026)

- **Unity Multiplay/Game Server Hosting was discontinued by Unity** (31 Mar 2026, transferred to Rocket Science Group; no free tier, no public pricing). No free Unity-hosted dedicated server exists → hence client-host.
- **Only free UGS services**: Relay (50 CCU/month, 150 GiB/month), Lobby, Authentication, Distributed Authority, Vivox.

## Open decisions

- **Backend API language**: Node + Fastify or Go + chi — decided in Phase 5. Backend scoping (OCI Postgres ARM64, capacity, free-tier risks): `docs/plans/PLANSTEAM.md` §3
- **Phase 7 dedicated hosting**: Rocket Science (ex-Multiplay), SDR+AWS if Steam, or other — decided in Phase 7
- **Steam integration**: researched (June 2026) → `docs/plans/PLANSTEAM.md`: SDR replaces Unity Relay (no CCU cap, free P2P), auth via `GetAuthTicketForWebApi` + OCI backend, execution Phase 7+

---

## Rollback-safety audit (June 2026) — bug class & tech debt

After the "last round" crash (NRE `RunTongue` during resim, fixed v0.4, commit `5b2d29d`), audit of the class: *an element of the rollback-replayed simulation path that is not rollback-safe*. Two sub-classes.

### 1. Stale inter-object references at `RestoreFrom` — ✅ under control

Any reference rebuilt by lookup on restore can be null/stale then dereferenced in resim.

| Reference | State | Protection |
|---|---|---|
| `Character.ingestingFly` → fly | ✅ fixed (v0.4) | null-guards + `tongueState`↔`ingestingFly` consistency in `RestoreFrom` |
| `Fly.ingestedBy` → character | ✅ safe | **watchdog** `Fly.SimTick` (frees if owner null/dead) + bounded restore index |
| `Character.lastHitByPlayer` | ✅ safe | bounded-index restore |
| `GameController.winningPlayer` | ✅ safe | bounded-index restore |
| Character/Fly instances (death/respawn) | ✅ safe | pooling + symmetric revive on restore |

Root cause of the crash = **asymmetry**: `Fly` had a watchdog for `ingestedBy==null`, `Character` had none for `ingestingFly`. Filled. No other crash of this type found.

### 2. Side-effects replayed during resimulation — ⚠️ tech debt (latent, unfixed)

Each rollback replays ticks → sounds/particles triggered in those ticks **replay**. Only KO/victory sounds are guarded (`!SimulationDriver.IsResimulating`). **Not guarded** in `Character.cs` (sim path):
- Sounds: `BatSwing`, `BatSwingVoice`, `FrogBounce(+Voice)`, `Land`, `TongueLaunch`, `TongueCollide`, `TongueCollideSurface`, `Burp`, `CharacterCollision`
- Particles: `CreateShingEffect`, `CreateBouncePuff`, `CreateTongueHitEffect`, `CreateHitEffect`, `CreateLocalizedShake`

**Impact**: under latency (frequent rollbacks), duplicated/stuttering SFX/particles on hits/bounces/tongues. **No crash, no desync** (audio/particles are outside sim state). Worsens with ping. Low today (p95 rollback = 1-4 ticks).

**Recommended fix** (~30 min): gate all sim-path SFX/FX behind `!SimulationDriver.IsResimulating` via a single helper (e.g. `SimFx.Play(...)` / `SimFx.Spawn(...)`) for consistency and to avoid future regressions. No `SoundController`-level guard (3rd-party, also called outside sim).

### 3. Determinism — note

Vigilance already present (`TongueClashesWith` dropped a frame-dependent physics query to avoid desync). Eventually: broad determinism audit pass (positions read from render/LateUpdate, `Physics2D` state-dependence). No crash, not urgent.

### Rule for future sim code

Any new code run in `SimTick`/`RunTongue`/`RunAttack`/`CheckDeath` must:
1. Dereference an inter-object ref only with a null-guard (refs may be stale post-rollback).
2. Guard any non-deterministic sound/particle/`Instantiate` behind `!SimulationDriver.IsResimulating`.
3. Keep snapshotted sim state consistent across linked fields (e.g. state↔ref).
