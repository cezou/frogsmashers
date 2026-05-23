using FreeLives;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FrogSmashers.Net
{
    public class LocalInputSource : IInputSource
    {
        const float DeadZone = 0.3f;

        static readonly Key kb1Left  = Key.LeftArrow;
        static readonly Key kb1Right = Key.RightArrow;
        static readonly Key kb1Up    = Key.UpArrow;
        static readonly Key kb1Down  = Key.DownArrow;
        static readonly Key kb1A     = Key.M;
        static readonly Key kb1B     = Key.Comma;
        static readonly Key kb1X     = Key.Period;
        static readonly Key kb1Y     = Key.Slash;
        static readonly Key kb1Start = Key.Enter;

        static readonly Key kb2Left  = Key.A;
        static readonly Key kb2Right = Key.D;
        static readonly Key kb2Up    = Key.W;
        static readonly Key kb2Down  = Key.S;
        static readonly Key kb2A     = Key.T;
        static readonly Key kb2B     = Key.Y;
        static readonly Key kb2X     = Key.U;
        static readonly Key kb2Y     = Key.I;
        static readonly Key kb2Start = Key.Space;

        public void Read(InputReader.Device device, InputState target)
        {
            switch (device)
            {
                case InputReader.Device.Keyboard1:
                    ReadKeyboard(target, kb1Left, kb1Right, kb1Up, kb1Down, kb1A, kb1B, kb1X, kb1Y, kb1Start);
                    break;
                case InputReader.Device.Keyboard2:
                    ReadKeyboard(target, kb2Left, kb2Right, kb2Up, kb2Down, kb2A, kb2B, kb2X, kb2Y, kb2Start);
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

        static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        static void ReadKeyboard(InputState s, Key left, Key right, Key up, Key down,
                                  Key a, Key b, Key x, Key y, Key start)
        {
            s.xAxis = s.yAxis = s.leftTrigger = s.rightTrigger = 0f;
            if (KeyPressed(left))  s.xAxis -= 1f;
            if (KeyPressed(right)) s.xAxis += 1f;
            if (KeyPressed(up))    s.yAxis += 1f;
            if (KeyPressed(down))  s.yAxis -= 1f;

            s.up    = KeyPressed(up);
            s.down  = KeyPressed(down);
            s.left  = KeyPressed(left);
            s.right = KeyPressed(right);

            s.aButton = KeyPressed(a);
            s.bButton = KeyPressed(b);
            s.xButton = KeyPressed(x);
            s.yButton = KeyPressed(y);
            s.start   = KeyPressed(start);
        }

        public static void ReadGamepad(Gamepad device, InputState s)
        {
            if (device == null) return;

            var ls = device.leftStick.ReadValue();
            s.right = ls.x >  DeadZone || device.dpad.right.isPressed;
            s.left  = ls.x < -DeadZone || device.dpad.left.isPressed;
            s.up    = ls.y >  DeadZone || device.dpad.up.isPressed;
            s.down  = ls.y < -DeadZone || device.dpad.down.isPressed;

            s.aButton = device.buttonSouth.isPressed;
            s.bButton = device.buttonEast.isPressed;
            s.xButton = device.buttonWest.isPressed;
            s.yButton = device.buttonNorth.isPressed;

            s.leftTrigger  = device.leftTrigger.ReadValue();
            s.rightTrigger = device.rightTrigger.ReadValue();
            s.start = device.startButton.isPressed;
        }
    }
}
