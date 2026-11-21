using System;
using System.Collections;
using System.Diagnostics;
using Orbis.Core.Population;
using Orbis.Core.World.Tiles;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Genera i cittadini del comune quando la partita non li ha ancora (M4, docs/03 §2): carica i tile del comune
    /// uno alla volta, raccoglie case, lavori e scuole e fa la generazione a fasi, un pezzo per fotogramma, mentre
    /// si gioca. Alla fine li collega alla partita con lo stesso saldo della città stimata.
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
            _game.OnPopulationStarted(life.Data.MunicipalityName);
            var places = new CityPlaces(streamer.Region, municipality, life.Balance);
            var tiles = CityPlaces.TilesFor(streamer.Region, municipality);
            for (int i = 0; i < tiles.Count; i++)
            {
                var id = tiles[i];
                byte[] file = null;
                yield return streamer.Source.Fetch(id.Path, b => file = b);
                if (file != null)
                {
                    try
                    {
                        places.Add(TileReader.ReadFile(file, streamer.Region.Scale));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Orbis] tile {id} illeggibile per i cittadini: {e.Message}");
                    }
                }
                Progress = 0.4f * (i + 1) / tiles.Count;
            }
            Debug.Log($"[Orbis] {life.Data.MunicipalityName}: {places.Dwellings.Count} case, {places.Workplaces.Count} posti di lavoro, " +
                      $"{places.Schools.Count} scuole da {tiles.Count} tile");

            var generator = new PopulationGenerator(life.Data, life.Names, life.Balance, places, _game.State.Seed);
            foreach (var step in generator.Steps())
            {
                Progress = 0.4f + 0.6f * step;
                yield return null; // una fase per fotogramma
            }
            var population = generator.Result;
            Debug.Log($"[Orbis] {population.Count} cittadini e {population.Households.Count} famiglie generati in {sw.ElapsedMilliseconds} ms");
            if (_game != null && _game.Session != null && _game.Session.Population == null) _game.OnPopulationGenerated(population);
            Progress = 1;
            Destroy(this);
        }
    }
}
