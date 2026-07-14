using FreeLives;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrogSmashers.Net.UI
{
    /// <summary>
    /// Drives the local player's real <see cref="JoinCanvas"/> in the
    /// online lobby: the same choose-color icons, with the on-platform frog
    /// frozen and live-recoloring until the player confirms. Reads the
    /// local device directly (control plane, not the rollback buffer)
    /// through the rebindable keyboard/pad bindings so the icon prompts
    /// stay truthful; JoinCanvas's own Update is suppressed online.
    /// B change color/team, L/R shade, X confirm, Y back (un-accept).
    /// Host-only SELECT toggles team mode, and only while the host is
    /// still selecting.
    /// </summary>
    public class OnlineLobbyOverlay : MonoBehaviour
    {
        static OnlineLobbyOverlay instance;

        JoinCanvas canvas;
        readonly InputState input = new InputState();
        bool wasB, wasX, wasY, wasSelect;
        bool lastAccepted;
        bool lastTeamMode;
        GUIStyle title;

        /// <summary>Creates the controller for the current lobby.</summary>
        public static void Create()
        {
            if (instance != null)
                return;
            var host = new GameObject("OnlineLobbyOverlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<OnlineLobbyOverlay>();
        }

        /// <summary>Removes the controller (lobby over).</summary>
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
            EnsureCanvas();
            OnlineMatch.LobbyFrameUpdate(Time.deltaTime);
            ReadControlPlane();
            RefreshVisuals();
        }

        void EnsureCanvas()
        {
            if (canvas != null)
                return;
            var all = GameController.GetJoinCanvases();
            if (all == null || OnlineMatch.LocalSlot >= all.Length)
                return;
            canvas = all[OnlineMatch.LocalSlot];
            if (canvas == null)
                return;
            canvas.gameObject.SetActive(true);
            if (canvas.frogImage != null)
                canvas.frogImage.enabled = false;
            canvas.ApplyOnlineSelectionLayout();
            SetChangeModeLineVisible(OnlineMatch.IsHost);
            lastAccepted = !OnlineMatch.LocalAccepted;
            lastTeamMode = !OnlineMatch.TeamModeEnabled;
        }

        void SetChangeModeLineVisible(bool visible)
        {
            if (canvas.changeModeObjects == null)
                return;
            foreach (var obj in canvas.changeModeObjects)
            {
                if (obj != null)
                    obj.SetActive(visible);
            }
        }

        void ReadControlPlane()
        {
            ReadDevices(input);
            bool select = SelectPressed();

            if (!OnlineMatch.LocalAccepted)
            {
                if (select && !wasSelect && OnlineMatch.IsHost)
                    OnlineMatch.HostToggleTeamMode();
                if (input.bButton && !wasB)
                    OnlineMatch.LobbyCycleChoice();
                if (input.xButton && !wasX)
                    OnlineMatch.LobbyAccept();
                float dir = (input.right ? 1f : 0f) - (input.left ? 1f : 0f);
                if (Mathf.Abs(dir) > 0.5f)
                    OnlineMatch.LobbyAdjustShade(dir, Time.deltaTime);
            }
            else if (input.yButton && !wasY)
            {
                OnlineMatch.LobbyBack();
            }

            wasB = input.bButton;
            wasX = input.xButton;
            wasY = input.yButton;
            wasSelect = select;
        }

        static void ReadDevices(InputState s)
        {
            InputReader.ClearInputState(s);
            var pad = Gamepad.current;
            if (pad != null)
                LocalInputSource.ReadGamepad(pad, s);
            s.bButton |= KeyHeld(FrogSmashers.Settings.SemanticButton.B);
            s.xButton |= KeyHeld(FrogSmashers.Settings.SemanticButton.X);
            s.yButton |= KeyHeld(FrogSmashers.Settings.SemanticButton.Y);
            s.right |= KeyHeld(FrogSmashers.Settings.SemanticButton.Right);
            s.left |= KeyHeld(FrogSmashers.Settings.SemanticButton.Left);
        }

        /// <summary>Reads the rebindable keyboard binding so the
        /// prompt sprites tell the truth.</summary>
        static bool KeyHeld(FrogSmashers.Settings.SemanticButton button)
        {
            var control = FrogSmashers.Settings.ControlBindingService
                .ResolveKeyboard(button);
            return control != null && control.isPressed;
        }

        static bool SelectPressed()
        {
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            return (pad != null && pad.selectButton.isPressed)
                || (kb != null && kb.tabKey.isPressed);
        }

        void RefreshVisuals()
        {
            if (canvas == null)
                return;
            bool accepted = OnlineMatch.LocalAccepted;
            bool team = OnlineMatch.TeamModeEnabled;
            if (accepted != lastAccepted || team != lastTeamMode)
            {
                lastAccepted = accepted;
                lastTeamMode = team;
                GameController.SetModeText(team);
                if (canvas.joinPromptCanvas != null)
                    canvas.joinPromptCanvas.enabled = false;
                if (canvas.chooseColorCanvas != null)
                    canvas.chooseColorCanvas.enabled = !accepted;
                if (canvas.backPromptCanvas != null)
                    canvas.backPromptCanvas.enabled = accepted;
                if (canvas.teamChangeColorObject != null)
                    canvas.teamChangeColorObject.SetActive(team);
                if (canvas.changeColorText != null)
                    canvas.changeColorText.text =
                        team ? "CHANGE TEAM" : "CHANGE COLOR";
            }
        }

        void OnGUI()
        {
            if (!OnlineMatch.InLobby)
                return;
            EnsureStyles();
            if (OnlineMatch.Countdown >= 0f)
            {
                GUI.Label(new Rect(24f, 24f, 700f, 44f),
                    "STARTING IN "
                    + $"{Mathf.CeilToInt(OnlineMatch.Countdown)}", title);
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
        }
    }
}
