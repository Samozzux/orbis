using System;
using System.Collections;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Sky;
using Orbis.Core.World.Tiles;
using Orbis.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.Gameplay
{
    /// <summary>Elemento selezionato nel mondo (per il pannello Dettagli).</summary>
    public sealed class WorldSelection
    {
        public TileView Tile;
        public int BuildingIndex;
        public BuildingInfo Building;
        public PoiInfo[] Pois;
        public Vector3 Point;
        public double Lon, Lat;
    }

    /// <summary>
    /// Partita nel mondo reale (M1: esplorazione di Chieti): collega streaming dei tile, telecamera, cielo,
    /// orologio, meteo e selezione. La crea l'interfaccia quando si sceglie "Nuova partita".
    /// </summary>
    public sealed class WorldSession : MonoBehaviour
    {
        public enum Phase { Loading, Playing, Missing, Failed }

        public WorldStreamer World { get; private set; }
        public CityCamera CameraRig { get; private set; }
        public SkyController Sky { get; private set; }
        public WorldPicker Picker { get; private set; }
        public GameClock Clock { get; private set; }
        public WeatherSimulator Weather { get; private set; }
        public Phase State { get; private set; } = Phase.Loading;
        public WorldSelection Selection { get; private set; }

        /// <summary>0..1 durante il primo caricamento (indice + tile vicini).</summary>
        public float LoadProgress { get; private set; }

        public event Action<WorldSelection> SelectionChanged;
        public event Action<WeatherKind> WeatherChanged;

        private GameObject _highlight;
        private Mesh _highlightMesh;
        private float _originalShadowDistance = -1;
        private UniversalRenderPipelineAsset _urp;

        public static WorldSession Create(Camera camera, Light sun, string region = "chieti", TileSource source = null)
        {
            var root = new GameObject("Mondo");
            var session = root.AddComponent<WorldSession>();
            session.Setup(camera, sun, source ?? TileSource.ForRegion(region));
            return session;
        }

        private void Setup(Camera camera, Light sun, TileSource source)
        {
            World = gameObject.AddComponent<WorldStreamer>();
            World.ViewCamera = camera;
            Picker = new WorldPicker(World);
            Sky = gameObject.AddComponent<SkyController>();
            Sky.Follow = camera.transform;
            Sky.Init(sun);
            CameraRig = camera.gameObject.AddComponent<CityCamera>();
            CameraRig.Cam = camera;
            CameraRig.World = World;
            CameraRig.Tapped += OnTapped;
            Clock = new GameClock(DateTime.Now);
            Weather = new WeatherSimulator((uint)DateTime.Now.DayOfYear * 7919u + 17u, WeatherKind.Clear);
            Clock.HourChanged += t =>
            {
                var before = Weather.Current;
                var now = Weather.StepHour(t.Month, t.Hour);
                if (now != before)
                {
                    Sky.SetWeather(WeatherState.For(now));
                    WeatherChanged?.Invoke(now);
                }
            };
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 50f;
            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_urp != null) _originalShadowDistance = _urp.shadowDistance;
            StartCoroutine(Load(source));
        }

        private IEnumerator Load(TileSource source)
        {
            yield return World.Open(source);
            if (World.State == WorldStreamer.Status.Missing)
            {
                State = Phase.Missing;
                yield break;
            }
            if (World.State != WorldStreamer.Status.Ready)
            {
                State = Phase.Failed;
                yield break;
            }
            SeaPlane.Create(transform, World.Materials.Water).Follow = CameraRig.Cam.transform;
            Sky.Latitude = World.Region.OriginLat;
            Sky.Longitude = World.Region.OriginLon;
            // il primo tile (centro) si aspetta, gli altri arrivano mentre si gioca
            CameraRig.Target = Vector3.zero;
            World.Focus = Vector3.zero;
            float started = Time.realtimeSinceStartup;
            while (World.Tiles.Count < Mathf.Min(9, World.Region.Tiles.Count) && Time.realtimeSinceStartup - started < 25f)
            {
                LoadProgress = Mathf.Clamp01(World.Tiles.Count / 9f);
                yield return null;
            }
            float h = World.HeightAt(0, 0);
            CameraRig.Target = new Vector3(0, float.IsNaN(h) ? 0 : h, 0);
            LoadProgress = 1;
            State = Phase.Playing;
        }

        private void Update()
        {
            if (State != Phase.Playing && State != Phase.Loading) return;
            Clock.Tick(Time.unscaledDeltaTime);
            Sky.Apply(Clock.Now, CameraRig.Distance);
            if (_urp != null)
                _urp.shadowDistance = Mathf.Clamp(CameraRig.Distance * 2.2f, 150f, 1400f);
        }

        private void OnDestroy()
        {
            if (_urp != null && _originalShadowDistance > 0) _urp.shadowDistance = _originalShadowDistance;
            if (CameraRig != null) Destroy(CameraRig);
            WorldMaterials.SetNight(0);
            WorldMaterials.SetWet(0);
        }

        // ---------------------------------------------------------------- selezione

        private void OnTapped(Vector2 screen)
        {
            var hit = Picker.Raycast(CameraRig.Cam.ScreenPointToRay(screen));
            if (!hit.Hit || hit.Building < 0)
            {
                Select(null);
                return;
            }
            var b = hit.BuildingInfo;
            var pois = new System.Collections.Generic.List<PoiInfo>();
            foreach (var p in hit.Tile.Data.Pois)
                if (p.Building == hit.Building) pois.Add(p);
            World.Frame.ToLonLat(hit.Point.x, hit.Point.z, out var lon, out var lat);
            Select(new WorldSelection
            {
                Tile = hit.Tile, BuildingIndex = hit.Building, Building = b, Pois = pois.ToArray(), Point = hit.Point,
                Lon = lon, Lat = lat,
            });
        }

        public void Select(WorldSelection s)
        {
            Selection = s;
            UpdateHighlight();
            SelectionChanged?.Invoke(s);
        }

        private void UpdateHighlight()
        {
            if (_highlight == null)
            {
                _highlight = new GameObject("Selezione");
                _highlight.transform.SetParent(transform, false);
                _highlight.AddComponent<MeshFilter>();
                var mr = _highlight.AddComponent<MeshRenderer>();
                var shader = Resources.Load<Shader>("OrbisShaders/Highlight");
                mr.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            if (Selection == null || Selection.Building == null)
            {
                _highlight.SetActive(false);
                return;
            }
            var mb = new MeshBuffer();
            var tile = Selection.Tile;
            new BuildingMesher().Build(Selection.Building, tile.Data.Terrain.HeightAt, mb, false, tile.Lod != TileLod.Full);
            if (_highlightMesh != null) Destroy(_highlightMesh);
            _highlightMesh = MeshUpload.ToMesh(mb, "Selezione");
            _highlight.GetComponent<MeshFilter>().sharedMesh = _highlightMesh;
            _highlight.transform.position = tile.Origin;
            _highlight.SetActive(true);
        }
    }
}
