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
        /// <summary>
        /// Max ticks the local sim may run ahead of the slowest remote
        /// slot's contiguous confirmed inputs; beyond it the sim stalls
        /// until inputs arrive, so a remote hitch becomes a brief
        /// freeze instead of an unrepairable misprediction.
        /// </summary>
        const uint maxPredictionTicks = 20;

        /// <summary>Driver of the current online match.</summary>
        public static RollbackNetDriver Active { get; private set; }

        readonly RollbackManager rollback;
        readonly LocalInputSource poller = new LocalInputSource();
        readonly InputState keyboard = new InputState();
        readonly InputState gamepad = new InputState();
        readonly InputBatch batch = new InputBatch();
        readonly System.Collections.Generic.Dictionary<ulong, uint[]>
            peerAcks =
                new System.Collections.Generic.Dictionary<ulong, uint[]>();
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
            NetMessages.InputAckReceived += Active.OnInputAckReceived;
            SimulationDriver.TickCompleted += Active.OnTickCompleted;
            SimulationDriver.MayStep = Active.WithinPredictionWindow;
            SimulationDriver.Register(Active);
            Active.rollback.SaveBaseline();
            RollbackMetrics.Reset();
            return Active;
        }

        /// <summary>Tears down the online input exchange.</summary>
        public static void Stop()
        {
            if (Active == null)
                return;
            Debug.Log(RollbackMetrics.Summary());
            NetMessages.InputReceived -= Active.OnInputReceived;
            NetMessages.InputAckReceived -= Active.OnInputAckReceived;
            SimulationDriver.TickCompleted -= Active.OnTickCompleted;
            SimulationDriver.MayStep = null;
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
                var manager = NetworkManager.Singleton;
                foreach (var clientId in manager.ConnectedClientsIds)
                {
                    if (clientId != manager.LocalClientId)
                        SendBatchTo(clientId, true);
                }
            }
            else
            {
                SendBatchTo(NetworkManager.ServerClientId, false);
                SyncPaceToHost(tick);
            }
        }

        /// <summary>
        /// SimulationDriver gate: false while any active remote slot's
        /// confirmed inputs lag the present by more than the
        /// prediction window (the freeze is recorded in the metrics).
        /// </summary>
        bool WithinPredictionWindow()
        {
            uint present = SimClock.CurrentTick;
            for (int slot = 0;
                slot < InputRingBuffer.MaxSlots; slot++)
            {
                if (slot == localSlot)
                    continue;
                uint contiguous =
                    rollback.Inputs.ContiguousConfirmedTick(slot);
                if (contiguous == 0)
                    continue;
                if (present > contiguous + maxPredictionTicks)
                {
                    RollbackMetrics.RecordStallFrame();
                    return false;
                }
            }
            return true;
        }

        void SyncPaceToHost(uint tick)
        {
            uint hostConfirmed = rollback.Inputs.LastConfirmedTick(0);
            if (hostConfirmed == 0)
                return;
            long lead = (long)hostConfirmed - tick;
            bool biased = false;
            if (lead > 3)
            {
                SimulationDriver.PaceBias = (int)System.Math.Min(
                    lead - 2, 8);
                biased = true;
            }
            else if (lead < -10)
            {
                SimulationDriver.PaceBias = -1;
                biased = true;
            }
            RollbackMetrics.RecordPace(lead, biased);
        }

        /// <summary>
        /// Sends one packet to a peer with, per relayed slot, the
        /// contiguous confirmed window starting right after that
        /// peer's last ack (capped to the max window): lost packets
        /// are retransmitted until acknowledged, so confirmed input
        /// streams never end up with permanent holes.
        /// </summary>
        void SendBatchTo(ulong peerId, bool allSlots)
        {
            var inputs = rollback.Inputs;
            uint[] acks = AcksFor(peerId);
            batch.SlotCount = 0;
            for (int slot = 0;
                slot < InputRingBuffer.MaxSlots; slot++)
            {
                if (!allSlots && slot != localSlot)
                    continue;
                uint lastTick = inputs.LastConfirmedTick(slot);
                if (lastTick == 0 || lastTick <= acks[slot])
                    continue;
                FillSlotWindow(slot, lastTick, acks[slot]);
            }
            for (int s = 0; s < NetMessages.AckSlots; s++)
                batch.Acks[s] = inputs.ContiguousConfirmedTick(s);
            NetMessages.SendInputBatch(peerId, batch);
        }

        void FillSlotWindow(int slot, uint lastTick, uint ack)
        {
            uint first = ack + 1;
            uint span = lastTick - first + 1;
            if (span > NetMessages.MaxInputWindow)
                first = lastTick - NetMessages.MaxInputWindow + 1;
            int count = 0;
            var window = batch.Inputs[batch.SlotCount];
            for (uint tick = first; tick <= lastTick; tick++)
            {
                if (rollback.Inputs.TryGetConfirmed(
                    slot, tick, out ushort input))
                {
                    window[count++] = input;
                }
                else
                {
                    count = 0;
                }
            }
            if (count == 0)
                return;
            batch.Slots[batch.SlotCount] = (byte)slot;
            batch.LastTicks[batch.SlotCount] = lastTick;
            batch.Counts[batch.SlotCount] = (byte)count;
            batch.SlotCount++;
        }

        uint[] AcksFor(ulong peerId)
        {
            if (!peerAcks.TryGetValue(peerId, out uint[] acks))
            {
                acks = new uint[NetMessages.AckSlots];
                peerAcks[peerId] = acks;
            }
            return acks;
        }

        void OnInputAckReceived(ulong senderId, int slot, uint ack)
        {
            uint[] acks = AcksFor(senderId);
            if (ack > acks[slot])
                acks[slot] = ack;
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
