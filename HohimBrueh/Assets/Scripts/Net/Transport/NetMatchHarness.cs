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
        const int lobbyMatchTicks = 900;
        const float lobbyBrawlSeconds = 8f;
        const ulong matchSeed = 0xF706C0DEUL;
        const float watchdogSeconds = 300f;
        const uint corruptionTick = 700;
        const int maxP95RollbackTicks = 15;
        const int maxRollbackDepthTicks = 30;
        const float maxCorrectionsPerMinute = 2f;

        static bool isHost;
        static bool injectDesync;
        static bool lobbyMode;
        static bool teamModeArg;
        static int netPlayers = 2;
        static NetMatchHarness instance;

        int desyncsBeforeCorrection;
        bool corrupted;
        bool finished;
        bool readySent;
        float lobbyReadyAt = -1f;

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
            bool lobbyHost = HasCliArg("-netLobbyHost");
            bool lobbyJoin = HasCliArg("-netLobbyJoin");
            if (!host && !join && !lobbyHost && !lobbyJoin)
                return;
            isHost = host || lobbyHost;
            lobbyMode = lobbyHost || lobbyJoin;
            injectDesync = HasCliArg("-injectDesync");
            teamModeArg = HasCliArg("-teamMode");
            netPlayers = GetCliArgInt("-netPlayers", 2);
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
            var net = await NetSession.CreateAsync(4, false);
            OnlineMatch.Listen();
            File.WriteAllText(CodeFile, net.JoinCode);
            if (lobbyMode)
            {
                SimulationDriver.Register(this);
                OnlineMatch.HostStartLobby();
                if (teamModeArg)
                    OnlineMatch.SetTeamMode(true);
                return;
            }
            var manager = NetworkManager.Singleton;
            while (manager.ConnectedClientsIds.Count < netPlayers)
                await Task.Delay(250);
            Debug.Log($"[NetMatch] {netPlayers - 1} client(s)"
                + " connected, starting match");
            SimulationDriver.Register(this);
            if (teamModeArg)
                OnlineMatch.SetTeamMode(true);
            OnlineMatch.HostStart(matchSeed);
        }

        async Task RunJoin()
        {
            while (!File.Exists(CodeFile))
                await Task.Delay(250);
            string code = File.ReadAllText(CodeFile).Trim();
            await NetSession.JoinByCodeAsync(code);
            OnlineMatch.Listen();
            if (lobbyMode)
                OnlineMatch.JoinAsClient();
            SimulationDriver.Register(this);
            Debug.Log("[NetMatch] joined, waiting for match start");
        }

        void Update()
        {
            if (!lobbyMode || finished || readySent)
                return;
            if (!OnlineMatch.InLobby
                || OnlineMatch.PlayerCount < netPlayers)
            {
                return;
            }
            if (lobbyReadyAt < 0f)
            {
                lobbyReadyAt = Time.time + lobbyBrawlSeconds;
                Debug.Log("[NetMatch] lobby brawl started, accept in"
                    + $" {lobbyBrawlSeconds}s");
            }
            else if (Time.time >= lobbyReadyAt)
            {
                readySent = true;
                Debug.Log("[NetMatch] accepting lobby choice");
                OnlineMatch.LobbyAccept();
            }
        }

        public void SimTick(float dt)
        {
            if (!OnlineMatch.Active || finished
                || SimulationDriver.IsResimulating)
            {
                return;
            }
            if (lobbyMode
                && OnlineMatch.CurrentPhase != OnlineMatch.Phase.Match)
            {
                return;
            }
            uint tick = SimClock.CurrentTick;

            if (injectDesync && !isHost && !corrupted
                && tick == corruptionTick)
            {
                CorruptState();
            }

            if (tick >= (lobbyMode ? lobbyMatchTicks : matchTicks))
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
                Debug.Log(RollbackMetrics.Summary());
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
            Debug.Log(RollbackMetrics.Summary());
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
            else if (sync.ComparedCount < (lobbyMode ? 10 : 30))
            {
                Debug.Log("[NetMatch] INCONCLUSIVE: only"
                    + $" {sync.ComparedCount} hashes compared");
                Application.Quit(2);
            }
            else if (NetSimulator.Enabled && !MetricsHealthy(sync))
            {
                Application.Quit(1);
            }
            else
            {
                Debug.Log("[NetMatch] PASS:"
                    + $" {sync.ComparedCount} authoritative hashes"
                    + " matched, zero desyncs");
                Application.Quit(0);
            }
        }

        /// <summary>
        /// Latency-gate assertions: under simulated network
        /// conditions the rollback must stay shallow and corrections
        /// rare, otherwise the tuning is wrong even with no desync.
        /// </summary>
        bool MetricsHealthy(AuthoritySync sync)
        {
            int p95 = RollbackMetrics.DepthPercentile(0.95);
            int max = RollbackMetrics.MaxDepth;
            int ticks = lobbyMode ? lobbyMatchTicks : matchTicks;
            float minutes = ticks / SimClock.TickRate / 60f;
            float perMinute = sync.CorrectionCount / minutes;
            if (p95 >= maxP95RollbackTicks)
            {
                Debug.Log("[NetMatch] FAIL: p95 rollback depth"
                    + $" {p95} >= {maxP95RollbackTicks} ticks");
                return false;
            }
            if (max >= maxRollbackDepthTicks)
            {
                Debug.Log("[NetMatch] FAIL: max rollback depth"
                    + $" {max} >= {maxRollbackDepthTicks} ticks");
                return false;
            }
            if (perMinute > maxCorrectionsPerMinute)
            {
                Debug.Log("[NetMatch] FAIL:"
                    + $" {perMinute:0.0} corrections/min >"
                    + $" {maxCorrectionsPerMinute}");
                return false;
            }
            return true;
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

        static int GetCliArgInt(string name, int fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name
                    && int.TryParse(args[i + 1], out int value))
                {
                    return value;
                }
            }
            return fallback;
        }
    }
}
