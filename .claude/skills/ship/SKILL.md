---
name: ship
description: Finalize and ship FrogSmashers work. Trigger words decide the action — "ship" or "push" → commit + push to master; "release" → cut a GitHub pre-release (build + zip + notes). Runs hygiene, build and gates first. Use when the user says ship, push, or release.
---

# Ship FrogSmashers

Pick the action from the user's word:

- **"ship" / "push"** → finalize, commit, and **push to `master`**.
- **"release"** → do the ship steps, then **cut a pre-release** (build zip +
  notes) on GitHub.

Do the steps in order. Stop and report if a step fails.

## 1. Code hygiene (SquidNorm hybrid-C#)
- No comments inside function bodies. XML `///` docs **above** types/methods
  only. Move stray comments into a doc line above, or delete.
- `using` directives at the top of the file, never mid-file.
- ≤ 80 columns. Methods/public fields/types `PascalCase`; locals/private
  fields `camelCase` (no `_`); interfaces `I`-prefixed. English everywhere.
- Sim/netcode code follows `docs/NETCODE.md` rules (no `Time.*` in sim, seeded
  RNG only, snapshot every mutable field, no `Destroy` on sim objects, guard
  non-deterministic SFX/FX, etc.).

## 2. Build (lint = warning-clean build; Editor must be CLOSED)
```bash
"/mnt/c/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "C:\frogsmashers\HohimBrueh" \
  -executeMethod FrogSmashers.Editor.WindowsBuilder.Build -quit -logFile - 2>&1 \
  | grep -E "(\[WindowsBuilder\]|error CS|warning SA|Build FAILED|Build succeeded)" | tail
```
Must show `Build succeeded`, no `error CS`. Address new `warning SA…`
(StyleCop) — there is no auto-formatter (no `dotnet` CLI); fix by hand per
`.editorconfig`.

## 3. Tests — run `/test` (see the test skill)
After ANY sim or netcode change, **all automated gates must pass** (offline
+ relay). Add/adapt unit tests for new pure logic (see the test skill). Do
not ship red gates.

## 4. Commit — Conventional Commits
Format: `type(scope): summary`, a blank line, then the body.
```
type(scope): imperative summary, lower-case, no trailing period

- terse body bullets, only if they add info
- English, changes only, NO phase/plan jargon (no "Phase 4")

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
- **type**: `feat` / `fix` / `docs` / `refactor` / `test` / `perf` / `build` /
  `ci` / `chore`. **scope** = touched area (`netcode`, `lobby`, `sim`, `ui`,
  `dev-setup`, …).
- One commit per distinct change (squash intermediate steps). Sign every
  commit with the `Co-Authored-By` trailer.

## 5a. push / ship
```bash
git push origin master   # direct to master, no PR
```
(If on another branch, that's fine for docs/wip; gameplay goes to master.)

## 5b. release
1. Build (step 2) → note the new `FrogSmashersV0.N` folder.
2. Zip it: `powershell.exe -Command "Compress-Archive -Path 'C:\frogsmashers\HohimBrueh\Builds\Windows\FrogSmashersV0.N\*' -DestinationPath 'C:\frogsmashers\FrogSmashers-vX.Y.zip' -Force"` (stop the game first if a file is locked).
3. Next tag = bump the last `vX.Y` (ask if unsure).
4. `gh release create vX.Y --prerelease --target master --title "vX.Y" --notes "<notes>" FrogSmashers-vX.Y.zip`
   - To update an existing release's build: `gh release upload vX.Y FrogSmashers-vX.Y.zip --clobber`.
5. Confirm the binary carries **no secret** (no OCI IP; online uses Unity
   Relay). No public non-prerelease build until Cloudflare fronts the
   backend.

## Commit & release text — least text, most concision, ENGLISH
Trim hard. Player-facing, changes only.
- **Commit**: Conventional Commits (§4) — `type(scope): summary`; terse body
  bullets only if they add info.
- **Release notes**: a short bullet list of feat/fix, summarized. No
  headings, no preamble, no template, no internal jargon. Match existing
  tags' tone. Example:
  ```
  - Fixed a host freeze when another player disconnects mid-game.
  - Dead lobbies are cleaned up; joining an unreachable game no longer hangs.
  ```
