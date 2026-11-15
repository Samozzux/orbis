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
    /// <summary>Genera la scena di avvio (Boot): camera, EventSystem (touch + mouse/tastiera) e documento UI.</summary>
    public static class SceneGenerator
    {
        public static void Generate()
        {
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

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(OrbisPaths.BootScene, true) };
        }
    }
}
