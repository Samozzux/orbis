using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Orbis.Core.Population;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Generatore di popolazione (M4 blocco 2) con i dati veri di Chieti e una città finta.</summary>
    public class PopulationTests
    {
        internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        internal static readonly Lazy<(PopulationData Data, NameData Names, PopulationBalance Balance)> Inputs =
            new Lazy<(PopulationData, NameData, PopulationBalance)>(() => (
                PopulationData.Parse(File.ReadAllText(Path.Combine(Root, "data", "population", "chieti.json"))),
                NameData.Parse(File.ReadAllText(Path.Combine(Root, "data", "population", "names-it.json"))),
                PopulationBalance.Parse(File.ReadAllText(Path.Combine(Root, "data", "balance", "population.json")))));

        private static readonly Lazy<CityPopulation> Chieti = new Lazy<CityPopulation>(() => Generate(42));

        /// <summary>Città finta: 2.500 case, 400 posti di lavoro, scuole di ogni livello su 6×6 km.</summary>
        internal static CityPlaces FakeCity()
        {
            var b = Inputs.Value.Balance;
            var places = new CityPlaces(b);
            var rng = new Rng(7);
            for (int i = 0; i < 2500; i++)
            {
                places.Dwellings.Add(new Dwelling
                {
                    OsmId = 1000 + i,
                    X = (float)(rng.NextDouble() * 6000),
                    Y = (float)(rng.NextDouble() * 6000),
                    FloorArea = (float)(80 + rng.NextDouble() * rng.NextDouble() * 3000),
                    Units = 1,
                    Use = i % 3 == 0 ? BuildingUse.House : BuildingUse.Apartments,
                    Address = "Via Prova " + i,
                });
                places.DwellingWeights.Add(1f);
            }
            string[] cats = { "Office", "shop/supermarket", "amenity/bar", "Hospital", "Industrial", "amenity/pharmacy" };
            for (int i = 0; i < 400; i++)
            {
                string cat = cats[i % cats.Length];
                places.Workplaces.Add(new Place
                {
                    OsmId = 50000 + i,
                    X = (float)(rng.NextDouble() * 6000),
                    Y = (float)(rng.NextDouble() * 6000),
                    Category = cat,
                    Name = "Posto " + i,
                    Capacity = cat == "Hospital" ? 400 : 5 + rng.Next(40),
                    MinEducation = cat == "Hospital" || cat == "Office" ? Education.Upper : Education.None,
                });
            }
            for (int i = 0; i < 30; i++)
            {
                var level = i < 10 ? PlaceLevel.Kindergarten : i < 28 ? PlaceLevel.School : PlaceLevel.University;
                places.Schools.Add(new Place
                {
                    OsmId = 90000 + i,
                    X = (float)(rng.NextDouble() * 6000),
                    Y = (float)(rng.NextDouble() * 6000),
                    Level = level,
                    Category = level.ToString(),
                    Capacity = level == PlaceLevel.University ? 3000 : level == PlaceLevel.School ? 450 : 120,
                });
            }
            return places;
        }

        private static CityPopulation Generate(int seed)
        {
            var (d, n, b) = Inputs.Value;
            return PopulationGenerator.Generate(d, n, b, FakeCity(), seed);
        }

        private static int Age(CityPopulation p, int i) => p.Citizens[i].AgeOn(p.Date);

        [Fact]
        public void EtaESessoComeIstat()
        {
            var d = Inputs.Value.Data;
            var p = Chieti.Value;
            Assert.Equal(48368, p.Count);
            Assert.Equal(d.Total, p.Count);
            var male = new int[101];
            var female = new int[101];
            for (int i = 0; i < p.Count; i++)
            {
                int a = Math.Min(100, Age(p, i));
                if (p.Citizens[i].Sex == Sex.Male) male[a]++;
                else female[a]++;
            }
            Assert.Equal(d.Male, male);
            Assert.Equal(d.Female, female);
        }

        [Fact]
        public void FamiglieCoerenti()
        {
            var d = Inputs.Value.Data;
            var p = Chieti.Value;
            Assert.All(p.Households, h => Assert.NotEmpty(h.Members));
            int members = p.Households.Sum(h => h.Members.Count);
            Assert.Equal(p.Count, members); // ognuno in una sola famiglia
            for (int h = 0; h < p.Households.Count; h++)
                foreach (var m in p.Households[h].Members)
                    Assert.Equal(h, p.Citizens[m].Household);

            double avg = (double)p.Count / p.Households.Count;
            Assert.InRange(avg, d.AverageHouseholdSize - 0.25, d.AverageHouseholdSize + 0.25);

            // coppie: reciproche, di sesso diverso, nella stessa famiglia
            for (int i = 0; i < p.Count; i++)
            {
                int q = p.Citizens[i].Partner;
                if (q < 0) continue;
                Assert.Equal(i, p.Citizens[q].Partner);
                Assert.NotEqual(p.Citizens[i].Sex, p.Citizens[q].Sex);
                Assert.Equal(p.Citizens[i].Household, p.Citizens[q].Household);
            }

            // nessun minore vive senza un adulto abbastanza grande
            foreach (var h in p.Households)
            {
                int oldest = h.Members.Max(m => Age(p, m));
                foreach (var m in h.Members)
                    if (Age(p, m) < 18) Assert.True(oldest >= Age(p, m) + 17, "minore senza adulto in famiglia");
            }

            // tipi di famiglia: persone sole e coppie con figli nelle proporzioni ISTAT (±7 punti)
            int alone = p.Households.Count(h => h.Type == HouseholdType.Alone);
            double aloneShare = (double)alone / p.Households.Count;
            double istatAlone = (double)(d.HouseholdTypes["alone_male"] + d.HouseholdTypes["alone_female"]) / d.HouseholdTypes.Values.Sum();
            Assert.InRange(aloneShare, istatAlone - 0.07, istatAlone + 0.07);
            Assert.Contains(p.Households, h => h.Type == HouseholdType.CoupleWithChildren && h.Members.Count >= 4);
        }

        [Fact]
        public void CaseLavoriEScuole()
        {
            var b = Inputs.Value.Balance;
            var p = Chieti.Value;
            Assert.All(p.Households, h => Assert.InRange(h.Dwelling, 0, p.Dwellings.Count - 1));
            var people = new int[p.Dwellings.Count];
            foreach (var h in p.Households) people[h.Dwelling] += h.Members.Count;
            int over = Enumerable.Range(0, people.Length).Count(i => people[i] > p.Dwellings[i].Capacity);
            Assert.True(over < p.Dwellings.Count * 0.02, $"{over} case troppo piene");
            Assert.Contains(p.Households, h => h.Tenure == Tenure.Renter && h.Rent > 0);

            var workers = Enumerable.Range(0, p.Count).Where(i => p.Citizens[i].Works).ToList();
            double local = workers.Count(i => p.Citizens[i].Work >= 0) / (double)workers.Count;
            Assert.InRange(local, b.LocalWorkShare - 0.03, b.LocalWorkShare + 0.03);
            var used = new int[p.Workplaces.Count];
            foreach (var i in workers)
                if (p.Citizens[i].Work >= 0) used[p.Citizens[i].Work]++;
            for (int w = 0; w < used.Length; w++) Assert.True(used[w] <= p.Workplaces[w].Capacity);

            for (int i = 0; i < p.Count; i++)
            {
                ref var c = ref p.Citizens[i];
                if (c.Occupation == Occupation.Pupil)
                {
                    Assert.InRange(Age(p, i), b.KindergartenFrom, b.SchoolTo);
                    Assert.True(c.School >= 0, "alunno senza scuola");
                    var level = p.Schools[c.School].Level;
                    Assert.True(Age(p, i) < b.SchoolFrom || level == PlaceLevel.School);
                }
                if (!c.Works) Assert.True(c.Work == Citizen.None, "lavoro a chi non lavora");
            }
        }

        [Fact]
        public void RedditiComeMef()
        {
            var d = Inputs.Value.Data;
            var p = Chieti.Value;
            int pensioners = Enumerable.Range(0, p.Count).Count(i => p.Citizens[i].Occupation == Occupation.Retired);
            Assert.Equal(d.IncomeKindCount("pension"), pensioners);
            int workers = Enumerable.Range(0, p.Count).Count(i => p.Citizens[i].Works);
            Assert.Equal(d.IncomeKindCount("employee") + d.IncomeKindCount("self_employed") + d.IncomeKindCount("business"), workers);
            Assert.Equal(d.IncomeKindCount("self_employed"), Enumerable.Range(0, p.Count).Count(i => p.Citizens[i].Occupation == Occupation.SelfEmployed));

            var incomes = Enumerable.Range(0, p.Count).Select(i => p.Citizens[i].Income).Where(v => v > 0).ToList();
            Assert.Equal(pensioners + workers, incomes.Count);
            // fasce dai 10.000 € in su: stesso numero di persone del MEF (±5%); quelle sotto sono un po' meno
            foreach (var c in d.IncomeClasses.Where(c => c.Count >= 200 && (c.From ?? 0) >= 10000))
            {
                int n = incomes.Count(v => v >= (c.From ?? int.MinValue) && (c.To == null || v < c.To));
                Assert.InRange(n, c.Count * 0.95, c.Count * 1.05);
            }
            double total = incomes.Sum(v => (double)v);
            Assert.InRange(total, d.IncomeAmount * 0.9, d.IncomeAmount * 1.05);
            // chi ha più studio guadagna in media di più
            double Avg(Education e) => Enumerable.Range(0, p.Count).Where(i => p.Citizens[i].Works && p.Citizens[i].Education == e)
                .Select(i => (double)p.Citizens[i].Income).DefaultIfEmpty(0).Average();
            Assert.True(Avg(Education.Degree) > Avg(Education.Lower));
            Assert.All(Enumerable.Range(0, p.Count), i => Assert.True(p.Citizens[i].Savings >= 0));
        }

        [Fact]
        public void NomiTrattiEAspetto()
        {
            var names = Inputs.Value.Names;
            var b = Inputs.Value.Balance;
            var p = Chieti.Value;
            for (int i = 0; i < p.Count; i += 37)
            {
                var c = p.Citizens[i];
                var first = p.FirstNames[c.FirstName];
                Assert.Contains(first, names.FirstNames(c.Sex, c.BirthDate.Year));
                Assert.Contains(p.Surnames[c.Surname], names.Surnames);
                Assert.False(string.IsNullOrWhiteSpace(p.FullName(i)));
                int traits = Enumerable.Range(0, 64).Count(t => c.HasTrait(t));
                if (Age(p, i) >= 6) Assert.InRange(traits, b.TraitsMin, b.TraitsMax);
                foreach (var (x, y) in b.OppositeTraits) Assert.False(c.HasTrait(x) && c.HasTrait(y));
                Assert.InRange((int)c.Health, 20, 100);
            }
            // i figli portano il cognome del padre (o del genitore solo)
            var family = p.Households.First(h => h.Type == HouseholdType.CoupleWithChildren);
            int father = family.Members.First(m => p.Citizens[m].Role == FamilyRole.Partner && p.Citizens[m].Sex == Sex.Male);
            foreach (var m in family.Members.Where(m => p.Citizens[m].Role == FamilyRole.Child))
                Assert.Equal(p.Citizens[father].Surname, p.Citizens[m].Surname);
        }

        [Fact]
        public void StessoSeedStessaCitta()
        {
            var a = PopulationSerializer.ToBytes(Generate(42));
            Assert.Equal(a, PopulationSerializer.ToBytes(Chieti.Value));
            Assert.NotEqual(a, PopulationSerializer.ToBytes(Generate(43)));
        }

        [Fact]
        public void SalvataggioCompattoAndataERitorno()
        {
            var p = Chieti.Value;
            var bytes = PopulationSerializer.ToBytes(p);
            Assert.True(bytes.Length < 6_000_000, $"{bytes.Length} byte");
            var back = PopulationSerializer.FromBytes(bytes);
            Assert.Equal(p.Count, back.Count);
            Assert.Equal(p.FullName(1234), back.FullName(1234));
            Assert.Equal(bytes, PopulationSerializer.ToBytes(back));
            var bad = (byte[])bytes.Clone();
            bad[0] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => PopulationSerializer.FromBytes(bad));
        }

        [Fact]
        public void GenerazioneVeloceEAFasi()
        {
            var (d, n, b) = Inputs.Value;
            var g = new PopulationGenerator(d, n, b, FakeCity(), 5);
            var sw = Stopwatch.StartNew();
            var steps = g.Steps().ToList();
            sw.Stop();
            Assert.True(steps.Count >= 6);
            Assert.Equal(1f, steps.Last());
            Assert.True(sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds} ms");
            Assert.Equal(d.Total, g.Result.Count);
        }

        [Fact]
        public void CaseELavoriDaiTileVeri()
        {
            string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            var region = TileReader.ReadIndexFile(File.ReadAllBytes(Fixture("index_sample.bin")));
            var tile = TileReader.ReadFile(File.ReadAllBytes(Fixture("tile_sample.bin")), region.Scale);
            var chieti = region.Municipality("069022");
            Assert.Contains(tile.Id, CityPlaces.TilesFor(region, chieti));

            var places = new CityPlaces(region, chieti, Inputs.Value.Balance);
            places.Add(tile);
            // condominio di Corso Marrucino: 5 piani × 600 m²
            var apt = Assert.Single(places.Dwellings, d => d.Address == "Corso Marrucino 12");
            Assert.InRange(apt.FloorArea, 2900, 3100);
            Assert.Equal(BuildingUse.Apartments, apt.Use);
            Assert.DoesNotContain(places.Dwellings, d => d.Use == BuildingUse.Church);
            // il bar al piano terra del condominio è un posto di lavoro
            Assert.Contains(places.Workplaces, w => w.Category == "amenity/bar" && w.Name == "Bar Centrale");
            // il teatro sta nel comune "Ovest": non è di Chieti
            Assert.DoesNotContain(places.Workplaces, w => w.Name == "Teatro Marrucino");
        }
    }
}
