using System;
using System.Collections.Generic;

namespace Orbis.Core.Population
{
    /// <summary>Movimenti di cassa del comune prodotti dalla vita dei cittadini.</summary>
    public enum CityFlow { ResidentialTax, CommercialTax, IndustrialTax, Welfare }

    /// <summary>Aliquote del sindaco in percentuale (docs/03 §4).</summary>
    public struct TaxRates
    {
        public float Residential, Commercial, Industrial;
    }

    /// <summary>Riepilogo di una giornata di vita della città.</summary>
    public sealed class LifeDay
    {
        public DateTime Date;
        public long Wages, Spending, LocalSpending;
        public long ResidentialTax, CommercialTax, IndustrialTax, Welfare;
        public int Births, Deaths, Arrived, Left, Hired, Closed;
        public bool Skipped;
    }

    /// <summary>Dati fissi che servono alla vita della città (gli stessi del generatore).</summary>
    public sealed class LifeInputs
    {
        public PopulationData Data;
        public NameData Names;
        public PopulationBalance Balance;
    }

    /// <summary>
    /// Una giornata di vita per tutti i cittadini (livello statistico L2, docs/01 §6, docs/03 §3–§5):
    /// stipendi e pensioni, tasse, spese della famiglia (cibo, affitto, bollette), debiti e aiuti sociali,
    /// conti delle attività (incassi, costi, fallimenti), ricerca di lavoro, nascite, morti, compleanni,
    /// arrivi e partenze. Deterministica (seed + giorno) e con il denaro che si conserva: ciò che entra o
    /// esce dalla città è contato in <see cref="CityPopulation.ExternalIn"/> e <see cref="CityPopulation.ExternalOut"/>.
    /// </summary>
    public sealed class CityLife
    {
        private readonly LifeInputs _in;
        private DateTime _today;
        private long _avgWage = 22000;
        private PopulationBalance B => _in.Balance;

        public CityLife(LifeInputs inputs) => _in = inputs;

        public static int DayIndex(DateTime d) => (int)(d.Date - Citizen.Epoch.Date).TotalDays;

        /// <summary>Quota di un importo annuo che tocca al giorno: la somma su 365 giorni dà l'importo esatto.</summary>
        public static long DailyShare(long perYear, int day) => perYear * (day + 1) / 365 - perYear * day / 365;

        /// <param name="city">movimento di cassa del comune: positivo = entrata, negativo = uscita</param>
        public LifeDay Day(CityPopulation pop, DateTime date, TaxRates taxes, bool lifeCycle, Action<CityFlow, long> city)
        {
            int day = DayIndex(date);
            var r = new LifeDay { Date = date.Date };
            if (day <= pop.LastDay)
            {
                r.Skipped = true;
                return r;
            }
            pop.LastDay = day;
            _today = date.Date;
            var rng = new Rng(((ulong)(uint)pop.Seed << 32) ^ (uint)day ^ 0xC17E11FEUL);
            var places = pop.Workplaces;

            // occupati per posto di lavoro e stipendi del giorno
            var employees = new int[places.Count];
            var wages = new long[places.Count];
            var netIncome = new long[pop.Households.Count];
            for (int i = 0; i < pop.Count; i++)
                if (pop.Citizens[i].Alive && pop.Citizens[i].Works && pop.Citizens[i].Work >= 0) employees[pop.Citizens[i].Work]++;
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Income <= 0) continue;
                long gross = DailyShare(c.Income, day);
                if (gross <= 0) continue;
                bool business = c.Works && c.Work >= 0 && !places[c.Work].Public && !places[c.Work].Closed;
                if (business)
                {
                    places[c.Work].Cash -= gross;
                    wages[c.Work] += gross;
                }
                else pop.ExternalIn += gross;
                long cityTax = (long)Math.Round(gross * taxes.Residential / 100.0);
                long stateTax = Math.Max(0, (long)Math.Round(gross * (1 - B.NetIncomeShare)) - cityTax);
                long net = gross - cityTax - stateTax;
                c.Savings = Clamp(c.Savings + net);
                pop.ExternalOut += stateTax;
                r.ResidentialTax += cityTax;
                r.Wages += gross;
                if (c.Household >= 0) netIncome[c.Household] += net;
            }
            if (r.ResidentialTax != 0) city(CityFlow.ResidentialTax, r.ResidentialTax);
            long sum = 0;
            int n = 0;
            for (int i = 0; i < pop.Count; i++)
                if (pop.Citizens[i].Alive && pop.Citizens[i].Occupation == Occupation.Employee)
                {
                    sum += pop.Citizens[i].Income;
                    n++;
                }
            _avgWage = n > 0 ? sum / n : 22000;

            long localPool = HouseholdSpending(pop, day, netIncome, r, city);
            Businesses(pop, day, rng, employees, wages, localPool, taxes, r, city);
            if (date.Day == 1) MonthlyChecks(pop, employees, wages, r);
            JobMarket(pop, rng, employees, r);
            if (lifeCycle)
            {
                Deaths(pop, date, rng, r);
                BirthsToday(pop, date, day, rng, r);
                Birthdays(pop, date, rng);
                Departures(pop, rng, r);
                Arrivals(pop, date, day, rng, employees, r);
            }
            return r;
        }

        /// <summary>Cifre della popolazione per l'economia del comune e per la previsione del pannello Finanze.</summary>
        public struct Summary
        {
            public int Alive, CommercialJobs, IndustrialJobs;
            public long Incomes;
            public double ResidentialTaxDay, CommercialTaxDay, IndustrialTaxDay, WelfareDay;
        }

        public Summary Summarize(CityPopulation pop, TaxRates taxes)
        {
            var s = new Summary();
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive) continue;
                s.Alive++;
                s.Incomes += c.Income;
                if (!c.Works || c.Work < 0) continue;
                if (PopulationBalance.InSet(B.IndustrialCategories, pop.Workplaces[c.Work].Category)) s.IndustrialJobs++;
                else s.CommercialJobs++;
            }
            s.ResidentialTaxDay = s.Incomes / 365.0 * taxes.Residential / 100.0;
            // prima del primo giorno simulato gli incassi si stimano: posti × resa + quota della spesa dei cittadini
            double consumerJobs = 0;
            foreach (var p in pop.Workplaces)
                if (IsConsumer(p)) consumerJobs += p.Capacity;
            double localSpending = s.Incomes / 365.0 * B.NetIncomeShare * B.SpendingShare * 0.85 * B.LocalSpendingShare;
            foreach (var p in pop.Workplaces)
            {
                if (p.Public || p.Closed) continue;
                bool consumer = PopulationBalance.InSet(B.ConsumerCategories, p.Category);
                double revenueDay = p.RevenueDay > 0 ? p.RevenueDay
                    : p.Capacity * (consumer ? B.ConsumerExternalPerJobDay : B.ProductivityPerJobDay)
                      + (consumer && consumerJobs > 0 ? localSpending * p.Capacity / consumerJobs : 0);
                if (PopulationBalance.InSet(B.IndustrialCategories, p.Category)) s.IndustrialTaxDay += revenueDay * taxes.Industrial / 100.0;
                else s.CommercialTaxDay += revenueDay * taxes.Commercial / 100.0;
            }
            s.WelfareDay = pop.LastWelfare;
            return s;
        }

        private static int Clamp(long v) => (int)Math.Max(-1_000_000_000, Math.Min(1_000_000_000, v));

        // ------------------------------------------------------------------ famiglie

        /// <summary>Spese di ogni famiglia; restituisce la parte spesa nelle attività della città.</summary>
        private long HouseholdSpending(CityPopulation pop, int day, long[] netIncome, LifeDay r, Action<CityFlow, long> city)
        {
            long local = 0, welfareTotal = 0;
            var adults = new List<int>(4);
            for (int h = 0; h < pop.Households.Count; h++)
            {
                var hh = pop.Households[h];
                int persons = 0;
                adults.Clear();
                foreach (var m in hh.Members)
                {
                    if (!pop.Citizens[m].Alive) continue;
                    persons++;
                    if (pop.Citizens[m].AgeOn(_today) >= 18 || pop.Citizens[m].Income > 0) adults.Add(m);
                }
                if (persons == 0 || adults.Count == 0) continue;
                long subsistence = (long)Math.Round(persons * B.SubsistencePerPersonDay);
                long desired = Math.Max(subsistence, (long)Math.Round(netIncome[h] * B.SpendingShare));
                long rent = hh.Tenure == Tenure.Renter ? DailyShare(hh.Rent * 12L, day) : 0;
                long utilities = DailyShare((long)Math.Round(persons * B.UtilitiesPerPersonMonth * 12), day);
                long basics = subsistence + rent + utilities;
                long need = Math.Max(basics, desired);

                long capacity = 0;
                foreach (var a in adults) capacity += Math.Max(0, pop.Citizens[a].Savings + (long)B.DebtLimitPerAdult);
                long welfare = 0;
                if (capacity < basics)
                {
                    // aiuto sociale: copre il minimo (cibo, casa, bollette); una parte la paga il comune, il resto lo Stato
                    welfare = basics - capacity;
                    long byCity = (long)Math.Round(welfare * B.CityWelfareShare);
                    pop.ExternalIn += welfare - byCity;
                    pop.Citizens[adults[0]].Savings = Clamp(pop.Citizens[adults[0]].Savings + welfare);
                    capacity += welfare;
                    welfareTotal += byCity;
                }
                long pay = Math.Min(need, capacity);
                // la spesa alimentare fatta oggi nei negozi (giornata L1) è già pagata: qui si paga il resto,
                // ma affitto e bollette sempre
                long food = hh.FoodSpentToday;
                hh.FoodSpentToday = 0;
                // biglietti dei mezzi pubblici di oggi: le linee reali sono di aziende esterne
                long tickets = hh.TransitCents / 100;
                hh.TransitCents %= 100;
                long generic = Math.Max(Math.Min(capacity, rent + utilities), pay - food);
                // ciascun adulto paga in proporzione a quanto può
                long left = generic;
                for (int k = 0; k < adults.Count; k++)
                {
                    ref var c = ref pop.Citizens[adults[k]];
                    long room = Math.Max(0, c.Savings + (long)B.DebtLimitPerAdult);
                    long share = k == adults.Count - 1 ? left : (capacity > 0 ? generic * room / capacity : 0);
                    share = Math.Min(share, left);
                    c.Savings = Clamp(c.Savings - share);
                    left -= share;
                }
                long consumption = generic - rent - utilities;
                if (consumption < 0) consumption = 0;
                long here = (long)Math.Round(consumption * B.LocalSpendingShare);
                local += here;
                pop.ExternalOut += generic - here;
                r.Spending += generic + food;
                if (tickets > 0)
                {
                    pop.Citizens[adults[0]].Savings = Clamp(pop.Citizens[adults[0]].Savings - tickets);
                    pop.ExternalOut += tickets;
                    r.Spending += tickets;
                }
            }
            pop.LastWelfare = welfareTotal;
            if (welfareTotal > 0)
            {
                r.Welfare = welfareTotal;
                city(CityFlow.Welfare, -welfareTotal);
            }
            r.LocalSpending = local;
            return local;
        }

        // ------------------------------------------------------------------ attività

        private void Businesses(CityPopulation pop, int day, Rng rng, int[] employees, long[] wages, long localPool, TaxRates taxes,
            LifeDay r, Action<CityFlow, long> city)
        {
            var places = pop.Workplaces;
            // la spesa locale va ai negozi, bar, ristoranti… aperti, in proporzione ai posti
            long weightTotal = 0;
            for (int i = 0; i < places.Count; i++)
                if (IsConsumer(places[i])) weightTotal += places[i].Capacity;
            var local = new long[places.Count];
            if (weightTotal > 0 && localPool > 0)
            {
                long given = 0;
                int last = -1;
                for (int i = 0; i < places.Count; i++)
                {
                    if (!IsConsumer(places[i])) continue;
                    local[i] = localPool * places[i].Capacity / weightTotal;
                    given += local[i];
                    last = i;
                }
                if (last >= 0) local[last] += localPool - given;
            }
            else pop.ExternalOut += localPool; // nessun negozio: si compra fuori

            long commercial = 0, industrial = 0;
            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];
                if (p.Public || p.Closed) continue;
                // i posti non coperti dai residenti li coprono pendolari di fuori: stipendio medio, pagato fuori città
                int commuters = Math.Max(0, p.Capacity - employees[i]);
                long commuterWages = commuters * DailyShare(_avgWage, day);
                p.Cash -= commuterWages;
                pop.ExternalOut += commuterWages;
                wages[i] += commuterWages;
                bool consumer = PopulationBalance.InSet(B.ConsumerCategories, p.Category);
                double perJob = consumer ? B.ConsumerExternalPerJobDay : B.ProductivityPerJobDay;
                double noise = 0.75 + 0.5 * rng.NextDouble();
                long external = (long)Math.Round(p.Capacity * perJob * noise);
                // vendite ai cittadini durante la giornata (spesa alimentare): già in cassa, qui entrano nel fatturato
                long sales = p.SalesToday;
                p.SalesToday = 0;
                long revenue = external + local[i] + sales;
                long costs = (long)Math.Round(revenue * B.OperatingCostShare);
                bool ind = PopulationBalance.InSet(B.IndustrialCategories, p.Category);
                long tax = (long)Math.Round(revenue * (ind ? taxes.Industrial : taxes.Commercial) / 100.0);
                pop.ExternalIn += external;
                pop.ExternalOut += costs;
                p.Cash += external + local[i] - costs - tax;
                if (ind) industrial += tax;
                else commercial += tax;
                long spent = wages[i] + costs + tax;
                p.RevenueDay = p.RevenueDay == 0 ? revenue : p.RevenueDay + (revenue - p.RevenueDay) / 30;
                p.CostsDay = p.CostsDay == 0 ? spent : p.CostsDay + (spent - p.CostsDay) / 30;
            }
            r.CommercialTax = commercial;
            r.IndustrialTax = industrial;
            if (commercial != 0) city(CityFlow.CommercialTax, commercial);
            if (industrial != 0) city(CityFlow.IndustrialTax, industrial);
        }

        private bool IsConsumer(Place p) => !p.Public && !p.Closed && PopulationBalance.InSet(B.ConsumerCategories, p.Category);

        /// <summary>Il primo del mese: un'attività con la cassa troppo in rosso per troppi mesi chiude.</summary>
        private void MonthlyChecks(CityPopulation pop, int[] employees, long[] wages, LifeDay r)
        {
            var places = pop.Workplaces;
            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];
                if (p.Public || p.Closed) continue;
                long monthWages = Math.Max(wages[i] * 30, 3000);
                if (p.Cash < -B.BadCashMonthsOfWages * monthWages) p.BadMonths++;
                else p.BadMonths = 0;
                // utili oltre la scorta di cassa: vanno ai proprietari (fuori dal conto dell'attività)
                long buffer = (long)Math.Round(B.CashBufferMonths * Math.Max(p.CostsDay * 30, monthWages));
                if (p.Cash > buffer)
                {
                    pop.ExternalOut += p.Cash - buffer;
                    p.Cash = buffer;
                }
                if (p.BadMonths < B.CloseAfterBadMonths) continue;
                p.Closed = true;
                r.Closed++;
                for (int k = 0; k < pop.Count; k++)
                {
                    ref var c = ref pop.Citizens[k];
                    if (!c.Alive || c.Work != i) continue;
                    c.Work = Citizen.None;
                    c.Occupation = Occupation.Unemployed;
                    c.Income = 0;
                }
                employees[i] = 0;
            }
        }

        // ------------------------------------------------------------------ lavoro

        private void JobMarket(CityPopulation pop, Rng rng, int[] employees, LifeDay r)
        {
            var places = pop.Workplaces;
            var vacancies = new WeightedPicker(places.Count);
            for (int i = 0; i < places.Count; i++)
                if (!places[i].Closed) vacancies.Set(i, Math.Max(0, places[i].Capacity - employees[i]));
            if (vacancies.Total <= 0) return;
            long wageSum = 0;
            int wageN = 0;
            for (int i = 0; i < pop.Count; i++)
                if (pop.Citizens[i].Alive && pop.Citizens[i].Occupation == Occupation.Employee)
                {
                    wageSum += pop.Citizens[i].Income;
                    wageN++;
                }
            double avgWage = wageN > 0 ? (double)wageSum / wageN : 22000;
            double chance = 1.0 / Math.Max(1, B.JobSearchDays);
            double loss = B.JobLossPerYear / 365.0;
            // contratti che finiscono: i dipendenti tornano a cercare lavoro
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Occupation != Occupation.Employee || !rng.Chance(loss)) continue;
                if (c.Work >= 0)
                {
                    employees[c.Work]--;
                    vacancies.Set(c.Work, Math.Max(0, places[c.Work].Capacity - employees[c.Work]));
                }
                c.Work = Citizen.None;
                c.Occupation = Occupation.Unemployed;
                c.Income = 0;
            }
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Occupation != Occupation.Unemployed) continue;
                int age = c.AgeOn(_today);
                if (age < B.WorkFromAge || age >= B.RetirementAge || !rng.Chance(chance)) continue;
                if (Hire(pop, i, rng, vacancies, employees, avgWage)) r.Hired++;
                if (vacancies.Total <= 0) break;
            }
        }

        private static bool Hire(CityPopulation pop, int i, Rng rng, WeightedPicker vacancies, int[] employees, double avgWage)
        {
            int w = vacancies.Pick(rng);
            if (w < 0) return false;
            ref var c = ref pop.Citizens[i];
            c.Work = w;
            c.Occupation = Occupation.Employee;
            c.Income = (int)Math.Round(avgWage * (0.75 + 0.1 * (int)c.Education) * (0.85 + 0.3 * rng.NextDouble()) / 100.0) * 100;
            employees[w]++;
            vacancies.Set(w, Math.Max(0, pop.Workplaces[w].Capacity - employees[w]));
            return true;
        }

        // ------------------------------------------------------------------ ciclo di vita

        private void Deaths(CityPopulation pop, DateTime date, Rng rng, LifeDay r)
        {
            var d = _in.Data;
            for (int i = 0; i < pop.Count; i++)
            {
                if (!pop.Citizens[i].Alive) continue;
                var table = pop.Citizens[i].Sex == Sex.Male ? d.MortalityMale : d.MortalityFemale;
                if (table.Length == 0) return;
                int age = Math.Max(0, Math.Min(table.Length - 1, pop.Citizens[i].AgeOn(date)));
                double q = table[age];
                double pDay = 1 - Math.Pow(1 - Math.Min(0.999, q), 1 / 365.0);
                if (!rng.Chance(pDay)) continue;
                Die(pop, i);
                r.Deaths++;
            }
        }

        private void Die(CityPopulation pop, int i)
        {
            ref var c = ref pop.Citizens[i];
            c.Flags |= CitizenFlags.Dead;
            pop.Deaths++;
            long savings = c.Savings;
            c.Savings = 0;
            c.Work = Citizen.None;
            int heir = -1;
            if (c.Partner >= 0 && pop.Citizens[c.Partner].Alive) heir = c.Partner;
            if (c.Partner >= 0) pop.Citizens[c.Partner].Partner = Citizen.None;
            c.Partner = Citizen.None;
            int h = c.Household;
            if (h >= 0)
            {
                var hh = pop.Households[h];
                hh.Members.Remove(i);
                if (heir < 0)
                    foreach (var m in hh.Members)
                        if (pop.Citizens[m].Alive && (heir < 0 || pop.Citizens[m].AgeOn(_today) > pop.Citizens[heir].AgeOn(_today))) heir = m;
                RefreshHousehold(pop, h);
            }
            // eredità: al partner o a chi resta in famiglia, altrimenti esce dalla città (parenti altrove)
            if (heir >= 0) pop.Citizens[heir].Savings = Clamp(pop.Citizens[heir].Savings + savings);
            else pop.ExternalOut += savings;
        }

        /// <summary>Dopo una morte o una partenza: tipo di famiglia, ruoli, minori senza adulti, famiglia sciolta.</summary>
        private void RefreshHousehold(CityPopulation pop, int h)
        {
            var hh = pop.Households[h];
            hh.Members.RemoveAll(m => !pop.Citizens[m].Alive);
            if (hh.Members.Count == 0)
            {
                hh.Dwelling = -1;
                return;
            }
            bool adult = false;
            int partners = 0, children = 0;
            foreach (var m in hh.Members)
            {
                if (pop.Citizens[m].AgeOn(_today) >= 18) adult = true;
                if (pop.Citizens[m].Role == FamilyRole.Partner) partners++;
                if (pop.Citizens[m].Role == FamilyRole.Child) children++;
            }
            if (!adult)
            {
                // minori rimasti soli: vanno da parenti fuori città
                foreach (var m in hh.Members.ToArray()) Leave(pop, m);
                hh.Members.Clear();
                hh.Dwelling = -1;
                return;
            }
            if (partners == 1)
                foreach (var m in hh.Members)
                    if (pop.Citizens[m].Role == FamilyRole.Partner)
                        pop.Citizens[m].Role = children > 0 ? FamilyRole.Parent : (hh.Members.Count == 1 ? FamilyRole.Single : FamilyRole.Relative);
            if (hh.Type != HouseholdType.Other)
                hh.Type = partners == 2 ? (children > 0 ? HouseholdType.CoupleWithChildren : HouseholdType.Couple)
                    : children > 0 ? HouseholdType.SingleParent : hh.Members.Count == 1 ? HouseholdType.Alone : HouseholdType.Other;
        }

        private static void Leave(CityPopulation pop, int i)
        {
            ref var c = ref pop.Citizens[i];
            if (!c.Alive) return;
            c.Flags |= CitizenFlags.Gone;
            pop.ExternalOut += c.Savings;
            c.Savings = 0;
            c.Work = Citizen.None;
            if (c.Partner >= 0) pop.Citizens[c.Partner].Partner = Citizen.None;
            c.Partner = Citizen.None;
            pop.Left++;
        }

        private void BirthsToday(CityPopulation pop, DateTime date, int day, Rng rng, LifeDay r)
        {
            var d = _in.Data;
            if (d.Total <= 0) return;
            // pesi per età delle madri (in coppia, 16–49 anni)
            double sum = 0;
            var weights = new double[pop.Count];
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Sex != Sex.Female || c.Partner < 0) continue;
                int age = c.AgeOn(date);
                if (age < 16 || age > 49) continue;
                double z = (age - B.FertilityPeakAge) / B.FertilitySd;
                weights[i] = Math.Exp(-0.5 * z * z);
                sum += weights[i];
            }
            if (sum <= 0) return;
            double perDay = d.Births * (double)pop.AliveCount / d.Total / 365.0;
            int count = pop.Count;
            for (int i = 0; i < count; i++)
            {
                if (weights[i] <= 0 || !rng.Chance(perDay * weights[i] / sum)) continue;
                Birth(pop, i, date, day, rng);
                r.Births++;
            }
        }

        private void Birth(CityPopulation pop, int mother, DateTime date, int day, Rng rng)
        {
            var m = pop.Citizens[mother];
            int father = m.Partner;
            var sex = rng.Chance(0.514) ? Sex.Male : Sex.Female;
            var baby = NewCitizen(pop, sex, day, rng);
            baby.Household = m.Household;
            baby.Role = FamilyRole.Child;
            baby.Surname = father >= 0 ? pop.Citizens[father].Surname : m.Surname;
            int idx = pop.Add(baby);
            pop.Births++;
            if (m.Household >= 0)
            {
                var hh = pop.Households[m.Household];
                hh.Members.Add(idx);
                if (hh.Type == HouseholdType.Couple) hh.Type = HouseholdType.CoupleWithChildren;
                foreach (var p in hh.Members)
                    if (pop.Citizens[p].Role == FamilyRole.Single) pop.Citizens[p].Role = FamilyRole.Parent;
            }
        }

        /// <summary>Compleanni che cambiano la vita: asilo, scuola, maturità, laurea, pensione.</summary>
        private void Birthdays(CityPopulation pop, DateTime date, Rng rng)
        {
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive) continue;
                var b = c.BirthDate;
                if (b.Month != date.Month || b.Day != date.Day) continue;
                int age = date.Year - b.Year;
                if (age == B.KindergartenFrom && c.Occupation == Occupation.Toddler)
                {
                    c.Occupation = Occupation.Pupil;
                    c.School = NearestSchool(pop, i, PlaceLevel.Kindergarten);
                    if (c.School < 0) c.School = NearestSchool(pop, i, PlaceLevel.School);
                }
                else if (age == B.SchoolFrom && c.Occupation == Occupation.Pupil) c.School = NearestSchool(pop, i, PlaceLevel.School);
                else if (age == 11 && c.Education < Education.Primary) c.Education = Education.Primary;
                else if (age == 14 && c.Education < Education.Lower) c.Education = Education.Lower;
                else if (age == B.SchoolTo + 1 && c.Occupation == Occupation.Pupil)
                {
                    c.School = Citizen.None;
                    if (rng.Chance(0.8)) c.Education = Education.Upper;
                    if (c.Education >= Education.Upper && rng.Chance(B.UniversityShare))
                    {
                        c.Occupation = Occupation.UniversityStudent;
                        c.School = NearestSchool(pop, i, PlaceLevel.University);
                    }
                    else c.Occupation = Occupation.Unemployed;
                }
                else if (age == B.UniversityTo + 1 && c.Occupation == Occupation.UniversityStudent)
                {
                    c.School = Citizen.None;
                    if (rng.Chance(0.7)) c.Education = Education.Degree;
                    c.Occupation = Occupation.Unemployed;
                }
                else if (age == B.RetirementAge && c.Works)
                {
                    c.Occupation = Occupation.Retired;
                    c.Work = Citizen.None;
                    c.Income = (int)Math.Round(c.Income * B.PensionReplacement);
                }
            }
        }

        private static int NearestSchool(CityPopulation pop, int person, PlaceLevel level)
        {
            int h = pop.Citizens[person].Household;
            float x = 0, y = 0;
            if (h >= 0 && pop.Households[h].Dwelling >= 0)
            {
                var d = pop.Dwellings[pop.Households[h].Dwelling];
                x = d.X;
                y = d.Y;
            }
            int best = -1;
            double bd = double.MaxValue;
            for (int i = 0; i < pop.Schools.Count; i++)
            {
                var s = pop.Schools[i];
                if (s.Level != level) continue;
                double dx = s.X - x, dy = s.Y - y, dist = dx * dx + dy * dy;
                if (dist < bd)
                {
                    bd = dist;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Famiglie che si trasferiscono altrove (stessa quota annua del bilancio ISTAT).</summary>
        private void Departures(CityPopulation pop, Rng rng, LifeDay r)
        {
            var d = _in.Data;
            if (d.Total <= 0) return;
            double perPersonDay = (d.MovedOut + d.Emigrated) / (double)d.Total / 365.0;
            for (int h = 0; h < pop.Households.Count; h++)
            {
                var hh = pop.Households[h];
                // una famiglia parte con la quota per persona: in media partono quota × abitanti persone
                if (hh.Members.Count == 0 || !rng.Chance(perPersonDay)) continue;
                int oldest = 0;
                foreach (var m in hh.Members) oldest = Math.Max(oldest, pop.Citizens[m].AgeOn(_today));
                if (oldest < B.DepartureMinAge) continue;
                foreach (var m in hh.Members.ToArray())
                {
                    Leave(pop, m);
                    r.Left++;
                }
                hh.Members.Clear();
                hh.Dwelling = -1;
            }
        }

        /// <summary>Nuove famiglie che arrivano (quota annua ISTAT): casa libera, lavoro se c'è, risparmi portati da fuori.</summary>
        private void Arrivals(CityPopulation pop, DateTime date, int day, Rng rng, int[] employees, LifeDay r)
        {
            var d = _in.Data;
            if (d.Total <= 0 || pop.Dwellings.Count == 0) return;
            double persons = (d.MovedIn + d.Immigrated) / 365.0;
            double households = persons / 2.0;
            int n = (int)households + (rng.Chance(households - (int)households) ? 1 : 0);
            if (n == 0) return;

            var occupancy = new int[pop.Dwellings.Count];
            foreach (var hh in pop.Households)
                if (hh.Dwelling >= 0) occupancy[hh.Dwelling] += hh.Members.Count;
            var free = new WeightedPicker(pop.Dwellings.Count);
            for (int i = 0; i < pop.Dwellings.Count; i++) free.Set(i, Math.Max(0, pop.Dwellings[i].Capacity - occupancy[i]));
            var vacancies = new WeightedPicker(pop.Workplaces.Count);
            for (int i = 0; i < pop.Workplaces.Count; i++)
                if (!pop.Workplaces[i].Closed) vacancies.Set(i, Math.Max(0, pop.Workplaces[i].Capacity - employees[i]));

            for (int k = 0; k < n; k++)
            {
                int size = rng.Pick(ArrivalSizes) + 1;
                int dw = free.Pick(rng);
                if (dw < 0) break; // nessuna casa libera: non arriva nessuno
                var hh = new Household { Dwelling = dw, Tenure = rng.Chance(0.4) ? Tenure.Owner : Tenure.Renter };
                if (hh.Tenure == Tenure.Renter)
                {
                    double unitArea = Math.Max(35, Math.Min(160, pop.Dwellings[dw].FloorArea / Math.Max(1, pop.Dwellings[dw].Units)));
                    hh.Rent = (int)Math.Round(unitArea * B.RentPerM2Month / 10.0) * 10;
                }
                hh.Unit = pop.Dwellings[dw].Units++;
                pop.Households.Add(hh);
                int h = pop.Households.Count - 1;

                int adultAge = (int)Math.Round(Math.Max(19, Math.Min(70, rng.Gaussian(B.ArrivalAgeMean, B.ArrivalAgeSd))));
                bool couple = size >= 2 && rng.Chance(0.75);
                int first = AddArrival(pop, h, rng.Chance(0.5) ? Sex.Male : Sex.Female, adultAge, day, rng, vacancies, employees);
                int surname = pop.Citizens[first].Surname;
                int members = 1;
                if (couple)
                {
                    var other = pop.Citizens[first].Sex == Sex.Male ? Sex.Female : Sex.Male;
                    int second = AddArrival(pop, h, other, Math.Max(19, adultAge + rng.Range(-4, 4)), day, rng, vacancies, employees);
                    pop.Citizens[first].Partner = second;
                    pop.Citizens[second].Partner = first;
                    pop.Citizens[first].Role = pop.Citizens[second].Role = FamilyRole.Partner;
                    if (pop.Citizens[first].Sex == Sex.Male) surname = pop.Citizens[first].Surname;
                    else surname = pop.Citizens[second].Surname;
                    members = 2;
                }
                for (; members < size; members++)
                {
                    int childAge = rng.Range(0, Math.Max(0, Math.Min(17, adultAge - 20)));
                    int c = AddArrival(pop, h, rng.Chance(0.514) ? Sex.Male : Sex.Female, childAge, day, rng, vacancies, employees);
                    pop.Citizens[c].Role = FamilyRole.Child;
                    pop.Citizens[c].Surname = (ushort)surname;
                }
                hh.Type = couple ? (size > 2 ? HouseholdType.CoupleWithChildren : HouseholdType.Couple)
                    : size > 1 ? HouseholdType.SingleParent : HouseholdType.Alone;
                if (!couple && size > 1) pop.Citizens[first].Role = FamilyRole.Parent;
                int added = hh.Members.Count;
                occupancy[dw] += added;
                free.Set(dw, Math.Max(0, pop.Dwellings[dw].Capacity - occupancy[dw]));
                pop.Arrived += added;
                r.Arrived += added;
            }
        }

        private static readonly double[] ArrivalSizes = { 0.45, 0.3, 0.15, 0.1 };

        private int AddArrival(CityPopulation pop, int h, Sex sex, int age, int day, Rng rng, WeightedPicker vacancies, int[] employees)
        {
            var today = Citizen.Epoch.AddDays(day);
            int birth = DayIndex(today.AddYears(-age)) - rng.Next(365);
            var c = NewCitizen(pop, sex, birth, rng);
            c.Household = h;
            c.Role = FamilyRole.Single;
            int idx = pop.Add(c);
            pop.Households[h].Members.Add(idx);
            ref var p = ref pop.Citizens[idx];
            if (age < B.KindergartenFrom) p.Occupation = Occupation.Toddler;
            else if (age <= B.SchoolTo)
            {
                p.Occupation = Occupation.Pupil;
                p.Education = age >= 14 ? Education.Lower : age >= 11 ? Education.Primary : Education.None;
                p.School = NearestSchool(pop, idx, age < B.SchoolFrom ? PlaceLevel.Kindergarten : PlaceLevel.School);
            }
            else
            {
                p.Education = (Education)Math.Max(0, rng.Pick(B.EducationShares(today.Year - age)));
                p.Occupation = Occupation.Unemployed;
                if (rng.Chance(B.ArrivalJobChance) && vacancies.Total > 0) Hire(pop, idx, rng, vacancies, employees, 24000);
                // risparmi portati da fuori
                long savings = (long)Math.Round(Math.Max(1500, p.Income * B.NetIncomeShare / 12.0 * B.ArrivalSavingsMonths));
                p.Savings = (int)savings;
                pop.ExternalIn += savings;
            }
            return idx;
        }

        /// <summary>Nuovo cittadino (nato o arrivato): nome, tratti, aspetto e bisogni.</summary>
        private Citizen NewCitizen(CityPopulation pop, Sex sex, int birthDay, Rng rng)
        {
            var c = new Citizen
            {
                Sex = sex,
                BirthDay = birthDay,
                Household = Citizen.None,
                Partner = Citizen.None,
                Work = Citizen.None,
                School = Citizen.None,
                TransitLine = -1,
                Occupation = Occupation.Toddler,
            };
            var names = _in.Names.FirstNames(sex, c.BirthDate.Year);
            c.FirstName = (ushort)NameIndex(pop.FirstNames, names[Math.Min(names.Length - 1, (int)(rng.NextDouble() * rng.NextDouble() * names.Length))]);
            var surnames = _in.Names.Surnames;
            c.Surname = (ushort)NameIndex(pop.Surnames, surnames[Math.Min(surnames.Length - 1, (int)(rng.NextDouble() * rng.NextDouble() * surnames.Length))]);
            int traitCount = B.TraitKeys.Length;
            for (int k = 0; k < 3 && traitCount > 0; k++) c.Traits |= 1UL << rng.Next(traitCount);
            foreach (var (a, b) in B.OppositeTraits)
                if ((c.Traits & (1UL << a)) != 0 && (c.Traits & (1UL << b)) != 0) c.Traits &= ~(1UL << b);
            double cm = sex == Sex.Male ? rng.Gaussian(176, 7) : rng.Gaussian(163, 6.5);
            c.Appearance = (uint)rng.Next(6) | ((uint)rng.Next(8) << 3) | ((uint)rng.Next(16) << 6) | ((uint)rng.Next(4) << 10)
                           | ((uint)Math.Max(0, Math.Min(255, Math.Round(cm - 100))) << 12) | ((uint)rng.Next(8) << 20);
            c.Tastes = (uint)rng.Next(65536);
            c.Hunger = c.Energy = c.Hygiene = c.Comfort = 80;
            c.Fun = c.Social = 70;
            c.Health = 95;
            c.Mood = 75;
            return c;
        }

        private static int NameIndex(List<string> table, string name)
        {
            int i = table.IndexOf(name);
            if (i >= 0) return i;
            table.Add(name);
            return table.Count - 1;
        }
    }
}
