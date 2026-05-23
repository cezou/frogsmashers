using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FreeLives
{
    public static class InputReader
    {
        public enum Device
        {
            Keyboard1, Keyboard2, Gamepad1, Gamepad2, Gamepad3, Gamepad4
        }

        // Keyboard 1 (arrows + m/,/./? + Enter)
        static Key kb1Left = Key.LeftArrow;
        static Key kb1Right = Key.RightArrow;
        static Key kb1Up = Key.UpArrow;
        static Key kb1Down = Key.DownArrow;
        static Key kb1A = Key.M;
        static Key kb1B = Key.Comma;
        static Key kb1X = Key.Period;
        static Key kb1Y = Key.Slash;
        static Key kb1Start = Key.Enter;

        // Keyboard 2 (WASD + tyui + Space)
        static Key kb2Left = Key.A;
        static Key kb2Right = Key.D;
        static Key kb2Up = Key.W;
        static Key kb2Down = Key.S;
        static Key kb2A = Key.T;
        static Key kb2B = Key.Y;
        static Key kb2X = Key.U;
        static Key kb2Y = Key.I;
        static Key kb2Start = Key.Space;

        static float deadZone = 0.3f;

        public static void GetInput(InputState inputState)
        {
            if (Gamepad.current != null)
            {
                GetGamepadInput(Gamepad.current, inputState);
            }
            else
            {
                GetKeyboard1Input(inputState);
            }
        }

        public static void GetInput(Device device, InputState inputState)
        {
            CacheLastInput(inputState);

            if (device == Device.Keyboard1)
            {
                GetKeyboard1Input(inputState);
            }
            else if (device == Device.Keyboard2)
            {
                GetKeyboard2Input(inputState);
            }
            else
            {
                int index = GamepadIndex(device);
                if (index >= 0 && index < Gamepad.all.Count)
                    GetGamepadInput(Gamepad.all[index], inputState);
                else
                    ClearInputState(inputState);
            }
        }

        static int GamepadIndex(Device device)
        {
            switch (device)
            {
                case Device.Gamepad1: return 0;
                case Device.Gamepad2: return 1;
                case Device.Gamepad3: return 2;
                case Device.Gamepad4: return 3;
                default: return -1;
            }
        }

        private static void CacheLastInput(InputState inputState)
        {
            inputState.wasAButton = inputState.aButton;
            inputState.wasBButton = inputState.bButton;
            inputState.wasXButton = inputState.xButton;
            inputState.wasYButton = inputState.yButton;
            inputState.wasLeft = inputState.left;
            inputState.wasRight = inputState.right;
            inputState.wasUp = inputState.up;
            inputState.wasDown = inputState.down;
            inputState.wasStart = inputState.start;
        }

        static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        static void GetKeyboard1Input(InputState inputState)
        {
            inputState.xAxis = inputState.yAxis = inputState.leftTrigger = inputState.rightTrigger = 0f;
            if (KeyPressed(kb1Left)) inputState.xAxis -= 1f;
            if (KeyPressed(kb1Right)) inputState.xAxis += 1f;
            if (KeyPressed(kb1Up)) inputState.yAxis += 1f;
            if (KeyPressed(kb1Down)) inputState.yAxis -= 1f;

            inputState.up = KeyPressed(kb1Up);
            inputState.down = KeyPressed(kb1Down);
            inputState.left = KeyPressed(kb1Left);
            inputState.right = KeyPressed(kb1Right);

            inputState.yButton = KeyPressed(kb1Y);
            inputState.xButton = KeyPressed(kb1X);
            inputState.aButton = KeyPressed(kb1A);
            inputState.bButton = KeyPressed(kb1B);
            inputState.start = KeyPressed(kb1Start);
        }

        static void GetKeyboard2Input(InputState inputState)
        {
            inputState.xAxis = inputState.yAxis = inputState.leftTrigger = inputState.rightTrigger = 0f;
            if (KeyPressed(kb2Left)) inputState.xAxis -= 1f;
            if (KeyPressed(kb2Right)) inputState.xAxis += 1f;
            if (KeyPressed(kb2Up)) inputState.yAxis += 1f;
            if (KeyPressed(kb2Down)) inputState.yAxis -= 1f;

            inputState.up = KeyPressed(kb2Up);
            inputState.down = KeyPressed(kb2Down);
            inputState.left = KeyPressed(kb2Left);
            inputState.right = KeyPressed(kb2Right);

            inputState.yButton = KeyPressed(kb2Y);
            inputState.xButton = KeyPressed(kb2X);
            inputState.aButton = KeyPressed(kb2A);
            inputState.bButton = KeyPressed(kb2B);
            inputState.start = KeyPressed(kb2Start);
        }

        // Cross-platform mapping. The InputState "aButton" etc. names are kept
        // for codebase compatibility, but map to physical positions:
        //   aButton = south (Xbox A, PS Cross/X, Nintendo B)
        //   bButton = east  (Xbox B, PS Circle,  Nintendo A)
        //   xButton = west  (Xbox X, PS Square,  Nintendo Y)
        //   yButton = north (Xbox Y, PS Triangle, Nintendo X)
        static void GetGamepadInput(Gamepad device, InputState inputState)
        {
            if (device == null) return;

            Vector2 ls = device.leftStick.ReadValue();
            inputState.right = ls.x > deadZone || device.dpad.right.isPressed;
            inputState.left = ls.x < -deadZone || device.dpad.left.isPressed;
            inputState.up = ls.y > deadZone || device.dpad.up.isPressed;
            inputState.down = ls.y < -deadZone || device.dpad.down.isPressed;

            inputState.aButton = device.buttonSouth.isPressed;
            inputState.bButton = device.buttonEast.isPressed;
            inputState.xButton = device.buttonWest.isPressed;
            inputState.yButton = device.buttonNorth.isPressed;

            inputState.leftTrigger = device.leftTrigger.ReadValue();
            inputState.rightTrigger = device.rightTrigger.ReadValue();
            inputState.start = device.startButton.isPressed;
        }

        public static void ClearInputState(InputState inputState)
        {
            inputState.rightTrigger = inputState.leftTrigger = inputState.xAxis = inputState.yAxis = 0f;
            inputState.left = inputState.right = inputState.up = inputState.down
                = inputState.aButton = inputState.bButton = inputState.xButton = inputState.yButton = false;
        }
    }
}
