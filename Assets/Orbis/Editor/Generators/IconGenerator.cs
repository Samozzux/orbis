using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Disegna l'icona dell'app (globo con orbita su fondo blu, come il logo del menu) e la imposta
    /// per tutte le piattaforme; esporta anche le icone della pagina web. Nessun asset esterno.
    /// </summary>
    public static class IconGenerator
    {
        private static readonly Color BgTop = new Color32(16, 58, 128, 255);
        private static readonly Color BgBottom = new Color32(6, 16, 40, 255);
        private static readonly Color Line = new Color32(255, 255, 255, 255);
        private static readonly Color Accent = new Color32(100, 210, 255, 255);

        public static void Generate()
        {
            LocalizationGenerator.EnsureFolder(OrbisPaths.BrandingDir);
            WritePng(OrbisPaths.AppIcon, Render(1024));
            AssetDatabase.ImportAsset(OrbisPaths.AppIcon, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OrbisPaths.AppIcon);
            if (importer != null && (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled))
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(OrbisPaths.AppIcon);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            // icone della pagina web (Aggiungi a schermata Home, favicon)
            Directory.CreateDirectory(OrbisPaths.WebTemplateData);
            WritePng(OrbisPaths.WebTemplateData + "/icon-512.png", Render(512));
            WritePng(OrbisPaths.WebTemplateData + "/icon-192.png", Render(192));
            WritePng(OrbisPaths.WebTemplateData + "/apple-touch-icon.png", Render(180));
            WritePng(OrbisPaths.WebTemplateData + "/favicon.png", Render(64));
        }

        private static void WritePng(string path, Texture2D tex)
        {
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return; // identica: non toccare il file (niente modifiche inutili in git)
            File.WriteAllBytes(path, bytes);
        }

        public static Texture2D Render(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var s = (float)size;
            var px = 1f / s; // un pixel in coordinate normalizzate
            var radius = 0.30f;
            var lineW = 0.028f;
            var tilt = 0.38f;

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // coordinate centrate, y verso l'alto
                var u = (x + 0.5f) / s - 0.5f;
                var v = (y + 0.5f) / s - 0.5f;
                var t = (y + 0.5f) / s;
                var c = Color.Lerp(BgBottom, BgTop, t * t);
                // alone dietro il globo
                var glow = Mathf.Exp(-(u * u + v * v) / 0.06f) * 0.35f;
                c = Color.Lerp(c, new Color(0.25f, 0.55f, 1f), glow);

                var r = Mathf.Sqrt(u * u + v * v);
                var inside = r < radius;

                // orbita posteriore (solo fuori dal disco)
                var orbit = OrbitDistance(u, v, radius, out var front);
                if (!front && !inside) c = Blend(c, Accent, Coverage(orbit, lineW * 0.8f, px) * 0.45f);

                if (inside)
                {
                    c = Color.Lerp(c, Color.white, 0.06f);
                    // paralleli e meridiani del globo (inclinato come nel logo)
                    var z = Mathf.Sqrt(Mathf.Max(0f, radius * radius - u * u - v * v)) / radius;
                    var nx = u / radius;
                    var ny = v / radius;
                    // ruota il punto sulla sfera all'indietro dell'inclinazione
                    var y0 = ny * Mathf.Cos(tilt) + z * Mathf.Sin(tilt);
                    var z0 = -ny * Mathf.Sin(tilt) + z * Mathf.Cos(tilt);
                    var lat = Mathf.Asin(Mathf.Clamp(y0, -1f, 1f));
                    var lon = Mathf.Atan2(nx, z0);
                    var gridAlpha = 0.35f + 0.65f * z;
                    var latStep = Mathf.PI / 6f;
                    var dLat = Mathf.Abs(Mathf.Repeat(lat + latStep * 0.5f, latStep) - latStep * 0.5f) * radius;
                    var dLon = Mathf.Abs(Mathf.Repeat(lon + latStep * 0.5f, latStep) - latStep * 0.5f) * radius * Mathf.Cos(lat);
                    var grid = Mathf.Max(Coverage(dLat, lineW * 0.45f, px), Coverage(dLon, lineW * 0.45f, px));
                    c = Blend(c, Line, grid * 0.45f * gridAlpha);
                }

                // contorno del globo
                c = Blend(c, Line, Coverage(Mathf.Abs(r - radius), lineW * 0.5f, px));
                // orbita anteriore
                if (front) c = Blend(c, Accent, Coverage(orbit, lineW * 0.8f, px));
                // satellite
                var sat = new Vector2(0.355f, 0.047f); // sul ramo anteriore dell'orbita
                var ds = Vector2.Distance(new Vector2(u, v), sat);
                c = Blend(c, Accent, Coverage(ds, lineW * 1.1f, px));

                c.a = 1f;
                pixels[y * size + x] = c;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Distanza approssimata dall'ellisse dell'orbita (inclinata); front = metà davanti al globo.</summary>
        private static float OrbitDistance(float u, float v, float radius, out bool front)
        {
            const float rot = 0.32f;
            var a = radius * 1.42f;
            var b = radius * 0.40f;
            var cos = Mathf.Cos(rot);
            var sin = Mathf.Sin(rot);
            var x = u * cos + v * sin;
            var y = -u * sin + v * cos;
            front = y < 0f; // metà bassa = davanti
            var g = x * x / (a * a) + y * y / (b * b) - 1f;
            var gx = 2f * x / (a * a);
            var gy = 2f * y / (b * b);
            return Mathf.Abs(g) / Mathf.Max(1e-5f, Mathf.Sqrt(gx * gx + gy * gy));
        }

        private static float Coverage(float distance, float halfWidth, float px) =>
            Mathf.Clamp01((halfWidth - distance) / px + 0.5f);

        private static Color Blend(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));
    }
}
