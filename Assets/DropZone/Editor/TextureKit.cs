using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DropZone.EditorTools
{
    /// <summary>Procedural textures (brick, slate, tarmac, paving, grass, newsprint…), saved as PNG assets.</summary>
    public static class TextureKit
    {
        public const string Dir = "Assets/DropZone/Generated/Textures/";

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        /// <summary>Smooth value noise, tileable with period `period`.</summary>
        public static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float u, float v, int baseFreq, int seed, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int f = baseFreq;
            for (int i = 0; i < octaves; i++) { sum += Noise(u * f, v * f, f, seed + i) * amp; norm += amp; amp *= 0.5f; f *= 2; }
            return sum / norm;
        }

        public static Texture2D Save(string name, int size, Func<float, float, Color> f, bool alpha = false, int height = 0)
        {
            Directory.CreateDirectory(Dir);
            int h = height > 0 ? height : size;
            var t = new Texture2D(size, h, TextureFormat.RGBA32, false);
            var px = new Color[size * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / h);
            t.SetPixels(px);
            t.Apply();
            string path = Dir + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.alphaIsTransparency = alpha;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A soft round blob (particles, glows).</summary>
        public static Texture2D Soft(string name) => Save(name, 64, (u, v) =>
        {
            float d = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3 - 2 * a);
            return new Color(1, 1, 1, a);
        }, true);

        public static Texture2D Square(string name) => Save(name, 16, (u, v) => Color.white, true);

        /// <summary>A crisp ring with a soft outer glow (selection highlights).</summary>
        public static Texture2D HighlightRing(string name) => Save(name, 256, (u, v) =>
        {
            float d = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.05f);
            float glow = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.16f) * 0.45f;
            return new Color(1, 1, 1, Mathf.Max(ring, glow));
        }, true);

        /// <summary>Thin expanding shockwave ring.</summary>
        public static Texture2D Shock(string name) => Save(name, 128, (u, v) =>
        {
            float d = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.12f);
            return new Color(1, 1, 1, a * a);
        }, true);

        /// <summary>Vertical beam: bright at the top, fading down and at the sides.</summary>
        public static Texture2D Beam(string name) => Save(name, 64, (u, v) =>
        {
            float side = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2f);
            side = side * side * (3 - 2 * side);
            float fade = Mathf.Lerp(0.25f, 1f, v);
            return new Color(1, 1, 1, side * fade);
        }, true, 256);

        /// <summary>Arena floor: dark tiles with fine glowing seams (emission map uses the same texture).</summary>
        public static Texture2D FloorGrid(string name) => Save(name, 512, (u, v) =>
        {
            float gu = Mathf.Abs(u * 4f - Mathf.Round(u * 4f)), gv = Mathf.Abs(v * 4f - Mathf.Round(v * 4f));
            float line = Mathf.Clamp01(1f - Mathf.Min(gu, gv) / 0.012f);
            float n = Fbm(u, v, 8, 21) * 0.06f;
            float b = 0.07f + n;
            var tile = new Color(b * 0.9f, b * 0.92f, b * 1.25f);
            return Color.Lerp(tile, new Color(0.55f, 0.45f, 1f), line);
        });

        /// <summary>Brushed dark metal for the board.</summary>
        public static Texture2D Metal(string name) => Save(name, 256, (u, v) =>
        {
            float streak = Noise(u * 4f, v * 96f, 4, 31) * 0.5f + Noise(u * 8f, v * 160f, 8, 32) * 0.3f;
            float b = 0.75f + streak * 0.25f;
            return new Color(b, b, b * 1.03f);
        });

        /// <summary>Panel lines for the arena walls.</summary>
        public static Texture2D WallPanel(string name) => Save(name, 256, (u, v) =>
        {
            float gu = Mathf.Abs(u * 2f - Mathf.Round(u * 2f)), gv = Mathf.Abs(v * 3f - Mathf.Round(v * 3f));
            float seam = Mathf.Clamp01(1f - Mathf.Min(gu, gv) / 0.02f);
            float n = 0.8f + Fbm(u, v, 4, 41) * 0.2f;
            return new Color(n * (1 - seam * 0.6f), n * (1 - seam * 0.6f), n * (1 - seam * 0.5f));
        });
    }
}
