using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Best-effort cleanup when the process closes: deletes the current
    /// session so a host's published lobby is removed on a clean quit
    /// (ALT+F4) instead of lingering as a dead, unjoinable entry in the
    /// public list. A hard crash or freeze still relies on the Sessions
    /// service heartbeat timing the entry out.
    /// </summary>
    public class NetLifecycle : MonoBehaviour
    {
        void OnApplicationQuit()
        {
            if (NetSession.Current != null)
                NetSession.Current.Leave();
        }
    }
}
