using System;
using System.IO;
using System.Linq;
using Orbis.Core.Game;
using Orbis.Core.Population;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Servizi e felicità dei cittadini (M4 blocco 5a).</summary>
    public class ServicesTests
    {
        private static ServicesBalance Balance() =>
            ServicesBalance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "services.json")));

        private static readonly TaxRates Normal = new TaxRates { Residential = 1.5f, Commercial = 2, Industrial = 2 };

        private static CityPopulation Pop(int seed = 3)
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            return PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), seed);
        }

        [Fact]
        public void BilanciamentoLetto()
        {
            var b = Balance();
            Assert.Contains("amenity/police", b.Kinds[(int)ServiceKind.Police].Categories);
            Assert.True(b.Kinds[(int)ServiceKind.Police].GoodMinutes > 0);
            Assert.True(b.Kinds[(int)ServiceKind.Education].Schools);
            Assert.Equal(80, b.Kinds[(int)ServiceKind.Waste].Citywide);
            Assert.Equal(ServiceKind.Fire, b.KindOfCatalog("vigili_del_fuoco"));
            Assert.Equal(ServiceKind.Count, b.KindOfCatalog("casa"));
            Assert.Equal(1.0, b.Weights.Sum(), 3);
        }

        [Fact]
        public void CoperturaDaiLuoghiVeriEDalleSediInPiu()
        {
            var pop = Pop();
            var s = new CityServices(Balance());
            s.Update(pop);
            int dw = pop.Dwellings.Count;
            // la città finta ha ospedali e scuole ma nessuna caserma
            Assert.True(s.Count(ServiceKind.Health) > 50);
            Assert.True(s.Count(ServiceKind.Education) >= 28);
            Assert.Equal(0, s.Count(ServiceKind.Police));
            Assert.True(Enumerable.Range(0, dw).Count(d => s.At(d, ServiceKind.Health) == 100) > dw / 2);
            Assert.All(Enumerable.Range(0, dw), d => Assert.Equal(0, s.At(d, ServiceKind.Police)));
            Assert.All(Enumerable.Range(0, dw), d => Assert.Equal(80, s.At(d, ServiceKind.Waste)));

            // una caserma in più accanto alla prima casa: copre bene le case vicine, non quelle lontane
            var home = pop.Dwellings[0];
            s.Extra.Add(new ServiceSource { Kind = ServiceKind.Police, X = home.X + 100, Y = home.Y, Name = "Caserma" });
            s.Update(pop);
            Assert.Equal(1, s.Count(ServiceKind.Police));
            Assert.Equal(100, s.At(0, ServiceKind.Police));
            for (int d = 0; d < dw; d++)
            {
                double dist = Math.Sqrt(Math.Pow(pop.Dwellings[d].X - home.X - 100, 2) + Math.Pow(pop.Dwellings[d].Y - home.Y, 2)) * 1.3;
                if (dist > 5000) Assert.Equal(0, s.At(d, ServiceKind.Police));
                if (dist < 1500) Assert.Equal(100, s.At(d, ServiceKind.Police));
            }
        }

        [Fact]
        public void SediVereDaiTile()
        {
            var civic = new float[] { 10, 10, 50, 10, 50, 40, 10, 40 };
            var hosp = new float[] { 200, 200, 300, 200, 300, 260, 200, 260 };
            var tile = new TileData
            {
                Id = new TileId(14, 1, 2),
                Buildings = new[]
                {
                    new BuildingInfo { OsmId = 1, Use = BuildingUse.Civic, Outer = civic, Bounds = Bounds2.Of(civic) },
                    new BuildingInfo { OsmId = 2, Use = BuildingUse.Hospital, Outer = hosp, Bounds = Bounds2.Of(hosp), Name = "Ospedale" },
                },
                Pois = new[]
                {
                    // i pompieri dentro un edificio "civico": conta la categoria del luogo
                    new PoiInfo { OsmId = 10, Category = "amenity/fire_station", Name = "Vigili del Fuoco", X = 30, Y = 25, Building = 0 },
                    new PoiInfo { OsmId = 11, Category = "amenity/police", Name = "Questura", X = 600, Y = 600, Building = -1 },
                    new PoiInfo { OsmId = 12, Category = "amenity/police", Name = "Questura (ingresso)", X = 620, Y = 610, Building = -1 },
                    new PoiInfo { OsmId = 13, Category = "shop/bakery", Name = "Forno", X = 5, Y = 5, Building = -1 },
                },
            };
            var list = CityServices.FromTiles(new[] { (tile, 1000.0, 2000.0) }, Balance());
            var fire = Assert.Single(list, x => x.Kind == ServiceKind.Fire);
            Assert.Equal(1030, fire.X);
            Assert.Equal(2025, fire.Y);
            Assert.Single(list, x => x.Kind == ServiceKind.Police); // a 22 m: la stessa sede
            var h = Assert.Single(list, x => x.Kind == ServiceKind.Health);
            Assert.Equal(1250, h.X);
            Assert.Equal(2230, h.Y);
            Assert.Equal(3, list.Count);

            // le sedi dei tile prendono il posto dei posti di lavoro della popolazione
            var pop = Pop();
            var s = new CityServices(Balance());
            s.Fixed.AddRange(list);
            s.Update(pop);
            Assert.Equal(1, s.Count(ServiceKind.Health));
            Assert.Equal(0, s.Count(ServiceKind.Education));
        }

        private sealed class SlowRoads : ITravelTimes
        {
            public int Minutes;
            public int CarMinutes(float x0, float y0, float x1, float y1) => Minutes;
            public void RecordTrip(float x0, float y0, float x1, float y1) { }
            public void Tick(int minute) { }
        }

        [Fact]
        public void EmergenzeColTempoDiArrivoInAuto()
        {
            var pop = Pop();
            var s = new CityServices(Balance());
            var home = pop.Dwellings[0];
            s.Extra.Add(new ServiceSource { Kind = ServiceKind.Fire, X = home.X, Y = home.Y });
            s.Travel = new SlowRoads { Minutes = 5 };
            s.Update(pop);
            Assert.Equal(100, s.At(0, ServiceKind.Fire));
            // col traffico i pompieri arrivano in 14 minuti: copertura a metà (buono 8, massimo 20)
            s.Travel = new SlowRoads { Minutes = 14 };
            s.Update(pop);
            Assert.Equal(50, s.At(0, ServiceKind.Fire));
        }

        private static double Mean(CityPopulation pop, Func<Citizen, bool> who)
        {
            var list = Enumerable.Range(0, pop.Count).Select(i => pop.Citizens[i]).Where(c => c.Alive && who(c)).ToList();
            return list.Average(c => (double)c.Happiness);
        }

        [Fact]
        public void FelicitaDiOgniCittadino()
        {
            var pop = Pop();
            var s = new CityServices(Balance());
            s.Update(pop);
            var rep = CityHappiness.Update(pop, s, Normal, new DateTime(2026, 3, 4));
            Assert.InRange(rep.Average, 45, 85);
            Assert.Equal(rep.Average, pop.AverageHappiness);
            Assert.Equal(pop.AliveCount, rep.Counted);
            Assert.All(Enumerable.Range(0, pop.Count).Where(i => pop.Citizens[i].Alive), i => Assert.InRange(pop.Citizens[i].Happiness, 1, 100));
            // chi ha un lavoro è più contento di chi lo cerca
            double employed = Mean(pop, c => c.Occupation == Occupation.Employee && c.Work != Citizen.None);
            double unemployed = Mean(pop, c => c.Occupation == Occupation.Unemployed);
            Assert.True(employed > unemployed + 5, $"occupati {employed:0.0}, disoccupati {unemployed:0.0}");
            Assert.Equal(0, rep.Factors[(int)HappinessFactor.Safety]); // nessuna caserma né pompieri

            // tasse più alte: il giorno dopo la felicità scende, ma piano (si avvicina al nuovo valore)
            var high = new TaxRates { Residential = 8, Commercial = 2, Industrial = 2 };
            var copy = PopulationSerializer.FromBytes(PopulationSerializer.ToBytes(pop));
            var after = CityHappiness.Update(copy, s, high, new DateTime(2026, 3, 5));
            Assert.True(after.Average < rep.Average - 0.5, $"{after.Average} contro {rep.Average}");
            Assert.True(after.Average > rep.Average - 5);
            Assert.True(after.Factors[(int)HappinessFactor.Taxes] < rep.Factors[(int)HappinessFactor.Taxes]);

            // caserme e pompieri ovunque: più sicuri e più felici
            var safe = new CityServices(Balance());
            foreach (var dw in pop.Dwellings.Take(400))
            {
                safe.Extra.Add(new ServiceSource { Kind = ServiceKind.Police, X = dw.X, Y = dw.Y });
                safe.Extra.Add(new ServiceSource { Kind = ServiceKind.Fire, X = dw.X, Y = dw.Y });
            }
            safe.Update(pop);
            var better = CityHappiness.Update(pop, safe, Normal, new DateTime(2026, 3, 5));
            Assert.True(better.Factors[(int)HappinessFactor.Safety] > 80);
            Assert.True(better.Average > rep.Average);
        }

        [Fact]
        public void FelicitaNelSalvataggioEDopoLaGiornata()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            var pop = Pop(9);
            var life = new CityLife(new LifeInputs { Data = d, Names = n, Balance = b, Services = Balance() });
            var day = new DateTime(2026, 3, 4);
            pop.LastDay = CityLife.DayIndex(day) - 1;
            long city = 0;
            var r = life.Day(pop, day, Normal, true, (f, a) => city += a);
            Assert.NotNull(r.Happiness);
            Assert.True(pop.AverageHappiness > 0);
            Assert.True(pop.HappinessFactors[(int)HappinessFactor.Services] > 0);
            pop.Citizens[0].Commute = 37;
            var back = PopulationSerializer.FromBytes(PopulationSerializer.ToBytes(pop));
            Assert.Equal(pop.AverageHappiness, back.AverageHappiness);
            Assert.Equal(pop.UnhappyCount, back.UnhappyCount);
            Assert.Equal(pop.HappinessFactors, back.HappinessFactors);
            Assert.Equal(37, back.Citizens[0].Commute);
            Assert.All(Enumerable.Range(0, pop.Count), i => Assert.Equal(pop.Citizens[i].Happiness, back.Citizens[i].Happiness));
        }

        [Fact]
        public void IlTragittoLungoPesa()
        {
            var pop = Pop();
            var s = new CityServices(Balance());
            s.Update(pop);
            int a = Enumerable.Range(0, pop.Count).First(i => pop.Citizens[i].Alive && pop.Citizens[i].Occupation == Occupation.Employee);
            pop.Citizens[a].Commute = 10;
            CityHappiness.Update(pop, s, Normal, new DateTime(2026, 3, 4));
            var shortTrip = pop.HappinessFactors[(int)HappinessFactor.Commute];
            int happyShort = pop.Citizens[a].Happiness;
            var copy = PopulationSerializer.FromBytes(PopulationSerializer.ToBytes(pop));
            copy.AverageHappiness = 0; // ricalcolo diretto, senza avvicinamento graduale
            copy.Citizens[a].Commute = 90;
            CityHappiness.Update(copy, s, Normal, new DateTime(2026, 3, 4));
            Assert.True(copy.Citizens[a].Happiness < happyShort);
            Assert.True(copy.HappinessFactors[(int)HappinessFactor.Commute] < shortTrip);
        }

        [Fact]
        public void EdificiDelCatalogoComeSediDeiServizi()
        {
            var b = Balance();
            b.Kinds[(int)ServiceKind.Police].Catalog.Add("casa"); // nel catalogo di prova la "casa" fa da caserma
            var frame = new WorldFrame(14.16, 42.35);
            var id = frame.TileAt(14, 0, 0);
            frame.TileOrigin(id, out var ox, out var oy);
            var lotOuter = new float[] { 0, 0, 300, 0, 300, 300, 0, 300 };
            var tile = new TileData
            {
                Id = id, Size = 1800, Terrain = TerrainInfo.Flat(1800),
                Lots = new[] { new LotInfo { Id = 7, Outer = lotOuter, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(lotOuter), Area = 90000 } },
            };
            var g = GameSession.New("Prova", new WorldConfig(), new DateTime(2026, 11, 16, 10, 0, 0), DateTime.UtcNow, 1, TestGame.Catalog(),
                new Balance { StartCash = 100000 }, new OneTile(tile), Missions.Parse(TestGame.MissionsJson));
            g.Context.Frame = frame;
            Assert.Equal(BuildResult.Ok, g.Do(new PlaceCommand { Item = "casa", Lot = new LotKey(id.X, id.Y, 7), HalfX = 200, HalfY = 100 }));
            // in cantiere non conta ancora
            Assert.Empty(ServiceSources.FromCatalog(g.Context, b));
            g.Advance(TimeSpan.FromHours(7));
            var src = Assert.Single(ServiceSources.FromCatalog(g.Context, b));
            Assert.Equal(ServiceKind.Police, src.Kind);
            // centro della casa 10×8 m con l'angolo in (100, 50) m del tile
            Assert.Equal(ox + 105, src.X, 1);
            Assert.Equal(oy + 54, src.Y, 1);
        }

        private sealed class OneTile : IWorldAccess
        {
            private readonly TileData _t;
            public OneTile(TileData t) => _t = t;
            public TileData Tile(int x, int y) => x == _t.Id.X && y == _t.Id.Y ? _t : null;
        }
    }
}
