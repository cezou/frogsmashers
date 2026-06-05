using UnityEngine;
using UnityEngine.InputSystem;

namespace FrogSmashers.Net.UI
{
    /// <summary>
    /// Minimal runtime overlay shown during the online lobby brawl:
    /// roster with ready flags, countdown, and the ready prompt. Also
    /// reads the local START/Enter press (frame-level control plane,
    /// not a sim input) and pumps the host's countdown logic.
    /// </summary>
    public class OnlineLobbyOverlay : MonoBehaviour
    {
        static OnlineLobbyOverlay instance;

        GUIStyle title;
        GUIStyle line;

        /// <summary>Creates the overlay for the current lobby.</summary>
        public static void Create()
        {
            if (instance != null)
                return;
            var host = new GameObject("OnlineLobbyOverlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<OnlineLobbyOverlay>();
        }

        /// <summary>Removes the overlay (lobby over).</summary>
        public static void Destroy()
        {
            if (instance == null)
                return;
            Object.Destroy(instance.gameObject);
            instance = null;
        }

        void Update()
        {
            if (!OnlineMatch.InLobby)
                return;
            OnlineMatch.LobbyFrameUpdate(Time.deltaTime);
            bool toggle =
                (Keyboard.current != null
                    && Keyboard.current.enterKey.wasPressedThisFrame)
                || (Gamepad.current != null
                    && Gamepad.current.startButton.wasPressedThisFrame);
            if (toggle)
            {
                var driver = Rollback.RollbackNetDriver.Active;
                if (driver != null)
                {
                    Debug.Log("[OnlineLobbyOverlay] Ready pressed,"
                        + " same-frame poll probe ="
                        + $" {driver.PollLocalDevices():X4}");
                }
                OnlineMatch.ToggleLocalReady();
            }
        }

        void OnGUI()
        {
            if (!OnlineMatch.InLobby)
                return;
            EnsureStyles();
            float x = 24f;
            float y = 24f;
            GUI.Label(new Rect(x, y, 700f, 44f), "ONLINE LOBBY", title);
            y += 48f;
            var roster = OnlineMatch.Roster;
            for (int i = 0; i < roster.Count; i++)
            {
                string ready = roster[i].Ready ? "READY" : "...";
                GUI.Label(new Rect(x, y, 700f, 34f),
                    $"{roster[i].Name}  [{ready}]", line);
                y += 34f;
            }
            y += 10f;
            if (OnlineMatch.Countdown >= 0f)
            {
                GUI.Label(new Rect(x, y, 700f, 44f),
                    "STARTING IN "
                    + $"{Mathf.CeilToInt(OnlineMatch.Countdown)}",
                    title);
            }
            else
            {
                GUI.Label(new Rect(x, y, 700f, 34f),
                    "PRESS START / ENTER WHEN READY", line);
            }
        }

        void EnsureStyles()
        {
            if (title != null)
                return;
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
            };
            title.normal.textColor = new Color(1f, 0.85f, 0.25f);
            line = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
            };
            line.normal.textColor = Color.white;
        }
    }
}
