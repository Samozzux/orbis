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
            var loaded = new List<(TileData Tile, double OriginX, double OriginY)>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                var id = tiles[i];
                byte[] file = null;
                yield return streamer.Source.Fetch(id.Path, b => file = b);
                if (file != null)
                {
                    try
                    {
                        var tile = TileReader.ReadFile(file, streamer.Region.Scale);
                        streamer.Frame.TileOrigin(id, out var ox, out var oy);
                        loaded.Add((tile, ox, oy));
                        if (generate) places.Add(tile);
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
            if (generate) yield return Generate(life, places, sw);
            // rete stradale: il riquadro del comune con un margine (i tile interi arrivano molto più in là)
            streamer.Frame.FromLonLat(municipality.MinLon, municipality.MinLat, out var ax, out var ay);
            streamer.Frame.FromLonLat(municipality.MaxLon, municipality.MaxLat, out var bx, out var by);
            double m = _game.Content.Traffic != null ? _game.Content.Traffic.AreaMarginM : 1000;
            yield return BuildTraffic(loaded, (Math.Min(ax, bx) - m, Math.Min(ay, by) - m, Math.Max(ax, bx) + m, Math.Max(ay, by) + m));
            Progress = 1;
            Destroy(this);
        }

        private IEnumerator BuildTraffic(List<(TileData Tile, double OriginX, double OriginY)> tiles,
            (double, double, double, double) area)
        {
            var b = _game.Content.Traffic;
            if (b == null || tiles.Count == 0) yield break;
            var sw = Stopwatch.StartNew();
            var graph = RoadGraph.Build(tiles, b, area);
            var traffic = new TrafficModel(graph, b);
            yield return null;
            // tempi a strada libera, poche zone per fotogramma (finché mancano, si stima in linea d'aria)
            for (int a = 0; a < traffic.Zones; a += 5)
            {
                traffic.RefreshRange(a, 5);
                yield return null;
            }
            if (_game == null || _game.Session == null) yield break;
            _game.OnTrafficReady(traffic);
            Debug.Log($"[Orbis] rete stradale: {graph.NodeCount} incroci, {graph.EdgeCount} tratti, {traffic.Zones} zone in {sw.ElapsedMilliseconds} ms");
        }

        private IEnumerator Generate(LifeInputs life, CityPlaces places, Stopwatch sw)
        {
            var generator = new PopulationGenerator(life.Data, life.Names, life.Balance, places, _game.State.Seed);
            foreach (var step in generator.Steps())
            {
                Progress = 0.4f + 0.6f * step;
                yield return null; // una fase per fotogramma
            }
            var population = generator.Result;
            Debug.Log($"[Orbis] {population.Count} cittadini e {population.Households.Count} famiglie generati in {sw.ElapsedMilliseconds} ms");
            if (_game != null && _game.Session != null && _game.Session.Population == null) _game.OnPopulationGenerated(population);
        }
    }
}
