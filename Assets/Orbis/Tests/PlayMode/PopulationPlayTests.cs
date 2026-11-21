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

            // un giorno di gioco: stipendi e tasse dei cittadini nel registro, saldo come prima
            long before = game.State.Cash;
            game.Session.Advance(System.TimeSpan.FromDays(1));
            const int residential = (int)Orbis.Core.Game.LedgerCategory.ResidentialTax;
            var day = game.State.Ledger.LastOrDefault(d => d.Amounts[residential] != 0);
            Assert.That(day, Is.Not.Null, "nessun giorno con le tasse dei cittadini");
            Assert.That(day.Amounts[residential], Is.GreaterThan(10000));
            Assert.That(game.State.Stats.Population, Is.InRange(45000, 52000));
            Assert.That(game.State.Cash, Is.Not.EqualTo(before));

            // il salvataggio porta i cittadini con sé
            var bytes = game.Session.Save(System.DateTime.UtcNow);
            var back = GameController.LoadSession(bytes, game.Content, session);
            Assert.That(back.Population, Is.Not.Null);
            Assert.That(back.Population.Count, Is.EqualTo(pop.Count));
            app.EndGame();
        }
    }
}
