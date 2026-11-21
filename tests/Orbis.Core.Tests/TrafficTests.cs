using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Orbis.Core.Population;
using Orbis.Core.Traffic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Strade e traffico (M4 blocco 4a): grafo dai tile, tempi tra zone, congestione.</summary>
    public class TrafficTests
    {
        private static readonly Lazy<TrafficBalance> Balance = new Lazy<TrafficBalance>(() =>
            TrafficBalance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "traffic.json"))));

        private static RoadInfo Road(long id, RoadClass cls, int oneway, params float[] xy) =>
            new RoadInfo { OsmId = id, Class = cls, Oneway = oneway, Points = xy, Name = "r" + id };

        /// <summary>Griglia di strade ogni 100 m su 4 km, con un vertice ogni 25 m e un viale veloce a metà.</summary>
        private static TileData Grid(int n = 41, float step = 100)
        {
            var roads = new List<RoadInfo>();
            long id = 1;
            for (int i = 0; i < n; i++)
            {
                var h = new List<float>();
                var v = new List<float>();
                for (float t = 0; t <= (n - 1) * step + 0.1f; t += 25)
                {
                    h.Add(t);
                    h.Add(i * step);
                    v.Add(i * step);
                    v.Add(t);
                }
                var cls = i == n / 2 ? RoadClass.Primary : RoadClass.Residential;
                roads.Add(Road(id++, cls, 0, h.ToArray()));
                roads.Add(Road(id++, cls, 0, v.ToArray()));
            }
            return new TileData { Id = new TileId(14, 0, 0), Roads = roads.ToArray() };
        }

        [Fact]
        public void CateneFuseEIncroci()
        {
            var g = RoadGraph.Build(new[] { (Grid(), 0.0, 0.0) }, Balance.Value);
            // restano solo gli incroci (i vertici ogni 25 m sono fusi); i 4 angoli, con due sole strade, diventano curve
            Assert.Equal(41 * 41 - 4, g.NodeCount);
            Assert.Equal(2 * 2 * 41 * 40 - 8, g.EdgeCount);
            Assert.Equal(2 * 2 * 41 * 40 - 16, Enumerable.Range(0, g.EdgeCount).Count(e => Math.Abs(g.Length[e] - 100f) < 0.5f));
            Assert.Equal(8, Enumerable.Range(0, g.EdgeCount).Count(e => Math.Abs(g.Length[e] - 200f) < 0.5f));
            // la forma del tratto resta: un punto ogni 25 m
            Assert.All(Enumerable.Range(0, g.EdgeCount), e => Assert.Equal((int)Math.Round(g.Length[e] / 25f) + 1, (g.PointStart[e + 1] - g.PointStart[e]) / 2));
            int center = g.Nearest(2003, 2004);
            Assert.Equal(2000f, g.X[center], 1);
            Assert.Equal(2000f, g.Y[center], 1);
        }

        [Fact]
        public void SensoUnicoRispettato()
        {
            var tile = new TileData
            {
                Id = new TileId(14, 0, 0),
                Roads = new[]
                {
                    Road(1, RoadClass.Residential, 0, 0, 0, 100, 0, 200, 0),
                    Road(2, RoadClass.Residential, 1, 100, 0, 100, 100),   // senso unico verso nord
                    Road(3, RoadClass.Residential, 0, 0, 100, 100, 100, 200, 100),
                    Road(4, RoadClass.Footway, 0, 0, 0, 0, 100),           // pedonale: non è nel grafo
                },
            };
            var g = RoadGraph.Build(new[] { (tile, 0.0, 0.0) }, Balance.Value);
            int south = g.Nearest(100, 0), north = g.Nearest(100, 100);
            bool up = false, down = false;
            for (int e = g.First[south]; e < g.First[south + 1]; e++) up |= g.To[e] == north;
            for (int e = g.First[north]; e < g.First[north + 1]; e++) down |= g.To[e] == south;
            Assert.True(up);
            Assert.False(down);
            Assert.DoesNotContain(g.Road, r => r == 4);
        }

        [Fact]
        public void GrafoDalTileVero()
        {
            string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            var region = TileReader.ReadIndexFile(File.ReadAllBytes(Fixture("index_sample.bin")));
            var tile = TileReader.ReadFile(File.ReadAllBytes(Fixture("tile_sample.bin")), region.Scale);
            region.CreateFrame().TileOrigin(tile.Id, out var ox, out var oy);
            var g = RoadGraph.Build(new[] { (tile, ox, oy) }, Balance.Value);
            Assert.True(g.NodeCount > 4 && g.EdgeCount > 4);
            // Via Pollione è a senso unico: i suoi tratti vanno tutti nello stesso verso
            long pollione = tile.Roads.First(r => r.Name == "Via Pollione").OsmId;
            var edges = Enumerable.Range(0, g.EdgeCount).Where(e => g.Road[e] == pollione).ToList();
            Assert.NotEmpty(edges);
            foreach (var e in edges)
            {
                int from = Enumerable.Range(0, g.NodeCount).First(n => g.First[n] <= e && e < g.First[n + 1]);
                Assert.True(g.Y[g.To[e]] > g.Y[from], "Via Pollione percorsa contromano");
            }
        }

        [Fact]
        public void TempiECongestione()
        {
            var b = Balance.Value;
            var g = RoadGraph.Build(new[] { (Grid(), 0.0, 0.0) }, b);
            var t = new TrafficModel(g, b);
            var sw = Stopwatch.StartNew();
            t.RefreshAll();
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms per tutte le zone");
            // 3 km in diagonale di quartiere a 30 km/h: tra 6 e 15 minuti
            int free = t.CarMinutes(300, 300, 3300, 300);
            Assert.InRange(free, 6, 16);
            // il viale veloce a metà accorcia i viaggi lungo il suo asse
            Assert.True(t.CarMinutes(200, 2000, 3800, 2000) < t.CarMinutes(200, 300, 3800, 300));

            // ora dell'ingorgo: 20.000 auto sullo stesso percorso. Sulla griglia si trovano strade parallele, ma i tratti
            // usati si intasano
            for (int k = 0; k < 20000; k++) t.RecordTrip(300, 300, 3300, 300);
            Cycle(t, b, 1);
            Assert.True(Enumerable.Range(0, g.EdgeCount).Max(e => t.Congestion(e)) > 2, "nessun tratto congestionato");
            Cycle(t, b, 1);
            Assert.True(t.CarMinutes(300, 300, 3300, 300) >= free);
            // senza altri viaggi il giro dopo le strade tornano scorrevoli (come di notte)
            Assert.True(Enumerable.Range(0, g.EdgeCount).Max(e => t.Congestion(e)) < 1.01f);
        }

        private static void Cycle(TrafficModel t, TrafficBalance b, int rounds)
        {
            int minute = 60 * 24 * 1000;
            for (int round = 0; round < rounds; round++)
                for (int s = 0; s < t.Zones; s += b.OriginsPerStep) t.Tick(minute += 15);
        }

        [Fact]
        public void SenzaAlternativeIlTrafficoRallenta()
        {
            var b = Balance.Value;
            var pts = new List<float>();
            for (float x = 0; x <= 3000; x += 25)
            {
                pts.Add(x);
                pts.Add(0);
            }
            var tile = new TileData { Id = new TileId(14, 0, 0), Roads = new[] { Road(1, RoadClass.Residential, 0, pts.ToArray()) } };
            var t = new TrafficModel(RoadGraph.Build(new[] { (tile, 0.0, 0.0) }, b), b);
            t.RefreshAll();
            int free = t.CarMinutes(0, 0, 3000, 0);
            for (int k = 0; k < 3000; k++) t.RecordTrip(0, 0, 3000, 0);
            Cycle(t, b, 2);
            int jam = t.CarMinutes(0, 0, 3000, 0);
            Assert.True(jam > free * 2, $"col traffico {jam} min, senza {free} min");
        }

        private sealed class CountingTravel : ITravelTimes
        {
            public int Trips, Ticks;
            public int CarMinutes(float x0, float y0, float x1, float y1) => 17;
            public void RecordTrip(float x0, float y0, float x1, float y1) => Trips++;
            public void Tick(int minute) => Ticks++;
        }

        [Fact]
        public void LaGiornataUsaLaRete()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            var pop = PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), 21);
            var routines = new CityRoutines(new LifeInputs { Data = d, Names = n, Balance = b });
            var travel = new CountingTravel();
            routines.Travel = travel;
            var day = new DateTime(2026, 3, 4);
            for (int q = 1; q <= 48; q++) routines.Step(pop, day.AddMinutes(15 * q));
            Assert.Equal(48, travel.Ticks);
            Assert.True(travel.Trips > 1000, $"{travel.Trips} viaggi in auto");
            // chi è partito in auto ci mette i minuti della rete
            int m = CityRoutines.Minute(day.AddHours(12));
            Assert.Contains(Enumerable.Range(0, pop.Count), i => pop.Citizens[i].TravelEnd - pop.Citizens[i].TravelStart == 17);
        }
    }
}
