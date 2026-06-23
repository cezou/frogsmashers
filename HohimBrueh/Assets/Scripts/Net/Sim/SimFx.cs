using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FreeLives;
using UnityEngine;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Single chokepoint for non-deterministic sim-path SFX/FX. Each
    /// effect fires once per sim event, not once per replayed tick: the
    /// forward pass always plays (and records the tick); a rollback
    /// resimulation only replays effects for ticks that did NOT already
    /// fire forward. A remote action first applied during a resim still
    /// sounds (its tick never fired), so nothing is muted — only true
    /// duplicates from re-running already-played ticks are dropped.
    ///
    /// Keyed per (slot, call site): different players and different call
    /// sites never suppress each other. Effects live outside the
    /// snapshotted state, so this never affects determinism.
    /// </summary>
    public static class SimFx
    {
        const int Window = 128;

        static readonly Dictionary<long, uint[]> rings =
            new Dictionary<long, uint[]>();

        static bool ShouldEmit(int slot, string file, int line)
        {
            uint tick = SimClock.CurrentTick;
            long key = ((long)slot << 48)
                ^ ((long)(uint)file.GetHashCode() << 16)
                ^ (uint)line;
            if (!rings.TryGetValue(key, out var ring))
            {
                ring = new uint[Window];
                for (int i = 0; i < Window; i++)
                    ring[i] = uint.MaxValue;
                rings[key] = ring;
            }
            int idx = (int)(tick % Window);
            bool firedThisTick = ring[idx] == tick;
            if (!SimulationDriver.IsResimulating)
            {
                ring[idx] = tick;
                return true;
            }
            if (firedThisTick)
                return false;
            ring[idx] = tick;
            return true;
        }

        /// <summary>Plays a spatial sound, de-duped across rollback.</summary>
        public static void Play(int slot, string name, float volume,
            Vector3 pos,
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            if (ShouldEmit(slot, file, line))
                SoundController.PlaySoundEffect(name, volume, pos);
        }

        /// <summary>Spawns a visual effect, de-duped across rollback.</summary>
        public static void Spawn(int slot, Action effect,
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            if (ShouldEmit(slot, file, line))
                effect();
        }
    }
}
