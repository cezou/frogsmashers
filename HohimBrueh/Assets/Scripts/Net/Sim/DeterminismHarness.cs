using System.Collections.Generic;
using FreeLives;
using FrogSmashers.Net.Rollback;
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
            InputPipe,
            Rollback,
        }

        /// <summary>
        /// Feeds scripted inputs through the rollback input buffer at
        /// the start of every tick, so characters read them back via
        /// RollbackInputSource (pack/unpack round-trip in real sim).
        /// </summary>
        class InputFeeder : ISimTickable
        {
            readonly InputRingBuffer buffer;
            readonly ScriptedInputSource script =
                new ScriptedInputSource();
            readonly InputState scratch = new InputState();

            public InputFeeder(InputRingBuffer buffer)
            {
                this.buffer = buffer;
            }

            public int SimOrder
            {
                get { return -100; }
            }

            public void SimTick(float dt)
            {
                Feed(InputReader.Device.Gamepad1, 0);
                Feed(InputReader.Device.Gamepad2, 1);
            }

            void Feed(InputReader.Device device, int slot)
            {
                script.Read(device, scratch);
                buffer.Confirm(slot, SimClock.CurrentTick,
                    InputPacking.Pack(scratch));
            }
        }

        /// <summary>
        /// Confirms slot 0 at the current tick but slot 1 only 5 ticks
        /// late, so slot 1 is simulated from predictions first and the
        /// rollback loop must repair every wrong guess.
        /// </summary>
        class DelayedFeeder : ISimTickable
        {
            public const uint Delay = 5;

            readonly InputRingBuffer buffer;
            readonly InputState scratch = new InputState();

            public DelayedFeeder(InputRingBuffer buffer)
            {
                this.buffer = buffer;
            }

            public int SimOrder
            {
                get { return -100; }
            }

            public void SimTick(float dt)
            {
                uint tick = SimClock.CurrentTick;
                ScriptedInputSource.ReadForTick(
                    tick, InputReader.Device.Gamepad1, scratch);
                buffer.Confirm(0, tick, InputPacking.Pack(scratch));
                if (tick <= Delay)
                    return;
                uint late = tick - Delay;
                ScriptedInputSource.ReadForTick(
                    late, InputReader.Device.Gamepad2, scratch);
                buffer.Confirm(1, late, InputPacking.Pack(scratch));
            }
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
        readonly uint[] runATicks = new uint[ticksPerRun];
        readonly uint[] runBTicks = new uint[ticksPerRun];
        SnapshotRingBuffer ring;
        InputRingBuffer inputBuffer;
        ISimTickable feeder;
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
            else if (HasCliArg("-inputPipeTest"))
                mode = Mode.InputPipe;
            else if (HasCliArg("-rollbackTest"))
                mode = Mode.Rollback;
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
            if (mode == Mode.InputPipe && runIndex == 1)
            {
                inputBuffer = new InputRingBuffer();
                feeder = new InputFeeder(inputBuffer);
                InputReader.ActiveSource =
                    new RollbackInputSource(inputBuffer);
            }
            else if (mode == Mode.Rollback)
            {
                if (runIndex == 0)
                {
                    inputBuffer = new InputRingBuffer();
                    feeder = new InputFeeder(inputBuffer);
                    InputReader.ActiveSource =
                        new RollbackInputSource(inputBuffer);
                }
                else
                {
                    var manager = RollbackManager.Enable();
                    feeder = new DelayedFeeder(manager.Inputs);
                }
            }
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
            if (feeder != null)
                SimulationDriver.Register(feeder);
            if (RollbackManager.Active != null)
                RollbackManager.Active.SaveBaseline();
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
            if (mode == Mode.Rollback)
            {
                TickRollbackMode();
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
            if (feeder != null)
                SimulationDriver.Unregister(feeder);
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

        void TickRollbackMode()
        {
            uint tick = SimClock.CurrentTick;
            if (tick == 0 || tick > ticksPerRun)
                return;

            var hashes = runIndex == 0 ? runATicks : runBTicks;
            hashes[tick - 1] = ComputeHash();

            if (GameController.State == GameState.RoundFinished)
            {
                Debug.Log("[DeterminismHarness] INCONCLUSIVE: round"
                    + $" ended at tick {tick}");
                Quit(2);
                return;
            }
            if (tick < ticksPerRun || SimulationDriver.IsResimulating)
                return;

            recording = false;
            SimulationDriver.Unregister(this);
            if (feeder != null)
                SimulationDriver.Unregister(feeder);
            if (runIndex == 0)
            {
                runIndex = 1;
                BeginRun();
            }
            else
            {
                RollbackManager.Disable();
                CompareRollback();
            }
        }

        void CompareRollback()
        {
            int confirmed = ticksPerRun - (int)DelayedFeeder.Delay;
            for (int i = 0; i < confirmed; i++)
            {
                if (runATicks[i] != runBTicks[i])
                {
                    Debug.Log("[DeterminismHarness] FAIL: rollback hash"
                        + $" mismatch at tick {i + 1}:"
                        + $" truth={runATicks[i]:X8}"
                        + $" rollback={runBTicks[i]:X8}");
                    Quit(1);
                    return;
                }
            }
            Debug.Log("[DeterminismHarness] PASS: rollback run with"
                + $" {DelayedFeeder.Delay}-tick-late slot 1 inputs"
                + $" matches ground truth on {confirmed} ticks");
            Quit(0);
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
            return MatchHasher.Compute();
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
