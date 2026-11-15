using System;
using UnityEngine;

namespace Orbis.UI.Glass
{
    /// <summary>
    /// Sfondo condiviso dalle superfici "liquid glass": una versione nitida (disegnata a tutto schermo)
    /// e una sfocata (campionata dal vetro nel punto in cui si trova). Le due immagini sono generate
    /// a runtime, quindi niente asset da scaricare. Da M1 la versione sfocata diventerà la vista 3D sfocata.
    /// </summary>
    public static class GlassBackdrop
    {
        private const int Width = 512;
        private const int Height = 288;
        private const int BlurDownsample = 4;

        public static Texture2D Sharp { get; private set; }
        public static Texture2D Blurred { get; private set; }

        /// <summary>Area dello sfondo in coordinate del pannello UI.</summary>
        public static Rect Area { get; private set; }

        /// <summary>Lanciato quando l'area dello sfondo cambia (resize, rotazione).</summary>
        public static event Action Changed;

        public static void EnsureTextures()
        {
            if (Sharp != null && Blurred != null)
                return;
            var pixels = Wallpaper.Generate(Width, Height, seed: 1907);
            Sharp = CreateTexture("OrbisBackdrop", Width, Height, pixels);
            var small = Wallpaper.Blur(Wallpaper.Downsample(pixels, Width, Height, BlurDownsample, out var w, out var h), w, h, radius: 3, passes: 3);
            Blurred = CreateTexture("OrbisBackdropBlurred", w, h, small);
        }

        internal static void SetArea(Rect area)
        {
            if (area == Area)
                return;
            Area = area;
            Changed?.Invoke();
        }

        /// <summary>UV della texture di sfondo nel punto <paramref name="panelPoint"/> (riempimento "cover").</summary>
        public static Vector2 UvAt(Vector2 panelPoint)
        {
            var area = Area;
            if (area.width <= 0f || area.height <= 0f)
                return new Vector2(0.5f, 0.5f);
            var nx = (panelPoint.x - area.x) / area.width;
            var ny = (panelPoint.y - area.y) / area.height;
            var elementAspect = area.width / area.height;
            var textureAspect = (float)Width / Height;
            float sx = 1f, sy = 1f;
            if (elementAspect > textureAspect) sy = textureAspect / elementAspect;
            else sx = elementAspect / textureAspect;
            return new Vector2(0.5f + (nx - 0.5f) * sx, 0.5f - (ny - 0.5f) * sy);
        }

        private static Texture2D CreateTexture(string name, int w, int h, Color32[] pixels)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
