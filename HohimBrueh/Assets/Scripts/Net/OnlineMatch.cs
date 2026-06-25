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
            Score,
        }

        /// <summary>One participant as tracked by the control plane.</summary>
        public class RosterEntry
        {
            public int Slot;
            public string Name;
            public bool Ready;
            public ulong ClientId;
            public uint ApplyTick;
            public Color Color = Color.white;
            public Team Team;
        }

        const string lobbyScene = "JoinScreen";
        const string scoreScene = "ScoreScreen";
        const uint applyMarginTicks = 60;
        const uint opRetentionTicks = 240;
        const float readyCountdownTime = 5f;
        const float scoreHoldTime = 5f;
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

        /// <summary>Sentinel level meaning "match over, back to lobby".</summary>
        const int lobbyReturnLevel = 254;

        /// <summary>Round wins per slot, persisted across level loads.</summary>
        static readonly int[] matchWinsBySlot = new int[maxPlayers];

        static readonly Color[] slotColors =
        {
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
        };

        /// <summary>FFA color choices cycled in the lobby.</summary>
        static readonly Color[] palette =
        {
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
            new Color(1f, 0.4f, 0f),
            new Color(0.7f, 0.2f, 1f),
            Color.cyan,
            Color.white,
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
        static LobbyPinApplier pinApplier;

        static float scoreHold = -1f;
        static int scoreWinnerSlot = -1;
        static int scoreOverallSlot = -1;
        static bool scoreMatchOver;
        static int pendingNextLevel;
        static bool lobbyReturnActive;

        const float forceLaunchTime = 10f;
        static bool teamMode;
        static bool localAccepted;
        static int localColorIndex;
        static float localShade;
        static Team localTeam;
        static float forceLaunch = -1f;

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

        /// <summary>
        /// True while the local player is still choosing color/team (frog
        /// frozen): the input chokepoint forces neutral sim input for this
        /// slot until ACCEPT.
        /// </summary>
        public static bool LocalChoosing
        {
            get { return InLobby && !localAccepted; }
        }

        /// <summary>True when the host has enabled team mode for the match.</summary>
        public static bool TeamModeEnabled
        {
            get { return teamMode; }
        }

        /// <summary>The local player's current choosing color.</summary>
        public static Color LocalColor
        {
            get
            {
                return teamMode
                    ? TeamColor(LocalTeam, localShade)
                    : palette[localColorIndex % palette.Length];
            }
        }

        /// <summary>The local player's current team.</summary>
        public static Team LocalTeam
        {
            get { return localTeam; }
        }

        /// <summary>True once the local player has accepted (is ready).</summary>
        public static bool LocalAccepted
        {
            get { return localAccepted; }
        }

        /// <summary>Round winner's slot, for the score interlude UI.</summary>
        public static int ScoreWinnerSlot
        {
            get { return scoreWinnerSlot; }
        }

        /// <summary>True when the score interlude ends the match.</summary>
        public static bool ScoreMatchOver
        {
            get { return scoreMatchOver; }
        }

        /// <summary>Overall match winner's slot (match-over only).</summary>
        public static int ScoreOverallWinnerSlot
        {
            get { return scoreOverallSlot; }
        }

        /// <summary>Display name for a slot, or a default.</summary>
        public static string SlotName(int slot)
        {
            var entry = FindBySlot(slot);
            return entry != null ? entry.Name : "PLAYER " + (slot + 1);
        }

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
            NetMessages.LobbyChoiceReceived += OnLobbyChoice;
            NetMessages.ScoreReceived += OnScore;
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
            forceLaunch = -1f;
            teamMode = false;
            roster.Clear();
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            var hostEntry = new RosterEntry
            {
                Slot = 0,
                Name = System.Environment.UserName,
                ClientId = NetworkManager.Singleton.LocalClientId,
                ApplyTick = 0,
            };
            DefaultChoice(hostEntry);
            roster.Add(hostEntry);
            ResetLocalChoice();
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
                var entry = new RosterEntry
                {
                    Slot = slot,
                    Name = $"PLAYER{slot + 1}",
                    ClientId = clientId,
                    ApplyTick = 0,
                };
                DefaultChoice(entry);
                roster.Add(entry);
                slot++;
            }
            localAccepted = true;
            TransitionTo(0, seed);
        }

        /// <summary>Test hook: forces team mode for harness matches.</summary>
        public static void SetTeamMode(bool on)
        {
            teamMode = on;
        }

        /// <summary>Local player leaves; tears down and shows menu.</summary>
        public static void LeaveLocal()
        {
            Stop();
            if (NetSession.Current != null)
                NetSession.Current.Leave();
            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>
        /// Round over (online): the host shows the score interlude, then
        /// drives the next level (or ends the match).
        /// </summary>
        public static void OnRoundFinished()
        {
            if (!IsHost || CurrentPhase != Phase.Match
                || transitionPending)
            {
                return;
            }
            transitionPending = true;
            int winnerSlot = -1;
            var winner = GameController.GetWinningPlayer();
            if (winner != null)
                winnerSlot = winner.sortPriority;
            CreditRoundWinner();
            int remaining = matchLevels.Length - (currentLevel + 1);
            bool over = remaining <= 0 || MatchClinched(remaining);
            int overallSlot = over ? LeadingSlot() : -1;
            Debug.Log("[OnlineMatch] Round finished: winnerSlot="
                + $"{winnerSlot} level={currentLevel} over={over}"
                + $" next={currentLevel + 1}");
            EnterScore(winnerSlot, over, overallSlot, currentLevel + 1);
        }

        /// <summary>Slot with the most round wins (ties: lowest slot).</summary>
        static int LeadingSlot()
        {
            int best = 0;
            int bestWins = -1;
            for (int i = 0; i < matchWinsBySlot.Length; i++)
            {
                if (matchWinsBySlot[i] > bestWins)
                {
                    bestWins = matchWinsBySlot[i];
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Host: bumps the epoch, tells every client to show the score
        /// interlude, and loads it locally. The host holds it for
        /// <see cref="scoreHoldTime"/> (see <see cref="ScoreFrameUpdate"/>)
        /// then drives the next level or ends the match.
        /// </summary>
        static void EnterScore(
            int winnerSlot, bool matchOver, int overallSlot, int nextLevel)
        {
            scoreWinnerSlot = winnerSlot;
            scoreMatchOver = matchOver;
            scoreOverallSlot = overallSlot;
            pendingNextLevel = nextLevel;
            scoreHold = scoreHoldTime;
            CurrentPhase = Phase.Score;
            NetMessages.CurrentEpoch++;
            GameController.levelNo = currentLevel + 1;
            SyncRoundWinsToPlayers();
            var manager = NetworkManager.Singleton;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                if (entry.ClientId != manager.LocalClientId)
                {
                    NetMessages.SendScore(entry.ClientId, winnerSlot,
                        matchOver, overallSlot, matchWinsBySlot);
                }
            }
            TearDownSimLayer();
            BeginScene(scoreScene);
        }

        /// <summary>Client: shows the score interlude sent by the host.</summary>
        static void OnScore(
            int winnerSlot, bool matchOver, int overallSlot, int[] wins)
        {
            if (IsHost)
                return;
            scoreWinnerSlot = winnerSlot;
            scoreMatchOver = matchOver;
            scoreOverallSlot = overallSlot;
            CurrentPhase = Phase.Score;
            for (int i = 0; i < matchWinsBySlot.Length; i++)
                matchWinsBySlot[i] = i < wins.Length ? wins[i] : 0;
            GameController.levelNo = currentLevel + 1;
            SyncRoundWinsToPlayers();
            TearDownSimLayer();
            BeginScene(scoreScene);
        }

        /// <summary>Pumped by the score screen; host drives the exit.</summary>
        public static void ScoreFrameUpdate(float dt)
        {
            if (!IsHost || CurrentPhase != Phase.Score || scoreHold < 0f)
                return;
            scoreHold -= dt;
            if (scoreHold > 0f)
                return;
            scoreHold = -1f;
            Debug.Log("[OnlineMatch] Score timer elapsed: matchOver="
                + $"{scoreMatchOver} next={pendingNextLevel}");
            if (scoreMatchOver)
                ReturnToLobby();
            else
                TransitionTo(pendingNextLevel,
                    (ulong)System.DateTime.Now.Ticks);
        }

        /// <summary>Reflects cumulative slot wins onto the player data.</summary>
        static void SyncRoundWinsToPlayers()
        {
            var players = GameController.activePlayers;
            for (int i = 0; i < players.Count; i++)
            {
                int slot = players[i].sortPriority;
                if (slot >= 0 && slot < matchWinsBySlot.Length)
                    players[i].roundWins = matchWinsBySlot[slot];
            }
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

        /// <summary>
        /// Match over: brings every peer back to the playable lobby for
        /// another match, keeping the roster, relay allocation and session
        /// alive. Per-match state is reset and the lobby re-published so it
        /// is discoverable again.
        /// </summary>
        static void ReturnToLobby()
        {
            System.Array.Clear(matchWinsBySlot, 0, matchWinsBySlot.Length);
            countdown = -1f;
            forceLaunch = -1f;
            scoreHold = -1f;
            scoreMatchOver = false;
            for (int i = 0; i < roster.Count; i++)
            {
                roster[i].Ready = false;
                roster[i].ApplyTick = 0;
            }
            CurrentPhase = Phase.Lobby;
            lobbyReturnActive = true;
            ResetLocalChoice();
            Seed = (ulong)System.DateTime.Now.Ticks;
            NetMessages.CurrentEpoch++;
            var manager = NetworkManager.Singleton;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                if (entry.ClientId != manager.LocalClientId)
                {
                    NetMessages.SendMatchStart(entry.ClientId, Seed,
                        entry.Slot, roster.Count, lobbyReturnLevel,
                        teamMode);
                }
            }
            if (NetSession.Current != null)
            {
                NetSession.Current.Republish();
                NetSession.Current.UpdatePlayerCount(roster.Count);
            }
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            TearDownSimLayer();
            BuildActivePlayers();
            BeginScene(lobbyScene);
            PushLocalChoice();
        }

        /// <summary>
        /// Per-frame lobby logic, pumped by the overlay (host only). The
        /// match launches when everyone has accepted (>=2, after a short
        /// countdown) or, in a full lobby, after a force-launch timer.
        /// </summary>
        public static void LobbyFrameUpdate(float dt)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby)
                return;
            bool allReady = roster.Count >= 2;
            for (int i = 0; i < roster.Count; i++)
                allReady &= roster[i].Ready;
            bool launch = false;
            if (allReady)
            {
                if (countdown < 0f)
                    countdown = readyCountdownTime;
                countdown -= dt;
                if (countdown <= 0f)
                    launch = true;
            }
            else if (countdown >= 0f)
            {
                countdown = -1f;
            }
            if (roster.Count >= maxPlayers)
            {
                if (forceLaunch < 0f)
                    forceLaunch = forceLaunchTime;
                forceLaunch -= dt;
                if (forceLaunch <= 0f)
                    launch = true;
            }
            else
            {
                forceLaunch = -1f;
            }
            rosterRebroadcast -= dt;
            if (rosterRebroadcast <= 0f)
            {
                rosterRebroadcast = 0.5f;
                BroadcastRoster();
            }
            if (launch)
            {
                countdown = -1f;
                forceLaunch = -1f;
                if (NetSession.Current != null)
                    NetSession.Current.Unpublish();
                TransitionTo(0, (ulong)System.DateTime.Now.Ticks);
            }
        }

        /// <summary>The active launch timer (countdown or force), or -1.</summary>
        static float LaunchTimer()
        {
            if (countdown >= 0f)
                return countdown;
            return forceLaunch;
        }

        /// <summary>B: cycle color (FFA) or switch team (team mode).</summary>
        public static void LobbyCycleChoice()
        {
            if (!InLobby)
                return;
            if (teamMode)
            {
                localTeam = localTeam == Team.Red ? Team.Blue : Team.Red;
                localShade = 0.3f;
            }
            else
            {
                localColorIndex = (localColorIndex + 1) % palette.Length;
            }
            PushLocalChoice();
            PlaySpawnFx();
        }

        /// <summary>Left/right shade adjust within a team color.</summary>
        public static void LobbyAdjustShade(float dir, float dt)
        {
            if (!InLobby || !teamMode)
                return;
            localShade = Mathf.Clamp(
                localShade + dir * dt * 0.5f, 0f, 0.7f);
            PushLocalChoice();
        }

        /// <summary>X: accept the current choice (= ready to launch).</summary>
        public static void LobbyAccept()
        {
            if (!InLobby || localAccepted)
                return;
            localAccepted = true;
            PushLocalChoice();
        }

        /// <summary>
        /// Y: go back to choosing (un-ready). The frog snaps back to its
        /// spawn point (the choosing flag re-pins it) with a spawn effect.
        /// </summary>
        public static void LobbyBack()
        {
            if (!InLobby || !localAccepted)
                return;
            localAccepted = false;
            PushLocalChoice();
            PlaySpawnFx();
        }

        /// <summary>Local spawn puff + sound at the player's platform.</summary>
        static void PlaySpawnFx()
        {
            Vector3 point = global::Terrain.GetSpawnPoint(LocalSlot);
            EffectsController.CreateSpawnEffects(
                point + Vector3.up, LocalColor);
            SoundController.PlaySoundEffect(
                "CharacterSpawn", 0.3f, point);
        }

        /// <summary>
        /// SELECT (host only): toggle team mode / FFA. Resets any running
        /// launch timer so changing mode mid-countdown restarts it.
        /// </summary>
        public static void HostToggleTeamMode()
        {
            if (!IsHost || CurrentPhase != Phase.Lobby)
                return;
            teamMode = !teamMode;
            countdown = -1f;
            forceLaunch = -1f;
            BroadcastRoster();
            OnTeamModeChangedLocal();
        }

        static void ResetLocalChoice()
        {
            localAccepted = false;
            localColorIndex = LocalSlot % palette.Length;
            localShade = 0.3f;
            localTeam = (LocalSlot & 1) == 0 ? Team.Blue : Team.Red;
        }

        static void OnTeamModeChangedLocal()
        {
            ResetLocalChoice();
            PushLocalChoice();
        }

        static void PushLocalChoice()
        {
            var color = LocalColor;
            ApplyColorToFrog(LocalSlot, color);
            if (IsHost)
            {
                var entry = FindBySlot(LocalSlot);
                if (entry != null)
                {
                    entry.Color = color;
                    entry.Team = localTeam;
                    entry.Ready = localAccepted;
                }
                BroadcastRoster();
            }
            else
            {
                NetMessages.SendLobbyChoice(
                    color, localTeam, localAccepted);
            }
        }

        /// <summary>
        /// Re-tints a spawned lobby frog so color choices show live (the
        /// animator reads Player.color each frame). Cosmetic — color is not
        /// in the sim hash.
        /// </summary>
        static void ApplyColorToFrog(int slot, Color color)
        {
            var players = GameController.activePlayers;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].sortPriority == slot)
                {
                    players[i].color = color;
                    return;
                }
            }
        }

        static Color TeamColor(Team team, float shade)
        {
            return Color.Lerp(
                team == Team.Red ? Color.red : Color.blue,
                Color.white, shade);
        }

        static void DefaultChoice(RosterEntry entry)
        {
            entry.Color = palette[entry.Slot % palette.Length];
            entry.Team = (entry.Slot & 1) == 0 ? Team.Blue : Team.Red;
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
                NetMessages.LobbyChoiceReceived -= OnLobbyChoice;
                NetMessages.ScoreReceived -= OnScore;
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
            if (pinApplier != null)
            {
                SimulationDriver.Unregister(pinApplier);
                pinApplier = null;
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
            scoreHold = -1f;
            forceLaunch = -1f;
            scoreMatchOver = false;
            lobbyReturnActive = false;
            teamMode = false;
            localAccepted = false;
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
            int notified = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var entry = roster[i];
                entry.ApplyTick = 0;
                if (entry.ClientId != manager.LocalClientId)
                {
                    NetMessages.SendMatchStart(entry.ClientId, seed,
                        entry.Slot, roster.Count, level, teamMode);
                    notified++;
                }
            }
            Debug.Log("[OnlineMatch] TransitionTo level "
                + $"{level}, MatchStart sent to {notified} client(s),"
                + $" epoch={NetMessages.CurrentEpoch}");
            SetupMatchPhase(level);
        }

        static void OnMatchStart(
            ulong seed, int slot, int playerCount, int level,
            bool matchTeamMode)
        {
            Debug.Log("[OnlineMatch] MatchStart received: level "
                + $"{level} slot={slot} count={playerCount} phase="
                + $"{CurrentPhase}");
            Seed = seed;
            LocalSlot = slot;
            teamMode = matchTeamMode;
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
            if (level == lobbyReturnLevel)
            {
                for (int i = 0; i < roster.Count; i++)
                    roster[i].Ready = false;
                SetupLobbyReturn();
                return;
            }
            currentLevel = level;
            SetupMatchPhase(level);
        }

        /// <summary>Client: rebuilds the playable lobby after a match.</summary>
        static void SetupLobbyReturn()
        {
            CurrentPhase = Phase.Lobby;
            lobbyReturnActive = true;
            countdown = -1f;
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            ResetLocalChoice();
            TearDownSimLayer();
            BuildActivePlayers();
            BeginScene(lobbyScene);
            PushLocalChoice();
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
            if (pinApplier != null)
            {
                SimulationDriver.Unregister(pinApplier);
                pinApplier = null;
            }
            AuthoritySync.Stop();
            RollbackNetDriver.Stop();
            OnlineLobbyOverlay.Destroy();
        }

        static void BuildActivePlayers()
        {
            GameController.activePlayers.Clear();
            GameController.isTeamMode =
                CurrentPhase == Phase.Match && teamMode;
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
            var entry = FindBySlot(slot);
            Color color = entry != null
                ? entry.Color : slotColors[slot % slotColors.Length];
            var player = new Player(
                InputReader.Device.Gamepad1 + slot, color, slot);
            if (entry != null)
                player.team = entry.Team;
            return player;
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
            if (CurrentPhase == Phase.Score)
            {
                NetMessages.Register();
                Debug.Log("[OnlineMatch] Score interlude ready,"
                    + " messages re-registered");
                return;
            }
            SimClock.ResetForNewMatch();
            DeterministicRng.Match.Reseed(Seed);
            RollbackNetDriver.Begin(LocalSlot, IsHost);
            AuthoritySync.Begin(RollbackManager.Active, IsHost);
            applier = new MembershipApplier();
            SimulationDriver.Register(applier);
            pinApplier = new LobbyPinApplier();
            SimulationDriver.Register(pinApplier);

            Debug.Log($"[OnlineMatch] Scene '{scene.name}' ready:"
                + $" phase={CurrentPhase} slot={LocalSlot}"
                + $" host={IsHost} players="
                + GameController.activePlayers.Count);
            if (CurrentPhase == Phase.Lobby)
            {
                OnlineLobbyOverlay.Create();
                if (IsHost || lobbyReturnActive)
                {
                    lobbyReturnActive = false;
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
            DefaultChoice(entry);
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
                writer.WriteValueSafe(teamMode);
                writer.WriteValueSafe((byte)roster.Count);
                for (int i = 0; i < roster.Count; i++)
                {
                    writer.WriteValueSafe((byte)roster[i].Slot);
                    writer.WriteValueSafe(roster[i].Name);
                    writer.WriteValueSafe(roster[i].Ready);
                    writer.WriteValueSafe(roster[i].ApplyTick);
                    WriteColor(ref writer, roster[i].Color);
                    writer.WriteValueSafe((byte)roster[i].Team);
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
            reader.ReadValueSafe(out bool welcomeTeamMode);
            reader.ReadValueSafe(out byte count);
            roster.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out byte slot);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out bool ready);
                reader.ReadValueSafe(out uint applyTick);
                Color color = ReadColor(ref reader);
                reader.ReadValueSafe(out byte team);
                roster.Add(new RosterEntry
                {
                    Slot = slot,
                    Name = name,
                    Ready = ready,
                    ApplyTick = applyTick,
                    Color = color,
                    Team = (Team)team,
                });
            }
            SnapshotWire.Read(ref reader, welcomeSnapshot);
            teamMode = welcomeTeamMode;
            LocalSlot = mySlot;
            Seed = seed;
            welcomeHostTick = hostTick;
            CurrentPhase = Phase.Lobby;
            pendingRemovals.Clear();
            parkedPlayers.Clear();
            ResetLocalChoice();
            BuildActivePlayersForWelcome();
            BeginScene(lobbyScene);
            PushLocalChoice();
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
            var entry = new RosterEntry
            {
                Slot = slot,
                Name = name,
                ApplyTick = applyTick,
            };
            DefaultChoice(entry);
            roster.Add(entry);
        }

        static void OnRemovePlayer(int slot, uint applyTick)
        {
            if (IsHost)
                return;
            pendingRemovals.Add((slot, applyTick));
        }

        static void OnLobbyChoice(
            ulong clientId, Color color, Team team, bool ready)
        {
            if (!IsHost || CurrentPhase != Phase.Lobby)
                return;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].ClientId == clientId)
                {
                    roster[i].Color = color;
                    roster[i].Team = team;
                    roster[i].Ready = ready;
                    ApplyColorToFrog(roster[i].Slot, color);
                    BroadcastRoster();
                    return;
                }
            }
        }

        static void WriteColor(ref FastBufferWriter writer, Color c)
        {
            writer.WriteValueSafe((byte)Mathf.Clamp(
                Mathf.RoundToInt(c.r * 255f), 0, 255));
            writer.WriteValueSafe((byte)Mathf.Clamp(
                Mathf.RoundToInt(c.g * 255f), 0, 255));
            writer.WriteValueSafe((byte)Mathf.Clamp(
                Mathf.RoundToInt(c.b * 255f), 0, 255));
        }

        static Color ReadColor(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte r);
            reader.ReadValueSafe(out byte g);
            reader.ReadValueSafe(out byte b);
            return new Color(r / 255f, g / 255f, b / 255f);
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
                    writer.WriteValueSafe(teamMode);
                    writer.WriteValueSafe((byte)roster.Count);
                    for (int i = 0; i < roster.Count; i++)
                    {
                        writer.WriteValueSafe((byte)roster[i].Slot);
                        writer.WriteValueSafe(roster[i].Name);
                        writer.WriteValueSafe(roster[i].Ready);
                        WriteColor(ref writer, roster[i].Color);
                        writer.WriteValueSafe((byte)roster[i].Team);
                    }
                    float timer = LaunchTimer();
                    sbyte cd = timer >= 0f
                        ? (sbyte)Mathf.CeilToInt(timer) : (sbyte)-1;
                    writer.WriteValueSafe(cd);
                    NetMessages.SendRoster(clientId, writer);
                }
            }
        }

        static void OnRoster(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool newTeamMode);
            reader.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out byte slot);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out bool ready);
                Color color = ReadColor(ref reader);
                reader.ReadValueSafe(out byte team);
                var entry = FindBySlot(slot);
                if (entry != null)
                {
                    entry.Name = name;
                    entry.Ready = ready;
                    entry.Color = color;
                    entry.Team = (Team)team;
                    ApplyColorToFrog(slot, color);
                }
            }
            reader.ReadValueSafe(out sbyte cd);
            countdown = cd;
            if (newTeamMode != teamMode)
            {
                teamMode = newTeamMode;
                OnTeamModeChangedLocal();
            }
        }

        static void OnClientDisconnected(ulong clientId)
        {
            if (!Active)
                return;
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.ShutdownInProgress)
                return;
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

        /// <summary>
        /// Keeps every frog still choosing its color pinned to its spawn
        /// point (idle) so it stays at the middle of its platform until it
        /// confirms — and snaps back there on Back. The choosing flag is
        /// read from the input word, so all peers pin identically and the
        /// teleport is deterministic. Runs after the characters move.
        /// </summary>
        class LobbyPinApplier : ISimTickable
        {
            public int SimOrder
            {
                get { return 500; }
            }

            public void SimTick(float dt)
            {
                if (CurrentPhase != Phase.Lobby
                    || RollbackManager.Active == null)
                {
                    return;
                }
                uint tick = SimClock.CurrentTick;
                var inputs = RollbackManager.Active.Inputs;
                var players = GameController.activePlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player.character == null)
                        continue;
                    ushort packed = inputs.Get(player.sortPriority, tick);
                    if ((packed & InputPacking.ChoosingBit) != 0)
                    {
                        player.character.ResetForSpawn(
                            global::Terrain.GetSpawnPoint(
                                player.sortPriority));
                    }
                }
            }
        }
    }
}
