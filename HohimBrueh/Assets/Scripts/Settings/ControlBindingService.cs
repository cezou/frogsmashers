using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.XInput;

namespace FrogSmashers.Settings
{
    /// <summary>Semantic gameplay buttons filling InputState bits.</summary>
    public enum SemanticButton
    {
        Left, Right, Up, Down, A, B, X, Y, Start
    }

    /// <summary>Binding set owners shown in the controls menu.
    /// A single keyboard set: Unity's Input System exposes one
    /// merged Keyboard device, so extra sets can't map to distinct
    /// physical keyboards anyway. (The internal map/group name stays
    /// "Keyboard1" to keep saved binding overrides valid.)</summary>
    public enum ControlDeviceKind
    {
        Keyboard1, Xbox, PlayStation, GenericPad
    }

    /// <summary>
    /// Runtime facade over the FrogControls InputActionAsset: loads
    /// it from Resources, restores saved binding overrides, resolves
    /// each semantic button to the concrete ButtonControl of a given
    /// device (cached; string path matching is too slow per frame),
    /// and exposes the binding-query API used by control prompts
    /// (issue #4). Actions are never enabled: gameplay keeps polling
    /// through LocalInputSource so rollback determinism is untouched.
    /// </summary>
    public static class ControlBindingService
    {
        const string PrefsKey = "FrogSmashers.BindingOverrides.v1";
        const string AssetName = "FrogControls";

        static readonly string[] DefaultKb1 =
        {
            "a", "d", "w", "s", "space", "t", "y", "u", "tab"
        };

        static readonly Dictionary<(int, SemanticButton),
            ButtonControl> cache =
                new Dictionary<(int, SemanticButton),
                    ButtonControl>();

        static InputActionAsset asset;
        static bool loaded;

        /// <summary>The loaded asset; null when Resources miss.</summary>
        public static InputActionAsset Asset
        {
            get { EnsureLoaded(); return asset; }
        }

        /// <summary>
        /// Resolves a semantic button to the bound key control on
        /// the current keyboard; null when no keyboard is present.
        /// </summary>
        public static ButtonControl ResolveKeyboard(
            SemanticButton button)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return null;
            var key = (keyboard.deviceId, button);
            if (cache.TryGetValue(key, out var cached))
                return cached;
            var control = FindKeyboardControl(keyboard, button);
            cache[key] = control;
            return control;
        }

        /// <summary>
        /// Resolves a semantic face/start button to the bound control
        /// on the given pad, using the binding group of the pad's
        /// brand (Xbox, PlayStation or Generic).
        /// </summary>
        public static ButtonControl ResolveGamepad(Gamepad pad,
            SemanticButton button)
        {
            if (pad == null)
                return null;
            var key = (pad.deviceId, button);
            if (cache.TryGetValue(key, out var cached))
                return cached;
            var control = FindGamepadControl(pad, button);
            cache[key] = control;
            return control;
        }

        /// <summary>Classifies a device into a binding-set owner.</summary>
        public static ControlDeviceKind KindOf(InputDevice device)
        {
            if (device is DualShockGamepad)
                return ControlDeviceKind.PlayStation;
            if (device is XInputController)
                return ControlDeviceKind.Xbox;
            if (device is Gamepad)
                return ControlDeviceKind.GenericPad;
            return ControlDeviceKind.Keyboard1;
        }

        /// <summary>
        /// Stable glyph identifier for the effective binding: the
        /// last control path component, e.g. "buttonSouth", "m",
        /// "leftArrow". Movement on gamepads is not rebindable and
        /// reports "leftStick". Used by control prompt sprites
        /// (issue #4).
        /// </summary>
        public static string ControlPathFor(ControlDeviceKind kind,
            SemanticButton button)
        {
            if (IsPad(kind) && IsMovement(button))
                return "leftStick";
            string path = EffectivePathFor(kind, button);
            if (string.IsNullOrEmpty(path))
                return null;
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        /// <summary>
        /// Human-readable name for the effective binding ("A",
        /// "Cross", layout-aware key names on keyboards). Used by
        /// the controls menu and by prompt fallbacks (issue #4).
        /// </summary>
        public static string DisplayNameFor(ControlDeviceKind kind,
            SemanticButton button)
        {
            if (IsPad(kind) && IsMovement(button))
                return "LEFT STICK / DPAD";
            string path = EffectivePathFor(kind, button);
            if (string.IsNullOrEmpty(path))
                return "?";
            if (!IsPad(kind) && Keyboard.current != null)
            {
                var control = InputControlPath.TryFindControl(
                    Keyboard.current, path);
                if (control != null
                    && !string.IsNullOrEmpty(control.displayName))
                    return control.displayName;
            }
            if (IsPad(kind))
            {
                string brandName = PadDisplayName(kind, path);
                if (!string.IsNullOrEmpty(brandName))
                    return brandName;
            }
            return InputControlPath.ToHumanReadableString(path,
                InputControlPath.HumanReadableStringOptions
                    .OmitDevice);
        }

        static string PadDisplayName(ControlDeviceKind kind,
            string path)
        {
            foreach (var pad in Gamepad.all)
            {
                if (KindOf(pad) != kind)
                    continue;
                var control = InputControlPath.TryFindControl(pad,
                    path);
                if (control != null
                    && !string.IsNullOrEmpty(control.displayName))
                    return control.displayName;
            }
            var layout = InputSystem.LoadLayout(LayoutName(kind));
            if (layout == null)
                return null;
            int slash = path.LastIndexOf('/');
            var name = new UnityEngine.InputSystem.Utilities
                .InternedString(
                    slash >= 0 ? path.Substring(slash + 1) : path);
            var item = layout.FindControl(name);
            return item?.displayName;
        }

        static string LayoutName(ControlDeviceKind kind)
        {
            switch (kind)
            {
                case ControlDeviceKind.Xbox:
                    return "XInputController";
                case ControlDeviceKind.PlayStation:
                    return "DualShockGamepad";
                default: return "Gamepad";
            }
        }

        /// <summary>
        /// Finds the action and binding index behind a binding-set
        /// row; bindingIndex is -1 when unavailable. Entry point for
        /// the interactive rebind flow.
        /// </summary>
        public static InputAction FindBinding(ControlDeviceKind kind,
            SemanticButton button, out int bindingIndex)
        {
            bindingIndex = -1;
            EnsureLoaded();
            if (asset == null)
                return null;
            var map = asset.FindActionMap(MapName(kind));
            var action = map?.FindAction(button.ToString());
            if (action == null)
                return null;
            string group = GroupName(kind);
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].groups != null
                    && bindings[i].groups.Contains(group))
                {
                    bindingIndex = i;
                    return action;
                }
            }
            return null;
        }

        /// <summary>Persists all binding overrides to PlayerPrefs.</summary>
        public static void SaveOverrides()
        {
            EnsureLoaded();
            if (asset == null)
                return;
            PlayerPrefs.SetString(PrefsKey,
                asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        /// <summary>Clears every override and the saved prefs.</summary>
        public static void ResetAll()
        {
            EnsureLoaded();
            if (asset != null)
                asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            InvalidateCache();
        }

        /// <summary>
        /// Drops cached control resolutions; must run after any
        /// rebind, reset or device change.
        /// </summary>
        public static void InvalidateCache()
        {
            cache.Clear();
        }

        static void EnsureLoaded()
        {
            if (loaded)
                return;
            loaded = true;
            asset = Resources.Load<InputActionAsset>(AssetName);
            if (asset == null)
            {
                Debug.LogWarning(
                    "[ControlBindingService] FrogControls asset " +
                    "missing; using default bindings.");
                return;
            }
            try
            {
                string overrides = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(overrides))
                    asset.LoadBindingOverridesFromJson(overrides);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[ControlBindingService] Bad overrides: {e}");
            }
            InputSystem.onDeviceChange += (device, change) =>
                InvalidateCache();
        }

        static ButtonControl FindKeyboardControl(Keyboard keyboard,
            SemanticButton button)
        {
            string path = EffectivePathFor(
                ControlDeviceKind.Keyboard1, button);
            if (string.IsNullOrEmpty(path))
                path = $"<Keyboard>/{DefaultKb1[(int)button]}";
            return InputControlPath.TryFindControl(keyboard, path)
                as ButtonControl;
        }

        static ButtonControl FindGamepadControl(Gamepad pad,
            SemanticButton button)
        {
            string path = EffectivePathFor(KindOf(pad), button);
            if (!string.IsNullOrEmpty(path))
            {
                var control = InputControlPath.TryFindControl(pad,
                    path) as ButtonControl;
                if (control != null)
                    return control;
            }
            return DefaultPadControl(pad, button);
        }

        static ButtonControl DefaultPadControl(Gamepad pad,
            SemanticButton button)
        {
            switch (button)
            {
                case SemanticButton.A: return pad.buttonSouth;
                case SemanticButton.B: return pad.buttonEast;
                case SemanticButton.X: return pad.buttonWest;
                case SemanticButton.Y: return pad.buttonNorth;
                case SemanticButton.Start: return pad.startButton;
                default: return null;
            }
        }

        static string EffectivePathFor(ControlDeviceKind kind,
            SemanticButton button)
        {
            var action = FindBinding(kind, button, out int index);
            if (action == null || index < 0)
                return null;
            return action.bindings[index].effectivePath;
        }

        static string MapName(ControlDeviceKind kind)
        {
            return kind == ControlDeviceKind.Keyboard1 ? "Keyboard1"
                : "Gamepad";
        }

        /// <summary>Binding group of a binding-set owner.</summary>
        public static string GroupName(ControlDeviceKind kind)
        {
            switch (kind)
            {
                case ControlDeviceKind.Keyboard1: return "Keyboard1";
                case ControlDeviceKind.Xbox: return "Xbox";
                case ControlDeviceKind.PlayStation:
                    return "PlayStation";
                default: return "Generic";
            }
        }

        static bool IsPad(ControlDeviceKind kind)
        {
            return kind == ControlDeviceKind.Xbox
                || kind == ControlDeviceKind.PlayStation
                || kind == ControlDeviceKind.GenericPad;
        }

        static bool IsMovement(SemanticButton button)
        {
            return button == SemanticButton.Left
                || button == SemanticButton.Right
                || button == SemanticButton.Up
                || button == SemanticButton.Down;
        }
    }
}
