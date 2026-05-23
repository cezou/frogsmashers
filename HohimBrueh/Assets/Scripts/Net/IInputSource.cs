using FreeLives;

namespace FrogSmashers.Net
{
    public interface IInputSource
    {
        void Read(InputReader.Device device, InputState target);
    }
}
