using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Orbis.Core.Data;
using Orbis.Core.Game;
using Orbis.Core.Persistence;
using Orbis.Core.World.Geo;
using UnityEngine;

namespace Orbis.Gameplay
{
    /// <summary>Riassunto di un salvataggio per la lista "Continua".</summary>
    public sealed class SaveMeta
    {
        public string Id;
        public string Name;
        public DateTime SavedUtc;
        public DateTime GameTime;
        public long Cash;
        public int Level;
    }

    /// <summary>Salvataggi nell'archivio della piattaforma: "saves/&lt;id&gt;/game" (binario) e "saves/&lt;id&gt;/meta" (JSON).</summary>
    public static class SaveManager
    {
        public static async Task<List<SaveMeta>> ListAsync(ISaveStore store)
        {
            var list = new List<SaveMeta>();
            foreach (var key in await store.ListAsync("saves/"))
            {
                if (!key.EndsWith("/meta", StringComparison.Ordinal)) continue;
                var bytes = await store.ReadAsync(key);
                if (bytes == null) continue;
                try
                {
                    var j = Json.Parse(Encoding.UTF8.GetString(bytes));
                    list.Add(new SaveMeta
                    {
                        Id = j["id"].AsString(),
                        Name = j["name"].AsString("Chieti"),
                        SavedUtc = DateTime.Parse(j["saved"].AsString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                        GameTime = DateTime.Parse(j["gameTime"].AsString(), CultureInfo.InvariantCulture),
                        Cash = j["cash"].AsLong(),
                        Level = j["level"].AsInt(1),
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Orbis] salvataggio illeggibile: " + key + " " + e.Message);
                }
            }
            list.Sort((a, b) => b.SavedUtc.CompareTo(a.SavedUtc));
            return list;
        }

        public static async Task SaveAsync(ISaveStore store, string id, GameSession session)
        {
            var now = DateTime.UtcNow;
            var bytes = session.Save(now);
            await store.WriteAsync("saves/" + id + "/game", bytes);
            var s = session.State;
            var meta = "{\"id\":\"" + id + "\",\"name\":\"" + Escape(s.Name) + "\",\"saved\":\"" + now.ToString("o", CultureInfo.InvariantCulture) +
                       "\",\"gameTime\":\"" + s.Now.ToString("s", CultureInfo.InvariantCulture) + "\",\"cash\":" + s.Cash.ToString(CultureInfo.InvariantCulture) +
                       ",\"level\":" + s.Level + "}";
            await store.WriteAsync("saves/" + id + "/meta", Encoding.UTF8.GetBytes(meta));
        }

        public static Task<byte[]> LoadAsync(ISaveStore store, string id) => store.ReadAsync("saves/" + id + "/game");

        public static async Task DeleteAsync(ISaveStore store, string id)
        {
            await store.DeleteAsync("saves/" + id + "/game");
            await store.DeleteAsync("saves/" + id + "/meta");
        }

        public static string NewId() => DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + UnityEngine.Random.Range(100, 999);

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Partita in corso dentro il mondo (M2): fa scorrere il tempo di gioco, esegue i comandi del giocatore,
    /// aggiorna la scena (oggetti piazzati, edifici demoliti) e salva in automatico.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private const float AutosaveSeconds = 60f;

        public GameSession Session { get; private set; }
        public GameContent Content { get; private set; }
        public WorldSession World { get; private set; }
        public PlacedObjectsView View { get; private set; }
        public string SaveId { get; private set; }
        public OfflineSummary OfflineSummary { get; private set; }
        public BuildController Build { get; private set; }
        /// <summary>Anteprime 3D del catalogo.</summary>
        public CatalogPreviews Previews { get; private set; }

        /// <summary>Eventi della simulazione (cantiere finito, missione, livello…).</summary>
        public event Action<SimEvent> SimulationEvent;
        /// <summary>Lo stato è cambiato (comando, annulla, riavvolgi): l'interfaccia si aggiorna.</summary>
        public event Action Changed;

        private ISaveStore _store;
        private double _pendingGameSeconds;
        private float _nextAutosave;
        private bool _saveRequested;
        private bool _saving;
        private float _nextViewSync;
        private readonly List<SimEvent> _events = new List<SimEvent>();

        public GameState State => Session.State;

        public static GameController Attach(WorldSession world, GameSession session, string saveId, ISaveStore store, OfflineSummary offline = null)
        {
            var c = world.gameObject.AddComponent<GameController>();
            c.World = world;
            c.Session = session;
            c.Content = GameContent.Load();
            c.SaveId = saveId;
            c._store = store;
            c.OfflineSummary = offline;
            c.View = new PlacedObjectsView(world.World, c.Content.Catalog, world.World.Content ?? world.transform);
            c.Build = BuildController.Attach(c);
            c.Previews = CatalogPreviews.Attach(c);
            c._nextAutosave = Time.unscaledTime + AutosaveSeconds;
            world.World.TileLoaded += _ => c.View.Sync(c.State);
            world.Game = c;
            c.SyncWorld(rebuildAll: true);
            return c;
        }

        /// <summary>Nuova partita alla data reale di oggi (docs/03 §1), alle 10 per partire con la luce.</summary>
        public static GameSession NewSession(string name, WorldConfig config, GameContent content, WorldSession world)
        {
            return GameSession.New(name, config, DateTime.Today.AddHours(10), DateTime.UtcNow, UnityEngine.Random.Range(1, int.MaxValue),
                content.Catalog, content.Balance, new WorldAccess(world.World), content.Missions);
        }

        public static GameSession LoadSession(byte[] bytes, GameContent content, WorldSession world) =>
            GameSession.Load(bytes, content.Catalog, content.Balance, new WorldAccess(world.World), content.Missions);

        private void Update()
        {
            if (Session == null || World.State != WorldSession.Phase.Playing) return;
            // tempo di gioco: 1 ora = 1 minuto reale × velocità (pausa = 0)
            float speed = World.Clock.Speed;
            _pendingGameSeconds += Time.unscaledDeltaTime * GameSession.GameSecondsPerRealSecond * speed;
            if (_pendingGameSeconds >= 30)
            {
                var span = TimeSpan.FromSeconds(Math.Min(_pendingGameSeconds, 6 * 3600));
                _pendingGameSeconds = 0;
                _events.Clear();
                Session.Simulation.Advance(Session.Context, span, _events);
                foreach (var e in _events)
                {
                    SimulationEvent?.Invoke(e);
                    if (e.Kind == SimEventKind.ConstructionDone || e.Kind == SimEventKind.DayClosed) _saveRequested |= e.Kind == SimEventKind.DayClosed;
                }
                if (_events.Count > 0) Changed?.Invoke();
            }
            World.Clock.Set(State.Now);
            View.UpdateConstructions(State);
            if (Time.unscaledTime >= _nextViewSync)
            {
                _nextViewSync = Time.unscaledTime + 2f;
                View.Sync(State);
            }
            if (Time.unscaledTime >= _nextAutosave)
            {
                _nextAutosave = Time.unscaledTime + AutosaveSeconds;
                _saveRequested = true;
            }
            if (_saveRequested && !_saving) SaveNow();
        }

        // ------------------------------------------------------------------ comandi

        public BuildResult Do(IGameCommand cmd)
        {
            int level = State.Level, mission = State.MissionIndex;
            var r = Session.Do(cmd);
            if (r != BuildResult.Ok) return r;
            AfterChange();
            // missioni e livelli completati dall'azione stessa (es. la prima casa)
            var missions = Content.Missions.All;
            for (int i = mission; i < State.MissionIndex && i < missions.Count; i++)
                SimulationEvent?.Invoke(new SimEvent { Kind = SimEventKind.MissionDone, Time = State.Now, Key = missions[i].Key, Amount = missions[i].RewardCash });
            if (State.Level > level) SimulationEvent?.Invoke(new SimEvent { Kind = SimEventKind.LevelUp, Time = State.Now, Amount = State.Level });
            return r;
        }

        public bool Undo()
        {
            if (!Session.Undo()) return false;
            AfterChange();
            return true;
        }

        public bool Redo()
        {
            if (Session.Redo() != BuildResult.Ok) return false;
            AfterChange();
            return true;
        }

        public void Rewind(Snapshot snapshot)
        {
            Session.Rewind(snapshot);
            World.Clock.Set(State.Now);
            AfterChange(rebuildAll: true);
        }

        private void AfterChange(bool rebuildAll = false)
        {
            SyncWorld(rebuildAll);
            View.Sync(State);
            _saveRequested = true;
            Changed?.Invoke();
        }

        private readonly Dictionary<long, LotKey> _lastLots = new Dictionary<long, LotKey>();
        private readonly Dictionary<long, (int level, LotKey lot)> _lastLevels = new Dictionary<long, (int, LotKey)>();

        /// <summary>Edifici OSM demoliti: nascosti nella scena, ricostruendo solo i tile interessati.</summary>
        private void SyncWorld(bool rebuildAll)
        {
            var streamer = World.World;
            if (streamer.Region == null) return;
            int z = streamer.Region.Zoom;
            var changed = new HashSet<TileId>();
            foreach (var kv in State.DemolishedOsm)
                if (!_lastLots.ContainsKey(kv.Key)) changed.Add(new TileId(z, kv.Value.TileX, kv.Value.TileY));
            foreach (var kv in _lastLots)
                if (!State.DemolishedOsm.ContainsKey(kv.Key)) changed.Add(new TileId(z, kv.Value.TileX, kv.Value.TileY));
            streamer.HiddenBuildings.Clear();
            _lastLots.Clear();
            foreach (var kv in State.DemolishedOsm)
            {
                streamer.HiddenBuildings.Add(kv.Key);
                _lastLots[kv.Key] = kv.Value;
            }
            // edifici reali potenziati: cambiano gli addobbi, si ridisegnano solo i tile toccati
            foreach (var kv in State.OsmUpgrades)
                if (!_lastLevels.TryGetValue(kv.Key, out var old) || old.level != kv.Value.Level)
                    changed.Add(new TileId(z, kv.Value.Lot.TileX, kv.Value.Lot.TileY));
            foreach (var kv in _lastLevels)
                if (!State.OsmUpgrades.ContainsKey(kv.Key)) changed.Add(new TileId(z, kv.Value.lot.TileX, kv.Value.lot.TileY));
            streamer.BuildingLevels.Clear();
            _lastLevels.Clear();
            foreach (var kv in State.OsmUpgrades)
            {
                streamer.BuildingLevels[kv.Key] = kv.Value.Level;
                _lastLevels[kv.Key] = (kv.Value.Level, kv.Value.Lot);
            }
            var targets = rebuildAll ? new List<TileId>(streamer.Tiles.Keys) : new List<TileId>(changed);
            foreach (var id in targets) streamer.Rebuild(id);
        }

        // ------------------------------------------------------------------ selezione

        /// <summary>Elemento piazzato colpito dal raggio (0 se nessuno): sagoma esatta e altezza del modello.</summary>
        public int PickPlaced(Ray ray, float maxDistance = 20000f)
        {
            var streamer = World.World;
            if (streamer.Region == null || State.Objects.Count == 0) return 0;
            var candidates = new List<(int id, float[] poly, Vector3 origin, float ground, float top)>();
            foreach (var o in State.Objects.Values)
            {
                var item = Content.Catalog.Get(o.ItemKey);
                var id = new TileId(streamer.Region.Zoom, o.Lot.TileX, o.Lot.TileY);
                if (item == null || !streamer.Tiles.TryGetValue(id, out var view) || view.Data == null) continue;
                var lot = Placement.FindLot(view.Data, o.Lot.LotId);
                if (lot == null) continue;
                var f = Placement.Footprint(lot, item, o.HalfX, o.HalfY, o.Rotation);
                float g = view.Data.Terrain.HeightAt((f[0] + f[4]) / 2, (f[1] + f[5]) / 2);
                candidates.Add((o.Id, f, view.Origin, g, g + Mathf.Max(1.5f, item.Height)));
            }
            float t = 0;
            while (t < maxDistance)
            {
                t += Mathf.Clamp(t * 0.004f, 0.3f, 10f);
                var p = ray.GetPoint(t);
                foreach (var c in candidates)
                {
                    if (p.y < c.ground - 2f || p.y > c.top) continue;
                    if (Orbis.Core.World.Geometry.Polygon2D.ContainsRing(c.poly, p.x - c.origin.x, p.z - c.origin.z)) return c.id;
                }
                float h = streamer.HeightAt(p.x, p.z);
                if (!float.IsNaN(h) && p.y < h - 1f) return 0;
            }
            return 0;
        }

        // ------------------------------------------------------------------ salvataggio

        public async void SaveNow()
        {
            if (Session == null || _store == null || _saving) return;
            _saving = true;
            _saveRequested = false;
            try
            {
                await SaveManager.SaveAsync(_store, SaveId, Session);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] salvataggio non riuscito: " + e.Message);
            }
            finally
            {
                _saving = false;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveNowSync();
        }

        private void OnApplicationQuit() => SaveNowSync();

        private void OnDestroy()
        {
            SaveNowSync();
            View?.Dispose();
        }

        /// <summary>Salvataggio immediato (uscita dall'app): l'archivio su file completa in modo sincrono.</summary>
        public void SaveNowSync()
        {
            if (Session == null || _store == null) return;
            try
            {
                SaveManager.SaveAsync(_store, SaveId, Session).Wait(2000);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] salvataggio in uscita non riuscito: " + e.Message);
            }
        }
    }
}
