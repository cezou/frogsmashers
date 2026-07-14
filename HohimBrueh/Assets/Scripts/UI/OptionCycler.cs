using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FrogSmashers.UI
{
    /// <summary>
    /// Gamepad-friendly replacement for a dropdown: a Selectable
    /// whose left/right cycles through a fixed option list (vertical
    /// navigation passes through). Submit steps forward so mouse
    /// clicks work too.
    /// </summary>
    public class OptionCycler : Selectable, ISubmitHandler
    {
        public Text valueText;

        string[] options = Array.Empty<string>();
        int index;

        /// <summary>Raised with the new index after each step.</summary>
        public event Action<int> Changed;

        /// <summary>Currently selected option index.</summary>
        public int Index => index;

        /// <summary>
        /// Replaces the option list and shows startIndex without
        /// raising Changed.
        /// </summary>
        public void SetOptions(string[] newOptions, int startIndex)
        {
            options = newOptions ?? Array.Empty<string>();
            index = options.Length == 0 ? 0
                : Mathf.Clamp(startIndex, 0, options.Length - 1);
            RefreshLabel();
        }

        /// <summary>Shows the given index without raising Changed.</summary>
        public void SetIndex(int newIndex)
        {
            if (options.Length == 0)
                return;
            index = Mathf.Clamp(newIndex, 0, options.Length - 1);
            RefreshLabel();
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left)
            {
                Step(-1);
                return;
            }
            if (eventData.moveDir == MoveDirection.Right)
            {
                Step(1);
                return;
            }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            Step(1);
        }

        void Step(int delta)
        {
            if (options.Length == 0 || !IsInteractable())
                return;
            index = (index + delta + options.Length) % options.Length;
            RefreshLabel();
            Changed?.Invoke(index);
        }

        void RefreshLabel()
        {
            if (valueText == null)
                return;
            valueText.text = options.Length == 0 ? ""
                : $"<  {options[index]}  >";
        }
    }
}
