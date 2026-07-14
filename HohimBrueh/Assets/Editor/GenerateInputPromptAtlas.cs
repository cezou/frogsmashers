using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace FrogSmashers.Editor
{
    /// <summary>
    /// Generates Assets/Resources/InputPromptAtlas.png, the named
    /// input-prompt sprite sheet used by control prompts (issue #4),
    /// from the Kenney "Input Prompts Pixel" source tile sheet
    /// (Assets/Sprites/UI/InputPrompts/Kenney/tilemap.png, CC0).
    /// Sprite names encode device + Input System control path
    /// ("xbox_buttonSouth", "ps_buttonEast", "kb_space", ...);
    /// PlayStation face buttons are composed from a blank button
    /// tile plus a two-half symbol overlay, wide keyboard keys
    /// (ENTER, TAB, SHIFT, ...) span two tiles. Output layout and
    /// sprite ids derive deterministically from the sorted names so
    /// regeneration is byte-stable.
    /// </summary>
    public static class InputPromptAtlasGenerator
    {
        const string SourcePath =
            "Assets/Sprites/UI/InputPrompts/Kenney/tilemap.png";
        const string AtlasPath =
            "Assets/Resources/InputPromptAtlas.png";

        /// <summary>Kenney sheet: 16px tiles with 1px spacing,
        /// tile index = row * 34 + column.</summary>
        const int Tile = 16;

        const int Stride = 17;

        const int SheetCols = 34;

        /// <summary>Output grid: fixed 32px-wide cells so two-tile
        /// keys fit; single tiles use the left half.</summary>
        const int CellW = 32;

        const int Cols = 8;

        enum Compose { Single, Wide, PsSymbol }

        struct Entry
        {
            public string name;
            public int a, b;
            public Compose mode;

            public Entry(string name, int a)
            {
                this.name = name; this.a = a; b = -1;
                mode = Compose.Single;
            }

            public Entry(string name, int a, int b, Compose mode)
            {
                this.name = name; this.a = a; this.b = b;
                this.mode = mode;
            }
        }

        /// <summary>Name-to-tile table: Xbox colored buttons, PS
        /// symbol halves, generic mono buttons, the two-arm d-pad,
        /// select/start glyphs (PS reuses menu/share), and the full
        /// light keyboard set (wide keys span two tiles).</summary>
        static List<Entry> Table()
        {
            var t = new List<Entry>
            {
                new Entry("xbox_buttonSouth", 4),
                new Entry("xbox_buttonEast", 5),
                new Entry("xbox_buttonWest", 6),
                new Entry("xbox_buttonNorth", 7),
                new Entry("xbox_start", 617),
                new Entry("xbox_select", 616),
                new Entry("xbox_leftStick", 212),

                new Entry("ps_buttonSouth", 567, 568, Compose.PsSymbol),
                new Entry("ps_buttonEast", 563, 564, Compose.PsSymbol),
                new Entry("ps_buttonWest", 565, 566, Compose.PsSymbol),
                new Entry("ps_buttonNorth", 561, 562, Compose.PsSymbol),
                new Entry("ps_start", 617),
                new Entry("ps_select", 618),
                new Entry("ps_leftStick", 212),

                new Entry("pad_buttonSouth", 13),
                new Entry("pad_buttonEast", 14),
                new Entry("pad_buttonWest", 15),
                new Entry("pad_buttonNorth", 16),
                new Entry("pad_start", 617),
                new Entry("pad_select", 616),
                new Entry("pad_leftStick", 212),

                new Entry("pad_dpadHorizontal", 39),

                new Entry("kb_blank", 233),
                new Entry("kb_space", 153),
                new Entry("kb_escape", 17),
                new Entry("kb_upArrow", 166),
                new Entry("kb_rightArrow", 167),
                new Entry("kb_downArrow", 168),
                new Entry("kb_leftArrow", 169),
                new Entry("kb_comma", 231),
                new Entry("kb_period", 197),
                new Entry("kb_slash", 165),
                new Entry("kb_backslash", 99),
                new Entry("kb_semicolon", 132),
                new Entry("kb_quote", 129),
                new Entry("kb_minus", 61),
                new Entry("kb_equals", 63),
                new Entry("kb_leftBracket", 95),
                new Entry("kb_rightBracket", 96),
                new Entry("kb_backquote", 30),

                new Entry("kb_enter", 134, 135, Compose.Wide),
                new Entry("kb_backspace", 66, 67, Compose.Wide),
                new Entry("kb_tab", 189, 190, Compose.Wide),
                new Entry("kb_delete", 191, 192, Compose.Wide),
                new Entry("kb_end", 193, 194, Compose.Wide),
                new Entry("kb_home", 225, 226, Compose.Wide),
                new Entry("kb_pageUp", 227, 228, Compose.Wide),
                new Entry("kb_pageDown", 229, 230, Compose.Wide),
                new Entry("kb_capsLock", 223, 224, Compose.Wide),
                new Entry("kb_numLock", 195, 196, Compose.Wide),
                new Entry("kb_insert", 257, 258, Compose.Wide),
                new Entry("kb_printScreen", 259, 260, Compose.Wide),
                new Entry("kb_scrollLock", 261, 262, Compose.Wide),
                new Entry("kb_pause", 263, 264, Compose.Wide),
                new Entry("kb_leftShift", 255, 256, Compose.Wide),
                new Entry("kb_rightShift", 255, 256, Compose.Wide),
                new Entry("kb_leftCtrl", 221, 222, Compose.Wide),
                new Entry("kb_rightCtrl", 221, 222, Compose.Wide),
                new Entry("kb_leftAlt", 187, 188, Compose.Wide),
                new Entry("kb_rightAlt", 187, 188, Compose.Wide),
            };

            AddRow(t, "qwertyuiop", 85);
            AddRow(t, "asdfghjkl", 120);
            AddRow(t, "zxcvbnm", 155);

            AddRow(t, "1234567890", 51);

            for (int i = 0; i < 12; i++)
                t.Add(new Entry($"kb_f{i + 1}", 18 + i));
            return t;
        }

        static void AddRow(List<Entry> t, string keys, int firstTile)
        {
            for (int i = 0; i < keys.Length; i++)
                t.Add(new Entry($"kb_{keys[i]}", firstTile + i));
        }

        [MenuItem("FrogSmashers/Generate Input Prompt Atlas")]
        public static void Generate()
        {
            var source = LoadSource();
            var entries = Table()
                .OrderBy(e => e.name, StringComparer.Ordinal)
                .ToList();
            var dupes = entries.GroupBy(e => e.name)
                .Where(g => g.Count() > 1).Select(g => g.Key)
                .ToList();
            if (dupes.Count > 0)
                throw new Exception(
                    $"Duplicate sprite names: {string.Join(", ", dupes)}");

            int rows = (entries.Count + Cols - 1) / Cols;
            var atlas = new Texture2D(Cols * CellW, rows * Tile,
                TextureFormat.RGBA32, false);
            var clear = new Color32[atlas.width * atlas.height];
            atlas.SetPixels32(clear);

            var rects = new List<(string name, RectInt rect)>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int x = (i % Cols) * CellW;
                int y = atlas.height - ((i / Cols) + 1) * Tile;
                if (e.mode == Compose.PsSymbol)
                {
                    BlitPsButton(atlas, source, e.a, e.b, x, y);
                }
                else
                {
                    Blit(atlas, source, e.a, x, y);
                    if (e.mode == Compose.Wide)
                        Blit(atlas, source, e.b, x + Tile, y);
                }
                int w = e.mode == Compose.Wide ? Tile * 2 : Tile;
                rects.Add((e.name, new RectInt(x, y, w, Tile)));
            }
            atlas.Apply();

            File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            UnityEngine.Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(AtlasPath);
            ConfigureImporter(rects);
            Debug.Log($"[InputPromptAtlas] Wrote {AtlasPath} with " +
                $"{rects.Count} sprites.");
        }

        static Texture2D LoadSource()
        {
            string file = Path.GetFullPath(SourcePath);
            if (!File.Exists(file))
                throw new FileNotFoundException(
                    "Kenney source sheet missing", SourcePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32,
                false);
            if (!tex.LoadImage(File.ReadAllBytes(file)))
                throw new Exception("Could not decode " + SourcePath);
            return tex;
        }

        static void Blit(Texture2D dst, Texture2D src, int tile,
            int dx, int dy)
        {
            var px = TilePixels(src, tile);
            dst.SetPixels(dx, dy, Tile, Tile, px);
        }

        /// <summary>The clean blank light button also used by the
        /// lettered generic-pad tiles; the symbol-bearing PS tiles
        /// have a different (pressed-style) bottom bevel, so they
        /// only contribute their colored symbol pixels.</summary>
        const int BlankButtonTile = 12;

        /// <summary>The pack draws the symbol one pixel above the
        /// button face's visual center; recenter it.</summary>
        const int PsSymbolShiftDown = 1;

        static void BlitPsButton(Texture2D dst, Texture2D src,
            int halfA, int halfB, int dx, int dy)
        {
            var button = TilePixels(src, BlankButtonTile);
            var a = TilePixels(src, halfA);
            var b = TilePixels(src, halfB);
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    int i = y * Tile + x;
                    var pixel = b[i].a > 0f ? b[i]
                        : IsSaturated(a[i]) ? a[i]
                        : default;
                    if (pixel.a <= 0f)
                        continue;
                    int ty = y - PsSymbolShiftDown;
                    if (ty >= 0 && ty < Tile)
                        button[ty * Tile + x] = pixel;
                }
            }
            dst.SetPixels(dx, dy, Tile, Tile, button);
        }

        static bool IsSaturated(Color c)
        {
            float max = Mathf.Max(c.r, c.g, c.b);
            float min = Mathf.Min(c.r, c.g, c.b);
            return c.a > 0f && max - min > 0.15f;
        }

        static Color[] TilePixels(Texture2D src, int tile)
        {
            int row = tile / SheetCols;
            int col = tile % SheetCols;
            int x = col * Stride;
            int y = src.height - row * Stride - Tile;
            return src.GetPixels(x, y, Tile, Tile);
        }

        static void ConfigureImporter(
            List<(string name, RectInt rect)> rects)
        {
            var importer = (TextureImporter)AssetImporter
                .GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory
                .GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var spriteRects = rects.Select(r => new SpriteRect
            {
                name = r.name,
                rect = new Rect(r.rect.x, r.rect.y, r.rect.width,
                    r.rect.height),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = StableGuid(r.name),
            }).ToArray();
            provider.SetSpriteRects(spriteRects);
            var nameIds = provider
                .GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameIds != null)
                nameIds.SetNameFileIdPairs(spriteRects.Select(s =>
                    new SpriteNameFileIdPair(s.name, s.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>Sprite id derived from the name so regeneration
        /// never churns the atlas .meta.</summary>
        static GUID StableGuid(string name)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(
                    Encoding.UTF8.GetBytes("InputPromptAtlas:" + name));
                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++)
                    sb.Append(hash[i].ToString("x2"));
                return new GUID(sb.ToString());
            }
        }
    }
}
