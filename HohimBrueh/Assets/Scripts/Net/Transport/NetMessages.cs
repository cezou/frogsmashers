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

        /// <summary>Redundant input frames per packet.</summary>
        public const int InputWindow = 8;

        /// <summary>Raised for every input frame received.</summary>
        public static event Action<int, uint, ushort> InputReceived;

        /// <summary>Raised on clients: (seed, localSlot, playerCount).</summary>
        public static event Action<ulong, int, int> MatchStartReceived;

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
            }
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

        /// <summary>Host → client: authoritative snapshot payload.</summary>
        public static void SendSnapshot(
            ulong targetClientId, FastBufferWriter writer)
        {
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(snapshotMsg, targetClientId, writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
        }

        /// <summary>Host → client: match parameters (reliable).</summary>
        public static void SendMatchStart(
            ulong targetClientId, ulong seed, int slot, int playerCount)
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(seed);
            writer.WriteValueSafe((byte)slot);
            writer.WriteValueSafe((byte)playerCount);
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
            using var writer = new FastBufferWriter(8, Allocator.Temp);
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
            int bytes = 8 + count * 2;
            using var writer = new FastBufferWriter(
                bytes, Allocator.Temp);
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
            reader.ReadValueSafe(out ulong seed);
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out byte playerCount);
            MatchStartReceived?.Invoke(seed, slot, playerCount);
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
            SnapshotReceived?.Invoke(reader);
        }
    }
}
