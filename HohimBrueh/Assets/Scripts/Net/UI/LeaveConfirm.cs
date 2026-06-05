using UnityEngine;
using UnityEngine.InputSystem;

namespace FrogSmashers.Net.UI
{
    /// <summary>
    /// Double-Escape leave flow for online play: the first Escape
    /// arms a 2-second confirmation overlay, the second one actually
    /// disconnects and returns to the main menu.
    /// </summary>
    public class LeaveConfirm : MonoBehaviour
    {
        const float confirmWindow = 2f;

        float armedUntil = -1f;
        GUIStyle style;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var host = new GameObject("LeaveConfirm");
            DontDestroyOnLoad(host);
            host.AddComponent<LeaveConfirm>();
        }

        void Update()
        {
            if (!OnlineMatch.Active)
            {
                armedUntil = -1f;
                return;
            }
            bool escape = Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (!escape)
                return;
            if (Time.unscaledTime < armedUntil)
            {
                armedUntil = -1f;
                OnlineMatch.LeaveLocal();
            }
            else
            {
                armedUntil = Time.unscaledTime + confirmWindow;
            }
        }

        void OnGUI()
        {
            if (!OnlineMatch.Active || Time.unscaledTime >= armedUntil)
                return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                style.normal.textColor = new Color(1f, 0.85f, 0.25f);
            }
            GUI.Label(new Rect(0f, Screen.height * 0.4f,
                Screen.width, 60f),
                "PRESS ESCAPE AGAIN TO LEAVE", style);
        }
    }
}
