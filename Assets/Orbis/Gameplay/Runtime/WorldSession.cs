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
        /// <summary>Elemento piazzato dal giocatore (0 = edificio reale OSM).</summary>
        public int PlacedObjectId;
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
        /// <summary>Meteo della partita (reale o simulato col clima): quando c'è, comanda il cielo al posto del simulatore di M1.</summary>
        public CityWeather GameWeather { get; set; }
        /// <summary>Tempo che si vede adesso.</summary>
        public WeatherKind CurrentWeather => GameWeather != null && GameWeather.Current.Time != default ? GameWeather.Current.Kind : Weather.Current;
        /// <summary>Temperatura dell'ora (°C), se c'è il meteo della partita.</summary>
        public float? Temperature => GameWeather != null && GameWeather.Current.Time != default ? GameWeather.Current.TempC : (float?)null;
        public Phase State { get; private set; } = Phase.Loading;
        public WorldSelection Selection { get; private set; }
        public GlobeController Globe { get; private set; }

        /// <summary>Partita (economia, costruzione) collegata al mondo; null finché non viene creata.</summary>
        public GameController Game
        {
            get => _game;
            set
            {
                _game = value;
                if (Clock != null) Clock.External = value != null;
            }
        }

        private GameController _game;

        /// <summary>0..1 durante il primo caricamento (indice + tile vicini).</summary>
        public float LoadProgress { get; private set; }

        public event Action<WorldSelection> SelectionChanged;
        public event Action<WeatherKind> WeatherChanged;
        private WeatherKind _skyWeather = WeatherKind.Clear;

        private GameObject _highlight;
        private Mesh _highlightMesh;
        private float _originalShadowDistance = -1;
        private float _originalRenderScale = -1;
        private UniversalAdditionalCameraData _cameraData;
        private Volume _volume;
        private UniversalRenderPipelineAsset _urp;

        public WorldQuality Quality { get; private set; }

        public static WorldSession Create(Camera camera, Light sun, WorldQuality quality, string region = "chieti", TileSource source = null)
        {
            var root = new GameObject("Mondo");
            var session = root.AddComponent<WorldSession>();
            session.Setup(camera, sun, quality, source ?? TileSource.ForRegion(region));
            return session;
        }

        private void Setup(Camera camera, Light sun, WorldQuality quality, TileSource source)
        {
            Quality = quality;
            World = gameObject.AddComponent<WorldStreamer>();
            quality.ApplyTo(World);
            Debug.Log("[Orbis] Qualità del mondo: " + quality.Name);
            World.ViewCamera = camera;
            Picker = new WorldPicker(World);
            Sky = gameObject.AddComponent<SkyController>();
            Sky.Follow = camera.transform;
            Sky.Init(sun);
            CameraRig = camera.gameObject.AddComponent<CityCamera>();
            CameraRig.Cam = camera;
            CameraRig.World = World;
            CameraRig.Tapped += OnTapped;
            // M1: la partita parte alle 10 di oggi (luce piena); dal M2 l'ora fa parte del salvataggio
            Clock = new GameClock(DateTime.Today.AddHours(10));
            Weather = new WeatherSimulator((uint)DateTime.Now.DayOfYear * 7919u + 17u, WeatherKind.Clear);
            Clock.HourChanged += t =>
            {
                var before = CurrentWeather;
                var sim = Weather.StepHour(t.Month, t.Hour);
                var now = GameWeather != null && GameWeather.Current.Time != default ? GameWeather.Current.Kind : sim;
                if (now != before || (GameWeather != null && now != _skyWeather))
                {
                    _skyWeather = now;
                    Sky.SetWeather(WeatherState.For(now));
                    WeatherChanged?.Invoke(now);
                }
            };
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 50f;
            // post-processing (tonemapping filmico, bloom leggero) solo nel mondo; sul web dei telefoni si risparmia
            _cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            _volume = FindAnyObjectByType<Volume>();
            bool post = quality.PostProcessing && _volume != null && _cameraData != null;
            if (post)
            {
                _cameraData.renderPostProcessing = true;
                _volume.weight = 1f;
            }
            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_urp != null)
            {
                _originalShadowDistance = _urp.shadowDistance;
                // immagine nitida: risoluzione piena sui dispositivi capaci (prima era fissa all'80% su telefoni e web)
                _originalRenderScale = _urp.renderScale;
                _urp.renderScale = Quality.RenderScale;
            }
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
            SeaPlane.Create(World.Content, World.Materials.Water).Follow = CameraRig.Cam.transform;
            Globe = GlobeController.Create(this);
            StartCoroutine(Globe.View.Load(source));
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
            if (Globe == null || !Globe.Active) Sky.Apply(Clock.Now, CameraRig.Distance, CameraRig.Cam.orthographic);
            if (_urp != null)
                _urp.shadowDistance = Mathf.Clamp(CameraRig.Distance * 2.2f, 150f, Quality.MaxShadowDistance);
        }

        private void OnDestroy()
        {
            if (_cameraData != null) _cameraData.renderPostProcessing = false;
            if (_volume != null) _volume.weight = 0f;
            if (_urp != null && _originalShadowDistance > 0) _urp.shadowDistance = _originalShadowDistance;
            if (_urp != null && _originalRenderScale > 0) _urp.renderScale = _originalRenderScale;
            if (CameraRig != null) Destroy(CameraRig);
            WorldMaterials.SetNight(0);
            WorldMaterials.SetWet(0);
        }

        // ---------------------------------------------------------------- selezione

        private void OnTapped(Vector2 screen)
        {
            var ray = CameraRig.Cam.ScreenPointToRay(screen);
            if (Game != null && Game.Build.Active)
            {
                // con il mouse il clic piazza; col tocco si conferma con ✓
                if (Game.Build.AimAtPointer) Game.Build.Confirm();
                return;
            }
            if (Game != null)
            {
                int placed = Game.PickPlaced(ray);
                if (placed != 0)
                {
                    SelectPlaced(placed);
                    return;
                }
            }
            var hit = Picker.Raycast(ray);
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

        public void SelectPlaced(int objectId)
        {
            if (Game == null || !Game.State.Objects.TryGetValue(objectId, out var o)) return;
            var id = new Orbis.Core.World.Geo.TileId(World.Region.Zoom, o.Lot.TileX, o.Lot.TileY);
            World.Tiles.TryGetValue(id, out var view);
            Select(new WorldSelection { PlacedObjectId = objectId, Tile = view });
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
            if (Selection == null || (Selection.Building == null && Selection.PlacedObjectId == 0) || Selection.Tile == null)
            {
                _highlight.SetActive(false);
                return;
            }
            var mb = new MeshBuffer();
            var tile = Selection.Tile;
            if (Selection.PlacedObjectId != 0 && Game != null && Game.State.Objects.TryGetValue(Selection.PlacedObjectId, out var po))
            {
                var item = Game.Content.Catalog.Get(po.ItemKey);
                var lot = item != null ? Orbis.Core.Game.Placement.FindLot(tile.Data, po.Lot.LotId) : null;
                if (lot == null)
                {
                    _highlight.SetActive(false);
                    return;
                }
                var f = Orbis.Core.Game.Placement.Footprint(lot, item, po.HalfX, po.HalfY, po.Rotation);
                new Orbis.Core.Game.ObjectMesher().Build(item, po.Level, po.Variant, new Orbis.Core.World.Geometry.Vec2(f[0], f[1]), lot.GridAngle,
                    po.Rotation, tile.Data.Terrain.HeightAt, mb, null, po.Id);
            }
            else
            {
                new BuildingMesher().Build(Selection.Building, tile.Data.Terrain.HeightAt, mb, false, tile.Lod != TileLod.Full);
            }
            if (_highlightMesh != null) Destroy(_highlightMesh);
            _highlightMesh = MeshUpload.ToMesh(mb, "Selezione");
            _highlight.GetComponent<MeshFilter>().sharedMesh = _highlightMesh;
            _highlight.transform.position = tile.Origin;
            _highlight.SetActive(true);
        }
    }
}
