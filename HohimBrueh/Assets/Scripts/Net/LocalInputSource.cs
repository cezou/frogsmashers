using FreeLives;
using FrogSmashers.Settings;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FrogSmashers.Net
{
    public class LocalInputSource : IInputSource
    {
        const float DeadZone = 0.3f;

        public void Read(InputReader.Device device, InputState target)
        {
            switch (device)
            {
                case InputReader.Device.Keyboard1:
                    ReadKeyboard(target, 1);
                    break;
                case InputReader.Device.Keyboard2:
                    ReadKeyboard(target, 2);
                    break;
                default:
                    int idx = GamepadIndex(device);
                    if (idx >= 0 && idx < Gamepad.all.Count)
                        ReadGamepad(Gamepad.all[idx], target);
                    else
                        InputReader.ClearInputState(target);
                    break;
            }
        }

        static int GamepadIndex(InputReader.Device d)
        {
            switch (d)
            {
                case InputReader.Device.Gamepad1: return 0;
                case InputReader.Device.Gamepad2: return 1;
                case InputReader.Device.Gamepad3: return 2;
                case InputReader.Device.Gamepad4: return 3;
                default: return -1;
            }
        }

        static bool Pressed(ButtonControl control)
        {
            return control != null && control.isPressed;
        }

        static bool KeyPressed(int kbIndex, SemanticButton button)
        {
            return Pressed(ControlBindingService.ResolveKeyboard(
                kbIndex, button));
        }

        static void ReadKeyboard(InputState s, int kbIndex)
        {
            s.xAxis = s.yAxis = s.leftTrigger = s.rightTrigger = 0f;
            s.up    = KeyPressed(kbIndex, SemanticButton.Up);
            s.down  = KeyPressed(kbIndex, SemanticButton.Down);
            s.left  = KeyPressed(kbIndex, SemanticButton.Left);
            s.right = KeyPressed(kbIndex, SemanticButton.Right);
            if (s.left)  s.xAxis -= 1f;
            if (s.right) s.xAxis += 1f;
            if (s.up)    s.yAxis += 1f;
            if (s.down)  s.yAxis -= 1f;

            s.aButton = KeyPressed(kbIndex, SemanticButton.A);
            s.bButton = KeyPressed(kbIndex, SemanticButton.B);
            s.xButton = KeyPressed(kbIndex, SemanticButton.X);
            s.yButton = KeyPressed(kbIndex, SemanticButton.Y);
            s.start   = KeyPressed(kbIndex, SemanticButton.Start);
        }

        public static void ReadGamepad(Gamepad device, InputState s)
        {
            if (device == null) return;

            var ls = device.leftStick.ReadValue();
            s.right = ls.x >  DeadZone || device.dpad.right.isPressed;
            s.left  = ls.x < -DeadZone || device.dpad.left.isPressed;
            s.up    = ls.y >  DeadZone || device.dpad.up.isPressed;
            s.down  = ls.y < -DeadZone || device.dpad.down.isPressed;

            s.aButton = Pressed(ControlBindingService.ResolveGamepad(
                device, SemanticButton.A));
            s.bButton = Pressed(ControlBindingService.ResolveGamepad(
                device, SemanticButton.B));
            s.xButton = Pressed(ControlBindingService.ResolveGamepad(
                device, SemanticButton.X));
            s.yButton = Pressed(ControlBindingService.ResolveGamepad(
                device, SemanticButton.Y));

            s.leftTrigger  = device.leftTrigger.ReadValue();
            s.rightTrigger = device.rightTrigger.ReadValue();
            s.start = Pressed(ControlBindingService.ResolveGamepad(
                device, SemanticButton.Start));
        }
    }
}
