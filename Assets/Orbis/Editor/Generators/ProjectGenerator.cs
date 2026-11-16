using System;
using UnityEditor;
using UnityEngine;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Genera da codice tutto ciò che non si scrive a mano (CLAUDE.md, "Unity senza Editor"):
    /// impostazioni del player, icona, tabelle Localization, impostazioni UI, scena di avvio.
    /// È idempotente: si può rilanciare quante volte si vuole. Lo esegue anche ogni build.
    /// Riga di comando: -executeMethod Orbis.EditorTools.ProjectGenerator.GenerateFromCommandLine
    /// </summary>
    public static class ProjectGenerator
    {
        [MenuItem("Orbis/Genera progetto")]
        public static void GenerateAll()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PlayerSettingsConfigurator.Apply();
            IconGenerator.Generate();
            LocalizationGenerator.Generate();
            WebTemplateConfigurator.Apply();
            UiAssetsGenerator.Generate();
            SceneGenerator.Generate();
            AssetDatabase.SaveAssets();
            Debug.Log("[Orbis] Progetto generato.");
        }

        public static void GenerateFromCommandLine()
        {
            try
            {
                GenerateAll();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
