namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Fixed-rate simulation clock. All gameplay code must read time from
    /// here instead of UnityEngine.Time so the simulation stays
    /// frame-rate independent and replayable.
    /// </summary>
    public static class SimClock
    {
        /// <summary>Simulation ticks per second.</summary>
        public const float TickRate = 60f;

        /// <summary>Fixed seconds advanced by one simulation tick.</summary>
        public const float TickDt = 1f / TickRate;

        /// <summary>Index of the tick currently being simulated.</summary>
        public static uint CurrentTick { get; private set; }

        /// <summary>Simulation seconds elapsed since match start.</summary>
        public static float SimTime
        {
            get { return CurrentTick * TickDt; }
        }

        /// <summary>Advances the clock by one tick.</summary>
        internal static void Advance()
        {
            CurrentTick++;
        }

        /// <summary>Rewinds the clock to a given tick (rollback).</summary>
        internal static void SetTick(uint tick)
        {
            CurrentTick = tick;
        }

        /// <summary>Resets the clock at the start of a new match.</summary>
        public static void ResetForNewMatch()
        {
            CurrentTick = 0;
        }
    }
}
