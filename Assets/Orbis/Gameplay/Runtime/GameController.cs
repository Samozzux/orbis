using System;
using System.Collections;
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bytes = session.Save(now);
            SlowWork.Log($"salvataggio ({bytes.Length / 1024} KB)", sw);
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
            // se era nel cloud, viene eliminata anche da lì alla prossima sincronizzazione
            await Orbis.Core.Online.CloudSync.MarkDeletedAsync(store, id);
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
        /// <summary>Strade disegnate dal sindaco (M4).</summary>
        public CityRoadsView RoadsView { get; private set; }
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
            c.RoadsView = new CityRoadsView(world.World, c.Content.Traffic, world.World.Content ?? world.transform);
            c.EnsureFrame();
            c.SetupWeather();
            c.Build = BuildController.Attach(c);
            world.gameObject.AddComponent<FrameWatch>();
            c.Previews = CatalogPreviews.Attach(c);
            c._nextAutosave = Time.unscaledTime + AutosaveSeconds;
            world.World.TileLoaded += _ =>
            {
                c.View.Sync(c.State);
                c.RoadsView.Sync(c.State);
            };
            world.Game = c;
            c.SyncWorld(rebuildAll: true);
            // tile del comune: cittadini (se la partita non li ha ancora) e rete stradale con il traffico
            if (c.Content.Life != null) PopulationLoader.Attach(c);
            return c;
        }

        /// <summary>Nuova partita alla data reale di oggi (docs/03 §1), alle 10 per partire con la luce.</summary>
        public static GameSession NewSession(string name, WorldConfig config, GameContent content, WorldSession world)
        {
            return GameSession.New(name, config, DateTime.Today.AddHours(10), DateTime.UtcNow, UnityEngine.Random.Range(1, int.MaxValue),
                content.Catalog, content.Balance, new WorldAccess(world.World), content.Missions);
        }

        public static GameSession LoadSession(byte[] bytes, GameContent content, WorldSession world) =>
            GameSession.Load(bytes, content.Catalog, content.Balance, new WorldAccess(world.World), content.Missions, content.Life);

        /// <summary>I cittadini sono arrivati (generati per la prima volta in questa partita).</summary>
        public event Action<Orbis.Core.Population.CityPopulation> PopulationReady;

        /// <summary>La generazione dei cittadini è partita.</summary>
        public event Action<string> PopulationStarted;

        internal void OnPopulationStarted(string municipality) => PopulationStarted?.Invoke(municipality);

        /// <summary>Traffico sulla rete stradale vera del comune (M4); null finché il caricatore non l'ha costruito.</summary>
        public Orbis.Core.Traffic.TrafficModel Traffic { get; private set; }

        internal void OnTrafficReady(Orbis.Core.Traffic.TrafficModel traffic)
        {
            Traffic = traffic;
            WireCity();
        }

        /// <summary>Collega rete stradale e linee alla giornata dei cittadini e ai servizi (tempi di arrivo dei mezzi d'emergenza).</summary>
        private void WireCity()
        {
            var ctx = Session.Context;
            if (ctx.Routines != null)
            {
                if (Traffic != null) ctx.Routines.Travel = Traffic;
                if (Transit != null) ctx.Routines.Transit = Transit;
            }
            if (ctx.Life?.Services != null && Traffic != null) ctx.Life.Services.Travel = Traffic;
            if (ctx.Routines != null) ctx.Routines.Weather = ctx.Weather;
            if (ctx.Life?.Services != null && _serviceSources != null && ctx.Life.Services.Fixed.Count == 0)
                ctx.Life.Services.Fixed.AddRange(_serviceSources);
        }

        /// <summary>Linee reali del trasporto pubblico (M4); null finché il caricatore non le ha preparate.</summary>
        public Orbis.Core.Traffic.TransitNetwork Transit { get; private set; }

        internal void OnTransitReady(Orbis.Core.Traffic.TransitNetwork transit)
        {
            Transit = transit;
            _linesSignature = LinesSignature();
            transit.SetCityLines(Infrastructure.NetworkLines(State));
            WireCity();
        }

        // ------------------------------------------------------------------ meteo

        private bool _weatherFetching;
        private float _nextWeatherTry;
        private bool _weatherLogged;

        /// <summary>Meteo della partita (M4): simulato col clima medio, con i dati veri di Open-Meteo quando arrivano.</summary>
        private void SetupWeather()
        {
            var w = new Orbis.Core.World.Sky.CityWeather(Content.Climate, (uint)State.Seed) { UseReal = State.Config.RealWeather };
            w.Step(State.Now);
            Session.Context.Weather = w;
            World.GameWeather = w;
            WireCity();
        }

        /// <summary>Se la partita usa il meteo reale e mancano le prossime ore, le chiede a Open-Meteo (solo le coordinate della città).</summary>
        private void UpdateWeatherFeed()
        {
            var w = Session.Context.Weather;
            if (w == null || !State.Config.RealWeather || _weatherFetching || Time.unscaledTime < _nextWeatherTry) return;
            if (Content.Climate == null || Content.Climate.Latitude == 0 || Application.internetReachability == NetworkReachability.NotReachable)
            {
                _nextWeatherTry = Time.unscaledTime + 60f;
                return;
            }
            var missing = w.MissingFrom(State.Now, 24);
            if (!missing.HasValue)
            {
                _nextWeatherTry = Time.unscaledTime + 10f;
                return;
            }
            StartCoroutine(FetchWeather(missing.Value.Date));
        }

        private IEnumerator FetchWeather(DateTime from)
        {
            _weatherFetching = true;
            _nextWeatherTry = Time.unscaledTime + 300f; // se non riesce, si riprova tra 5 minuti
            var (url, shift) = Orbis.Core.World.Sky.OpenMeteo.Request(Content.Climate.Latitude, Content.Climate.Longitude, from, from.AddDays(7), DateTime.Today);
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success && Session != null)
                {
                    try
                    {
                        var hours = Orbis.Core.World.Sky.OpenMeteo.Parse(req.downloadHandler.text, shift);
                        Session.Context.Weather?.SetReal(hours);
                        _nextWeatherTry = Time.unscaledTime + 5f;
                        if (!_weatherLogged)
                        {
                            _weatherLogged = true;
                            Debug.Log($"[Orbis] meteo reale da Open-Meteo: {hours.Count} ore dal {from:dd/MM}" + (shift > 0 ? $" (archivio, {shift} anni fa)" : " (previsioni)"));
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Orbis] risposta del meteo illeggibile: " + e.Message);
                    }
                }
                else Debug.LogWarning("[Orbis] meteo reale non disponibile, resta quello simulato: " + req.error);
            }
            _weatherFetching = false;
        }

        // ------------------------------------------------------------------ strade e linee del sindaco

        private List<(Orbis.Core.World.Tiles.TileData Tile, double OriginX, double OriginY)> _cityTiles;

        /// <summary>Tile del comune tenuti dalla partita e loro edifici (per la diagnosi della memoria).</summary>
        public (int Tiles, int Buildings) CityTileStats
        {
            get
            {
                if (_cityTiles == null) return (0, 0);
                int b = 0;
                foreach (var t in _cityTiles) b += t.Tile.Buildings.Length;
                return (_cityTiles.Count, b);
            }
        }
        private (double, double, double, double) _cityArea;
        private string _roadsSignature, _linesSignature;
        private int _trafficVersion;
        private bool _trafficBuilding;

        /// <summary>Le strade e le linee del sindaco sono in metri della regione: le regole del Core devono conoscerne il sistema.</summary>
        private void EnsureFrame()
        {
            var streamer = World.World;
            if (Session.Context.Frame != null || streamer.Frame == null || streamer.Region == null) return;
            Session.Context.Frame = streamer.Frame;
            Session.Context.TileZoom = streamer.Region.Zoom;
        }

        /// <summary>
        /// Legge i tile del comune uno alla volta e ne tiene solo quello che serve dopo: sedi dei servizi e monumenti
        /// (subito), strade e lotti (per ricostruire la rete e trovare gli edifici del catalogo). Edifici, aree, alberi
        /// e terreno restano solo nei tile vicini alla camera: tenerli per tutto il comune costava centinaia di MB, troppi
        /// per il web (la memoria non torna indietro e il garbage collector blocca il gioco più a lungo).
        /// </summary>
        internal sealed class CityTilesBuilder
        {
            private readonly GameContent _content;
            public readonly List<Orbis.Core.Population.ServiceSource> ServiceSources = new List<Orbis.Core.Population.ServiceSource>();

            public CityTilesBuilder(GameContent content) => _content = content;

            public (Orbis.Core.World.Tiles.TileData Tile, double OriginX, double OriginY) Keep(Orbis.Core.World.Tiles.TileData tile, double ox, double oy)
            {
                var one = new[] { (tile, ox, oy) };
                // sedi vere dei servizi (questure, caserme, pompieri, ospedali, scuole…)
                if (_content.Life?.Services != null) Orbis.Core.Population.CityServices.AddFromTiles(ServiceSources, one, _content.Life.Services);
                // luoghi degli eventi (la cattedrale per San Giustino…) tra i monumenti
                _content.Life?.Calendar?.LocatePlaces(one);
                var light = new Orbis.Core.World.Tiles.TileData
                {
                    Id = tile.Id, Size = tile.Size, Scale = tile.Scale, Roads = tile.Roads, Lots = tile.Lots,
                };
                return (light, ox, oy);
            }
        }

        /// <summary>Tile del comune (solo strade e lotti, dal caricatore della città) e sedi dei servizi trovate.</summary>
        internal void OnCityTiles(List<(Orbis.Core.World.Tiles.TileData Tile, double OriginX, double OriginY)> tiles, (double, double, double, double) area,
            List<Orbis.Core.Population.ServiceSource> services)
        {
            _cityTiles = tiles;
            _cityArea = area;
            // le sedi dei servizi del catalogo si trovano anche nei lotti lontani dalla camera
            Session.Context.CityWorld = new CityTilesAccess(tiles, Session.Context.World);
            if (Content.Life?.Services != null) _serviceSources = services;
            // i luoghi degli eventi ora sono noti: si riprepara la giornata
            var pop = Session.Population;
            if (pop != null && Session.Context.Life?.Events != null) Session.Context.Life.Events.Plan(pop, State.Now.Date);
            WireCity();
        }

        private List<Orbis.Core.Population.ServiceSource> _serviceSources;

        /// <summary>
        /// Grafo stradale (strade vere più quelle del sindaco) e tempi a strada libera, poche zone per fotogramma.
        /// Se nel frattempo il sindaco cambia di nuovo le strade, questa costruzione si abbandona per la successiva.
        /// </summary>
        internal IEnumerator BuildTraffic()
        {
            var b = Content.Traffic;
            if (b == null || _cityTiles == null || _cityTiles.Count == 0) yield break;
            int version = ++_trafficVersion;
            _trafficBuilding = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var frame = System.Diagnostics.Stopwatch.StartNew();
                _roadsSignature = RoadsSignature();
                // un tile alla volta, pochi millisecondi per fotogramma: sul web un lavoro lungo blocca la pagina
                var builder = new Orbis.Core.Traffic.RoadGraph.Builder(b, _cityArea);
                foreach (var (tile, ox, oy) in _cityTiles)
                {
                    var piece = System.Diagnostics.Stopwatch.StartNew();
                    builder.Add(tile, ox, oy);
                    SlowWork.Log($"rete stradale: tile {tile.Id} ({tile.Roads.Length} strade)", piece);
                    if (frame.ElapsedMilliseconds >= SlowWork.FrameBudgetMs)
                    {
                        yield return null;
                        frame.Restart();
                        if (version != _trafficVersion || Session == null) yield break;
                    }
                }
                foreach (var r in Infrastructure.GraphRoads(State)) builder.AddExtra(r);
                var finish = System.Diagnostics.Stopwatch.StartNew();
                var graph = builder.Finish();
                var traffic = new Orbis.Core.Traffic.TrafficModel(graph, b);
                SlowWork.Log("fusione della rete stradale", finish);
                yield return null;
                frame.Restart();
                // tempi a strada libera: una zona di partenza alla volta, finché c'è tempo nel fotogramma
                for (int a = 0; a < traffic.Zones; a++)
                {
                    var piece = System.Diagnostics.Stopwatch.StartNew();
                    traffic.RefreshRange(a, 1);
                    SlowWork.Log($"tempi dalla zona {a}", piece);
                    if (frame.ElapsedMilliseconds >= SlowWork.FrameBudgetMs)
                    {
                        yield return null;
                        frame.Restart();
                        if (version != _trafficVersion || Session == null) yield break;
                    }
                }
                if (version != _trafficVersion || Session == null) yield break;
                OnTrafficReady(traffic);
                Debug.Log($"[Orbis] rete stradale: {graph.NodeCount} incroci, {graph.EdgeCount} tratti, {traffic.Zones} zone, " +
                          $"{State.Roads.Count} strade del sindaco in {sw.ElapsedMilliseconds} ms");
            }
            finally
            {
                if (version == _trafficVersion) _trafficBuilding = false;
            }
        }

        /// <summary>La rete stradale si sta (ri)costruendo.</summary>
        public bool TrafficBuilding => _trafficBuilding;

        private string RoadsSignature()
        {
            var sb = new StringBuilder();
            foreach (var r in State.Roads.Values)
            {
                sb.Append(r.Id).Append(':').Append((int)r.Class).Append(r.Oneway).Append(r.Bridge ? 'p' : '-');
                foreach (var v in r.Points) sb.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(';');
            }
            return sb.ToString();
        }

        private string LinesSignature()
        {
            var sb = new StringBuilder();
            foreach (var l in State.Lines.Values)
            {
                sb.Append(l.Id).Append(':').Append(l.HeadwayMinutes).Append('/').Append(l.FareCents).Append('/').Append((int)l.Mode);
                foreach (var v in l.Stops) sb.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Dopo un comando, un annulla o un riavvolgimento: rete stradale e linee seguono le scelte del sindaco.</summary>
        private void SyncInfrastructure()
        {
            if (_cityTiles != null && RoadsSignature() != _roadsSignature) StartCoroutine(BuildTraffic());
            if (Transit != null)
            {
                var lines = LinesSignature();
                if (lines != _linesSignature)
                {
                    _linesSignature = lines;
                    Transit.SetCityLines(Infrastructure.NetworkLines(State));
                }
            }
        }

        /// <summary>Collega i cittadini appena generati, mantenendo il saldo della città stimata, e salva.</summary>
        internal void OnPopulationGenerated(Orbis.Core.Population.CityPopulation population)
        {
            Session.AttachPopulation(population, Content.Life, keepBalance: true, _serviceSources);
            WireCity();
            _saveRequested = true;
            PopulationReady?.Invoke(population);
            Changed?.Invoke();
        }

        private void Update()
        {
            if (Session == null || World.State != WorldSession.Phase.Playing) return;
            EnsureFrame();
            UpdateWeatherFeed();
            // tempo di gioco: 1 ora = 1 minuto reale × velocità (pausa = 0)
            float speed = World.Clock.Speed;
            _pendingGameSeconds += Time.unscaledDeltaTime * GameSession.GameSecondsPerRealSecond * speed;
            if (_pendingGameSeconds >= 30)
            {
                var span = TimeSpan.FromSeconds(Math.Min(_pendingGameSeconds, 6 * 3600));
                _pendingGameSeconds = 0;
                _events.Clear();
                var sim = System.Diagnostics.Stopwatch.StartNew();
                var from = State.Now;
                Session.Simulation.Advance(Session.Context, span, _events);
                SlowWork.Log($"simulazione {from:HH:mm}→{State.Now:HH:mm}", sim);
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
                var sync = System.Diagnostics.Stopwatch.StartNew();
                View.Sync(State);
                RoadsView.Sync(State);
                SlowWork.Log("aggiornamento degli elementi", sync);
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
            EnsureFrame();
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
            RoadsView.Sync(State);
            SyncInfrastructure();
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
            if (World != null) World.GameWeather = null;
            View?.Dispose();
            RoadsView?.Dispose();
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
