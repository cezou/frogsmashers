using FreeLives;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Stateless deterministic input generator for the determinism
    /// harness: inputs are a pure function of (tick, device), so two
    /// replays produce bit-identical input streams.
    /// </summary>
    public class ScriptedInputSource : FrogSmashers.Net.IInputSource
    {
        const uint phaseTicks = 12;

        public void Read(InputReader.Device device, InputState target)
        {
            uint phase = SimClock.CurrentTick / phaseTicks;
            uint h = Scramble(phase, (uint)device);

            bool left = (h & 1u) != 0 && (h & 2u) == 0;
            bool right = (h & 2u) != 0 && (h & 1u) == 0;

            target.left = left;
            target.right = right;
            target.up = (h & 4u) != 0;
            target.down = (h & 8u) != 0 && (h & 4u) == 0;
            target.aButton = (h & 16u) != 0;
            target.xButton = (h & 32u) != 0 && (h & 64u) == 0;
            target.bButton = (h & 128u) != 0;
            target.yButton = false;
            target.start = false;

            target.xAxis = left ? -1f : (right ? 1f : 0f);
            target.yAxis = target.up ? 1f : (target.down ? -1f : 0f);
            target.leftTrigger = 0f;
            target.rightTrigger = 0f;
        }

        static uint Scramble(uint phase, uint device)
        {
            uint h = StateHash.Seed;
            h = StateHash.Mix(h, phase);
            h = StateHash.Mix(h, device * 2654435761u);
            return h;
        }
    }
}
