using UnityEngine;
using UnityEngine.Audio;

namespace FrogSmashers.Settings
{
    /// <summary>Window mode options offered in the settings menu.</summary>
    public enum WindowMode
    {
        Fullscreen,
        Borderless,
        Windowed
    }

    /// <summary>
    /// Central user settings: PlayerPrefs persistence, immediate
    /// application (window mode, resolution, mixer volumes) and access
    /// to the audio mixer groups every AudioSource routes through.
    /// Saved properties hold the persisted values; PreviewDisplay lets
    /// the UI try a display change before committing it.
    /// </summary>
    public static class GameSettings
    {
        const string WindowModeKey = "Settings.WindowMode";
        const string ResWidthKey = "Settings.ResolutionWidth";
        const string ResHeightKey = "Settings.ResolutionHeight";
        const string MusicKey = "Settings.MusicVolume";
        const string SfxKey = "Settings.SfxVolume";
        const string MixerPath = "GameAudioMixer";
        const float MutedDb = -80f;
        const int DefaultWindowedWidth = 1600;
        const int DefaultWindowedHeight = 900;

        static AudioMixer mixer;
        static bool loaded;
        static WindowMode windowMode;
        static int resolutionWidth;
        static int resolutionHeight;
        static float musicVolume;
        static float sfxVolume;

        /// <summary>Mixer group all music AudioSources output to.</summary>
        public static AudioMixerGroup MusicGroup { get; private set; }

        /// <summary>Mixer group all SFX AudioSources output to.</summary>
        public static AudioMixerGroup SfxGroup { get; private set; }

        /// <summary>Saved window mode; setter applies and persists.</summary>
        public static WindowMode Mode
        {
            get { EnsureLoaded(); return windowMode; }
            set
            {
                EnsureLoaded();
                windowMode = value;
                PlayerPrefs.SetInt(WindowModeKey, (int)value);
                ApplyDisplay(windowMode, resolutionWidth, resolutionHeight);
            }
        }

        /// <summary>Saved resolution width; 0 means native/current.</summary>
        public static int ResolutionWidth
        {
            get { EnsureLoaded(); return resolutionWidth; }
        }

        /// <summary>Saved resolution height; 0 means native/current.</summary>
        public static int ResolutionHeight
        {
            get { EnsureLoaded(); return resolutionHeight; }
        }

        /// <summary>Saved music volume, linear 0..1; applies live.</summary>
        public static float MusicVolume
        {
            get { EnsureLoaded(); return musicVolume; }
            set
            {
                EnsureLoaded();
                musicVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicKey, musicVolume);
                SetMixerVolume("MusicVolume", musicVolume);
            }
        }

        /// <summary>Saved SFX volume, linear 0..1; applies live.</summary>
        public static float SfxVolume
        {
            get { EnsureLoaded(); return sfxVolume; }
            set
            {
                EnsureLoaded();
                sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(SfxKey, sfxVolume);
                SetMixerVolume("SFXVolume", sfxVolume);
            }
        }

        /// <summary>
        /// Loads prefs and the mixer, then spawns the hidden bootstrap
        /// object that pushes volumes once the audio system is ready.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            EnsureLoaded();
            var go = new GameObject("SettingsBootstrap");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<SettingsBootstrap>();
            Object.DontDestroyOnLoad(go);
            if (CanApplyDisplay() && PlayerPrefs.HasKey(WindowModeKey))
                ApplyDisplay(windowMode, resolutionWidth, resolutionHeight);
        }

        /// <summary>
        /// Applies a display configuration without persisting it, so
        /// the UI can preview and revert. Width/height 0 keeps the
        /// current resolution (or a default size for windowed mode).
        /// </summary>
        public static void ApplyDisplay(WindowMode mode, int width,
            int height)
        {
            if (!CanApplyDisplay())
                return;
            int w = width;
            int h = height;
            if (w <= 0 || h <= 0)
            {
                bool windowed = mode == WindowMode.Windowed;
                w = windowed ? DefaultWindowedWidth
                    : Screen.currentResolution.width;
                h = windowed ? DefaultWindowedHeight
                    : Screen.currentResolution.height;
            }
            Screen.SetResolution(w, h, ToUnityMode(mode));
        }

        /// <summary>Persists a previewed display configuration.</summary>
        public static void SaveDisplay(WindowMode mode, int width,
            int height)
        {
            EnsureLoaded();
            windowMode = mode;
            resolutionWidth = width;
            resolutionHeight = height;
            PlayerPrefs.SetInt(WindowModeKey, (int)mode);
            PlayerPrefs.SetInt(ResWidthKey, width);
            PlayerPrefs.SetInt(ResHeightKey, height);
        }

        /// <summary>Pushes both saved volumes onto the mixer.</summary>
        public static void ApplyAudio()
        {
            EnsureLoaded();
            SetMixerVolume("MusicVolume", musicVolume);
            SetMixerVolume("SFXVolume", sfxVolume);
        }

        /// <summary>Flushes pending PlayerPrefs writes to disk.</summary>
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// <summary>Converts a linear 0..1 volume to mixer decibels.</summary>
        public static float LinearToDb(float linear)
        {
            if (linear <= 0.0001f)
                return MutedDb;
            return Mathf.Log10(linear) * 20f;
        }

        static void EnsureLoaded()
        {
            if (loaded)
                return;
            loaded = true;
            windowMode = (WindowMode)PlayerPrefs.GetInt(
                WindowModeKey, (int)WindowMode.Borderless);
            resolutionWidth = PlayerPrefs.GetInt(ResWidthKey, 0);
            resolutionHeight = PlayerPrefs.GetInt(ResHeightKey, 0);
            musicVolume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(MusicKey, 1f));
            sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
            mixer = Resources.Load<AudioMixer>(MixerPath);
            if (mixer == null)
                return;
            MusicGroup = FindGroup("Music");
            SfxGroup = FindGroup("SFX");
        }

        static AudioMixerGroup FindGroup(string name)
        {
            var groups = mixer.FindMatchingGroups(name);
            return groups != null && groups.Length > 0
                ? groups[0] : null;
        }

        static void SetMixerVolume(string parameter, float linear)
        {
            if (mixer != null)
                mixer.SetFloat(parameter, LinearToDb(linear));
        }

        static bool CanApplyDisplay()
        {
            if (Application.isBatchMode)
                return false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-screenshot")
                    return false;
            }
            return true;
        }

        static FullScreenMode ToUnityMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.Fullscreen:
                    return FullScreenMode.ExclusiveFullScreen;
                case WindowMode.Windowed:
                    return FullScreenMode.Windowed;
                default:
                    return FullScreenMode.FullScreenWindow;
            }
        }
    }
}
