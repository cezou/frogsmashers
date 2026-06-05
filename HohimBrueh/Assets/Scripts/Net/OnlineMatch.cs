using System.Collections.Generic;
using FreeLives;
using FrogSmashers.Net.Rollback;
using FrogSmashers.Net.Sim;
using FrogSmashers.Net.Transport;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Net
{
    /// <summary>
    /// Orchestrates an online match over an established NetSession:
    /// the host assigns slots and broadcasts MatchStart (seed, slot,
    /// player count); every peer builds the same player list, loads
    /// the level paused, resets clock and RNG from the shared seed and
    /// starts the rollback net driver. Clients report Ready once
    /// loaded; the host sends Go and everyone starts ticking.
    /// </summary>
    public static class OnlineMatch
    {
        const string levelName = "1BusStop";

        static readonly Color[] slotColors =
        {
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
        };

        static readonly HashSet<ulong> readyClients =
            new HashSet<ulong>();

        static bool listening;
        static bool sceneReady;
        static bool goSent;

        /// <summary>True while an online match drives the sim.</summary>
        public static bool Active { get; private set; }

        /// <summary>This peer's player slot (0 = host).</summary>
        public static int LocalSlot { get; private set; }

        /// <summary>True on the authoritative client-host.</summary>
        public static bool IsHost { get; private set; }

        /// <summary>Shared match seed.</summary>
        public static ulong Seed { get; private set; }

        /// <summary>Players in the match (slots 0..count-1).</summary>
        public static int PlayerCount { get; private set; }

        /// <summary>Subscribes to match messages; call once connected.</summary>
        public static void Listen()
        {
            if (listening)
                return;
            listening = true;
            NetMessages.Register();
            NetMessages.MatchStartReceived += OnMatchStart;
            NetMessages.ReadyReceived += OnClientReady;
            NetMessages.GoReceived += OnGo;
        }

        /// <summary>Host: assigns slots, notifies clients, starts.</summary>
        public static void HostStart(ulong seed)
        {
            var manager = NetworkManager.Singleton;
            var clients = manager.ConnectedClientsIds;
            int playerCount = clients.Count;
            int slot = 1;
            foreach (var clientId in clients)
            {
                if (clientId == manager.LocalClientId)
                    continue;
                NetMessages.SendMatchStart(
                    clientId, seed, slot, playerCount);
                slot++;
            }
            Setup(seed, 0, playerCount, true);
        }

        /// <summary>Tears the online match down (back to local play).</summary>
        public static void Stop()
        {
            if (!Active)
                return;
            Active = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RollbackNetDriver.Stop();
            InputReader.ActiveSource = new LocalInputSource();
        }

        static void OnMatchStart(ulong seed, int slot, int playerCount)
        {
            Setup(seed, slot, playerCount, false);
        }

        static void Setup(
            ulong seed, int slot, int playerCount, bool isHost)
        {
            Active = true;
            Seed = seed;
            LocalSlot = slot;
            PlayerCount = playerCount;
            IsHost = isHost;
            sceneReady = false;
            goSent = false;
            readyClients.Clear();
            SetupPlayers(playerCount);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SimulationDriver.Paused = true;
            Debug.Log($"[OnlineMatch] Starting: slot={slot}"
                + $" players={playerCount} host={isHost} seed={seed:X}");
            SceneManager.LoadScene(levelName);
        }

        static void SetupPlayers(int playerCount)
        {
            GameController.activePlayers.Clear();
            for (int i = 0; i < playerCount; i++)
            {
                GameController.activePlayers.Add(new Player(
                    InputReader.Device.Gamepad1 + i, slotColors[i], i));
            }
            GameController.isTeamMode = false;
            GameController.playersCanDropIn = false;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Active || scene.name != levelName)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SimClock.ResetForNewMatch();
            DeterministicRng.Match.Reseed(Seed);
            RollbackNetDriver.Begin(LocalSlot, IsHost);
            sceneReady = true;
            if (IsHost)
                TryGo();
            else
                NetMessages.SendReady();
        }

        static void OnClientReady(ulong clientId)
        {
            if (!IsHost)
                return;
            readyClients.Add(clientId);
            TryGo();
        }

        static void TryGo()
        {
            if (!IsHost || goSent || !sceneReady)
                return;
            if (readyClients.Count < PlayerCount - 1)
                return;
            goSent = true;
            foreach (var clientId in readyClients)
                NetMessages.SendGo(clientId);
            SimulationDriver.Paused = false;
            Debug.Log("[OnlineMatch] Go sent, sim running");
        }

        static void OnGo()
        {
            SimulationDriver.Paused = false;
            Debug.Log("[OnlineMatch] Go received, sim running");
        }
    }
}
