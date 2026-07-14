using System.Collections.Generic;
using FrogSmashers.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace FrogSmashers.UI
{
    /// <summary>
    /// JOIN line glyphs of the local lobby: one prompt sprite per
    /// device kind that is connected AND still has a free slot
    /// (Xbox pad, PS pad, generic pad, keyboard) — a single
    /// last-used-device glyph would lie once that device joined.
    /// Polls like ControlPromptIcon; unused icon slots stay hidden,
    /// and with no joinable device left only the text remains.
    /// Inert online (GameController deactivates the join canvases).
    /// </summary>
    public class JoinPromptIcons : MonoBehaviour
    {
        const float PollInterval = 0.25f;

        public Image[] icons;

        readonly List<ControlDeviceKind> kinds =
            new List<ControlDeviceKind>();

        float nextPoll;

        void OnEnable()
        {
            nextPoll = 0f;
        }

        void Update()
        {
            if (Time.unscaledTime < nextPoll)
                return;
            nextPoll = Time.unscaledTime + PollInterval;
            Refresh();
        }

        void Refresh()
        {
            if (icons == null)
                return;
            GameController.GetAvailableJoinKinds(kinds);
            int shown = 0;
            foreach (var kind in kinds)
            {
                if (shown >= icons.Length)
                    break;
                string name = ControlPromptIcon.SpriteNameFor(kind,
                    ControlPromptIcon.PromptAction.X);
                if (!ControlPromptSprites.TryGet(name, out var sprite))
                    continue;
                var icon = icons[shown];
                if (icon == null)
                    continue;
                icon.sprite = sprite;
                icon.enabled = true;
                shown++;
            }
            for (int i = shown; i < icons.Length; i++)
            {
                if (icons[i] != null)
                    icons[i].enabled = false;
            }
        }
    }
}
