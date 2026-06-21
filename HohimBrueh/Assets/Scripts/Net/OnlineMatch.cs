using System.Collections.Generic;
using FreeLives;
using FrogSmashers.Net.Rollback;
using FrogSmashers.Net.Sim;
using FrogSmashers.Net.Transport;
using FrogSmashers.Net.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Net
{
    /// <summary>
    /// Orchestrates online play. The lobby is the JoinScreen arena
    /// running the full rollback simulation (frogs brawl freely); the
    /// roster (names, ready flags, countdown) is a host-authoritative
    /// control plane outside the sim. Players join and leave the
    /// running sim deterministically: every membership change is
    /// applied at a host-chosen future tick on all peers, and a late
    /// joiner bootstraps from a full authoritative snapshot.
    /// </summary>
    public static class OnlineMatch
    {
        public enum Phase
        {
            Inactive,
            Lobby,
            Match,
        }

        /// <summary>One participant as tracked by the control plane.</summary>
        public class RosterEntry
        {
            public int Slot;
            public string Name;
            public bool Ready;
            public ulong ClientId;
            public uint ApplyTick;
        }

        const string lobbyScene = "JoinScreen";
        const uint applyMarginTicks = 60;
        const uint opRetentionTicks = 240;
        const float readyCountdownTime = 5f;
        const int maxPlayers = 4;

        static readonly string[] matchLevels =
        {
            "1BusStop",
            "2DownSmash",
            "3Moon",
            "4FinalFrogstination",
            "5Skyline",
            "6Finale",
        };

        /// <summary>Sentinel level meaning "match over, leave to menu".</summary>
        const int matchOverLevel = 255;

        /// <summary>Round wins per slot, persisted across level loads.</summary>
        static readonly int[] matchWinsBySlot = new int[maxPlayers];

        static readonly Color[] slotColors =
        {
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
        };

        static readonly List<RosterEntry> roster =
            new List<RosterEntry>();
        static readonly List<(int Slot, uint Tick)> pendingRemovals =
            new List<(int, uint)>();
        static readonly Dictionary<int, Player> parkedPlayers =
            new Dictionary<int, Player>();
        static readonly MatchSnapshot welcomeSnapshot =
            new MatchSnapshot();

        static bool listening;
        static bool welcomePending;
        static bool transitionPending;
        static uint welcomeHostTick;
        static int currentLevel;
        static float countdown = -1f;
        static float rosterRebroadcast;
        static MembershipApplier applier;

        /// <summary>Current online phase.</summary>
        public static Phase CurrentPhase { get; private set; }

        /// <summary>True while any online phase drives the sim.</summary>
        public static bool Active
        {
            get { return CurrentPhase != Phase.Inactive; }
        }

        /// <summary>True while brawling in the online lobby.</summary>
        public static bool InLobby
        {
            get { return CurrentPhase == Phase.Lobby; }
        }

        /// <summary>This peer's player slot (0 = host).</summary>
        public static int LocalSlot { get; private set; }

        /// <summary>True on the authoritative client-host.</summary>
        public static bool IsHost { get; private set; }

        /// <summary>Shared match seed.</summary>
        public static ulong Seed { get; private set; }

        /// <summary>Roster snapshot for UI display.</summary>
        public static IReadOnlyList<RosterEntry> Roster
        {
            get { return roster; }
        }

        /// <summary>Countdown seconds left, or negative when idle.</summary>
        public static float Countdown
        {
            get { return countdown; }
        }

        /// <summary>Players known to the control plane.</summary>
        public static int PlayerCount
        {
            get { return roster.Count; }
        }

        /// <summary>
        /// True while a slot still belongs to the active roster. A slot
        /// drops out the instant its client disconnects, so the rollback
        /// pace gate can stop waiting on inputs that will never arrive.
        /// </summary>
        public static bool IsSlotActive(int slot)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].Slot == slot)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Newest tick whose inputs are confirmed for every roster
        /// slot with no unconfirmed gap behind it (and below any
        /// pending misprediction). Gap-free matters: a dropped input
        /// packet must never let authoritative hashes or snapshots be
        /// built on predicted inputs.
        /// </summary>
        public static uint SafeTick()
        {
            var inputs = RollbackManager.Active.Inputs;
            uint safe = uint.MaxValue;
            for (int i = 0; i < roster.Count; i++)
            {
                uint confirmed =
                    inputs.ContiguousConfirmedTick(roster[i].Slot);
                if (roster[i].ApplyTick > 0)
                {
                    confirmed = System.Math.Max(
                        confirmed, roster[i].ApplyTick - 1);
                }
                safe = System.Math.Min(safe, confirmed);
            }
            if (inputs.FirstMispredictedTick
                != InputRingBuffer.NoMispredict)
            {
                safe = System.Math.Min(
                    safe, inputs.FirstMispredictedTick - 1);
            }
            return safe;
        }

        /// <summary>Subscribes to net events; call once connected.</summary>
        public static void Listen()
        {
            if (listening)
                return;
            listening = true;
            NetMessages.Register();
            NetMessages.MatchStartReceived += OnMatchStart;
            NetMessages.ReadyReceived += OnClientSceneReady;
            NetMessages.GoReceived += OnGo;
            NetMessages.LobbyHelloReceived += OnLobbyHello;
            NetMessages.AddPlayerReceived += OnAddPlayer;
            NetMessages.RemovePlayerReceived += OnRemovePlayer;
            NetMessages.RosterReceived += OnRoster;
            NetMessages.WelcomeReceived += OnWelcome;
            NetMessages.LobbyReadyToggleReceived += OnLobbyReadyToggle;
            NetworkManager.Singleton.OnClientDisconnectCallback +=
                OnClientDisconnected;
        }

        /// <summary>Host: opens the playable lobby (JoinScreen).</summary>
        public static void HostStartLobby()
        {
            Listen();
            IsHost = true;
            LocalSlot = 0;
            NetMessages.CurrentEpoch++;
            Seed = (ulong)System.DateTime.Now.Ticks;
            CurrentPhase = Phase.Lobby;
            countdown = -1f;
            roster.Clear();
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            roster.Add(new RosterEntry
            {
                Slot = 0,
                Name = System.Environment.UserName,
                ClientId = NetworkManager.Singleton.LocalClientId,
                ApplyTick = 0,
            });
            BuildActivePlayers();
            BeginScene(lobbyScene);
        }

        /// <summary>Client: announces itself once connected.</summary>
        public static void JoinAsClient()
        {
            Listen();
            IsHost = false;
            welcomePending = true;
            var manager = NetworkManager.Singleton;
            if (manager.IsConnectedClient)
            {
                SendHello();
            }
            else
            {
                manager.OnClientConnectedCallback += HelloWhenConnected;
            }
        }

        static void HelloWhenConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (clientId != manager.LocalClientId)
                return;
            manager.OnClientConnectedCallback -= HelloWhenConnected;
            SendHello();
        }

        static void SendHello()
        {
            Debug.Log("[OnlineMatch] Connected, sending hello");
            NetMessages.SendLobbyHello(System.Environment.UserName);
        }

        /// <summary>Direct-to-match start (harness path, no lobby).</summary>
        public static void HostStart(ulong seed)
        {
            Listen();
            IsHost = true;
            LocalSlot = 0;
            roster.Clear();
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            var manager = NetworkManager.Singleton;
            int slot = 0;
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                roster.Add(new RosterEntry
                {
                    Slot = slot,
                    Name = $"PLAYER{slot + 1}",
                    ClientId = clientId,
                    ApplyTick = 0,
                });
                slot++;
            }
            TransitionTo(0, seed);
        }

        /// <summary>Local player leaves; tears down and shows menu.</summary>
        public static void LeaveLocal()
        {
            Stop();
            if (NetSession.Current != null)
                NetSession.Current.Leave();
            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>Round over (online): the host drives the next level.</summary>
        public static void OnRoundFinished()
        {
            if (!IsHost || CurrentPhase != Phase.Match
                || transitionPending)
            {
                return;
            }
            transitionPending = true;
            CreditRoundWinner();
            int remaining = matchLevels.Length - (currentLevel + 1);
            if (remaining <= 0 || MatchClinched(remaining))
            {
                EndMatch();
                return;
            }
            TransitionTo(currentLevel + 1,
                (ulong)System.DateTime.Now.Ticks);
        }

        /// <summary>Credits the round winner's slot toward the match.</summary>
        static void CreditRoundWinner()
        {
            var winner = GameController.GetWinningPlayer();
            if (winner == null)
                return;
            int slot = winner.sortPriority;
            if (slot >= 0 && slot < matchWinsBySlot.Length)
                matchWinsBySlot[slot]++;
        }

        /// <summary>
        /// True when the leader's round wins can no longer be matched by
        /// anyone else, even if they took every remaining round.
        /// </summary>
        static bool MatchClinched(int remaining)
        {
            int leader = 0;
            int second = 0;
            for (int i = 0; i < matchWinsBySlot.Length; i++)
            {
                int w = matchWinsBySlot[i];
                if (w > leader)
                {
                    second = leader;
                    leader = w;
                }
                else if (w > second)
                {
                    second = w;
                }
            }
            return leader > second + remaining;
        }

        /// <summary>Tells every client to leave, then leaves locally.</summary>
        static void EndMatch()
        {
            var manager = NetworkManager.Singleton;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                if (entry.ClientId != manager.LocalClientId)
                {
                    NetMessages.SendMatchStart(entry.ClientId, Seed,
                        entry.Slot, roster.Count, matchOverLevel);
                }
            }
            LeaveLocal();
        }

        /// <summary>Per-frame lobby logic, pumped by the overlay.</summary>
        public static void LobbyFrameUpdate(float dt)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby)
                return;
            bool allReady = roster.Count >= 2;
            for (int i = 0; i < roster.Count; i++)
                allReady &= roster[i].Ready;
            if (allReady)
            {
                if (countdown < 0f)
                    countdown = readyCountdownTime;
                countdown -= dt;
                rosterRebroadcast -= dt;
                if (rosterRebroadcast <= 0f)
                {
                    rosterRebroadcast = 0.5f;
                    BroadcastRoster();
                }
                if (countdown <= 0f)
                {
                    countdown = -1f;
                    if (NetSession.Current != null)
                        NetSession.Current.Unpublish();
                    TransitionTo(0, (ulong)System.DateTime.Now.Ticks);
                }
            }
            else if (countdown >= 0f)
            {
                countdown = -1f;
                BroadcastRoster();
            }
        }

        /// <summary>Toggles the local player's ready flag.</summary>
        public static void ToggleLocalReady()
        {
            if (CurrentPhase != Phase.Lobby)
                return;
            if (IsHost)
            {
                var entry = FindBySlot(LocalSlot);
                if (entry != null)
                {
                    entry.Ready = !entry.Ready;
                    BroadcastRoster();
                }
            }
            else
            {
                NetMessages.SendLobbyReadyToggle();
            }
        }

        /// <summary>Tears the online session state down.</summary>
        public static void Stop()
        {
            if (!Active && !listening)
                return;
            CurrentPhase = Phase.Inactive;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (listening)
            {
                listening = false;
                NetMessages.MatchStartReceived -= OnMatchStart;
                NetMessages.ReadyReceived -= OnClientSceneReady;
                NetMessages.GoReceived -= OnGo;
                NetMessages.LobbyHelloReceived -= OnLobbyHello;
                NetMessages.AddPlayerReceived -= OnAddPlayer;
                NetMessages.RemovePlayerReceived -= OnRemovePlayer;
                NetMessages.RosterReceived -= OnRoster;
                NetMessages.WelcomeReceived -= OnWelcome;
                NetMessages.LobbyReadyToggleReceived -=
                    OnLobbyReadyToggle;
                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.OnClientDisconnectCallback
                        -= OnClientDisconnected;
                }
            }
            if (applier != null)
            {
                SimulationDriver.Unregister(applier);
                applier = null;
            }
            AuthoritySync.Stop();
            RollbackNetDriver.Stop();
            NetMessages.Unregister();
            OnlineLobbyOverlay.Destroy();
            roster.Clear();
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            welcomePending = false;
            countdown = -1f;
            SimulationDriver.Paused = false;
            InputReader.ActiveSource = new LocalInputSource();
        }

        static readonly HashSet<ulong> sceneReadyClients =
            new HashSet<ulong>();

        static bool localSceneReady;
        static bool goSent;

        static void TransitionTo(int level, ulong seed)
        {
            if (level == 0)
                System.Array.Clear(matchWinsBySlot, 0,
                    matchWinsBySlot.Length);
            currentLevel = level;
            Seed = seed;
            NetMessages.CurrentEpoch++;
            var manager = NetworkManager.Singleton;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                entry.ApplyTick = 0;
                if (entry.ClientId != manager.LocalClientId)
                {
                    NetMessages.SendMatchStart(entry.ClientId, seed,
                        entry.Slot, roster.Count, level);
                }
            }
            SetupMatchPhase(level);
        }

        static void OnMatchStart(
            ulong seed, int slot, int playerCount, int level)
        {
            if (level == matchOverLevel)
            {
                LeaveLocal();
                return;
            }
            Seed = seed;
            LocalSlot = slot;
            currentLevel = level;
            welcomePending = false;
            if (roster.Count != playerCount)
            {
                roster.Clear();
                for (int s = 0; s < playerCount; s++)
                {
                    roster.Add(new RosterEntry
                    {
                        Slot = s,
                        Name = $"PLAYER{s + 1}",
                        ApplyTick = 0,
                    });
                }
            }
            for (int i = 0; i < roster.Count; i++)
                roster[i].ApplyTick = 0;
            SetupMatchPhase(level);
        }

        static void SetupMatchPhase(int level)
        {
            CurrentPhase = Phase.Match;
            countdown = -1f;
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            TearDownSimLayer();
            BuildActivePlayers();
            sceneReadyClients.Clear();
            goSent = false;
            localSceneReady = false;
            BeginScene(matchLevels[level]);
        }

        static void TearDownSimLayer()
        {
            if (applier != null)
            {
                SimulationDriver.Unregister(applier);
                applier = null;
            }
            AuthoritySync.Stop();
            RollbackNetDriver.Stop();
            OnlineLobbyOverlay.Destroy();
        }

        static void BuildActivePlayers()
        {
            GameController.activePlayers.Clear();
            GameController.isTeamMode = false;
            GameController.playersCanDropIn = false;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                if (entry.ApplyTick > 0)
                    continue;
                GameController.activePlayers.Add(MakePlayer(entry.Slot));
            }
        }

        static Player MakePlayer(int slot)
        {
            if (parkedPlayers.TryGetValue(slot, out var parked))
            {
                parkedPlayers.Remove(slot);
                return parked;
            }
            return new Player(InputReader.Device.Gamepad1 + slot,
                slotColors[slot], slot);
        }

        static void BeginScene(string sceneName)
        {
            SimulationDriver.Paused = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(sceneName);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            transitionPending = false;
            if (!Active)
                return;
            SimClock.ResetForNewMatch();
            DeterministicRng.Match.Reseed(Seed);
            RollbackNetDriver.Begin(LocalSlot, IsHost);
            AuthoritySync.Begin(RollbackManager.Active, IsHost);
            applier = new MembershipApplier();
            SimulationDriver.Register(applier);

            Debug.Log($"[OnlineMatch] Scene '{scene.name}' ready:"
                + $" phase={CurrentPhase} slot={LocalSlot}"
                + $" host={IsHost} players="
                + GameController.activePlayers.Count);
            if (CurrentPhase == Phase.Lobby)
            {
                OnlineLobbyOverlay.Create();
                if (IsHost)
                {
                    SimulationDriver.Paused = false;
                }
                else
                {
                    FinishClientLobbyJoin();
                }
            }
            else
            {
                localSceneReady = true;
                if (IsHost)
                    TryGo();
                else
                    NetMessages.SendReady();
            }
        }

        static void FinishClientLobbyJoin()
        {
            if (!GameController.RestoreFrom(welcomeSnapshot))
            {
                Debug.LogError("[OnlineMatch] Welcome snapshot restore"
                    + " failed, leaving");
                LeaveLocal();
                return;
            }
            RollbackManager.Active.SaveBaseline();
            uint target = welcomeHostTick;
            int steps = 0;
            SimulationDriver.IsResimulating = true;
            while (SimClock.CurrentTick < target && steps < 1200)
            {
                SimulationDriver.StepNow();
                steps++;
            }
            SimulationDriver.IsResimulating = false;
            SimulationDriver.Paused = false;
            Debug.Log("[OnlineMatch] Joined lobby at tick"
                + $" {SimClock.CurrentTick} (caught up {steps})");
        }

        static void OnLobbyHello(ulong clientId, string name)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby
                || roster.Count >= maxPlayers)
            {
                return;
            }
            int slot = FreeSlot();
            uint applyTick = SimClock.CurrentTick + applyMarginTicks;
            var entry = new RosterEntry
            {
                Slot = slot,
                Name = SanitizeName(name),
                ClientId = clientId,
                ApplyTick = applyTick,
            };
            roster.Add(entry);
            var manager = NetworkManager.Singleton;
            foreach (var other in manager.ConnectedClientsIds)
            {
                if (other == manager.LocalClientId || other == clientId)
                    continue;
                NetMessages.SendAddPlayer(
                    other, slot, entry.Name, applyTick);
            }
            SendWelcome(clientId, entry);
            BroadcastRoster();
            if (NetSession.Current != null)
                NetSession.Current.UpdatePlayerCount(roster.Count);
            Debug.Log($"[OnlineMatch] '{entry.Name}' joins as slot"
                + $" {slot}, applies at tick {applyTick}");
        }

        static void SendWelcome(ulong clientId, RosterEntry newcomer)
        {
            uint safe = System.Math.Min(
                SafeTick(), SimClock.CurrentTick);
            var snap = RollbackManager.Active.Snapshots.TryGet(safe);
            if (snap == null)
            {
                Debug.LogError("[OnlineMatch] No snapshot at tick"
                    + $" {safe} for welcome");
                return;
            }
            var writer = new FastBufferWriter(
                SnapshotWire.MaxBytes + 512, Allocator.Temp);
            using (writer)
            {
                writer.WriteValueSafe(NetMessages.CurrentEpoch);
                writer.WriteValueSafe((byte)newcomer.Slot);
                writer.WriteValueSafe(Seed);
                writer.WriteValueSafe(SimClock.CurrentTick);
                writer.WriteValueSafe((byte)roster.Count);
                for (int i = 0; i < roster.Count; i++)
                {
                    writer.WriteValueSafe((byte)roster[i].Slot);
                    writer.WriteValueSafe(roster[i].Name);
                    writer.WriteValueSafe(roster[i].Ready);
                    writer.WriteValueSafe(roster[i].ApplyTick);
                }
                SnapshotWire.Write(ref writer, snap);
                NetMessages.SendLobbyWelcome(clientId, writer);
            }
        }

        static void OnWelcome(FastBufferReader reader)
        {
            if (!welcomePending)
                return;
            welcomePending = false;
            reader.ReadValueSafe(out byte epoch);
            NetMessages.CurrentEpoch = epoch;
            reader.ReadValueSafe(out byte mySlot);
            reader.ReadValueSafe(out ulong seed);
            reader.ReadValueSafe(out uint hostTick);
            reader.ReadValueSafe(out byte count);
            roster.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out byte slot);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out bool ready);
                reader.ReadValueSafe(out uint applyTick);
                roster.Add(new RosterEntry
                {
                    Slot = slot,
                    Name = name,
                    Ready = ready,
                    ApplyTick = applyTick,
                });
            }
            SnapshotWire.Read(ref reader, welcomeSnapshot);
            LocalSlot = mySlot;
            Seed = seed;
            welcomeHostTick = hostTick;
            CurrentPhase = Phase.Lobby;
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            BuildActivePlayersForWelcome();
            BeginScene(lobbyScene);
        }

        static void BuildActivePlayersForWelcome()
        {
            GameController.activePlayers.Clear();
            GameController.isTeamMode = false;
            GameController.playersCanDropIn = false;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                if (entry.ApplyTick != 0
                    && entry.ApplyTick > welcomeSnapshot.Tick)
                {
                    continue;
                }
                GameController.activePlayers.Add(MakePlayer(entry.Slot));
            }
        }

        static void OnAddPlayer(int slot, string name, uint applyTick)
        {
            if (IsHost || FindBySlot(slot) != null)
                return;
            roster.Add(new RosterEntry
            {
                Slot = slot,
                Name = name,
                ApplyTick = applyTick,
            });
        }

        static void OnRemovePlayer(int slot, uint applyTick)
        {
            if (IsHost)
                return;
            pendingRemovals.Add((slot, applyTick));
        }

        static void OnLobbyReadyToggle(ulong clientId)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby)
                return;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].ClientId == clientId)
                {
                    roster[i].Ready = !roster[i].Ready;
                    BroadcastRoster();
                    return;
                }
            }
        }

        static void BroadcastRoster()
        {
            var manager = NetworkManager.Singleton;
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                if (clientId == manager.LocalClientId)
                    continue;
                var writer = new FastBufferWriter(512, Allocator.Temp);
                using (writer)
                {
                    writer.WriteValueSafe((byte)roster.Count);
                    for (int i = 0; i < roster.Count; i++)
                    {
                        writer.WriteValueSafe((byte)roster[i].Slot);
                        writer.WriteValueSafe(roster[i].Name);
                        writer.WriteValueSafe(roster[i].Ready);
                    }
                    sbyte cd = countdown >= 0f
                        ? (sbyte)Mathf.CeilToInt(countdown) : (sbyte)-1;
                    writer.WriteValueSafe(cd);
                    NetMessages.SendRoster(clientId, writer);
                }
            }
        }

        static void OnRoster(FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out byte slot);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out bool ready);
                var entry = FindBySlot(slot);
                if (entry != null)
                {
                    entry.Name = name;
                    entry.Ready = ready;
                }
            }
            reader.ReadValueSafe(out sbyte cd);
            countdown = cd;
        }

        static void OnClientDisconnected(ulong clientId)
        {
            if (!Active)
                return;
            var manager = NetworkManager.Singleton;
            if (IsHost)
            {
                var entry = FindByClient(clientId);
                if (entry == null)
                    return;
                uint applyTick = SimClock.CurrentTick + applyMarginTicks;
                pendingRemovals.Add((entry.Slot, applyTick));
                foreach (var other in manager.ConnectedClientsIds)
                {
                    if (other != manager.LocalClientId)
                    {
                        NetMessages.SendRemovePlayer(
                            other, entry.Slot, applyTick);
                    }
                }
                roster.Remove(entry);
                BroadcastRoster();
                if (NetSession.Current != null)
                    NetSession.Current.UpdatePlayerCount(roster.Count);
                Debug.Log($"[OnlineMatch] '{entry.Name}' left, slot"
                    + $" {entry.Slot} frees at tick {applyTick}");
            }
            else if (clientId == manager.LocalClientId)
            {
                Debug.Log("[OnlineMatch] Disconnected from host");
                LeaveLocal();
            }
        }

        static void OnClientSceneReady(ulong clientId)
        {
            if (!IsHost)
                return;
            sceneReadyClients.Add(clientId);
            TryGo();
        }

        static void TryGo()
        {
            if (!IsHost || goSent || !localSceneReady)
                return;
            if (sceneReadyClients.Count < roster.Count - 1)
                return;
            goSent = true;
            foreach (var clientId in sceneReadyClients)
                NetMessages.SendGo(clientId);
            SimulationDriver.Paused = false;
            Debug.Log("[OnlineMatch] Go sent, sim running");
        }

        static void OnGo()
        {
            SimulationDriver.Paused = false;
            Debug.Log("[OnlineMatch] Go received, sim running");
        }

        static RosterEntry FindBySlot(int slot)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].Slot == slot)
                    return roster[i];
            }
            return null;
        }

        static RosterEntry FindByClient(ulong clientId)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].ClientId == clientId)
                    return roster[i];
            }
            return null;
        }

        static int FreeSlot()
        {
            for (int slot = 0; slot < maxPlayers; slot++)
            {
                if (FindBySlot(slot) == null)
                    return slot;
            }
            return maxPlayers - 1;
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "FROG";
            name = name.Trim();
            return name.Length > 12
                ? name.Substring(0, 12).ToUpperInvariant()
                : name.ToUpperInvariant();
        }

        /// <summary>
        /// Applies deterministic membership changes inside the sim:
        /// joins and leaves take effect at their host-chosen tick on
        /// every peer (including during resimulation, where a restore
        /// may have stripped a later-joined player).
        /// </summary>
        class MembershipApplier : ISimTickable
        {
            public int SimOrder
            {
                get { return -200; }
            }

            public void SimTick(float dt)
            {
                uint tick = SimClock.CurrentTick;
                for (int i = 0; i < roster.Count; i++)
                {
                    var entry = roster[i];
                    if (entry.ApplyTick != 0 && entry.ApplyTick == tick)
                        EnsurePlayerAdded(entry.Slot);
                }
                foreach (var stripped in GameController.StrippedPlayers)
                    parkedPlayers[stripped.sortPriority] = stripped;
                GameController.StrippedPlayers.Clear();
                for (int i = pendingRemovals.Count - 1; i >= 0; i--)
                {
                    if (pendingRemovals[i].Tick == tick)
                        EnsurePlayerRemoved(pendingRemovals[i].Slot);
                    else if (tick > pendingRemovals[i].Tick
                        + opRetentionTicks)
                    {
                        pendingRemovals.RemoveAt(i);
                    }
                }
            }

            static void EnsurePlayerAdded(int slot)
            {
                var players = GameController.activePlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].sortPriority == slot)
                        return;
                }
                players.Add(MakePlayer(slot));
            }

            static void EnsurePlayerRemoved(int slot)
            {
                var players = GameController.activePlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].sortPriority != slot)
                        continue;
                    var player = players[i];
                    if (player.character != null)
                    {
                        player.character.gameObject.SetActive(false);
                        player.pooledCharacter = player.character;
                        player.character = null;
                    }
                    players.RemoveAt(i);
                    parkedPlayers[slot] = player;
                    return;
                }
            }
        }
    }
}
