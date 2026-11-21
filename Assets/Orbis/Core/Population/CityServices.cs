using System;
using System.Collections.Generic;
using Orbis.Core.Data;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Population
{
    /// <summary>Servizi della città (docs/03 §8): ognuno copre le case vicine.</summary>
    public enum ServiceKind : byte { Police = 0, Fire, Health, Education, Culture, Waste, Count }

    /// <summary>Fattori della felicità di un cittadino (docs/03 §6), ciascuno 0–100.</summary>
    public enum HappinessFactor : byte { Needs = 0, Home, Work, Money, Services, Safety, Commute, Taxes, Count }

    /// <summary>Parametri di servizi e felicità (data/balance/services.json).</summary>
    public sealed class ServicesBalance
    {
        public sealed class KindSpec
        {
            public ServiceKind Kind;
            public readonly HashSet<string> Categories = new HashSet<string>();
            public readonly HashSet<string> Catalog = new HashSet<string>();
            /// <summary>Contano anche le scuole della popolazione (tranne le università).</summary>
            public bool Schools;
            public double GoodM = 1000, MaxM = 3000;
            /// <summary>Tempo di arrivo in auto (minuti); 0 = conta solo la distanza.</summary>
            public double GoodMinutes, MaxMinutes;
            /// <summary>Servizio uguale in tutto il comune (punteggio fisso); -1 = no.</summary>
            public int Citywide = -1;
        }

        public readonly KindSpec[] Kinds = new KindSpec[(int)ServiceKind.Count];
        public readonly double[] Weights = new double[(int)HappinessFactor.Count];
        public readonly double[] ServiceWeights = new double[(int)ServiceKind.Count];
        public readonly double[] SafetyWeights = new double[(int)ServiceKind.Count];
        public double Employed = 80, Unemployed = 25, Retired = 70, Homemaker = 60, Student = 75;
        public double TaxDefault = 1.5, TaxPerPoint = 10, DailyChange = 0.3;
        public int UnhappyBelow = 40;
        /// <summary>Il problema più grave pesa di più: sotto <see cref="WorstBelow"/> toglie <see cref="WorstPenalty"/> per punto.</summary>
        public double WorstBelow = 60, WorstPenalty = 0.35;
        /// <summary>Reazioni dei cittadini (blocco 5b).</summary>
        public ReactionSpec Reactions = new ReactionSpec();
        /// <summary>Eventi (blocco 5c).</summary>
        public EventsSpec Events = new EventsSpec();

        public ServicesBalance()
        {
            for (int k = 0; k < Kinds.Length; k++) Kinds[k] = new KindSpec { Kind = (ServiceKind)k };
        }

        public static ServicesBalance Parse(string json)
        {
            var r = Json.Parse(json);
            var b = new ServicesBalance();
            foreach (var key in r["kinds"].Keys)
            {
                if (!Enum.TryParse<ServiceKind>(key, out var kind) || kind == ServiceKind.Count) continue;
                var j = r["kinds"][key];
                var s = b.Kinds[(int)kind];
                foreach (var c in j["categories"].Items) s.Categories.Add(c.AsString(""));
                foreach (var c in j["catalog"].Items) s.Catalog.Add(c.AsString(""));
                s.Schools = j["schools"].AsBool(false);
                s.GoodM = j["goodM"].AsDouble(s.GoodM);
                s.MaxM = Math.Max(s.GoodM + 1, j["maxM"].AsDouble(s.MaxM));
                s.GoodMinutes = j["goodMinutes"].AsDouble(0);
                s.MaxMinutes = Math.Max(s.GoodMinutes + 1, j["maxMinutes"].AsDouble(0));
                s.Citywide = j["citywide"].IsNull ? -1 : Math.Max(0, Math.Min(100, j["citywide"].AsInt(0)));
            }
            var h = r["happiness"];
            foreach (var key in h["weights"].Keys)
                if (Enum.TryParse<HappinessFactor>(key, true, out var f) && f != HappinessFactor.Count)
                    b.Weights[(int)f] = Math.Max(0, h["weights"][key].AsDouble(0));
            foreach (var key in h["services"].Keys)
                if (Enum.TryParse<ServiceKind>(key, out var k) && k != ServiceKind.Count) b.ServiceWeights[(int)k] = h["services"][key].AsDouble(0);
            foreach (var key in h["safety"].Keys)
                if (Enum.TryParse<ServiceKind>(key, out var k) && k != ServiceKind.Count) b.SafetyWeights[(int)k] = h["safety"][key].AsDouble(0);
            var w = h["work"];
            b.Employed = w["employed"].AsDouble(b.Employed);
            b.Unemployed = w["unemployed"].AsDouble(b.Unemployed);
            b.Retired = w["retired"].AsDouble(b.Retired);
            b.Homemaker = w["homemaker"].AsDouble(b.Homemaker);
            b.Student = w["student"].AsDouble(b.Student);
            b.TaxDefault = h["taxDefault"].AsDouble(b.TaxDefault);
            b.TaxPerPoint = h["taxPerPoint"].AsDouble(b.TaxPerPoint);
            b.DailyChange = Math.Max(0.01, Math.Min(1, h["dailyChange"].AsDouble(b.DailyChange)));
            b.UnhappyBelow = h["unhappyBelow"].AsInt(b.UnhappyBelow);
            b.WorstBelow = h["worstBelow"].AsDouble(b.WorstBelow);
            b.WorstPenalty = h["worstPenalty"].AsDouble(b.WorstPenalty);
            if (!r["reactions"].IsNull) b.Reactions = ReactionSpec.Parse(r["reactions"]);
            if (!r["events"].IsNull) b.Events = EventsSpec.Parse(r["events"]);
            return b;
        }

        /// <summary>Servizio offerto da un elemento del catalogo; Count se nessuno.</summary>
        public ServiceKind KindOfCatalog(string itemKey)
        {
            foreach (var s in Kinds)
                if (s.Catalog.Contains(itemKey ?? "")) return s.Kind;
            return ServiceKind.Count;
        }
    }

    /// <summary>Sede di un servizio in metri della regione (luogo vero o edificio del catalogo).</summary>
    public struct ServiceSource
    {
        public ServiceKind Kind;
        public float X, Y;
        public string Name;
    }

    /// <summary>
    /// Copertura dei servizi per ogni casa (docs/03 §8): la sede più vicina conta, con un punteggio che scende con la
    /// distanza (o col tempo di arrivo in auto per polizia e vigili del fuoco). Si ricalcola una volta al giorno.
    /// </summary>
    public sealed class CityServices
    {
        private readonly ServicesBalance _b;

        /// <summary>Tempi in auto sulla rete col traffico; null = distanza in linea d'aria.</summary>
        public ITravelTimes Travel;
        /// <summary>Sedi in più dagli edifici del catalogo (le prepara la partita).</summary>
        public List<ServiceSource> Extra = new List<ServiceSource>();
        /// <summary>Sedi vere lette dai tile del comune (<see cref="FromTiles"/>); vuoto = si usano i posti di lavoro e le scuole.</summary>
        public readonly List<ServiceSource> Fixed = new List<ServiceSource>();

        /// <summary>Sedi usate nell'ultimo calcolo.</summary>
        public readonly List<ServiceSource> Sources = new List<ServiceSource>();
        /// <summary>Copertura 0–100 per casa e servizio: [casa × Count + servizio].</summary>
        public byte[] Coverage = Array.Empty<byte>();

        public CityServices(ServicesBalance b) => _b = b;

        public ServicesBalance Balance => _b;

        public byte At(int dwelling, ServiceKind kind)
        {
            int i = dwelling * (int)ServiceKind.Count + (int)kind;
            return dwelling >= 0 && i < Coverage.Length ? Coverage[i] : (byte)0;
        }

        public int Count(ServiceKind kind)
        {
            int n = 0;
            foreach (var s in Sources)
                if (s.Kind == kind) n++;
            return n;
        }

        /// <summary>Sedi dei servizi: quelle vere dei tile (o, se mancano, posti di lavoro e scuole) più quelle in più.</summary>
        public void CollectSources(CityPopulation pop)
        {
            Sources.Clear();
            if (Fixed.Count > 0) Sources.AddRange(Fixed);
            else
            {
                foreach (var p in pop.Workplaces) AddPlace(p, false);
                foreach (var p in pop.Schools) AddPlace(p, true);
            }
            if (Extra != null) Sources.AddRange(Extra);
        }

        /// <summary>
        /// Sedi dei servizi nei tile (angolo sud-ovest in metri della regione): luoghi (POI) con la loro categoria, anche
        /// dentro un edificio, ed edifici per uso (ospedale, scuola, museo…). Le sedi doppie a meno di 40 m si contano una volta.
        /// </summary>
        public static List<ServiceSource> FromTiles(IEnumerable<(TileData Tile, double OriginX, double OriginY)> tiles, ServicesBalance b)
        {
            var list = new List<ServiceSource>();
            foreach (var (tile, ox, oy) in tiles)
            {
                foreach (var p in tile.Pois)
                    foreach (var s in b.Kinds)
                        if (s.Citywide < 0 && PopulationBalance.InSet(s.Categories, p.Category))
                            AddUnique(list, s.Kind, (float)(ox + p.X), (float)(oy + p.Y), p.Name);
                foreach (var bi in tile.Buildings)
                {
                    string use = bi.Use.ToString();
                    foreach (var s in b.Kinds)
                        if (s.Citywide < 0 && s.Categories.Contains(use))
                            AddUnique(list, s.Kind, (float)(ox + (bi.Bounds.MinX + bi.Bounds.MaxX) / 2), (float)(oy + (bi.Bounds.MinY + bi.Bounds.MaxY) / 2), bi.Name);
                }
            }
            return list;
        }

        private static void AddUnique(List<ServiceSource> list, ServiceKind kind, float x, float y, string name)
        {
            foreach (var s in list)
                if (s.Kind == kind && Math.Abs(s.X - x) < 40 && Math.Abs(s.Y - y) < 40) return;
            list.Add(new ServiceSource { Kind = kind, X = x, Y = y, Name = name });
        }

        private void AddPlace(Place p, bool school)
        {
            foreach (var s in _b.Kinds)
            {
                if (s.Citywide >= 0) continue;
                bool match = PopulationBalance.InSet(s.Categories, p.Category)
                             || (school && s.Schools && p.Level != PlaceLevel.University);
                if (match) Sources.Add(new ServiceSource { Kind = s.Kind, X = p.X, Y = p.Y, Name = p.Name });
            }
        }

        /// <summary>Ricalcola la copertura di tutte le case.</summary>
        public void Update(CityPopulation pop)
        {
            CollectSources(pop);
            int kinds = (int)ServiceKind.Count;
            if (Coverage.Length != pop.Dwellings.Count * kinds) Coverage = new byte[pop.Dwellings.Count * kinds];
            var byKind = new List<ServiceSource>[kinds];
            for (int k = 0; k < kinds; k++) byKind[k] = new List<ServiceSource>();
            foreach (var s in Sources) byKind[(int)s.Kind].Add(s);
            for (int d = 0; d < pop.Dwellings.Count; d++)
            {
                var dw = pop.Dwellings[d];
                for (int k = 0; k < kinds; k++)
                {
                    var spec = _b.Kinds[k];
                    double best = spec.Citywide >= 0 ? spec.Citywide : 0;
                    if (spec.Citywide < 0)
                        foreach (var s in byKind[k])
                        {
                            best = Math.Max(best, Score(spec, s, dw.X, dw.Y));
                            if (best >= 100) break;
                        }
                    Coverage[d * kinds + k] = (byte)Math.Round(Math.Max(0, Math.Min(100, best)));
                }
            }
        }

        private double Score(ServicesBalance.KindSpec spec, ServiceSource s, float x, float y)
        {
            double dx = x - s.X, dy = y - s.Y, dist = Math.Sqrt(dx * dx + dy * dy) * 1.3;
            if (dist > spec.MaxM * 2) return 0; // troppo lontano anche per i mezzi d'emergenza
            if (Travel != null && spec.GoodMinutes > 0)
            {
                // tempo di arrivo dalla sede alla casa sulla rete col traffico
                double minutes = Travel.CarMinutes(s.X, s.Y, x, y);
                return Linear(minutes, spec.GoodMinutes, spec.MaxMinutes);
            }
            return Linear(dist, spec.GoodM, spec.MaxM);
        }

        /// <summary>100 fino a <paramref name="good"/>, poi in discesa fino a 0 a <paramref name="max"/>.</summary>
        public static double Linear(double v, double good, double max)
        {
            if (v <= good) return 100;
            if (v >= max) return 0;
            return 100 * (max - v) / (max - good);
        }
    }

    /// <summary>Riepilogo della felicità della città dopo il calcolo del giorno.</summary>
    public sealed class HappinessReport
    {
        public float Average;
        /// <summary>Media di ciascun fattore sui cittadini.</summary>
        public readonly float[] Factors = new float[(int)HappinessFactor.Count];
        /// <summary>Copertura media dei servizi per abitante.</summary>
        public readonly float[] Coverage = new float[(int)ServiceKind.Count];
        public int Unhappy, Counted;
    }

    /// <summary>
    /// Felicità di ogni cittadino (docs/03 §6): media pesata di bisogni, casa, lavoro, soldi, servizi vicini,
    /// sicurezza, tempo del tragitto e tasse. Si avvicina ogni giorno al valore del momento, senza salti.
    /// </summary>
    public static class CityHappiness
    {
        public static HappinessReport Update(CityPopulation pop, CityServices services, TaxRates taxes, DateTime date)
        {
            var b = services.Balance;
            var rep = new HappinessReport();
            int n = (int)HappinessFactor.Count;
            var f = new double[n];
            double wsum = 0;
            foreach (var w in b.Weights) wsum += w;
            if (wsum <= 0) wsum = 1;
            double taxScore = Clamp(75 - (taxes.Residential - b.TaxDefault) * b.TaxPerPoint);
            bool first = pop.AverageHappiness <= 0;

            // per famiglia: reddito, soldi degli adulti (li condividono anche i figli), figli in età scolare
            var hhIncome = new long[pop.Households.Count];
            var hhMoney = new double[pop.Households.Count];
            var hhAdults = new int[pop.Households.Count];
            var kids = new bool[pop.Households.Count];
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Household < 0) continue;
                hhIncome[c.Household] += Math.Max(0, c.Income);
                if (c.Occupation == Occupation.Pupil || c.Occupation == Occupation.Toddler) kids[c.Household] = true;
                if (c.AgeOn(date) >= 18)
                {
                    hhMoney[c.Household] += Money(c);
                    hhAdults[c.Household]++;
                }
            }

            double total = 0;
            var cov = new double[(int)ServiceKind.Count];
            var sums = new double[n];
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Household < 0) continue;
                var hh = pop.Households[c.Household];
                int dwelling = hh.Dwelling;

                f[(int)HappinessFactor.Needs] = c.Mood;
                f[(int)HappinessFactor.Home] = Home(pop, hh, hhIncome[c.Household]);
                f[(int)HappinessFactor.Work] = Work(b, c);
                f[(int)HappinessFactor.Money] = hhAdults[c.Household] > 0 ? hhMoney[c.Household] / hhAdults[c.Household] : Money(c);
                double sv = 0, sw = 0;
                for (int k = 0; k < (int)ServiceKind.Count; k++)
                {
                    double w = b.ServiceWeights[k];
                    // la scuola conta per le famiglie con bambini e ragazzi (e per chi studia)
                    if ((ServiceKind)k == ServiceKind.Education && !kids[c.Household] && c.Occupation != Occupation.UniversityStudent) w = 0;
                    if (w <= 0) continue;
                    sv += w * services.At(dwelling, (ServiceKind)k);
                    sw += w;
                }
                f[(int)HappinessFactor.Services] = sw > 0 ? sv / sw : 50;
                double fv = 0, fw = 0;
                for (int k = 0; k < (int)ServiceKind.Count; k++)
                {
                    double w = b.SafetyWeights[k];
                    if (w <= 0) continue;
                    fv += w * services.At(dwelling, (ServiceKind)k);
                    fw += w;
                }
                f[(int)HappinessFactor.Safety] = fw > 0 ? fv / fw : 50;
                f[(int)HappinessFactor.Commute] = c.Commute == 0 ? 85 : CityServices.Linear(c.Commute, 15, 75) * 0.7 + 30;
                f[(int)HappinessFactor.Taxes] = taxScore;

                double target = 0;
                for (int k = 0; k < n; k++)
                {
                    target += b.Weights[k] * f[k];
                    sums[k] += f[k];
                }
                target /= wsum;
                // la preoccupazione: il fattore peggiore (tranne l'umore del giorno), che pesa di più se è grave
                int worst = (int)HappinessFactor.Home;
                for (int k = (int)HappinessFactor.Home; k < n; k++)
                    if (f[k] < f[worst]) worst = k;
                if (f[worst] < b.WorstBelow)
                {
                    target -= (b.WorstBelow - f[worst]) * b.WorstPenalty;
                    c.Worry = (byte)worst;
                }
                else c.Worry = (byte)HappinessFactor.Count;
                // le feste degli ultimi giorni
                target += pop.EventBonus;
                if (target < 1) target = 1;
                double now = first || c.Happiness == 0 ? target : c.Happiness + (target - c.Happiness) * b.DailyChange;
                c.Happiness = (byte)Math.Max(1, Math.Min(100, Math.Round(now)));
                total += c.Happiness;
                if (c.Happiness < b.UnhappyBelow) rep.Unhappy++;
                for (int k = 0; k < cov.Length; k++) cov[k] += services.At(dwelling, (ServiceKind)k);
                rep.Counted++;
            }
            if (rep.Counted > 0)
            {
                rep.Average = (float)(total / rep.Counted);
                for (int k = 0; k < n; k++) rep.Factors[k] = (float)(sums[k] / rep.Counted);
                for (int k = 0; k < cov.Length; k++) rep.Coverage[k] = (float)(cov[k] / rep.Counted);
            }
            pop.AverageHappiness = rep.Average;
            pop.UnhappyCount = rep.Unhappy;
            Array.Copy(rep.Factors, pop.HappinessFactors, n);
            return rep;
        }

        private static double Clamp(double v) => Math.Max(0, Math.Min(100, v));

        /// <summary>Casa: spazio per persona e peso dell'affitto sul reddito della famiglia.</summary>
        private static double Home(CityPopulation pop, Household hh, long income)
        {
            if (hh.Dwelling < 0) return 20;
            var dw = pop.Dwellings[hh.Dwelling];
            double perUnit = Math.Max(1.0, dw.Capacity / (double)Math.Max(1, dw.Units));
            double crowd = hh.Members.Count / perUnit;
            double space = crowd <= 1 ? 100 : Math.Max(40, 100 - (crowd - 1) * 60);
            double rent = 100;
            if (hh.Rent > 0)
            {
                double burden = income > 0 ? hh.Rent * 12.0 / income : 1;
                rent = burden <= 0.25 ? 100 : Math.Max(30, 100 - (burden - 0.25) * 200);
            }
            return (space + rent) / 2;
        }

        private static double Work(ServicesBalance b, in Citizen c)
        {
            switch (c.Occupation)
            {
                case Occupation.Employee:
                case Occupation.SelfEmployed:
                case Occupation.Entrepreneur:
                    return c.Work != Citizen.None ? b.Employed : b.Unemployed;
                case Occupation.Unemployed: return b.Unemployed;
                case Occupation.Retired: return b.Retired;
                case Occupation.Homemaker: return b.Homemaker;
                default: return b.Student;
            }
        }

        /// <summary>Soldi: mesi di spese coperti dai risparmi (i debiti pesano).</summary>
        private static double Money(in Citizen c)
        {
            double monthly = Math.Max(8000, c.Income) / 12.0;
            double months = c.Savings / monthly;
            if (months >= 6) return 95;
            if (months >= 0) return 50 + months / 6 * 45;
            return Math.Max(15, 50 + months / 3 * 35);
        }
    }
}
