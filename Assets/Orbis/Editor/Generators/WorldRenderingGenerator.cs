using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Resa più realistica del mondo (stile Classico, docs/08 §3): profilo di post-processing
    /// (tonemapping filmico, bloom leggero, colori) usato da un Volume globale nella scena, e ombre del profilo
    /// Mobile (iPhone, Android, web) più definite. Il Volume nella scena tiene gli shader di post-processing
    /// dentro le build (altrimenti URP li toglie).
    /// </summary>
    public static class WorldRenderingGenerator
    {
        public const string ProfilePath = "Assets/Orbis/Rendering/Settings/WorldVolumeProfile.asset";

        public static VolumeProfile Generate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);
            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0.35f);
            color.contrast.Override(8f);
            color.saturation.Override(6f);
            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.28f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);
            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.16f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);

            // ombre del profilo Mobile: 2 cascate da 2048 (prima 1 da 1024, sfocate a distanza)
            var mobile = AssetDatabase.LoadMainAssetAtPath("Assets/Orbis/Rendering/Settings/Mobile_RPAsset.asset");
            if (mobile != null)
            {
                var so = new SerializedObject(mobile);
                SetInt(so, "m_ShadowCascadeCount", 2);
                SetInt(so, "m_MainLightShadowmapResolution", 2048);
                SetInt(so, "m_SoftShadowsSupported", 1);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var c))
            {
                c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            c.active = true;
            return c;
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }
    }
}
