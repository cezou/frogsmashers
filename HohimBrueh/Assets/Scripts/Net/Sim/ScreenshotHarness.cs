using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Net.Sim
{
    /// <summary>
    /// Boots the game into a chosen state and writes a PNG, for an
    /// agent-driven visual verification loop (no human input needed).
    /// Activated by <c>-screenshot</c>; inert otherwise. Launch WITHOUT
    /// <c>-nographics</c> (rendering is required) — ideally a plain windowed
    /// run. Flags:
    /// <list type="bullet">
    /// <item><c>-shotScene &lt;name&gt;</c> load a scene directly (e.g.
    /// MainMenu, JoinScreen); omit to keep whatever another flag booted,
    /// e.g. <c>-netLobbyHost -scriptedLocal</c> for the solo lobby.</item>
    /// <item><c>-shotDelay &lt;sec&gt;</c> settle time before capture
    /// (default 3; use ~8 for the relay lobby).</item>
    /// <item><c>-shotPath &lt;path&gt;</c> output PNG (default
    /// C:\frogsmashers\shot.png).</item>
    /// <item><c>-shotPad &lt;xbox|ps&gt;</c> add a virtual gamepad of
    /// that brand before the scene loads, to verify device-aware
    /// control prompts (issue #4).</item>
    /// <item><c>-shotJoin</c> on JoinScreen, join Keyboard1 onto the
    /// host slot and Gamepad1 onto the second slot so the hint lines
    /// are visible.</item>
    /// <item><c>-shotReady</c> with -shotJoin, also confirm the
    /// joined slots so the Ready hints show.</item>
    /// <item><c>-shotTeam</c> with -shotJoin, switch to team mode
    /// first so the shade arrows line shows.</item>
    /// <item><c>-shotTitle</c> on TitleScreen, skip the intro pan so
    /// the START prompt is in view.</item>
    /// </list>
    /// Uses <see cref="ScreenCapture.CaptureScreenshot(string)"/> so the
    /// full frame (3D + overlay UI) is captured, then quits once the file
    /// is on disk so it is flushed before the agent reads it.
    /// </summary>
    public class ScreenshotHarness : MonoBehaviour
    {
        static string shotScene;
        static float shotDelay = 3f;
        static string shotPath = @"C:\frogsmashers\shot.png";
        static bool shotJoin;
        static bool shotReady;
        static bool shotTeam;
        static bool shotTitle;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (!HasCliArg("-screenshot"))
                return;
            shotScene = GetCliArg("-shotScene", null);
            shotDelay = GetCliArgFloat("-shotDelay", 3f);
            shotPath = GetCliArg("-shotPath", shotPath);
            shotJoin = HasCliArg("-shotJoin");
            shotReady = HasCliArg("-shotReady");
            shotTeam = HasCliArg("-shotTeam");
            shotTitle = HasCliArg("-shotTitle");
            AddVirtualPad(GetCliArg("-shotPad", null));
            var go = new GameObject("ScreenshotHarness");
            DontDestroyOnLoad(go);
            go.AddComponent<ScreenshotHarness>();
        }

        static void AddVirtualPad(string brand)
        {
            if (string.IsNullOrEmpty(brand))
                return;
            string[] layouts = brand == "ps"
                ? new[] { "DualShock4GamepadHID", "DualShockGamepad" }
                : new[] { "XInputControllerWindows", "XInputController" };
            foreach (var layout in layouts)
            {
                try
                {
                    InputSystem.AddDevice(layout);
                    Debug.Log($"[Screenshot] Added virtual pad '{layout}'");
                    return;
                }
                catch (System.Exception)
                {
                }
            }
            Debug.LogWarning($"[Screenshot] No pad layout for '{brand}'");
        }

        IEnumerator Start()
        {
            if (!string.IsNullOrEmpty(shotScene))
            {
                Debug.Log($"[Screenshot] Loading scene '{shotScene}'");
                SceneManager.LoadScene(shotScene);
                yield return null;
            }
            if (shotJoin)
            {
                yield return null;
                if (shotTeam && !GameController.isTeamMode)
                    GameController.ToggleTeamMode();
                bool kb = GameController.DebugAssignSlot(0,
                    FreeLives.InputReader.Device.Keyboard1);
                bool pad = GameController.DebugAssignSlot(1,
                    FreeLives.InputReader.Device.Gamepad1);
                Debug.Log($"[Screenshot] Joined kb1={kb} pad1={pad}");
                if (shotReady)
                {
                    yield return null;
                    var canvases = GameController.GetJoinCanvases();
                    foreach (var joinCanvas in canvases)
                    {
                        if (joinCanvas != null
                            && joinCanvas.HasAssignedPlayer())
                            joinCanvas.ConfirmSelection();
                    }
                    Debug.Log("[Screenshot] Confirmed joined slots");
                }
            }
            if (shotTitle)
            {
                yield return null;
                var intro = FindFirstObjectByType<IntroAnimController>();
                if (intro != null)
                    intro.SkipToTitle();
                Debug.Log($"[Screenshot] Title skip={(intro != null)}");
            }
            yield return new WaitForSecondsRealtime(shotDelay);

            var dir = Path.GetDirectoryName(shotPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(shotPath))
                File.Delete(shotPath);

            ScreenCapture.CaptureScreenshot(shotPath);
            Debug.Log($"[Screenshot] Capturing → {shotPath}");

            float waited = 0f;
            while (!File.Exists(shotPath) && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log(File.Exists(shotPath)
                ? $"[Screenshot] Wrote {shotPath}"
                : $"[Screenshot] FAILED to write {shotPath}");
            Application.Quit(0);
        }

        static bool HasCliArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name)
                    return true;
            }
            return false;
        }

        static string GetCliArg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }
            return fallback;
        }

        static float GetCliArgFloat(string name, float fallback)
        {
            var raw = GetCliArg(name, null);
            return raw != null && float.TryParse(raw, out float value)
                ? value : fallback;
        }
    }
}
