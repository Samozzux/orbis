using System;
using System.Collections;
using System.Collections.Generic;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Mondo reale a tile (docs/02 §3): carica i tile attorno al punto osservato con tre livelli di dettaglio,
    /// li costruisce fuori dal main thread e scarica quelli lontani. Le coordinate di Unity sono metri veri
    /// attorno all'origine della regione (x est, z nord).
    /// </summary>
    public sealed class WorldStreamer : MonoBehaviour
    {
        public enum Status { Idle, LoadingIndex, Ready, Missing, Error }

        public Status State { get; private set; } = Status.Idle;
        public RegionInfo Region { get; private set; }
        public WorldFrame Frame { get; private set; }
        public WorldMaterials Materials { get; private set; }
        public VegetationRenderer Vegetation { get; private set; }
        public string Error { get; private set; }

        /// <summary>Punto osservato (dove guarda la telecamera) e distanza della telecamera da esso.</summary>
        public Vector3 Focus;
        public float ViewDistance = 600f;
        public Camera ViewCamera;

        /// <summary>Raggi dei livelli di dettaglio (m) per una distanza di vista di 600 m; crescono con lo zoom.</summary>
        public float FullRadius = 1100f;
        public float MediumRadius = 3200f;
        public float FarRadius = 9000f;
        public int MaxTiles = 260;
        public WorldQuality Quality;

        private readonly Dictionary<TileId, TileView> _views = new Dictionary<TileId, TileView>();
        private readonly Dictionary<TileId, TileLod> _building = new Dictionary<TileId, TileLod>();
        private readonly Dictionary<TileId, byte[]> _downloading = new Dictionary<TileId, byte[]>();
        private readonly HashSet<TileId> _requested = new HashSet<TileId>();
        private readonly List<(TileId id, float dist, TileLod lod)> _wanted = new List<(TileId, float, TileLod)>();
        private TileSource _source;
        private WorkRunner _runner;
        private float _nextScan;
        private int _activeDownloads;

        /// <summary>Contenitore di tutto ciò che si vede del mondo piatto (si nasconde nel globo).</summary>
        public Transform Content { get; private set; }

        public bool Visible => Content == null || Content.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (Content != null) Content.gameObject.SetActive(visible);
        }

        /// <summary>Edifici OSM nascosti (demoliti nella partita): chiave (tipo &lt;&lt; 60 | id).</summary>
        public readonly HashSet<long> HiddenBuildings = new HashSet<long>();

        /// <summary>Livello degli edifici OSM potenziati nella partita (assente = 1): cambia gli addobbi.</summary>
        public readonly Dictionary<long, int> BuildingLevels = new Dictionary<long, int>();

        /// <summary>Ricostruisce un tile già caricato (es. dopo una demolizione), con gli stessi dati.</summary>
        public void Rebuild(TileId id)
        {
            if (!_views.TryGetValue(id, out var v) || v.Data == null || _building.ContainsKey(id)) return;
            StartBuild(id, null, v.Data, v.Lod, 0);
        }

        public bool IsHidden(Orbis.Core.World.Tiles.BuildingInfo b) => HiddenBuildings.Count > 0 && HiddenBuildings.Contains(((long)b.OsmType << 60) | b.OsmId);

        public event Action<TileView> TileLoaded;
        public event Action<TileId> TileUnloaded;

        public IReadOnlyDictionary<TileId, TileView> Tiles => _views;
        public int PendingBuilds => _building.Count;
        public int LoadedBuildings
        {
            get
            {
                int n = 0;
                foreach (var v in _views.Values)
                    if (v.Lod != TileLod.Far && v.Data != null) n += v.Data.Buildings.Length;
                return n;
            }
        }

        /// <summary>Apre il region pack e inizia lo streaming.</summary>
        public IEnumerator Open(TileSource source)
        {
            _source = source;
            State = Status.LoadingIndex;
            Content = new GameObject("Contenuto").transform;
            Content.SetParent(transform, false);
            Materials = new WorldMaterials();
            Vegetation = new VegetationRenderer(Materials.Plant);
            _runner = new WorkRunner();
            if (Quality != null)
            {
                Vegetation.MaxDistance = Quality.VegetationDistance;
                Vegetation.CastShadows = Quality.PlantShadows;
                _runner.FrameBudgetMs = Quality.FrameBudgetMs;
            }
            byte[] index = null;
            yield return source.Fetch("index.bin", b => index = b);
            if (index == null)
            {
                State = Status.Missing;
                Error = "pack del mondo assente: " + source.BaseUrl;
                Debug.Log("[Orbis] " + Error); // normale nei test e nelle build senza pack: lo dice l'HUD
                yield break;
            }
            try
            {
                Region = TileReader.ReadIndexFile(index);
                Frame = Region.CreateFrame();
            }
            catch (Exception e)
            {
                State = Status.Error;
                Error = e.Message;
                Debug.LogException(e);
                yield break;
            }
            FarTerrainView.Create(Content, Region, Frame, Materials.Plant);
            State = Status.Ready;
            Debug.Log($"[Orbis] Mondo «{Region.Name}» v{Region.Version}: {Region.Tiles.Count} tile ({Region.Created})");
        }

        private void OnDestroy()
        {
            _runner?.Dispose();
            foreach (var v in _views.Values) v.Destroy();
            _views.Clear();
        }

        private void Update()
        {
            _runner?.Tick();
            if (State != Status.Ready || !Visible) return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.25f;
                Scan();
            }
            var cam = ViewCamera != null ? ViewCamera : Camera.main;
            Vegetation.Draw(cam);
            if (cam != null && Time.frameCount % 10 == 0)
            {
                float decor = Quality != null ? Quality.DecorDistance : 800f;
                var pos = cam.transform.position;
                foreach (var v in _views.Values) v.CullDecorations(pos, decor);
            }
        }

        /// <summary>Quota del terreno nel punto (metri del gioco); NaN se il tile non è caricato.</summary>
        public float HeightAt(float x, float z)
        {
            if (Frame == null) return float.NaN;
            var id = Frame.TileAt(Region.Zoom, x, z);
            if (!_views.TryGetValue(id, out var v) || v.Data == null) return float.NaN;
            return v.Data.Terrain.HeightAt(x - v.Origin.x, z - v.Origin.z);
        }

        public Vector3 TileOrigin(TileId id)
        {
            Frame.TileOrigin(id, out var x, out var y);
            return new Vector3((float)x, 0, (float)y);
        }

        private float Scale => Mathf.Clamp(ViewDistance / 600f, 0.6f, 6f);

        private void Scan()
        {
            float full = FullRadius * Mathf.Min(Scale, 1.8f);
            float medium = MediumRadius * Scale;
            float far = FarRadius * Mathf.Max(1f, Scale);
            int z = Region.Zoom;
            double size = Frame.TileSize(z);
            var center = Frame.TileAt(z, Focus.x, Focus.z);
            int reach = (int)Math.Ceiling(far / size) + 1;
            _wanted.Clear();
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                var id = new TileId(z, center.X + dx, center.Y + dy);
                if (!Region.Tiles.TryGetValue(id, out var entry)) continue;
                var o = TileOrigin(id);
                // distanza dal punto osservato al rettangolo del tile
                float ex = Mathf.Max(0, Mathf.Max(o.x - Focus.x, Focus.x - (o.x + (float)size)));
                float ez = Mathf.Max(0, Mathf.Max(o.z - Focus.z, Focus.z - (o.z + (float)size)));
                float d = Mathf.Sqrt(ex * ex + ez * ez);
                if (d > far) continue;
                var lod = d <= full ? TileLod.Full : (d <= medium ? TileLod.Medium : TileLod.Far);
                if (entry.Buildings == 0 && lod == TileLod.Full) lod = TileLod.Medium; // tile di contorno
                _wanted.Add((id, d, lod));
            }
            _wanted.Sort((a, b) => a.dist.CompareTo(b.dist));
            if (_wanted.Count > MaxTiles) _wanted.RemoveRange(MaxTiles, _wanted.Count - MaxTiles);

            // scarica i tile non più richiesti (con margine per non caricare e scaricare di continuo)
            var keep = new HashSet<TileId>();
            foreach (var w in _wanted) keep.Add(w.id);
            var drop = new List<TileId>();
            foreach (var kv in _views)
                if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop)
            {
                _views[id].Destroy();
                _views.Remove(id);
                Vegetation.Remove(id);
                TileUnloaded?.Invoke(id);
            }

            foreach (var w in _wanted)
            {
                if (_building.ContainsKey(w.id)) continue;
                if (_views.TryGetValue(w.id, out var v) && v.Lod == w.lod) continue;
                // da Far a Full si passa direttamente; i dati del tile già letti si riusano
                if (_views.TryGetValue(w.id, out var existing) && existing.Data != null)
                {
                    StartBuild(w.id, null, existing.Data, w.lod, w.dist);
                    continue;
                }
                if (_requested.Contains(w.id) || _activeDownloads >= 6) continue;
                StartCoroutine(Download(w.id, w.lod, w.dist));
            }
        }

        private IEnumerator Download(TileId id, TileLod lod, float dist)
        {
            _requested.Add(id);
            _activeDownloads++;
            byte[] data = null;
            yield return _source.Fetch(id.Path, b => data = b);
            _activeDownloads--;
            _requested.Remove(id);
            if (data == null)
            {
                Debug.LogWarning("[Orbis] tile mancante: " + id);
                yield break;
            }
            var entry = Region.Tiles[id];
            if (entry.Crc != 0 && Crc32.Compute(data) != entry.Crc)
            {
                Debug.LogWarning("[Orbis] tile corrotto (CRC): " + id);
                yield break;
            }
            StartBuild(id, data, null, lod, dist);
        }

        private void StartBuild(TileId id, byte[] file, TileData cached, TileLod lod, float priority)
        {
            var result = new TileBuildResult();
            _building[id] = lod;
            double scale = Region.Scale;
            var hidden = HiddenBuildings.Count > 0 ? new HashSet<long>(HiddenBuildings) : null;
            var levels = BuildingLevels.Count > 0 ? new Dictionary<long, int>(BuildingLevels) : null;
            float density = Quality != null ? Quality.DecorDensity : 1f;
            _runner.Enqueue(TileBuild.Run(() => file, cached, scale, lod, result, hidden, levels, density), priority,
                () => Finish(id, result),
                e =>
                {
                    _building.Remove(id);
                    Debug.LogError("[Orbis] errore nel tile " + id + ": " + e);
                });
        }

        private void Finish(TileId id, TileBuildResult result)
        {
            _building.Remove(id);
            if (!_views.TryGetValue(id, out var view))
            {
                view = new TileView(id, Content, TileOrigin(id));
                _views[id] = view;
            }
            view.Apply(result, Materials);
            if (result.Plants != null) Vegetation.Set(id, view.Origin, result.Plants);
            else Vegetation.Remove(id);
            TileLoaded?.Invoke(view);
        }
    }
}
