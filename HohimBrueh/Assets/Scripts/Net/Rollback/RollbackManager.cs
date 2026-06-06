using FreeLives;
using FrogSmashers.Net.Sim;
using UnityEngine;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Owns the rollback loop. A snapshot is saved after every tick;
    /// before every live step, if the input buffer reports that an
    /// already-simulated tick used a wrong prediction, the state is
    /// restored to just before it and resimulated to the present with
    /// the corrected inputs.
    /// </summary>
    public class RollbackManager
    {
        const int snapshotCapacity = 128;
        const int maxRollbackTicks = 60;

        /// <summary>Manager driving the current online session.</summary>
        public static RollbackManager Active { get; private set; }

        public InputRingBuffer Inputs { get; }

        /// <summary>Per-tick snapshots of the current match.</summary>
        public SnapshotRingBuffer Snapshots
        {
            get { return snapshots; }
        }

        readonly SnapshotRingBuffer snapshots;

        RollbackManager()
        {
            Inputs = new InputRingBuffer();
            snapshots = new SnapshotRingBuffer(snapshotCapacity);
        }

        /// <summary>
        /// Creates the manager, hooks the driver and routes character
        /// input reads through the rollback buffer.
        /// </summary>
        public static RollbackManager Enable()
        {
            if (Active != null)
                Disable();
            Active = new RollbackManager();
            SimulationDriver.BeforeStep += Active.ProcessRollback;
            SimulationDriver.TickCompleted += Active.SaveSnapshot;
            InputReader.ActiveSource =
                new RollbackInputSource(Active.Inputs);
            return Active;
        }

        /// <summary>Unhooks the driver and discards the manager.</summary>
        public static void Disable()
        {
            if (Active == null)
                return;
            SimulationDriver.BeforeStep -= Active.ProcessRollback;
            SimulationDriver.TickCompleted -= Active.SaveSnapshot;
            Active = null;
        }

        /// <summary>Saves the pre-sim baseline (call at match start).</summary>
        public void SaveBaseline()
        {
            snapshots.Save(SimClock.CurrentTick);
        }

        void SaveSnapshot(uint tick)
        {
            snapshots.Save(tick);
        }

        void ProcessRollback()
        {
            uint mispredicted = Inputs.FirstMispredictedTick;
            if (mispredicted == InputRingBuffer.NoMispredict)
                return;

            uint present = SimClock.CurrentTick;
            if (mispredicted > present)
            {
                Inputs.AcknowledgeMispredict();
                return;
            }
            if (present + 1 - mispredicted > maxRollbackTicks)
            {
                Debug.LogWarning("[RollbackManager] Mispredict at tick"
                    + $" {mispredicted} exceeds the {maxRollbackTicks}"
                    + " tick window");
                Inputs.AcknowledgeMispredict();
                return;
            }

            var snap = snapshots.TryGet(mispredicted - 1);
            if (snap == null || !GameController.RestoreFrom(snap))
            {
                Debug.LogError("[RollbackManager] Missing snapshot for"
                    + $" tick {mispredicted - 1}, cannot roll back");
                Inputs.AcknowledgeMispredict();
                return;
            }

            Inputs.AcknowledgeMispredict();
            RollbackMetrics.RecordRollback(
                (int)(present + 1 - mispredicted));
            SimulationDriver.IsResimulating = true;
            while (SimClock.CurrentTick < present)
                SimulationDriver.StepNow();
            SimulationDriver.IsResimulating = false;
        }
    }
}
