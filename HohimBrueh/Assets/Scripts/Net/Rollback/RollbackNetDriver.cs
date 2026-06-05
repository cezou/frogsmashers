using FreeLives;
using FrogSmashers.Net.Sim;
using FrogSmashers.Net.Transport;
using Unity.Netcode;
using UnityEngine;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Bridges the rollback loop and the network during an online
    /// match. Every live tick it polls the local devices, confirms the
    /// local slot in the input buffer and sends a redundant input
    /// window (client to host; host fans every slot out to every
    /// client). Received inputs are confirmed into the buffer, where a
    /// contradiction with an already-simulated prediction triggers the
    /// rollback in RollbackManager.
    /// </summary>
    public class RollbackNetDriver : ISimTickable
    {
        /// <summary>Driver of the current online match.</summary>
        public static RollbackNetDriver Active { get; private set; }

        readonly RollbackManager rollback;
        readonly LocalInputSource poller = new LocalInputSource();
        readonly InputState keyboard = new InputState();
        readonly InputState gamepad = new InputState();
        readonly ushort[] window = new ushort[NetMessages.InputWindow];
        readonly int localSlot;
        readonly bool isHost;

        RollbackNetDriver(int localSlot, bool isHost)
        {
            this.localSlot = localSlot;
            this.isHost = isHost;
            rollback = RollbackManager.Enable();
        }

        /// <summary>Local input feeds before everything else.</summary>
        public int SimOrder
        {
            get { return -100; }
        }

        /// <summary>Starts the online input exchange for this match.</summary>
        public static RollbackNetDriver Begin(int localSlot, bool isHost)
        {
            Stop();
            Active = new RollbackNetDriver(localSlot, isHost);
            NetMessages.Register();
            NetMessages.InputReceived += Active.OnInputReceived;
            SimulationDriver.TickCompleted += Active.OnTickCompleted;
            SimulationDriver.Register(Active);
            Active.rollback.SaveBaseline();
            return Active;
        }

        /// <summary>Tears down the online input exchange.</summary>
        public static void Stop()
        {
            if (Active == null)
                return;
            NetMessages.InputReceived -= Active.OnInputReceived;
            SimulationDriver.TickCompleted -= Active.OnTickCompleted;
            SimulationDriver.Unregister(Active);
            NetMessages.Unregister();
            RollbackManager.Disable();
            Active = null;
        }

        public void SimTick(float dt)
        {
            if (SimulationDriver.IsResimulating)
                return;
            poller.Read(InputReader.Device.Keyboard1, keyboard);
            poller.Read(InputReader.Device.Gamepad1, gamepad);
            ushort packed = (ushort)(InputPacking.Pack(keyboard)
                | InputPacking.Pack(gamepad));
            rollback.Inputs.Confirm(
                localSlot, SimClock.CurrentTick, packed);
        }

        void OnTickCompleted(uint tick)
        {
            if (SimulationDriver.IsResimulating)
                return;
            if (isHost)
                SendAllSlotsToClients();
            else
                SendSlotTo(NetworkManager.ServerClientId, localSlot);
        }

        void SendAllSlotsToClients()
        {
            var manager = NetworkManager.Singleton;
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                if (clientId == manager.LocalClientId)
                    continue;
                for (int slot = 0;
                    slot < InputRingBuffer.MaxSlots; slot++)
                {
                    if (rollback.Inputs.LastConfirmedTick(slot) > 0)
                        SendSlotTo(clientId, slot);
                }
            }
        }

        void SendSlotTo(ulong clientId, int slot)
        {
            uint lastTick = rollback.Inputs.LastConfirmedTick(slot);
            if (lastTick == 0)
                return;
            int count = 0;
            for (int i = NetMessages.InputWindow - 1; i >= 0; i--)
            {
                uint tick = lastTick - (uint)i;
                if (tick == 0 || tick > lastTick)
                    continue;
                if (rollback.Inputs.TryGetConfirmed(
                    slot, tick, out ushort input))
                {
                    window[count++] = input;
                }
                else if (count > 0)
                {
                    count = 0;
                }
            }
            if (count > 0)
                NetMessages.SendInputs(
                    clientId, slot, lastTick, window, count);
        }

        void OnInputReceived(int slot, uint tick, ushort input)
        {
            if (slot == localSlot)
                return;
            rollback.Inputs.Confirm(slot, tick, input);
        }
    }
}
