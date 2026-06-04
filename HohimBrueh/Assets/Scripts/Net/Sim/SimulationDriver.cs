using System.Collections.Generic;
using UnityEngine;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Central fixed-rate driver. Accumulates frame time and steps every
    /// registered <see cref="ISimTickable"/> at <see cref="SimClock.TickRate"/>
    /// in deterministic order (SimOrder, then registration sequence).
    /// </summary>
    public class SimulationDriver : MonoBehaviour
    {
        const int maxTicksPerFrame = 5;

        static readonly List<Entry> tickables = new List<Entry>();
        static readonly List<Entry> scratch = new List<Entry>();
        static SimulationDriver instance;
        static int nextSequence;
        static bool dirty;

        float accumulator;

        /// <summary>Stops ticking (scene transitions, harness resets).</summary>
        public static bool Paused { get; set; }

        /// <summary>Runs exactly N ticks per frame when set (harness).</summary>
        public static int ForcedTicksPerFrame { get; set; }

        class Entry
        {
            public ISimTickable Tickable;
            public int Order;
            public int Sequence;
            public bool Active;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null)
                return;
            var host = new GameObject("SimulationDriver");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SimulationDriver>();
        }

        /// <summary>Adds a tickable to the simulation loop.</summary>
        public static void Register(ISimTickable tickable)
        {
            for (int i = 0; i < tickables.Count; i++)
            {
                if (tickables[i].Tickable == tickable)
                    return;
            }
            tickables.Add(new Entry
            {
                Tickable = tickable,
                Order = tickable.SimOrder,
                Sequence = nextSequence++,
                Active = true,
            });
            dirty = true;
        }

        /// <summary>Removes a tickable from the simulation loop.</summary>
        public static void Unregister(ISimTickable tickable)
        {
            for (int i = tickables.Count - 1; i >= 0; i--)
            {
                if (tickables[i].Tickable == tickable)
                {
                    tickables[i].Active = false;
                    tickables.RemoveAt(i);
                    return;
                }
            }
        }

        void Update()
        {
            if (Paused)
            {
                accumulator = 0f;
                return;
            }
            if (ForcedTicksPerFrame > 0)
            {
                for (int i = 0; i < ForcedTicksPerFrame; i++)
                    Step();
                return;
            }
            accumulator += Time.deltaTime;
            int steps = 0;
            while (accumulator >= SimClock.TickDt
                && steps < maxTicksPerFrame)
            {
                accumulator -= SimClock.TickDt;
                steps++;
                Step();
            }
            if (steps == maxTicksPerFrame)
                accumulator = 0f;
        }

        static void Step()
        {
            SimClock.Advance();
            SortIfDirty();
            scratch.Clear();
            scratch.AddRange(tickables);
            for (int i = 0; i < scratch.Count; i++)
            {
                if (scratch[i].Active)
                    scratch[i].Tickable.SimTick(SimClock.TickDt);
            }
        }

        static void SortIfDirty()
        {
            if (!dirty)
                return;
            dirty = false;
            tickables.Sort(CompareEntries);
        }

        static int CompareEntries(Entry a, Entry b)
        {
            if (a.Order != b.Order)
                return a.Order.CompareTo(b.Order);
            return a.Sequence.CompareTo(b.Sequence);
        }
    }
}
