using System;
using System.Collections.Generic;
using Orbis.Core.Data;

namespace Orbis.Core.Population
{
    /// <summary>Fascia di reddito complessivo (MEF): estremi in euro (null = aperta), contribuenti e totale.</summary>
    public struct IncomeClass
    {
        public int? From, To;
        public int Count;
        public long Amount;

        public double Average => Count > 0 ? (double)Amount / Count : 0;
    }

    /// <summary>Posizione in famiglia per classe d'età (ISTAT, previsioni delle famiglie), nell'ordine di <see cref="PopulationData.PositionKeys"/>.</summary>
    public struct PositionRow
    {
        public int From, To;
        public int[] Counts;
    }

    /// <summary>
    /// Dati reali di un comune (data/population/&lt;regione&gt;.json, scritti da tools/population):
    /// abitanti per età e sesso, famiglie, mortalità, redditi.
    /// </summary>
    public sealed class PopulationData
    {
        public const int MaxAge = 100;

        public string Region;
        public string MunicipalityCode;
        public string MunicipalityName;
        public DateTime Date;
        public int Total;
        public int[] Male = new int[MaxAge + 1];
        public int[] Female = new int[MaxAge + 1];

        public int Births, Deaths, MovedIn, MovedOut, Immigrated, Emigrated;

        public double AverageHouseholdSize;
        public string[] PositionKeys = Array.Empty<string>();
        public readonly List<PositionRow> Positions = new List<PositionRow>();
        public readonly Dictionary<string, int> HouseholdTypes = new Dictionary<string, int>();

        public double[] MortalityMale = Array.Empty<double>();
        public double[] MortalityFemale = Array.Empty<double>();

        public int Taxpayers;
        public int IncomeCount;
        public long IncomeAmount;
        public IncomeClass[] IncomeClasses = Array.Empty<IncomeClass>();
        public readonly Dictionary<string, (int Count, long Amount)> IncomeKinds = new Dictionary<string, (int, long)>();

        public readonly List<(string Code, string Name, int Population)> Municipalities = new List<(string, string, int)>();

        public int Position(string key) => Array.IndexOf(PositionKeys, key);

        public int IncomeKindCount(string key) => IncomeKinds.TryGetValue(key, out var k) ? k.Count : 0;

        public static PopulationData Parse(string json)
        {
            var root = Json.Parse(json);
            if (root["format"].AsInt() != 1) throw new FormatException("data/population: formato non supportato");
            var d = new PopulationData
            {
                Region = root["region"].AsString(""),
                MunicipalityCode = root["municipality"]["istat"].AsString(""),
                MunicipalityName = root["municipality"]["name"].AsString(""),
            };
            DateTime.TryParse(root["population_date"].AsString("2026-01-01"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out d.Date);
            var pop = root["population"];
            d.Total = pop["total"].AsInt();
            for (int a = 0; a <= MaxAge; a++)
            {
                d.Male[a] = pop["male"][a].AsInt();
                d.Female[a] = pop["female"][a].AsInt();
            }

            var b = root["balance"];
            d.Births = b["births"].AsInt();
            d.Deaths = b["deaths"].AsInt();
            d.MovedIn = b["moved_in"].AsInt();
            d.MovedOut = b["moved_out"].AsInt();
            d.Immigrated = b["immigrated"].AsInt();
            d.Emigrated = b["emigrated"].AsInt();

            var h = root["households"];
            d.AverageHouseholdSize = h["size"]["average"].AsDouble(2.2);
            var keys = new List<string>();
            foreach (var k in h["position_keys"].Items) keys.Add(k.AsString(""));
            d.PositionKeys = keys.ToArray();
            foreach (var row in h["positions_by_age"].Items)
            {
                var counts = new int[keys.Count];
                for (int i = 0; i < counts.Length; i++) counts[i] = row["counts"][i].AsInt();
                d.Positions.Add(new PositionRow { From = row["from"].AsInt(), To = row["to"].AsInt(), Counts = counts });
            }
            foreach (var k in h["types"].Keys) d.HouseholdTypes[k] = h["types"][k].AsInt();

            d.MortalityMale = Doubles(root["mortality"]["male"]);
            d.MortalityFemale = Doubles(root["mortality"]["female"]);

            var inc = root["income"];
            d.Taxpayers = inc["taxpayers"].AsInt();
            d.IncomeCount = inc["total"]["count"].AsInt();
            d.IncomeAmount = inc["total"]["amount"].AsLong();
            var classes = new List<IncomeClass>();
            foreach (var c in inc["classes"].Items)
                classes.Add(new IncomeClass
                {
                    From = c["from"].IsNull ? (int?)null : c["from"].AsInt(),
                    To = c["to"].IsNull ? (int?)null : c["to"].AsInt(),
                    Count = c["count"].AsInt(),
                    Amount = c["amount"].AsLong(),
                });
            d.IncomeClasses = classes.ToArray();
            foreach (var k in inc["kinds"].Keys) d.IncomeKinds[k] = (inc["kinds"][k]["count"].AsInt(), inc["kinds"][k]["amount"].AsLong());

            foreach (var m in root["province"]["municipalities"].Items)
                d.Municipalities.Add((m["istat"].AsString(""), m["name"].AsString(""), m["population"].AsInt()));
            return d;
        }

        private static double[] Doubles(JsonValue v)
        {
            var r = new double[v.Count];
            for (int i = 0; i < r.Length; i++) r[i] = v[i].AsDouble();
            return r;
        }
    }

    /// <summary>Nomi frequenti per periodo di nascita e cognomi (data/population/names-it.json).</summary>
    public sealed class NameData
    {
        public struct Period
        {
            public int From, To;
            public string[] Male, Female;
        }

        public readonly List<Period> Periods = new List<Period>();
        public string[] Surnames = Array.Empty<string>();

        public static NameData Parse(string json)
        {
            var root = Json.Parse(json);
            var n = new NameData();
            foreach (var p in root["first_names"].Items)
                n.Periods.Add(new Period { From = p["from"].AsInt(), To = p["to"].AsInt(), Male = Strings(p["male"]), Female = Strings(p["female"]) });
            n.Surnames = Strings(root["surnames"]);
            if (n.Periods.Count == 0 || n.Surnames.Length == 0) throw new FormatException("names: elenco vuoto");
            return n;
        }

        /// <summary>Nomi del periodo che contiene l'anno (il più vicino se fuori da tutti).</summary>
        public string[] FirstNames(Sex sex, int birthYear)
        {
            var best = Periods[0];
            foreach (var p in Periods)
            {
                if (birthYear >= p.From && birthYear <= p.To)
                {
                    best = p;
                    break;
                }
                if (birthYear > p.To) best = p;
            }
            return sex == Sex.Male ? best.Male : best.Female;
        }

        private static string[] Strings(JsonValue v)
        {
            var r = new string[v.Count];
            for (int i = 0; i < r.Length; i++) r[i] = v[i].AsString("");
            return r;
        }
    }

    /// <summary>Parametri della giornata dei cittadini (sezione "routine" di data/balance/population.json).</summary>
    public sealed class RoutineBalance
    {
        public int StepMinutes = 15;
        public double HungerPerHour = -7, EnergyPerHour = -5, HygienePerHour = -3, FunPerHour = -3, SocialPerHour = -2.5, ComfortPerHour = -2;
        public double SleepEnergyPerHour = 11, HomeComfortPerHour = 12, HomeFunPerHour = 2, FamilySocialPerHour = 3;
        public double LeisureFunPerHour = 25, LeisureSocialPerHour = 20, MealHunger = 40, WakeHygiene = 60;
        public int SleepStart = 23, SleepEnd = 7, WorkStart = 8, WorkEnd = 17, ShopWorkStart = 9, ShopWorkEnd = 19;
        public int SchoolStart = 8, SchoolEnd = 13, KindergartenEnd = 16, UniversityStart = 9, UniversityEnd = 17;
        public int StoresOpen = 8, StoresClose = 20;
        public double WalkKmh = 4.5, DriveKmh = 28, WalkMaxM = 1200;
        public int OutsideTravelMinutes = 25;
        public int FridgePortionsPerPerson = 14;
        public double ShopWhenBelow = 0.3;
        public double[] PortionPrice = { 2.8, 4.0, 6.0 };
        public int StoreStockPerJob = 400;
        public readonly HashSet<string> FoodCategories = new HashSet<string>();
        public readonly HashSet<string> LeisureCategories = new HashSet<string>();

        public static RoutineBalance Parse(JsonValue r)
        {
            var b = new RoutineBalance();
            if (r.IsNull) return b;
            b.StepMinutes = Math.Max(5, r["stepMinutes"].AsInt(b.StepMinutes));
            var n = r["needsPerHour"];
            b.HungerPerHour = n["hunger"].AsDouble(b.HungerPerHour);
            b.EnergyPerHour = n["energy"].AsDouble(b.EnergyPerHour);
            b.HygienePerHour = n["hygiene"].AsDouble(b.HygienePerHour);
            b.FunPerHour = n["fun"].AsDouble(b.FunPerHour);
            b.SocialPerHour = n["social"].AsDouble(b.SocialPerHour);
            b.ComfortPerHour = n["comfort"].AsDouble(b.ComfortPerHour);
            b.SleepEnergyPerHour = r["sleepEnergyPerHour"].AsDouble(b.SleepEnergyPerHour);
            b.HomeComfortPerHour = r["homeComfortPerHour"].AsDouble(b.HomeComfortPerHour);
            b.HomeFunPerHour = r["homeFunPerHour"].AsDouble(b.HomeFunPerHour);
            b.FamilySocialPerHour = r["familySocialPerHour"].AsDouble(b.FamilySocialPerHour);
            b.LeisureFunPerHour = r["leisureFunPerHour"].AsDouble(b.LeisureFunPerHour);
            b.LeisureSocialPerHour = r["leisureSocialPerHour"].AsDouble(b.LeisureSocialPerHour);
            b.MealHunger = r["mealHunger"].AsDouble(b.MealHunger);
            b.WakeHygiene = r["wakeHygiene"].AsDouble(b.WakeHygiene);
            b.SleepStart = r["sleepStart"].AsInt(b.SleepStart);
            b.SleepEnd = r["sleepEnd"].AsInt(b.SleepEnd);
            b.WorkStart = r["workStart"].AsInt(b.WorkStart);
            b.WorkEnd = r["workEnd"].AsInt(b.WorkEnd);
            b.ShopWorkStart = r["shopWorkStart"].AsInt(b.ShopWorkStart);
            b.ShopWorkEnd = r["shopWorkEnd"].AsInt(b.ShopWorkEnd);
            b.SchoolStart = r["schoolStart"].AsInt(b.SchoolStart);
            b.SchoolEnd = r["schoolEnd"].AsInt(b.SchoolEnd);
            b.KindergartenEnd = r["kindergartenEnd"].AsInt(b.KindergartenEnd);
            b.UniversityStart = r["universityStart"].AsInt(b.UniversityStart);
            b.UniversityEnd = r["universityEnd"].AsInt(b.UniversityEnd);
            b.StoresOpen = r["storesOpen"].AsInt(b.StoresOpen);
            b.StoresClose = r["storesClose"].AsInt(b.StoresClose);
            b.WalkKmh = r["walkKmh"].AsDouble(b.WalkKmh);
            b.DriveKmh = r["driveKmh"].AsDouble(b.DriveKmh);
            b.WalkMaxM = r["walkMaxM"].AsDouble(b.WalkMaxM);
            b.OutsideTravelMinutes = r["outsideTravelMinutes"].AsInt(b.OutsideTravelMinutes);
            b.FridgePortionsPerPerson = r["fridgePortionsPerPerson"].AsInt(b.FridgePortionsPerPerson);
            b.ShopWhenBelow = r["shopWhenBelow"].AsDouble(b.ShopWhenBelow);
            if (r["portionPrice"].Count == 3)
                for (int i = 0; i < 3; i++) b.PortionPrice[i] = r["portionPrice"][i].AsDouble(b.PortionPrice[i]);
            b.StoreStockPerJob = r["storeStockPerJob"].AsInt(b.StoreStockPerJob);
            foreach (var c in r["foodCategories"].Items) b.FoodCategories.Add(c.AsString(""));
            foreach (var c in r["leisureCategories"].Items) b.LeisureCategories.Add(c.AsString(""));
            return b;
        }
    }

    /// <summary>Parametri del generatore (data/balance/population.json): solo le stime che le fonti non danno.</summary>
    public sealed class PopulationBalance
    {
        public readonly Dictionary<string, double> ResidentialWeight = new Dictionary<string, double>();
        public double MinFootprintM2 = 30, UnknownMaxFootprintM2 = 1500, FloorHeightM = 3, GrossM2PerPerson = 45, SpareCapacity = 1.15;
        public double UnitM2 = 85, OwnerShare = 0.72, RentPerM2Month = 6;
        public int MaxLevels = 30;

        public readonly Dictionary<string, double> JobsPerM2 = new Dictionary<string, double>();
        public readonly Dictionary<string, int> PoiJobs = new Dictionary<string, int>();
        public readonly Dictionary<string, int> MinEducation = new Dictionary<string, int>();
        public int MaxJobsPerPlace = 1500;
        public double LocalWorkShare = 0.7, JobsPerLocalWorker = 1.45;
        public int WorkFromAge = 19, RetirementAge = 67, EarlyPensionFromAge = 58;
        public readonly List<(int From, int To, double Weight)> EmploymentByAge = new List<(int, int, double)>();
        public double HomemakerShareFemale = 0.55;

        public readonly List<(int To, double[] Shares)> EducationByBirthYear = new List<(int, double[])>();

        public int KindergartenFrom = 3, SchoolFrom = 6, SchoolTo = 18, UniversityTo = 24;
        public double UniversityShare = 0.45, StudentsPerM2 = 0.1, KindergartenChildrenPerM2 = 0.08, UniversityStudentsPerM2 = 0.05;
        public readonly Dictionary<string, int> PoiStudents = new Dictionary<string, int>();

        public double ParentGapMean = 31, ParentGapSd = 5;
        public int ParentGapMin = 17, ParentGapMax = 50, MaxChildren = 6;
        public double OtherHouseholdSize = 2.4;

        public double SavingsMonthsMean = 8, SavingsSpread = 0.9;

        // vita giornaliera (sezione "life")
        public double NetIncomeShare = 0.78, SpendingShare = 0.85, SubsistencePerPersonDay = 9, UtilitiesPerPersonMonth = 55;
        public double LocalSpendingShare = 0.6, DebtLimitPerAdult = 3000, CityWelfareShare = 0.25, CashBufferMonths = 3;
        public readonly HashSet<string> PublicCategories = new HashSet<string>();
        public readonly HashSet<string> ConsumerCategories = new HashSet<string>();
        public readonly HashSet<string> IndustrialCategories = new HashSet<string>();
        public double ProductivityPerJobDay = 175, ConsumerExternalPerJobDay = 40, OperatingCostShare = 0.35, InitialCashPerJob = 4000;
        public double BadCashMonthsOfWages = 2;
        public int CloseAfterBadMonths = 4;
        public double JobLossPerYear = 0.07;
        public double JobSearchDays = 75, PensionReplacement = 0.7, FertilityPeakAge = 32, FertilitySd = 5.5;
        public double ArrivalAgeMean = 33, ArrivalAgeSd = 9, ArrivalJobChance = 0.6, ArrivalSavingsMonths = 4;
        public int DepartureMinAge = 18;

        public RoutineBalance Routine = new RoutineBalance();

        public int TraitsMin = 3, TraitsMax = 5;
        public string[] TraitKeys = Array.Empty<string>();
        public readonly List<(int A, int B)> OppositeTraits = new List<(int, int)>();

        public static PopulationBalance Parse(string json)
        {
            var r = Json.Parse(json);
            var b = new PopulationBalance();
            var h = r["housing"];
            foreach (var k in h["residentialWeight"].Keys) b.ResidentialWeight[k] = h["residentialWeight"][k].AsDouble();
            b.MinFootprintM2 = h["minFootprintM2"].AsDouble(b.MinFootprintM2);
            b.UnknownMaxFootprintM2 = h["unknownMaxFootprintM2"].AsDouble(b.UnknownMaxFootprintM2);
            b.FloorHeightM = h["floorHeightM"].AsDouble(b.FloorHeightM);
            b.MaxLevels = h["maxLevels"].AsInt(b.MaxLevels);
            b.GrossM2PerPerson = h["grossM2PerPerson"].AsDouble(b.GrossM2PerPerson);
            b.SpareCapacity = h["spareCapacity"].AsDouble(b.SpareCapacity);
            b.UnitM2 = h["unitM2"].AsDouble(b.UnitM2);
            b.OwnerShare = h["ownerShare"].AsDouble(b.OwnerShare);
            b.RentPerM2Month = h["rentPerM2Month"].AsDouble(b.RentPerM2Month);

            var w = r["work"];
            foreach (var k in w["jobsPerM2"].Keys) b.JobsPerM2[k] = w["jobsPerM2"][k].AsDouble();
            foreach (var k in w["poiJobs"].Keys)
                if (!k.StartsWith("_", StringComparison.Ordinal)) b.PoiJobs[k] = w["poiJobs"][k].AsInt();
            foreach (var k in w["minEducation"].Keys) b.MinEducation[k] = w["minEducation"][k].AsInt();
            b.MaxJobsPerPlace = w["maxJobsPerPlace"].AsInt(b.MaxJobsPerPlace);
            b.LocalWorkShare = w["localShare"].AsDouble(b.LocalWorkShare);
            b.JobsPerLocalWorker = w["jobsPerLocalWorker"].AsDouble(b.JobsPerLocalWorker);
            b.WorkFromAge = w["workFromAge"].AsInt(b.WorkFromAge);
            b.RetirementAge = w["retirementAge"].AsInt(b.RetirementAge);
            b.EarlyPensionFromAge = w["earlyPensionFromAge"].AsInt(b.EarlyPensionFromAge);
            foreach (var e in w["employmentByAge"].Items) b.EmploymentByAge.Add((e["from"].AsInt(), e["to"].AsInt(), e["weight"].AsDouble()));
            b.HomemakerShareFemale = w["homemakerShareFemale"].AsDouble(b.HomemakerShareFemale);

            foreach (var e in r["education"]["byBirthYear"].Items)
            {
                var s = new double[e["shares"].Count];
                for (int i = 0; i < s.Length; i++) s[i] = e["shares"][i].AsDouble();
                b.EducationByBirthYear.Add((e["to"].AsInt(), s));
            }

            var sc = r["school"];
            b.KindergartenFrom = sc["kindergartenFrom"].AsInt(b.KindergartenFrom);
            b.SchoolFrom = sc["schoolFrom"].AsInt(b.SchoolFrom);
            b.SchoolTo = sc["schoolTo"].AsInt(b.SchoolTo);
            b.UniversityTo = sc["universityTo"].AsInt(b.UniversityTo);
            b.UniversityShare = sc["universityShare"].AsDouble(b.UniversityShare);
            b.StudentsPerM2 = sc["studentsPerM2"].AsDouble(b.StudentsPerM2);
            b.KindergartenChildrenPerM2 = sc["kindergartenChildrenPerM2"].AsDouble(b.KindergartenChildrenPerM2);
            b.UniversityStudentsPerM2 = sc["universityStudentsPerM2"].AsDouble(b.UniversityStudentsPerM2);
            foreach (var k in sc["poiStudents"].Keys) b.PoiStudents[k] = sc["poiStudents"][k].AsInt();

            var f = r["family"];
            b.ParentGapMean = f["parentAgeGapMean"].AsDouble(b.ParentGapMean);
            b.ParentGapSd = f["parentAgeGapSd"].AsDouble(b.ParentGapSd);
            b.ParentGapMin = f["parentAgeGapMin"].AsInt(b.ParentGapMin);
            b.ParentGapMax = f["parentAgeGapMax"].AsInt(b.ParentGapMax);
            b.MaxChildren = f["maxChildren"].AsInt(b.MaxChildren);
            b.OtherHouseholdSize = f["otherHouseholdSize"].AsDouble(b.OtherHouseholdSize);

            b.SavingsMonthsMean = r["money"]["savingsMonthsMean"].AsDouble(b.SavingsMonthsMean);
            b.SavingsSpread = r["money"]["savingsSpread"].AsDouble(b.SavingsSpread);

            var life = r["life"];
            b.NetIncomeShare = life["netIncomeShare"].AsDouble(b.NetIncomeShare);
            b.SpendingShare = life["spendingShare"].AsDouble(b.SpendingShare);
            b.SubsistencePerPersonDay = life["subsistencePerPersonDay"].AsDouble(b.SubsistencePerPersonDay);
            b.UtilitiesPerPersonMonth = life["utilitiesPerPersonMonth"].AsDouble(b.UtilitiesPerPersonMonth);
            b.LocalSpendingShare = life["localSpendingShare"].AsDouble(b.LocalSpendingShare);
            b.DebtLimitPerAdult = life["debtLimitPerAdult"].AsDouble(b.DebtLimitPerAdult);
            b.CityWelfareShare = life["cityWelfareShare"].AsDouble(b.CityWelfareShare);
            b.CashBufferMonths = life["cashBufferMonths"].AsDouble(b.CashBufferMonths);
            foreach (var c in life["publicCategories"].Items) b.PublicCategories.Add(c.AsString(""));
            foreach (var c in life["consumerCategories"].Items) b.ConsumerCategories.Add(c.AsString(""));
            foreach (var c in life["industrialCategories"].Items) b.IndustrialCategories.Add(c.AsString(""));
            b.ProductivityPerJobDay = life["productivityPerJobDay"].AsDouble(b.ProductivityPerJobDay);
            b.ConsumerExternalPerJobDay = life["consumerExternalPerJobDay"].AsDouble(b.ConsumerExternalPerJobDay);
            b.OperatingCostShare = life["operatingCostShare"].AsDouble(b.OperatingCostShare);
            b.InitialCashPerJob = life["initialCashPerJob"].AsDouble(b.InitialCashPerJob);
            b.BadCashMonthsOfWages = life["badCashMonthsOfWages"].AsDouble(b.BadCashMonthsOfWages);
            b.CloseAfterBadMonths = life["closeAfterBadMonths"].AsInt(b.CloseAfterBadMonths);
            b.JobSearchDays = life["jobSearchDays"].AsDouble(b.JobSearchDays);
            b.JobLossPerYear = life["jobLossPerYear"].AsDouble(b.JobLossPerYear);
            b.PensionReplacement = life["pensionReplacement"].AsDouble(b.PensionReplacement);
            b.FertilityPeakAge = life["fertilityPeakAge"].AsDouble(b.FertilityPeakAge);
            b.FertilitySd = life["fertilitySd"].AsDouble(b.FertilitySd);
            b.ArrivalAgeMean = life["arrivalAgeMean"].AsDouble(b.ArrivalAgeMean);
            b.ArrivalAgeSd = life["arrivalAgeSd"].AsDouble(b.ArrivalAgeSd);
            b.ArrivalJobChance = life["arrivalJobChance"].AsDouble(b.ArrivalJobChance);
            b.ArrivalSavingsMonths = life["arrivalSavingsMonths"].AsDouble(b.ArrivalSavingsMonths);
            b.DepartureMinAge = life["departureMinAge"].AsInt(b.DepartureMinAge);

            b.Routine = RoutineBalance.Parse(r["routine"]);

            var t = r["traits"];
            b.TraitsMin = t["perCitizenMin"].AsInt(b.TraitsMin);
            b.TraitsMax = t["perCitizenMax"].AsInt(b.TraitsMax);
            var keys = new List<string>();
            foreach (var k in t["keys"].Items) keys.Add(k.AsString(""));
            if (keys.Count > 64) throw new FormatException("al massimo 64 tratti");
            b.TraitKeys = keys.ToArray();
            foreach (var pair in t["opposites"].Items)
            {
                int a = keys.IndexOf(pair[0].AsString("")), c = keys.IndexOf(pair[1].AsString(""));
                if (a >= 0 && c >= 0) b.OppositeTraits.Add((a, c));
            }
            return b;
        }

        public double[] EducationShares(int birthYear)
        {
            foreach (var e in EducationByBirthYear)
                if (birthYear <= e.To) return e.Shares;
            return EducationByBirthYear.Count > 0 ? EducationByBirthYear[EducationByBirthYear.Count - 1].Shares : new[] { 0, 0, 0.3, 0.4, 0.3 };
        }

        /// <summary>La categoria (o il suo prefisso prima di "/") è nell'insieme.</summary>
        public static bool InSet(HashSet<string> set, string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            if (set.Contains(category)) return true;
            int slash = category.IndexOf('/');
            return slash > 0 && set.Contains(category.Substring(0, slash));
        }

        public double EmploymentWeight(int age)
        {
            foreach (var e in EmploymentByAge)
                if (age >= e.From && age <= e.To) return e.Weight;
            return 0;
        }
    }
}
