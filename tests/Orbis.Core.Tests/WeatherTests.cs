using System;
using System.IO;
using System.Linq;
using Orbis.Core.Game;
using Orbis.Core.Population;
using Orbis.Core.World.Sky;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Meteo reale e clima medio (M4 blocco 5d).</summary>
    public class WeatherTests
    {
        private static ClimateNormals Chieti() =>
            ClimateNormals.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "climate", "chieti.json")));

        [Fact]
        public void ClimaDiChieti()
        {
            var c = Chieti();
            Assert.Equal("Chieti", c.Name);
            Assert.Equal(42.35, c.Latitude, 2);
            Assert.InRange(c.Months[0].TempMean, 2, 8);    // gennaio
            Assert.InRange(c.Months[6].TempMean, 20, 27);  // luglio
            Assert.True(c.Months[6].Cloud < c.Months[10].Cloud);
            Assert.True(c.Months[5].RadiationMj > c.Months[11].RadiationMj * 3);
            Assert.InRange(c.AnnualWind, 4, 12);
        }

        [Fact]
        public void RichiesteAOpenMeteo()
        {
            var today = new DateTime(2026, 11, 21);
            // vicino a oggi: previsioni
            var (url, shift) = OpenMeteo.Request(42.35, 14.17, new DateTime(2026, 11, 21), new DateTime(2026, 11, 28), today);
            Assert.Equal(0, shift);
            Assert.StartsWith("https://api.open-meteo.com/v1/forecast?latitude=42.35&longitude=14.17", url);
            Assert.Contains("start_date=2026-11-21&end_date=2026-11-28", url);
            Assert.Contains("hourly=" + OpenMeteo.Hourly, url);
            // il gioco è corso avanti di un mese: lo stesso periodo dell'anno scorso dall'archivio
            (url, shift) = OpenMeteo.Request(42.35, 14.17, new DateTime(2026, 12, 20), new DateTime(2026, 12, 27), today);
            Assert.Equal(1, shift);
            Assert.StartsWith("https://archive-api.open-meteo.com/v1/archive?", url);
            Assert.Contains("start_date=2025-12-20&end_date=2025-12-27", url);
        }

        [Fact]
        public void RispostaDiOpenMeteo()
        {
            const string json = @"{""latitude"": 42.35, ""hourly"": {
              ""time"": [""2025-12-20T00:00"", ""2025-12-20T01:00"", ""2025-12-20T02:00"", ""2025-12-20T03:00""],
              ""temperature_2m"": [4.1, 3.8, 3.5, null],
              ""precipitation"": [0.0, 1.2, 0.0, null],
              ""cloud_cover"": [10, 100, 90, null],
              ""wind_speed_10m"": [5.0, 12.5, 8.0, null],
              ""weather_code"": [0, 61, 45, null],
              ""shortwave_radiation"": [0, 0, 0, null]}}";
            var hours = OpenMeteo.Parse(json, 1);
            Assert.Equal(3, hours.Count); // l'ora senza dati si salta
            Assert.Equal(new DateTime(2026, 12, 20, 1, 0, 0), hours[1].Time);
            Assert.Equal(WeatherKind.Clear, hours[0].Kind);
            Assert.Equal(WeatherKind.Rain, hours[1].Kind);
            Assert.Equal(WeatherKind.Fog, hours[2].Kind);
            Assert.Equal(12.5f, hours[1].WindKmh);
            Assert.True(hours.All(h => h.Real));
            Assert.Equal(WeatherKind.Cloudy, OpenMeteo.KindOf(3, 95, 0));
            Assert.Equal(WeatherKind.PartlyCloudy, OpenMeteo.KindOf(2, 50, 0));
            Assert.Equal(WeatherKind.Rain, OpenMeteo.KindOf(73, 100, 0.4f)); // neve: per ora come pioggia
        }

        [Fact]
        public void UnAnnoSimulatoComeIlClima()
        {
            var climate = Chieti();
            var w = new CityWeather(climate, 42);
            var temps = new double[12];
            var hours = new int[12];
            int rain = 0, total = 0;
            for (var t = new DateTime(2026, 1, 1); t < new DateTime(2027, 1, 1); t = t.AddHours(1))
            {
                var h = w.Step(t);
                temps[t.Month - 1] += h.TempC;
                hours[t.Month - 1]++;
                if (h.Raining) rain++;
                total++;
                Assert.False(h.Real);
            }
            for (int m = 0; m < 12; m++) Assert.InRange(temps[m] / hours[m], climate.Months[m].TempMean - 2.5, climate.Months[m].TempMean + 2.5);
            Assert.InRange(rain / (double)total, 0.03, 0.3);
            // stesso seme, stesso meteo
            var a = new CityWeather(climate, 7);
            var b = new CityWeather(climate, 7);
            for (int i = 0; i < 500; i++)
            {
                var t = new DateTime(2026, 3, 1).AddHours(i);
                Assert.Equal(a.Step(t).Kind, b.Step(t).Kind);
            }
        }

        [Fact]
        public void DatiVeriAlPostoDeiSimulati()
        {
            var w = new CityWeather(Chieti(), 3);
            var t = new DateTime(2026, 11, 21, 10, 0, 0);
            w.SetReal(new[] { new WeatherHour { Time = t, Kind = WeatherKind.Rain, TempC = 9, WindKmh = 30, Real = true } });
            Assert.True(w.HasReal(t));
            Assert.Equal(t.AddHours(1), w.MissingFrom(t, 24));
            var h = w.Step(t);
            Assert.True(h.Real);
            Assert.True(h.Raining);
            Assert.Equal(9, h.TempC);
            // con il meteo simulato scelto nella partita i dati veri si ignorano
            var sim = new CityWeather(Chieti(), 3) { UseReal = false };
            sim.SetReal(new[] { new WeatherHour { Time = t, Kind = WeatherKind.Rain, TempC = 9, Real = true } });
            Assert.False(sim.Step(t).Real);
        }

        private static CityWeather Day(DateTime day, float wind, float cloud, WeatherKind kind)
        {
            var w = new CityWeather(Chieti(), 1);
            w.SetReal(Enumerable.Range(0, 24).Select(h => new WeatherHour
            {
                Time = day.AddHours(h), Kind = kind, WindKmh = wind, Cloud = cloud, Real = true,
                Radiation = (float)Math.Max(0, Math.Sin((h - 6) / 12.0 * Math.PI)) * 800 * (1 - cloud / 140f),
            }));
            for (int h = 0; h < 24; h++) w.Step(day.AddHours(h));
            return w;
        }

        [Fact]
        public void SoleAMetaGiornataSenzaContareLaNotte()
        {
            var c = Chieti();
            var w = new CityWeather(c, 5);
            var day = new DateTime(2026, 11, 21);
            // mattina serena di novembre: sole pieno per la stagione
            w.SetReal(Enumerable.Range(0, 10).Select(h => new WeatherHour
            {
                Time = day.AddHours(h), Kind = WeatherKind.Clear, Real = true,
                Radiation = (float)(Math.Max(0, Math.Sin((h - 6) / 12.0 * Math.PI)) * c.Months[10].RadiationMj * 1e6 / 3600 / 7.6 * 1.2),
            }));
            for (int h = 0; h < 10; h++) w.Step(day.AddHours(h));
            double season = c.Months[10].RadiationMj / c.AnnualRadiation;
            Assert.InRange(w.SolarFactor, season * 1.1, season * 1.3);
        }

        [Fact]
        public void EnergiaColVentoEColSole()
        {
            var day = new DateTime(2026, 6, 10);
            var windy = Day(day, 20, 20, WeatherKind.PartlyCloudy);
            var calm = Day(day, 2, 100, WeatherKind.Rain);
            Assert.True(windy.WindFactor > 2);
            Assert.True(calm.WindFactor < 0.4);
            Assert.True(windy.SolarFactor > calm.SolarFactor * 2);
            Assert.Equal(1f, new CityWeather(Chieti(), 1).WindFactor); // nessun dato: produzione media

            // una pala eolica produce secondo il vento del giorno
            var g = TestGame.New(new Balance { StartCash = 500000, XpBase = 100, XpExponent = 1f });
            g.State.Config.CreativeMode = true;
            Assert.Equal(BuildResult.Ok, g.Do(TestGame.Place("pala", 60, 30)));
            double Power(CityWeather w)
            {
                g.Context.Weather = w;
                return Economy.Compute(g.Context).PowerKw;
            }
            double none = Power(null);
            Assert.True(Power(windy) > none + 500);
            Assert.True(Power(calm) < none - 300);
        }

        [Fact]
        public void ConLaPioggiaMenoGenteAPiediEPiuAuto()
        {
            var (d, n, b) = PopulationTests.Inputs.Value;
            (int Walk, int Car) Trips(WeatherKind kind)
            {
                var pop = PopulationGenerator.Generate(d, n, b, PopulationTests.FakeCity(), 8);
                var day = new DateTime(2026, 3, 4);
                var routines = new CityRoutines(new LifeInputs { Data = d, Names = n, Balance = b }) { Weather = Day(day, 8, 90, kind) };
                routines.Weather.Step(day.AddHours(7));
                int walk = 0, car = 0;
                for (int q = 1; q <= 96; q++)
                {
                    var t = day.AddMinutes(15 * q);
                    routines.Step(pop, t);
                    int minute = CityRoutines.Minute(t);
                    for (int i = 0; i < pop.Count; i++)
                    {
                        ref var c = ref pop.Citizens[i];
                        if (!c.Alive || c.TravelStart != minute || c.TravelEnd <= c.TravelStart) continue;
                        if (c.Mode == TravelMode.Walk) walk++;
                        else if (c.Mode == TravelMode.Car) car++;
                    }
                }
                return (walk, car);
            }
            var dry = Trips(WeatherKind.Cloudy);
            var wet = Trips(WeatherKind.Rain);
            Assert.True(wet.Walk < dry.Walk * 0.9, $"a piedi: {wet.Walk} con la pioggia, {dry.Walk} senza");
            Assert.True(wet.Car > dry.Car, $"in auto: {wet.Car} con la pioggia, {dry.Car} senza");
        }
    }
}
