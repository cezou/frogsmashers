# PHASE4.md — Netcode/lobby solidification + multi-match

> See `docs/plans/PLANGLOBAL.md` for global context, `AGENTS.md` for methodology. Netcode: `docs/NETCODE.md`.

## Goal

Harden the Phase 3 online netcode and **restore parity with the local game**. Four work items (decided June 2026):

- ✅ **A — Inter-round score screen + winner** (online had lost them).
- ✅ **B — Multi-match lobbies**: return to the lobby after a match (not the menu) to replay.
- ✅ **C — Color choice + team mode** online (deferred from 3.11).
- ✅ **D — Rollback solidification**: dedupe SFX replayed during resim.

> Status: ✅ 4.1–4.4 code done · ⬜ manual 2-instance playtest + commit still pending (see below).

Out of scope: 2-real-machine soak (3.14, deferred); accounts/ranked/Steam/release (Phases 5-7).

## Status

- ✅ **4.1** Inter-round score screen + "PLAYER WINS" (instead of Outro).
- ✅ **4.2** Return to lobby after match (multi-match, lobby re-published).
- ✅ **4.3** Color choice + teams online — **reuses local UI** (`JoinCanvas`): the player's frog appears mid-platform (`Terrain.GetSpawnPoint(slot)`), **frozen** (local input gated to neutral at the `RollbackNetDriver.SimTick` chokepoint) until the player confirms. The **default icons** of `chooseColorCanvas` are shown (no more IMGUI text overlay; `JoinCanvas.Update` short-circuited online and driven by `OnlineLobbyOverlay`). Buttons = local mapping: **B** color/team, **L/R** shade, **X** confirm, **Y** back; host only **SELECT** = team mode/FFA. Color changes **live** on the frog (update the live `Player.color`, read each frame by `CharacterAnimator` — the "inert buttons" bug came from only writing network metadata). Full lobby (4) → forced 10 s start (canceled if someone leaves). Color/team = control-plane metadata (outside sim hash), locked at match start. Sim model unchanged vs v2 (gates green); no "spawn on confirm" membership rework (determinism risk avoided, choice user-approved).
- ✅ **4.4** Rollback SFX dedup: `SimFx` (keyed by slot + call site) always plays on the forward pass and only re-fires in resim for ticks not yet played → no duplication, **without muting** a first-applied remote action in resim (lesson from revert `f765390`).

**Gates** (build V0.54): all green — offline (determinism/snapshot/inputPipe/rollback in FFA, **team mode**, **4 players**), network (match, match `-simLatency 150 -simJitter 30 -simLoss 5` p95=1, `-injectDesync` repaired, lobby). New gate flag `-teamMode` (determinism + lobby).

**Remaining**: manual 2-instance playtest (the score-screen / lobby-return / color-team selection / 4-player timer flow isn't gate-covered — scripted matches don't finish a round). Not yet committed (commit on request).

## Framing facts

- The naive fix "gate all SFX behind `!IsResimulating`" (commit `f765390`) was **reverted** (`2975803`): a remote player's action is **first applied during a resim pass**, so blanket gating muted their hits/jumps/tongues under any ping. Any SFX fix must dedupe **replayed** effects without muting **first-applied** ones.
- Online currently short-circuits the local `ScoreScreen` and `Outro` scenes: round end routes to `OnlineMatch.OnRoundFinished()` (`GameController.cs:728`), which jumps straight to the next level (`OnlineMatch.cs:304`) or to `MainMenu` via `EndMatch()`→`LeaveLocal()` (`OnlineMatch.cs:344-356`).

## Reused existing building blocks

| Need | Existing | Path |
|---|---|---|
| Host-authoritative scene sync | `sceneReadyClients`/`goSent`/`localSceneReady` + `BeginScene` | `Net/OnlineMatch.cs:457-527,566` |
| Epoch-bumped transition | `TransitionTo` / `OnMatchStart` / `SendMatchStart` | `Net/OnlineMatch.cs:463-513` |
| Score UI + 5 s timer | `ScoreScreenController` (`ScoreScreen` scene) | `UI/ScoreScreenController.cs` |
| Winner label | `PlayerScoreDisplay.TemorarilyDisplay(text, secs)` | `UI/PlayerScoreDisplay.cs` |
| Wins per slot | `matchWinsBySlot[]` | `Net/OnlineMatch.cs:62` |
| Palette + color pool | `playerColors[]`, `GetAvailableColor()`, `ReturnColor()` | `Controllers/GameController.cs:51-53,996-1007` |
| Team data + rules | `Player.team`, friendly-fire, team score, `isTeamMode` | `Player.cs:16`, `Character.cs:993-1010`, `GameController.cs:65,857-909` |
| Color/team picker | ChooseColor state of `JoinCanvas` | `UI/JoinCanvas.cs:54-170` |
| Resim flag | `SimulationDriver.IsResimulating` | `Net/Sim/SimulationDriver.cs:30` |

## Sub-steps (each independently testable)

### ✅ 4.1 — Inter-round score screen + winner (A)
Replace the silent `TransitionTo(currentLevel+1, …)` jump with a host-driven **score-screen interlude**, shown in lockstep by all peers, then advance to the next level.

- Reuse `ScoreScreen` scene + `ScoreScreenController`, fed by `matchWinsBySlot`/roster; **gate auto-advance and next-level `LoadScene` behind `!OnlineMatch.Active`**, host drives the advance.
- Host coordinates via the existing handshake: bump epoch → "show score screen" message → wait for `sceneReadyClients` → hold ~5 s → real transition to next level.
- **Match end**: instead of `Outro`, score screen with `PlayerScoreDisplay.TemorarilyDisplay("PLAYER N WINS", …)` then return to lobby (4.2). No `Outro` scene online.
- Exit test: 2 instances, multi-level match → score screen between each round with correct wins on each peer; last round shows "PLAYER N WINS" instead of `Outro`. `-netLobbyHost/-netLobbyJoin` gate green (non-sim UI → hash unchanged; verify epoch drops in-flight sim messages).

### ✅ 4.2 — Multi-match lobbies: return to lobby (B)
After a match ends, all peers **return to the lobby** (`JoinScreen`) ready for another match, instead of `LeaveLocal()`→`MainMenu`.

- `RETURN_TO_LOBBY` path: `EndMatch()` (after the 4.1 winner screen) bumps epoch and transitions each peer to `Phase.Lobby`/`lobbyScene` instead of tearing everything down. Reuse the `HostStartLobby` bootstrap.
- Reset match state: clear `matchWinsBySlot`, clear ready, re-arm ready/countdown loop (`LobbyFrameUpdate`). Keep roster, slots, relay allocation and NetSession alive.
- Re-publish the UGS lobby entry (it was `Unpublish()`ed at countdown, `OnlineMatch.cs:382`) to be discoverable again between matches.
- `LeaveLocal()`→`MainMenu` stays for an explicit leave (double-Escape / `LeaveConfirm`).
- Exit test: 2 instances finish a match → return to playable lobby, can re-ready and start a 2nd match; a 3rd instance can discover and join the lobby between matches. Gates green.

### ✅ 4.3 — Color choice + team mode online (C)
Color and team chosen in the lobby instead of the hardcodes `slotColors[slot]`/`isTeamMode=false` (`OnlineMatch.cs:544,563,741`).

- Extend `RosterEntry` (`OnlineMatch.cs:33-40`) with `Team` and `Color` (or a palette index) and serialize them in roster broadcast, `LobbyWelcome`, `AddPlayer`.
- Lobby selection: reuse the `JoinCanvas` ChooseColor pattern (B = cycle color from pool / toggle team; host A = toggle team mode). Host authoritative on the team flag and color-conflict resolution.
- `BuildActivePlayers`/`MakePlayer`: pass the chosen roster color and set `GameController.isTeamMode` from the host flag; team rules (friendly-fire, score) already wired once `isTeamMode=true` + `team`.
- Determinism: color is cosmetic (safe), but `isTeamMode` and `team` affect sim → fixed at match start, identical on all peers, part of match-start config (not changeable mid-match). Add to the gates' scripted config.
- Exit test: 2-4 instances pick distinct colors + toggle team; the match spawns correct colors/teams on each peer; friendly-fire and team win condition as in local. `-determinismTest` and `-netLobbyHost/Join` green with team mode.

### ✅ 4.4 — Rollback solidification: SFX dedup (D)
Fix duplicated SFX/particles under rollback **without** re-muting remotes (lesson from revert `f765390`).

- Approach: emit each sim-path effect **at most once per sim tick** rather than gating the whole resim. Track, per effect site (or globally per tick), the highest tick already emitted; during a sim/resim pass, only fire if that tick hasn't emitted that effect. A **first-applied** remote action in resim sounds (its tick never emitted); a re-pass of an already-seen tick stays silent. Centralize in a helper (e.g. `SimFx.Play/Spawn`) keyed on tick-emitted, not `IsResimulating`.
- Apply to unguarded sim SFX/particles (PLANGLOBAL audit §2): `Character.cs` bat/jump/land/tongue/burp/collision + `Create*Effect`, `GameController` spawn FX. Keep KO/victory guards as-is.
- Validate by ear on a 2-instance run under `-simLatency 150 -simLoss 5` (frequent rollbacks): remote hits/jumps/tongues audible, no machine-gun duplication. No determinism regression (non-sim effects, but rerun all gates per the netcode rule).

## ⬜ Additional solidification candidates (if time)
- Magic numbers / ghost-slot assumption in `MatchClinched` (`OnlineMatch.cs:323-340`).
- `matchOverLevel = 255` sentinel and `byte` epoch overflow guard (`OnlineMatch.cs:59,470`).
- Membership edge cases on lobby return (parked players, pending removals).

## Verification (whole phase)

1. **All gates green** after each sub-step (netcode law): `-determinismTest`, `-snapshotTest`, `-inputPipeTest`, `-rollbackTest` (+ `-netPlayers 4`), `-netMatchHost/-netMatchJoin` (+ `-simLatency 150 -simJitter 30 -simLoss 5`, + 4 instances, + `-injectDesync`), `-netLobbyHost/-netLobbyJoin`. Relay warm-up run before judging.
2. **Manual 2-instance run, 1 PC** (2nd as `-authProfile guest`): lobby → color/team choice → ready → match → score screen each round → "PLAYER N WINS" on the last → lobby return → 2nd match.
3. **Local non-regression**: a 100%-local game still shows `ScoreScreen` + `Outro` unchanged (online branches guarded on `OnlineMatch.Active`).
4. Build + (if E: key present) copy; commit per feature on `master`, **no release** (Cloudflare gate, Phase 7).
