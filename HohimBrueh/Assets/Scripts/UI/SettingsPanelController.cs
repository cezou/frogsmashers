using System;
using System.Collections.Generic;
using FrogSmashers.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FrogSmashers.UI
{
    /// <summary>
    /// Drives the settings overlay: window mode and resolution
    /// cyclers with an apply + keep-or-revert countdown, live volume
    /// sliders, and the gateway to the controls submenu. Owns its
    /// whole open/close lifecycle (EventSystem selection swap,
    /// Escape/East to close) so a future pause menu can reuse it.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        const float ConfirmSeconds = 10f;

        public GameObject root;
        public OptionCycler windowModeCycler;
        public OptionCycler resolutionCycler;
        public Button applyButton;
        public Slider musicSlider;
        public Slider sfxSlider;
        public Button controlsButton;
        public GameObject controlsPanel;
        public Button backButton;
        public GameObject confirmPanel;
        public Text confirmText;
        public Button confirmKeepButton;

        readonly List<(int width, int height)> resolutions =
            new List<(int, int)>();

        GameObject returnSelection;
        Action onClosed;
        float confirmTimeLeft;
        WindowMode savedMode;
        int savedWidth;
        int savedHeight;

        void Start()
        {
            windowModeCycler.SetOptions(new[]
            {
                "FULLSCREEN", "BORDERLESS", "WINDOWED"
            }, (int)GameSettings.Mode);
            BuildResolutionOptions();

            musicSlider.minValue = 0f;
            musicSlider.maxValue = 1f;
            musicSlider.onValueChanged.AddListener(
                v => GameSettings.MusicVolume = v);
            sfxSlider.minValue = 0f;
            sfxSlider.maxValue = 1f;
            sfxSlider.onValueChanged.AddListener(
                v => GameSettings.SfxVolume = v);

            applyButton.onClick.AddListener(OnApplyDisplay);
            controlsButton.onClick.AddListener(ShowControls);
            backButton.onClick.AddListener(Close);
            confirmKeepButton.onClick.AddListener(OnKeepDisplay);
        }

        void Update()
        {
            if (!root.activeSelf)
                return;
            if (confirmPanel.activeSelf)
            {
                TickConfirm();
                return;
            }
            if (RebindController.IsListening)
                return;
            if (!BackPressed())
                return;
            if (controlsPanel.activeSelf)
                CloseControls();
            else
                Close();
        }

        /// <summary>
        /// Opens the panel; selection returns to returnSelection and
        /// onClosed runs when the panel closes.
        /// </summary>
        public void Open(GameObject returnTo, Action closedCallback)
        {
            returnSelection = returnTo;
            onClosed = closedCallback;
            SyncFromSettings();
            root.SetActive(true);
            Select(windowModeCycler.gameObject);
        }

        /// <summary>Closes the panel and flushes prefs to disk.</summary>
        public void Close()
        {
            controlsPanel.SetActive(false);
            confirmPanel.SetActive(false);
            root.SetActive(false);
            GameSettings.Save();
            if (EventSystem.current != null && returnSelection != null)
                EventSystem.current.SetSelectedGameObject(
                    returnSelection);
            onClosed?.Invoke();
            onClosed = null;
        }

        /// <summary>Closes the controls submenu back to the panel.</summary>
        public void CloseControls()
        {
            controlsPanel.SetActive(false);
            Select(controlsButton.gameObject);
        }

        /// <summary>Opens the controls submenu overlay.</summary>
        public void ShowControls()
        {
            controlsPanel.SetActive(true);
        }

        void OnApplyDisplay()
        {
            savedMode = GameSettings.Mode;
            savedWidth = GameSettings.ResolutionWidth;
            savedHeight = GameSettings.ResolutionHeight;
            var (width, height) = SelectedResolution();
            GameSettings.ApplyDisplay(
                (WindowMode)windowModeCycler.Index, width, height);
            confirmTimeLeft = ConfirmSeconds;
            RefreshConfirmText();
            confirmPanel.SetActive(true);
            Select(confirmKeepButton.gameObject);
        }

        void OnKeepDisplay()
        {
            var (width, height) = SelectedResolution();
            GameSettings.SaveDisplay(
                (WindowMode)windowModeCycler.Index, width, height);
            GameSettings.Save();
            confirmPanel.SetActive(false);
            Select(applyButton.gameObject);
        }

        void TickConfirm()
        {
            confirmTimeLeft -= Time.unscaledDeltaTime;
            if (confirmTimeLeft > 0f && !BackPressed())
            {
                RefreshConfirmText();
                return;
            }
            GameSettings.ApplyDisplay(savedMode, savedWidth,
                savedHeight);
            SyncFromSettings();
            confirmPanel.SetActive(false);
            Select(applyButton.gameObject);
        }

        void RefreshConfirmText()
        {
            int seconds = Mathf.CeilToInt(
                Mathf.Max(confirmTimeLeft, 0f));
            confirmText.text =
                $"KEEP  THESE  SETTINGS ?  ( {seconds} )";
        }

        void SyncFromSettings()
        {
            windowModeCycler.SetIndex((int)GameSettings.Mode);
            resolutionCycler.SetIndex(FindResolutionIndex(
                GameSettings.ResolutionWidth,
                GameSettings.ResolutionHeight));
            musicSlider.SetValueWithoutNotify(
                GameSettings.MusicVolume);
            sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
        }

        void BuildResolutionOptions()
        {
            resolutions.Clear();
            resolutions.Add((0, 0));
            var seen = new HashSet<(int, int)>();
            var all = Screen.resolutions;
            for (int i = all.Length - 1; i >= 0; i--)
            {
                var size = (all[i].width, all[i].height);
                if (seen.Add(size))
                    resolutions.Add(size);
            }
            var labels = new string[resolutions.Count];
            labels[0] = "DESKTOP";
            for (int i = 1; i < resolutions.Count; i++)
            {
                labels[i] = $"{resolutions[i].width} X " +
                    $"{resolutions[i].height}";
            }
            resolutionCycler.SetOptions(labels, FindResolutionIndex(
                GameSettings.ResolutionWidth,
                GameSettings.ResolutionHeight));
        }

        int FindResolutionIndex(int width, int height)
        {
            for (int i = 1; i < resolutions.Count; i++)
            {
                if (resolutions[i].width == width
                    && resolutions[i].height == height)
                    return i;
            }
            return 0;
        }

        (int, int) SelectedResolution()
        {
            int i = resolutionCycler.Index;
            return i >= 0 && i < resolutions.Count
                ? resolutions[i] : (0, 0);
        }

        static bool BackPressed()
        {
            return (Keyboard.current != null
                    && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null
                    && Gamepad.current.buttonEast
                        .wasPressedThisFrame);
        }

        static void Select(GameObject target)
        {
            if (EventSystem.current == null)
                return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
