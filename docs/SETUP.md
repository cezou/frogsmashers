# SETUP.md — dev environment for FrogSmashers

Work on this project from another machine. Target: **WSL2 (Ubuntu) on
Windows**, repo at `/mnt/c/frogsmashers` (`C:\frogsmashers`). Edit/commit from
WSL; launch the Windows `.exe` builds via interop.

> Method + paths: `AGENTS.md` (repo root). Roadmap: `docs/plans/`. Netcode
> rules: `docs/NETCODE.md`. Build/test/ship: the `test` and `ship` skills.

## 1. Install (the "requirements")

Unity packages are pinned in `HohimBrueh/Packages/manifest.json` and restore
on first open. Install on the machine:

| Dependency | Version / notes |
|---|---|
| **Unity Editor** | **6000.4.7f1** (exact), via Unity Hub. Module **Windows Build Support (IL2CPP)** (+ **Linux IL2CPP** only for the headless server). |
| Unity Hub | latest |
| WSL2 + Ubuntu | repo on the Windows drive so WSL runs Windows `Unity.exe` / game `.exe` via interop |
| git, GitHub CLI (`gh`) | `gh auth login` for releases |
| PowerShell interop | `powershell.exe` reachable from WSL (copy builds, kill procs, zip) |

Pinned packages: NGO 2.12.0, transport 2.6.0, services.multiplayer 2.2.3
(Relay/Lobby/Auth), inputsystem 1.19.0, StyleCop.Analyzers.

**UGS (online):** relay matches need the machine's Unity login to have access
to the repo's UGS project and be online. Offline gates need no network.

## 2. Paths

| What | Path |
|---|---|
| Repo (WSL) | `/mnt/c/frogsmashers/` |
| Unity project | `/mnt/c/frogsmashers/HohimBrueh/` |
| Editor exe (WSL) | `/mnt/c/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe` |
| Windows builds | `HohimBrueh/Builds/Windows/FrogSmashersV0.N/FrogSmashers.exe` |

## 3. Build / test / ship / screenshot

Use the skills (they hold the exact commands): **`test`** (build + gates +
EditMode + screenshot) and **`ship`** ("ship"/"push" → commit+push; "release"
→ `gh` pre-release). Golden rule: **no manual Unity Editor UI** — every Unity
op runs as a batch-mode Editor script (Editor closed; one instance per
project). See `AGENTS.md`.

Quick manual run (windowed, with log):
```bash
EXE="/mnt/c/frogsmashers/HohimBrueh/Builds/Windows/FrogSmashersV0.N/FrogSmashers.exe"
"$EXE" -logFile "C:\frogsmashers\playtest.log" &   # 2nd instance: add -authProfile guest
```
Kill with `powershell.exe -Command "Stop-Process -Name FrogSmashers -Force"` (not WSL `kill`).

## 4. Secrets

Confidential infra (OCI IP/user + SSH key) lives in gitignored **`.env`** at
the repo root — never committed. Copy it from your primary machine (it's not
in git). Load: `set -a; . ./.env; set +a`. Recreate the SSH key per the
comment in `.env`:
`echo "$OCI_SSH_KEY_B64" | base64 -d > "$OCI_SSH_KEY" && chmod 600 "$OCI_SSH_KEY"`.
The build bakes in no server IP (online = Unity Relay); keep it so until
Cloudflare fronts the backend (Phase 7).
