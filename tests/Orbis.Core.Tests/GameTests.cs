using System;
using System.Linq;
using Orbis.Core.Data;
using Orbis.Core.Game;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void LeggeOggettiArrayStringheENumeri()
        {
            var j = Json.Parse("// commento\n{\"a\": [1, 2.5, -3e2], \"b\": {\"c\": \"x\\\"y\\u00e8\"}, \"t\": true, \"n\": null}");
            Assert.Equal(3, j["a"].Count);
            Assert.Equal(2.5, j["a"][1].AsDouble());
            Assert.Equal(-300, j["a"][2].AsInt());
            Assert.Equal("x\"yè", j["b"]["c"].AsString());
            Assert.True(j["t"].AsBool());
            Assert.True(j["n"].IsNull);
            Assert.True(j["manca"].IsNull);
            Assert.Equal(new[] { "a", "b", "t", "n" }, j.Keys.ToArray());
            Assert.Throws<JsonException>(() => Json.Parse("{\"a\": 1,}"));
            Assert.Throws<JsonException>(() => Json.Parse("[1, 2"));
        }
    }

    /// <summary>Mondo finto: un tile con un lotto 60×40 m e un edificio OSM 10×10 m dentro.</summary>
    internal sealed class FakeWorld : IWorldAccess
    {
        public readonly TileData Tile0;

        public FakeWorld()
        {
            var lotOuter = new float[] { 0, 0, 60, 0, 60, 40, 0, 40 };
            var bOuter = new float[] { 5, 5, 15, 5, 15, 15, 5, 15 };
            Tile0 = new TileData
            {
                Size = 1800,
                Lots = new[] { new LotInfo { Id = 7, Outer = lotOuter, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(lotOuter), Area = 2400 } },
                Buildings = new[] { new BuildingInfo { OsmId = 99, OsmType = OsmType.Way, Outer = bOuter, Holes = Array.Empty<float[]>(), Height = 9, Bounds = Bounds2.Of(bOuter) } },
                Terrain = TerrainInfo.Flat(1800),
            };
        }

        public TileData Tile(int x, int y) => x == 1 && y == 2 ? Tile0 : null;
    }

    internal static class TestGame
    {
        public static readonly LotKey Lot = new LotKey(1, 2, 7);

        public const string CatalogJson = @"{""items"": [
          {""key"": ""casa"", ""category"": ""residential"", ""name"": ""Casa"", ""footprint"": {""w"": 10, ""d"": 8}, ""height"": 7,
           ""model"": {""family"": ""house"", ""roof"": ""hipped""},
           ""levels"": [{""level"": 1, ""cost"": 20000, ""buildHours"": 6, ""capacity"": 4, ""rentPerMonth"": 1200, ""maintenancePerMonth"": 60},
                        {""level"": 2, ""cost"": 15000, ""buildHours"": 8, ""capacity"": 6, ""rentPerMonth"": 1800, ""maintenancePerMonth"": 80}],
           ""variants"": [{""key"": ""ocra"", ""wall"": ""#e0b872""}, {""key"": ""pietra"", ""wall"": ""#cdbb9a"", ""costMultiplier"": 1.2}],
           ""tags"": [""casa""]},
          {""key"": ""panchina"", ""category"": ""decoration"", ""footprint"": {""w"": 1.5, ""d"": 0.5}, ""height"": 1,
           ""model"": {""family"": ""bench""}, ""levels"": [{""level"": 1, ""cost"": 300}]},
          {""key"": ""pala"", ""category"": ""energy"", ""footprint"": {""w"": 12, ""d"": 12}, ""height"": 90, ""unlock"": {""playerLevel"": 3},
           ""model"": {""family"": ""wind""}, ""levels"": [{""level"": 1, ""cost"": 120000, ""powerKw"": 600, ""maintenancePerMonth"": 900}]}
        ]}";

        public const string MissionsJson = @"{""missions"": [
          {""key"": ""m1"", ""title"": ""Prima casa"", ""condition"": {""type"": ""placed"", ""target"": ""Residential"", ""count"": 1}, ""reward"": {""xp"": 400, ""cash"": 5000}},
          {""key"": ""m2"", ""title"": ""Due case"", ""condition"": {""type"": ""tag"", ""target"": ""casa"", ""count"": 2}, ""reward"": {""xp"": 10, ""cash"": 1000}}
        ]}";

        public static Catalog Catalog()
        {
            var c = new Catalog();
            c.Load(CatalogJson);
            return c;
        }

        public static GameSession New(Balance b = null, FakeWorld world = null)
        {
            b = b ?? new Balance { StartCash = 100000, XpBase = 100, XpExponent = 1f };
            return GameSession.New("Prova", new WorldConfig(), new DateTime(2026, 11, 16, 10, 0, 0), new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc),
                1, Catalog(), b, world ?? new FakeWorld(), Missions.Parse(MissionsJson));
        }

        public static PlaceCommand Place(string item, int halfX, int halfY, int rot = 0) =>
            new PlaceCommand { Item = item, Lot = Lot, HalfX = halfX, HalfY = halfY, Rotation = rot };
    }

    public class CatalogTests
    {
        [Fact]
        public void CaricaEValida()
        {
            var c = TestGame.Catalog();
            Assert.Equal(3, c.Count);
            var casa = c.Get("casa");
            Assert.Equal(CatalogCategory.Residential, casa.Category);
            Assert.Equal(10, casa.CellsX);
            Assert.Equal(8, casa.CellsY);
            Assert.Equal(2, casa.MaxLevel);
            Assert.Equal(0x1E0B872u, casa.Variants[0].WallColor);
            var panchina = c.Get("panchina");
            Assert.Equal(0.5f, panchina.CellSize);
            Assert.Equal(3, panchina.CellsX);
            Assert.Throws<FormatException>(() => c.Load(@"{""items"": [{""key"": ""casa"", ""category"": ""residential"", ""footprint"": {""w"": 1, ""d"": 1}, ""levels"": [{""level"": 1, ""cost"": 1}]}]}"));
            // sagoma non multipla della griglia da 1 m
            Assert.Throws<FormatException>(() => new Catalog().Load(@"{""items"": [{""key"": ""x"", ""category"": ""commercial"", ""footprint"": {""w"": 2.5, ""d"": 1}, ""levels"": [{""level"": 1, ""cost"": 1}]}]}"));
        }
    }

    public class PlacementTests
    {
        [Fact]
        public void DentroIlLottoSuCelleLibere()
        {
            var g = TestGame.New();
            var ctx = g.Context;
            var casa = ctx.Catalog.Get("casa");
            Assert.Equal(BuildResult.Ok, Placement.CheckSpace(ctx, casa, TestGame.Lot, 40, 40, 0));        // (20 m, 20 m)
            Assert.Equal(BuildResult.Occupied, Placement.CheckSpace(ctx, casa, TestGame.Lot, 10, 10, 0));  // sopra l'edificio OSM
            Assert.Equal(BuildResult.OutsideLot, Placement.CheckSpace(ctx, casa, TestGame.Lot, 104, 0, 0)); // da 52 a 62 m: sporge
            Assert.Equal(BuildResult.Ok, Placement.CheckSpace(ctx, casa, TestGame.Lot, 104, 0, 1));        // ruotata: 8×10, entra
            Assert.Equal(BuildResult.LotNotLoaded, Placement.CheckSpace(ctx, casa, new LotKey(9, 9, 1), 0, 0, 0));
        }

        [Fact]
        public void DemolireUnEdificioRealeLiberaLaSuaSagoma()
        {
            var g = TestGame.New();
            Assert.Equal(BuildResult.Occupied, g.Do(TestGame.Place("casa", 10, 10)));
            Assert.Equal(BuildResult.Ok, g.Do(new DemolishOsmCommand { OsmKey = GameState.OsmKey(OsmType.Way, 99), Lot = TestGame.Lot, AreaM2 = 100 }));
            Assert.Equal(BuildResult.Ok, g.Do(TestGame.Place("casa", 10, 10)));
        }

        [Fact]
        public void NienteSovrapposizioniTraElementiEDecorazioniAMezzoMetro()
        {
            var g = TestGame.New();
            Assert.Equal(BuildResult.Ok, g.Do(TestGame.Place("casa", 40, 40)));
            Assert.Equal(BuildResult.Occupied, g.Do(TestGame.Place("casa", 50, 50)));
            // panchina 1,5×0,5 m subito accanto alla casa (la casa finisce a 30 m): libera
            Assert.Equal(BuildResult.Ok, g.Do(TestGame.Place("panchina", 60, 40)));
            Assert.Equal(BuildResult.Occupied, g.Do(TestGame.Place("panchina", 59, 40)));
        }

        [Fact]
        public void SbloccoECassa()
        {
            var g = TestGame.New();
            Assert.Equal(BuildResult.NotUnlocked, g.Do(TestGame.Place("pala", 60, 40)));
            g.State.Level = 3;
            g.State.Cash = 1000;
            Assert.Equal(BuildResult.NotEnoughCash, g.Do(TestGame.Place("pala", 60, 40)));
        }
    }

    public class CommandTests
    {
        [Fact]
        public void AnnullaRipeti_RimborsiENienteSoldiGratis()
        {
            var g = TestGame.New();
            long cash0 = g.State.Cash;
            Assert.Equal(BuildResult.Ok, g.Do(TestGame.Place("casa", 40, 40)));
            // la prima casa completa la missione m1 (+5000 e 400 XP → livello 5 con XpBase 100 lineare)
            Assert.Contains("m1", g.State.CompletedMissions);
            Assert.True(g.State.Level > 1);
            Assert.True(g.State.Cash > cash0 - 20000);
            Assert.True(g.Undo());
            Assert.Equal(cash0, g.State.Cash);
            Assert.Empty(g.State.Objects);
            Assert.Equal(1, g.State.Level);
            Assert.Equal(0, g.State.Xp);
            Assert.DoesNotContain("m1", g.State.CompletedMissions);
            Assert.Equal(0, g.State.MissionIndex);
            Assert.Equal(BuildResult.Ok, g.Redo());
            Assert.Single(g.State.Objects);
            Assert.Contains("m1", g.State.CompletedMissions);
            Assert.Equal(g.State.StartCash + g.State.LedgerTotal, g.State.Cash);
        }

        [Fact]
        public void SpostaRuotaPotenziaVarianteDemolisci()
        {
            var g = TestGame.New();
            g.Do(TestGame.Place("casa", 40, 40));
            int id = g.State.Objects.Keys.Single();
            Assert.Equal(BuildResult.Ok, g.Do(new MoveCommand { ObjectId = id, Lot = TestGame.Lot, HalfX = 60, HalfY = 40, Rotation = 1 }));
            Assert.Equal(1, g.State.Objects[id].Rotation);
            Assert.Equal(BuildResult.NotReady, g.Do(new UpgradeCommand { ObjectId = id }));
            g.Advance(TimeSpan.FromHours(7));
            Assert.Equal(BuildResult.Ok, g.Do(new UpgradeCommand { ObjectId = id }));
            Assert.Equal(2, g.State.Objects[id].Level);
            Assert.Equal(BuildResult.Ok, g.Do(new VariantCommand { ObjectId = id, Variant = "pietra" }));
            Assert.Equal(BuildResult.Ok, g.Do(new DemolishCommand { ObjectId = id }));
            Assert.Empty(g.State.Objects);
            // annullando tutto si torna alla casa appena costruita, nella posizione di partenza
            for (int i = 0; i < 4; i++) Assert.True(g.Undo());
            var o = g.State.Objects[id];
            Assert.Equal(1, o.Level);
            Assert.Equal(40, o.HalfX);
            Assert.Equal(0, o.Rotation);
            Assert.Equal("ocra", o.Variant);
            Assert.Equal(g.State.StartCash + g.State.LedgerTotal, g.State.Cash);
        }

        [Fact]
        public void Aliquote_EntroILimiti()
        {
            var g = TestGame.New();
            g.Do(new SetTaxCommand { Kind = TaxKind.Residential, Rate = 99 });
            Assert.Equal(g.Context.Balance.ResidentialTax.Max, g.State.ResidentialTax);
            g.Undo();
            Assert.Equal(g.Context.Balance.ResidentialTax.Default, g.State.ResidentialTax);
        }
    }

    public class EconomyTests
    {
        [Fact]
        public void UnAnnoDiGioco_NienteValoriAssurdiEDenaroConservato()
        {
            var g = TestGame.New(new Balance { StartCash = 500000 });
            g.State.Level = 10;
            g.Do(TestGame.Place("casa", 40, 40));
            g.Do(TestGame.Place("pala", 60, 40, 1));
            g.Do(TestGame.Place("panchina", 0, 60));
            long cashStart = g.State.Cash;
            for (int day = 0; day < 365; day++)
            {
                g.Advance(TimeSpan.FromDays(1));
                Assert.False(double.IsNaN(g.State.Stats.Happiness));
                Assert.Equal(g.State.StartCash + g.State.LedgerTotal, g.State.Cash);
            }
            Assert.Equal(g.Context.Balance.BasePopulation + 4, g.State.Stats.Population);
            Assert.True(g.State.Stats.PowerBalanceKw > 0);
            Assert.True(g.State.Ledger.Sum(d => d.Amounts[(int)LedgerCategory.EnergySales]) > 0);
            Assert.True(g.State.Ledger.Sum(d => d.Amounts[(int)LedgerCategory.Rent]) > 0);
            // con il bilanciamento predefinito la città esistente non va in rosso
            Assert.True(g.State.Cash > cashStart, "cassa dopo un anno: " + g.State.Cash);
        }

        [Fact]
        public void TasseAlte_MenoFelicita()
        {
            var g = TestGame.New();
            float h0 = Economy.Compute(g.Context).Happiness;
            g.Do(new SetTaxCommand { Kind = TaxKind.Residential, Rate = 5 });
            Assert.True(Economy.Compute(g.Context).Happiness < h0);
        }
    }

    public class SnapshotAndSaveTests
    {
        [Fact]
        public void IstantaneeGiornaliereERiavvolgimento()
        {
            var g = TestGame.New();
            g.Advance(TimeSpan.FromDays(40));
            // 30 giornaliere + i lunedì più vecchi (+ quella iniziale se lunedì)
            Assert.InRange(g.Snapshots.Items.Count, 30, 36);
            var target = g.Snapshots.Items[g.Snapshots.Items.Count - 10];
            long cashThen = target.Cash;
            g.Do(TestGame.Place("casa", 40, 40));
            g.Rewind(target);
            Assert.Equal(cashThen, g.State.Cash);
            Assert.Empty(g.State.Objects);
            Assert.False(g.Commands.CanUndo);
            Assert.True(g.Snapshots.Items.All(x => x.GameTime <= target.GameTime));
        }

        [Fact]
        public void SalvaECarica_TuttoComePrima()
        {
            var g = TestGame.New();
            g.Do(TestGame.Place("casa", 40, 40));
            g.Do(new DemolishOsmCommand { OsmKey = GameState.OsmKey(OsmType.Way, 99), Lot = TestGame.Lot, AreaM2 = 100 });
            g.Advance(TimeSpan.FromDays(3));
            var bytes = g.Save(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));
            var l = GameSession.Load(bytes, TestGame.Catalog(), g.Context.Balance, new FakeWorld(), Missions.Parse(TestGame.MissionsJson));
            Assert.Equal(g.State.Cash, l.State.Cash);
            Assert.Equal(g.State.Now, l.State.Now);
            Assert.Equal(g.State.Objects.Count, l.State.Objects.Count);
            Assert.Equal(g.State.Ledger.Count, l.State.Ledger.Count);
            Assert.Equal(g.Snapshots.Items.Count, l.Snapshots.Items.Count);
            Assert.True(l.State.DemolishedOsm.ContainsKey(GameState.OsmKey(OsmType.Way, 99)));
            // la storia dei comandi sopravvive: si può annullare dopo il caricamento
            Assert.Equal(2, l.Commands.UndoCount);
            Assert.True(l.Undo());
            Assert.Empty(l.State.DemolishedOsm);
            Assert.Throws<System.IO.InvalidDataException>(() => GameSession.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, null, null, null, null));
        }

        [Fact]
        public void TempoAdAppChiusa_Riepilogo()
        {
            var g = TestGame.New();
            g.State.LastPlayedUtc = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
            var start = g.State.Now;
            var summary = g.CatchUp(new DateTime(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc)); // 2 ore reali
            Assert.NotNull(summary);
            Assert.Equal(TimeSpan.FromHours(120), summary.GameElapsed);
            Assert.Equal(start.AddHours(120), g.State.Now);
            Assert.True(summary.Income > 0 && summary.Expenses < 0);
            // limite di 12 ore reali
            g.State.LastPlayedUtc = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
            var s2 = g.CatchUp(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(TimeSpan.FromHours(12), s2.RealElapsed);
        }
    }
}

namespace Orbis.Core.Tests
{
    /// <summary>I dati veri di data/ (catalogo, bilanciamento, missioni) si caricano e sono coerenti.</summary>
    public class GameDataTests
    {
        private static string Root => System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static Catalog LoadCatalog()
        {
            var c = new Catalog();
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(Root, "data", "catalog"), "*.json"))
                c.Load(System.IO.File.ReadAllText(f));
            return c;
        }

        [Fact]
        public void Catalogo_150VociInTutteLeCategorie()
        {
            var c = LoadCatalog();
            Assert.True(c.Count >= 150, "voci: " + c.Count);
            foreach (CatalogCategory cat in Enum.GetValues(typeof(CatalogCategory)))
                Assert.NotEmpty(c.InCategory(cat));
            Assert.Contains(c.Items, i => i.Key == "centrale_nucleare");
            Assert.Contains(c.Items, i => i.HasTag("vineyard"));
            Assert.Contains(c.Items, i => i.HasTag("olive"));
            Assert.Contains(c.Items, i => i.UnlockLevel == 1 && i.Category == CatalogCategory.Residential);
        }

        [Fact]
        public void Missioni_TutteRaggiungibiliConIlCatalogo()
        {
            var c = LoadCatalog();
            var m = Missions.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Root, "data", "scenarios", "missions.json")));
            Assert.Equal(20, m.All.Count);
            foreach (var d in m.All)
            {
                if (d.Type == "tag") Assert.Contains(c.Items, i => i.HasTag(d.Target));
                if (d.Type == "placed" && d.Target != "")
                    Assert.True(Enum.TryParse<CatalogCategory>(d.Target, out _), d.Key);
            }
        }

        [Fact]
        public void Bilanciamento_LaCittaEsistenteEInLeggeroAttivo()
        {
            var b = Balance.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Root, "data", "balance", "economy.json")));
            var g = GameSession.New("Chieti", new WorldConfig(), new DateTime(2026, 11, 16, 10, 0, 0), DateTime.UtcNow, 1, LoadCatalog(), b, new FakeWorld(), new Missions());
            var f = Economy.Compute(g.Context);
            Assert.InRange(f.Net, 1000, 10000);
            g.Advance(TimeSpan.FromDays(30));
            Assert.True(g.State.Cash > b.StartCash);
            Assert.Equal(g.State.StartCash + g.State.LedgerTotal, g.State.Cash);
        }
    }
}
