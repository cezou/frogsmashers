namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Implemented by every object that takes part in the fixed-rate
    /// gameplay simulation driven by <see cref="SimulationDriver"/>.
    /// </summary>
    public interface ISimTickable
    {
        /// <summary>Tick ordering; lower values tick first.</summary>
        int SimOrder { get; }

        /// <summary>Advances this object by one fixed simulation step.</summary>
        void SimTick(float dt);
    }
}
