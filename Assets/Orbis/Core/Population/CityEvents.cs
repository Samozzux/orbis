using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Core.Data;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Population
{
    /// <summary>Tipi di evento (docs/03 §10): dal calendario o generati.</summary>
    public enum CityEventKind : byte { Holiday = 0, Festival, Procession, Patron, Sagra, Inauguration, Accident }

    /// <summary>Evento del calendario (data/events/*.json): data fissa o rispetto a Pasqua, luogo, orari.</summary>
    public sealed class CalendarEvent
    {
        public string Key, Name, Place, Description;
        public CityEventKind Kind;
        public bool Holiday;
        /// <summary>Data fissa (mese, giorno) oppure giorni rispetto a Pasqua.</summary>
        public int Month, Day;
        public int? Easter;
        /// <summary>Orari in minuti dalla mezzanotte; Start == End = tutto il giorno senza raduno.</summary>
        public int Start, End;

        public bool On(DateTime date)
        {
            if (Easter.HasValue) return EventCalendar.Easter(date.Year).AddDays(Easter.Value) == date.Date;
            return date.Month == Month && date.Day == Day;
        }
    }

    /// <summary>Calendario della città: eventi nazionali e locali, con i luoghi trovati nei tile (monumenti).</summary>
    public sealed class EventCalendar
    {
        public readonly List<CalendarEvent> Events = new List<CalendarEvent>();
        /// <summary>Luoghi degli eventi (chiave del monumento → metri della regione).</summary>
        public readonly Dictionary<string, (float X, float Y)> Places = new Dictionary<string, (float, float)>();

        public static EventCalendar Parse(params string[] jsons)
        {
            var c = new EventCalendar();
            foreach (var json in jsons)
            {
                if (string.IsNullOrEmpty(json)) continue;
                foreach (var e in Json.Parse(json)["events"].Items)
                {
                    var ev = new CalendarEvent
                    {
                        Key = e["key"].AsString(""),
                        Name = e["name"].AsString(""),
                        Place = e["place"].AsString(null),
                        Description = e["description"].AsString(""),
                        Holiday = e["holiday"].AsBool(false),
                        Kind = Enum.TryParse<CityEventKind>(e["kind"].AsString("Festival"), true, out var k) ? k : CityEventKind.Festival,
                        Start = Minutes(e["start"].AsString(null), 0),
                        End = Minutes(e["end"].AsString(null), 0),
                    };
                    if (!e["easter"].IsNull) ev.Easter = e["easter"].AsInt(0);
                    else
                    {
                        var parts = e["date"].AsString("01-01").Split('-');
                        ev.Month = int.Parse(parts[0]);
                        ev.Day = int.Parse(parts[1]);
                    }
                    // fino alle 2 di notte: si chiude a mezzanotte (la giornata finisce lì)
                    if (ev.End < ev.Start) ev.End = 24 * 60;
                    c.Events.Add(ev);
                }
            }
            return c;
        }

        /// <summary>"HH:MM" in minuti dalla mezzanotte.</summary>
        public static int Minutes(string hhmm, int fallback)
        {
            if (string.IsNullOrEmpty(hhmm)) return fallback;
            var p = hhmm.Split(':');
            int m = Math.Min(24 * 60, int.Parse(p[0]) * 60 + (p.Length > 1 ? int.Parse(p[1]) : 0));
            return m == 24 * 60 - 1 ? 24 * 60 : m; // "23:59" = fino a mezzanotte
        }

        /// <summary>Domenica di Pasqua (calendario gregoriano, algoritmo di Meeus/Jones/Butcher).</summary>
        public static DateTime Easter(int y)
        {
            int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
            return new DateTime(y, month, day);
        }

        public List<CalendarEvent> On(DateTime date)
        {
            var list = new List<CalendarEvent>();
            foreach (var e in Events)
                if (e.On(date)) list.Add(e);
            return list;
        }

        /// <summary>Giorno festivo (scuole e uffici chiusi), domeniche escluse.</summary>
        public bool IsHoliday(DateTime date)
        {
            foreach (var e in Events)
                if (e.Holiday && e.On(date)) return true;
            return false;
        }

        /// <summary>Trova i luoghi degli eventi tra i monumenti dei tile (edifici con la chiave del monumento).</summary>
        public void LocatePlaces(IEnumerable<(TileData Tile, double OriginX, double OriginY)> tiles)
        {
            foreach (var (tile, ox, oy) in tiles)
                foreach (var b in tile.Buildings)
                    if (!string.IsNullOrEmpty(b.Landmark) && !Places.ContainsKey(b.Landmark))
                        Places[b.Landmark] = ((float)(ox + (b.Bounds.MinX + b.Bounds.MaxX) / 2), (float)(oy + (b.Bounds.MinY + b.Bounds.MaxY) / 2));
        }
    }

    /// <summary>Parametri degli eventi (sezione "events" di data/balance/services.json).</summary>
    public sealed class EventsSpec
    {
        public double AttendShare = 0.3, BonusDecay = 0.5, AccidentsPerYear = 120, SagraChance = 0.3;
        public int StayMinutes = 150, SagraStart = 18 * 60, SagraEnd = 24 * 60, NewsSize = 60;
        public readonly double[] Bonus = new double[Enum.GetValues(typeof(CityEventKind)).Length];
        public readonly HashSet<int> SagraMonths = new HashSet<int> { 6, 7, 8, 9 };
        public string CenterPlace = "";

        public static EventsSpec Parse(JsonValue j)
        {
            var s = new EventsSpec
            {
                AttendShare = j["attendShare"].AsDouble(0.3),
                StayMinutes = j["stayMinutes"].AsInt(150),
                BonusDecay = j["bonusDecay"].AsDouble(0.5),
                AccidentsPerYear = j["accidentsPerYear"].AsDouble(120),
                SagraChance = j["sagraChance"].AsDouble(0.3),
                SagraStart = EventCalendar.Minutes(j["sagraStart"].AsString("18:00"), 18 * 60),
                SagraEnd = EventCalendar.Minutes(j["sagraEnd"].AsString("23:59"), 24 * 60),
                CenterPlace = j["centerPlace"].AsString(""),
                NewsSize = Math.Max(1, j["newsSize"].AsInt(60)),
            };
            foreach (var k in j["bonus"].Keys)
                if (Enum.TryParse<CityEventKind>(k, out var kind)) s.Bonus[(int)kind] = j["bonus"][k].AsDouble(0);
            if (j["sagraMonths"].Count > 0)
            {
                s.SagraMonths.Clear();
                foreach (var m in j["sagraMonths"].Items) s.SagraMonths.Add(m.AsInt(0));
            }
            return s;
        }
    }

    /// <summary>Evento di oggi (dal calendario o generato): luogo e orari per il raduno, persone presenti.</summary>
    public sealed class CityEvent
    {
        public string Key = "";
        /// <summary>Nome dal calendario; vuoto per gli eventi generati (il testo viene dalle tabelle Localization).</summary>
        public string Name = "";
        /// <summary>Dettaglio: via dell'incidente, elemento inaugurato…</summary>
        public string Detail = "";
        public CityEventKind Kind;
        public bool Generated, Holiday;
        public int Day;
        /// <summary>Raduno: luogo (metri della regione) e locale di svago più vicino (-1 = nessun raduno).</summary>
        public float X, Y;
        public int Place = -1;
        public int Start, End;
        public int Attendance;

        public void Write(BinaryWriter w)
        {
            w.Write(Key ?? "");
            w.Write(Name ?? "");
            w.Write(Detail ?? "");
            w.Write((byte)Kind);
            w.Write(Generated);
            w.Write(Holiday);
            w.Write(Day);
            w.Write(X);
            w.Write(Y);
            w.Write(Place);
            w.Write(Start);
            w.Write(End);
            w.Write(Attendance);
        }

        public static CityEvent Read(BinaryReader r) => new CityEvent
        {
            Key = r.ReadString(), Name = r.ReadString(), Detail = r.ReadString(), Kind = (CityEventKind)r.ReadByte(), Generated = r.ReadBoolean(),
            Holiday = r.ReadBoolean(), Day = r.ReadInt32(), X = r.ReadSingle(), Y = r.ReadSingle(), Place = r.ReadInt32(),
            Start = r.ReadInt32(), End = r.ReadInt32(), Attendance = r.ReadInt32(),
        };
    }

    /// <summary>
    /// Eventi della città (docs/03 §10): ogni giorno si preparano quelli di domani (calendario, sagre d'estate), la
    /// giornata dei cittadini li vive (festivi, raduni), a fine giornata entrano nelle notizie e lasciano felicità.
    /// </summary>
    public sealed class CityEvents
    {
        public readonly EventCalendar Calendar;
        public readonly EventsSpec Spec;

        public CityEvents(EventCalendar calendar, EventsSpec spec)
        {
            Calendar = calendar;
            Spec = spec;
        }

        /// <summary>Prepara gli eventi del giorno <paramref name="date"/> (sostituisce quelli del giorno prima).</summary>
        public List<CityEvent> Plan(CityPopulation pop, DateTime date)
        {
            int day = CityLife.DayIndex(date);
            pop.Today.Clear();
            pop.TodayDay = day;
            var rng = new Rng(((ulong)(uint)pop.Seed << 32) ^ (uint)day ^ 0xE7E47UL);
            foreach (var e in Calendar.On(date))
            {
                var ev = new CityEvent { Key = e.Key, Name = e.Name, Kind = e.Kind, Holiday = e.Holiday, Day = day, Start = e.Start, End = e.End };
                if (!string.IsNullOrEmpty(e.Place) && e.End > e.Start && Calendar.Places.TryGetValue(e.Place, out var p)) Gather(pop, ev, p.X, p.Y);
                pop.Today.Add(ev);
            }
            // sagra d'estate il sabato in piazza
            if (date.DayOfWeek == DayOfWeek.Saturday && Spec.SagraMonths.Contains(date.Month) && rng.Chance(Spec.SagraChance)
                && Calendar.Places.TryGetValue(Spec.CenterPlace ?? "", out var c))
            {
                var ev = new CityEvent { Key = "sagra", Kind = CityEventKind.Sagra, Generated = true, Day = day, Start = Spec.SagraStart, End = Spec.SagraEnd };
                Gather(pop, ev, c.X, c.Y);
                pop.Today.Add(ev);
            }
            return new List<CityEvent>(pop.Today);
        }

        /// <summary>Il raduno si tiene nel locale di svago (bar, piazza…) più vicino al luogo dell'evento.</summary>
        private static void Gather(CityPopulation pop, CityEvent ev, float x, float y)
        {
            ev.X = x;
            ev.Y = y;
            double best = double.MaxValue;
            for (int i = 0; i < pop.Workplaces.Count; i++)
            {
                var w = pop.Workplaces[i];
                if (w.Closed || w.Public) continue;
                double d = (w.X - x) * (w.X - x) + (w.Y - y) * (w.Y - y);
                if (d < best)
                {
                    best = d;
                    ev.Place = i;
                }
            }
        }

        /// <summary>Oggi è festivo (scuole e uffici chiusi, negozi chiusi)?</summary>
        public static bool Holiday(CityPopulation pop, int day)
        {
            if (pop.TodayDay != day) return false;
            foreach (var e in pop.Today)
                if (e.Holiday) return true;
            return false;
        }

        /// <summary>
        /// Fine giornata: gli eventi di oggi entrano nelle notizie, lasciano felicità (che poi si consuma) e si
        /// aggiungono gli incidenti stradali del giorno.
        /// </summary>
        public List<CityEvent> Close(CityPopulation pop, DateTime date, Rng rng)
        {
            int day = CityLife.DayIndex(date);
            var done = new List<CityEvent>();
            pop.EventBonus = (float)(pop.EventBonus * Spec.BonusDecay);
            if (pop.TodayDay == day)
                foreach (var e in pop.Today)
                {
                    pop.EventBonus += (float)Spec.Bonus[(int)e.Kind];
                    done.Add(e);
                }
            // incidenti stradali (quota annua), in una via a caso della città
            double perDay = Spec.AccidentsPerYear / 365.0;
            int accidents = (int)perDay + (rng.Chance(perDay - (int)perDay) ? 1 : 0);
            for (int k = 0; k < accidents && pop.Dwellings.Count > 0; k++)
            {
                var dw = pop.Dwellings[rng.Next(pop.Dwellings.Count)];
                done.Add(new CityEvent { Key = "incidente", Kind = CityEventKind.Accident, Generated = true, Day = day, X = dw.X, Y = dw.Y, Detail = Street(dw.Address) });
            }
            foreach (var e in done) AddNews(pop, e);
            return done;
        }

        /// <summary>Inaugurazione di un edificio del sindaco: notizia e un po' di felicità in città.</summary>
        public CityEvent Inaugurate(CityPopulation pop, DateTime date, string what, float x, float y)
        {
            var e = new CityEvent { Key = "inaugurazione", Kind = CityEventKind.Inauguration, Generated = true, Day = CityLife.DayIndex(date), Detail = what ?? "", X = x, Y = y };
            pop.EventBonus += (float)Spec.Bonus[(int)CityEventKind.Inauguration];
            AddNews(pop, e);
            return e;
        }

        private void AddNews(CityPopulation pop, CityEvent e)
        {
            pop.News.Add(e);
            if (pop.News.Count > Spec.NewsSize) pop.News.RemoveRange(0, pop.News.Count - Spec.NewsSize);
        }

        /// <summary>"Via Arniense 12" → "Via Arniense".</summary>
        public static string Street(string address)
        {
            if (string.IsNullOrEmpty(address)) return "";
            // si tolgono in fondo le parole con un numero (civico, "12/A")
            var words = new List<string>(address.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            while (words.Count > 1 && words[words.Count - 1].IndexOfAny("0123456789".ToCharArray()) >= 0) words.RemoveAt(words.Count - 1);
            return string.Join(" ", words).TrimEnd(',');
        }
    }
}
