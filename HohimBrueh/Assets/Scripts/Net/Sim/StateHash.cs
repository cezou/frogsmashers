using UnityEngine;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// FNV-1a hash helpers used to fingerprint simulation state per tick,
    /// for the determinism harness and later for desync detection.
    /// </summary>
    public static class StateHash
    {
        /// <summary>FNV-1a offset basis; start every hash from this.</summary>
        public const uint Seed = 2166136261;

        const uint prime = 16777619;

        /// <summary>Mixes a uint into the hash.</summary>
        public static uint Mix(uint h, uint v)
        {
            h = (h ^ (v & 0xFF)) * prime;
            h = (h ^ ((v >> 8) & 0xFF)) * prime;
            h = (h ^ ((v >> 16) & 0xFF)) * prime;
            h = (h ^ ((v >> 24) & 0xFF)) * prime;
            return h;
        }

        /// <summary>Mixes an int into the hash.</summary>
        public static uint Mix(uint h, int v)
        {
            return Mix(h, (uint)v);
        }

        /// <summary>Mixes a bool into the hash.</summary>
        public static uint Mix(uint h, bool v)
        {
            return Mix(h, v ? 1u : 0u);
        }

        /// <summary>Mixes a float's raw bits into the hash.</summary>
        public static uint Mix(uint h, float v)
        {
            return Mix(h, (uint)System.BitConverter.SingleToInt32Bits(v));
        }

        /// <summary>Mixes a Vector2 into the hash.</summary>
        public static uint Mix(uint h, Vector2 v)
        {
            return Mix(Mix(h, v.x), v.y);
        }

        /// <summary>Mixes a Vector3 into the hash.</summary>
        public static uint Mix(uint h, Vector3 v)
        {
            return Mix(Mix(Mix(h, v.x), v.y), v.z);
        }

        /// <summary>Mixes a ulong into the hash.</summary>
        public static uint Mix(uint h, ulong v)
        {
            return Mix(Mix(h, (uint)v), (uint)(v >> 32));
        }
    }
}
