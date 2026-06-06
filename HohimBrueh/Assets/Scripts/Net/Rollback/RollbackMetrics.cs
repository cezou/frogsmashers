using FrogSmashers.Net.Sim;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Lightweight counters describing how hard the rollback works
    /// during an online match: depth distribution of resimulations
    /// and pace-bias behavior. Reset at match start, summarized at
    /// match end and asserted by the latency gate together with the
    /// AuthoritySync correction counters.
    /// </summary>
    public static class RollbackMetrics
    {
        const int depthBuckets = 64;

        static readonly int[] depthCounts = new int[depthBuckets];

        static int rollbackCount;
        static int maxDepth;
        static long leadTotal;
        static long leadMin;
        static long leadMax;
        static int paceSamples;
        static int biasBursts;
        static int stallFrames;
        static uint startTick;

        /// <summary>Rollback resimulations since reset.</summary>
        public static int RollbackCount
        {
            get { return rollbackCount; }
        }

        /// <summary>Deepest rollback in ticks since reset.</summary>
        public static int MaxDepth
        {
            get { return maxDepth; }
        }

        /// <summary>Clears every counter (call at match start).</summary>
        public static void Reset()
        {
            for (int i = 0; i < depthBuckets; i++)
                depthCounts[i] = 0;
            rollbackCount = 0;
            maxDepth = 0;
            leadTotal = 0;
            leadMin = long.MaxValue;
            leadMax = long.MinValue;
            paceSamples = 0;
            biasBursts = 0;
            stallFrames = 0;
            startTick = SimClock.CurrentTick;
        }

        /// <summary>Records one frame stalled by the prediction gate.</summary>
        public static void RecordStallFrame()
        {
            stallFrames++;
        }

        /// <summary>Records one rollback resimulation of N ticks.</summary>
        public static void RecordRollback(int depthTicks)
        {
            rollbackCount++;
            if (depthTicks > maxDepth)
                maxDepth = depthTicks;
            int bucket = depthTicks < depthBuckets
                ? depthTicks : depthBuckets - 1;
            if (bucket < 0)
                bucket = 0;
            depthCounts[bucket]++;
        }

        /// <summary>Records one pace check against the host clock.</summary>
        public static void RecordPace(long lead, bool biased)
        {
            paceSamples++;
            leadTotal += lead;
            if (lead < leadMin)
                leadMin = lead;
            if (lead > leadMax)
                leadMax = lead;
            if (biased)
                biasBursts++;
        }

        /// <summary>Rollback depth percentile in ticks (0 when none).</summary>
        public static int DepthPercentile(double fraction)
        {
            if (rollbackCount == 0)
                return 0;
            int threshold =
                (int)System.Math.Ceiling(rollbackCount * fraction);
            int seen = 0;
            for (int i = 0; i < depthBuckets; i++)
            {
                seen += depthCounts[i];
                if (seen >= threshold)
                    return i;
            }
            return depthBuckets - 1;
        }

        /// <summary>One-line digest of every counter for the logs.</summary>
        public static string Summary()
        {
            uint ticks = SimClock.CurrentTick - startTick;
            double seconds = ticks / SimClock.TickRate;
            double perSecond = seconds > 0
                ? rollbackCount / seconds : 0;
            double leadAvg = paceSamples > 0
                ? (double)leadTotal / paceSamples : 0;
            string lead = paceSamples > 0
                ? $"{leadAvg:0.0} [{leadMin}..{leadMax}]" : "n/a";
            return "[RollbackMetrics] rollbacks=" + rollbackCount
                + $" ({perSecond:0.0}/s, p50="
                + DepthPercentile(0.50) + " p95="
                + DepthPercentile(0.95) + $" max={maxDepth} ticks)"
                + $" pace lead avg={lead} biased={biasBursts}"
                + $" stalls={stallFrames} over {ticks} ticks";
        }
    }
}
