using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Named-message layer over NGO for the rollback netcode. Gameplay
    /// state is never replicated by NGO; peers only exchange compact
    /// input windows (and, later, authoritative snapshots): each
    /// InputMsg carries one slot's inputs for a redundant range of
    /// ticks so packet loss is tolerated.
    /// </summary>
    public static class NetMessages
    {
        const string inputMsg = "FSInput";
        const string matchStartMsg = "FSMatchStart";
        const string readyMsg = "FSReady";
        const string goMsg = "FSGo";
        const string hashMsg = "FSHash";
        const string snapRequestMsg = "FSSnapReq";
        const string snapshotMsg = "FSSnap";
        const string lobbyHelloMsg = "FSLobbyHello";
        const string addPlayerMsg = "FSAddPlayer";
        const string removePlayerMsg = "FSRemovePlayer";
        const string rosterMsg = "FSRoster";
        const string welcomeMsg = "FSWelcome";
        const string lobbyReadyMsg = "FSLobbyReady";

        /// <summary>Redundant input frames per packet.</summary>
        public const int InputWindow = 8;

        /// <summary>
        /// Session generation, bumped at every scene transition; sim
        /// traffic from an older generation (in-flight during the
        /// transition, with ticks from the previous clock) is dropped
        /// on receive instead of corrupting the fresh input buffers.
        /// </summary>
        public static byte CurrentEpoch { get; set; }

        /// <summary>Raised for every input frame received.</summary>
        public static event Action<int, uint, ushort> InputReceived;

        /// <summary>Raised on clients: (seed, slot, playerCount, level).</summary>
        public static event Action<ulong, int, int, int> MatchStartReceived;

        /// <summary>Raised on the host when a client is scene-ready.</summary>
        public static event Action<ulong> ReadyReceived;

        /// <summary>Raised on clients when the host starts the sim.</summary>
        public static event Action GoReceived;

        /// <summary>Raised on clients: authoritative (tick, hash).</summary>
        public static event Action<uint, uint> HostHashReceived;

        /// <summary>Raised on the host: a client needs a snapshot.</summary>
        public static event Action<ulong> SnapshotRequested;

        /// <summary>Raised on clients with an authoritative snapshot.</summary>
        public static event Action<FastBufferReader> SnapshotReceived;

        /// <summary>Raised on the host: (clientId, playerName).</summary>
        public static event Action<ulong, string> LobbyHelloReceived;

        /// <summary>Raised on all: (slot, name, applyAtTick).</summary>
        public static event Action<int, string, uint> AddPlayerReceived;

        /// <summary>Raised on all: (slot, applyAtTick).</summary>
        public static event Action<int, uint> RemovePlayerReceived;

        /// <summary>Raised on clients with lobby roster/ready state.</summary>
        public static event Action<FastBufferReader> RosterReceived;

        /// <summary>Raised on a joining client with the lobby state.</summary>
        public static event Action<FastBufferReader> WelcomeReceived;

        /// <summary>Raised on the host: a client toggled ready.</summary>
        public static event Action<ulong> LobbyReadyToggleReceived;

        static bool registered;

        /// <summary>Registers handlers; call once connected.</summary>
        public static void Register()
        {
            if (registered)
                return;
            var messaging =
                NetworkManager.Singleton.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(inputMsg, OnInputMsg);
            messaging.RegisterNamedMessageHandler(
                matchStartMsg, OnMatchStartMsg);
            messaging.RegisterNamedMessageHandler(readyMsg, OnReadyMsg);
            messaging.RegisterNamedMessageHandler(goMsg, OnGoMsg);
            messaging.RegisterNamedMessageHandler(hashMsg, OnHashMsg);
            messaging.RegisterNamedMessageHandler(
                snapRequestMsg, OnSnapRequestMsg);
            messaging.RegisterNamedMessageHandler(
                snapshotMsg, OnSnapshotMsg);
            messaging.RegisterNamedMessageHandler(
                lobbyHelloMsg, OnLobbyHelloMsg);
            messaging.RegisterNamedMessageHandler(
                addPlayerMsg, OnAddPlayerMsg);
            messaging.RegisterNamedMessageHandler(
                removePlayerMsg, OnRemovePlayerMsg);
            messaging.RegisterNamedMessageHandler(rosterMsg, OnRosterMsg);
            messaging.RegisterNamedMessageHandler(
                welcomeMsg, OnWelcomeMsg);
            messaging.RegisterNamedMessageHandler(
                lobbyReadyMsg, OnLobbyReadyMsg);
            registered = true;
        }

        /// <summary>Unregisters handlers (session teardown).</summary>
        public static void Unregister()
        {
            registered = false;
            var manager = NetworkManager.Singleton;
            if (manager != null
                && manager.CustomMessagingManager != null)
            {
                var messaging = manager.CustomMessagingManager;
                messaging.UnregisterNamedMessageHandler(inputMsg);
                messaging.UnregisterNamedMessageHandler(matchStartMsg);
                messaging.UnregisterNamedMessageHandler(readyMsg);
                messaging.UnregisterNamedMessageHandler(goMsg);
                messaging.UnregisterNamedMessageHandler(hashMsg);
                messaging.UnregisterNamedMessageHandler(snapRequestMsg);
                messaging.UnregisterNamedMessageHandler(snapshotMsg);
                messaging.UnregisterNamedMessageHandler(lobbyHelloMsg);
                messaging.UnregisterNamedMessageHandler(addPlayerMsg);
                messaging.UnregisterNamedMessageHandler(removePlayerMsg);
                messaging.UnregisterNamedMessageHandler(rosterMsg);
                messaging.UnregisterNamedMessageHandler(welcomeMsg);
                messaging.UnregisterNamedMessageHandler(lobbyReadyMsg);
            }
        }

        /// <summary>Client → host: toggle my ready flag (reliable).</summary>
        public static void SendLobbyReadyToggle()
        {
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(lobbyReadyMsg,
                    NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Client → host: my display name (reliable).</summary>
        public static void SendLobbyHello(string name)
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteValueSafe(name);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(lobbyHelloMsg,
                    NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Host → all: deterministic player add (reliable).</summary>
        public static void SendAddPlayer(
            ulong targetClientId, int slot, string name, uint applyTick)
        {
            using var writer = new FastBufferWriter(160, Allocator.Temp);
            writer.WriteValueSafe((byte)slot);
            writer.WriteValueSafe(name);
            writer.WriteValueSafe(applyTick);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(addPlayerMsg, targetClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Host → all: deterministic player removal.</summary>
        public static void SendRemovePlayer(
            ulong targetClientId, int slot, uint applyTick)
        {
            using var writer = new FastBufferWriter(8, Allocator.Temp);
            writer.WriteValueSafe((byte)slot);
            writer.WriteValueSafe(applyTick);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(removePlayerMsg, targetClientId,
                    writer, NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Host → client: lobby roster payload (reliable).</summary>
        public static void SendRoster(
            ulong targetClientId, FastBufferWriter writer)
        {
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(rosterMsg, targetClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Host → joining client: full lobby state.</summary>
        public static void SendLobbyWelcome(
            ulong targetClientId, FastBufferWriter writer)
        {
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(welcomeMsg, targetClientId, writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
        }

        /// <summary>Client → host: please send a snapshot (reliable).</summary>
        public static void SendSnapshotRequest()
        {
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(snapRequestMsg,
                    NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>
        /// Host → client: authoritative snapshot payload. The writer
        /// must begin with <see cref="CurrentEpoch"/>.
        /// </summary>
        public static void SendSnapshot(
            ulong targetClientId, FastBufferWriter writer)
        {
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(snapshotMsg, targetClientId, writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
        }

        /// <summary>Host → client: match parameters (reliable).</summary>
        public static void SendMatchStart(
            ulong targetClientId, ulong seed, int slot, int playerCount,
            int level)
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(CurrentEpoch);
            writer.WriteValueSafe(seed);
            writer.WriteValueSafe((byte)slot);
            writer.WriteValueSafe((byte)playerCount);
            writer.WriteValueSafe((byte)level);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(matchStartMsg, targetClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Client → host: scene loaded, sim ready (reliable).</summary>
        public static void SendReady()
        {
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(readyMsg,
                    NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Host → client: start ticking now (reliable).</summary>
        public static void SendGo(ulong targetClientId)
        {
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteValueSafe((byte)1);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(goMsg, targetClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Host → client: authoritative state hash (reliable).</summary>
        public static void SendHostHash(
            ulong targetClientId, uint tick, uint hash)
        {
            using var writer = new FastBufferWriter(9, Allocator.Temp);
            writer.WriteValueSafe(CurrentEpoch);
            writer.WriteValueSafe(tick);
            writer.WriteValueSafe(hash);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(hashMsg, targetClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        /// <summary>
        /// Sends one slot's inputs for ticks
        /// [lastTick - count + 1, lastTick] to a peer (unreliable;
        /// redundancy covers losses).
        /// </summary>
        public static void SendInputs(
            ulong targetClientId, int slot, uint lastTick,
            ushort[] inputs, int count)
        {
            int bytes = 9 + count * 2;
            using var writer = new FastBufferWriter(
                bytes, Allocator.Temp);
            writer.WriteValueSafe(CurrentEpoch);
            writer.WriteValueSafe((byte)slot);
            writer.WriteValueSafe(lastTick);
            writer.WriteValueSafe((byte)count);
            for (int i = 0; i < count; i++)
                writer.WriteValueSafe(inputs[i]);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(inputMsg, targetClientId, writer,
                    NetworkDelivery.Unreliable);
        }

        static void OnInputMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte epoch);
            if (epoch != CurrentEpoch)
                return;
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out uint lastTick);
            reader.ReadValueSafe(out byte count);
            uint firstTick = lastTick - (uint)(count - 1);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ushort input);
                InputReceived?.Invoke(slot, firstTick + (uint)i, input);
            }
        }

        static void OnMatchStartMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte epoch);
            CurrentEpoch = epoch;
            reader.ReadValueSafe(out ulong seed);
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out byte playerCount);
            reader.ReadValueSafe(out byte level);
            MatchStartReceived?.Invoke(seed, slot, playerCount, level);
        }

        static void OnReadyMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte _);
            ReadyReceived?.Invoke(senderClientId);
        }

        static void OnGoMsg(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte _);
            GoReceived?.Invoke();
        }

        static void OnHashMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte epoch);
            if (epoch != CurrentEpoch)
                return;
            reader.ReadValueSafe(out uint tick);
            reader.ReadValueSafe(out uint hash);
            HostHashReceived?.Invoke(tick, hash);
        }

        static void OnSnapRequestMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte _);
            SnapshotRequested?.Invoke(senderClientId);
        }

        static void OnSnapshotMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte epoch);
            if (epoch != CurrentEpoch)
                return;
            SnapshotReceived?.Invoke(reader);
        }

        static void OnLobbyHelloMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string name);
            LobbyHelloReceived?.Invoke(senderClientId, name);
        }

        static void OnAddPlayerMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out string name);
            reader.ReadValueSafe(out uint applyTick);
            AddPlayerReceived?.Invoke(slot, name, applyTick);
        }

        static void OnRemovePlayerMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out uint applyTick);
            RemovePlayerReceived?.Invoke(slot, applyTick);
        }

        static void OnRosterMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            RosterReceived?.Invoke(reader);
        }

        static void OnWelcomeMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            WelcomeReceived?.Invoke(reader);
        }

        static void OnLobbyReadyMsg(
            ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte _);
            LobbyReadyToggleReceived?.Invoke(senderClientId);
        }
    }
}
