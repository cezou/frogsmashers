using System.Collections.Generic;
using System.IO;
using FrogSmashers.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrogSmashers.Editor
{
    public static class MainMenuGenerator
    {
        const string ScenePath        = "Assets/Scenes/MainMenu.unity";
        const string BackgroundPath   = "Assets/Sprites/IntroAnim/intro_bg.png";
        const string LogoPath         = "Assets/Sprites/logo_v2.png";
        const string FontPath         = "Assets/Font/ArcadeClassic.ttf";

        static readonly Color TextNormal    = new Color(160f / 255f, 217f / 255f, 236f / 255f, 1f);
        static readonly Color TextSelected  = new Color(170f / 255f, 176f / 255f,  88f / 255f, 1f);
        static readonly Color TextPressed   = new Color(120f / 255f, 130f / 255f,  60f / 255f, 1f);
        static readonly Color TextDisabled  = new Color(100f / 255f, 135f / 255f, 145f / 255f, 1f);

        [MenuItem("Tools/FrogSmashers/Generate Main Menu Scene")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                Log("Cannot run while Unity is in Play mode.");
                return;
            }

            var bgSprite   = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            var logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            var font       = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (bgSprite   == null) Log($"WARN: background sprite not found at {BackgroundPath}");
            if (logoSprite == null) Log($"WARN: logo sprite not found at {LogoPath}");
            if (font       == null) Log($"WARN: font not found at {FontPath}");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            var canvas = BuildCanvas();
            BuildEventSystem();

            BuildBackground(canvas.transform, bgSprite);
            BuildLogo(canvas.transform, logoSprite);

            var localBtn    = BuildMenuButton(canvas.transform, "LocalGameButton",   "LOCAL GAME",    0,  font);
            var createBtn   = BuildMenuButton(canvas.transform, "CreateLobbyButton", "CREATE LOBBY",  1,  font);
            var rankedBtn   = BuildMenuButton(canvas.transform, "RankedQueueButton", "RANKED QUEUE",  2,  font);
            var settingsBtn = BuildMenuButton(canvas.transform, "SettingsButton",    "SETTINGS",      3,  font);

            var (panel, panelText, backBtn) = BuildComingSoonPanel(canvas.transform, font);
            var list = BuildLobbyList(canvas.transform, font);
            var settingsPanel = SettingsPanelBuilder.Build(canvas.transform, font);

            SetupNavigation(localBtn, createBtn, rankedBtn, settingsBtn);

            var controllerGo = new GameObject("MainMenuController");
            var controller = controllerGo.AddComponent<MainMenuController>();
            controller.localGameButton    = localBtn;
            controller.createLobbyButton  = createBtn;
            controller.rankedQueueButton  = rankedBtn;
            controller.settingsButton     = settingsBtn;
            controller.comingSoonPanel    = panel;
            controller.comingSoonText     = panelText;
            controller.comingSoonBackButton = backBtn;
            controller.settingsPanel      = settingsPanel;
            controller.lobbyListStatus    = list.status;
            controller.lobbyEntryButtons  = list.entries;
            controller.statusText         = list.bottomStatus;

            WirePersistent(localBtn.onClick,    controller, nameof(MainMenuController.OnLocalGame));
            WirePersistent(createBtn.onClick,   controller, nameof(MainMenuController.OnCreateLobby));
            WirePersistent(rankedBtn.onClick,   controller, nameof(MainMenuController.OnRankedQueue));
            WirePersistent(settingsBtn.onClick, controller, nameof(MainMenuController.OnSettings));
            WirePersistent(backBtn.onClick,     controller, nameof(MainMenuController.HideComingSoon));

            panel.SetActive(false);

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            Log(saved ? $"Saved scene to {ScenePath}" : $"FAILED to save scene to {ScenePath}");

            AddSceneToBuildSettings(ScenePath, insertIndex: 1);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Log("Main Menu generation complete.");
        }

        static void BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            go.AddComponent<AudioListener>();
        }

        static Canvas BuildCanvas()
        {
            var go = new GameObject("Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static void BuildEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        static void BuildBackground(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Background", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.transform.SetAsFirstSibling();

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = false;
            img.raycastTarget = false;
        }

        static void BuildLogo(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(900f, 360f);

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        static Button BuildMenuButton(Transform parent, string name, string label, int index, Font font)
        {
            float topY = 40f;
            float spacing = 92f;
            float y = topY - index * spacing;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(MenuButtonSelectionFx));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(560f, 78f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            var text = BuildLabel(go.transform, label, font);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = text;
            var colors = btn.colors;
            colors.normalColor      = TextNormal;
            colors.highlightedColor = TextSelected;
            colors.selectedColor    = TextSelected;
            colors.pressedColor     = TextPressed;
            colors.disabledColor    = TextDisabled;
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = 0.08f;
            btn.colors = colors;

            return btn;
        }

        static Text BuildLabel(Transform parent, string label, Font font)
        {
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            textGo.transform.SetParent(parent, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<Text>();
            text.text = label;
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 54;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            var shadow = textGo.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shadow.effectDistance = new Vector2(4f, -4f);

            return text;
        }

        static (GameObject panel, Text text, Button backBtn) BuildComingSoonPanel(Transform parent, Font font)
        {
            var panel = new GameObject("ComingSoonPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.transform.SetAsLastSibling();
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            var pimg = panel.GetComponent<Image>();
            pimg.color = new Color(0f, 0f, 0f, 0.92f);
            pimg.raycastTarget = true;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Shadow));
            labelGo.transform.SetParent(panel.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(0f, 120f);
            lrt.sizeDelta = new Vector2(1600f, 220f);

            var label = labelGo.GetComponent<Text>();
            label.text = "FEATURE — COMING SOON";
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 84;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 0.85f, 0.25f, 1f);
            label.raycastTarget = false;

            var shadow = labelGo.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 1f);
            shadow.effectDistance = new Vector2(4f, -4f);

            var backGo = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(MenuButtonSelectionFx));
            backGo.transform.SetParent(panel.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, -80f);
            brt.sizeDelta = new Vector2(420f, 90f);

            var backImg = backGo.GetComponent<Image>();
            backImg.color = new Color(0f, 0f, 0f, 0f);
            backImg.raycastTarget = true;

            var backText = BuildLabel(backGo.transform, "BACK", font);

            var backBtn = backGo.GetComponent<Button>();
            backBtn.targetGraphic = backText;
            var bc = backBtn.colors;
            bc.normalColor      = TextNormal;
            bc.highlightedColor = TextSelected;
            bc.selectedColor    = TextSelected;
            bc.pressedColor     = TextPressed;
            bc.disabledColor    = TextDisabled;
            bc.fadeDuration     = 0.08f;
            backBtn.colors = bc;

            return (panel, label, backBtn);
        }

        struct LobbyListParts
        {
            public Text status;
            public Button[] entries;
            public Text bottomStatus;
        }

        static LobbyListParts BuildLobbyList(Transform parent, Font font)
        {
            var parts = new LobbyListParts();

            BuildPanelText(parent, "LobbyListHeader", "LOBBIES", font,
                48, new Vector2(560f, 120f), new Vector2(500f, 60f),
                new Color(1f, 0.85f, 0.25f, 1f));

            parts.status = BuildPanelText(parent, "LobbyListStatus",
                "NO  LOBBIES  YET", font, 28,
                new Vector2(560f, 60f), new Vector2(500f, 40f),
                TextDisabled);

            parts.entries = new Button[6];
            for (int i = 0; i < parts.entries.Length; i++)
            {
                var btn = BuildPanelButton(parent, $"LobbyEntry{i}",
                    "LOBBY", font,
                    new Vector2(560f, 10f - i * 64f));
                var rt = (RectTransform)btn.transform;
                rt.sizeDelta = new Vector2(500f, 58f);
                var label = btn.GetComponentInChildren<Text>();
                label.fontSize = 34;
                parts.entries[i] = btn;
            }

            parts.bottomStatus = BuildPanelText(parent, "StatusText",
                "", font, 32, new Vector2(0f, -440f),
                new Vector2(1700f, 50f),
                new Color(1f, 0.85f, 0.25f, 1f));

            return parts;
        }

        static Text BuildPanelText(Transform parent, string name,
            string content, Font font, int size, Vector2 pos,
            Vector2 dims, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 1f);
            shadow.effectDistance = new Vector2(4f, -4f);
            return text;
        }

        static Button BuildPanelButton(Transform parent, string name,
            string label, Font font, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(MenuButtonSelectionFx));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(560f, 90f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;

            var text = BuildLabel(go.transform, label, font);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = text;
            var colors = btn.colors;
            colors.normalColor      = TextNormal;
            colors.highlightedColor = TextSelected;
            colors.selectedColor    = TextSelected;
            colors.pressedColor     = TextPressed;
            colors.disabledColor    = TextDisabled;
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = 0.08f;
            btn.colors = colors;
            return btn;
        }

        static void SetupNavigation(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var nav = buttons[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp   = i > 0 ? buttons[i - 1] : buttons[buttons.Length - 1];
                nav.selectOnDown = i < buttons.Length - 1 ? buttons[i + 1] : buttons[0];
                buttons[i].navigation = nav;
            }
        }

        static void WirePersistent(UnityEngine.Events.UnityEvent evt, MainMenuController target, string methodName)
        {
            var method = typeof(MainMenuController).GetMethod(methodName);
            var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
                typeof(UnityEngine.Events.UnityAction), target, method);
            UnityEventTools.AddPersistentListener(evt, action);
        }

        static void AddSceneToBuildSettings(string scenePath, int insertIndex)
        {
            var current = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            current.RemoveAll(s => s.path == scenePath);
            insertIndex = Mathf.Clamp(insertIndex, 0, current.Count);
            current.Insert(insertIndex, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = current.ToArray();
            Log($"Build settings updated. {scenePath} at index {insertIndex}. Total scenes: {current.Count}");
        }

        static void Log(string msg)
        {
            Debug.Log($"[MainMenuGenerator] {msg}");
        }
    }
}
