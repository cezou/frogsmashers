using UnityEngine;

namespace FrogSmashers.Settings
{
    /// <summary>
    /// Hidden DontDestroyOnLoad helper spawned by GameSettings.
    /// Mixer values set before the audio system finishes booting are
    /// silently reset by snapshot initialization, so the saved volumes
    /// are pushed from the first Start callback instead.
    /// </summary>
    public class SettingsBootstrap : MonoBehaviour
    {
        void Start()
        {
            GameSettings.ApplyAudio();
        }
    }
}
