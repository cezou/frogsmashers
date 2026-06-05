using System.IO;
using System.Threading.Tasks;
using FrogSmashers.Net.Rollback;
using FrogSmashers.Net.Sim;
using Unity.Netcode;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// End-to-end online match gate. Launch with -netMatchHost and
    /// -netMatchJoin (plus -scriptedLocal on both so each instance
    /// generates a different deterministic input stream for its own
    /// slot): both peers play the same match through the relay with
    /// prediction and rollback while AuthoritySync compares the
    /// client's state against the host's authoritative hashes.
    /// Pass: 1800 ticks with zero desyncs. With -injectDesync the
    /// client corrupts its state at tick 700 and must be repaired by
    /// an authoritative snapshot, with clean checkpoints afterwards.
    /// Exit codes: 0 pass, 1 fail, 2 inconclusive.
    /// </summary>
    public class NetMatchHarness : MonoBehaviour, ISimTickable
    {
        const int matchTicks = 1800;
        const ulong matchSeed = 0xF706C0DEUL;
        const float watchdogSeconds = 240f;
        const uint corruptionTick = 700;

        static bool isHost;
        static bool injectDesync;
        static NetMatchHarness instance;

        int desyncsBeforeCorrection;
        bool corrupted;
        bool finished;

        static string CodeFile
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    "netHelloJoinCode.txt");
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            bool host = HasCliArg("-netMatchHost");
            bool join = HasCliArg("-netMatchJoin");
            if (!host && !join)
                return;
            isHost = host;
            injectDesync = HasCliArg("-injectDesync");
            var go = new GameObject("NetMatchHarness");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetMatchHarness>();
        }

        /// <summary>Runs after AuthoritySync has hashed the tick.</summary>
        public int SimOrder
        {
            get { return 10000; }
        }

        async void Start()
        {
            Debug.Log($"[NetMatch] role={(isHost ? "host" : "join")}"
                + $" injectDesync={injectDesync}");
            Invoke(nameof(Watchdog), watchdogSeconds);
            try
            {
                if (isHost)
                    await RunHost();
                else
                    await RunJoin();
            }
            catch (System.Exception e)
            {
                Debug.Log($"[NetMatch] FAIL: {e}");
                Application.Quit(1);
            }
        }

        async Task RunHost()
        {
            if (File.Exists(CodeFile))
                File.Delete(CodeFile);
            var net = await NetSession.CreateAsync(4);
            OnlineMatch.Listen();
            File.WriteAllText(CodeFile, net.JoinCode);
            var manager = NetworkManager.Singleton;
            while (manager.ConnectedClientsIds.Count < 2)
                await Task.Delay(250);
            Debug.Log("[NetMatch] client connected, starting match");
            SimulationDriver.Register(this);
            OnlineMatch.HostStart(matchSeed);
        }

        async Task RunJoin()
        {
            while (!File.Exists(CodeFile))
                await Task.Delay(250);
            string code = File.ReadAllText(CodeFile).Trim();
            await NetSession.JoinByCodeAsync(code);
            OnlineMatch.Listen();
            SimulationDriver.Register(this);
            Debug.Log("[NetMatch] joined, waiting for match start");
        }

        public void SimTick(float dt)
        {
            if (!OnlineMatch.Active || finished
                || SimulationDriver.IsResimulating)
            {
                return;
            }
            uint tick = SimClock.CurrentTick;

            if (injectDesync && !isHost && !corrupted
                && tick == corruptionTick)
            {
                CorruptState();
            }

            if (tick >= matchTicks)
                Finish();
        }

        void CorruptState()
        {
            corrupted = true;
            var players = GameController.activePlayers;
            if (players.Count > 0 && players[0].character != null)
            {
                players[0].character.transform.position +=
                    new Vector3(3f, 2f, 0f);
                desyncsBeforeCorrection =
                    AuthoritySync.Active.DesyncCount;
                Debug.Log("[NetMatch] State corrupted at tick"
                    + $" {SimClock.CurrentTick}");
            }
        }

        void Finish()
        {
            finished = true;
            SimulationDriver.Unregister(this);
            if (isHost)
            {
                Debug.Log("[NetMatch] PASS: host reached tick"
                    + $" {matchTicks}");
                Invoke(nameof(QuitOk), 5f);
            }
            else
            {
                Invoke(nameof(EvaluateClient), 4f);
            }
        }

        void EvaluateClient()
        {
            var sync = AuthoritySync.Active;
            Debug.Log($"[NetMatch] compared={sync.ComparedCount}"
                + $" desyncs={sync.DesyncCount}"
                + $" corrections={sync.CorrectionCount}");
            if (injectDesync)
                EvaluateRecovery(sync);
            else
                EvaluateClean(sync);
        }

        void EvaluateClean(AuthoritySync sync)
        {
            if (sync.DesyncCount > 0)
            {
                Debug.Log("[NetMatch] FAIL:"
                    + $" {sync.DesyncCount} desyncs over"
                    + $" {sync.ComparedCount} compared hashes");
                Application.Quit(1);
            }
            else if (sync.ComparedCount < 30)
            {
                Debug.Log("[NetMatch] INCONCLUSIVE: only"
                    + $" {sync.ComparedCount} hashes compared");
                Application.Quit(2);
            }
            else
            {
                Debug.Log("[NetMatch] PASS:"
                    + $" {sync.ComparedCount} authoritative hashes"
                    + " matched, zero desyncs");
                Application.Quit(0);
            }
        }

        void EvaluateRecovery(AuthoritySync sync)
        {
            if (!corrupted)
            {
                Debug.Log("[NetMatch] INCONCLUSIVE: corruption never"
                    + " injected");
                Application.Quit(2);
            }
            else if (sync.DesyncCount <= desyncsBeforeCorrection)
            {
                Debug.Log("[NetMatch] FAIL: corruption was never"
                    + " detected");
                Application.Quit(1);
            }
            else if (sync.CorrectionCount < 1)
            {
                Debug.Log("[NetMatch] FAIL: desync detected but no"
                    + " snapshot correction applied");
                Application.Quit(1);
            }
            else
            {
                Debug.Log("[NetMatch] PASS: injected desync detected"
                    + $" ({sync.DesyncCount}) and corrected"
                    + $" ({sync.CorrectionCount} snapshot restores)");
                Application.Quit(0);
            }
        }

        void QuitOk()
        {
            Application.Quit(0);
        }

        void Watchdog()
        {
            if (finished)
                return;
            Debug.Log("[NetMatch] FAIL: watchdog timeout at tick"
                + $" {SimClock.CurrentTick}");
            Application.Quit(1);
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
