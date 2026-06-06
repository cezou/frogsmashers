using UnityEngine;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Per-slot ring of packed inputs by tick. Confirmed inputs come
    /// from the local player or the network; missing remote inputs are
    /// predicted by repeating the last confirmed one. When a confirm
    /// contradicts a prediction that was already simulated, the tick is
    /// flagged so the rollback loop knows where to resimulate from.
    /// </summary>
    public class InputRingBuffer
    {
        public const int MaxSlots = 4;
        public const int Capacity = 256;

        /// <summary>Sentinel meaning no misprediction is pending.</summary>
        public const uint NoMispredict = uint.MaxValue;

        struct Cell
        {
            public uint Tick;
            public ushort Input;
            public bool Confirmed;
            public bool UsedPrediction;
        }

        readonly Cell[][] cells;
        readonly uint[] lastConfirmedTick;
        readonly uint[] contiguousConfirmedTick;
        readonly ushort[] lastConfirmedInput;

        uint firstMispredictedTick = NoMispredict;

        public InputRingBuffer()
        {
            cells = new Cell[MaxSlots][];
            for (int s = 0; s < MaxSlots; s++)
                cells[s] = new Cell[Capacity];
            lastConfirmedTick = new uint[MaxSlots];
            contiguousConfirmedTick = new uint[MaxSlots];
            lastConfirmedInput = new ushort[MaxSlots];
        }

        /// <summary>
        /// Earliest tick simulated with a wrong prediction, or
        /// <see cref="NoMispredict"/> when none is pending.
        /// </summary>
        public uint FirstMispredictedTick
        {
            get { return firstMispredictedTick; }
        }

        /// <summary>Clears the pending misprediction marker.</summary>
        public void AcknowledgeMispredict()
        {
            firstMispredictedTick = NoMispredict;
        }

        /// <summary>Latest confirmed tick for a slot (may have older
        /// gaps behind it; use for pacing, not safety).</summary>
        public uint LastConfirmedTick(int slot)
        {
            return lastConfirmedTick[slot];
        }

        /// <summary>
        /// Latest tick for a slot with no unconfirmed tick behind it
        /// (anchored at the slot's first confirm). State at or before
        /// this tick is built from real inputs only, never from
        /// predictions, so authority checks are safe up to here.
        /// </summary>
        public uint ContiguousConfirmedTick(int slot)
        {
            return contiguousConfirmedTick[slot];
        }

        /// <summary>Stores a confirmed input (local or from network).</summary>
        public void Confirm(int slot, uint tick, ushort input)
        {
            ref var cell = ref cells[slot][tick % Capacity];
            bool mispredicted = cell.Tick == tick
                && cell.UsedPrediction
                && !cell.Confirmed
                && cell.Input != input;
            if (mispredicted && tick < firstMispredictedTick)
                firstMispredictedTick = tick;
            cell.Tick = tick;
            cell.Input = input;
            cell.Confirmed = true;
            cell.UsedPrediction = false;
            if (tick >= lastConfirmedTick[slot])
            {
                lastConfirmedTick[slot] = tick;
                lastConfirmedInput[slot] = input;
            }
            AdvanceContiguous(slot, tick);
        }

        void AdvanceContiguous(int slot, uint tick)
        {
            if (contiguousConfirmedTick[slot] == 0)
                contiguousConfirmedTick[slot] = tick;
            else if (tick != contiguousConfirmedTick[slot] + 1)
                return;
            uint next = contiguousConfirmedTick[slot] + 1;
            while (true)
            {
                ref var cell = ref cells[slot][next % Capacity];
                if (cell.Tick != next || !cell.Confirmed)
                    break;
                contiguousConfirmedTick[slot] = next;
                next++;
            }
        }

        /// <summary>
        /// Returns the input to simulate this tick: the confirmed value
        /// when known, else a repeat-last-confirmed prediction (recorded
        /// so a later contradicting confirm triggers a rollback).
        /// </summary>
        public ushort Get(int slot, uint tick)
        {
            ref var cell = ref cells[slot][tick % Capacity];
            if (cell.Tick == tick && cell.Confirmed)
                return cell.Input;
            ushort predicted = lastConfirmedInput[slot];
            cell.Tick = tick;
            cell.Input = predicted;
            cell.Confirmed = false;
            cell.UsedPrediction = true;
            return predicted;
        }

        /// <summary>
        /// Reads a confirmed input without recording a prediction
        /// (used to build redundant send windows).
        /// </summary>
        public bool TryGetConfirmed(int slot, uint tick, out ushort input)
        {
            ref var cell = ref cells[slot][tick % Capacity];
            if (cell.Tick == tick && cell.Confirmed)
            {
                input = cell.Input;
                return true;
            }
            input = 0;
            return false;
        }

        /// <summary>Resets all slots (new match).</summary>
        public void Clear()
        {
            for (int s = 0; s < MaxSlots; s++)
            {
                System.Array.Clear(cells[s], 0, Capacity);
                lastConfirmedTick[s] = 0;
                contiguousConfirmedTick[s] = 0;
                lastConfirmedInput[s] = 0;
            }
            firstMispredictedTick = NoMispredict;
        }
    }
}
