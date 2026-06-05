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

        /// <summary>Redundant input frames per packet.</summary>
        public const int InputWindow = 8;

        /// <summary>Raised for every input frame received.</summary>
        public static event Action<int, uint, ushort> InputReceived;

        static bool registered;

        /// <summary>Registers handlers; call once connected.</summary>
        public static void Register()
        {
            if (registered)
                return;
            var messaging =
                NetworkManager.Singleton.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(inputMsg, OnInputMsg);
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
                manager.CustomMessagingManager
                    .UnregisterNamedMessageHandler(inputMsg);
            }
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
    }
}
