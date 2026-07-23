namespace FrogSmashers.Net
{
    /// <summary>
    /// Pure standing math for the online match flow. Round wins are
    /// tallied per slot and departed players keep theirs, so every
    /// question ("is it decided?", "who leads?") must only consider
    /// the slots still present — and in team mode the contenders are
    /// the teams of those slots, not the individual slots.
    /// </summary>
    public static class MatchStandings
    {
        /// <summary>
        /// True when the current leader can no longer be caught by any
        /// other contender, even if that contender took every one of
        /// the remaining rounds. Contenders are the active slots (FFA)
        /// or the teams of the active slots (team mode).
        /// </summary>
        public static bool Clinched(
            int[] winsBySlot, bool[] activeBySlot, Team[] teamBySlot,
            bool teamMode, int remaining)
        {
            int leader;
            int second;
            if (teamMode)
            {
                int blue = TeamWins(
                    winsBySlot, activeBySlot, teamBySlot, Team.Blue);
                int red = TeamWins(
                    winsBySlot, activeBySlot, teamBySlot, Team.Red);
                leader = System.Math.Max(blue, red);
                second = System.Math.Min(blue, red);
            }
            else
            {
                TopTwo(winsBySlot, activeBySlot,
                    out leader, out second);
            }
            return leader > second + remaining;
        }

        /// <summary>
        /// Slot presented as the overall winner: the active slot with
        /// the most wins — restricted to the leading team in team mode
        /// (ties: lowest slot) — or -1 when no slot is active.
        /// </summary>
        public static int LeadingSlot(
            int[] winsBySlot, bool[] activeBySlot, Team[] teamBySlot,
            bool teamMode)
        {
            bool teamFilter = false;
            Team leadingTeam = Team.Blue;
            if (teamMode)
            {
                int blue = TeamWins(
                    winsBySlot, activeBySlot, teamBySlot, Team.Blue);
                int red = TeamWins(
                    winsBySlot, activeBySlot, teamBySlot, Team.Red);
                if (blue != red)
                {
                    teamFilter = true;
                    leadingTeam = blue > red ? Team.Blue : Team.Red;
                }
            }
            int best = -1;
            int bestWins = -1;
            for (int slot = 0; slot < winsBySlot.Length; slot++)
            {
                if (!activeBySlot[slot])
                    continue;
                if (teamFilter && teamBySlot[slot] != leadingTeam)
                    continue;
                if (winsBySlot[slot] > bestWins)
                {
                    bestWins = winsBySlot[slot];
                    best = slot;
                }
            }
            return best;
        }

        static int TeamWins(
            int[] winsBySlot, bool[] activeBySlot, Team[] teamBySlot,
            Team team)
        {
            int wins = 0;
            for (int slot = 0; slot < winsBySlot.Length; slot++)
            {
                if (activeBySlot[slot] && teamBySlot[slot] == team)
                    wins += winsBySlot[slot];
            }
            return wins;
        }

        static void TopTwo(
            int[] winsBySlot, bool[] activeBySlot,
            out int leader, out int second)
        {
            leader = 0;
            second = 0;
            for (int slot = 0; slot < winsBySlot.Length; slot++)
            {
                if (!activeBySlot[slot])
                    continue;
                int w = winsBySlot[slot];
                if (w > leader)
                {
                    second = leader;
                    leader = w;
                }
                else if (w > second)
                {
                    second = w;
                }
            }
        }
    }
}
