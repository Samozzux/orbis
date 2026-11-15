using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.EditorTools
{
    /// <summary>Crea le impostazioni del pannello UI Toolkit (scala gestita a runtime da AdaptiveLayout).</summary>
    public static class UiAssetsGenerator
    {
        public static void Generate()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(OrbisPaths.Theme);
            if (theme == null)
                throw new System.IO.FileNotFoundException("Tema UI mancante", OrbisPaths.Theme);

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(OrbisPaths.PanelSettings);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, OrbisPaths.PanelSettings);
            }
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.sortingOrder = 0;
            settings.clearColor = false;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
