namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Fixed-capacity ring of <see cref="MatchSnapshot"/> indexed by tick.
    /// Slots are preallocated and rewritten in place (no GC churn).
    /// </summary>
    public class SnapshotRingBuffer
    {
        readonly MatchSnapshot[] slots;

        public SnapshotRingBuffer(int capacity)
        {
            slots = new MatchSnapshot[capacity];
            for (int i = 0; i < capacity; i++)
                slots[i] = new MatchSnapshot();
        }

        /// <summary>Captures the current sim state under this tick.</summary>
        public void Save(uint tick)
        {
            var snap = slots[tick % (uint)slots.Length];
            GameController.SaveTo(snap);
            snap.Tick = tick;
            snap.Valid = true;
        }

        /// <summary>Returns the snapshot for a tick, or null if evicted.</summary>
        public MatchSnapshot TryGet(uint tick)
        {
            var snap = slots[tick % (uint)slots.Length];
            if (snap.Valid && snap.Tick == tick)
                return snap;
            return null;
        }

        /// <summary>Invalidates all stored snapshots.</summary>
        public void Clear()
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i].Valid = false;
        }
    }
}
