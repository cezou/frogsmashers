using FreeLives;

namespace FrogSmashers.Net
{
    // Phase 2: stub. Returns empty input for every device.
    // Phase 3: will pull from a buffer fed by network messages from clients.
    public class RemoteInputSource : IInputSource
    {
        public void Read(InputReader.Device device, InputState target)
        {
            InputReader.ClearInputState(target);
        }
    }
}
