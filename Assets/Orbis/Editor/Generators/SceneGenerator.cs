using Orbis.UI.App;
using Orbis.UI.Menu3D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Genera la scena di avvio (Boot): telecamera che gira attorno alla città 3D del menu, sole, cielo,
    /// EventSystem (touch + mouse/tastiera) e documento UI.
    /// Se la scena esiste ed è già corretta non la riscrive (gli ID interni cambierebbero a ogni build).
    /// </summary>
    public static class SceneGenerator
    {
        private const string CameraName = "Main Camera";
        private const string SunName = "Sole";
        private const string CityName = "Città del menu";
        private const string EventSystemName = "EventSystem";
        private const string AppName = "Orbis";

        [MenuItem("Orbis/Rigenera scena di avvio")]
        public static void Regenerate() => Generate(MenuAssetsGenerator.Generate(), force: true);

        public static void Generate(string[] cityMaterialPaths, bool force = false)
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(OrbisPaths.BootScene, true) };
            if (!force && IsUpToDate())
                return;

            LocalizationGenerator.EnsureFolder(System.IO.Path.GetDirectoryName(OrbisPaths.BootScene)?.Replace('\\', '/'));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // gli asset si caricano DOPO NewScene: creare la scena scarica gli oggetti non referenziati
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(OrbisPaths.PanelSettings);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OrbisPaths.AppUxml);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(MenuAssetsGenerator.SkyMaterial);
            var blur = AssetDatabase.LoadAssetAtPath<Material>(MenuAssetsGenerator.BlurMaterial);
            if (panelSettings == null || uxml == null || sky == null || blur == null)
                throw new System.InvalidOperationException("Mancano asset UI o del menu: generarli prima della scena.");
            var cityMaterials = new Material[cityMaterialPaths.Length];
            for (var i = 0; i < cityMaterialPaths.Length; i++)
                cityMaterials[i] = AssetDatabase.LoadAssetAtPath<Material>(cityMaterialPaths[i])
                                   ?? throw new System.InvalidOperationException("Materiale mancante: " + cityMaterialPaths[i]);

            // cielo, luce ambiente e nebbia
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.74f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.80f, 0.76f, 0.70f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.36f, 0.30f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.82f, 0.95f);
            RenderSettings.fogStartDistance = 130f;
            RenderSettings.fogEndDistance = 380f;

            var sunGo = new GameObject(SunName);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sunGo.transform.rotation = Quaternion.Euler(46f, 58f, 0f);
            RenderSettings.sun = sun;

            var cityGo = new GameObject(CityName);
            var city = cityGo.AddComponent<MenuCity>();
            SetArray(city, "materials", cityMaterials);

            var cameraGo = new GameObject(CameraName) { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = ~0;
            camera.nearClipPlane = 0.5f;
            camera.fieldOfView = 34f;
            var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraGo.AddComponent<MenuCameraRig>();
            var glassBlur = cameraGo.AddComponent<GlassBlurCapture>();
            SetReference(glassBlur, "blurMaterial", blur);

            var eventSystemGo = new GameObject(EventSystemName);
            eventSystemGo.AddComponent<EventSystem>();
            // senza azioni assegnate il modulo usa quelle predefinite (touch, mouse, tastiera, gamepad)
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            var appGo = new GameObject(AppName);
            var document = appGo.AddComponent<UIDocument>();
            // assegnazione sui campi serializzati: la proprietà panelSettings in batchmode non viene salvata nella scena
            SetReference(document, "m_PanelSettings", panelSettings);
            SetReference(document, "sourceAsset", uxml);
            var app = appGo.AddComponent<OrbisApp>();
            SetReference(app, "menuCity", city);
            SetReference(app, "glassBlur", glassBlur);

            SceneManager.SetActiveScene(scene);
            if (!EditorSceneManager.SaveScene(scene, OrbisPaths.BootScene))
                throw new System.IO.IOException("Salvataggio della scena fallito: " + OrbisPaths.BootScene);
            Debug.Log("[Orbis] Scena di avvio generata.");
        }

        private static void SetReference(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property) ?? throw new System.InvalidOperationException($"Campo '{property}' non trovato in {target.GetType().Name}");
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string property, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property) ?? throw new System.InvalidOperationException($"Campo '{property}' non trovato in {target.GetType().Name}");
            p.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
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

            var camera = Find(CameraName);
            var eventSystem = Find(EventSystemName);
            var app = Find(AppName);
            var city = Find(CityName);
            var sun = Find(SunName);
            if (roots.Length != 5 || camera == null || eventSystem == null || app == null || city == null || sun == null)
                return false;
            var document = app.GetComponent<UIDocument>();
            var orbisApp = app.GetComponent<OrbisApp>();
            var cityComponent = city.GetComponent<MenuCity>();
            if (orbisApp == null || cityComponent == null || document == null)
                return false;
            var appSo = new SerializedObject(orbisApp);
            var citySo = new SerializedObject(cityComponent).FindProperty("materials");
            return camera.GetComponent<MenuCameraRig>() != null
                   && camera.GetComponent<GlassBlurCapture>() != null
                   && camera.GetComponent<UniversalAdditionalCameraData>() != null
                   && eventSystem.GetComponent<InputSystemUIInputModule>() != null
                   && sun.GetComponent<Light>() != null
                   && document.panelSettings == panelSettings
                   && document.visualTreeAsset == uxml
                   && appSo.FindProperty("menuCity").objectReferenceValue == cityComponent
                   && appSo.FindProperty("glassBlur").objectReferenceValue != null
                   && citySo != null && citySo.arraySize == MenuCity.MaterialCount
                   && RenderSettings.skybox != null;
        }
    }
}
