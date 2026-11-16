using System;
using System.Collections.Generic;
using Orbis.UI.Menu3D;
using UnityEditor;
using UnityEngine;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Asset della città del menu: un materiale a tinta unita per ogni <see cref="MenuCityMaterial"/>,
    /// il cielo procedurale e il materiale di sfocatura del vetro. Ritocca anche ombre e antialiasing URP.
    /// </summary>
    public static class MenuAssetsGenerator
    {
        public const string Folder = "Assets/Orbis/Rendering/Menu";
        public const string SkyMaterial = Folder + "/Sky.mat";
        public const string BlurMaterial = Folder + "/GlassBlur.mat";
        public const string BlurShader = "Hidden/Orbis/GlassBlur";

        // colori sRGB: palette calda "mediterranea"
        private static readonly Dictionary<MenuCityMaterial, Color32> Colors = new Dictionary<MenuCityMaterial, Color32>
        {
            { MenuCityMaterial.Grass, new Color32(126, 189, 82, 255) },
            { MenuCityMaterial.GrassDark, new Color32(84, 148, 69, 255) },
            { MenuCityMaterial.FieldYellow, new Color32(237, 204, 97, 255) },
            { MenuCityMaterial.FieldGreen, new Color32(168, 204, 92, 255) },
            { MenuCityMaterial.Earth, new Color32(148, 102, 66, 255) },
            { MenuCityMaterial.Rock, new Color32(117, 107, 102, 255) },
            { MenuCityMaterial.Road, new Color32(92, 92, 102, 255) },
            { MenuCityMaterial.WallCream, new Color32(247, 230, 194, 255) },
            { MenuCityMaterial.WallOchre, new Color32(242, 189, 112, 255) },
            { MenuCityMaterial.WallSalmon, new Color32(242, 158, 128, 255) },
            { MenuCityMaterial.WallWhite, new Color32(250, 247, 240, 255) },
            { MenuCityMaterial.Roof, new Color32(209, 92, 56, 255) },
            { MenuCityMaterial.RoofDark, new Color32(168, 69, 46, 255) },
            { MenuCityMaterial.Stone, new Color32(224, 209, 178, 255) },
            { MenuCityMaterial.Tree, new Color32(61, 148, 71, 255) },
            { MenuCityMaterial.TreeLight, new Color32(115, 184, 71, 255) },
            { MenuCityMaterial.Trunk, new Color32(117, 79, 51, 255) },
            { MenuCityMaterial.Cloud, new Color32(255, 255, 255, 255) },
            { MenuCityMaterial.CarRed, new Color32(235, 61, 51, 255) },
            { MenuCityMaterial.CarBlue, new Color32(51, 122, 242, 255) },
            { MenuCityMaterial.CarYellow, new Color32(255, 204, 46, 255) },
            { MenuCityMaterial.Pitch, new Color32(87, 184, 82, 255) },
            { MenuCityMaterial.Stands, new Color32(219, 224, 237, 255) },
            { MenuCityMaterial.Water, new Color32(64, 156, 222, 255) },
            { MenuCityMaterial.Mountain, new Color32(120, 140, 128, 255) },
            { MenuCityMaterial.Snow, new Color32(245, 248, 255, 255) },
        };

        /// <summary>Crea o aggiorna gli asset e restituisce i percorsi dei materiali della città (in ordine di enum).</summary>
        public static string[] Generate()
        {
            LocalizationGenerator.EnsureFolder(Folder + "/Materials");
            var names = Enum.GetNames(typeof(MenuCityMaterial));
            if (names.Length != MenuCity.MaterialCount)
                throw new InvalidOperationException("MenuCity.MaterialCount non corrisponde all'enum MenuCityMaterial");

            var simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? throw new InvalidOperationException("Shader URP Simple Lit non trovato");
            var result = new string[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var key = (MenuCityMaterial)i;
                var path = $"{Folder}/Materials/City_{names[i]}.mat";
                var mat = LoadOrCreate(path, simpleLit);
                mat.SetColor("_BaseColor", Colors[key]);
                mat.SetFloat("_Smoothness", 0.1f);
                // in Simple Lit 1 = "nessun riflesso" (enum SpecularSource); le keyword le aggiorna URP
                mat.SetFloat("_SpecularHighlights", 1f);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                result[i] = path;
            }

            var sky = LoadOrCreate(SkyMaterial, Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.8f);
            sky.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.95f));
            // sotto l'orizzonte lo stesso azzurro della nebbia: mare e cielo si fondono
            sky.SetColor("_GroundColor", new Color(0.70f, 0.82f, 0.95f));
            sky.SetFloat("_Exposure", 1.3f);
            EditorUtility.SetDirty(sky);

            var blurShader = Shader.Find(BlurShader) ?? throw new InvalidOperationException("Shader " + BlurShader + " non trovato");
            var blur = LoadOrCreate(BlurMaterial, blurShader);
            blur.SetFloat("_Lighten", 0.06f);
            blur.SetFloat("_Saturation", 1.35f);
            EditorUtility.SetDirty(blur);

            TuneRenderPipelines();
            AssetDatabase.SaveAssets();
            return result;
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        /// <summary>Ombre fino a 150 m (la città è a 60–100 m dalla telecamera) e MSAA 4x per i bordi low-poly.</summary>
        private static void TuneRenderPipelines()
        {
            foreach (var path in new[] { "Assets/Orbis/Rendering/Settings/Mobile_RPAsset.asset", "Assets/Orbis/Rendering/Settings/PC_RPAsset.asset" })
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    continue;
                var so = new SerializedObject(asset);
                Set(so, "m_ShadowDistance", 150f);
                Set(so, "m_MSAA", 4);
                Set(so, "m_SoftShadowsSupported", 1);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void Set(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
            else p.intValue = Mathf.RoundToInt(value);
        }
    }
}
