using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Core.Population;
using Orbis.Gameplay;
using Orbis.Platform;
using Orbis.UI.App;
using Orbis.UI.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Nuova partita su Chieti (M4): i cittadini si generano dai tile veri del comune mentre si gioca, entrano nel
    /// salvataggio e vivono (stipendi, tasse). Serve il pack v4 in StreamingAssets (in CI manca: il test viene ignorato).
    /// </summary>
    public class PopulationPlayTests
    {
        private sealed class TestServices : PlatformServicesBase
        {
            public TestServices() : base(PlatformKind.Editor)
            {
                DeviceProfile = new UnityDeviceProfile(PlatformKind.Editor, () => 1f, () => false);
            }
        }

        [SetUp]
        public void Setup() => PlatformServices.Override(new TestServices());

        [TearDown]
        public void TearDown() => PlatformServices.Override(null);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator NuovaPartita_CittadiniDaiTileVeri()
        {
            var previous = OrbisApp.Instance;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 30f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            var app = OrbisApp.Instance;
            app.StartGame(new GameStart { Name = "Cittadini", Transient = true });
            var session = app.Session;
            deadline = Time.realtimeSinceStartup + 90f;
            while (session.State == WorldSession.Phase.Loading && Time.realtimeSinceStartup < deadline) yield return null;
            if (session.State != WorldSession.Phase.Playing)
            {
                app.EndGame();
                Assert.Ignore("pack del mondo assente (normale in CI)");
            }
            var game = app.Game;
            if (game.Session.Population == null && game.GetComponent<PopulationLoader>() == null)
            {
                app.EndGame();
                Assert.Ignore("pack senza confini dei comuni (serve il v4)");
            }

            deadline = Time.realtimeSinceStartup + 180f;
            while (game.Session.Population == null && Time.realtimeSinceStartup < deadline) yield return null;
            var pop = game.Session.Population;
            Assert.That(pop, Is.Not.Null, "i cittadini non sono arrivati entro 3 minuti");
            Assert.That(pop.Count, Is.EqualTo(game.Content.Life.Data.Total));
            Assert.That(pop.MunicipalityName, Is.EqualTo("Chieti"));
            Assert.That(pop.Dwellings.Count, Is.GreaterThan(2000), "poche case trovate nei tile del comune");
            Assert.That(pop.Workplaces.Count, Is.GreaterThan(200), "pochi posti di lavoro trovati");
            Assert.That(pop.Schools.Count, Is.GreaterThan(10), "poche scuole trovate");
            Assert.That(pop.Households.All(h => h.Dwelling >= 0), "famiglie senza casa");
            Debug.Log($"[Test] Chieti: {pop.Dwellings.Count} case, {pop.Workplaces.Count} lavori, {pop.Schools.Count} scuole, " +
                      $"{pop.Households.Count} famiglie; prima: {pop.FullName(0)}");

            // la rete stradale vera del comune con il traffico
            deadline = Time.realtimeSinceStartup + 60f;
            while (game.Traffic == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.Traffic, Is.Not.Null, "rete stradale non costruita");
            Assert.That(game.Traffic.Graph.NodeCount, Is.GreaterThan(1000));
            Assert.That(game.Session.Context.Routines.Travel, Is.SameAs(game.Traffic));
            var tick = System.Diagnostics.Stopwatch.StartNew();
            game.Traffic.Tick(Orbis.Core.Population.CityRoutines.Minute(game.State.Now));
            tick.Stop();
            Debug.Log($"[Test] rete: {game.Traffic.Graph.NodeCount} incroci, {game.Traffic.Graph.EdgeCount} tratti, {game.Traffic.Zones} zone; " +
                      $"un passo del traffico {tick.ElapsedMilliseconds} ms");

            // le linee reali del pack v4 (bus urbani, filovia, treni)
            deadline = Time.realtimeSinceStartup + 10f;
            while (game.Transit == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.Transit, Is.Not.Null, "linee del trasporto pubblico non preparate");
            Assert.That(game.Transit.Lines.Count, Is.GreaterThanOrEqualTo(5));
            Debug.Log("[Test] linee: " + string.Join(", ", game.Transit.Lines.Select(l => $"{l.Mode} {l.Ref} ({l.X.Length} fermate)")));

            // il sindaco disegna una strada libera vicino alla camera: la rete si ricostruisce con l'incrocio nuovo
            var ctx = game.Session.Context;
            game.State.Transact(Orbis.Core.Game.LedgerCategory.LevelRewards, 500_000); // fondi per la prova
            var graph = game.Traffic.Graph;
            var cam = Camera.main.transform.position;
            var nodes = Enumerable.Range(0, graph.NodeCount)
                .Where(n => !float.IsNaN(session.World.HeightAt(graph.X[n], graph.Y[n])))
                .OrderBy(n => (graph.X[n] - cam.x) * (graph.X[n] - cam.x) + (graph.Y[n] - cam.z) * (graph.Y[n] - cam.z))
                .Take(400).ToList();
            Orbis.Core.Game.BuildRoadCommand road = null;
            foreach (var n in nodes)
            {
                for (int k = 0; k < 16 && road == null; k++)
                {
                    float a = k * Mathf.PI / 8, x = graph.X[n], y = graph.Y[n];
                    var points = new[] { x + Mathf.Cos(a) * 10, y + Mathf.Sin(a) * 10, x + Mathf.Cos(a) * 90, y + Mathf.Sin(a) * 90 };
                    var candidate = new Orbis.Core.Game.CityRoad { Class = Orbis.Core.World.Fb.RoadClass.Residential, Points = points };
                    if (Orbis.Core.Game.Infrastructure.CheckRoad(ctx, candidate) == Orbis.Core.Game.BuildResult.Ok)
                        road = new Orbis.Core.Game.BuildRoadCommand { Class = candidate.Class, Points = points };
                }
                if (road != null) break;
            }
            Assert.That(road, Is.Not.Null, "nessuno spazio libero per una strada vicino alla camera");
            Assert.That(game.Do(road), Is.EqualTo(Orbis.Core.Game.BuildResult.Ok));
            var oldTraffic = game.Traffic;
            deadline = Time.realtimeSinceStartup + 60f;
            while ((game.Traffic == oldTraffic || game.TrafficBuilding) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.Traffic, Is.Not.SameAs(oldTraffic), "rete non ricostruita dopo la strada nuova");
            int own = game.Traffic.Graph.EdgesOfExtra(road.RoadId).Count;
            Assert.That(own, Is.GreaterThanOrEqualTo(2), "la strada nuova non è nel grafo");
            Assert.That(game.RoadsView.RootOf(road.RoadId), Is.Not.Null, "strada nuova non disegnata");
            Debug.Log($"[Test] strada del sindaco {road.RoadId}: {own} tratti nel grafo, {game.Traffic.Graph.NodeCount} incroci");

            // una linea bus del comune tra due incroci a 400–900 m: due sensi nella rete, costo del servizio a fine giornata
            graph = game.Traffic.Graph;
            int s0 = nodes[0];
            int s1 = nodes.FirstOrDefault(n =>
            {
                float d = Mathf.Sqrt((graph.X[n] - graph.X[s0]) * (graph.X[n] - graph.X[s0]) + (graph.Y[n] - graph.Y[s0]) * (graph.Y[n] - graph.Y[s0]));
                return d > 400 && d < 900;
            });
            if (s1 == 0) s1 = nodes[nodes.Count - 1];
            var line = new Orbis.Core.Game.CityLine
            {
                Stops = new[] { graph.X[s0], graph.Y[s0], graph.X[s1], graph.Y[s1] }, HeadwayMinutes = 15, FareCents = 130,
            };
            var open = new Orbis.Core.Game.SetLineCommand { Line = line };
            Assert.That(game.Do(open), Is.EqualTo(Orbis.Core.Game.BuildResult.Ok));
            Assert.That(game.Transit.Lines.Count(l => l.CityId == open.ResultId), Is.EqualTo(2));

            // un giorno di gioco con le reazioni realistiche: stipendi e tasse dei cittadini nel registro
            game.State.Config.CitizenReactions = ReactionLevel.Realistic;
            long before = game.State.Cash;
            var dayEvents = game.Session.Advance(System.TimeSpan.FromDays(1));
            var reactions = dayEvents.Where(e => e.Kind == Orbis.Core.Game.SimEventKind.Reaction).ToList();
            const int residential = (int)Orbis.Core.Game.LedgerCategory.ResidentialTax;
            var day = game.State.Ledger.LastOrDefault(d => d.Amounts[residential] != 0);
            Assert.That(day, Is.Not.Null, "nessun giorno con le tasse dei cittadini");
            Assert.That(day.Amounts[residential], Is.GreaterThan(10000));
            Assert.That(game.State.Stats.Population, Is.InRange(45000, 52000));
            Assert.That(game.State.Cash, Is.Not.EqualTo(before));
            long riders = game.Transit.Lines.Sum(l => l.Passengers);
            Debug.Log($"[Test] in un giorno: {riders} viaggi sui mezzi pubblici, " +
                      $"{Enumerable.Range(0, pop.Count).Count(i => pop.Citizens[i].Mode == TravelMode.Car)} in auto nell'ultimo viaggio");
            Assert.That(riders, Is.GreaterThan(0), "nessuno ha preso i mezzi pubblici");
            // servizi veri vicino a casa e felicità di ognuno (blocco 5)
            var services = game.Session.Context.Life.Services;
            Assert.That(services, Is.Not.Null, "servizi non calcolati (manca services_balance?)");
            Assert.That(services.Count(ServiceKind.Health), Is.GreaterThan(0), "nessuna sede sanitaria trovata");
            Assert.That(services.Count(ServiceKind.Education), Is.GreaterThan(10), "poche scuole tra i servizi");
            Assert.That(pop.AverageHappiness, Is.InRange(30f, 90f));
            Assert.That(game.State.Stats.Happiness, Is.EqualTo(pop.AverageHappiness).Within(15f));
            string Cover(ServiceKind k) =>
                $"{k} {services.Count(k)} sedi, {Enumerable.Range(0, pop.Dwellings.Count).Average(d => (double)services.At(d, k)):0}";
            Debug.Log($"[Test] felicità media {pop.AverageHappiness:0.0}, scontenti {pop.UnhappyCount}; fattori: " +
                      string.Join(", ", System.Enum.GetValues(typeof(HappinessFactor)).Cast<HappinessFactor>()
                          .Where(f => f != HappinessFactor.Count).Select(f => $"{f} {pop.HappinessFactors[(int)f]:0}")) +
                      "; copertura: " + string.Join(", ", new[] { ServiceKind.Police, ServiceKind.Fire, ServiceKind.Health, ServiceKind.Education, ServiceKind.Culture }.Select(Cover)));

            // reazioni del giorno: commenti nelle notizie, gradimento; petizioni e proteste solo se la città è scontenta
            Assert.That(pop.Feed.Count(r => r.Kind == ReactionKind.Comment), Is.GreaterThan(0), "nessun commento dei cittadini");
            Assert.That(pop.Approval, Is.InRange(1f, 100f));
            Debug.Log($"[Test] reazioni: gradimento {pop.Approval:0}%, {pop.Feed.Count} nel feed, {reactions.Count} avvisi " +
                      $"({string.Join(", ", reactions.Select(e => e.Key + " " + e.Amount))}), petizioni aperte: " +
                      string.Join(", ", pop.Petitions.Select(p => $"{p.Factor} {p.Signatures} firme")) +
                      "; preoccupazioni: " + string.Join(", ", Enumerable.Range(1, (int)HappinessFactor.Count - 1)
                          .Select(f => $"{(HappinessFactor)f} {Enumerable.Range(0, pop.Count).Count(i => pop.Citizens[i].Alive && pop.Citizens[i].Worry == f)}")));

            // calendario: la cattedrale di San Giustino tra i monumenti, la festa del patrono con il raduno lì vicino
            var events = game.Session.Context.Life.Events;
            Assert.That(events, Is.Not.Null, "calendario degli eventi non caricato");
            Assert.That(events.Calendar.Places.ContainsKey("cattedrale_san_giustino"), "cattedrale non trovata tra i monumenti");
            var patron = events.Plan(pop, new System.DateTime(2026, 5, 11)).Single(e => e.Key == "san_giustino");
            Assert.That(patron.Place, Is.GreaterThanOrEqualTo(0));
            Debug.Log($"[Test] eventi: San Giustino al locale «{pop.Workplaces[patron.Place].Name}» ({pop.Workplaces[patron.Place].Category}); " +
                      $"notizie: {string.Join(", ", pop.News.Select(e => e.Key + (e.Detail != "" ? " " + e.Detail : "")))}");
            events.Plan(pop, game.State.Now.Date);

            const int transitCosts = (int)Orbis.Core.Game.LedgerCategory.TransitCosts;
            Assert.That(game.State.Ledger.Any(d => d.Amounts[transitCosts] < 0), "il servizio della linea del comune non si è pagato");
            Debug.Log($"[Test] linea del comune: {game.Transit.CityPassengers(open.ResultId)} passeggeri, " +
                      $"{pop.LastCityFares} € di biglietti nell'ultimo giorno");

            // il salvataggio porta i cittadini con sé
            var bytes = game.Session.Save(System.DateTime.UtcNow);
            var back = GameController.LoadSession(bytes, game.Content, session);
            Assert.That(back.Population, Is.Not.Null);
            Assert.That(back.Population.Count, Is.EqualTo(pop.Count));
            app.EndGame();
        }
    }
}
