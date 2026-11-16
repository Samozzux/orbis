using System;
using Orbis.Core.World.Sky;
using Xunit;

namespace Orbis.Core.Tests
{
    public class SkyTests
    {
        [Fact]
        public void SoleAChieti_Solstizi()
        {
            // 21 giugno, mezzogiorno solare: elevazione ≈ 90 - 42.35 + 23.44 = 71°
            SunPosition.Compute(42.35, 172, 12, out var el, out var az);
            Assert.InRange(el, 70, 72);
            Assert.InRange(az, 170, 190);
            // 21 dicembre: ≈ 24°
            SunPosition.Compute(42.35, 355, 12, out el, out _);
            Assert.InRange(el, 23, 25.5);
            // mezzanotte: sotto l'orizzonte
            SunPosition.Compute(42.35, 172, 0, out el, out _);
            Assert.True(el < -10);
            // mattino a est, pomeriggio a ovest
            SunPosition.Compute(42.35, 80, 8, out _, out az);
            Assert.InRange(az, 90, 135);
            SunPosition.Compute(42.35, 80, 16, out _, out az);
            Assert.InRange(az, 225, 270);
        }

        [Fact]
        public void OraSolare_ChietiEOraLegale()
        {
            var t = new DateTime(2026, 7, 1, 13, 0, 0);
            // Chieti 14.17°E, ora legale (UTC+2): 13:00 → circa 11:57 solari
            Assert.Equal(11.94, SunPosition.SolarHour(t, 14.17, true), 1);
        }

        [Fact]
        public void Meteo_DeterministicoEVario()
        {
            var a = new WeatherSimulator(42);
            var b = new WeatherSimulator(42);
            var seen = new System.Collections.Generic.HashSet<WeatherKind>();
            for (int h = 0; h < 24 * 120; h++)
            {
                int month = 1 + h / (24 * 10) % 12;
                Assert.Equal(a.StepHour(month, h % 24), b.StepHour(month, h % 24));
                seen.Add(a.Current);
            }
            Assert.True(seen.Count >= 4, "in 120 giorni il meteo deve cambiare");
            Assert.Equal(1f, WeatherState.For(WeatherKind.Rain).Rain);
        }
    }
}
