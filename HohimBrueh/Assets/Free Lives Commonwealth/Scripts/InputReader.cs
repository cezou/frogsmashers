using FrogSmashers.Net;
using UnityEngine.InputSystem;

namespace FreeLives
{
    public static class InputReader
    {
        public enum Device
        {
            Keyboard1, Keyboard2, Gamepad1, Gamepad2, Gamepad3, Gamepad4
        }

        public static IInputSource ActiveSource = new LocalInputSource();

        public static void GetInput(InputState inputState)
        {
            CacheLastInput(inputState);
            if (Gamepad.current != null)
                LocalInputSource.ReadGamepad(Gamepad.current, inputState);
            else
                ActiveSource.Read(Device.Keyboard1, inputState);
        }

        public static void GetInput(Device device, InputState inputState)
        {
            CacheLastInput(inputState);
            ActiveSource.Read(device, inputState);
        }

        static void CacheLastInput(InputState inputState)
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

        public static void ClearInputState(InputState inputState)
        {
            inputState.rightTrigger = inputState.leftTrigger = inputState.xAxis = inputState.yAxis = 0f;
            inputState.left = inputState.right = inputState.up = inputState.down
                = inputState.aButton = inputState.bButton = inputState.xButton = inputState.yButton = false;
        }
    }
}
