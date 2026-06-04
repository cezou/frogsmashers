using System.Collections.Generic;
using FreeLives;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Offline determinism gate, activated with the -determinismTest CLI
    /// arg: runs the match scene twice with the same seed and scripted
    /// inputs, hashing the full sim state every tick. Both runs must
    /// produce identical hash streams, else the sim reads wall-clock or
    /// unseeded randomness somewhere. Exit codes: 0 pass, 1 fail,
    /// 2 inconclusive.
    /// </summary>
    public class DeterminismHarness : MonoBehaviour, ISimTickable
    {
        enum Mode
        {
            Replay,
            Snapshot,
        }

        const string levelName = "1BusStop";
        const ulong testSeed = 0xF706C0DEUL;
        const int ticksPerRun = 1800;
        const int minTicks = 300;
        const uint snapStartTick = 600;
        const uint snapEndTick = 900;

        /// <summary>True when the harness drives the current process.</summary>
        public static bool Active { get; private set; }

        static DeterminismHarness instance;
        static Mode mode;

        readonly List<uint> runA = new List<uint>(ticksPerRun);
        readonly List<uint> runB = new List<uint>(ticksPerRun);
        SnapshotRingBuffer ring;
        int runIndex;
        bool recording;
        bool restoredOnce;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (HasCliArg("-determinismTest"))
                mode = Mode.Replay;
            else if (HasCliArg("-snapshotTest"))
                mode = Mode.Snapshot;
            else
                return;

            Active = true;
            Debug.Log($"[DeterminismHarness] Active, mode={mode}");
            Application.targetFrameRate = 300;
            QualitySettings.vSyncCount = 0;
            SimulationDriver.ForcedTicksPerFrame = 10;
            InputReader.ActiveSource = new ScriptedInputSource();

            var host = new GameObject("DeterminismHarness");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<DeterminismHarness>();
            instance.ring = new SnapshotRingBuffer(1024);
            SceneManager.sceneLoaded += instance.OnSceneLoaded;
        }

        /// <summary>Harness hashes after everything else has ticked.</summary>
        public int SimOrder
        {
            get { return 10000; }
        }

        void Start()
        {
            BeginRun();
        }

        void BeginRun()
        {
            SimulationDriver.Paused = true;
            recording = false;
            GameController.activePlayers.Clear();
            GameController.activePlayers.Add(new Player(
                InputReader.Device.Gamepad1, Color.red, 0));
            GameController.activePlayers.Add(new Player(
                InputReader.Device.Gamepad2, Color.blue, 1));
            GameController.isTeamMode = false;
            SceneManager.LoadScene(levelName);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != levelName)
            {
                if (recording)
                {
                    Debug.Log("[DeterminismHarness] INCONCLUSIVE: scene"
                        + $" changed to '{scene.name}' mid-run");
                    Quit(2);
                }
                return;
            }

            SimClock.ResetForNewMatch();
            DeterministicRng.Match.Reseed(testSeed);
            SimulationDriver.Register(this);
            recording = true;
            SimulationDriver.Paused = false;
            Debug.Log($"[DeterminismHarness] Run {runIndex} started");
        }

        public void SimTick(float dt)
        {
            if (!recording)
                return;
            if (mode == Mode.Snapshot)
            {
                TickSnapshotMode();
                return;
            }

            var hashes = runIndex == 0 ? runA : runB;
            hashes.Add(ComputeHash());

            bool roundOver =
                GameController.State == GameState.RoundFinished;
            if (hashes.Count < ticksPerRun && !roundOver)
                return;

            recording = false;
            SimulationDriver.Unregister(this);
            if (runIndex == 0)
            {
                runIndex = 1;
                BeginRun();
            }
            else
            {
                Compare();
            }
        }

        void TickSnapshotMode()
        {
            ring.Save(SimClock.CurrentTick);
            uint hash = ComputeHash();

            if (!restoredOnce)
            {
                runA.Add(hash);
                if (GameController.State == GameState.RoundFinished)
                {
                    Debug.Log("[DeterminismHarness] INCONCLUSIVE:"
                        + " round ended before snapshot window closed");
                    Quit(2);
                    return;
                }
                if (SimClock.CurrentTick < snapEndTick)
                    return;
                var snap = ring.TryGet(snapStartTick);
                if (snap == null || !GameController.RestoreFrom(snap))
                {
                    Debug.Log("[DeterminismHarness] FAIL:"
                        + $" snapshot restore of tick {snapStartTick}");
                    Quit(1);
                    return;
                }
                restoredOnce = true;
                Debug.Log("[DeterminismHarness] Restored tick"
                    + $" {snapStartTick}, resimulating");
                return;
            }

            runB.Add(hash);
            if (runB.Count < (int)(snapEndTick - snapStartTick))
                return;

            recording = false;
            SimulationDriver.Unregister(this);
            for (int i = 0; i < runB.Count; i++)
            {
                int tickIndex = (int)snapStartTick + i;
                if (runA[tickIndex] != runB[i])
                {
                    Debug.Log("[DeterminismHarness] FAIL: resim hash"
                        + $" mismatch at tick {tickIndex + 1}:"
                        + $" live={runA[tickIndex]:X8} resim={runB[i]:X8}");
                    Quit(1);
                    return;
                }
            }
            Debug.Log("[DeterminismHarness] PASS: restore at tick"
                + $" {snapEndTick} back to {snapStartTick}, resim of"
                + $" {runB.Count} ticks bit-identical to live run");
            Quit(0);
        }

        uint ComputeHash()
        {
            uint h = StateHash.Seed;
            h = StateHash.Mix(h, SimClock.CurrentTick);
            h = StateHash.Mix(h, DeterministicRng.Match.State);
            h = GameController.HashSimState(h);
            return h;
        }

        void Compare()
        {
            int common = Mathf.Min(runA.Count, runB.Count);
            for (int i = 0; i < common; i++)
            {
                if (runA[i] != runB[i])
                {
                    Debug.Log("[DeterminismHarness] FAIL: hash mismatch"
                        + $" at tick {i + 1}:"
                        + $" runA={runA[i]:X8} runB={runB[i]:X8}");
                    Quit(1);
                    return;
                }
            }
            if (runA.Count != runB.Count)
            {
                Debug.Log("[DeterminismHarness] FAIL: identical prefix"
                    + $" but run lengths differ (runA={runA.Count}"
                    + $" runB={runB.Count} ticks)");
                Quit(1);
                return;
            }
            if (runA.Count < minTicks)
            {
                Debug.Log("[DeterminismHarness] INCONCLUSIVE: round ended"
                    + $" after only {runA.Count} ticks");
                Quit(2);
                return;
            }
            Debug.Log("[DeterminismHarness] PASS:"
                + $" {runA.Count} ticks bit-identical across 2 runs");
            Quit(0);
        }

        static void Quit(int code)
        {
            Application.Quit(code);
        }

        static bool HasCliArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name)
                    return true;
            }
            return false;
        }
    }
}
