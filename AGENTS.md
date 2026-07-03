# AGENTS.md — FrogSmashers

Guide for AI agents on this repo. What the project is, the critical paths,
and the working method. Build/test/ship details live in the skills, not here.

## Project

**FrogSmashers** — a 2D local-multiplayer brawler (up to 6 players couch-coop
on one PC; Xbox/PS/keyboard) on Unity 6 (migrated from 2019.2). Goal: make it
**playable online** over the internet on top of the local mode. Roadmap:
`docs/plans/PLANGLOBAL.md`; active phase: `docs/plans/PHASE4.md`. Netcode
design + rules: `docs/NETCODE.md`.

## Environment

Code lives on Windows; development (editing, git, shell, SSH) runs from
**WSL2 (Ubuntu)** on the same machine. WSL launches Windows `.exe`s directly
via interop (no PowerShell wrapper needed). Paths map `C:\… ↔ /mnt/c/…`.

| What | Path |
|---|---|
| Repo root (WSL) | `/mnt/c/frogsmashers/` |
| Unity project | `/mnt/c/frogsmashers/HohimBrueh/` |
| C# / assets | `HohimBrueh/Assets/` |
| Editor scripts | `HohimBrueh/Assets/Editor/` |
| Unity Editor (WSL) | `/mnt/c/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe` |
| Editor.log (WSL) | `/mnt/c/Users/cesaire/AppData/Local/Unity/Editor/Editor.log` |

## Golden rule: no manual Unity UI

The user does **not** open the Unity Editor to click through operations
(create scenes, drag GameObjects, configure Inspectors, File > Build).
Everything is done by **Editor scripts** in `Assets/Editor/`, exposed as a
`[MenuItem]` and/or a static method invoked in batch mode:

```bash
"/mnt/c/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "C:\frogsmashers\HohimBrueh" \
  -executeMethod Namespace.Class.Method -quit -logFile -
```

Unity allows **one instance per project**: the Editor must be closed before a
batch run, or it fails with "Multiple Unity instances cannot open the same
project". New Unity-side work (scene, component, build target) = a new Editor
script, never manual clicks.

| Script | Method | Role |
|---|---|---|
| `BuildWindows.cs` | `FrogSmashers.Editor.WindowsBuilder.Build` | Windows x86_64 client, auto-incrementing output `Builds/Windows/FrogSmashersV0.N/` |
| `BuildLinuxServer.cs` | `FrogSmashers.Editor.LinuxServerBuilder.Build` | Linux x86_64 headless server |
| `BuildLinuxArm64Server.cs` | `FrogSmashers.Editor.LinuxArm64ServerBuilder.Build` | Linux ARM64 headless server |
| `GenerateMainMenuScene.cs` | `FrogSmashers.Editor.MainMenuGenerator.Generate` | regenerates `MainMenu.unity` |

## Build / test / ship → use the skills

- **`test`** — build + all verification gates (offline determinism/rollback,
  real-relay match/lobby) + EditMode unit tests. Run after any sim/netcode/
  gameplay change.
- **`ship`** — "ship"/"push" → commit + push to `master`; "release" → `gh`
  pre-release. Carries the commit/release conventions (English, minimal text,
  one feat per commit, signed).

Don't retype these command sequences here; the skills own them
(`.claude/skills/`).

## Screenshot / visual verify

Agent-side visual checks with no human input: `ScreenshotHarness`
(`Assets/Scripts/Net/Sim/ScreenshotHarness.cs`) boots a scene or the solo
lobby and writes a PNG to Read. Commands live in the **`test`** skill
(§Visual capture).

## USB key (2nd-PC testing)

Online is tested between this PC and a 2nd PC via a USB key (`E:`). After a
Windows build, if `E:` is mounted, copy the build folder onto it; the 2nd PC
runs from the key and writes `player-*.log` beside the exe — bring the key
back and read those logs to debug the remote side. Skip silently if `E:` is
absent (often on the other PC).

## OCI server

An Oracle Cloud ARM64 VM exists but is **not used for gameplay** since Phase 3
(online = rollback client-host on Unity Relay). It's a Phase 5 backend
candidate. **Secrets (IP, user, SSH key) are in gitignored `.env`** — load
with `set -a; . ./.env; set +a`, recreate the key per the comment in `.env`.
Never commit secrets; the confidential IP is replaced by a Cloudflare hostname
before any distributed build (Phase 7).

## Code standard

SquidNorm hybrid-C#: methods / public
fields / types `PascalCase`; locals / private fields `camelCase` (no `_`);
interfaces `I`-prefixed; ≤ 80 cols; XML `///` above types/methods only, no
inline comments; English everywhere. Enforced by **StyleCop.Analyzers**
(`.editorconfig`) — violations show as build warnings (the `test`/`ship`
builds must be warning-clean).

## Git

- Branch **`master`**, push direct (no PR).
- Releases are **`gh` pre-releases** (build is IP-free — gameplay uses Relay);
  a full public release is gated on Cloudflare (Phase 7). Mechanics + commit/
  release wording: the `ship` skill.

## Stack

Unity 6 (6000.4.7f1) · C# (.NET Standard 2.1 / IL2CPP) · InputSystem 1.19 ·
NGO 2.x + UnityTransport + UGS (Relay/Lobby/Auth, anonymous) · StyleCop 1.1.118.
Platforms: Windows x86_64 client, Linux x86_64 / ARM64 headless server.

## Working conventions (durable lessons)

Captured here because Claude's local auto-memory does **not** travel between
machines:

- **Stay in scope.** Don't add unrequested "bonus" changes, especially to
  working tooling — a "helpful" `Launch.bat` rewrite once broke `player.log`.
  Propose a side-improvement in one line and wait for a yes; a yes/no given
  under a wrong framing is not a mandate.
- **Port local features by reusing the real local UI/systems**, never by
  reinventing them (the actual prefabs / icon prompts / `JoinCanvas`, not an
  IMGUI text overlay). And wire the **live object**, not just netcode
  metadata: e.g. `CharacterAnimator` reads `Player.color` every frame, so a
  color change must set the live `Player.color` to show up.
