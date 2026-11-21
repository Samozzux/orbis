using System;
using System.IO;
using System.Linq;
using Orbis.Core.Population;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Reazioni dei cittadini (M4 blocco 5b): commenti, petizioni, proteste, scioperi, partenze e arrivi.</summary>
    public class ReactionsTests
    {
        private static ServicesBalance Balance() =>
            ServicesBalance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "services.json")));

        private static readonly TaxRates Normal = new TaxRates { Residential = 1.5f, Commercial = 2, Industrial = 2 };
        private static readonly TaxRates Heavy = new TaxRates { Residential = 8f, Commercial = 2, Industrial = 2 };

        private static (CityPopulation Pop, CityServices Services) City(int seed = 4)
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            var pop = PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), seed);
            var s = Safe(pop);
            s.Update(pop);
            return (pop, s);
        }

        /// <summary>Servizi con caserme e pompieri sparsi (la città finta non ne ha): la sicurezza non è il problema di tutti.</summary>
        private static CityServices Safe(CityPopulation pop)
        {
            var s = new CityServices(Balance());
            for (int i = 0; i < pop.Dwellings.Count; i += 6)
            {
                s.Extra.Add(new ServiceSource { Kind = ServiceKind.Police, X = pop.Dwellings[i].X, Y = pop.Dwellings[i].Y });
                s.Extra.Add(new ServiceSource { Kind = ServiceKind.Fire, X = pop.Dwellings[i].X, Y = pop.Dwellings[i].Y });
            }
            return s;
        }

        private static readonly DateTime Start = new DateTime(2026, 3, 2);

        /// <summary>Un giorno di felicità e reazioni (senza il resto della vita della città).</summary>
        private static System.Collections.Generic.List<Reaction> Day(CityPopulation pop, CityServices s, TaxRates t, int k, int level = ReactionLevel.Realistic)
        {
            var date = Start.AddDays(k);
            CityHappiness.Update(pop, s, t, date);
            return CityReactions.Day(pop, CityLife.DayIndex(date), level, s.Balance.Reactions, new Rng((ulong)k + 1), date);
        }

        [Fact]
        public void IlProblemaPeggiorePesaEDiventaLaPreoccupazione()
        {
            var (pop, s) = City();
            var rep = CityHappiness.Update(pop, s, Heavy, Start);
            // con le tasse all'8% la preoccupazione più comune sono le tasse, e molti sono scontenti
            int taxes = Enumerable.Range(0, pop.Count).Count(i => pop.Citizens[i].Alive && pop.Citizens[i].Worry == (byte)HappinessFactor.Taxes);
            Assert.True(taxes > pop.AliveCount / 2, $"{taxes} preoccupati per le tasse");
            Assert.True(rep.Unhappy > 0);
            var (calm, s2) = City();
            var normal = CityHappiness.Update(calm, s2, Normal, Start);
            Assert.True(normal.Average > rep.Average + 10);
            Assert.DoesNotContain(Enumerable.Range(0, calm.Count).Where(i => calm.Citizens[i].Alive), i => calm.Citizens[i].Worry == (byte)HappinessFactor.Taxes);
        }

        [Fact]
        public void LivelliDelleReazioni()
        {
            var (pop, s) = City();
            Assert.Empty(Day(pop, s, Heavy, 0, ReactionLevel.Off));
            Assert.Empty(pop.Feed);
            // leggere: solo commenti, anche con la città arrabbiata
            var light = Day(pop, s, Heavy, 1, ReactionLevel.Light);
            Assert.Equal(s.Balance.Reactions.CommentsPerDay, light.Count);
            Assert.All(light, r => Assert.Equal(ReactionKind.Comment, r.Kind));
            Assert.All(light, r => Assert.InRange(r.Citizen, 0, pop.Count - 1));
            Assert.Contains(light, r => !r.Positive && r.Factor == HappinessFactor.Taxes);
            Assert.Empty(pop.Petitions);
            Assert.InRange(pop.Approval, 0, 100);
        }

        [Fact]
        public void PetizioneSiApreCresceESiChiudeQuandoIlSindacoAscolta()
        {
            var (pop, s) = City();
            var first = Day(pop, s, Heavy, 0);
            var petition = Assert.Single(first, r => r.Kind == ReactionKind.Petition);
            Assert.Equal(HappinessFactor.Taxes, petition.Factor);
            Assert.True(petition.Count > 100);
            int signatures = pop.Petitions.Single().Signatures;
            Day(pop, s, Heavy, 1);
            Assert.True(pop.Petitions.Single(p => p.Factor == HappinessFactor.Taxes).Signatures > signatures);
            // il sindaco riporta le tasse al 1,5%: la petizione si chiude soddisfatta
            var after = Day(pop, s, Normal, 2);
            Assert.Contains(after, r => r.Kind == ReactionKind.PetitionAccepted && r.Factor == HappinessFactor.Taxes);
            Assert.DoesNotContain(pop.Petitions, p => p.Factor == HappinessFactor.Taxes);
        }

        [Fact]
        public void PetizioneIgnorataEProteste()
        {
            var (pop, s) = City();
            var all = Enumerable.Range(0, 40).SelectMany(k => Day(pop, s, Heavy, k)).ToList();
            Assert.Contains(all, r => r.Kind == ReactionKind.PetitionIgnored && r.Factor == HappinessFactor.Taxes);
            var protests = all.Where(r => r.Kind == ReactionKind.Protest).ToList();
            Assert.NotEmpty(protests);
            Assert.All(protests, p => Assert.True(p.Count > 0));
            // al massimo una protesta a settimana
            for (int i = 1; i < protests.Count; i++) Assert.True(protests[i].Day - protests[i - 1].Day >= 7);
            Assert.True(pop.Feed.Count <= s.Balance.Reactions.FeedSize);
        }

        [Fact]
        public void SciopeoSeIlLavoroPreoccupaALungo()
        {
            var (pop, s) = City();
            Reaction strike = null;
            for (int k = 0; k < 5 && strike == null; k++)
            {
                var date = Start.AddDays(k);
                CityHappiness.Update(pop, s, Normal, date);
                pop.HappinessFactors[(int)HappinessFactor.Work] = 30; // lavoro che preoccupa tutti
                strike = CityReactions.Day(pop, CityLife.DayIndex(date), ReactionLevel.Realistic, s.Balance.Reactions, new Rng(9), date)
                    .FirstOrDefault(r => r.Kind == ReactionKind.Strike);
            }
            Assert.NotNull(strike);
            Assert.Equal(pop.StrikeDay, strike.Day);
            Assert.True(strike.Count > 1000);
        }

        [Fact]
        public void EffettiSuProduttivitaPartenzeEArrivi()
        {
            var r = Balance().Reactions;
            Assert.Equal(1.0, r.Productivity(74), 6);
            Assert.True(r.Productivity(40) < 0.95);
            Assert.True(r.Emigration(45) > 2);
            Assert.True(r.Emigration(85) < 0.5);
            Assert.True(r.Arrivals(85) > 1.5);
            Assert.Equal(0.5, r.Arrivals(40), 6);

            // un mese con le tasse all'8%: con le reazioni realistiche partono molte più famiglie
            int Left(int level, TaxRates taxes)
            {
                var (d, n, b) = PopulationTests.Inputs.Value;
                var pop = PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), 12);
                var life = new CityLife(new LifeInputs { Data = d, Names = n, Balance = b, Services = Balance() }) { Reactions = level };
                life.Services.Extra = Safe(pop).Extra;
                pop.LastDay = CityLife.DayIndex(Start) - 1;
                int left = 0;
                for (int k = 0; k < 30; k++) left += life.Day(pop, Start.AddDays(k), taxes, true, (f, a) => { }).Left;
                return left;
            }
            int calm = Left(ReactionLevel.Light, Heavy);
            int angry = Left(ReactionLevel.Realistic, Heavy);
            Assert.True(angry > calm * 3 / 2, $"partiti {angry} con le reazioni realistiche, {calm} con quelle leggere");
        }

        [Fact]
        public void ReazioniNelSalvataggio()
        {
            var (pop, s) = City();
            for (int k = 0; k < 3; k++) Day(pop, s, Heavy, k);
            pop.StrikeDay = 1234;
            var back = PopulationSerializer.FromBytes(PopulationSerializer.ToBytes(pop));
            Assert.Equal(pop.Approval, back.Approval);
            Assert.Equal(pop.Feed.Count, back.Feed.Count);
            Assert.Equal(pop.Feed.Select(r => (r.Kind, r.Day, r.Factor, r.Citizen, r.Count, r.Positive)),
                back.Feed.Select(r => (r.Kind, r.Day, r.Factor, r.Citizen, r.Count, r.Positive)));
            Assert.Equal(pop.Petitions.Select(p => (p.Factor, p.Signatures, p.StartDay)), back.Petitions.Select(p => (p.Factor, p.Signatures, p.StartDay)));
            Assert.Equal(1234, back.StrikeDay);
            Assert.All(Enumerable.Range(0, pop.Count), i => Assert.Equal(pop.Citizens[i].Worry, back.Citizens[i].Worry));
        }
    }
}
