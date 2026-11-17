using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orbis.Core.Device;
using Orbis.Core.Game;
using Orbis.Core.World.Lots;
using Orbis.Gameplay;
using Orbis.Platform;
using Orbis.UI.App;
using Orbis.UI.Game;
using Orbis.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orbis.Tests.PlayMode
{
    /// <summary>
    /// Partita vera su Chieti (M2): costruire su un lotto reale, annullare e ripetere, demolire un edificio OSM,
    /// far passare un mese di gioco, salvare, uscire e riaprire trovando tutto com'era.
    /// Serve il pack del mondo in StreamingAssets (in CI manca: il test viene ignorato).
    /// </summary>
    public class GameplayTests
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
        public IEnumerator Partita_CostruireAnnullareUnMeseSalvareRiaprire()
        {
            var previous = OrbisApp.Instance;
            SceneManager.LoadScene("Boot");
            var deadline = Time.realtimeSinceStartup + 30f;
            while ((OrbisApp.Instance == null || OrbisApp.Instance == previous || OrbisApp.Instance.State != OrbisApp.AppState.MainMenu)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            var app = OrbisApp.Instance;
            app.StartGame(new GameStart { Name = "Prova" });
            var session = app.Session;
            deadline = Time.realtimeSinceStartup + 90f;
            while (session.State == WorldSession.Phase.Loading && Time.realtimeSinceStartup < deadline) yield return null;
            if (session.State != WorldSession.Phase.Playing)
            {
                app.EndGame();
                Assert.Ignore("pack del mondo assente (normale in CI)");
            }
            var game = app.Game;
            Assert.That(game, Is.Not.Null);
            var ctx = game.Session.Context;
            long startCash = game.State.Cash;

            // 1) una casa sbloccata al livello 1, nel primo spazio libero di un lotto vero vicino al centro
            var item = game.Content.Catalog.InCategory(CatalogCategory.Residential)
                .Where(i => i.UnlockLevel <= 1).OrderBy(i => i.Width * i.Depth).First();
            Assert.That(FindSpot(session, ctx, item, out var lot, out var hx, out var hy), Is.True, "nessun lotto libero per " + item.Key);
            var r = game.Do(new PlaceCommand { Item = item.Key, Lot = lot, HalfX = hx, HalfY = hy, Rotation = 0 });
            Assert.That(r, Is.EqualTo(BuildResult.Ok));
            int id = game.State.Objects.Keys.Single();
            long cost = Placement.CostOf(item, null);
            Assert.That(game.State.Today().Amounts[(int)LedgerCategory.Construction], Is.EqualTo(-cost), "il costo non è stato pagato");
            // la prima casa completa la prima missione: il premio arriva in cassa
            long rewards = game.State.Today().Amounts[(int)LedgerCategory.Missions] + game.State.Today().Amounts[(int)LedgerCategory.LevelRewards];
            Assert.That(game.State.MissionIndex, Is.GreaterThan(0), "la missione della prima casa non si è completata");
            Assert.That(game.State.Cash, Is.EqualTo(startCash - cost + rewards));
            yield return null;
            Assert.That(game.View.RootOf(id), Is.Not.Null, "il modello non è comparso nella scena");
            // lo stesso posto ora è occupato
            Assert.That(Placement.CanBuild(ctx, item, null, lot, hx, hy, 0), Is.EqualTo(BuildResult.Occupied));

            // 2) annulla e ripeti
            Assert.That(game.Undo(), Is.True);
            Assert.That(game.State.Objects.Count, Is.EqualTo(0));
            Assert.That(game.State.Cash, Is.EqualTo(startCash), "annullare deve restituire tutto");
            Assert.That(game.Redo(), Is.True);
            Assert.That(game.State.Objects.Count, Is.EqualTo(1));

            // 3) demolire un edificio reale lo nasconde (e annullando torna)
            var tile = session.World.Tiles.Values.First(v => v.Lod == TileLod.Full && v.Data.Buildings.Length > 50);
            var building = tile.Data.Buildings.First(b => b.Bounds.Width > 8 && b.Bounds.Width < 30);
            long osmKey = GameState.OsmKey(building.OsmType, building.OsmId);
            r = game.Do(new DemolishOsmCommand { OsmKey = osmKey, Lot = new LotKey(tile.Id.X, tile.Id.Y, 0), AreaM2 = 100 });
            Assert.That(r, Is.EqualTo(BuildResult.Ok));
            Assert.That(session.World.HiddenBuildings.Contains(osmKey), Is.True, "l'edificio demolito è ancora visibile");
            Assert.That(game.Undo(), Is.True);
            Assert.That(session.World.HiddenBuildings.Contains(osmKey), Is.False, "annullando l'edificio non è tornato");

            // 4) un mese di gioco: i lavori finiscono, l'economia gira, ci sono le istantanee per riavvolgere
            var start = game.State.Now;
            game.Session.Advance(TimeSpan.FromDays(31));
            Assert.That(game.State.Now, Is.GreaterThanOrEqualTo(start.AddDays(31)));
            Assert.That(game.State.Objects[id].IsReady(game.State.Now), Is.True, "la casa non è stata finita in un mese");
            Assert.That(game.State.Ledger.Count, Is.GreaterThanOrEqualTo(30), "il bilancio non ha registrato i giorni");
            Assert.That(game.Session.Snapshots.Items.Count, Is.GreaterThanOrEqualTo(30), "mancano i punti di ripristino");
            long cashAfterMonth = game.State.Cash;
            var now = game.State.Now;

            // 5) riavvolgi a 10 giorni fa e poi torna al menu: la partita si salva
            var snap = game.Session.Snapshots.Items.Last(s => s.GameTime <= now.AddDays(-10));
            game.Rewind(snap);
            Assert.That(game.State.Now, Is.EqualTo(snap.GameTime));
            Assert.That(game.State.Objects.ContainsKey(id), Is.True);
            long cashSaved = game.State.Cash;
            Assert.That(cashSaved, Is.Not.EqualTo(cashAfterMonth), "riavvolgere non ha cambiato la cassa");
            string saveId = game.SaveId;
            app.EndGame();
            for (int i = 0; i < 5; i++) yield return null;

            // 6) riaprendo è tutto com'era
            app.StartGame(new GameStart { SaveId = saveId });
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(app.State, Is.EqualTo(OrbisApp.AppState.Game), "la partita salvata non si è riaperta");
            game = app.Game;
            Assert.That(game.State.Name, Is.EqualTo("Prova"));
            Assert.That(game.State.Objects.Count, Is.EqualTo(1));
            Assert.That(game.State.Objects[id].ItemKey, Is.EqualTo(item.Key));
            Assert.That(game.State.Cash, Is.EqualTo(cashSaved));
            Assert.That(game.State.Now, Is.EqualTo(snap.GameTime));
            app.EndGame();
            yield return null;
        }

        /// <summary>Prima posizione libera nei lotti dei tile vicini al centro (una mappa di occupazione per lotto).</summary>
        internal static bool FindSpot(WorldSession session, GameContext ctx, CatalogItem item, out LotKey key, out int hx, out int hy)
        {
            int w = item.CellsX, h = item.CellsY;
            foreach (var view in session.World.Tiles.Values.Where(v => v.Lod == TileLod.Full).OrderBy(v => v.Origin.sqrMagnitude).Take(4))
            {
                foreach (var lot in view.Data.Lots.Where(l => l.Area > 300 && l.Bounds.Width < 300 && l.Bounds.Height < 300))
                {
                    var k = new LotKey(view.Id.X, view.Id.Y, lot.Id);
                    LotGrid grid;
                    try
                    {
                        grid = Placement.Occupancy(ctx, k, item.CellSize);
                    }
                    catch (ArgumentException)
                    {
                        Debug.Log($"[Orbis] lotto {lot.Id} saltato: {lot.Bounds.Width:0}×{lot.Bounds.Height:0} m, angolo {lot.GridAngle}, origine {lot.GridOriginX:0},{lot.GridOriginY:0}");
                        continue;
                    }
                    if (grid == null) continue;
                    for (int cy = grid.MinY; cy <= grid.MinY + grid.Height - h; cy++)
                    for (int cx = grid.MinX; cx <= grid.MinX + grid.Width - w; cx++)
                    {
                        if (grid.CanPlace(cx, cy, w, h) != Orbis.Core.World.Lots.PlacementResult.Ok) continue;
                        int x = (int)Math.Round(cx * item.CellSize * 2), y = (int)Math.Round(cy * item.CellSize * 2);
                        if (Placement.CanBuild(ctx, item, null, k, x, y, 0) != BuildResult.Ok) continue;
                        key = k;
                        hx = x;
                        hy = y;
                        return true;
                    }
                }
            }
            key = default;
            hx = hy = 0;
            return false;
        }
    }
}
