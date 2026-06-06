using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Drives <see cref="NetSimulator.Pump"/> every frame. Created
    /// lazily by the simulator on its first deferred message; never
    /// present when the simulator is disabled.
    /// </summary>
    public class NetSimulatorPump : MonoBehaviour
    {
        void Update()
        {
            NetSimulator.Pump();
        }
    }
}
