using UnityEngine;

namespace Orbis.UI.Glass
{
    /// <summary>Genera lo sfondo astratto del menu: blu profondo con aloni di colore morbidi (stile iOS 26).</summary>
    internal static class Wallpaper
    {
        private struct Blob
        {
            public Vector2 Center;
            public float Radius;
            public Color Color;
            public float Strength;
        }

        private static readonly Color Top = new Color32(7, 16, 36, 255);
        private static readonly Color Bottom = new Color32(10, 30, 60, 255);

        private static readonly Blob[] Blobs =
        {
            new Blob { Center = new Vector2(0.80f, 0.20f), Radius = 0.55f, Color = new Color32(38, 104, 255, 255), Strength = 0.60f },
            new Blob { Center = new Vector2(0.10f, 0.85f), Radius = 0.50f, Color = new Color32(18, 186, 172, 255), Strength = 0.45f },
            new Blob { Center = new Vector2(0.98f, 0.95f), Radius = 0.42f, Color = new Color32(132, 92, 255, 255), Strength = 0.42f },
            new Blob { Center = new Vector2(0.30f, 0.02f), Radius = 0.34f, Color = new Color32(255, 150, 72, 255), Strength = 0.22f },
            new Blob { Center = new Vector2(0.55f, 0.60f), Radius = 0.30f, Color = new Color32(70, 160, 255, 255), Strength = 0.18f },
        };

        /// <summary>Pixel in ordine Unity (riga 0 = in basso).</summary>
        public static Color32[] Generate(int width, int height, int seed)
        {
            var pixels = new Color32[width * height];
            var aspect = (float)width / height;
            var rng = new System.Random(seed);
            for (var py = 0; py < height; py++)
            {
                var v = 1f - (py + 0.5f) / height; // 0 in alto, 1 in basso
                var baseColor = Color.Lerp(Top, Bottom, v);
                for (var px = 0; px < width; px++)
                {
                    var u = (px + 0.5f) / width;
                    var c = baseColor;
                    for (var i = 0; i < Blobs.Length; i++)
                    {
                        var b = Blobs[i];
                        var dx = (u - b.Center.x) * aspect;
                        var dy = v - b.Center.y;
                        var d2 = (dx * dx + dy * dy) / (b.Radius * b.Radius);
                        var w = Mathf.Exp(-d2 * 1.5f) * b.Strength;
                        // fusione "screen": schiarisce senza bruciare i colori
                        c.r = 1f - (1f - c.r) * (1f - b.Color.r * w);
                        c.g = 1f - (1f - c.g) * (1f - b.Color.g * w);
                        c.b = 1f - (1f - c.b) * (1f - b.Color.b * w);
                    }
                    // vignettatura leggera
                    var cx = u - 0.5f;
                    var cy = v - 0.5f;
                    var vignette = 1f - 0.28f * Mathf.SmoothStep(0.25f, 0.75f, Mathf.Sqrt(cx * cx + cy * cy));
                    // dithering per evitare le bande nei gradienti
                    var n = ((float)rng.NextDouble() - 0.5f) / 255f;
                    pixels[py * width + px] = new Color(
                        Mathf.Clamp01(c.r * vignette + n),
                        Mathf.Clamp01(c.g * vignette + n),
                        Mathf.Clamp01(c.b * vignette + n),
                        1f);
                }
            }
            return pixels;
        }

        public static Color32[] Downsample(Color32[] src, int width, int height, int factor, out int outWidth, out int outHeight)
        {
            outWidth = Mathf.Max(1, width / factor);
            outHeight = Mathf.Max(1, height / factor);
            var dst = new Color32[outWidth * outHeight];
            for (var y = 0; y < outHeight; y++)
            for (var x = 0; x < outWidth; x++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (var sy = 0; sy < factor; sy++)
                for (var sx = 0; sx < factor; sx++)
                {
                    var p = src[(y * factor + sy) * width + x * factor + sx];
                    r += p.r; g += p.g; b += p.b; n++;
                }
                dst[y * outWidth + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
            return dst;
        }

        /// <summary>Schiarisce verso il bianco e aumenta la saturazione (valori sRGB).</summary>
        public static void Vibrancy(Color32[] pixels, float lighten, float saturation)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                var grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                c.r = Mathf.Clamp01(grey + (c.r - grey) * saturation);
                c.g = Mathf.Clamp01(grey + (c.g - grey) * saturation);
                c.b = Mathf.Clamp01(grey + (c.b - grey) * saturation);
                c = Color.Lerp(c, Color.white, lighten);
                c.a = 1f;
                pixels[i] = c;
            }
        }

        /// <summary>Sfocatura a box separabile ripetuta (approssima una gaussiana).</summary>
        public static Color32[] Blur(Color32[] src, int width, int height, int radius, int passes)
        {
            var a = src;
            var b = new Color32[src.Length];
            for (var p = 0; p < passes; p++)
            {
                BoxPass(a, b, width, height, radius, horizontal: true);
                BoxPass(b, a, width, height, radius, horizontal: false);
            }
            return a;
        }

        private static void BoxPass(Color32[] src, Color32[] dst, int width, int height, int radius, bool horizontal)
        {
            var lines = horizontal ? height : width;
            var length = horizontal ? width : height;
            for (var line = 0; line < lines; line++)
            for (var i = 0; i < length; i++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var j = Mathf.Clamp(i + k, 0, length - 1);
                    var p = horizontal ? src[line * width + j] : src[j * width + line];
                    r += p.r; g += p.g; b += p.b; n++;
                }
                var c = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                if (horizontal) dst[line * width + i] = c;
                else dst[i * width + line] = c;
            }
        }
    }
}
