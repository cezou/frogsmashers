using System;
using FreeLives;
using UnityEngine;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Single chokepoint for non-deterministic sim-path SFX/FX. No-ops
    /// during rollback resimulation so each effect fires once per event,
    /// not once per replayed tick. Sounds and particles live outside the
    /// snapshotted state, so gating them never affects determinism.
    /// </summary>
    public static class SimFx
    {
        /// <summary>Plays a spatial sound unless we are resimulating.</summary>
        public static void Play(string name, float volume, Vector3 pos)
        {
            if (SimulationDriver.IsResimulating)
                return;
            SoundController.PlaySoundEffect(name, volume, pos);
        }

        /// <summary>Spawns a visual effect unless we are resimulating.</summary>
        public static void Spawn(Action effect)
        {
            if (SimulationDriver.IsResimulating)
                return;
            effect();
        }
    }
}
