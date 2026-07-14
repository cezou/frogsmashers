using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PromptAction = FrogSmashers.UI.ControlPromptIcon.PromptAction;

namespace FrogSmashers.Settings
{
    /// <summary>
    /// Controls submenu of the settings panel: one tab per binding
    /// set (Keyboard, Xbox, PlayStation, plus Other Gamepad when
    /// such a pad is connected), one rebindable row per semantic
    /// button. Rows are built at runtime so labels always reflect
    /// the effective bindings.
    /// </summary>
    public class ControlsMenu : MonoBehaviour
    {
        static readonly Color TextNormal =
            new Color(160f / 255f, 217f / 255f, 236f / 255f, 1f);
        static readonly Color TextSelected =
            new Color(170f / 255f, 176f / 255f, 88f / 255f, 1f);
        static readonly Color TextPressed =
            new Color(120f / 255f, 130f / 255f, 60f / 255f, 1f);
        static readonly Color TextDisabled =
            new Color(100f / 255f, 135f / 255f, 145f / 255f, 1f);

        static readonly SemanticButton[] KeyboardRows =
        {
            SemanticButton.Left, SemanticButton.Right,
            SemanticButton.Up, SemanticButton.Down,
            SemanticButton.A, SemanticButton.B,
            SemanticButton.X, SemanticButton.Start
        };

        static readonly SemanticButton[] PadRows =
        {
            SemanticButton.A, SemanticButton.B,
            SemanticButton.X, SemanticButton.Start
        };

        public Font font;
        public RectTransform tabBar;
        public RectTransform rowsRoot;
        public Button resetButton;
        public Button backButton;
        public Text statusText;
        public FrogSmashers.UI.SettingsPanelController owner;

        readonly List<(ControlDeviceKind kind, Button button)> tabs =
            new List<(ControlDeviceKind, Button)>();
        readonly List<(SemanticButton button, Button widget,
            Text value)> rows =
                new List<(SemanticButton, Button, Text)>();
        readonly List<GameObject> rowShells = new List<GameObject>();
        readonly List<(PromptAction action, Image icon)> rowIcons =
            new List<(PromptAction, Image)>();

        ControlDeviceKind currentTab = ControlDeviceKind.Keyboard1;
        float statusTimeLeft;
        bool built;

        void Start()
        {
            resetButton.onClick.AddListener(OnReset);
            if (owner != null)
                backButton.onClick.AddListener(owner.CloseControls);
        }

        void OnEnable()
        {
            if (!built)
            {
                built = true;
                BuildTabs();
                BuildRows();
            }
            RefreshRows();
            SelectFirstTab();
        }

        void Update()
        {
            if (statusTimeLeft <= 0f || statusText == null)
                return;
            statusTimeLeft -= Time.unscaledDeltaTime;
            if (statusTimeLeft <= 0f)
                statusText.text = "";
        }

        void BuildTabs()
        {
            var kinds = new List<ControlDeviceKind>
            {
                ControlDeviceKind.Keyboard1,
                ControlDeviceKind.Xbox,
                ControlDeviceKind.PlayStation
            };
            if (GenericPadConnected())
                kinds.Add(ControlDeviceKind.GenericPad);

            float width = 300f;
            float startX = -(kinds.Count - 1) * width * 0.5f;
            for (int i = 0; i < kinds.Count; i++)
            {
                var kind = kinds[i];
                var button = MakeTextButton(tabBar, TabLabel(kind),
                    36, new Vector2(startX + i * width, 0f),
                    new Vector2(width - 10f, 56f));
                button.onClick.AddListener(() => SetTab(kind));
                tabs.Add((kind, button));
            }
        }

        void BuildRows()
        {
            foreach (var shell in rowShells)
                Destroy(shell);
            rowShells.Clear();
            rows.Clear();
            rowIcons.Clear();

            var buttons = IsPadTab() ? PadRows : KeyboardRows;
            float top = 210f;
            float spacing = 58f;
            int index = 0;
            if (IsPadTab())
            {
                var moveShell = MakeRowShell("MOVE",
                    "LEFT STICK / DPAD", top);
                rowIcons.Add(((PromptAction)SemanticButton.Left,
                    MakeRowIcon(moveShell)));
                index = 1;
            }
            foreach (var button in buttons)
            {
                float y = top - (index + 0) * spacing;
                var (widget, value) = MakeRow(RowLabel(button), y);
                var captured = button;
                widget.onClick.AddListener(
                    () => StartRebind(captured));
                rows.Add((captured, widget, value));
                rowIcons.Add(((PromptAction)captured, MakeRowIcon(
                    (RectTransform)widget.transform.parent)));
                index++;
            }
            var modeShell = MakeRowShell("CHANGE MODE",
                ChangeModeValue(), top - index * spacing);
            rowIcons.Add((PromptAction.Select, MakeRowIcon(modeShell)));
            WireNavigation();
        }

        /// <summary>Fixed value of the lobby-only CHANGE MODE row,
        /// deliberately not rebindable (pad select / keyboard Tab,
        /// mirroring the online lobby).</summary>
        string ChangeModeValue()
        {
            switch (currentTab)
            {
                case ControlDeviceKind.Keyboard1:
                    return "TAB";
                case ControlDeviceKind.PlayStation:
                    return "SHARE";
                default:
                    return "SELECT";
            }
        }

        /// <summary>Refreshes value labels and their prompt sprites;
        /// exotic keys without a sprite keep the text only.</summary>
        void RefreshRows()
        {
            foreach (var (button, _, value) in rows)
            {
                value.text = ControlBindingService.DisplayNameFor(
                    currentTab, button).ToUpper();
            }
            foreach (var (action, icon) in rowIcons)
            {
                string name = FrogSmashers.UI.ControlPromptIcon
                    .SpriteNameFor(currentTab, action);
                bool found = FrogSmashers.UI.ControlPromptSprites
                    .TryGet(name, out var sprite);
                icon.sprite = sprite;
                icon.enabled = found;
            }
        }

        void SetTab(ControlDeviceKind kind)
        {
            if (RebindController.IsListening)
                return;
            currentTab = kind;
            BuildRows();
            RefreshRows();
        }

        void StartRebind(SemanticButton button)
        {
            if (RebindController.IsListening)
                return;
            foreach (var (b, _, value) in rows)
            {
                if (b == button)
                    value.text = "PRESS  A  KEY . . .";
            }
            foreach (var (action, icon) in rowIcons)
            {
                if (action == (PromptAction)button)
                    icon.enabled = false;
            }
            if (EventSystem.current != null)
                EventSystem.current.sendNavigationEvents = false;
            RebindController.StartRebind(currentTab, button,
                result => OnRebindDone(result));
        }

        void OnRebindDone(RebindResult result)
        {
            RefreshRows();
            StartCoroutine(ReenableNavigation());
        }

        IEnumerator ReenableNavigation()
        {
            yield return null;
            if (EventSystem.current != null)
                EventSystem.current.sendNavigationEvents = true;
        }

        void OnReset()
        {
            ControlBindingService.ResetAll();
            RefreshRows();
            ShowStatus("CONTROLS  RESET  TO  DEFAULTS");
        }

        void ShowStatus(string message)
        {
            if (statusText == null)
                return;
            statusText.text = message;
            statusTimeLeft = 2f;
        }

        void SelectFirstTab()
        {
            if (tabs.Count > 0 && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(
                    tabs[0].button.gameObject);
            }
        }

        void WireNavigation()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = tabs[Wrap(i - 1, tabs.Count)]
                        .button,
                    selectOnRight = tabs[Wrap(i + 1, tabs.Count)]
                        .button,
                    selectOnDown = rows.Count > 0
                        ? rows[0].widget : (Selectable)resetButton
                };
                tabs[i].button.navigation = nav;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? rows[i - 1].widget
                        : (Selectable)tabs[0].button,
                    selectOnDown = i < rows.Count - 1
                        ? rows[i + 1].widget
                        : (Selectable)resetButton
                };
                rows[i].widget.navigation = nav;
            }
            var resetNav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = rows.Count > 0
                    ? rows[rows.Count - 1].widget : null,
                selectOnRight = backButton,
                selectOnDown = backButton
            };
            resetButton.navigation = resetNav;
            var backNav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = resetButton,
                selectOnLeft = resetButton
            };
            backButton.navigation = backNav;
        }

        static int Wrap(int i, int count)
        {
            return (i + count) % count;
        }

        bool IsPadTab()
        {
            return currentTab == ControlDeviceKind.Xbox
                || currentTab == ControlDeviceKind.PlayStation
                || currentTab == ControlDeviceKind.GenericPad;
        }

        static bool GenericPadConnected()
        {
            foreach (var pad in Gamepad.all)
            {
                if (ControlBindingService.KindOf(pad)
                    == ControlDeviceKind.GenericPad)
                    return true;
            }
            return false;
        }

        static string TabLabel(ControlDeviceKind kind)
        {
            switch (kind)
            {
                case ControlDeviceKind.Keyboard1:
                    return "KEYBOARD";
                case ControlDeviceKind.Xbox: return "XBOX";
                case ControlDeviceKind.PlayStation:
                    return "PLAYSTATION";
                default: return "OTHER PAD";
            }
        }

        static string RowLabel(SemanticButton button)
        {
            switch (button)
            {
                case SemanticButton.Left: return "MOVE LEFT";
                case SemanticButton.Right: return "MOVE RIGHT";
                case SemanticButton.Up: return "MOVE UP";
                case SemanticButton.Down: return "MOVE DOWN";
                case SemanticButton.A: return "JUMP";
                case SemanticButton.B: return "TONGUE";
                case SemanticButton.X: return "BAT";
                default: return "START / PAUSE";
            }
        }

        (Button, Text) MakeRow(string label, float y)
        {
            var shell = MakeRowShell(label, "", y);
            var widget = MakeTextButton(shell, "",
                40, new Vector2(330f, 0f), new Vector2(620f, 54f));
            var value = widget.GetComponentInChildren<Text>();
            return (widget, value);
        }

        /// <summary>Prompt sprite slot between a row's label and its
        /// value; 2:1 so wide key sprites keep their aspect.</summary>
        Image MakeRowIcon(RectTransform shell)
        {
            var go = new GameObject("PromptIcon",
                typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(shell, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-10f, 0f);
            rt.sizeDelta = new Vector2(108f, 54f);
            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        RectTransform MakeRowShell(string label, string fixedValue,
            float y)
        {
            var go = new GameObject($"Row_{label}",
                typeof(RectTransform));
            rowShells.Add(go);
            var rt = (RectTransform)go.transform;
            rt.SetParent(rowsRoot, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(1400f, 54f);
            MakeText(rt, label, 40, new Vector2(-370f, 0f),
                new Vector2(640f, 54f), TextNormal,
                TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(fixedValue))
                MakeText(rt, fixedValue, 40, new Vector2(330f, 0f),
                    new Vector2(620f, 54f), TextDisabled,
                    TextAnchor.MiddleCenter);
            return rt;
        }

        Button MakeTextButton(RectTransform parent, string label,
            int size, Vector2 pos, Vector2 dims)
        {
            var go = new GameObject($"Button_{label}",
                typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            var text = MakeText(rt, label, size, Vector2.zero, dims,
                Color.white, TextAnchor.MiddleCenter);

            var button = go.GetComponent<Button>();
            button.targetGraphic = text;
            var colors = button.colors;
            colors.normalColor = TextNormal;
            colors.highlightedColor = TextSelected;
            colors.selectedColor = TextSelected;
            colors.pressedColor = TextPressed;
            colors.disabledColor = TextDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            return button;
        }

        Text MakeText(RectTransform parent, string content, int size,
            Vector2 pos, Vector2 dims, Color color,
            TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform),
                typeof(Text), typeof(Shadow));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = font != null ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shadow.effectDistance = new Vector2(3f, -3f);
            return text;
        }
    }
}
