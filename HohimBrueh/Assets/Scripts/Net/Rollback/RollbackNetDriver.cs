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

        static bool ScriptedLocal
        {
            get
            {
                return System.Array.IndexOf(
                    System.Environment.GetCommandLineArgs(),
                    "-scriptedLocal") >= 0;
            }
        }

        bool loggedFirstInput;

        public void SimTick(float dt)
        {
            if (SimulationDriver.IsResimulating)
                return;
            if (SimClock.CurrentTick == 1)
            {
                Debug.Log("[RollbackNetDriver] First tick: slot"
                    + $" {localSlot}, host={isHost}, source="
                    + InputReader.ActiveSource.GetType().Name
                    + $", players={GameController.activePlayers.Count}");
            }
            ushort packed;
            if (ScriptedLocal)
            {
                ScriptedInputSource.ReadForTick(SimClock.CurrentTick,
                    InputReader.Device.Gamepad1 + localSlot, keyboard);
                packed = InputPacking.Pack(keyboard);
            }
            else
            {
                packed = PollLocalDevices();
            }
            if (packed != 0 && !loggedFirstInput)
            {
                loggedFirstInput = true;
                Debug.Log("[RollbackNetDriver] First local input"
                    + $" {packed:X4} at tick {SimClock.CurrentTick}");
            }
            rollback.Inputs.Confirm(
                localSlot, SimClock.CurrentTick, packed);
        }

        /// <summary>
        /// Merged local input for online play: both keyboard layouts
        /// (arrows and WASD) and the last-used gamepad all drive the
        /// local frog. An unfocused instance reports neutral input so
        /// stale held keys never leak into the match.
        /// </summary>
        public ushort PollLocalDevices()
        {
            if (!Application.isFocused)
                return 0;
            poller.Read(InputReader.Device.Keyboard1, keyboard);
            ushort packed = InputPacking.Pack(keyboard);
            poller.Read(InputReader.Device.Keyboard2, keyboard);
            packed |= InputPacking.Pack(keyboard);
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
            {
                LocalInputSource.ReadGamepad(pad, gamepad);
                packed |= InputPacking.Pack(gamepad);
            }
            return packed;
        }

        void OnTickCompleted(uint tick)
        {
            if (SimulationDriver.IsResimulating)
                return;
            if (isHost)
            {
                SendAllSlotsToClients();
            }
            else
            {
                SendSlotTo(NetworkManager.ServerClientId, localSlot);
                SyncPaceToHost(tick);
            }
        }

        void SyncPaceToHost(uint tick)
        {
            uint hostConfirmed = rollback.Inputs.LastConfirmedTick(0);
            if (hostConfirmed == 0)
                return;
            long lead = (long)hostConfirmed - tick;
            if (lead > 3)
                SimulationDriver.PaceBias = (int)System.Math.Min(
                    lead - 2, 8);
            else if (lead < -10)
                SimulationDriver.PaceBias = -1;
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
            if (tick > SimClock.CurrentTick + 300)
                return;
            rollback.Inputs.Confirm(slot, tick, input);
        }
    }
}
