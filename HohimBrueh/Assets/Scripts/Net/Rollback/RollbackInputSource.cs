using FreeLives;
using FrogSmashers.Net.Sim;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// IInputSource implementation for online play: characters read
    /// their inputs from the rollback input buffer at the current sim
    /// tick instead of polling local devices. Online player slots map
    /// to the Gamepad1-4 device aliases.
    /// </summary>
    public class RollbackInputSource : IInputSource
    {
        readonly InputRingBuffer buffer;

        public RollbackInputSource(InputRingBuffer buffer)
        {
            this.buffer = buffer;
        }

        /// <summary>Maps a device alias to an online slot, or -1.</summary>
        public static int SlotForDevice(InputReader.Device device)
        {
            switch (device)
            {
                case InputReader.Device.Gamepad1: return 0;
                case InputReader.Device.Gamepad2: return 1;
                case InputReader.Device.Gamepad3: return 2;
                case InputReader.Device.Gamepad4: return 3;
                default: return -1;
            }
        }

        public void Read(InputReader.Device device, InputState target)
        {
            int slot = SlotForDevice(device);
            if (slot < 0)
            {
                InputReader.ClearInputState(target);
                return;
            }
            ushort packed = buffer.Get(slot, SimClock.CurrentTick);
            InputPacking.Unpack(packed, target);
        }
    }
}
