using System;
using System.Collections.Generic;
using Orbis.Core.Game;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Anteprime 3D degli elementi del catalogo (docs/04 §4): ogni modello procedurale viene costruito fuori scena e
    /// fotografato da una telecamera dedicata in una piccola immagine, sempre con la luce del giorno.
    /// Le foto si fanno poche alla volta (due per frame) e restano in memoria per la partita.
    /// </summary>
    public sealed class CatalogPreviews : MonoBehaviour
    {
        public const int Width = 256, Height = 176;
        private const int PreviewLayer = 31;
        private static readonly Vector3 Stage = new Vector3(0, -20000f, 0);

        private GameController _game;
        private Camera _cam;
        private readonly Dictionary<string, RenderTexture> _cache = new Dictionary<string, RenderTexture>();
        private readonly Dictionary<string, List<Action<Texture>>> _waiting = new Dictionary<string, List<Action<Texture>>>();
        private readonly Queue<CatalogItem> _queue = new Queue<CatalogItem>();
        private readonly ObjectMesher _mesher = new ObjectMesher();
        private readonly MeshBuffer _mb = new MeshBuffer();
        private readonly List<PlantInstance> _plants = new List<PlantInstance>();

        /// <summary>Le anteprime si possono fare (non nei test senza GPU).</summary>
        public static bool Supported => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        public int Rendered => _cache.Count;

        public static CatalogPreviews Attach(GameController game)
        {
            var p = game.gameObject.AddComponent<CatalogPreviews>();
            p._game = game;
            return p;
        }

        /// <summary>Immagine dell'elemento: subito se è già pronta, altrimenti null e <paramref name="ready"/> quando lo sarà.</summary>
        public Texture Get(CatalogItem item, Action<Texture> ready)
        {
            if (item == null || !Supported) return null;
            if (_cache.TryGetValue(item.Key, out var rt) && rt != null) return rt;
            if (!_waiting.TryGetValue(item.Key, out var list))
            {
                _waiting[item.Key] = list = new List<Action<Texture>>();
                _queue.Enqueue(item);
            }
            if (ready != null) list.Add(ready);
            return null;
        }

        private void LateUpdate()
        {
            // al massimo ~3 ms per frame (almeno una foto), così aprire il catalogo resta fluido
            float start = Time.realtimeSinceStartup;
            for (int n = 0; _queue.Count > 0 && (n == 0 || Time.realtimeSinceStartup - start < 0.003f); n++)
            {
                var item = _queue.Dequeue();
                RenderTexture rt = null;
                try
                {
                    rt = Render(item);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Orbis] anteprima non riuscita per " + item.Key + ": " + e.Message);
                }
                _cache[item.Key] = rt;
                if (_waiting.TryGetValue(item.Key, out var list))
                {
                    _waiting.Remove(item.Key);
                    if (rt != null)
                        foreach (var cb in list) cb(rt);
                }
            }
        }

        private void EnsureCamera()
        {
            if (_cam != null) return;
            var go = new GameObject("Anteprime catalogo");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.cullingMask = 1 << PreviewLayer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.70f, 0.83f, 0.95f, 1f);
            _cam.fieldOfView = 28f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 3000f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
        }

        private RenderTexture Render(CatalogItem item)
        {
            EnsureCamera();
            var world = _game.World.World;
            var root = new GameObject("Anteprima " + item.Key) { layer = PreviewLayer };
            root.transform.position = Stage;
            var meshes = new List<Mesh>();
            try
            {
                // il modello con le misure vere, sul terreno piano a quota 0
                _mb.Clear();
                _plants.Clear();
                _mesher.Build(item, 1, null, new Vec2(0, 0), 0, 0, (x, y) => 0f, _mb, _plants, 7);
                // una lastra di prato sotto l'elemento, per dare il terreno (stesso materiale degli edifici)
                float mx = item.Width * 0.18f + 1f, my = item.Depth * 0.18f + 1f;
                uint grass = MeshBuffer.Rgb(0x8CAD66);
                int g0 = _mb.Add(-mx, -my, -0.03f, 0, 0, 1, grass, 0, 0, 1, 1, BuildingDecorator.KindProp, 0.5f, 0, 0);
                int g1 = _mb.Add(item.Width + mx, -my, -0.03f, 0, 0, 1, grass, 0, 0, 1, 1, BuildingDecorator.KindProp, 0.5f, 0, 0);
                int g2 = _mb.Add(item.Width + mx, item.Depth + my, -0.03f, 0, 0, 1, grass, 0, 0, 1, 1, BuildingDecorator.KindProp, 0.5f, 0, 0);
                int g3 = _mb.Add(-mx, item.Depth + my, -0.03f, 0, 0, 1, grass, 0, 0, 1, 1, BuildingDecorator.KindProp, 0.5f, 0, 0);
                _mb.Quad(g0, g1, g2, g3);
                var bounds = new Bounds(new Vector3(item.Width / 2, item.Height / 2, item.Depth / 2), new Vector3(item.Width, Mathf.Max(0.5f, item.Height), item.Depth));
                if (!_mb.IsEmpty)
                {
                    var mesh = MeshUpload.ToMesh(_mb, item.Key);
                    meshes.Add(mesh);
                    var go = new GameObject("Modello") { layer = PreviewLayer };
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = world.Materials.Building;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    var mb = mesh.bounds;
                    mb.min = new Vector3(Mathf.Max(mb.min.x, -0.5f), Mathf.Max(mb.min.y, -0.1f), Mathf.Max(mb.min.z, -0.5f));
                    mb.max = new Vector3(Mathf.Min(mb.max.x, item.Width + 0.5f), mb.max.y, Mathf.Min(mb.max.z, item.Depth + 0.5f));
                    bounds.Encapsulate(mb);
                }
                foreach (var p in _plants)
                {
                    var go = new GameObject("Pianta") { layer = PreviewLayer };
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(p.X, p.Z, p.Y);
                    go.transform.localRotation = Quaternion.Euler(0, -p.Rotation * Mathf.Rad2Deg, 0);
                    go.transform.localScale = p.Kind == PlantKind.VineRow ? new Vector3(p.Length, 1, 1) : Vector3.one * p.Scale;
                    go.AddComponent<MeshFilter>().sharedMesh = world.Vegetation.MeshOf(p.Kind);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = world.Vegetation.Material;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    bounds.Encapsulate(new Vector3(p.X, p.Z + VegetationScatter.BaseHeight(p.Kind) * p.Scale, p.Y));
                }
                // inquadratura da tre quarti che contiene tutto il modello
                var center = Stage + bounds.center;
                float radius = Mathf.Max(1.2f, bounds.extents.magnitude);
                var rot = Quaternion.Euler(28f, 215f, 0);
                float dist = radius / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
                _cam.transform.SetPositionAndRotation(center - rot * Vector3.forward * dist, rot);
                _cam.nearClipPlane = Mathf.Max(0.05f, dist - radius * 1.5f);
                _cam.farClipPlane = dist + radius * 3f;

                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Anteprima " + item.Key };
                rt.Create();
                _cam.targetTexture = rt;
                RenderInDaylight();
                _cam.targetTexture = null;
                return rt;
            }
            finally
            {
                foreach (var m in meshes) Destroy(m);
                Destroy(root);
            }
        }

        /// <summary>Foto con il sole di metà mattina, qualunque sia l'ora in città; poi tutto torna com'era.</summary>
        private void RenderInDaylight()
        {
            var sun = RenderSettings.sun;
            Quaternion sunRot = default;
            Color sunColor = default;
            float sunIntensity = 0;
            LightShadows sunShadows = LightShadows.None;
            bool sunEnabled = false;
            if (sun != null)
            {
                sunRot = sun.transform.rotation;
                sunColor = sun.color;
                sunIntensity = sun.intensity;
                sunShadows = sun.shadows;
                sunEnabled = sun.enabled;
                sun.transform.rotation = Quaternion.Euler(48f, 160f, 0);
                sun.color = new Color(1f, 0.96f, 0.9f);
                sun.intensity = 1.15f;
                sun.shadows = LightShadows.None;
                sun.enabled = true;
            }
            var probe = RenderSettings.ambientProbe;
            bool fog = RenderSettings.fog;
            float night = Shader.GetGlobalFloat("_OrbisNight");
            var day = new SphericalHarmonicsL2();
            day.AddAmbientLight(new Color(0.42f, 0.45f, 0.5f));
            day.AddDirectionalLight(Vector3.up, new Color(0.25f, 0.3f, 0.38f), 1f);
            RenderSettings.ambientProbe = day;
            RenderSettings.fog = false;
            WorldMaterials.SetNight(0);
            try
            {
                _cam.Render();
            }
            finally
            {
                RenderSettings.ambientProbe = probe;
                RenderSettings.fog = fog;
                WorldMaterials.SetNight(night);
                if (sun != null)
                {
                    sun.transform.rotation = sunRot;
                    sun.color = sunColor;
                    sun.intensity = sunIntensity;
                    sun.shadows = sunShadows;
                    sun.enabled = sunEnabled;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var rt in _cache.Values)
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            _cache.Clear();
        }
    }
}
