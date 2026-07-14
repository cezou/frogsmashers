using FrogSmashers.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;

namespace FrogSmashers.EditorTests
{
    /// <summary>
    /// Pins the settings feature invariants: dB mapping, the mixer
    /// asset shape, and the FrogControls asset defaults staying
    /// byte-compatible with the pre-rebinding hardcoded tables
    /// (the rollback-determinism lock).
    /// </summary>
    public class SettingsTests
    {
        const string MixerPath =
            "Assets/Resources/GameAudioMixer.mixer";
        const string ActionsPath =
            "Assets/Resources/FrogControls.inputactions";

        static readonly (string action, string path)[] Kb1Defaults =
        {
            ("Left", "<Keyboard>/a"),
            ("Right", "<Keyboard>/d"),
            ("Up", "<Keyboard>/w"),
            ("Down", "<Keyboard>/s"),
            ("A", "<Keyboard>/space"),
            ("B", "<Keyboard>/t"),
            ("X", "<Keyboard>/y"),
            ("Y", "<Keyboard>/u"),
            ("Start", "<Keyboard>/tab")
        };

        static readonly (string action, string control)[]
            PadDefaults =
        {
            ("A", "buttonSouth"),
            ("B", "buttonEast"),
            ("X", "buttonWest"),
            ("Y", "buttonNorth"),
            ("Start", "start")
        };

        [Test]
        public void LinearToDbMapsEndpointsAndMidpoint()
        {
            Assert.AreEqual(-80f, GameSettings.LinearToDb(0f));
            Assert.AreEqual(0f, GameSettings.LinearToDb(1f), 1e-4f);
            Assert.AreEqual(-6.0206f, GameSettings.LinearToDb(0.5f),
                1e-3f);
        }

        [Test]
        public void MixerHasGroupsAndExposedParameters()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(
                MixerPath);
            Assert.IsNotNull(mixer, $"missing {MixerPath}");
            Assert.AreEqual(1,
                mixer.FindMatchingGroups("Music").Length);
            Assert.AreEqual(1,
                mixer.FindMatchingGroups("SFX").Length);
            string yaml = System.IO.File.ReadAllText(MixerPath);
            AssertExposedVolume(yaml, "Music", "MusicVolume");
            AssertExposedVolume(yaml, "SFX", "SFXVolume");
        }

        static void AssertExposedVolume(string yaml, string group,
            string parameter)
        {
            var exposed = System.Text.RegularExpressions.Regex.Match(
                yaml, $"guid: (\\w+)\\s+name: {parameter}\\b");
            Assert.IsTrue(exposed.Success,
                $"{parameter} not exposed");
            var volume = System.Text.RegularExpressions.Regex.Match(
                yaml, $"m_Name: {group}\\b[\\s\\S]*?m_Volume: (\\w+)");
            Assert.IsTrue(volume.Success, $"{group} group missing");
            Assert.AreEqual(volume.Groups[1].Value,
                exposed.Groups[1].Value,
                $"{parameter} guid must match {group} volume guid");
        }

        [Test]
        public void ActionsAssetKeepsLegacyDefaultBindings()
        {
            var asset = LoadActions();
            AssertMapDefaults(asset, "Keyboard1", "Keyboard1",
                Kb1Defaults);
            foreach (var (action, control) in PadDefaults)
            {
                AssertBindingPath(asset, "Gamepad", action, "Xbox",
                    $"<XInputController>/{control}");
                AssertBindingPath(asset, "Gamepad", action,
                    "PlayStation", $"<DualShockGamepad>/{control}");
                AssertBindingPath(asset, "Gamepad", action,
                    "Generic", $"<Gamepad>/{control}");
            }
        }

        [Test]
        public void BindingOverridesSurviveJsonRoundTrip()
        {
            var asset = Object.Instantiate(LoadActions());
            var action = asset.FindActionMap("Keyboard1")
                .FindAction("A");
            action.ApplyBindingOverride(0, "<Keyboard>/k");
            string json = asset.SaveBindingOverridesAsJson();
            asset.RemoveAllBindingOverrides();
            Assert.AreEqual("<Keyboard>/space",
                action.bindings[0].effectivePath);
            asset.LoadBindingOverridesFromJson(json);
            Assert.AreEqual("<Keyboard>/k",
                action.bindings[0].effectivePath);
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void GeneratorIsDeterministic()
        {
            string before = System.IO.File.ReadAllText(ActionsPath);
            Editor.InputActionsGenerator.Generate();
            string after = System.IO.File.ReadAllText(ActionsPath);
            Assert.AreEqual(before, after,
                "regeneration must keep stable ids");
        }

        static InputActionAsset LoadActions()
        {
            var asset =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                    ActionsPath);
            Assert.IsNotNull(asset, $"missing {ActionsPath}");
            return asset;
        }

        static void AssertMapDefaults(InputActionAsset asset,
            string map, string group,
            (string action, string path)[] expected)
        {
            foreach (var (action, path) in expected)
                AssertBindingPath(asset, map, action, group, path);
        }

        static void AssertBindingPath(InputActionAsset asset,
            string map, string actionName, string group,
            string expectedPath)
        {
            var action = asset.FindActionMap(map)
                ?.FindAction(actionName);
            Assert.IsNotNull(action, $"{map}/{actionName}");
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].groups == null
                    || !bindings[i].groups.Contains(group))
                    continue;
                Assert.AreEqual(expectedPath, bindings[i].path,
                    $"{map}/{actionName} [{group}]");
                return;
            }
            Assert.Fail($"{map}/{actionName}: no binding in group " +
                group);
        }
    }
}
