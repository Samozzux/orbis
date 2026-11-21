using System;
using System.Diagnostics;
using System.Linq;
using Orbis.Core.Population;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Giornata dei cittadini (M4 blocco 3c): sonno, lavoro, scuola, pasti, spesa nei negozi veri, svago.</summary>
    public class CityRoutinesTests
    {
        private static LifeInputs Life()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            return new LifeInputs { Data = d, Names = n, Balance = b };
        }

        /// <summary>Città di prova con alimentari e bar veri, fatta vivere 3 giorni (mar–gio) con giornata e vita.</summary>
        private sealed class Run
        {
            public CityPopulation Pop;
            public long MoneyBefore, City, ExtIn, ExtOut;
            public double SleepAt3, WorkAt10, SchoolAt10, LeisureEvening, Traveling8;
            public long Ms;
        }

        private static readonly Lazy<Run> Days = new Lazy<Run>(() =>
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            var places = PopulationTests.FakeCity();
            // nella città di prova "shop/supermarket" e "amenity/bar" ci sono già: aggiungo una palestra e un ristorante
            places.Workplaces.Add(new Place { OsmId = 777001, X = 3000, Y = 3000, Category = "leisure/fitness_centre", Name = "Palestra", Capacity = 6 });
            places.Workplaces.Add(new Place { OsmId = 777002, X = 2500, Y = 2800, Category = "amenity/restaurant", Name = "Trattoria", Capacity = 8 });
            var pop = PopulationGenerator.Generate(d, n, b, places, 11);
            var life = new CityLife(Life());
            var routines = new CityRoutines(Life());
            var run = new Run { Pop = pop, MoneyBefore = Money(pop), ExtIn = pop.ExternalIn, ExtOut = pop.ExternalOut };
            var taxes = new TaxRates { Residential = 1.5f, Commercial = 2f, Industrial = 2f };
            var start = new DateTime(2026, 3, 3); // martedì
            pop.LastDay = CityLife.DayIndex(start) - 1;
            var sw = Stopwatch.StartNew();
            for (int day = 0; day < 3; day++)
            {
                var date = start.AddDays(day);
                for (int q = 1; q <= 96; q++)
                {
                    var t = date.AddMinutes(15 * q);
                    routines.Step(pop, t);
                    if (day == 1 && t.Hour == 3 && t.Minute == 0) run.SleepAt3 = Share(pop, c => c.Activity == CitizenActivity.Sleep && c.Location == LocationKind.Home, Adults);
                    if (day == 1 && t.Hour == 10 && t.Minute == 30)
                    {
                        run.WorkAt10 = Share(pop, c => c.Activity == CitizenActivity.Work, c => c.Works && c.Work >= 0 && c.Occupation == Occupation.Employee);
                        run.SchoolAt10 = Share(pop, c => c.Activity == CitizenActivity.School && c.Location == LocationKind.School, c => c.Occupation == Occupation.Pupil && c.School >= 0);
                    }
                    if (day == 1 && t.Hour == 8 && t.Minute == 0)
                    {
                        int m = CityRoutines.Minute(t);
                        run.Traveling8 = Enumerable.Range(0, pop.Count).Count(i => pop.Citizens[i].Alive && m < pop.Citizens[i].TravelEnd && m >= pop.Citizens[i].TravelStart);
                    }
                    if (day == 1 && t.Hour == 19 && t.Minute == 0) run.LeisureEvening = Share(pop, c => c.Activity == CitizenActivity.Leisure || c.Location == LocationKind.Leisure, Adults);
                }
                life.Day(pop, date, taxes, true, (f, a) => run.City += a);
            }
            sw.Stop();
            run.Ms = sw.ElapsedMilliseconds;
            return run;
        });

        private static bool Adults(Citizen c) => c.AgeOn(new DateTime(2026, 3, 4)) >= 18;

        private static double Share(CityPopulation p, Func<Citizen, bool> what, Func<Citizen, bool> among)
        {
            int n = 0, k = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var c = p.Citizens[i];
                if (!c.Alive || !among(c)) continue;
                n++;
                if (what(c)) k++;
            }
            return n == 0 ? 0 : (double)k / n;
        }

        private static long Money(CityPopulation p)
        {
            long s = 0;
            for (int i = 0; i < p.Count; i++) s += p.Citizens[i].Savings;
            foreach (var w in p.Workplaces) s += w.Cash;
            return s;
        }

        [Fact]
        public void UnaGiornataFeriale()
        {
            var r = Days.Value;
            Assert.InRange(r.SleepAt3, 0.85, 1.0);      // di notte quasi tutti dormono a casa
            Assert.InRange(r.WorkAt10, 0.85, 1.0);      // alle 10:30 chi lavora in città è al lavoro
            Assert.InRange(r.SchoolAt10, 0.9, 1.0);     // e gli alunni sono a scuola
            Assert.True(r.Traveling8 > 1000, $"alle 8 solo {r.Traveling8} persone per strada");
            Assert.True(r.LeisureEvening > 0.02, $"la sera solo {r.LeisureEvening:P1} fuori a svagarsi");
        }

        [Fact]
        public void FrigoSpesaEScorte()
        {
            var p = Days.Value.Pop;
            var b = PopulationTests.Inputs.Value.Balance;
            // i frigo non restano vuoti: la spesa li riempie nei negozi di alimentari veri, che vendono e calano le scorte
            int empty = p.Households.Count(h => h.Members.Count > 0 && h.Fridge == 0);
            Assert.True(empty < p.Households.Count * 0.1, $"{empty} famiglie col frigo vuoto");
            var stores = p.Workplaces.Where(w => PopulationBalance.InSet(b.Routine.FoodCategories, w.Category)).ToList();
            Assert.NotEmpty(stores);
            Assert.Contains(stores, s => s.Stock < s.Capacity * b.Routine.StoreStockPerJob);
            Assert.True(stores.Sum(s => s.RevenueDay) > 0);
            // bisogni in media sani
            double hunger = Enumerable.Range(0, p.Count).Where(i => p.Citizens[i].Alive).Average(i => (double)p.Citizens[i].Hunger);
            double energy = Enumerable.Range(0, p.Count).Where(i => p.Citizens[i].Alive).Average(i => (double)p.Citizens[i].Energy);
            Assert.InRange(hunger, 25, 95);
            Assert.InRange(energy, 25, 100);
        }

        [Fact]
        public void IlDenaroSiConservaAncheConLaSpesa()
        {
            var r = Days.Value;
            var p = r.Pop;
            Assert.Equal(r.MoneyBefore + (p.ExternalIn - r.ExtIn) - (p.ExternalOut - r.ExtOut), Money(p) + r.City);
            // a fine giornata le vendite sono passate negli incassi e il conto della spesa è azzerato
            Assert.All(p.Workplaces, w => Assert.Equal(0, w.SalesToday));
            Assert.All(p.Households, h => Assert.Equal(0, h.FoodSpentToday));
        }

        [Fact]
        public void PosizioniEsalvataggio()
        {
            var p = Days.Value.Pop;
            int minute = CityRoutines.Minute(new DateTime(2026, 3, 6, 0, 0, 0));
            int inside = 0;
            for (int i = 0; i < p.Count; i++)
            {
                if (!p.Citizens[i].Alive) continue;
                if (CityRoutines.CurrentPosition(p, i, minute, out var x, out var y))
                {
                    inside++;
                    Assert.InRange(x, -100, 6100);
                    Assert.InRange(y, -100, 6100);
                }
            }
            Assert.True(inside > p.AliveCount * 0.9);
            var bytes = PopulationSerializer.ToBytes(p);
            Assert.Equal(bytes, PopulationSerializer.ToBytes(PopulationSerializer.FromBytes(bytes)));
        }

        [Fact]
        public void TreGiorniVeloci()
        {
            // 288 passi da 15 minuti con 48.000 cittadini
            Assert.True(Days.Value.Ms < 8000, $"{Days.Value.Ms} ms");
        }
    }
}
