using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FrogSmashers.Net.UI
{
    /// <summary>
    /// Minimal IMGUI overlay during online play: the transport-level
    /// round-trip time to the host (or, on the host, the worst RTT
    /// across connected clients), top-left. Sampled twice per second.
    /// </summary>
    public class NetHud : MonoBehaviour
    {
        const float refreshInterval = 0.5f;

        static GUIStyle style;
        static GUIStyle shadow;

        float nextRefresh;
        ulong rttMs;

        void Update()
        {
            if (!OnlineMatch.Active
                || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + refreshInterval;
            rttMs = SampleRtt();
        }

        void OnGUI()
        {
            if (!OnlineMatch.Active)
                return;
            EnsureStyles();
            var rect = new Rect(10f, 8f, 200f, 24f);
            string text = $"PING {rttMs} ms";
            var offset = rect;
            offset.x += 1f;
            offset.y += 1f;
            GUI.Label(offset, text, shadow);
            GUI.Label(rect, text, style);
        }

        static ulong SampleRtt()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null
                || manager.NetworkConfig.NetworkTransport
                    is not UnityTransport transport)
            {
                return 0;
            }
            if (!manager.IsHost)
                return transport.GetCurrentRtt(
                    NetworkManager.ServerClientId);
            ulong worst = 0;
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                if (clientId == manager.LocalClientId)
                    continue;
                ulong rtt = transport.GetCurrentRtt(clientId);
                if (rtt > worst)
                    worst = rtt;
            }
            return worst;
        }

        static void EnsureStyles()
        {
            if (style != null)
                return;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
            style.normal.textColor = Color.white;
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        }
    }
}
