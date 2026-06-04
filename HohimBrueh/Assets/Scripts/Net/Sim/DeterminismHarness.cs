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
        const string levelName = "1BusStop";
        const ulong testSeed = 0xF706C0DEUL;
        const int ticksPerRun = 1800;
        const int minTicks = 300;

        /// <summary>True when the harness drives the current process.</summary>
        public static bool Active { get; private set; }

        static DeterminismHarness instance;

        readonly List<uint> runA = new List<uint>(ticksPerRun);
        readonly List<uint> runB = new List<uint>(ticksPerRun);
        int runIndex;
        bool recording;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (!HasCliArg("-determinismTest"))
                return;

            Active = true;
            Debug.Log("[DeterminismHarness] Active");
            Application.targetFrameRate = 300;
            QualitySettings.vSyncCount = 0;
            SimulationDriver.ForcedTicksPerFrame = 10;
            InputReader.ActiveSource = new ScriptedInputSource();

            var host = new GameObject("DeterminismHarness");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<DeterminismHarness>();
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
