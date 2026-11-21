using System;
using System.IO;
using System.Linq;
using Orbis.Core.Population;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Eventi della città (M4 blocco 5c): calendario, festivi, raduni, sagre, incidenti, inaugurazioni.</summary>
    public class EventsTests
    {
        private static string Data(params string[] path) => File.ReadAllText(Path.Combine(new[] { PopulationTests.Root, "data" }.Concat(path).ToArray()));

        private static ServicesBalance Balance() => ServicesBalance.Parse(Data("balance", "services.json"));

        private static EventCalendar Calendar() => EventCalendar.Parse(Data("events", "italia.json"), Data("events", "chieti.json"));

        [Fact]
        public void Pasqua()
        {
            Assert.Equal(new DateTime(2024, 3, 31), EventCalendar.Easter(2024));
            Assert.Equal(new DateTime(2025, 4, 20), EventCalendar.Easter(2025));
            Assert.Equal(new DateTime(2026, 4, 5), EventCalendar.Easter(2026));
            Assert.Equal(new DateTime(2027, 3, 28), EventCalendar.Easter(2027));
        }

        [Fact]
        public void CalendarioVero()
        {
            var c = Calendar();
            Assert.Contains(c.On(new DateTime(2026, 5, 11)), e => e.Key == "san_giustino" && e.Kind == CityEventKind.Patron && e.Holiday);
            var friday = Assert.Single(c.On(new DateTime(2026, 4, 3)));
            Assert.Equal("venerdi_santo_chieti", friday.Key);
            Assert.Equal(CityEventKind.Procession, friday.Kind);
            Assert.Equal(19 * 60, friday.Start);
            Assert.Equal(22 * 60 + 30, friday.End);
            Assert.False(c.IsHoliday(new DateTime(2026, 4, 3)));
            Assert.True(c.IsHoliday(new DateTime(2026, 4, 6)));   // Pasquetta
            Assert.True(c.IsHoliday(new DateTime(2026, 12, 25)));
            Assert.False(c.IsHoliday(new DateTime(2026, 12, 24)));
            Assert.Equal(24 * 60, c.On(new DateTime(2026, 12, 31)).Single().End); // fino alle 2: si chiude a mezzanotte
        }

        [Fact]
        public void LuoghiDaiMonumentiDeiTile()
        {
            var c = Calendar();
            var outer = new float[] { 100, 100, 140, 100, 140, 160, 100, 160 };
            var tile = new TileData
            {
                Id = new TileId(14, 1, 1),
                Buildings = new[] { new BuildingInfo { OsmId = 5, Landmark = "cattedrale_san_giustino", Outer = outer, Bounds = Bounds2.Of(outer) } },
            };
            c.LocatePlaces(new[] { (tile, 1000.0, 2000.0) });
            Assert.Equal((1120f, 2130f), c.Places["cattedrale_san_giustino"]);
            Assert.Equal("Via Arniense", CityEvents.Street("Via Arniense 12"));
            Assert.Equal("Viale IV Novembre", CityEvents.Street("Viale IV Novembre 3/A"));
            Assert.Equal("Piazza San Giustino", CityEvents.Street("Piazza San Giustino"));
        }

        private static (CityPopulation Pop, CityEvents Events, LifeInputs Inputs) City()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            var pop = PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), 21);
            var cal = Calendar();
            // la cattedrale al centro della città finta
            cal.Places["cattedrale_san_giustino"] = (3000f, 3000f);
            var services = Balance();
            var inputs = new LifeInputs { Data = d, Names = n, Balance = b, Services = services, Calendar = cal };
            return (pop, new CityEvents(cal, services.Events), inputs);
        }

        [Fact]
        public void SanGiustino_FestivoERaduno()
        {
            var (pop, events, inputs) = City();
            var day = new DateTime(2026, 5, 11); // lunedì, festa del patrono
            var plan = events.Plan(pop, day);
            var patron = Assert.Single(plan);
            Assert.True(patron.Place >= 0);
            Assert.True(CityEvents.Holiday(pop, CityLife.DayIndex(day)));
            var routines = new CityRoutines(inputs);
            int atWork = 0, atSchool = 0;
            for (int q = 1; q <= 96; q++)
            {
                var t = day.AddMinutes(15 * q);
                routines.Step(pop, t);
                if (t.Hour == 10 && t.Minute == 30)
                    for (int i = 0; i < pop.Count; i++)
                    {
                        if (!pop.Citizens[i].Alive) continue;
                        if (pop.Citizens[i].Location == LocationKind.Work && pop.Citizens[i].Activity == CitizenActivity.Work) atWork++;
                        if (pop.Citizens[i].Location == LocationKind.School) atSchool++;
                    }
            }
            // festivo: niente scuola né lavoro; alla festa va molta gente
            Assert.Equal(0, atSchool);
            Assert.Equal(0, atWork);
            Assert.True(patron.Attendance > 1000, $"{patron.Attendance} alla festa");

            // a fine giornata: nelle notizie, con un po' di felicità che poi si consuma
            float before = pop.EventBonus;
            var done = events.Close(pop, day, new Rng(1));
            Assert.Contains(done, e => e.Key == "san_giustino");
            Assert.Contains(pop.News, e => e.Key == "san_giustino" && e.Attendance == patron.Attendance);
            Assert.True(pop.EventBonus > before + 4);
            float bonus = pop.EventBonus;
            events.Plan(pop, day.AddDays(1));
            events.Close(pop, day.AddDays(1), new Rng(2));
            Assert.Equal(bonus * 0.5f, pop.EventBonus, 3);
        }

        [Fact]
        public void GiornoFeriale_LavoroNormale()
        {
            var (pop, events, inputs) = City();
            var day = new DateTime(2026, 5, 12); // martedì
            Assert.Empty(events.Plan(pop, day));
            var routines = new CityRoutines(inputs);
            int atWork = 0;
            for (int q = 1; q <= 42; q++) routines.Step(pop, day.AddMinutes(15 * q));
            for (int i = 0; i < pop.Count; i++)
                if (pop.Citizens[i].Alive && pop.Citizens[i].Activity == CitizenActivity.Work) atWork++;
            Assert.True(atWork > 3000);
        }

        [Fact]
        public void SagreIncidentiEInaugurazioni()
        {
            var (pop, events, _) = City();
            events.Spec.SagraChance = 1;
            var saturday = new DateTime(2026, 7, 11);
            var sagra = Assert.Single(events.Plan(pop, saturday), e => e.Kind == CityEventKind.Sagra);
            Assert.True(sagra.Generated);
            Assert.Equal("", sagra.Name); // il nome viene dalle tabelle Localization
            Assert.Equal(18 * 60, sagra.Start);
            Assert.Empty(events.Plan(pop, new DateTime(2026, 11, 14))); // sabato d'inverno: niente sagra

            // incidenti: circa 120 l'anno, in vie vere
            int accidents = 0;
            var rng = new Rng(3);
            for (int k = 0; k < 365; k++)
                accidents += events.Close(pop, new DateTime(2026, 1, 1).AddDays(k), rng).Count(e => e.Kind == CityEventKind.Accident);
            Assert.InRange(accidents, 90, 150);
            Assert.All(pop.News.Where(e => e.Kind == CityEventKind.Accident), e => Assert.StartsWith("Via Prova", e.Detail));
            Assert.True(pop.News.Count <= events.Spec.NewsSize);

            var inauguration = events.Inaugurate(pop, saturday, "Biblioteca", 10, 20);
            Assert.Equal(CityEventKind.Inauguration, inauguration.Kind);
            Assert.Equal("Biblioteca", pop.News.Last().Detail);
        }

        [Fact]
        public void EventiNelSalvataggioENellaGiornata()
        {
            var (pop, events, inputs) = City();
            var life = new CityLife(inputs);
            Assert.NotNull(life.Events);
            var day = new DateTime(2026, 4, 3); // Venerdì Santo
            pop.LastDay = CityLife.DayIndex(day) - 1;
            life.Events.Plan(pop, day);
            var r = life.Day(pop, day, new TaxRates { Residential = 1.5f, Commercial = 2, Industrial = 2 }, true, (f, a) => { });
            Assert.Contains(r.Events, e => e.Key == "venerdi_santo_chieti");
            // il giorno dopo è già preparato (sabato santo: nessun evento)
            Assert.Equal(CityLife.DayIndex(day.AddDays(1)), pop.TodayDay);
            Assert.True(pop.EventBonus > 0);
            var back = PopulationSerializer.FromBytes(PopulationSerializer.ToBytes(pop));
            Assert.Equal(pop.EventBonus, back.EventBonus);
            Assert.Equal(pop.TodayDay, back.TodayDay);
            Assert.Equal(pop.News.Select(e => (e.Key, e.Kind, e.Day, e.Detail, e.Attendance)), back.News.Select(e => (e.Key, e.Kind, e.Day, e.Detail, e.Attendance)));
        }
    }
}
