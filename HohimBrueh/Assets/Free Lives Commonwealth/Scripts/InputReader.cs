using FrogSmashers.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeLives
{
    public static class InputReader
    {
        public enum Device
        {
            Keyboard1, Gamepad1, Gamepad2, Gamepad3, Gamepad4
        }

        public static IInputSource ActiveSource = new LocalInputSource();

        static readonly InputState scratch = new InputState();

        /// <summary>
        /// Combined "any device" input for menu-style screens: the
        /// current gamepad and the keyboard are merged so e.g. the
        /// title screen reacts to keyboard START even while a
        /// gamepad is connected.
        /// </summary>
        public static void GetInput(InputState inputState)
        {
            CacheLastInput(inputState);
            ClearInputState(inputState);
            inputState.start = false;
            if (Gamepad.current != null)
            {
                LocalInputSource.ReadGamepad(Gamepad.current, scratch);
                Merge(inputState, scratch);
            }
            ActiveSource.Read(Device.Keyboard1, scratch);
            Merge(inputState, scratch);
        }

        static void Merge(InputState target, InputState other)
        {
            target.left |= other.left;
            target.right |= other.right;
            target.up |= other.up;
            target.down |= other.down;
            target.aButton |= other.aButton;
            target.bButton |= other.bButton;
            target.xButton |= other.xButton;
            target.yButton |= other.yButton;
            target.start |= other.start;
            if (Mathf.Abs(other.xAxis) > Mathf.Abs(target.xAxis))
                target.xAxis = other.xAxis;
            if (Mathf.Abs(other.yAxis) > Mathf.Abs(target.yAxis))
                target.yAxis = other.yAxis;
            if (other.leftTrigger > target.leftTrigger)
                target.leftTrigger = other.leftTrigger;
            if (other.rightTrigger > target.rightTrigger)
                target.rightTrigger = other.rightTrigger;
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
