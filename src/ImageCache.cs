using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

namespace TestMode1
{
    public class ImageCache
    {
        public enum ItemType { Card, Relic, Potion }

        public static ImageCache Instance { get; private set; }

        private readonly string _basePath;
        private readonly Dictionary<string, Texture2D> _texCache = new();
        private readonly Dictionary<string, Image>     _atlasCache = new();

        private static readonly string[] CardCharFolders =
        {
            "ironclad", "silent", "defect", "necrobinder", "regent",
            "colorless", "curse", "status", "event", "token", "quest"
        };

        public ImageCache(string basePath)
        {
            _basePath = basePath;
            Instance  = this;
        }

        public Texture2D GetTexture(string displayName, ItemType type)
        {
            var key = $"{type}:{displayName}";
            if (_texCache.TryGetValue(key, out var cached)) return cached;

            var tex = type switch
            {
                ItemType.Relic  => LoadDirect(Path.Combine("images", "relics",  StripPrefix(displayName, "relic_")  + ".png")),
                ItemType.Potion => LoadDirect(Path.Combine("images", "potions", StripPrefix(displayName, "potion_") + ".png")),
                ItemType.Card   => LoadCard(displayName),
                _               => null
            };

            if (tex != null)
                _texCache[key] = tex;
            else
                GD.PrintErr($"[ImageCache] No texture: {key}");

            return tex;
        }

        public static string ToSnakeCase(string name) =>
            Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

        private static string StripPrefix(string key, string prefix) =>
            key.StartsWith(prefix) ? key[prefix.Length..] : key;

        private Texture2D LoadDirect(string relativePath)
        {
            var full = Path.Combine(_basePath, relativePath);
            if (!File.Exists(full))
            {
                GD.PrintErr($"[ImageCache] Not found: {full}");
                return null;
            }
            try
            {
                var bytes = File.ReadAllBytes(full);
                var img   = new Image();
                var err   = img.LoadPngFromBuffer(bytes);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[ImageCache] LoadPng failed ({err}): {full}");
                    return null;
                }
                return ImageTexture.CreateFromImage(img);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[ImageCache] Exception {full}: {e.Message}");
                return null;
            }
        }

        private Texture2D LoadCard(string displayName)
        {
            var snake       = StripPrefix(displayName, "card_");
            var spritesRoot = Path.Combine(_basePath, "images", "atlases", "card_atlas.sprites");

            foreach (var ch in CardCharFolders)
            {
                var tresPath = Path.Combine(spritesRoot, ch, snake + ".tres");
                if (!File.Exists(tresPath)) continue;

                var content = File.ReadAllText(tresPath);
                if (!ParseAtlasRegion(content, out var atlasFile, out var region)) continue;

                var atlasPath = Path.Combine(_basePath, "images", "atlases", atlasFile);
                var atlas     = GetOrLoadAtlas(atlasPath);
                if (atlas == null) continue;

                var cropped = atlas.GetRegion(region);
                return ImageTexture.CreateFromImage(cropped);
            }
            GD.PrintErr($"[ImageCache] No .tres for card: {snake}");
            return null;
        }

        private Image GetOrLoadAtlas(string path)
        {
            if (_atlasCache.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path))
            {
                GD.PrintErr($"[ImageCache] Atlas not found: {path}");
                return null;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                var img   = new Image();
                var err   = img.LoadPngFromBuffer(bytes);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[ImageCache] Atlas LoadPng failed ({err}): {path}");
                    return null;
                }
                _atlasCache[path] = img;
                return img;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[ImageCache] Atlas exception {path}: {e.Message}");
                return null;
            }
        }

        private static bool ParseAtlasRegion(string content, out string atlasFile, out Rect2I region)
        {
            atlasFile = null;
            region    = default;

            var pathMatch = Regex.Match(content, @"path=""res://images/atlases/([^""]+)""");
            if (!pathMatch.Success) return false;
            atlasFile = pathMatch.Groups[1].Value;

            var rectMatch = Regex.Match(content,
                @"region\s*=\s*Rect2\(\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\s*\)");
            if (!rectMatch.Success) return false;
            region = new Rect2I(
                int.Parse(rectMatch.Groups[1].Value),
                int.Parse(rectMatch.Groups[2].Value),
                int.Parse(rectMatch.Groups[3].Value),
                int.Parse(rectMatch.Groups[4].Value));
            return true;
        }
    }
}
