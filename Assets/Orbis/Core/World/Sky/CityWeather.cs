using System;
using System.Collections.Generic;
using System.Globalization;
using Orbis.Core.Data;

namespace Orbis.Core.World.Sky
{
    /// <summary>Meteo di un'ora: tipo (per il cielo), temperatura, pioggia, nuvole, vento, sole.</summary>
    public struct WeatherHour
    {
        public DateTime Time;
        public WeatherKind Kind;
        public float TempC;
        /// <summary>Pioggia in mm nell'ora.</summary>
        public float RainMm;
        /// <summary>Nuvolosità 0–100.</summary>
        public float Cloud;
        public float WindKmh;
        /// <summary>Radiazione solare W/m².</summary>
        public float Radiation;
        /// <summary>Dato vero (Open-Meteo) o simulato dal clima medio.</summary>
        public bool Real;

        public bool Raining => Kind == WeatherKind.Rain;
    }

    /// <summary>Clima medio per mese di una città (data/climate/*.json, da tools/climate).</summary>
    public sealed class ClimateNormals
    {
        public sealed class Month
        {
            public float TempMean, TempMax, TempMin, RainHoursPerDay, RainyDayShare, Cloud, WindKmh, RadiationMj;
        }

        public string Name = "";
        public double Latitude, Longitude;
        public readonly Month[] Months = new Month[12];

        public ClimateNormals()
        {
            for (int m = 0; m < 12; m++) Months[m] = new Month { TempMean = 14, TempMax = 19, TempMin = 9, RainHoursPerDay = 3, Cloud = 45, WindKmh = 7, RadiationMj = 15 };
        }

        public static ClimateNormals Parse(string json)
        {
            var j = Json.Parse(json);
            var c = new ClimateNormals { Name = j["name"].AsString(""), Latitude = j["latitude"].AsDouble(0), Longitude = j["longitude"].AsDouble(0) };
            foreach (var m in j["months"].Items)
            {
                int i = m["month"].AsInt(1) - 1;
                if (i < 0 || i > 11) continue;
                c.Months[i] = new Month
                {
                    TempMean = m["tempMean"].AsFloat(14), TempMax = m["tempMax"].AsFloat(19), TempMin = m["tempMin"].AsFloat(9),
                    RainHoursPerDay = m["rainHoursPerDay"].AsFloat(3), RainyDayShare = m["rainyDayShare"].AsFloat(0.3f),
                    Cloud = m["cloudCover"].AsFloat(45), WindKmh = m["windKmh"].AsFloat(7), RadiationMj = m["radiationMjPerDay"].AsFloat(15),
                };
            }
            return c;
        }

        public Month Of(DateTime t) => Months[t.Month - 1];

        public float AnnualWind
        {
            get
            {
                float s = 0;
                foreach (var m in Months) s += m.WindKmh;
                return s / 12;
            }
        }

        public float AnnualRadiation
        {
            get
            {
                float s = 0;
                foreach (var m in Months) s += m.RadiationMj;
                return s / 12;
            }
        }
    }

    /// <summary>
    /// Meteo reale da Open-Meteo (CC BY 4.0, docs/02): previsioni per le date vicine a oggi, archivio storico per le
    /// altre (lo stesso giorno di anni passati), così il tempo di gioco, che corre più veloce, ha sempre dati veri.
    /// Nella richiesta ci sono solo le coordinate della città.
    /// </summary>
    public static class OpenMeteo
    {
        public const string Hourly = "temperature_2m,precipitation,cloud_cover,wind_speed_10m,weather_code,shortwave_radiation";

        /// <summary>Richiesta per le ore di gioco [from, to]: indirizzo e anni di spostamento (0 = previsioni).</summary>
        public static (string Url, int YearShift) Request(double lat, double lon, DateTime from, DateTime to, DateTime realToday)
        {
            string coords = "latitude=" + lat.ToString("0.####", CultureInfo.InvariantCulture) + "&longitude=" + lon.ToString("0.####", CultureInfo.InvariantCulture);
            string tz = "&timezone=Europe%2FRome&hourly=" + Hourly;
            // le previsioni coprono 92 giorni indietro e 16 avanti
            if (from.Date >= realToday.AddDays(-90) && to.Date <= realToday.AddDays(15))
                return ("https://api.open-meteo.com/v1/forecast?" + coords + tz + "&start_date=" + D(from) + "&end_date=" + D(to), 0);
            // altrimenti l'archivio (ERA5 arriva fino a circa 5 giorni fa): lo stesso periodo di anni passati
            int shift = 0;
            while (to.AddYears(-shift).Date > realToday.AddDays(-6)) shift++;
            while (to.AddYears(-shift).Year < 1950) shift--;
            return ("https://archive-api.open-meteo.com/v1/archive?" + coords + tz + "&start_date=" + D(from.AddYears(-shift)) + "&end_date=" + D(to.AddYears(-shift)), shift);
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Ore della risposta, riportate alle date di gioco (anni di spostamento aggiunti).</summary>
        public static List<WeatherHour> Parse(string json, int yearShift = 0)
        {
            var list = new List<WeatherHour>();
            var h = Json.Parse(json)["hourly"];
            var time = h["time"];
            for (int i = 0; i < time.Count; i++)
            {
                if (!DateTime.TryParseExact(time[i].AsString(""), "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
                if (h["temperature_2m"][i].IsNull) continue; // ora ancora senza dati
                float rain = h["precipitation"][i].AsFloat(0), cloud = h["cloud_cover"][i].AsFloat(0);
                list.Add(new WeatherHour
                {
                    Time = t.AddYears(yearShift),
                    TempC = h["temperature_2m"][i].AsFloat(0),
                    RainMm = rain,
                    Cloud = cloud,
                    WindKmh = h["wind_speed_10m"][i].AsFloat(0),
                    Radiation = h["shortwave_radiation"][i].AsFloat(0),
                    Kind = KindOf(h["weather_code"][i].AsInt(-1), cloud, rain),
                    Real = true,
                });
            }
            return list;
        }

        /// <summary>Codice meteo WMO → tipo per il cielo (la neve per ora si disegna come pioggia).</summary>
        public static WeatherKind KindOf(int wmo, float cloud, float rainMm)
        {
            if (wmo == 45 || wmo == 48) return WeatherKind.Fog;
            if (wmo >= 51 || rainMm >= 0.2f) return WeatherKind.Rain;
            if (wmo == 3 || (wmo < 0 && cloud >= 75)) return WeatherKind.Cloudy;
            if (wmo == 1 || wmo == 2 || (wmo < 0 && cloud >= 30)) return WeatherKind.PartlyCloudy;
            return WeatherKind.Clear;
        }
    }

    /// <summary>
    /// Meteo della partita, un'ora alla volta: dati veri quando ci sono (e se la partita usa il meteo reale), altrimenti
    /// simulati con la catena di <see cref="WeatherSimulator"/> e le temperature del clima medio. Tiene il riepilogo
    /// del giorno per l'energia (vento e sole) e dice alla giornata dei cittadini se piove.
    /// </summary>
    public sealed class CityWeather
    {
        public readonly ClimateNormals Climate;
        private readonly WeatherSimulator _sim;
        private readonly Dictionary<long, WeatherHour> _real = new Dictionary<long, WeatherHour>();
        private uint _noise;

        /// <summary>Usa i dati veri quando ci sono (impostazione "Meteo reale" della partita).</summary>
        public bool UseReal = true;

        public WeatherHour Current { get; private set; }

        // riepilogo del giorno in corso e di quello prima
        private DateTime _day;
        private double _windSum, _radSum, _radExpected;
        private int _hours, _rainHours;
        public float DayWindKmh { get; private set; }
        public float DayRadiationMj { get; private set; }
        public int DayRainHours { get; private set; }

        public CityWeather(ClimateNormals climate, uint seed)
        {
            Climate = climate ?? new ClimateNormals();
            _sim = new WeatherSimulator(seed);
            _noise = seed == 0 ? 0x1234567u : seed;
        }

        private static long Key(DateTime t) => t.Ticks / TimeSpan.TicksPerHour;

        /// <summary>Ore vere arrivate da Open-Meteo.</summary>
        public void SetReal(IEnumerable<WeatherHour> hours)
        {
            foreach (var h in hours) _real[Key(h.Time)] = h;
        }

        public bool HasReal(DateTime t) => _real.ContainsKey(Key(t));

        /// <summary>Prima ora (da <paramref name="from"/> in avanti, fino a <paramref name="hours"/> ore) senza dati veri; null se ci sono tutte.</summary>
        public DateTime? MissingFrom(DateTime from, int hours)
        {
            var t = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0);
            for (int i = 0; i < hours; i++, t = t.AddHours(1))
                if (!_real.ContainsKey(Key(t))) return t;
            return null;
        }

        private float Rand()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return (_noise & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Un'ora di gioco: il meteo di quell'ora diventa quello attuale.</summary>
        public WeatherHour Step(DateTime t)
        {
            var hour = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0);
            var kind = _sim.StepHour(t.Month, t.Hour); // la catena avanza sempre, così il simulato resta coerente
            WeatherHour w;
            if (UseReal && _real.TryGetValue(Key(hour), out var real)) w = real;
            else w = Simulated(hour, kind);
            Current = w;
            Accumulate(w);
            return w;
        }

        private WeatherHour Simulated(DateTime t, WeatherKind kind)
        {
            var m = Climate.Of(t);
            // giornata: minima verso le 6, massima verso le 15
            double phase = Math.Cos((t.Hour - 15) / 24.0 * 2 * Math.PI);
            float temp = (float)(m.TempMean + (m.TempMax - m.TempMin) / 2 * phase) + (Rand() - 0.5f) * 2;
            if (kind == WeatherKind.Rain) temp -= 2;
            float cloud = kind == WeatherKind.Clear ? 10 : kind == WeatherKind.PartlyCloudy ? 40 : kind == WeatherKind.Cloudy ? 85 : kind == WeatherKind.Rain ? 100 : 70;
            // sole: forma della giornata (6–18 ore circa) con la media del mese, meno le nuvole
            float radiation = (float)(ClimateRadiation(t) * (1 - 0.7 * cloud / 100.0) / (1 - 0.7 * m.Cloud / 100.0));
            return new WeatherHour
            {
                Time = t, Kind = kind, TempC = temp, Cloud = cloud,
                RainMm = kind == WeatherKind.Rain ? 0.5f + Rand() * 2 : 0,
                WindKmh = m.WindKmh * (0.5f + Rand()),
                Radiation = Math.Max(0, radiation),
            };
        }

        /// <summary>Sole medio del mese in quell'ora (W/m²): forma della giornata dalle 6 alle 18 con l'energia media del mese.</summary>
        private double ClimateRadiation(DateTime t)
        {
            double day = Math.Max(0, Math.Sin((t.Hour - 6) / 12.0 * Math.PI));
            return Climate.Of(t).RadiationMj * 1e6 / 3600.0 / 7.6 * day;
        }

        private void Accumulate(WeatherHour w)
        {
            if (w.Time.Date != _day)
            {
                _day = w.Time.Date;
                _windSum = _radSum = _radExpected = 0;
                _hours = _rainHours = 0;
            }
            _windSum += w.WindKmh;
            _radSum += w.Radiation * 3600 / 1e6;
            _radExpected += ClimateRadiation(w.Time) * 3600 / 1e6;
            _hours++;
            if (w.Raining) _rainHours++;
            DayWindKmh = (float)(_windSum / _hours);
            DayRadiationMj = (float)(_radSum * 24.0 / _hours);
            DayRainHours = _rainHours;
        }

        /// <summary>Produzione delle pale eoliche rispetto alla media (vento del giorno / vento medio annuo).</summary>
        public float WindFactor => _hours == 0 ? 1 : Math.Max(0.1f, Math.Min(2.5f, DayWindKmh / Math.Max(1, Climate.AnnualWind)));

        /// <summary>
        /// Produzione dei pannelli solari rispetto alla media dell'anno: il sole arrivato finora rispetto a quello atteso nelle
        /// stesse ore (così a metà giornata la notte che deve ancora venire non conta), per la stagione del mese.
        /// </summary>
        public float SolarFactor
        {
            get
            {
                if (_hours == 0) return 1;
                double season = Climate.Of(_day).RadiationMj / Math.Max(1, Climate.AnnualRadiation);
                double sky = _radExpected > 0.05 ? _radSum / _radExpected : 1;
                return (float)Math.Max(0.05, Math.Min(2, sky * season));
            }
        }
    }
}
