using System;
using System.Collections.Generic;

namespace Orbis.Core.Population
{
    /// <summary>Tempi di viaggio in auto sulla rete vera, con il traffico (lo fornisce Orbis.Core.Traffic).</summary>
    public interface ITravelTimes
    {
        int CarMinutes(float x0, float y0, float x1, float y1);
        /// <summary>Un viaggio in auto appena partito: carica le strade del percorso.</summary>
        void RecordTrip(float x0, float y0, float x1, float y1);
        /// <summary>Un passo di 15 minuti: aggiorna un po' di tempi.</summary>
        void Tick(int minute);
    }

    /// <summary>Trasporto pubblico: le linee reali (Orbis.Core.Traffic.TransitNetwork).</summary>
    public interface ITransit
    {
        /// <summary>Minuti porta a porta col trasporto pubblico (fermata, attesa, viaggio, fermata); -1 se non c'è.</summary>
        int Minutes(float x0, float y0, float x1, float y1, out int line);
        int FareCents(int line);
        void Ride(int line);
        /// <summary>Linea del comune (il biglietto va nella sua cassa) invece che di un'azienda esterna.</summary>
        bool IsCityLine(int line);
    }

    /// <summary>
    /// Giornata dei cittadini a passi di 15 minuti (livello L1, docs/01 §6, docs/03 §3): bisogni che calano e si
    /// recuperano, sonno, lavoro e scuola con orari veri, pasti col cibo del frigo, spesa nei negozi di alimentari
    /// reali quando il frigo è quasi vuoto (con scorte e cassa), svago in bar, palestre e cinema, spostamenti a piedi
    /// o in auto. Deterministica (seed + minuto). Il denaro della spesa passa davvero dal cittadino al negozio;
    /// <see cref="CityLife"/> lo toglie poi dalla spesa generica del giorno, così i conti restano chiusi.
    /// </summary>
    public sealed class CityRoutines
    {
        private readonly LifeInputs _in;
        private RoutineBalance R => _in.Balance.Routine;
        private PopulationBalance B => _in.Balance;

        // elenchi per tipo di luogo, ricostruiti quando cambia la popolazione
        private CityPopulation _cachedFor;
        private int _cachedPlaces;
        private readonly List<int> _food = new List<int>();
        private readonly List<int> _leisure = new List<int>();
        private readonly int _nightOwl, _earlyBird;
        private int _minute;
        private const int OutsideFareCents = 250;
        private TravelMode _lastMode;
        private int _lastLine = -1;

        /// <summary>Rete stradale con il traffico; null = tempi in linea d'aria.</summary>
        public ITravelTimes Travel;
        /// <summary>Linee del trasporto pubblico; null = nessuna.</summary>
        public ITransit Transit;

        public CityRoutines(LifeInputs inputs)
        {
            _in = inputs;
            _nightOwl = Array.IndexOf(B.TraitKeys, "night_owl");
            _earlyBird = Array.IndexOf(B.TraitKeys, "early_bird");
        }

        public static int Minute(DateTime t) => (int)((t - Citizen.Epoch).Ticks / TimeSpan.TicksPerMinute);

        public static DateTime FromMinute(int minute) => Citizen.Epoch.AddMinutes(minute);

        public int StepMinutes => R.StepMinutes;

        /// <summary>Un passo che finisce a <paramref name="time"/>: bisogni del passo, poi chi ha finito sceglie cosa fare.</summary>
        public void Step(CityPopulation pop, DateTime time)
        {
            Cache(pop);
            int minute = Minute(time);
            double dt = R.StepMinutes / 60.0;
            var rng = new Rng(((ulong)(uint)pop.Seed << 32) ^ (uint)minute ^ 0x5EED_0A11UL);
            int hour = time.Hour;
            bool weekday = time.DayOfWeek != DayOfWeek.Saturday && time.DayOfWeek != DayOfWeek.Sunday;
            bool sunday = time.DayOfWeek == DayOfWeek.Sunday;

            // all'alba i negozi di alimentari si riforniscono
            if (hour == 6 && time.Minute < R.StepMinutes)
                foreach (var i in _food) pop.Workplaces[i].Stock = pop.Workplaces[i].Capacity * R.StoreStockPerJob;

            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive) continue;
                Needs(pop, ref c, dt, hour, minute);
                if (c.ActivityEnd > minute) continue;
                _minute = minute;
                Decide(pop, i, minute, time, hour, weekday, sunday, rng);
            }
            Travel?.Tick(minute);
        }

        private void Cache(CityPopulation pop)
        {
            if (_cachedFor == pop && _cachedPlaces == pop.Workplaces.Count) return;
            _cachedFor = pop;
            _cachedPlaces = pop.Workplaces.Count;
            _food.Clear();
            _leisure.Clear();
            for (int i = 0; i < pop.Workplaces.Count; i++)
            {
                var p = pop.Workplaces[i];
                if (PopulationBalance.InSet(R.FoodCategories, p.Category)) _food.Add(i);
                if (PopulationBalance.InSet(R.LeisureCategories, p.Category)) _leisure.Add(i);
            }
        }

        // ------------------------------------------------------------------ bisogni

        private void Needs(CityPopulation pop, ref Citizen c, double dt, int hour, int minute)
        {
            bool traveling = minute < c.TravelEnd;
            double hunger = R.HungerPerHour, energy = R.EnergyPerHour, hygiene = R.HygienePerHour;
            double fun = R.FunPerHour, social = R.SocialPerHour, comfort = R.ComfortPerHour;
            switch (c.Activity)
            {
                case CitizenActivity.Sleep:
                    energy = R.SleepEnergyPerHour;
                    hunger *= 0.4;
                    hygiene *= 0.5;
                    fun = social = 0;
                    comfort = R.HomeComfortPerHour * 0.5;
                    break;
                case CitizenActivity.Home:
                case CitizenActivity.Eat:
                    comfort = R.HomeComfortPerHour;
                    fun = R.HomeFunPerHour;
                    if (c.Household >= 0 && pop.Households[c.Household].Members.Count > 1) social = R.FamilySocialPerHour;
                    energy *= 0.6;
                    break;
                case CitizenActivity.Leisure:
                    fun = R.LeisureFunPerHour;
                    social = R.LeisureSocialPerHour;
                    break;
                case CitizenActivity.Work:
                case CitizenActivity.School:
                    fun *= 1.5;
                    social = 0; // colleghi e compagni: la socialità non cala
                    break;
            }
            if (traveling) comfort *= 1.5;
            c.Hunger = Add(c.Hunger, hunger * dt);
            c.Energy = Add(c.Energy, energy * dt);
            c.Hygiene = Add(c.Hygiene, hygiene * dt);
            c.Fun = Add(c.Fun, fun * dt);
            c.Social = Add(c.Social, social * dt);
            c.Comfort = Add(c.Comfort, comfort * dt);
            // a pranzo chi lavora o studia mangia lì (mensa, panino portato da casa)
            if ((c.Activity == CitizenActivity.Work || c.Activity == CitizenActivity.School) && hour == 13 && c.Hunger < 60)
                c.Hunger = Add(c.Hunger, R.MealHunger);
            c.Mood = (byte)((c.Hunger + c.Energy + c.Hygiene + c.Fun + c.Social + c.Comfort + c.Health) / 7);
        }

        private static byte Add(byte v, double d) => (byte)Math.Max(0, Math.Min(100, Math.Round(v + d)));

        // ------------------------------------------------------------------ scelte

        private void Decide(CityPopulation pop, int i, int minute, DateTime time, int hour, bool weekday, bool sunday, Rng rng)
        {
            ref var c = ref pop.Citizens[i];
            int age = c.AgeOn(time);
            int shift = (_nightOwl >= 0 && c.HasTrait(_nightOwl) ? 1 : 0) - (_earlyBird >= 0 && c.HasTrait(_earlyBird) ? 1 : 0);
            int sleepStart = R.SleepStart + shift;
            int sleepEnd = R.SleepEnd + shift;
            if (age < 14) sleepStart = Math.Min(sleepStart, 21);
            bool night = InWindow(hour, sleepStart, sleepEnd);
            bool woke = c.Activity == CitizenActivity.Sleep;

            // 1) di notte (o sfinito) si dorme a casa fino alla sveglia
            if (night || c.Energy < 12)
            {
                int wake = NextHour(time, sleepEnd) + rng.Next(30);
                GoTo(pop, ref c, LocationKind.Home, 0, CitizenActivity.Sleep, minute, Math.Max(minute + R.StepMinutes, wake));
                return;
            }
            if (woke) c.Hygiene = Add(c.Hygiene, R.WakeHygiene); // doccia appena svegli

            // 2) lavoro e scuola negli orari veri
            if (c.Works && c.Work != Citizen.None)
            {
                bool shopWorker = c.Work >= 0 && PopulationBalance.InSet(B.ConsumerCategories, pop.Workplaces[c.Work].Category);
                int start = shopWorker ? R.ShopWorkStart : R.WorkStart, end = shopWorker ? R.ShopWorkEnd : R.WorkEnd;
                bool workday = shopWorker ? !sunday : weekday;
                if (workday && hour >= start && hour < end && c.Work >= 0 && !pop.Workplaces[c.Work].Closed)
                {
                    GoTo(pop, ref c, LocationKind.Work, c.Work, CitizenActivity.Work, minute, AtHour(time, end));
                    return;
                }
                if (workday && hour >= start && hour < end && c.Work == Citizen.WorkOutside)
                {
                    GoTo(pop, ref c, LocationKind.Outside, 0, CitizenActivity.Work, minute, AtHour(time, end));
                    return;
                }
            }
            if (c.Occupation == Occupation.Pupil && weekday)
            {
                bool kindergarten = age < B.SchoolFrom;
                int end = kindergarten ? R.KindergartenEnd : R.SchoolEnd;
                if (hour >= R.SchoolStart && hour < end)
                {
                    if (c.School >= 0) GoTo(pop, ref c, LocationKind.School, c.School, CitizenActivity.School, minute, AtHour(time, end));
                    else GoTo(pop, ref c, LocationKind.Outside, 0, CitizenActivity.School, minute, AtHour(time, end));
                    return;
                }
            }
            if (c.Occupation == Occupation.UniversityStudent && weekday && hour >= R.UniversityStart && hour < R.UniversityEnd && rng.Chance(0.8))
            {
                if (c.School >= 0) GoTo(pop, ref c, LocationKind.School, c.School, CitizenActivity.School, minute, AtHour(time, R.UniversityEnd));
                else GoTo(pop, ref c, LocationKind.Outside, 0, CitizenActivity.School, minute, AtHour(time, R.UniversityEnd));
                return;
            }

            // 3) i bambini piccoli restano a casa con la famiglia
            if (age < 12)
            {
                GoTo(pop, ref c, LocationKind.Home, 0, c.Hunger < 60 && MealTime(hour) ? CitizenActivity.Eat : CitizenActivity.Home, minute, minute + 60);
                if (c.Activity == CitizenActivity.Eat) EatAtHome(pop, ref c);
                return;
            }

            // 4) ora di pranzo o di cena e fame: si mangia a casa, se il frigo è vuoto fuori (chi può)
            if (c.Hunger < 65 && MealTime(hour))
            {
                var hh = c.Household >= 0 ? pop.Households[c.Household] : null;
                if (hh != null && hh.Fridge > 0)
                {
                    GoTo(pop, ref c, LocationKind.Home, 0, CitizenActivity.Eat, minute, minute + 30);
                    EatAtHome(pop, ref c);
                    return;
                }
                if (c.Savings > 20 && _leisure.Count > 0)
                {
                    int place = PickNear(pop, c, _leisure, rng, food: true);
                    if (place >= 0)
                    {
                        GoTo(pop, ref c, LocationKind.Leisure, place, CitizenActivity.Eat, minute, minute + 45);
                        c.Hunger = Add(c.Hunger, R.MealHunger);
                        return;
                    }
                }
            }

            // 5) frigo quasi vuoto: un adulto della famiglia va a fare la spesa
            if (age >= 18 && !sunday && hour >= R.StoresOpen && hour < R.StoresClose - 1 && c.Household >= 0 && _food.Count > 0)
            {
                var hh = pop.Households[c.Household];
                int capacity = Math.Max(1, hh.Members.Count) * R.FridgePortionsPerPerson;
                if (hh.Fridge < capacity * R.ShopWhenBelow)
                {
                    int store = PickStore(pop, c, rng);
                    if (store >= 0)
                    {
                        GoTo(pop, ref c, LocationKind.Shop, store, CitizenActivity.Shopping, minute, minute + 40);
                        Buy(pop, c.Household, store, capacity - hh.Fridge, i);
                        return;
                    }
                }
            }

            // 6) tempo libero: svago se servono divertimento o compagnia
            if (hour >= 10 && hour < 23 && (c.Fun < 55 || c.Social < 50) && _leisure.Count > 0 && rng.Chance(0.5))
            {
                int place = PickNear(pop, c, _leisure, rng, food: false);
                if (place >= 0)
                {
                    GoTo(pop, ref c, LocationKind.Leisure, place, CitizenActivity.Leisure, minute, minute + 60 + rng.Next(90));
                    return;
                }
            }

            // 7) altrimenti a casa per un po'
            GoTo(pop, ref c, LocationKind.Home, 0, CitizenActivity.Home, minute, minute + 45 + rng.Next(45));
        }

        private static bool InWindow(int hour, int start, int end) => start <= end ? hour >= start && hour < end : hour >= start || hour < end;

        private static bool MealTime(int hour) => (hour >= 7 && hour < 9) || (hour >= 12 && hour < 15) || (hour >= 19 && hour < 22);

        private static int AtHour(DateTime time, int hour) => Minute(time.Date.AddHours(hour));

        private static int NextHour(DateTime time, int hour)
        {
            var t = time.Date.AddHours(hour);
            if (t <= time) t = t.AddDays(1);
            return Minute(t);
        }

        private void EatAtHome(CityPopulation pop, ref Citizen c)
        {
            if (c.Household < 0) return;
            var hh = pop.Households[c.Household];
            if (hh.Fridge <= 0) return;
            hh.Fridge--;
            c.Hunger = Add(c.Hunger, R.MealHunger);
        }

        // ------------------------------------------------------------------ spesa

        /// <summary>Negozio di alimentari: vicino, con scorte, e col prezzo adatto al reddito della famiglia.</summary>
        private int PickStore(CityPopulation pop, in Citizen c, Rng rng)
        {
            Position(pop, c.Location, c.LocationIndex, c.Household, out var x, out var y);
            int best = -1;
            double bestScore = 0;
            int level = IncomeLevel(c.Income);
            foreach (var s in _food)
            {
                var p = pop.Workplaces[s];
                if (p.Closed || p.Stock <= 0) continue;
                double dx = p.X - x, dy = p.Y - y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int price = PriceLevel(p);
                double fit = price == level ? 1.0 : Math.Abs(price - level) == 1 ? 0.6 : 0.3;
                double score = fit / Math.Pow(dist + 250, 1.5) * (0.8 + 0.4 * rng.NextDouble());
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        private static int IncomeLevel(int income) => income < 15000 ? 0 : income < 40000 ? 1 : 2;

        /// <summary>Fascia di prezzo del negozio (discount, normale, caro), fissa e ricavata dall'ID OSM.</summary>
        public static int PriceLevel(Place p)
        {
            ulong h = (ulong)p.OsmId * 0x9E3779B97F4A7C15UL;
            int v = (int)((h >> 40) % 100);
            return v < 25 ? 0 : v < 85 ? 1 : 2;
        }

        private void Buy(CityPopulation pop, int household, int store, int portions, int buyer)
        {
            var hh = pop.Households[household];
            var p = pop.Workplaces[store];
            portions = Math.Min(portions, p.Stock);
            if (portions <= 0) return;
            double price = R.PortionPrice[Math.Max(0, Math.Min(2, PriceLevel(p)))];
            ref var c = ref pop.Citizens[buyer];
            // chi è in rosso compra solo quello che può (al massimo fino al limite del debito)
            double room = c.Savings + B.DebtLimitPerAdult;
            if (room < portions * price) portions = Math.Max(0, (int)(room / price));
            if (portions <= 0) return;
            int cost = (int)Math.Round(portions * price);
            c.Savings -= cost;
            p.Cash += cost;
            p.SalesToday += cost;
            p.Stock -= portions;
            hh.Fridge += portions;
            hh.FoodSpentToday += cost;
        }

        // ------------------------------------------------------------------ luoghi e spostamenti

        private int PickNear(CityPopulation pop, in Citizen c, List<int> list, Rng rng, bool food)
        {
            Position(pop, c.Location, c.LocationIndex, c.Household, out var x, out var y);
            int best = -1;
            double bestScore = 0;
            for (int k = 0; k < 10; k++)
            {
                int s = list[rng.Next(list.Count)];
                var p = pop.Workplaces[s];
                if (p.Closed) continue;
                if (food && !(p.Category.Contains("restaurant") || p.Category.Contains("bar") || p.Category.Contains("cafe")
                              || p.Category.Contains("fast_food") || p.Category.Contains("pub"))) continue;
                double dx = p.X - x, dy = p.Y - y;
                double score = 1.0 / (Math.Sqrt(dx * dx + dy * dy) + 200) * (0.6 + 0.8 * rng.NextDouble());
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Va nel luogo indicato (viaggio se è altrove) e ci resta fino a <paramref name="until"/>.</summary>
        private void GoTo(CityPopulation pop, ref Citizen c, LocationKind kind, int index, CitizenActivity activity, int minute, int until)
        {
            bool same = c.Location == kind && (kind == LocationKind.Home || kind == LocationKind.Outside || c.LocationIndex == index);
            int travel = same ? 0 : TravelMinutes(pop, c, kind, index);
            if (!same)
            {
                c.Mode = _lastMode;
                c.TransitLine = _lastLine;
                c.PreviousLocation = c.Location;
                c.PreviousIndex = c.LocationIndex;
                c.Location = kind;
                c.LocationIndex = index;
                c.TravelStart = minute;
                c.TravelEnd = minute + travel;
                if (kind == LocationKind.Work || kind == LocationKind.School) c.Commute = (byte)Math.Max(1, Math.Min(255, travel));
            }
            c.Activity = activity;
            c.ActivityEnd = Math.Max(until, minute + travel + R.StepMinutes);
        }

        private int TravelMinutes(CityPopulation pop, in Citizen c, LocationKind kind, int index)
        {
            _lastMode = TravelMode.Walk;
            _lastLine = -1;
            if (kind == LocationKind.Outside || c.Location == LocationKind.Outside)
            {
                // fuori città: in auto se c'è, altrimenti treno o bus regionale (biglietto fuori città)
                bool hasCar = c.Household >= 0 && pop.Households[c.Household].Cars > 0 && c.AgeOn(FromMinute(_minute)) >= 18;
                _lastMode = hasCar ? TravelMode.Car : TravelMode.Transit;
                if (!hasCar && c.Household >= 0) pop.Households[c.Household].TransitCents += OutsideFareCents;
                return R.OutsideTravelMinutes;
            }
            Position(pop, c.Location, c.LocationIndex, c.Household, out var x0, out var y0);
            Position(pop, kind, index, c.Household, out var x1, out var y1);
            double dx = x1 - x0, dy = y1 - y0, dist = Math.Sqrt(dx * dx + dy * dy) * 1.3; // strade non dritte
            int walk = Math.Max(2, (int)Math.Ceiling(dist / 1000.0 / R.WalkKmh * 60.0));
            if (dist <= R.WalkMaxM) return walk;

            // auto (se la famiglia ce l'ha e si è adulti), linea pubblica o a piedi: vince il tempo, con un po' di
            // preferenza per l'auto (comodità)
            bool car = c.Household >= 0 && pop.Households[c.Household].Cars > 0 && c.AgeOn(FromMinute(_minute)) >= 18;
            int carMin = Travel != null ? Travel.CarMinutes(x0, y0, x1, y1)
                : (int)Math.Ceiling(dist / 1000.0 / R.DriveKmh * 60.0) + 4;
            int line = -1;
            int busMin = Transit != null ? Transit.Minutes(x0, y0, x1, y1, out line) : -1;
            if (car && (busMin < 0 || carMin * R.CarTimeFactor <= busMin))
            {
                _lastMode = TravelMode.Car;
                Travel?.RecordTrip(x0, y0, x1, y1);
                return Math.Max(2, carMin);
            }
            if (busMin > 0 && busMin < walk)
            {
                _lastMode = TravelMode.Transit;
                _lastLine = line;
                Transit.Ride(line);
                if (c.Household >= 0)
                {
                    int fare = Transit.FareCents(line);
                    var hh = pop.Households[c.Household];
                    hh.TransitCents += fare;
                    if (Transit.IsCityLine(line)) hh.CityTransitCents += fare;
                }
                return Math.Max(2, busMin);
            }
            if (car)
            {
                _lastMode = TravelMode.Car;
                Travel?.RecordTrip(x0, y0, x1, y1);
                return Math.Max(2, carMin);
            }
            // niente auto né linee: a piedi (o un passaggio: un po' più veloce di camminare)
            return Math.Max(2, Math.Min(walk, carMin * 2));
        }

        /// <summary>Posizione (metri della regione) di un luogo; casa = casa della famiglia.</summary>
        public static void Position(CityPopulation pop, LocationKind kind, int index, int household, out float x, out float y)
        {
            x = y = 0;
            switch (kind)
            {
                case LocationKind.Home:
                    if (household >= 0 && pop.Households[household].Dwelling >= 0)
                    {
                        var d = pop.Dwellings[pop.Households[household].Dwelling];
                        x = d.X;
                        y = d.Y;
                    }
                    break;
                case LocationKind.Work:
                case LocationKind.Shop:
                case LocationKind.Leisure:
                    if (index >= 0 && index < pop.Workplaces.Count)
                    {
                        x = pop.Workplaces[index].X;
                        y = pop.Workplaces[index].Y;
                    }
                    break;
                case LocationKind.School:
                    if (index >= 0 && index < pop.Schools.Count)
                    {
                        x = pop.Schools[index].X;
                        y = pop.Schools[index].Y;
                    }
                    break;
            }
        }

        /// <summary>
        /// Dove si trova adesso (per le persone visibili, blocco 6): a destinazione, oppure lungo il tragitto in
        /// linea retta se è in viaggio. Restituisce false se è fuori città.
        /// </summary>
        public static bool CurrentPosition(CityPopulation pop, int i, int minute, out float x, out float y)
        {
            ref var c = ref pop.Citizens[i];
            Position(pop, c.Location, c.LocationIndex, c.Household, out x, out y);
            if (c.Location == LocationKind.Outside) return false;
            if (minute >= c.TravelEnd || minute < c.TravelStart || c.PreviousLocation == LocationKind.Outside) return true;
            Position(pop, c.PreviousLocation, c.PreviousIndex, c.Household, out var x0, out var y0);
            float t = (minute - c.TravelStart) / (float)Math.Max(1, c.TravelEnd - c.TravelStart);
            x = x0 + (x - x0) * t;
            y = y0 + (y - y0) * t;
            return true;
        }
    }
}
