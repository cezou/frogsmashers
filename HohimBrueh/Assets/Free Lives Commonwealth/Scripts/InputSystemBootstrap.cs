using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeLives
{
    public static class InputSystemBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // DualShock4 floods ~5 MB/frame of gyro/accelerometer/touchpad
            // HID reports that we never read in InputReader.cs. The default
            // ~5 MB cap throws "Exceeded budget for maximum input event
            // throughput". Lift the cap — sensor events are still discarded
            // silently by our reading code, only the buffer pressure changes.
            InputSystem.settings.maxEventBytesPerUpdate = 0;
        }
    }
}
