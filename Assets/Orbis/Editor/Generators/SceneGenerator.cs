using Orbis.UI.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Genera la scena di avvio (Boot): camera, EventSystem (touch + mouse/tastiera) e documento UI.
    /// Se la scena esiste ed è già corretta non la riscrive (gli ID interni cambierebbero a ogni build).
    /// </summary>
    public static class SceneGenerator
    {
        [MenuItem("Orbis/Rigenera scena di avvio")]
        public static void Regenerate() => Generate(force: true);

        public static void Generate(bool force = false)
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(OrbisPaths.BootScene, true) };
            if (!force && IsUpToDate())
                return;

            LocalizationGenerator.EnsureFolder(System.IO.Path.GetDirectoryName(OrbisPaths.BootScene)?.Replace('\\', '/'));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // gli asset si caricano DOPO NewScene: creare la scena scarica gli oggetti non referenziati
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(OrbisPaths.PanelSettings);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OrbisPaths.AppUxml);
            if (panelSettings == null || uxml == null)
                throw new System.InvalidOperationException("Mancano PanelSettings o App.uxml: generare prima gli asset UI.");

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(7, 16, 36, 255);
            camera.cullingMask = 0; // nel menu disegna solo la UI
            camera.orthographic = true;
            var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            // senza azioni assegnate il modulo usa quelle predefinite (touch, mouse, tastiera, gamepad)
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            var appGo = new GameObject("Orbis");
            var document = appGo.AddComponent<UIDocument>();
            // assegnazione sui campi serializzati: la proprietà panelSettings in batchmode non viene salvata nella scena
            var so = new SerializedObject(document);
            so.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
            so.FindProperty("sourceAsset").objectReferenceValue = uxml;
            so.ApplyModifiedPropertiesWithoutUndo();
            appGo.AddComponent<OrbisApp>();

            SceneManager.SetActiveScene(scene);
            if (!EditorSceneManager.SaveScene(scene, OrbisPaths.BootScene))
                throw new System.IO.IOException("Salvataggio della scena fallito: " + OrbisPaths.BootScene);
            Debug.Log("[Orbis] Scena di avvio generata.");
        }

        /// <summary>Controlla struttura e riferimenti della scena salvata.</summary>
        private static bool IsUpToDate()
        {
            if (!System.IO.File.Exists(OrbisPaths.BootScene))
                return false;
            var scene = EditorSceneManager.OpenScene(OrbisPaths.BootScene, OpenSceneMode.Single);
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(OrbisPaths.PanelSettings);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OrbisPaths.AppUxml);
            var roots = scene.GetRootGameObjects();
            GameObject Find(string name) => System.Array.Find(roots, g => g.name == name);

            var camera = Find("Main Camera");
            var eventSystem = Find("EventSystem");
            var app = Find("Orbis");
            if (roots.Length != 3 || camera == null || eventSystem == null || app == null)
                return false;
            var document = app.GetComponent<UIDocument>();
            return camera.GetComponent<Camera>() != null
                   && camera.GetComponent<UniversalAdditionalCameraData>() != null
                   && eventSystem.GetComponent<InputSystemUIInputModule>() != null
                   && app.GetComponent<OrbisApp>() != null
                   && document != null
                   && document.panelSettings == panelSettings
                   && document.visualTreeAsset == uxml;
        }
    }
}
