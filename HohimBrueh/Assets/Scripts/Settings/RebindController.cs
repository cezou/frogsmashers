using System;
using UnityEngine.InputSystem;

namespace FrogSmashers.Settings
{
    /// <summary>Outcome of an interactive rebind attempt.</summary>
    public enum RebindResult
    {
        Success, Cancelled
    }

    /// <summary>
    /// Runs the official interactive rebind flow on the FrogControls
    /// asset. Capture is restricted to the device family of the
    /// binding set being edited, cancels on Escape, times out after
    /// five seconds. Duplicates within one set swap with the previous
    /// binding (no action is ever left unbound).
    /// </summary>
    public static class RebindController
    {
        const float TimeoutSeconds = 5f;

        static InputActionRebindingExtensions.RebindingOperation
            operation;

        /// <summary>True while a capture is listening.</summary>
        public static bool IsListening { get; private set; }

        /// <summary>
        /// Starts listening for a new binding of the given row.
        /// Invokes onDone exactly once with the outcome.
        /// </summary>
        public static void StartRebind(ControlDeviceKind kind,
            SemanticButton button, Action<RebindResult> onDone)
        {
            if (IsListening)
                return;
            var action = ControlBindingService.FindBinding(kind,
                button, out int index);
            if (action == null || index < 0)
            {
                onDone?.Invoke(RebindResult.Cancelled);
                return;
            }

            string oldPath = action.bindings[index].effectivePath;
            IsListening = true;
            operation = action.PerformInteractiveRebinding(index)
                .WithControlsHavingToMatchPath(DevicePath(kind))
                .WithControlsExcluding("<Pointer>")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Keyboard>/escape")
                .WithControlsExcluding("<Gamepad>/leftStick")
                .WithControlsExcluding("<Gamepad>/rightStick")
                .WithControlsExcluding("<Gamepad>/dpad")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithTimeout(TimeoutSeconds)
                .OnComplete(op => Finish(kind, button, action, index,
                    oldPath, onDone))
                .OnCancel(op => Cancel(onDone))
                .Start();
        }

        /// <summary>Aborts a capture in progress, if any.</summary>
        public static void CancelPending()
        {
            operation?.Cancel();
        }

        static void Finish(ControlDeviceKind kind,
            SemanticButton button, InputAction action, int index,
            string oldPath, Action<RebindResult> onDone)
        {
            Dispose();
            string newPath = action.bindings[index].effectivePath;
            SwapSameSetDuplicate(kind, button, newPath, oldPath);
            ControlBindingService.SaveOverrides();
            ControlBindingService.InvalidateCache();
            onDone?.Invoke(RebindResult.Success);
        }

        static void Cancel(Action<RebindResult> onDone)
        {
            Dispose();
            ControlBindingService.InvalidateCache();
            onDone?.Invoke(RebindResult.Cancelled);
        }

        static void Dispose()
        {
            operation?.Dispose();
            operation = null;
            IsListening = false;
        }

        static void SwapSameSetDuplicate(ControlDeviceKind kind,
            SemanticButton button, string newPath, string oldPath)
        {
            foreach (SemanticButton b in
                Enum.GetValues(typeof(SemanticButton)))
            {
                if (b == button)
                    continue;
                var action = ControlBindingService.FindBinding(kind,
                    b, out int index);
                if (action == null || index < 0)
                    continue;
                if (SamePath(action.bindings[index].effectivePath,
                    newPath))
                {
                    action.ApplyBindingOverride(index, oldPath);
                    return;
                }
            }
        }

        static bool SamePath(string a, string b)
        {
            return !string.IsNullOrEmpty(a)
                && string.Equals(a, b,
                    StringComparison.OrdinalIgnoreCase);
        }

        static string DevicePath(ControlDeviceKind kind)
        {
            switch (kind)
            {
                case ControlDeviceKind.Xbox:
                    return "<XInputController>";
                case ControlDeviceKind.PlayStation:
                    return "<DualShockGamepad>";
                case ControlDeviceKind.GenericPad:
                    return "<Gamepad>";
                default:
                    return "<Keyboard>";
            }
        }
    }
}
