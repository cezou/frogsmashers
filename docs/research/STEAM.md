# STEAM.md — Steam: Relay (SDR), accounts & "OAuth", ELO backend

> Research doc (June 2026, 3 parallel deep-research agents, ~47 tool-uses, official `partner.steamgames.com` sources preferred). Answers: Steam relay limits vs Unity Relay, relay ↔ stats/auth link, "Steam OAuth" flow for accounts/ELO/rank, OCI as the DB host. No implementation here — feeds the accounts, ranked and Steam GitHub issues (roadmap: `docs/ROADMAP.md`).
>
> **Revised July 2026** (deep-research re-check): OCI Always-Free ARM was **halved to 2 OCPU / 12 GB** (was 4/24); "no Steam OAuth2" wording clarified (a legacy partner OAuth2 exists); PAYG upgrade + `pg_dump` backups promoted to explicit launch-blockers. No change to the DB/auth architecture — still self-hosted Postgres on OCI + Steam auth-ticket→JWT.

## TL;DR

| Question | Answer |
|---|---|
| Does the Steam relay (SDR) cap concurrent players? | **No.** No documented CCU limit or bandwidth quota; free for P2P client-host. The Unity Relay wall (50 CCU / 150 GiB) disappears. |
| Does SDR serve stats ("player count") or OAuth? | **No, unrelated.** SDR = pure transport. Player counts = `ISteamUserStats`; auth = `ISteamUser` auth tickets. Separate subsystems. |
| Does the relay affect the accounts/ELO/rank system? | **Zero impact.** Only "link": the same SteamID64 is both SDR routing identity and DB account key — convenience, not coupling. |
| Can OCI host the accounts/ELO DB? | **Yes, easily.** A1.Flex free (**2 OCPU / 12 GB** since Oracle's 2026 cut; was 4/24) + native ARM64 PostgreSQL handles <1000 CCU of REST API effortlessly. |
| Does "Steam OAuth" exist? | No OAuth2. For a game client: `GetAuthTicketForWebApi` → backend verifies via `AuthenticateUserTicket` → backend issues its own JWT. |

---

## §1 — The Steam relay: Steam Datagram Relay (SDR)

### 1.1 CCU and bandwidth limits

- **No CCU limit, no bandwidth quota, no paid tier** documented for SDR. Free for P2P/client-host on Steam ("Client-hosting using SDR is available and means developers can have reliable Client-to-Client networking for free").
- ⚠️ Caveat: the "unlimited" free tier is never contractually quantified by Valve. For **third-party dedicated servers** over SDR, Valve "cannot guarantee availability". For P2P (our case), no documented or reported cap. Strong, not absolute, certainty.

**Unity Relay vs SDR:**

| | Unity Relay (current) | Steam SDR |
|---|---|---|
| Free CCU max | 50 avg CCU/month | no documented cap |
| Bandwidth | ~3 GiB/CCU ⇒ ~150 GiB/month | no documented quota |
| Beyond | ~$0.16/CCU | n/a |
| Prerequisite | UGS account (free) | Steam App ID ($100) |
| Lobbies | Unity Lobby (free) | `ISteamMatchmaking` (free) |
| Non-Steam players | ✅ | ❌ (Steam client required) |

### 1.2 Prerequisites

- **Steam App ID required** (SDR fetches config via `GetSDRConfig/v1?appid=xxxx`).
- **Steam Direct: $100 per app**, non-refundable but recouped once $1,000 gross revenue is reached.
- **Usable in dev before release** (documented dev mode: `SDR_POPID` left empty).
- **Free prototyping under App ID 480 (Spacewar)** — Valve example app on every Steam account, default for community Unity transports. ⚠️ Devs report degraded relay behavior under 480; final validation needs your own App ID.
- Extending the relay to **non-Steam players** (other stores): 4 Valve conditions (ship a version on Steam, commit to updates, no availability guarantee, provide your own matchmaking/"game coordinator").

### 1.3 Technical integration (client-host P2P, Unity)

- **Core APIs**: `ISteamNetworkingSockets::CreateListenSocketP2P` (host) + `ConnectP2P` (clients), connection identity = SteamID. Exactly the current authoritative client-host model.
- **Community NGO transports** (drop in place of `UnityTransport` on the `NetworkManager`):
  - Facepunch Transport (`multiplayer-community-contributions`) — set `FacepunchTransport.targetSteamId`, then standard NGO.
  - `UnityNetcodeSteamP2PRelayTransport` — NGO `NetworkTransport` for SDR P2P relay. "Sample implementation" (~9 commits, no release) → harden before prod.
  - Heathen Engineering's Steamworks.NET transport.
- **Architecture consequence**: only the transport/connection layer changes (NGO stays). All custom rollback (prediction, inputs v2, snapshots, hashes) stays intact on top — SDR only provides the pipe + relay. SteamID replaces the Relay allocation + join code as the "address".
- ⚠️ Transports are **community-maintained**, no official Unity support: plan tests (existing network gates `-netMatchHost/-netMatchJoin` revalidate everything) and maintenance.

### 1.4 Lobbies: `ISteamMatchmaking` replaces Unity Lobby

- `CreateLobby` / `RequestLobbyList`, **free, included in Steamworks**.
- Limits: 50 results max per search, 1 Normal + 2 Invisible lobbies per user, 300 ms–5 s search latency. No impact for 2-6 players.
- Clean separation: "Matchmaking and lobbies do not provide networking features" — lobby = discovery/metadata, SDR = transport. Same pattern as current Unity Lobby + Relay.

### 1.5 NAT traversal and latency

- **Adaptive**: direct NAT punch-through first, automatic fallback to Valve relay; SDK can tell clients to drop back to direct UDP.
- Valve backbone can route **faster than the public internet** for many players.
- Dev-measured overhead: **connection establishment +1-3 s** at join (no in-match effect); direct < relay in ping when punch succeeds.
- Traffic authenticated, encrypted, rate-limited; **IPs never revealed** (anti-DoS) — better than Unity Relay on this point.
- **Rollback implication**: the relay detour adds a few ms, well within the current prediction window (measured p95 = 2 ticks at 150 ms simulated). Suitable profile.

### 1.6 SDR ↔ stats / OAuth / accounts: NO link

- SDR = "a virtual and private network that routes multiplayer content". No stats, no account auth, no OAuth.
- Separate Steamworks subsystems: `ISteamNetworkingSockets` = transport; `ISteamMatchmaking` = lobbies; `ISteamUser` = auth; `ISteamUserStats` = stats/player counts.
- SDR uses SteamID as connection identity (and prevents SteamID spoofing), but account auth runs independently. Adopting SDR commits nothing on stats/auth.

---

## §2 — Accounts, "Steam OAuth" and ELO/Rank

### 2.1 What actually exists (no OAuth2 at Steam)

Steam offers **no OAuth2/OIDC flow suited to a native game client**. (⚠️ nuance, July 2026: a *legacy partner* OAuth2 does exist — `partner.steamgames.com/doc/webapi_overview/oauth`, `ISteamUserOAuth/GetTokenDetails` — but it is **not** the path for a game client; the auth-ticket flow below is.) Two mechanisms matter here:

1. **Steam OpenID 2.0** (`steamcommunity.com/openid`) — **web-only** login. Returns just the SteamID64 via the Claimed ID `https://steamcommunity.com/openid/id/<steamid>`. No email, no private profile. Legacy protocol (never migrated to OIDC), still supported in 2026 but with reported intermittent reliability (503 on POST). → **Wrong tool for the Unity client.** Useful later only if a website (public leaderboard) wants a "Sign in through Steam" button.
2. **Web API auth tickets** — the right flow for a game client proving its identity to a custom backend (§2.2).

### 2.2 The canonical Unity client → backend flow (THE one to use)

- **Client**: `ISteamUser::GetAuthTicketForWebApi(identity)` (2023+ method, built for Web API validation). **Wait for the `GetTicketForWebApiResponse_t` callback** before using the ticket, then send it to the backend as a hex string.
- **Backend**: GET `https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/` with `key` (publisher key), `appid`, `ticket` (hex), `identity` (must match the client's). "Can never be used directly by clients" — secure server only.
- **Response**: `result`, `steamid`, `ownersteamid`, `vacbanned`, `publisherbanned`.
- The old `GetAuthSessionTicket` still exists (legacy, for game-server auth) — don't use it for an HTTP backend.

```
 UNITY CLIENT (Steamworks.NET)        BACKEND (OCI VM)              STEAM (partner.steam-api.com)
 =============================        ================              =============================
 1. SteamClient.Init(appID)                 |                                  |
 2. GetAuthTicketForWebApi(identity)        |                                  |
 3. <wait for callback                      |                                  |
    GetTicketForWebApiResponse_t>           |                                  |
 4. ticket -> hex                           |                                  |
    |---- POST /auth { ticket_hex } ------->|                                  |
    |                                5. GET AuthenticateUserTicket/v1          |
    |                                   (key=PUBLISHER, appid, ticket,         |
    |                                    identity)                             |
    |                                       |--------------------------------->|
    |                                       |<--- { result, steamid,           |
    |                                       |      ownersteamid, vacbanned,    |
    |                                       |      publisherbanned }           |
    |                                6. Checks: result==OK,                    |
    |                                   !vacbanned && !publisherbanned,        |
    |                                   steamid vs ownersteamid policy         |
    |                                7. UPSERT users (steamid64 PK),           |
    |                                   read/init ELO + RANK (PostgreSQL)      |
    |                                8. issue signed JWT (sub=steamid64)       |
    |<------ 200 { jwt, elo, rank } --------|                                  |
 9. JWT as Bearer for /queue,               |                                  |
    /match-result, /leaderboard...          |                                  |
    |                                       |                                  |
 [match P2P via SDR — transport only, NO link with backend auth]               |
```

### 2.3 Prerequisites & costs

- **App ID** (same as SDR: $100 Steam Direct).
- **Publisher Web API key** (tied to the publisher group + AppID), **never embedded in the client**, free API access.

### 2.4 Unity: Steamworks.NET, NOT Facepunch (for auth)

- **🐛 CRITICAL TRAP**: Facepunch.Steamworks' `GetAuthSessionTicket(Async)` returns a **TRUNCATED ticket** (~234 bytes instead of 1024) → Web API validation fails. Issue #827 opened July 2025, **unresolved as of June 2026**. In contrast, Steamworks.NET's `GetAuthTicketForWebApi` works.
- → **Steamworks.NET for auth.** (The NGO transport can stay Facepunch — different subsystems — but using Steamworks.NET everywhere avoids shipping 2 wrappers.)
- General trap: little error feedback if `Init`/`steam_appid.txt` are misconfigured — painful to debug.

### 2.5 Security (non-negotiable)

- Ticket validation **server-side only**; publisher key never in the client.
- Auth checks: `result == OK`, `!vacbanned`, `!publisherbanned` (exclude cheaters from ranked).
- **Family Sharing**: `ownersteamid != steamid` = borrowed game. Decide a policy (allow casual, restrict ranked to owner?) and log both IDs in DB.
- **IP allow-list** on the Web API key in Steamworks (the OCI IP).
- **Ownership gate (optional for ranked)**: `ISteamUser/CheckAppOwnership` Web API confirms the account owns the app — an extra server-side gate beyond `vacbanned`/`publisherbanned`.
- **Auth tickets are single-use**: cancel at session end; issue your own JWT once and never reuse the Steam ticket.

### 2.6 Number of online players

- `GET https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1?appid=<APPID>` — **public, no API key**, players currently active on the app.
- Steam Charts / SteamDB = third-party aggregators of this same data.
- **No link to SDR** (global Steam metric, not transport).
- ⚠️ App-global counter — "players in MY ranked queue" comes from our backend (sessions table / Redis counter), not Steam.

### 2.7 ELO/Rank: dedicated backend mandatory

- **Pattern**: Steam ticket = session bootstrap only → the backend issues **its own JWT** (sub = steamid64) for all subsequent calls (/queue, /match-result, /leaderboard).
- **SteamID64 = primary key** of `users` (stable 64-bit int, unique per account).
- **Native Steam bricks insufficient** for our need:
  - Steam Leaderboards (10,000 max/title): no custom matchmaking logic, writes come from the client (not server-authoritative unless pushed via Web API from our server).
  - Steam Stats: per-player storage, no server ELO calc or queue orchestration.
  - → The OCI/PostgreSQL backend is justified; Steam Leaderboards optional as a Top "showcase" (pushed from the backend).
- **⚠️ Client-host anti-cheat**: who reports the match result? The host = cheatable (client authority accepted until a dedicated arbiter exists). Mitigations from the start: **report by ALL clients + cross-check**, disagreement ⇒ match flagged/ignored, ELO gain caps, logging for review. Real fix = dedicated arbiter server (tracked as a GitHub issue; authority code already ready for it).

---

## §3 — OCI backend (accounts DB + ELO)

### 3.1 The OCI free tier in 2026

- **A1.Flex (Ampere ARM64): 2 OCPU + 12 GB RAM** total (1,500 OCPU-h + 9,000 GB-h/month = 2/12 continuous) — ⚠️ **Oracle halved this in 2026** (was 4 OCPU / 24 GB; confirmed oracle.com/cloud/free/faq + InfoQ Jul 2026); still ample for <1000 CCU. Plus **200 GB block storage**, **20 GB Object Storage**, **10 TB/month egress**.
- **⚠️ Risk #1 — idle reclamation**: Oracle may reclaim an A1 Always Free instance if over 7 days: CPU p95 < 20% AND network < 20% AND memory < 20%. Accounts idle 30d+: possible suspension, reclaimed resources not restorable.
- **Countermeasures (launch-blockers, not nice-to-haves)**: (1) upgrade to **Pay As You Go** (card, ~$100 auth) — Always Free Services stay free, billing only beyond quotas, and the account leaves the "abandoned/idle free" logic; (2) **`pg_dump` → Object Storage backups** — the free tier has **no automated DB backups**, so a reclamation = data loss without your own dumps. **Both required before any public launch.**
- ⚠️ Extra 2026 caveats: if a tenancy ever provisions **more A1 than the free allowance, all A1 instances are disabled then deleted after 30 days** unless upgraded; idle *accounts* untouched 30+ days risk suspension. PAYG neutralizes both.
- Home region fixed at account creation (irreversible) — no gameplay impact (netcode doesn't go through this API).

### 3.2 PostgreSQL on ARM64

- **Mature native support**: apt.postgresql.org provides arm64 binaries for Ubuntu LTS; operation identical to x86_64.
- **Backups**: `pg_dump` cron → OCI Object Storage (20 GB free, 50,000 req/month — ample for a small game's dumps).
- **Do NOT use Autonomous DB free**: 2 instances max, 20 GB, 1 OCPU, 30 sessions, **no manual backup or restore**, and it's Oracle DB (not Postgres) → lock-in, non-portable.
- **Reversible by design**: because it's plain PostgreSQL, migrating off OCI is a `pg_dump`/`pg_restore`. Keep **managed Postgres (Neon / Supabase free tier)** documented as the fallback if OCI ever becomes a liability — no lock-in either way, so the OCI choice is not a one-way door.

### 3.3 API stack & exposure

- **Go or Node/Fastify**: both native ARM64 without issue (language decision made when the auth API starts, cf. `docs/ROADMAP.md` § Open decisions).
- **Cloudflare free as HTTPS proxy** in front (orange cloud): masks the OCI IP, TLS + L7 anti-DDoS included. **Harden**: VM firewall accepting only Cloudflare IP ranges (otherwise the origin remains findable via DNS history).
- ⚠️ Cloudflare free proxy covers only HTTP/HTTPS (80/443) — not game UDP. Irrelevant: gameplay goes through SDR/Unity Relay, never OCI.

### 3.4 Capacity & Steam quota

- **<1000 CCU API = non-issue**: auth at login + ELO writes at match end = tens of req/s at peak; Fastify/Go + local Postgres handle thousands of req/s on 4 OCPU/24 GB.
- **Steam Web API: 100,000 calls/day per key**. We only authenticate at connection (1 call/session) → huge margin.

---

## §4 — Roadmap impact

| Work item | Recommendation from this research |
|---|---|
| **Accounts** | OCI backend: native ARM64 Postgres + Go/Node API + home-grown JWT. **Abstract the identity provider** (`IIdentityProvider` interface: anonymous UGS auth today → Steam ticket later) so the `users` table (PK steamid64, provider/provider_id columns) needn't be redone. **PAYG upgrade + pg_dump backups → Object Storage = launch-blockers** (free tier has no auto DB backup; A1 now 2 OCPU/12 GB). |
| **Ranked/ELO** | 100% custom backend (queue, ELO calc, ranks). Match report by ALL clients + cross-check from the design. Steam Leaderboards = optional showcase later. "Players in queue" counter = backend, not Steam. |
| **Steam** | Buy the App ID ($100). Migrate transport: Unity Relay + Unity Lobby → **SDR + `ISteamMatchmaking`** (community NGO transport to harden, rollback unchanged, rerun network gates). Auth: `GetAuthTicketForWebApi` via **Steamworks.NET** (NOT Facepunch — issue #827) + `AuthenticateUserTicket` on the backend + IP allow-list on the key. Checks `vacbanned`/`publisherbanned`/family sharing. **SDR prototyping possible now under App ID 480**, final validation under the real App ID. |
| **Lock-in to note** | SDR = players under the Steam client only. If a non-Steam build ever ships (itch.io…), keep Unity Relay as a parallel transport for those players (50 CCU then suffices as a secondary channel). |

### Bottom-line decision

**SDR is the superior Unity Relay replacement once the game is on Steam**: lifts the 50 CCU cap, free, anti-DDoS/masked IPs, lobbies included, identical client-host P2P architecture (rollback unchanged). The relay has **no** role in accounts/ELO/stats — the account system (Steam ticket → home-grown JWT → Postgres on OCI) is an orthogonal effort that can start (accounts issues) without waiting on Steam.
