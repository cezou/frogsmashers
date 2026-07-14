using System.Collections.Generic;
using System.Linq;
using FrogSmashers.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FrogSmashers.Editor
{
    /// <summary>
    /// Wires the device-aware control prompts (issue #4) into the
    /// serialized assets: attaches ControlPromptIcon to every button
    /// glyph of JoinCanvas.prefab, adds the host-only CHANGE MODE
    /// hint lines, and adds the title screen's start-button glyph.
    /// Idempotent: generated nodes are found by name and updated in
    /// place on rerun, so serialized fileIDs never churn.
    /// </summary>
    public static class ControlPromptGenerator
    {
        const string PrefabPath = "Assets/Prefabs/UI/JoinCanvas.prefab";
        const string TitleScenePath = "Assets/Scenes/TitleScreen.unity";
        const string JoinScenePath = "Assets/Scenes/JoinScreen.unity";
        const string ChangeModeName = "ChangeModePrompt";
        const string FallbackLabelName = "FallbackLabel";
        const string StartPromptName = "StartPrompt";

        /// <summary>Prompt icons are recognized by the Xbox.png
        /// sub-sprite the prefab ships as placeholder art.</summary>
        static readonly Dictionary<string,
            ControlPromptIcon.PromptAction> ActionBySprite =
                new Dictionary<string,
                    ControlPromptIcon.PromptAction>
        {
            { "XButton", ControlPromptIcon.PromptAction.X },
            { "BButton", ControlPromptIcon.PromptAction.B },
            { "YButton", ControlPromptIcon.PromptAction.Y },
            { "AButton", ControlPromptIcon.PromptAction.A },
            { "Left", ControlPromptIcon.PromptAction.Left },
            { "Right", ControlPromptIcon.PromptAction.Right },
        };

        [MenuItem("FrogSmashers/Generate Control Prompts")]
        public static void GenerateAll()
        {
            UpdateJoinCanvasPrefab();
            UpdateTitleScreen();
            UpdateJoinScreen();
            AssetDatabase.SaveAssets();
            Debug.Log("[ControlPrompts] JoinCanvas prefab, title and " +
                "join screens updated.");
        }

        static void UpdateJoinCanvasPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var joinCanvas = root.GetComponent<JoinCanvas>();
                var colorPrompt = root.transform.Find("ColorPrompt");
                var backPrompt = root.transform.Find("BackPrompt");
                var joinPrompt = root.transform.Find("JoinPrompt");

                foreach (var image in
                    root.GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite == null
                        || image.transform.IsChildOf(joinPrompt))
                        continue;
                    if (!ActionBySprite.TryGetValue(
                        image.sprite.name, out var action))
                        continue;
                    var icon = image.GetComponent<ControlPromptIcon>();
                    if (icon == null)
                        icon = image.gameObject
                            .AddComponent<ControlPromptIcon>();
                    icon.action = action;
                    icon.source =
                        ControlPromptIcon.DeviceSource.JoinSlot;
                    icon.fallbackLabel = MakeFallbackLabel(image);
                }

                MakeJoinPromptIcons(joinPrompt);
                AlignTeamArrowIcons(joinCanvas);

                var line = MakeChangeModeLine(joinCanvas, colorPrompt,
                    new Vector2(-3.34f, -6.6f),
                    new Vector2(3.74f, -5.1f));
                joinCanvas.changeModeObjects = new[] { line };
                RemoveChangeModeLine(joinCanvas, backPrompt);

                joinCanvas.selectionBackObjects = new[]
                {
                    colorPrompt.Find("Text (1)").gameObject,
                    colorPrompt.Find("Image (1)").gameObject,
                };
                joinCanvas.confirmObjects = new[]
                {
                    colorPrompt.Find("Text (2)").gameObject,
                    colorPrompt.Find("Image (2)").gameObject,
                };

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Gets or creates the child GameObject of that
        /// name, keeping existing nodes so regeneration never churns
        /// serialized fileIDs.</summary>
        static GameObject GetOrCreateChild(Transform parent,
            string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return existing.gameObject;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component
                : go.AddComponent<T>();
        }

        /// <summary>Label shown on the blank keycap when a rebound
        /// key has no dedicated sprite.</summary>
        static Text MakeFallbackLabel(Image icon)
        {
            var reference = icon.transform.parent
                .GetComponentsInChildren<Text>(true)
                .FirstOrDefault(t => t.name != FallbackLabelName);
            var go = GetOrCreateChild(icon.transform,
                FallbackLabelName);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(2f, 1f);
            var text = GetOrAdd<Text>(go);
            if (reference != null)
                text.font = reference.font;
            text.fontSize = 1;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(0.1f, 0.1f, 0.15f);
            text.text = "";
            text.enabled = false;
            return text;
        }

        /// <summary>
        /// Centers the team-mode shade hint on the icon column
        /// (x -3.34): the keyboard key PAIR sits at -3.94/-2.74
        /// (midpoint on the column) and on pads the single combined
        /// d-pad glyph shifts +0.6 back onto the column while the
        /// second icon hides (ControlPromptIcon.padCenterOffsetX).
        /// </summary>
        static void AlignTeamArrowIcons(JoinCanvas joinCanvas)
        {
            var container = joinCanvas.teamChangeColorObject
                .transform;
            var left = container.Find("Image (4)");
            MoveIconX(left, -3.94f);
            MoveIconX(container.Find("Image (5)"), -2.74f);
            left.GetComponent<ControlPromptIcon>()
                .padCenterOffsetX = 0.6f;
        }

        static void MoveIconX(Transform icon, float x)
        {
            var rt = (RectTransform)icon;
            var pos = rt.anchoredPosition;
            pos.x = x;
            rt.anchoredPosition = pos;
        }

        /// <summary>
        /// Turns the JOIN line's single glyph into a JoinPromptIcons
        /// row (one glyph per joinable device kind): strips the
        /// per-slot ControlPromptIcon the older layout attached and
        /// adds two more icon slots to the left of the original.
        /// </summary>
        static void MakeJoinPromptIcons(Transform joinPrompt)
        {
            var first = joinPrompt.Find("Image").GetComponent<Image>();
            var oldIcon = first.GetComponent<ControlPromptIcon>();
            if (oldIcon != null)
                Object.DestroyImmediate(oldIcon);
            var oldLabel = first.transform.Find(FallbackLabelName);
            if (oldLabel != null)
                Object.DestroyImmediate(oldLabel.gameObject);

            var firstRt = (RectTransform)first.transform;
            var icons = new Image[3];
            icons[0] = first;
            for (int i = 1; i < icons.Length; i++)
            {
                var go = GetOrCreateChild(joinPrompt, $"Icon{i}");
                var rt = (RectTransform)go.transform;
                rt.anchorMin = firstRt.anchorMin;
                rt.anchorMax = firstRt.anchorMax;
                rt.pivot = firstRt.pivot;
                rt.sizeDelta = firstRt.sizeDelta;
                rt.anchoredPosition = firstRt.anchoredPosition
                    + new Vector2(-1.2f * i, 0f);
                var image = GetOrAdd<Image>(go);
                image.preserveAspect = true;
                image.enabled = false;
                icons[i] = image;
            }
            first.preserveAspect = true;

            var prompt = GetOrAdd<JoinPromptIcons>(
                joinPrompt.gameObject);
            prompt.icons = icons;
        }

        /// <summary>Drops a previously generated CHANGE MODE line
        /// (older layouts also had one under BackPrompt).</summary>
        static void RemoveChangeModeLine(JoinCanvas joinCanvas,
            Transform parent)
        {
            var line = parent.Find(ChangeModeName);
            if (line == null)
                return;
            joinCanvas.texts = joinCanvas.texts.Where(t =>
                t != null && !t.transform.IsChildOf(line))
                .ToArray();
            Object.DestroyImmediate(line.gameObject);
        }

        static GameObject MakeChangeModeLine(JoinCanvas joinCanvas,
            Transform parent, Vector2 iconPos, Vector2 textPos)
        {
            var existingLine = parent.Find(ChangeModeName);
            var reference = parent
                .GetComponentsInChildren<Text>(true)
                .First(t => t.name != FallbackLabelName
                    && (existingLine == null
                        || !t.transform.IsChildOf(existingLine)));
            var line = GetOrCreateChild(parent, ChangeModeName);
            joinCanvas.texts = joinCanvas.texts.Where(t =>
                t != null && !t.transform.IsChildOf(line.transform))
                .ToArray();
            var lineRt = (RectTransform)line.transform;
            lineRt.anchorMin = Vector2.zero;
            lineRt.anchorMax = Vector2.one;
            lineRt.sizeDelta = Vector2.zero;
            lineRt.anchoredPosition = Vector2.zero;

            var referenceRt = (RectTransform)reference.transform;
            var textGo = GetOrCreateChild(line.transform, "Text");
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = textRt.anchorMax = new Vector2(0.5f, 1f);
            textRt.pivot = referenceRt.pivot;
            textRt.anchoredPosition = textPos;
            textRt.sizeDelta = referenceRt.sizeDelta;
            var text = GetOrAdd<Text>(textGo);
            text.font = reference.font;
            text.fontSize = reference.fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.text = "CHANGE MODE";
            joinCanvas.texts = joinCanvas.texts
                .Concat(new[] { text }).ToArray();

            var referenceIcon = parent
                .GetComponentsInChildren<Image>(true)
                .FirstOrDefault(i =>
                    i.GetComponent<ControlPromptIcon>() != null
                    && !i.transform.IsChildOf(line.transform));
            var iconGo = GetOrCreateChild(line.transform, "Icon");
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 1f);
            if (referenceIcon != null)
                iconRt.pivot = ((RectTransform)referenceIcon
                    .transform).pivot;
            iconRt.anchoredPosition = iconPos;
            iconRt.sizeDelta = new Vector2(2f, 1f);
            var image = GetOrAdd<Image>(iconGo);
            var icon = GetOrAdd<ControlPromptIcon>(iconGo);
            icon.action = ControlPromptIcon.PromptAction.Select;
            icon.source = ControlPromptIcon.DeviceSource.JoinSlot;
            icon.fallbackLabel = MakeFallbackLabel(image);

            line.SetActive(false);
            return line;
        }

        static void UpdateTitleScreen()
        {
            var scene = EditorSceneManager.OpenScene(TitleScenePath);
            var start = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "Start"
                    && t.GetComponent<SpriteRenderer>() != null);
            if (start == null)
            {
                Debug.LogError("[ControlPrompts] TitleScreen has no " +
                    "'Start' sprite object.");
                return;
            }
            var parentRenderer = start.GetComponent<SpriteRenderer>();
            var existing = start.Find(StartPromptName);
            var go = existing != null ? existing.gameObject
                : new GameObject(StartPromptName);
            if (existing == null)
                go.transform.SetParent(start, false);
            go.transform.localPosition = new Vector3(-3.2f, -0.8f, 0f);
            go.transform.localScale = new Vector3(8f, 8f, 1f);
            var renderer = GetOrAdd<SpriteRenderer>(go);
            renderer.sortingLayerID = parentRenderer.sortingLayerID;
            renderer.sortingOrder = parentRenderer.sortingOrder;
            var icon = GetOrAdd<ControlPromptIcon>(go);
            icon.action = ControlPromptIcon.PromptAction.Start;
            icon.source = ControlPromptIcon.DeviceSource.LastUsed;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Moves the mode label ("TEAM" / "FREE FOR ALL")
        /// into the bottom black band of the join screen, mirroring
        /// its old top-band position.</summary>
        static void UpdateJoinScreen()
        {
            var scene = EditorSceneManager.OpenScene(JoinScenePath);
            var controller = scene.GetRootGameObjects()
                .SelectMany(g =>
                    g.GetComponentsInChildren<GameController>(true))
                .FirstOrDefault();
            if (controller == null || controller.joinGameModeText == null)
            {
                Debug.LogError("[ControlPrompts] JoinScreen has no " +
                    "GameController.joinGameModeText.");
                return;
            }
            var rt = (RectTransform)controller.joinGameModeText
                .transform;
            rt.anchoredPosition = new Vector2(0f, -16.87f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
