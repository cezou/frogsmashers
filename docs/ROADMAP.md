# ROADMAP.md — FrogSmashers Online

Turn FrogSmashers into a ranked online game.

> **Task tracking lives in GitHub issues** (cezou/frogsmashers): topic labels
> (`netcode`, `ui`, `account system`, `ranked`, `backend`, `steam`, `misc`),
> kanban `status:*` labels, closed = done. Each open issue lists what blocks
> it ("Blocked by #N") — don't start an issue whose blockers are open.
> Netcode design + rules: `docs/NETCODE.md`. Research references:
> `docs/research/`.

## Target architecture

```
┌──────────────────┐  inputs/snapshots (UTP)   ┌──────────────────────┐
│  Unity Client    │ ────────────────────────▶ │  Unity Relay (free)  │
│  (Windows .exe)  │ ◀──────────────────────── │  + Lobby             │
│  rollback GGPO   │                           └──────────┬───────────┘
└────────┬─────────┘                                      │
         │                              ┌─────────────────▼──────────┐
         │                              │  Authoritative client-HOST │
         │                              │  (sim authority = lobby    │
         │                              │  creator; later → paid     │
         │                              │  dedicated headless build) │
         │                              └─────────────────┬──────────┘
         │ REST/WebSocket (HTTPS via Cloudflare)          │ match results
         ▼                                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  Backend API (Oracle OCI VM, Node or Go)                         │
│  - /auth (login, register, JWT)                                  │
│  - /matchmaking (ranked queue, ELO)                              │
│  - /leaderboard, /profile/{id}                                   │
└──────────────────────────────────┬───────────────────────────────┘
                                   ▼
                          ┌────────────────┐
                          │  PostgreSQL    │
                          │  users, elo,   │
                          │  matches, ...  │
                          └────────────────┘
```

## Where we are

Done: Unity 6 migration, main menu, client/server split, full GGPO-style
rollback netcode on Unity Relay (playable end-to-end: lobby list →
playable lobby-arena with dynamic join → match → score screens → return
to lobby; color/team choice online; 4 players; network simulator + gates).
See the closed GitHub issues for the full record.

Next up and beyond (accounts → ranked/ELO → Cloudflare/anti-cheat/Steam →
first public release): the open GitHub issues, ordered by their
"Blocked by" links.

## Global constraints (always apply)

1. **No manual Unity UI** — everything via Editor scripts (see `AGENTS.md`)
2. **SquidNorm + hybrid C# standard** — enforced by StyleCop.Analyzers
   (see `AGENTS.md` § Code standard)
3. **No distributed release/build** before the Cloudflare front is up
4. **OCI IP confidential** — strict gitignore, replaced by the Cloudflare
   hostname before any distributed build
5. **Automated workflow** — chain Generate → Build → Launch after every
   Unity-side change (Editor closed)
6. **Gates are the law** — after any sim/netcode change, all automated
   gates must pass (`docs/NETCODE.md` § Rules)

## Architectural decisions log

- **Unity 6** (LTS), **InputSystem 1.19**
- **Windows client + headless server** (Linux ARM64 IL2CPP kept compilable)
- **Netcode**: custom GGPO-style rollback, authoritative client-host on
  Unity Relay + Lobby (free). NGO = transport/connection only. No
  fixed-point — authoritative snapshots correct float drift. Details:
  `docs/NETCODE.md`.
- **OCI ARM64**: not used for gameplay (client-host relay instead); candidate
  host for the accounts/ELO backend (`docs/research/STEAM.md` §3).
- **Cloudflare in front of the backend** before any release.
- **C#/Unity 6 retained** (verified Jul 2026): watch Unity's CoreCLR
  migration (6.7 experimental → 6.8 drops Mono, .NET 10 BCL,
  source-compatible) as a routine upgrade — **not** a rewrite. No
  engine/language migration (a rewrite would discard the custom rollback).
- **Market context** (verified June 2026): Unity Multiplay was discontinued
  (Mar 2026, → Rocket Science Group, no free tier). Only free UGS services:
  Relay (50 CCU/mo, 150 GiB/mo), Lobby, Authentication, Distributed
  Authority, Vivox — hence client-host.

## Open decisions

- **Backend API language**: Node + Fastify or Go + chi — decided when the
  auth API starts. Backend scoping: `docs/research/STEAM.md` §3.
- **Dedicated arbiter hosting**: Rocket Science (ex-Multiplay), SDR + own
  servers if on Steam, or other — decided when anti-cheat work starts.
- **Steam integration**: researched → `docs/research/STEAM.md` (SDR replaces
  Unity Relay, auth via `GetAuthTicketForWebApi` + backend).
