using FreeLives;
using FrogSmashers.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FrogSmashers.UI
{
    /// <summary>
    /// Swaps an Image or SpriteRenderer to the input-prompt sprite
    /// matching the device actually in use and the effective
    /// (possibly rebound) binding. Lobby hint lines resolve their
    /// JoinCanvas slot's device; device-agnostic prompts (title
    /// screen, unjoined JOIN line) follow the last used device.
    /// Bindings and devices are re-checked on a short poll so
    /// rebinds and hot-plugs self-heal. When a keyboard key has no
    /// dedicated sprite the blank keycap plus the optional label is
    /// shown instead (label text = layout-aware key name).
    /// </summary>
    public class ControlPromptIcon : MonoBehaviour
    {
        /// <summary>Semantic actions a prompt can display. The
        /// first nine mirror SemanticButton; Select is the lobby
        /// mode toggle (pad select / keyboard Tab), deliberately
        /// not rebindable to match the online lobby.</summary>
        public enum PromptAction
        {
            Left, Right, Up, Down, A, B, X, Y, Start, Select
        }

        public enum DeviceSource
        {
            JoinSlot, LastUsed
        }

        const float PollInterval = 0.25f;

        public PromptAction action;
        public DeviceSource source;
        public Text fallbackLabel;

        /// <summary>The keyboard shows the shade hint as a centered
        /// key PAIR while pads collapse it into ONE d-pad glyph;
        /// this x offset moves that single glyph from the pair's
        /// left slot onto the icon column.</summary>
        public float padCenterOffsetX;

        Image image;
        SpriteRenderer spriteRenderer;
        JoinCanvas joinCanvas;
        Sprite originalSprite;
        Vector2 originalPosition;
        string currentName;
        float nextPoll;

        void Awake()
        {
            image = GetComponent<Image>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (image != null)
            {
                image.preserveAspect = true;
                originalSprite = image.sprite;
                originalPosition = ((RectTransform)transform)
                    .anchoredPosition;
            }
            else if (spriteRenderer != null)
            {
                originalSprite = spriteRenderer.sprite;
            }
            if (source == DeviceSource.JoinSlot)
                joinCanvas = GetComponentInParent<JoinCanvas>();
        }

        void OnEnable()
        {
            currentName = null;
            nextPoll = 0f;
            Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < nextPoll)
                return;
            nextPoll = Time.unscaledTime + PollInterval;
            Refresh();
        }

        /// <summary>
        /// Re-resolves the sprite. Pad movement collapses the lobby's
        /// left/right pair into the single two-arm d-pad glyph (shown
        /// on the Left icon, recentered; the Right one hides; Up/Down
        /// keep the prefab art). Everything else resolves through the
        /// binding query, then falls back generic pad set, labeled
        /// blank keycap, and finally the prefab's original art.
        /// </summary>
        void Refresh()
        {
            var kind = ResolveKind();
            bool isPad = kind == ControlDeviceKind.Xbox
                || kind == ControlDeviceKind.PlayStation
                || kind == ControlDeviceKind.GenericPad;
            if (isPad && action <= PromptAction.Down)
            {
                switch (action)
                {
                    case PromptAction.Left:
                        if (currentName == "pad_dpadHorizontal")
                            return;
                        currentName = "pad_dpadHorizontal";
                        SetOffsetX(padCenterOffsetX);
                        if (ControlPromptSprites.TryGet(currentName,
                            out var dpadSprite))
                            Show(dpadSprite, null);
                        else
                            Show(originalSprite, null);
                        return;
                    case PromptAction.Right:
                        currentName = "hidden";
                        Show(null, null);
                        return;
                    default:
                        currentName = "original";
                        Show(originalSprite, null);
                        return;
                }
            }
            SetOffsetX(0f);
            string name = SpriteNameFor(kind, action);
            if (name == currentName)
                return;
            currentName = name;

            if (ControlPromptSprites.TryGet(name, out var sprite))
            {
                Show(sprite, null);
                return;
            }
            if (isPad)
            {
                string generic = "pad_" + (name == null ? ""
                    : name.Substring(name.IndexOf('_') + 1));
                if (ControlPromptSprites.TryGet(generic, out sprite))
                {
                    Show(sprite, null);
                    return;
                }
                Show(originalSprite, null);
                return;
            }
            string label = ControlBindingService.DisplayNameFor(kind,
                (SemanticButton)action).ToUpper();
            if (ControlPromptSprites.TryGet("kb_blank", out sprite))
                Show(sprite, label);
            else
                Show(originalSprite, null);
        }

        void SetOffsetX(float dx)
        {
            if (padCenterOffsetX == 0f || image == null)
                return;
            ((RectTransform)transform).anchoredPosition =
                originalPosition + new Vector2(dx, 0f);
        }

        void Show(Sprite sprite, string label)
        {
            if (image != null)
            {
                image.sprite = sprite;
                image.enabled = sprite != null;
            }
            if (spriteRenderer != null)
                spriteRenderer.sprite = sprite;
            if (fallbackLabel != null)
            {
                fallbackLabel.text = label ?? "";
                fallbackLabel.enabled = label != null;
            }
        }

        ControlDeviceKind ResolveKind()
        {
            if (source == DeviceSource.JoinSlot
                && joinCanvas != null
                && joinCanvas.assignedPlayer != null)
                return KindOfSlot(
                    joinCanvas.assignedPlayer.inputDevice);
            return LastUsedDevice.Kind;
        }

        /// <summary>Binding-set owner of a fixed player slot.</summary>
        public static ControlDeviceKind KindOfSlot(
            InputReader.Device device)
        {
            switch (device)
            {
                case InputReader.Device.Keyboard1:
                    return ControlDeviceKind.Keyboard1;
                default:
                    int idx = (int)device
                        - (int)InputReader.Device.Gamepad1;
                    if (idx >= 0 && idx < Gamepad.all.Count)
                        return ControlBindingService.KindOf(
                            Gamepad.all[idx]);
                    return ControlDeviceKind.Xbox;
            }
        }

        /// <summary>
        /// Atlas sprite name of an action's effective binding for a
        /// binding-set owner, e.g. (Xbox, A) → "xbox_buttonSouth",
        /// (Keyboard1, X) → "kb_u". Select is fixed: pad select
        /// button or keyboard Tab.
        /// </summary>
        public static string SpriteNameFor(ControlDeviceKind kind,
            PromptAction action)
        {
            string prefix = ControlPromptSprites.PrefixFor(kind);
            if (action == PromptAction.Select)
            {
                return prefix == "kb" ? "kb_tab"
                    : prefix + "_select";
            }
            string path = ControlBindingService.ControlPathFor(kind,
                (SemanticButton)action);
            if (string.IsNullOrEmpty(path))
                return null;
            return prefix + "_" + path;
        }
    }
}
