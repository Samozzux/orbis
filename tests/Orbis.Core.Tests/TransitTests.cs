using System;
using System.IO;
using System.Linq;
using Orbis.Core.Population;
using Orbis.Core.Traffic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Linee reali del trasporto pubblico (M4 blocco 4b) e scelta del mezzo dei cittadini.</summary>
    public class TransitTests
    {
        private static readonly Lazy<TransitBalance> Balance = new Lazy<TransitBalance>(() =>
            TransitBalance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "transit.json"))));

        [Fact]
        public void LineaVeraDelPackNelSuoVerso()
        {
            var region = TileReader.ReadIndexFile(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "index_sample.bin")));
            var net = TransitNetwork.Build(region, Balance.Value);
            var line = Assert.Single(net.Lines);
            Assert.Equal("2", line.Ref);
            Assert.Equal(3, line.X.Length);
            Assert.True(line.Minutes[2] > line.Minutes[1] && line.Minutes[1] > 0);
            // da Piazza Vico (-200, 0) alla Stazione (130, 250): a piedi alla fermata, attesa, bus
            int t = net.Minutes(line.X[0] + 50, line.Y[0], line.X[2], line.Y[2] - 40, out int l);
            Assert.Equal(0, l);
            Assert.InRange(t, 10, 25);
            // lontano da ogni fermata: niente bus
            Assert.Equal(-1, net.Minutes(line.X[0] + 5000, line.Y[0], line.X[2], line.Y[2], out _));
            net.Ride(0);
            Assert.Equal(1, net.Lines[0].Passengers);
            Assert.Equal(130, net.FareCents(0));
        }

        [Fact]
        public void LaLineaHaUnVerso()
        {
            // fermate a 1 km l'una dall'altra lungo un parallelo
            var region = new RegionInfo { OriginLat = 42.35, OriginLon = 14.16, Zoom = 14 };
            for (int i = 0; i < 3; i++) region.TransitStops.Add(new TransitStopInfo { Lon = 14.16 + i * 0.0122, Lat = 42.35, Name = "F" + i });
            region.TransitRoutes.Add(new TransitRouteInfo { OsmId = 1, Mode = TransitMode.Bus, Ref = "1", Stops = new[] { 0, 1, 2 }, Ways = new long[0] });
            var net = TransitNetwork.Build(region, Balance.Value);
            var l = net.Lines[0];
            Assert.InRange(l.X[1] - l.X[0], 900, 1100);
            Assert.True(net.Minutes(l.X[0], l.Y[0], l.X[2], l.Y[2], out _) > 0);
            Assert.Equal(-1, net.Minutes(l.X[2], l.Y[2], l.X[0], l.Y[0], out _));
        }

        [Fact]
        public void LineeLungaDistanzaEscluse()
        {
            var region = new RegionInfo { OriginLat = 42.35, OriginLon = 14.16, Zoom = 14 };
            region.TransitStops.Add(new TransitStopInfo { Lon = 14.160, Lat = 42.350, Name = "A" });
            region.TransitStops.Add(new TransitStopInfo { Lon = 14.170, Lat = 42.350, Name = "B" });
            region.TransitRoutes.Add(new TransitRouteInfo { OsmId = 1, Mode = TransitMode.Bus, Ref = "1", Operator = "La Panoramica", Stops = new[] { 0, 1 }, Ways = new long[0] });
            region.TransitRoutes.Add(new TransitRouteInfo { OsmId = 2, Mode = TransitMode.Bus, Ref = "FX", Operator = "FlixBus", Stops = new[] { 0, 1 }, Ways = new long[0] });
            region.TransitRoutes.Add(new TransitRouteInfo { OsmId = 3, Mode = TransitMode.Train, Ref = "R", Operator = "Trenitalia", Stops = new[] { 0, 1 }, Ways = new long[0] });
            var net = TransitNetwork.Build(region, Balance.Value);
            Assert.Equal(new long[] { 1, 3 }, net.Lines.Select(l => l.OsmId).ToArray());
            Assert.Equal(200, net.Lines[1].FareCents);
        }

        private sealed class FakeTransit : ITransit
        {
            public int Rides;
            public int Minutes(float x0, float y0, float x1, float y1, out int line)
            {
                line = 0;
                return 12;
            }
            public int FareCents(int line) => 130;
            public void Ride(int line) => Rides++;
        }

        [Fact]
        public void ChiNonHaLAutoPrendeIlBusEPagaIlBiglietto()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            var pop = PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), 31);
            Assert.InRange(pop.Households.Count(h => h.Cars > 0) / (double)pop.Households.Count, 0.6, 0.95);
            var inputs = new LifeInputs { Data = d, Names = n, Balance = b };
            var routines = new CityRoutines(inputs) { Transit = new FakeTransit() };
            var life = new CityLife(inputs);
            long Money()
            {
                long s = 0;
                for (int i = 0; i < pop.Count; i++) s += pop.Citizens[i].Savings;
                foreach (var w in pop.Workplaces) s += w.Cash;
                return s;
            }
            long before = Money(), extIn = pop.ExternalIn, extOut = pop.ExternalOut, city = 0;
            var day = new DateTime(2026, 3, 4);
            pop.LastDay = CityLife.DayIndex(day) - 1;
            for (int q = 1; q <= 96; q++) routines.Step(pop, day.AddMinutes(15 * q));
            var rides = ((FakeTransit)routines.Transit).Rides;
            Assert.True(rides > 500, $"{rides} viaggi in bus");
            int onBus = Enumerable.Range(0, pop.Count).Count(i => pop.Citizens[i].Mode == TravelMode.Transit);
            Assert.True(onBus > 200);
            // in bus vanno soprattutto le famiglie senza auto e i ragazzi
            Assert.All(Enumerable.Range(0, pop.Count).Where(i => pop.Citizens[i].Mode == TravelMode.Car),
                i => Assert.True(pop.Households[pop.Citizens[i].Household].Cars > 0));
            // biglietti: 1,30 € per ogni corsa in città, più i viaggi fuori città senza auto
            long cents = pop.Households.Sum(h => (long)h.TransitCents);
            Assert.True(cents >= rides * 130L);
            life.Day(pop, day, new TaxRates { Residential = 1.5f, Commercial = 2, Industrial = 2 }, true, (f, a) => city += a);
            Assert.All(pop.Households, h => Assert.InRange(h.TransitCents, 0, 99));
            Assert.Equal(before + (pop.ExternalIn - extIn) - (pop.ExternalOut - extOut), Money() + city);
        }
    }
}
