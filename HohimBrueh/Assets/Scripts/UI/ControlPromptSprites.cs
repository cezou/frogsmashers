using System.Collections.Generic;
using FrogSmashers.Settings;
using UnityEngine;

namespace FrogSmashers.UI
{
    /// <summary>
    /// Name-indexed access to the generated input-prompt atlas
    /// (Assets/Resources/InputPromptAtlas.png). Sprite names encode
    /// device prefix + Input System control path, e.g.
    /// "xbox_buttonSouth", "ps_buttonEast", "kb_space".
    /// </summary>
    public static class ControlPromptSprites
    {
        const string AtlasName = "InputPromptAtlas";

        static Dictionary<string, Sprite> sprites;

        public static bool TryGet(string name, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(name))
                return false;
            EnsureLoaded();
            return sprites.TryGetValue(name, out sprite);
        }

        /// <summary>Atlas sprite-name prefix of a binding-set owner.</summary>
        public static string PrefixFor(ControlDeviceKind kind)
        {
            switch (kind)
            {
                case ControlDeviceKind.Xbox: return "xbox";
                case ControlDeviceKind.PlayStation: return "ps";
                case ControlDeviceKind.GenericPad: return "pad";
                default: return "kb";
            }
        }

        static void EnsureLoaded()
        {
            if (sprites != null)
                return;
            sprites = new Dictionary<string, Sprite>();
            foreach (var sprite in
                Resources.LoadAll<Sprite>(AtlasName))
            {
                sprites[sprite.name] = sprite;
            }
            if (sprites.Count == 0)
                Debug.LogWarning(
                    "[ControlPromptSprites] Atlas missing or empty.");
        }
    }
}
