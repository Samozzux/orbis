using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Orbis.Core.Game;
using Orbis.Core.Population;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Vita giornaliera dei cittadini (M4 blocco 3): soldi che si conservano, un anno senza valori assurdi.</summary>
    public class CityLifeTests
    {
        private static LifeInputs Life()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            return new LifeInputs { Data = d, Names = n, Balance = b };
        }

        private static CityPopulation NewPopulation(int seed = 42)
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            return PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), seed);
        }

        private static long Money(CityPopulation p)
        {
            long s = 0;
            for (int i = 0; i < p.Count; i++) s += p.Citizens[i].Savings;
            foreach (var w in p.Workplaces) s += w.Cash;
            return s;
        }

        private static readonly Lazy<(CityPopulation Pop, LifeDay[] Days, long City, long MoneyBefore, long ExtIn, long ExtOut, long Ms)> Year =
            new Lazy<(CityPopulation, LifeDay[], long, long, long, long, long)>(() =>
            {
                var pop = NewPopulation();
                var life = new CityLife(Life());
                long before = Money(pop), city = 0;
                long extIn = pop.ExternalIn, extOut = pop.ExternalOut;
                var taxes = new TaxRates { Residential = 1.5f, Commercial = 2f, Industrial = 2f };
                var days = new LifeDay[365];
                var start = new DateTime(2026, 1, 1);
                pop.LastDay = CityLife.DayIndex(start) - 1;
                var sw = Stopwatch.StartNew();
                for (int k = 0; k < days.Length; k++)
                    days[k] = life.Day(pop, start.AddDays(k), taxes, true, (flow, amount) => city += amount);
                sw.Stop();
                return (pop, days, city, before, extIn, extOut, sw.ElapsedMilliseconds);
            });

        [Fact]
        public void IlDenaroSiConserva()
        {
            var y = Year.Value;
            var p = y.Pop;
            // quello che hanno cittadini, attività e comune cambia solo per ciò che entra o esce dalla città
            long after = Money(p) + y.City;
            Assert.Equal(y.MoneyBefore + (p.ExternalIn - y.ExtIn) - (p.ExternalOut - y.ExtOut), after);
        }

        [Fact]
        public void UnAnnoSenzaValoriAssurdi()
        {
            var y = Year.Value;
            var p = y.Pop;
            var d = PopulationTests.Inputs.Value.Data;
            int alive = p.AliveCount;
            Assert.InRange(alive, d.Total * 0.97, d.Total * 1.03);
            int births = y.Days.Sum(x => x.Births), deaths = y.Days.Sum(x => x.Deaths);
            Assert.InRange(births, d.Births * 0.7, d.Births * 1.3);
            Assert.InRange(deaths, d.Deaths * 0.75, d.Deaths * 1.25);
            Assert.InRange(y.Days.Sum(x => x.Arrived), (d.MovedIn + d.Immigrated) * 0.7, (d.MovedIn + d.Immigrated) * 1.3);
            Assert.InRange(y.Days.Sum(x => x.Left), (d.MovedOut + d.Emigrated) * 0.7, (d.MovedOut + d.Emigrated) * 1.3);

            // lavoro: la disoccupazione non esplode e le attività non falliscono in massa
            int labor = Enumerable.Range(0, p.Count).Count(i => p.Citizens[i].Alive && (p.Citizens[i].Works || p.Citizens[i].Occupation == Occupation.Unemployed));
            int unemployed = Enumerable.Range(0, p.Count).Count(i => p.Citizens[i].Alive && p.Citizens[i].Occupation == Occupation.Unemployed);
            Assert.InRange((double)unemployed / labor, 0.02, 0.25);
            int businesses = p.Workplaces.Count(w => !w.Public);
            Assert.True(p.Workplaces.Count(w => w.Closed) < businesses * 0.25, $"{p.Workplaces.Count(w => w.Closed)} chiuse su {businesses}");

            // famiglie: ognuno vivo sta in una famiglia che lo contiene; i morti e i partiti in nessuna
            for (int i = 0; i < p.Count; i++)
            {
                ref var c = ref p.Citizens[i];
                bool listed = c.Household >= 0 && p.Households[c.Household].Members.Contains(i);
                Assert.Equal(c.Alive, listed);
                if (c.Partner >= 0) Assert.True(p.Citizens[c.Partner].Alive && p.Citizens[c.Partner].Partner == i);
            }
            Assert.All(y.Days, x => Assert.True(x.ResidentialTax > 0 && x.Wages > 0 && x.Spending > 0));
            Assert.True(y.Ms < 20000, $"{y.Ms} ms per un anno");
        }

        [Fact]
        public void StessoGiornoNonSiRipete()
        {
            var pop = NewPopulation(7);
            var life = new CityLife(Life());
            var day = new DateTime(2026, 3, 1);
            pop.LastDay = CityLife.DayIndex(day) - 1;
            var taxes = new TaxRates { Residential = 1.5f, Commercial = 2f, Industrial = 2f };
            Assert.False(life.Day(pop, day, taxes, true, (f, a) => { }).Skipped);
            var bytes = PopulationSerializer.ToBytes(pop);
            Assert.True(life.Day(pop, day, taxes, true, (f, a) => { }).Skipped);
            Assert.Equal(bytes, PopulationSerializer.ToBytes(pop));
        }

        [Fact]
        public void SenzaCicloDiVitaNessunoNasceOMuore()
        {
            var pop = NewPopulation(9);
            var life = new CityLife(Life());
            var start = new DateTime(2026, 2, 1);
            pop.LastDay = CityLife.DayIndex(start) - 1;
            var taxes = new TaxRates { Residential = 1.5f, Commercial = 2f, Industrial = 2f };
            for (int k = 0; k < 60; k++)
            {
                var r = life.Day(pop, start.AddDays(k), taxes, false, (f, a) => { });
                Assert.Equal(0, r.Births + r.Deaths + r.Arrived + r.Left);
            }
            Assert.Equal(pop.Count, pop.AliveCount);
        }

        private static GameSession NewSession()
        {
            var b = Balance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "economy.json")));
            var g = GameSession.New("Chieti", new WorldConfig(), new DateTime(2026, 11, 16, 10, 0, 0), DateTime.UtcNow, 42,
                TestGame.Catalog(), b, new FakeWorld(), new Missions());
            g.AttachPopulation(NewPopulation(), Life(), keepBalance: true);
            return g;
        }

        [Fact]
        public void PartitaConICittadini_TasseNelRegistroEConti()
        {
            var g = NewSession();
            g.Advance(TimeSpan.FromDays(40));
            var s = g.State;
            Assert.Equal(s.StartCash + s.LedgerTotal, s.Cash);
            var day = s.Ledger[s.Ledger.Count - 2];
            Assert.True(day.Amounts[(int)LedgerCategory.ResidentialTax] > 20000, "tasse dei cittadini nel registro");
            Assert.True(day.Amounts[(int)LedgerCategory.CommercialTax] > 0);
            Assert.InRange(s.Stats.Population, 45000, 52000);
            // la città vera parte in leggero attivo, come quella stimata di M2
            double avg = s.Ledger.Skip(s.Ledger.Count - 15).Take(14).Average(x => (double)x.Net);
            Assert.InRange(avg, -2000, 25000);
            var f = Economy.Compute(g.Context);
            Assert.InRange(f.Net, -5000, 30000);
        }

        [Fact]
        public void SalvataggioERiavvolgimentoConICittadini()
        {
            var g = NewSession();
            g.Advance(TimeSpan.FromDays(3));
            var bytes = g.Save(DateTime.UtcNow);
            var back = GameSession.Load(bytes, g.Context.Catalog, g.Context.Balance, new FakeWorld(), new Missions(), Life());
            Assert.NotNull(back.Population);
            Assert.Equal(PopulationSerializer.ToBytes(g.Population), PopulationSerializer.ToBytes(back.Population));
            back.Advance(TimeSpan.FromDays(1));
            Assert.True(back.Population.LastDay > g.Population.LastDay);

            // riavvolgendo di un giorno tornano anche i cittadini di quel giorno
            var snap = g.Snapshots.Items[g.Snapshots.Items.Count - 2];
            var expected = g.PopulationSnapshots.Single(p => p.GameTime == snap.GameTime).Data;
            g.Advance(TimeSpan.FromDays(1));
            g.Rewind(snap);
            Assert.Equal(expected, PopulationSerializer.ToBytes(g.Population));
        }
    }
}
