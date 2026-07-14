using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrogSmashers.Editor
{
    /// <summary>
    /// Generates Assets/Resources/FrogControls.inputactions, the
    /// rebindable source of gameplay bindings: map Keyboard1 (the
    /// single keyboard set — Unity exposes one merged Keyboard
    /// device) and map Gamepad with one binding per brand group
    /// (Xbox, PlayStation, Generic). All ids derive deterministically
    /// from stable seed
    /// strings so a regeneration keeps every saved binding override
    /// valid (overrides are matched by binding id).
    /// </summary>
    public static class InputActionsGenerator
    {
        const string AssetPath =
            "Assets/Resources/FrogControls.inputactions";

        static readonly string[] Buttons =
        {
            "Left", "Right", "Up", "Down", "A", "B", "X", "Y", "Start"
        };

        static readonly string[] Kb1Paths =
        {
            "a", "d", "w", "s", "space", "t", "y", "u", "tab"
        };

        static readonly string[] PadButtons =
        {
            "A", "B", "X", "Y", "Start"
        };

        static readonly string[] PadPaths =
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth",
            "start"
        };

        [MenuItem("Tools/FrogSmashers/Generate Input Actions")]
        public static void Generate()
        {
            var sb = new StringBuilder();
            sb.Append("{\n    \"name\": \"FrogControls\",\n");
            sb.Append("    \"maps\": [\n");
            AppendKeyboardMap(sb, "Keyboard1", Kb1Paths);
            sb.Append(",\n");
            AppendGamepadMap(sb);
            sb.Append("\n    ],\n");
            AppendControlSchemes(sb);
            sb.Append("}\n");

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            File.WriteAllText(AssetPath, sb.ToString());
            AssetDatabase.ImportAsset(AssetPath);
            Debug.Log($"[InputActionsGenerator] Wrote {AssetPath}");
        }

        static void AppendKeyboardMap(StringBuilder sb, string map,
            string[] paths)
        {
            sb.Append("        {\n");
            sb.Append($"            \"name\": \"{map}\",\n");
            sb.Append($"            \"id\": \"{Id(map)}\",\n");
            sb.Append("            \"actions\": [\n");
            for (int i = 0; i < Buttons.Length; i++)
            {
                AppendAction(sb, map, Buttons[i]);
                sb.Append(i < Buttons.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("            ],\n");
            sb.Append("            \"bindings\": [\n");
            for (int i = 0; i < Buttons.Length; i++)
            {
                AppendBinding(sb, map, Buttons[i],
                    $"<Keyboard>/{paths[i]}", map);
                sb.Append(i < Buttons.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("            ]\n");
            sb.Append("        }");
        }

        static void AppendGamepadMap(StringBuilder sb)
        {
            const string map = "Gamepad";
            sb.Append("        {\n");
            sb.Append($"            \"name\": \"{map}\",\n");
            sb.Append($"            \"id\": \"{Id(map)}\",\n");
            sb.Append("            \"actions\": [\n");
            for (int i = 0; i < PadButtons.Length; i++)
            {
                AppendAction(sb, map, PadButtons[i]);
                sb.Append(i < PadButtons.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("            ],\n");
            sb.Append("            \"bindings\": [\n");
            for (int i = 0; i < PadButtons.Length; i++)
            {
                AppendBinding(sb, map, PadButtons[i],
                    $"<XInputController>/{PadPaths[i]}", "Xbox");
                sb.Append(",\n");
                AppendBinding(sb, map, PadButtons[i],
                    $"<DualShockGamepad>/{PadPaths[i]}", "PlayStation");
                sb.Append(",\n");
                AppendBinding(sb, map, PadButtons[i],
                    $"<Gamepad>/{PadPaths[i]}", "Generic");
                sb.Append(i < PadButtons.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("            ]\n");
            sb.Append("        }");
        }

        static void AppendAction(StringBuilder sb, string map,
            string action)
        {
            sb.Append("                {\n");
            sb.Append($"                    \"name\": \"{action}\",\n");
            sb.Append("                    \"type\": \"Button\",\n");
            sb.Append("                    \"id\": " +
                $"\"{Id($"{map}/{action}")}\",\n");
            sb.Append("                    \"expectedControlType\": " +
                "\"Button\",\n");
            sb.Append("                    \"processors\": \"\",\n");
            sb.Append("                    \"interactions\": \"\",\n");
            sb.Append("                    \"initialStateCheck\": " +
                "false\n");
            sb.Append("                }");
        }

        static void AppendBinding(StringBuilder sb, string map,
            string action, string path, string group)
        {
            sb.Append("                {\n");
            sb.Append("                    \"name\": \"\",\n");
            sb.Append("                    \"id\": " +
                $"\"{Id($"{map}/{action}/{group}")}\",\n");
            sb.Append($"                    \"path\": \"{path}\",\n");
            sb.Append("                    \"interactions\": \"\",\n");
            sb.Append("                    \"processors\": \"\",\n");
            sb.Append($"                    \"groups\": \"{group}\",\n");
            sb.Append($"                    \"action\": \"{action}\",\n");
            sb.Append("                    \"isComposite\": false,\n");
            sb.Append("                    \"isPartOfComposite\": " +
                "false\n");
            sb.Append("                }");
        }

        static void AppendControlSchemes(StringBuilder sb)
        {
            sb.Append("    \"controlSchemes\": [\n");
            AppendScheme(sb, "Keyboard1", "<Keyboard>");
            sb.Append(",\n");
            AppendScheme(sb, "Xbox", "<XInputController>");
            sb.Append(",\n");
            AppendScheme(sb, "PlayStation", "<DualShockGamepad>");
            sb.Append(",\n");
            AppendScheme(sb, "Generic", "<Gamepad>");
            sb.Append("\n    ]\n");
        }

        static void AppendScheme(StringBuilder sb, string name,
            string devicePath)
        {
            sb.Append("        {\n");
            sb.Append($"            \"name\": \"{name}\",\n");
            sb.Append($"            \"bindingGroup\": \"{name}\",\n");
            sb.Append("            \"devices\": [\n");
            sb.Append("                {\n");
            sb.Append("                    \"devicePath\": " +
                $"\"{devicePath}\",\n");
            sb.Append("                    \"isOptional\": false,\n");
            sb.Append("                    \"isOR\": false\n");
            sb.Append("                }\n");
            sb.Append("            ]\n");
            sb.Append("        }");
        }

        static string Id(string seed)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(
                    Encoding.UTF8.GetBytes($"FrogControls/{seed}"));
                return new Guid(hash).ToString();
            }
        }
    }
}
