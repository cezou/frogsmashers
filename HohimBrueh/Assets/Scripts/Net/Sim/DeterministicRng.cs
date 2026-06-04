using UnityEngine;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Seeded xorshift64* PRNG used by all gameplay code instead of
    /// UnityEngine.Random, so the simulation is replayable from a seed
    /// and its state can be captured in rollback snapshots.
    /// </summary>
    public class DeterministicRng
    {
        const ulong defaultSeed = 0x9E3779B97F4A7C15UL;

        ulong state;

        /// <summary>Shared instance driving the match simulation.</summary>
        public static readonly DeterministicRng Match =
            new DeterministicRng(defaultSeed);

        public DeterministicRng(ulong seed)
        {
            Reseed(seed);
        }

        /// <summary>Raw generator state, for snapshot save/restore.</summary>
        public ulong State
        {
            get { return state; }
            set { state = value != 0UL ? value : defaultSeed; }
        }

        /// <summary>Resets the generator to a known seed.</summary>
        public void Reseed(ulong seed)
        {
            state = seed != 0UL ? seed : defaultSeed;
        }

        /// <summary>Random float in [0, 1).</summary>
        public float Value
        {
            get { return (NextUInt64() >> 40) * (1f / (1 << 24)); }
        }

        /// <summary>Random float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * Value;
        }

        /// <summary>Random int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt64() % span);
        }

        /// <summary>Random unit-length direction vector.</summary>
        public Vector2 UnitCircle()
        {
            float angle = Value * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        ulong NextUInt64()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }
    }
}
