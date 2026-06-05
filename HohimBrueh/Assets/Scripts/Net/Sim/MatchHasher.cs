namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Canonical per-tick fingerprint of the whole simulation, shared
    /// by the determinism gates and online desync detection.
    /// </summary>
    public static class MatchHasher
    {
        /// <summary>Hashes the current tick's complete sim state.</summary>
        public static uint Compute()
        {
            uint h = StateHash.Seed;
            h = StateHash.Mix(h, SimClock.CurrentTick);
            h = StateHash.Mix(h, DeterministicRng.Match.State);
            h = GameController.HashSimState(h);
            return h;
        }
    }
}
