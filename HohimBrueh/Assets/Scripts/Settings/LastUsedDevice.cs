using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace FrogSmashers.Settings
{
    /// <summary>
    /// Tracks which device produced the last button press so
    /// device-agnostic prompts (title screen, unjoined lobby slots)
    /// can show the right glyph set. Uses the Input System's
    /// onAnyButtonPress stream; before any press it mirrors the
    /// combined-input default (current gamepad if one is connected,
    /// else keyboard).
    /// </summary>
    public static class LastUsedDevice
    {
        static InputDevice device;
        static bool subscribed;

        public static ControlDeviceKind Kind
        {
            get
            {
                EnsureSubscribed();
                if (device != null)
                    return ControlBindingService.KindOf(device);
                if (Gamepad.current != null)
                    return ControlBindingService.KindOf(
                        Gamepad.current);
                return ControlDeviceKind.Keyboard1;
            }
        }

        static void EnsureSubscribed()
        {
            if (subscribed)
                return;
            subscribed = true;
            InputSystem.onAnyButtonPress.Call(
                control => device = control.device);
            InputSystem.onDeviceChange += (changed, change) =>
            {
                if (change == InputDeviceChange.Removed
                    && changed == device)
                    device = null;
            };
        }
    }
}
