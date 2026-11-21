using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Orbis.Core.Population;
using Orbis.Core.Traffic;
using Orbis.Core.World.Tiles;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Città viva all'avvio della partita (M4): carica i tile del comune uno alla volta e
    /// 1) se la partita non ha ancora i cittadini (docs/03 §2), raccoglie case, lavori e scuole e li genera a fasi,
    ///    un pezzo per fotogramma, mentre si gioca; poi li collega con lo stesso saldo della città stimata;
    /// 2) costruisce la rete stradale con il traffico (docs/03 §7) e la passa alla giornata dei cittadini.
    /// </summary>
    public sealed class PopulationLoader : MonoBehaviour
    {
        private GameController _game;

        /// <summary>Avanzamento (0–1), per l'interfaccia.</summary>
        public float Progress { get; private set; }

        public static PopulationLoader Attach(GameController game)
        {
            var l = game.gameObject.AddComponent<PopulationLoader>();
            l._game = game;
            return l;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            var world = _game.World;
            // il pack deve essere aperto (indice con i comuni)
            while (world.State == WorldSession.Phase.Loading) yield return null;
            var streamer = world.World;
            var life = _game.Content.Life;
            if (world.State != WorldSession.Phase.Playing || streamer.Region == null || streamer.Source == null || life == null)
            {
                Destroy(this);
                yield break;
            }
            var municipality = streamer.Region.Municipality(life.Data.MunicipalityCode);
            if (municipality == null)
            {
                Debug.LogWarning($"[Orbis] il pack non ha il confine del comune {life.Data.MunicipalityCode}: niente cittadini (serve il pack v4)");
                Destroy(this);
                yield break;
            }

            var sw = Stopwatch.StartNew();
            bool generate = _game.Session.Population == null;
            if (generate) _game.OnPopulationStarted(life.Data.MunicipalityName);
            var places = new CityPlaces(streamer.Region, municipality, life.Balance);
            var tiles = CityPlaces.TilesFor(streamer.Region, municipality);
            // un tile alla volta: se ne prende ciò che serve e poi si lascia andare (sul web la memoria non torna indietro)
            var loaded = new List<(TileData Tile, double OriginX, double OriginY)>(tiles.Count);
            var city = new GameController.CityTilesBuilder(_game.Content);
            for (int i = 0; i < tiles.Count; i++)
            {
                var id = tiles[i];
                byte[] file = null;
                yield return streamer.Source.Fetch(id.Path, b => file = b);
                if (file != null)
                {
                    try
                    {
                        var tileWork = Stopwatch.StartNew();
                        var tile = TileReader.ReadFile(file, streamer.Region.Scale);
                        file = null;
                        streamer.Frame.TileOrigin(id, out var ox, out var oy);
                        if (generate) places.Add(tile);
                        loaded.Add(city.Keep(tile, ox, oy));
                        SlowWork.Log($"tile {id} per la città", tileWork);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Orbis] tile {id} illeggibile per la città: {e.Message}");
                    }
                }
                Progress = 0.4f * (i + 1) / tiles.Count;
            }
            if (generate)
                Debug.Log($"[Orbis] {life.Data.MunicipalityName}: {places.Dwellings.Count} case, {places.Workplaces.Count} posti di lavoro, " +
                          $"{places.Schools.Count} scuole da {tiles.Count} tile");
            // rete stradale: il riquadro del comune con un margine (i tile interi arrivano molto più in là)
            streamer.Frame.FromLonLat(municipality.MinLon, municipality.MinLat, out var ax, out var ay);
            streamer.Frame.FromLonLat(municipality.MaxLon, municipality.MaxLat, out var bx, out var by);
            double m = _game.Content.Traffic != null ? _game.Content.Traffic.AreaMarginM : 1000;
            var area = (Math.Min(ax, bx) - m, Math.Min(ay, by) - m, Math.Max(ax, bx) + m, Math.Max(ay, by) + m);
            // la partita tiene i tile del comune: sedi dei servizi per i cittadini, rete ricostruita a ogni strada nuova
            _game.OnCityTiles(loaded, area, city.ServiceSources);
            if (generate) yield return Generate(life, places, sw);
            yield return _game.BuildTraffic();
            // linee reali di bus, filobus e treni con almeno due fermate nell'area, più quelle del comune
            if (_game != null && _game.Session != null && _game.Content.Transit != null)
            {
                var transit = TransitNetwork.Build(streamer.Region, _game.Content.Transit, area);
                _game.OnTransitReady(transit);
                Debug.Log($"[Orbis] trasporto pubblico: {transit.Lines.Count} linee");
            }
            Progress = 1;
            Destroy(this);
        }

        private IEnumerator Generate(LifeInputs life, CityPlaces places, Stopwatch sw)
        {
            var generator = new PopulationGenerator(life.Data, life.Names, life.Balance, places, _game.State.Seed);
            var phase = Stopwatch.StartNew();
            foreach (var step in generator.Steps())
            {
                SlowWork.Log($"generazione dei cittadini fino al {step:P0}", phase);
                Progress = 0.4f + 0.6f * step;
                yield return null; // una fase per fotogramma
                phase.Restart();
            }
            var population = generator.Result;
            Debug.Log($"[Orbis] {population.Count} cittadini e {population.Households.Count} famiglie generati in {sw.ElapsedMilliseconds} ms");
            if (_game != null && _game.Session != null && _game.Session.Population == null) _game.OnPopulationGenerated(population);
        }
    }
}
