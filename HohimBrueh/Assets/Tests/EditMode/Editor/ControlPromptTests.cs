using System.Collections.Generic;
using System.Linq;
using FrogSmashers.Settings;
using FrogSmashers.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

namespace FrogSmashers.EditorTests
{
    /// <summary>
    /// Pins the control-prompt feature invariants (issue #4): the
    /// generated atlas shape, sprite coverage of every default
    /// binding for every binding-set owner, and the JoinCanvas
    /// prefab wiring produced by ControlPromptGenerator.
    /// </summary>
    public class ControlPromptTests
    {
        const string AtlasPath =
            "Assets/Resources/InputPromptAtlas.png";
        const string PrefabPath =
            "Assets/Prefabs/UI/JoinCanvas.prefab";

        static readonly ControlDeviceKind[] AllKinds =
        {
            ControlDeviceKind.Keyboard1, ControlDeviceKind.Xbox,
            ControlDeviceKind.PlayStation,
            ControlDeviceKind.GenericPad
        };

        static readonly SemanticButton[] AllButtons =
        {
            SemanticButton.Left, SemanticButton.Right,
            SemanticButton.Up, SemanticButton.Down,
            SemanticButton.A, SemanticButton.B, SemanticButton.X,
            SemanticButton.Y, SemanticButton.Start
        };

        static Dictionary<string, Sprite> LoadAtlas()
        {
            return AssetDatabase.LoadAllAssetsAtPath(AtlasPath)
                .OfType<Sprite>()
                .ToDictionary(s => s.name, s => s);
        }

        [Test]
        public void AtlasIsMultiSpritePointFilteredAndNamed()
        {
            var importer =
                (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            Assert.IsNotNull(importer, "atlas asset missing");
            Assert.AreEqual(SpriteImportMode.Multiple,
                importer.spriteImportMode);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed,
                importer.textureCompression);

            var sprites = AssetDatabase.LoadAllAssetsAtPath(AtlasPath)
                .OfType<Sprite>().ToList();
            Assert.Greater(sprites.Count, 100,
                "atlas lost most of its sprites");
            Assert.AreEqual(sprites.Count,
                sprites.Select(s => s.name).Distinct().Count(),
                "duplicate sprite names");
        }

        [Test]
        public void EveryDefaultBindingHasASprite()
        {
            var atlas = LoadAtlas();
            var asset = ControlBindingService.Asset;
            Assert.IsNotNull(asset, "FrogControls asset missing");
            string saved = asset.SaveBindingOverridesAsJson();
            asset.RemoveAllBindingOverrides();
            ControlBindingService.InvalidateCache();
            try
            {
                foreach (var kind in AllKinds)
                {
                    foreach (var button in AllButtons)
                    {
                        string name = ControlPromptIcon.SpriteNameFor(
                            kind,
                            (ControlPromptIcon.PromptAction)button);
                        Assert.IsNotNull(name,
                            $"{kind}/{button} resolves no path");
                        Assert.IsTrue(atlas.ContainsKey(name),
                            $"{kind}/{button} → '{name}' not in atlas");
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(saved))
                    asset.LoadBindingOverridesFromJson(saved);
                ControlBindingService.InvalidateCache();
            }
        }

        [Test]
        public void SelectAndFallbackSpritesExist()
        {
            var atlas = LoadAtlas();
            foreach (var name in new[]
            {
                "xbox_select", "ps_select", "pad_select", "kb_tab",
                "kb_blank", "xbox_start", "ps_start", "pad_start",
                "pad_dpadHorizontal"
            })
            {
                Assert.IsTrue(atlas.ContainsKey(name),
                    $"'{name}' missing from atlas");
            }
            foreach (var kind in AllKinds)
            {
                string name = ControlPromptIcon.SpriteNameFor(kind,
                    ControlPromptIcon.PromptAction.Select);
                Assert.IsTrue(atlas.ContainsKey(name),
                    $"select prompt '{name}' missing for {kind}");
            }
        }

        [Test]
        public void JoinCanvasPrefabIsWired()
        {
            var root =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(root);
            var placeholders = new HashSet<string>
            {
                "XButton", "BButton", "YButton", "AButton",
                "Left", "Right"
            };
            var joinPrompt = root.transform.Find("JoinPrompt");
            foreach (var image in
                root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null
                    || !placeholders.Contains(image.sprite.name)
                    || image.transform.IsChildOf(joinPrompt))
                    continue;
                Assert.IsNotNull(
                    image.GetComponent<ControlPromptIcon>(),
                    $"{image.name} has no ControlPromptIcon");
            }

            var joinIcons = joinPrompt
                .GetComponent<JoinPromptIcons>();
            Assert.IsNotNull(joinIcons, "JoinPromptIcons missing");
            Assert.AreEqual(3, joinIcons.icons.Length);
            foreach (var icon in joinIcons.icons)
                Assert.IsNotNull(icon, "join icon slot missing");
            Assert.IsNull(joinIcons.icons[0]
                .GetComponent<ControlPromptIcon>(),
                "JOIN glyph must not keep a per-slot icon");

            var joinCanvas = root.GetComponent<JoinCanvas>();
            Assert.IsNotNull(joinCanvas.changeModeObjects);
            Assert.AreEqual(1, joinCanvas.changeModeObjects.Length,
                "one CHANGE MODE line, under ColorPrompt only");
            foreach (var line in joinCanvas.changeModeObjects)
            {
                Assert.IsNotNull(line, "change-mode line missing");
                Assert.IsFalse(line.activeSelf,
                    "change-mode line must start inactive");
                Assert.AreEqual("ColorPrompt", line.transform.parent.name);
                var text = line.GetComponentsInChildren<Text>(true)
                    .First(t => t.name == "Text");
                Assert.AreEqual("CHANGE MODE", text.text);
                var icon = line
                    .GetComponentInChildren<ControlPromptIcon>(true);
                Assert.IsNotNull(icon);
                Assert.AreEqual(ControlPromptIcon.PromptAction.Select,
                    icon.action);
            }

            Assert.AreEqual(2, joinCanvas.selectionBackObjects.Length);
            Assert.AreEqual(2, joinCanvas.confirmObjects.Length);
            foreach (var obj in joinCanvas.selectionBackObjects
                .Concat(joinCanvas.confirmObjects))
            {
                Assert.IsNotNull(obj, "selection handle missing");
                Assert.AreEqual("ColorPrompt",
                    obj.transform.parent.name);
            }
        }

        [Test]
        public void DualShockResolvesPlayStationSprites()
        {
            var atlas = LoadAtlas();
            var pad = InputSystem.AddDevice<DualShockGamepad>();
            try
            {
                Assert.AreEqual(ControlDeviceKind.PlayStation,
                    ControlBindingService.KindOf(pad));
                string name = ControlPromptIcon.SpriteNameFor(
                    ControlDeviceKind.PlayStation,
                    ControlPromptIcon.PromptAction.A);
                StringAssert.StartsWith("ps_", name);
                Assert.IsTrue(atlas.ContainsKey(name));
            }
            finally
            {
                InputSystem.RemoveDevice(pad);
            }
        }
    }
}
