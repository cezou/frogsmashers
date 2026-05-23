using FreeLives;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogSmashers.Net
{
    public static class ServerBootstrap
    {
        const string DefaultLevel = "1BusStop";

        static bool _redirected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (!ServerMode.IsServer) return;

            Debug.Log("[ServerBootstrap] Headless server mode active");
            Application.targetFrameRate = 60;
            InputReader.ActiveSource = new RemoteInputSource();

            GameController.activePlayers.Clear();
            GameController.activePlayers.Add(new Player(InputReader.Device.Gamepad1, Color.red,  0));
            GameController.activePlayers.Add(new Player(InputReader.Device.Gamepad2, Color.blue, 1));
            GameController.isTeamMode = false;

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_redirected) return;
            if (scene.name == DefaultLevel)
            {
                _redirected = true;
                Debug.Log($"[ServerBootstrap] Entered level '{DefaultLevel}', simulation running");
                return;
            }
            _redirected = true;
            Debug.Log($"[ServerBootstrap] Loaded '{scene.name}', redirecting to '{DefaultLevel}'");
            SceneManager.LoadScene(DefaultLevel);
            _redirected = false; // allow re-entry once for the redirect itself
        }
    }
}
