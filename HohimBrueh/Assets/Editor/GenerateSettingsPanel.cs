using FrogSmashers.Settings;
using FrogSmashers.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FrogSmashers.Editor
{
    /// <summary>
    /// Builds the settings overlay inside the MainMenu canvas:
    /// display section (window mode + resolution cyclers, apply with
    /// keep-or-revert confirm), audio sliders, controls submenu
    /// shell, and wires SettingsPanelController + ControlsMenu.
    /// Called by MainMenuGenerator.Generate.
    /// </summary>
    public static class SettingsPanelBuilder
    {
        static readonly Color TextNormal =
            new Color(160f / 255f, 217f / 255f, 236f / 255f, 1f);
        static readonly Color TextSelected =
            new Color(170f / 255f, 176f / 255f, 88f / 255f, 1f);
        static readonly Color TextPressed =
            new Color(120f / 255f, 130f / 255f, 60f / 255f, 1f);
        static readonly Color TextDisabled =
            new Color(100f / 255f, 135f / 255f, 145f / 255f, 1f);
        static readonly Color Accent =
            new Color(1f, 0.85f, 0.25f, 1f);

        /// <summary>Builds the whole panel; returns its controller.</summary>
        public static SettingsPanelController Build(Transform canvas,
            Font font)
        {
            var root = MakeOverlay(canvas, "SettingsPanel", 0.92f);

            MakeText(root, "Title", "SETTINGS", font, 80,
                new Vector2(0f, 430f), new Vector2(900f, 100f),
                Accent);

            MakeText(root, "DisplayHeader", "DISPLAY", font, 48,
                new Vector2(0f, 330f), new Vector2(700f, 60f),
                Accent);
            MakeText(root, "WindowModeLabel", "WINDOW  MODE", font,
                44, new Vector2(-420f, 260f), new Vector2(620f, 60f),
                TextNormal);
            var modeCycler = MakeCycler(root, "WindowModeCycler",
                font, new Vector2(320f, 260f));
            MakeText(root, "ResolutionLabel", "RESOLUTION", font, 44,
                new Vector2(-420f, 190f), new Vector2(620f, 60f),
                TextNormal);
            var resolutionCycler = MakeCycler(root,
                "ResolutionCycler", font, new Vector2(320f, 190f));
            var applyButton = MakeButton(root, "ApplyButton",
                "APPLY", font, 46, new Vector2(0f, 112f),
                new Vector2(420f, 72f));

            MakeText(root, "AudioHeader", "AUDIO", font, 48,
                new Vector2(0f, 26f), new Vector2(700f, 60f),
                Accent);
            MakeText(root, "MusicLabel", "MUSIC", font, 44,
                new Vector2(-420f, -46f), new Vector2(620f, 60f),
                TextNormal);
            var musicSlider = MakeSlider(root, "MusicSlider",
                new Vector2(320f, -46f));
            MakeText(root, "FxLabel", "FX", font, 44,
                new Vector2(-420f, -116f), new Vector2(620f, 60f),
                TextNormal);
            var sfxSlider = MakeSlider(root, "FxSlider",
                new Vector2(320f, -116f));

            var controlsButton = MakeButton(root, "ControlsButton",
                "CONTROLS", font, 50, new Vector2(0f, -220f),
                new Vector2(520f, 80f));
            var backButton = MakeButton(root, "BackButton", "BACK",
                font, 50, new Vector2(0f, -320f),
                new Vector2(420f, 80f));

            var confirmPanel = MakeOverlay(root, "ConfirmPanel",
                0.95f);
            var confirmText = MakeText(confirmPanel, "ConfirmText",
                "KEEP  THESE  SETTINGS ?", font, 62,
                new Vector2(0f, 90f), new Vector2(1700f, 120f),
                Accent);
            var keepButton = MakeButton(confirmPanel, "KeepButton",
                "KEEP", font, 50, new Vector2(0f, -70f),
                new Vector2(420f, 90f));
            confirmPanel.gameObject.SetActive(false);

            var (controlsPanel, controlsMenu) = BuildControlsPanel(
                root, font);

            var controller = root.gameObject
                .AddComponent<SettingsPanelController>();
            controller.root = root.gameObject;
            controller.windowModeCycler = modeCycler;
            controller.resolutionCycler = resolutionCycler;
            controller.applyButton = applyButton;
            controller.musicSlider = musicSlider;
            controller.sfxSlider = sfxSlider;
            controller.controlsButton = controlsButton;
            controller.controlsPanel = controlsPanel.gameObject;
            controller.backButton = backButton;
            controller.confirmPanel = confirmPanel.gameObject;
            controller.confirmText = confirmText;
            controller.confirmKeepButton = keepButton;
            controlsMenu.owner = controller;

            SetupVerticalChain(modeCycler, resolutionCycler,
                applyButton, musicSlider, sfxSlider, controlsButton,
                backButton);

            root.gameObject.SetActive(false);
            return controller;
        }

        static (RectTransform, ControlsMenu) BuildControlsPanel(
            RectTransform parent, Font font)
        {
            var panel = MakeOverlay(parent, "ControlsPanel", 0.97f);
            MakeText(panel, "Title", "CONTROLS", font, 72,
                new Vector2(0f, 440f), new Vector2(900f, 90f),
                Accent);

            var tabBar = MakeContainer(panel, "TabBar",
                new Vector2(0f, 340f), new Vector2(1700f, 60f));
            var rowsRoot = MakeContainer(panel, "Rows",
                new Vector2(0f, 20f), new Vector2(1700f, 600f));
            var status = MakeText(panel, "Status", "", font, 40,
                new Vector2(0f, -330f), new Vector2(1700f, 60f),
                Accent);

            var resetButton = MakeButton(panel, "ResetButton",
                "RESET TO DEFAULTS", font, 44,
                new Vector2(-320f, -420f), new Vector2(620f, 80f));
            var backButton = MakeButton(panel, "BackButton", "BACK",
                font, 44, new Vector2(320f, -420f),
                new Vector2(420f, 80f));

            var menu = panel.gameObject.AddComponent<ControlsMenu>();
            menu.font = font;
            menu.tabBar = tabBar;
            menu.rowsRoot = rowsRoot;
            menu.resetButton = resetButton;
            menu.backButton = backButton;
            menu.statusText = status;

            panel.gameObject.SetActive(false);
            return (panel, menu);
        }

        static RectTransform MakeOverlay(Transform parent,
            string name, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform),
                typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetAsLastSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, alpha);
            img.raycastTarget = true;
            return rt;
        }

        static RectTransform MakeContainer(RectTransform parent,
            string name, Vector2 pos, Vector2 dims)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;
            return rt;
        }

        static Text MakeText(RectTransform parent, string name,
            string content, Font font, int size, Vector2 pos,
            Vector2 dims, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform),
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
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 1f);
            shadow.effectDistance = new Vector2(4f, -4f);
            return text;
        }

        static Button MakeButton(RectTransform parent, string name,
            string label, Font font, int size, Vector2 pos,
            Vector2 dims)
        {
            var go = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(Button),
                typeof(MenuButtonSelectionFx));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            var text = MakeText(rt, "Text", label, font, size,
                Vector2.zero, dims, Color.white);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var button = go.GetComponent<Button>();
            button.targetGraphic = text;
            ApplyColors(button);
            return button;
        }

        static OptionCycler MakeCycler(RectTransform parent,
            string name, Font font, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform),
                typeof(Image), typeof(OptionCycler));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(680f, 60f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            var text = MakeText(rt, "Value", "", font, 44,
                Vector2.zero, new Vector2(680f, 60f), Color.white);

            var cycler = go.GetComponent<OptionCycler>();
            cycler.valueText = text;
            cycler.targetGraphic = text;
            ApplyColors(cycler);
            return cycler;
        }

        static Slider MakeSlider(RectTransform parent, string name,
            Vector2 pos)
        {
            var go = DefaultControls.CreateSlider(
                new DefaultControls.Resources());
            go.name = name;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(620f, 44f);

            var background = go.transform.Find("Background")
                ?.GetComponent<Image>();
            if (background != null)
                background.color = new Color(0f, 0f, 0f, 0.55f);
            var fill = go.transform
                .Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill != null)
                fill.color = Accent;
            var handle = go.transform
                .Find("Handle Slide Area/Handle")
                ?.GetComponent<Image>();
            if (handle != null)
                handle.color = TextNormal;

            var slider = go.GetComponent<Slider>();
            ApplyColors(slider);
            return slider;
        }

        static void ApplyColors(Selectable selectable)
        {
            var colors = selectable.colors;
            colors.normalColor = TextNormal;
            colors.highlightedColor = TextSelected;
            colors.selectedColor = TextSelected;
            colors.pressedColor = TextPressed;
            colors.disabledColor = TextDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
        }

        static void SetupVerticalChain(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = items[i > 0 ? i - 1
                        : items.Length - 1],
                    selectOnDown = items[i < items.Length - 1
                        ? i + 1 : 0]
                };
                items[i].navigation = nav;
            }
        }
    }
}
