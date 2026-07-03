---
name: test
description: Verify FrogSmashers after ANY gameplay, netcode or sim logic change (or on "test" / "verify" / "run the gates" / "check it works"). Builds and runs the automated gates (offline determinism/rollback + real-relay match/lobby) and Unity EditMode unit tests, and can capture a screenshot of a chosen state for visual checks. Run before shipping.
---

# Test FrogSmashers

Two layers: **gates** (integration, always available) and **EditMode unit
tests** (pure logic, if set up). Build first, then run.

## 0. Build (Editor CLOSED)
```bash
EXE_DIR="/mnt/c/frogsmashers/HohimBrueh/Builds/Windows"
"/mnt/c/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe" \
  -batchmode -nographics -projectPath "C:\frogsmashers\HohimBrueh" \
  -executeMethod FrogSmashers.Editor.WindowsBuilder.Build -quit -logFile - 2>&1 \
  | grep -E "(\[WindowsBuilder\]|error CS|Build succeeded|FAILED)" | tail
EXE="$EXE_DIR/$(ls -t "$EXE_DIR" | head -1)/FrogSmashers.exe"
```

## 1. Offline gates (fast, single process; exit 0 pass / 1 fail / 2 inconclusive)
```bash
for g in determinismTest snapshotTest inputPipeTest rollbackTest; do
  "$EXE" -batchmode -nographics -$g -logFile "C:\frogsmashers\g-$g.log"; echo "$g -> $?"; done
```
Scaling flags: `-netPlayers 4`, `-teamMode` (run these on determinism +
rollback too after team/roster changes).

## 2. Relay gates (2 processes, real Unity relay)
Host + join, 2nd with a distinct `-authProfile`; host gets a ~3 s head
start. **Warm-up first** — a cold relay flakes on the first connect
(`join code not found`); rerun before judging.
```bash
"$EXE" -batchmode -nographics -netLobbyHost -scriptedLocal -authProfile h -logFile "C:\frogsmashers\host.log" &
powershell.exe -Command "Start-Sleep -Seconds 3" >/dev/null
"$EXE" -batchmode -nographics -netLobbyJoin -scriptedLocal -authProfile g -logFile "C:\frogsmashers\join.log" &
powershell.exe -Command "try { Get-Process FrogSmashers | Wait-Process -Timeout 200 } catch {}" >/dev/null
grep -hE "PASS|FAIL|desyncs" /mnt/c/frogsmashers/host.log /mnt/c/frogsmashers/join.log
```
Variants: `-netMatchHost/-netMatchJoin`, `-injectDesync`,
`-simLatency 150 -simJitter 30 -simLoss 5 -simSeed N`, `-netPlayers 4`.
Kill stuck instances with `powershell.exe -Command "Stop-Process -Name FrogSmashers -Force"` (WSL `kill` won't).

## 3. Unity EditMode unit tests (if `Assets/Tests/EditMode` exists)
```bash
"/mnt/c/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe" \
  -batchmode -runTests -projectPath "C:\frogsmashers\HohimBrueh" \
  -testPlatform EditMode -testResults "C:\frogsmashers\testresults.xml" -logFile - 2>&1 | tail
# result: exit 0 = pass; parse testresults.xml for details
```

## 4. Visual capture (screenshot — needs rendering, NO -nographics)
For UI / visual verification, boot a state and read the PNG (`ScreenshotHarness.cs`):
```bash
"$EXE" -screenshot -shotScene MainMenu -shotDelay 3 -shotPath "C:\frogsmashers\shot.png" \
  -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "C:\frogsmashers\shot.log" &
powershell.exe -Command "try { Get-Process FrogSmashers | Wait-Process -Timeout 40 } catch {}" >/dev/null
# then Read /mnt/c/frogsmashers/shot.png (the Read tool renders PNGs)
```
`-shotScene <name>` (MainMenu, JoinScreen, a level) loads it directly; or add
`-netLobbyHost -scriptedLocal -shotDelay 8` to capture the solo online lobby.
Needs a GPU/desktop session.

## What to unit-test (pure, deterministic — no scene needed)
Best candidates: `InputPacking` pack/unpack round-trip, `DeterministicRng`
sequences, `MatchSnapshot` save→restore→hash equality, `MatchClinched` /
`LeadingSlot` logic, `InputRingBuffer` confirm/predict/contiguous. The
gates already integration-cover the full sim; unit tests pin the small
pieces fast. Note: the between-round score→next-round transition is NOT
gate-covered (scripted matches never finish a round) — verify by a manual
2-instance playtest.

## Coverage rule
After a sim/netcode change, all gates must be green before shipping. If you
touched team/roster/membership, add `-teamMode` and `-netPlayers 4` runs.
