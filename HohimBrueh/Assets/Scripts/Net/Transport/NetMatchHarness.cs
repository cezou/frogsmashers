using System.Collections.Generic;
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
    /// prediction and rollback. The host broadcasts its state hash
    /// every 30 ticks; the client compares it against its own
    /// post-rollback hash once the tick is fully confirmed. Pass: the
    /// match reaches 1800 ticks with zero hash mismatches.
    /// </summary>
    public class NetMatchHarness : MonoBehaviour, ISimTickable
    {
        const int matchTicks = 1800;
        const uint hashInterval = 30;
        const ulong matchSeed = 0xF706C0DEUL;
        const float watchdogSeconds = 240f;
        const int hashRing = 4096;

        static bool isHost;
        static NetMatchHarness instance;

        readonly uint[] ownHashes = new uint[hashRing];
        readonly uint[] ownHashTicks = new uint[hashRing];
        readonly SortedDictionary<uint, uint> pendingHostHashes =
            new SortedDictionary<uint, uint>();
        uint lastBroadcastTick;
        int comparedCount;
        int mismatchCount;
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
            var go = new GameObject("NetMatchHarness");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetMatchHarness>();
        }

        /// <summary>Hashes after the whole tick has simulated.</summary>
        public int SimOrder
        {
            get { return 10000; }
        }

        async void Start()
        {
            Debug.Log($"[NetMatch] role={(isHost ? "host" : "join")}");
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
            NetMessages.HostHashReceived += OnHostHash;
            SimulationDriver.Register(this);
            Debug.Log("[NetMatch] joined, waiting for match start");
        }

        public void SimTick(float dt)
        {
            if (!OnlineMatch.Active || finished)
                return;
            uint tick = SimClock.CurrentTick;
            ownHashes[tick % hashRing] = MatchHasher.Compute();
            ownHashTicks[tick % hashRing] = tick;

            if (SimulationDriver.IsResimulating)
                return;

            if (isHost)
                BroadcastConfirmedHash();
            else
                CompareReadyHashes();

            if (tick >= matchTicks)
                Finish();
        }

        void BroadcastConfirmedHash()
        {
            uint safe = SafeTick();
            if (safe == uint.MaxValue || safe == 0)
                return;
            uint target = safe - (safe % hashInterval);
            if (target <= lastBroadcastTick
                || ownHashTicks[target % hashRing] != target)
            {
                return;
            }
            lastBroadcastTick = target;
            BroadcastHash(target);
        }

        uint SafeTick()
        {
            var inputs = RollbackManager.Active.Inputs;
            uint safe = uint.MaxValue;
            for (int s = 0; s < OnlineMatch.PlayerCount; s++)
                safe = System.Math.Min(safe, inputs.LastConfirmedTick(s));
            if (inputs.FirstMispredictedTick
                != InputRingBuffer.NoMispredict)
            {
                safe = System.Math.Min(
                    safe, inputs.FirstMispredictedTick - 1);
            }
            return safe;
        }

        void BroadcastHash(uint tick)
        {
            var manager = NetworkManager.Singleton;
            uint hash = ownHashes[tick % hashRing];
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                if (clientId != manager.LocalClientId)
                    NetMessages.SendHostHash(clientId, tick, hash);
            }
        }

        void OnHostHash(uint tick, uint hash)
        {
            pendingHostHashes[tick] = hash;
        }

        void CompareReadyHashes()
        {
            uint safe = SafeTick();
            var done = new List<uint>();
            foreach (var entry in pendingHostHashes)
            {
                if (entry.Key > safe)
                    break;
                done.Add(entry.Key);
                if (ownHashTicks[entry.Key % hashRing] != entry.Key)
                    continue;
                uint own = ownHashes[entry.Key % hashRing];
                comparedCount++;
                if (own != entry.Value)
                {
                    mismatchCount++;
                    Debug.Log("[NetMatch] DESYNC at tick"
                        + $" {entry.Key}: host={entry.Value:X8}"
                        + $" local={own:X8}");
                }
            }
            foreach (var tick in done)
                pendingHostHashes.Remove(tick);
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
            CompareReadyHashes();
            if (mismatchCount > 0)
            {
                Debug.Log("[NetMatch] FAIL:"
                    + $" {mismatchCount} desyncs over"
                    + $" {comparedCount} compared hashes");
                Application.Quit(1);
            }
            else if (comparedCount < 30)
            {
                Debug.Log("[NetMatch] INCONCLUSIVE: only"
                    + $" {comparedCount} hashes compared");
                Application.Quit(2);
            }
            else
            {
                Debug.Log("[NetMatch] PASS:"
                    + $" {comparedCount} authoritative hashes matched,"
                    + " zero desyncs");
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
