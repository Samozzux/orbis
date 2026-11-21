using System;
using System.Collections.Generic;

namespace Orbis.Core.Population
{
    /// <summary>
    /// Popolazione sintetica di un comune (docs/03 §2), deterministica dal seed della partita:
    /// età e sesso esatti (ISTAT), famiglie dalle posizioni in famiglia per età (ISTAT, regione), case in
    /// proporzione alla superficie dei piani, titolo di studio, lavoro e redditi con le fasce del MEF.
    /// <see cref="Steps"/> lavora a fasi, così il gioco può spezzarla su più fotogrammi.
    /// </summary>
    public sealed class PopulationGenerator
    {
        private readonly PopulationData _d;
        private readonly NameData _names;
        private readonly PopulationBalance _b;
        private readonly CityPlaces _places;
        private readonly int _seed;

        private CityPopulation _pop;
        private int[] _age;
        private int[] _position;
        private readonly Dictionary<string, int> _firstNameIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _surnameIndex = new Dictionary<string, int>();

        // posizioni in famiglia (chiavi del file ISTAT)
        private int _pAloneM, _pAloneF, _pCoupleNo, _pCoupleYoung, _pCoupleAdult, _pFatherYoung, _pFatherAdult,
            _pMotherYoung, _pMotherAdult, _pChild, _pOther;

        public PopulationGenerator(PopulationData data, NameData names, PopulationBalance balance, CityPlaces places, int seed)
        {
            _d = data;
            _names = names;
            _b = balance;
            _places = places;
            _seed = seed;
        }

        public CityPopulation Result => _pop;

        public static CityPopulation Generate(PopulationData data, NameData names, PopulationBalance balance, CityPlaces places, int seed)
        {
            var g = new PopulationGenerator(data, names, balance, places, seed);
            foreach (var _ in g.Steps()) { }
            return g.Result;
        }

        /// <summary>Esegue la generazione a fasi; ogni valore è l'avanzamento (0–1).</summary>
        public IEnumerable<float> Steps()
        {
            _pop = new CityPopulation
            {
                MunicipalityCode = _d.MunicipalityCode,
                MunicipalityName = _d.MunicipalityName,
                Seed = _seed,
                Date = _d.Date,
                TraitKeys = _b.TraitKeys,
            };
            if (_places != null)
            {
                // copie: la generazione regola capacità e appartamenti, i luoghi di partenza restano com'erano
                foreach (var d in _places.Dwellings) _pop.Dwellings.Add(d.Clone());
                foreach (var w in _places.Workplaces) _pop.Workplaces.Add(w.Clone());
                foreach (var sc in _places.Schools) _pop.Schools.Add(sc.Clone());
            }
            CreatePeople();
            yield return 0.15f;
            AssignPositions();
            yield return 0.25f;
            BuildHouseholds();
            yield return 0.45f;
            PlaceHouseholds();
            yield return 0.6f;
            AssignEducation();
            AssignOccupationAndIncome();
            yield return 0.75f;
            AssignWork();
            AssignSchools();
            yield return 0.9f;
            Personalize();
            yield return 1f;
        }

        // ------------------------------------------------------------------ persone

        private void CreatePeople()
        {
            var rng = Rng.For(_seed, "population.people");
            _pop.Citizens = new Citizen[Math.Max(64, _d.Total)];
            var ages = new List<int>(_d.Total);
            int dateDay = Days(_d.Date);
            for (int age = 0; age <= PopulationData.MaxAge; age++)
            {
                for (int s = 0; s < 2; s++)
                {
                    int n = s == 0 ? _d.Male[age] : _d.Female[age];
                    for (int k = 0; k < n; k++)
                    {
                        int a = age;
                        // "100 e più": qualche anno in più per i più longevi
                        if (age == PopulationData.MaxAge && !rng.Chance(0.6)) a += rng.Range(1, 4);
                        int hi = Days(_d.Date.AddYears(-a));
                        int lo = Days(_d.Date.AddYears(-a - 1)) + 1;
                        var c = new Citizen
                        {
                            Sex = (Sex)s,
                            BirthDay = lo + rng.Next(hi - lo + 1),
                            Household = Citizen.None,
                            Partner = Citizen.None,
                            Work = Citizen.None,
                            School = Citizen.None,
                            TransitLine = -1,
                        };
                        _pop.Add(c);
                        ages.Add(a);
                    }
                }
            }
            _age = ages.ToArray();
            if (dateDay <= 0) throw new InvalidOperationException("data di riferimento non valida");
        }

        private static int Days(DateTime d) => (int)(d.Date - Citizen.Epoch.Date).TotalDays;

        // ------------------------------------------------------------------ posizioni in famiglia

        private void AssignPositions()
        {
            _pAloneM = _d.Position("alone_male");
            _pAloneF = _d.Position("alone_female");
            _pCoupleNo = _d.Position("couple_no_children");
            _pCoupleYoung = _d.Position("couple_young_children");
            _pCoupleAdult = _d.Position("couple_adult_children");
            _pFatherYoung = _d.Position("father_young_children");
            _pFatherAdult = _d.Position("father_adult_children");
            _pMotherYoung = _d.Position("mother_young_children");
            _pMotherAdult = _d.Position("mother_adult_children");
            _pChild = _d.Position("child");
            _pOther = _d.Position("other");
            if (_pChild < 0 || _pOther < 0 || _pAloneM < 0 || _pCoupleNo < 0)
                throw new FormatException("posizioni in famiglia mancanti nei dati");

            var rng = Rng.For(_seed, "population.positions");
            _position = new int[_pop.Count];
            int keys = _d.PositionKeys.Length;
            foreach (var row in _d.Positions)
            {
                var men = new List<int>();
                var women = new List<int>();
                for (int i = 0; i < _pop.Count; i++)
                {
                    int a = Math.Min(_age[i], PopulationData.MaxAge);
                    if (a < row.From || a > row.To) continue;
                    (_pop[i].Sex == Sex.Male ? men : women).Add(i);
                }
                int total = men.Count + women.Count;
                if (total == 0) continue;
                double regional = 0;
                foreach (var c in row.Counts) regional += c;
                if (regional <= 0) continue;
                double maleShare = (double)men.Count / total;
                Distribute(men, SexWeights(row.Counts, true, maleShare * regional, keys), rng);
                Distribute(women, SexWeights(row.Counts, false, (1 - maleShare) * regional, keys), rng);
            }
        }

        /// <summary>Pesi delle posizioni per un sesso: coppie a metà, figli e "altro" in proporzione a ciò che resta.</summary>
        private double[] SexWeights(int[] r, bool male, double sexTotal, int keys)
        {
            var w = new double[keys];
            int alone = male ? _pAloneM : _pAloneF;
            w[alone] = r[alone];
            foreach (var c in new[] { _pCoupleNo, _pCoupleYoung, _pCoupleAdult })
                if (c >= 0) w[c] = r[c] * 0.5;
            foreach (var p in male ? new[] { _pFatherYoung, _pFatherAdult } : new[] { _pMotherYoung, _pMotherAdult })
                if (p >= 0) w[p] = r[p];
            double fixedSum = 0;
            foreach (var x in w) fixedSum += x;
            double rest = r[_pChild] + r[_pOther];
            double k = rest > 0 ? Math.Max(0, sexTotal - fixedSum) / rest : 0;
            w[_pChild] = r[_pChild] * k;
            w[_pOther] = r[_pOther] * k;
            return w;
        }

        /// <summary>
        /// Assegna le posizioni in proporzione ai pesi (resti più grandi). Le posizioni da adulto (solo, in coppia,
        /// genitore) vanno ai più grandi della classe d'età, "figlio" e "altro" ai più giovani: nella classe 15–19
        /// un minorenne non resta mai da solo.
        /// </summary>
        private void Distribute(List<int> people, double[] weights, Rng rng)
        {
            var counts = Apportion(weights, people.Count);
            var arr = people.ToArray();
            rng.Shuffle(arr);
            // dal più grande al più giovane (a parità d'età resta l'ordine casuale)
            var order = new int[arr.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) => _age[arr[x]] != _age[arr[y]] ? _age[arr[y]].CompareTo(_age[arr[x]]) : x.CompareTo(y));
            int k = 0;
            for (int p = 0; p < counts.Length; p++)
            {
                if (p == _pChild || p == _pOther) continue;
                for (int j = 0; j < counts[p]; j++) _position[arr[order[k++]]] = p;
            }
            // il resto (i più giovani) tra figli e "altro", in ordine casuale
            var rest = new int[arr.Length - k];
            for (int i = 0; i < rest.Length; i++) rest[i] = arr[order[k + i]];
            rng.Shuffle(rest);
            int r = 0;
            for (int j = 0; j < counts[_pChild]; j++) _position[rest[r++]] = _pChild;
            for (int j = 0; j < counts[_pOther]; j++) _position[rest[r++]] = _pOther;
        }

        public static int[] Apportion(double[] weights, int n)
        {
            var result = new int[weights.Length];
            double total = 0;
            foreach (var w in weights) total += Math.Max(0, w);
            if (n <= 0) return result;
            if (total <= 0)
            {
                result[weights.Length - 1] = n;
                return result;
            }
            var rem = new double[weights.Length];
            int given = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                double exact = Math.Max(0, weights[i]) / total * n;
                result[i] = (int)Math.Floor(exact);
                rem[i] = exact - result[i];
                given += result[i];
            }
            while (given < n)
            {
                int best = 0;
                for (int i = 1; i < rem.Length; i++)
                    if (rem[i] > rem[best]) best = i;
                result[best]++;
                rem[best] = -1;
                given++;
            }
            return result;
        }

        // ------------------------------------------------------------------ famiglie

        private const int NeedNone = 0, NeedYoung = 1, NeedAdult = 2;
        private readonly List<int> _need = new List<int>();   // per famiglia: che figli servono
        private readonly List<int> _parentAge = new List<int>();

        private int NewHousehold(HouseholdType type, int need, int parentAge)
        {
            _pop.Households.Add(new Household { Type = type });
            _need.Add(need);
            _parentAge.Add(parentAge);
            return _pop.Households.Count - 1;
        }

        private void Join(int household, int person, FamilyRole role)
        {
            _pop.Households[household].Members.Add(person);
            _pop[person].Household = household;
            _pop[person].Role = role;
        }

        private void BuildHouseholds()
        {
            var rng = Rng.For(_seed, "population.households");
            var byPos = new List<int>[_d.PositionKeys.Length];
            for (int i = 0; i < byPos.Length; i++) byPos[i] = new List<int>();
            for (int i = 0; i < _pop.Count; i++) byPos[_position[i]].Add(i);

            var alone = new List<int>(byPos[_pAloneM]);
            alone.AddRange(byPos[_pAloneF]);
            var fatherYoung = _pFatherYoung >= 0 ? new List<int>(byPos[_pFatherYoung]) : new List<int>();
            var fatherAdult = _pFatherAdult >= 0 ? new List<int>(byPos[_pFatherAdult]) : new List<int>();
            var motherYoung = _pMotherYoung >= 0 ? new List<int>(byPos[_pMotherYoung]) : new List<int>();
            var motherAdult = _pMotherAdult >= 0 ? new List<int>(byPos[_pMotherAdult]) : new List<int>();

            // coppie: uomini e donne della stessa posizione, appaiati per età
            foreach (var (pos, need) in new[] { (_pCoupleNo, NeedNone), (_pCoupleYoung, NeedYoung), (_pCoupleAdult, NeedAdult) })
            {
                if (pos < 0) continue;
                var men = new List<int>();
                var women = new List<int>();
                foreach (var p in byPos[pos]) (_pop[p].Sex == Sex.Male ? men : women).Add(p);
                TrimRandom(men, women.Count, rng, need == NeedNone ? alone : need == NeedYoung ? fatherYoung : fatherAdult);
                TrimRandom(women, men.Count, rng, need == NeedNone ? alone : need == NeedYoung ? motherYoung : motherAdult);
                men.Sort(ByAge);
                women.Sort(ByAge);
                for (int i = 0; i < men.Count; i++)
                {
                    int m = men[i], w = women[i];
                    int h = NewHousehold(need == NeedNone ? HouseholdType.Couple : HouseholdType.CoupleWithChildren, need, _age[w]);
                    Join(h, m, FamilyRole.Partner);
                    Join(h, w, FamilyRole.Partner);
                    _pop[m].Partner = w;
                    _pop[w].Partner = m;
                }
            }
            foreach (var p in fatherYoung) Join(NewHousehold(HouseholdType.SingleParent, NeedYoung, _age[p]), p, FamilyRole.Parent);
            foreach (var p in fatherAdult) Join(NewHousehold(HouseholdType.SingleParent, NeedAdult, _age[p]), p, FamilyRole.Parent);
            foreach (var p in motherYoung) Join(NewHousehold(HouseholdType.SingleParent, NeedYoung, _age[p]), p, FamilyRole.Parent);
            foreach (var p in motherAdult) Join(NewHousehold(HouseholdType.SingleParent, NeedAdult, _age[p]), p, FamilyRole.Parent);

            var leftover = AssignChildren(byPos[_pChild], rng);
            foreach (var p in alone) Join(NewHousehold(HouseholdType.Alone, NeedNone, _age[p]), p, FamilyRole.Single);
            AssignOthers(byPos[_pOther], leftover, rng);
            FinalizeHouseholds();
        }

        /// <summary>Riduce la lista a <paramref name="count"/> persone scelte a caso; le altre vanno in <paramref name="overflow"/>.</summary>
        private static void TrimRandom(List<int> list, int count, Rng rng, List<int> overflow)
        {
            if (list.Count <= count) return;
            var arr = list.ToArray();
            rng.Shuffle(arr);
            list.Clear();
            for (int i = 0; i < arr.Length; i++)
                if (i < count) list.Add(arr[i]);
                else overflow.Add(arr[i]);
        }

        /// <summary>Figli alle famiglie con un genitore di età plausibile; restituisce quelli che restano senza.</summary>
        private List<int> AssignChildren(List<int> children, Rng rng)
        {
            const int adultFrom = 20;
            var young = new SortedDictionary<int, List<int>>();
            var adult = new SortedDictionary<int, List<int>>();
            foreach (var c in children)
            {
                var map = _age[c] < adultFrom ? young : adult;
                if (!map.TryGetValue(_age[c], out var l)) map[_age[c]] = l = new List<int>();
                l.Add(c);
            }
            foreach (var l in young.Values) Shuffle(l, rng);
            foreach (var l in adult.Values) Shuffle(l, rng);

            // 1) ogni famiglia "con figli" ne riceve almeno uno
            var order = new int[_pop.Households.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            rng.Shuffle(order);
            foreach (var h in order)
            {
                int need = _need[h];
                if (need == NeedNone) continue;
                int pa = _parentAge[h];
                int gap = (int)Math.Round(Clamp(rng.Gaussian(_b.ParentGapMean, _b.ParentGapSd), _b.ParentGapMin, _b.ParentGapMax));
                int lo = pa - _b.ParentGapMax, hi = pa - _b.ParentGapMin;
                if (need == NeedYoung) hi = Math.Min(hi, adultFrom - 1);
                else lo = Math.Max(lo, adultFrom);
                int child = TakeNearest(need == NeedYoung ? young : adult, pa - gap, lo, hi);
                if (child < 0)
                {
                    _need[h] = NeedNone;
                    continue;
                }
                Join(h, child, FamilyRole.Child);
            }

            // 2) gli altri figli: famiglie con un genitore dell'età giusta e posto per un altro figlio
            var parentsYoung = new SortedDictionary<int, List<int>>();
            var parentsAll = new SortedDictionary<int, List<int>>();
            for (int h = 0; h < _pop.Households.Count; h++)
            {
                if (_need[h] == NeedNone) continue;
                Add(parentsAll, _parentAge[h], h);
                if (_need[h] == NeedYoung) Add(parentsYoung, _parentAge[h], h);
            }
            var left = new List<int>();
            var rest = new List<int>();
            foreach (var l in young.Values) rest.AddRange(l);
            foreach (var l in adult.Values) rest.AddRange(l);
            var restArr = rest.ToArray();
            rng.Shuffle(restArr);
            foreach (var c in restArr)
            {
                int ca = _age[c];
                int gap = (int)Math.Round(Clamp(rng.Gaussian(_b.ParentGapMean, _b.ParentGapSd), _b.ParentGapMin, _b.ParentGapMax));
                var map = ca < adultFrom ? parentsYoung : parentsAll;
                int h = TakeParent(map, ca + gap, ca + _b.ParentGapMin, ca + _b.ParentGapMax, rng);
                if (h < 0) left.Add(c);
                else Join(h, c, FamilyRole.Child);
            }
            return left;
        }

        private static void Add(SortedDictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var l)) map[key] = l = new List<int>();
            l.Add(value);
        }

        private static void Shuffle(List<int> l, Rng rng)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = l[i];
                l[i] = l[j];
                l[j] = t;
            }
        }

        /// <summary>Toglie dalla mappa (età → persone) quella con l'età più vicina a <paramref name="want"/> in [lo, hi].</summary>
        private static int TakeNearest(SortedDictionary<int, List<int>> map, int want, int lo, int hi)
        {
            if (lo > hi) return -1;
            want = Math.Max(lo, Math.Min(hi, want));
            for (int d = 0; d <= hi - lo; d++)
            {
                foreach (int a in d == 0 ? new[] { want } : new[] { want - d, want + d })
                {
                    if (a < lo || a > hi) continue;
                    if (map.TryGetValue(a, out var l) && l.Count > 0)
                    {
                        int p = l[l.Count - 1];
                        l.RemoveAt(l.Count - 1);
                        return p;
                    }
                }
            }
            return -1;
        }

        /// <summary>Famiglia con un genitore di età vicina a <paramref name="want"/> e posto per un figlio in più.</summary>
        private int TakeParent(SortedDictionary<int, List<int>> map, int want, int lo, int hi, Rng rng)
        {
            want = Math.Max(lo, Math.Min(hi, want));
            for (int d = 0; d <= hi - lo; d++)
            {
                foreach (int a in d == 0 ? new[] { want } : new[] { want - d, want + d })
                {
                    if (a < lo || a > hi || !map.TryGetValue(a, out var l)) continue;
                    while (l.Count > 0)
                    {
                        int i = rng.Next(l.Count);
                        int h = l[i];
                        if (Children(h) < _b.MaxChildren && rng.Chance(ChildChance(Children(h)))) return h;
                        if (Children(h) >= _b.MaxChildren)
                        {
                            l[i] = l[l.Count - 1];
                            l.RemoveAt(l.Count - 1);
                            continue;
                        }
                        break;
                    }
                }
            }
            return -1;
        }

        // famiglie numerose più rare: con già n figli, la probabilità di accoglierne un altro cala
        private static double ChildChance(int n) => n <= 1 ? 1.0 : n == 2 ? 0.55 : n == 3 ? 0.25 : 0.1;

        private int Children(int h)
        {
            int n = 0;
            foreach (var m in _pop.Households[h].Members)
                if (_pop[m].Role == FamilyRole.Child) n++;
            return n;
        }

        /// <summary>Persone in "altra posizione" (e figli rimasti senza famiglia): convivenze o parenti in casa d'altri.</summary>
        private void AssignOthers(List<int> others, List<int> leftover, Rng rng)
        {
            int totalTypes = 0;
            foreach (var v in _d.HouseholdTypes.Values) totalTypes += v;
            _d.HouseholdTypes.TryGetValue("other", out var otherTypes);
            double expectedHouseholds = _d.AverageHouseholdSize > 0 ? _pop.Count / _d.AverageHouseholdSize : _pop.Households.Count;
            int otherHouseholds = totalTypes > 0 ? (int)Math.Round(expectedHouseholds * otherTypes / totalTypes) : 0;

            var adults = new List<int>();
            var minors = new List<int>();
            foreach (var p in others) (_age[p] >= 18 ? adults : minors).Add(p);
            foreach (var p in leftover)
            {
                if (_age[p] >= 18) Join(NewHousehold(HouseholdType.Alone, NeedNone, _age[p]), p, FamilyRole.Single);
                else minors.Add(p);
            }

            // convivenze di adulti di età simile (amici, studenti, fratelli): gruppi consecutivi nella lista per età
            adults.Sort(ByAge);
            var used = new bool[adults.Count];
            for (int k = 0; k < otherHouseholds && adults.Count > 1; k++)
            {
                int size = rng.Chance(_b.OtherHouseholdSize - 2) ? 3 : 2;
                int at = rng.Next(adults.Count);
                var group = new List<int>(size);
                for (int j = 0; j < adults.Count && group.Count < size; j++)
                {
                    int q = (at + j) % adults.Count;
                    if (!used[q]) group.Add(q);
                }
                if (group.Count < 2) break;
                int h = NewHousehold(HouseholdType.Other, NeedNone, _age[adults[group[0]]]);
                foreach (var q in group)
                {
                    used[q] = true;
                    Join(h, adults[q], FamilyRole.Relative);
                }
            }

            // gli altri vivono con una famiglia (nonni, parenti); i minori solo dove c'è un adulto abbastanza grande
            var families = new List<int>();
            for (int h = 0; h < _pop.Households.Count; h++)
                if (_pop.Households[h].Type != HouseholdType.Other) families.Add(h);
            for (int i = 0; i < adults.Count; i++)
                if (!used[i]) JoinFamily(adults[i], families, rng);
            foreach (var p in minors) JoinFamily(p, families, rng);
        }

        private int ByAge(int x, int y) => _age[x] != _age[y] ? _age[x].CompareTo(_age[y]) : x.CompareTo(y);

        private void JoinFamily(int person, List<int> families, Rng rng)
        {
            for (int attempt = 0; attempt < 40 && families.Count > 0; attempt++)
            {
                int h = families[rng.Next(families.Count)];
                if (_age[person] >= 18 || OldestAge(h) >= _age[person] + _b.ParentGapMin)
                {
                    Join(h, person, _age[person] < 18 ? FamilyRole.Child : FamilyRole.Relative);
                    return;
                }
            }
            // a caso non si è trovata: la prima famiglia con un adulto abbastanza grande
            foreach (var h in families)
                if (_age[person] >= 18 || OldestAge(h) >= _age[person] + _b.ParentGapMin)
                {
                    Join(h, person, _age[person] < 18 ? FamilyRole.Child : FamilyRole.Relative);
                    return;
                }
            Join(NewHousehold(HouseholdType.Alone, NeedNone, _age[person]), person, FamilyRole.Single);
        }

        private int OldestAge(int h)
        {
            int max = 0;
            foreach (var m in _pop.Households[h].Members) max = Math.Max(max, _age[m]);
            return max;
        }

        /// <summary>Tipo finale di ogni famiglia e cognomi (i figli prendono quello del padre o del genitore solo).</summary>
        private void FinalizeHouseholds()
        {
            var rngNames = Rng.For(_seed, "population.names");
            for (int h = 0; h < _pop.Households.Count; h++)
            {
                var hh = _pop.Households[h];
                int partners = 0, children = 0, parents = 0;
                foreach (var m in hh.Members)
                {
                    if (_pop[m].Role == FamilyRole.Partner) partners++;
                    else if (_pop[m].Role == FamilyRole.Child) children++;
                    else if (_pop[m].Role == FamilyRole.Parent) parents++;
                }
                if (hh.Type == HouseholdType.Other)
                {
                    // convivenza: resta tale
                }
                else if (partners == 2) hh.Type = children > 0 ? HouseholdType.CoupleWithChildren : HouseholdType.Couple;
                else if (parents == 1 && children > 0) hh.Type = HouseholdType.SingleParent;
                else if (hh.Members.Count == 1) hh.Type = HouseholdType.Alone;
                else hh.Type = HouseholdType.Other;
                // genitore solo senza figli: vive da solo (o con parenti)
                if (parents == 1 && children == 0)
                    foreach (var m in hh.Members)
                        if (_pop[m].Role == FamilyRole.Parent) _pop[m].Role = hh.Members.Count == 1 ? FamilyRole.Single : FamilyRole.Relative;

                int familyName = -1;
                foreach (var m in hh.Members)
                {
                    var role = _pop[m].Role;
                    if ((role == FamilyRole.Partner && _pop[m].Sex == Sex.Male) || role == FamilyRole.Parent)
                    {
                        _pop[m].Surname = (ushort)Surname(rngNames);
                        familyName = _pop[m].Surname;
                    }
                }
                foreach (var m in hh.Members)
                {
                    if ((_pop[m].Role == FamilyRole.Partner && _pop[m].Sex == Sex.Male) || _pop[m].Role == FamilyRole.Parent) continue;
                    _pop[m].Surname = (ushort)(_pop[m].Role == FamilyRole.Child && familyName >= 0 ? familyName : Surname(rngNames));
                }
                foreach (var m in hh.Members) _pop[m].FirstName = (ushort)FirstName(rngNames, _pop[m]);
            }
        }

        private int Surname(Rng rng)
        {
            var list = _names.Surnames;
            string s = list[Zipf(rng, list.Length)];
            if (!_surnameIndex.TryGetValue(s, out var i))
            {
                i = _pop.Surnames.Count;
                _pop.Surnames.Add(s);
                _surnameIndex[s] = i;
            }
            return i;
        }

        private int FirstName(Rng rng, in Citizen c)
        {
            var list = _names.FirstNames(c.Sex, c.BirthDate.Year);
            string s = list[Zipf(rng, list.Length)];
            if (!_firstNameIndex.TryGetValue(s, out var i))
            {
                i = _pop.FirstNames.Count;
                _pop.FirstNames.Add(s);
                _firstNameIndex[s] = i;
            }
            return i;
        }

        /// <summary>Indice con peso 1/(rango+3): i nomi in cima alla lista escono più spesso.</summary>
        private static int Zipf(Rng rng, int n)
        {
            double total = 0;
            for (int i = 0; i < n; i++) total += 1.0 / (i + 3);
            double r = rng.NextDouble() * total;
            for (int i = 0; i < n; i++)
            {
                r -= 1.0 / (i + 3);
                if (r < 0) return i;
            }
            return n - 1;
        }

        // ------------------------------------------------------------------ case

        private void PlaceHouseholds()
        {
            var rng = Rng.For(_seed, "population.dwellings");
            var dw = _pop.Dwellings;
            if (dw.Count == 0) return;
            var raw = new double[dw.Count];
            double rawTotal = 0;
            for (int i = 0; i < dw.Count; i++)
            {
                double w = _places != null && i < _places.DwellingWeights.Count ? _places.DwellingWeights[i] : 1.0;
                raw[i] = dw[i].FloorArea * w / _b.GrossM2PerPerson;
                rawTotal += raw[i];
            }
            double factor = rawTotal > 0 ? _pop.Count * _b.SpareCapacity / rawTotal : 0;
            var picker = new WeightedPicker(dw.Count);
            var remaining = new int[dw.Count];
            for (int i = 0; i < dw.Count; i++)
            {
                dw[i].Capacity = Math.Max(1, (int)Math.Round(raw[i] * factor));
                remaining[i] = dw[i].Capacity;
                picker.Set(i, remaining[i]);
            }
            var order = new int[_pop.Households.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            rng.Shuffle(order);
            // le famiglie più numerose per prime, quando c'è ancora spazio (a parità, l'ordine casuale di prima)
            var rank = new int[order.Length];
            for (int i = 0; i < order.Length; i++) rank[order[i]] = i;
            Array.Sort(order, (x, y) =>
            {
                int c = _pop.Households[y].Members.Count.CompareTo(_pop.Households[x].Members.Count);
                return c != 0 ? c : rank[x].CompareTo(rank[y]);
            });
            var units = new int[dw.Count];
            foreach (var h in order)
            {
                var hh = _pop.Households[h];
                int size = hh.Members.Count;
                int pick = -1;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int i = picker.Pick(rng);
                    if (i < 0) break;
                    pick = i;
                    if (remaining[i] >= size) break;
                }
                if (pick < 0) pick = rng.Next(dw.Count); // tutto pieno: si stringono
                remaining[pick] -= size;
                picker.Set(pick, Math.Max(0, remaining[pick]));
                hh.Dwelling = pick;
                hh.Unit = units[pick]++;
                if (units[pick] > dw[pick].Units) dw[pick].Units = units[pick];
                hh.Fridge = (int)Math.Round(size * _b.Routine.FridgePortionsPerPerson * (0.4 + 0.5 * rng.NextDouble()));
                hh.Tenure = rng.Chance(_b.OwnerShare) ? Tenure.Owner : Tenure.Renter;
                if (hh.Tenure == Tenure.Renter)
                {
                    double unitArea = dw[pick].FloorArea / Math.Max(1, dw[pick].Units);
                    unitArea = Math.Max(35, Math.Min(160, unitArea));
                    hh.Rent = (int)Math.Round(unitArea * _b.RentPerM2Month * (0.85 + 0.3 * rng.NextDouble()) / 10.0) * 10;
                }
            }
        }

        // ------------------------------------------------------------------ studio, lavoro, reddito

        private void AssignEducation()
        {
            var rng = Rng.For(_seed, "population.education");
            for (int i = 0; i < _pop.Count; i++)
            {
                int a = _age[i];
                Education e;
                if (a < 11) e = Education.None;
                else if (a < 14) e = Education.Primary;
                else if (a < 19) e = Education.Lower;
                else
                {
                    e = (Education)Math.Max(0, rng.Pick(_b.EducationShares(_pop[i].BirthDate.Year)));
                    if (e == Education.Degree && a < 23) e = Education.Upper;
                }
                _pop[i].Education = e;
            }
        }

        private void AssignOccupationAndIncome()
        {
            var rng = Rng.For(_seed, "population.work");
            int n = _pop.Count;
            var assigned = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int a = _age[i];
                if (a < _b.KindergartenFrom) Set(i, Occupation.Toddler, assigned);
                else if (a <= _b.SchoolTo) Set(i, Occupation.Pupil, assigned);
                else if (a <= _b.UniversityTo && _pop[i].Education >= Education.Upper && rng.Chance(_b.UniversityShare))
                    Set(i, Occupation.UniversityStudent, assigned);
            }

            // pensioni: alle persone più anziane, a partire da earlyPensionFromAge
            var seniors = new List<int>();
            for (int i = 0; i < n; i++)
                if (!assigned[i] && _age[i] >= _b.EarlyPensionFromAge) seniors.Add(i);
            seniors.Sort((x, y) => _age[y] != _age[x] ? _age[y].CompareTo(_age[x]) : x.CompareTo(y));
            int pensions = Math.Min(_d.IncomeKindCount("pension"), seniors.Count);
            var holders = new List<int>();
            for (int k = 0; k < pensions; k++)
            {
                Set(seniors[k], Occupation.Retired, assigned);
                holders.Add(seniors[k]);
            }

            // lavoratori: dipendenti, autonomi e imprenditori del MEF, scelti con i pesi per età
            int employees = _d.IncomeKindCount("employee");
            int self = _d.IncomeKindCount("self_employed");
            int business = _d.IncomeKindCount("business");
            var picker = new WeightedPicker(n);
            for (int i = 0; i < n; i++)
            {
                if (assigned[i] || _age[i] < _b.WorkFromAge || _age[i] >= _b.RetirementAge) continue;
                picker.Set(i, _b.EmploymentWeight(_age[i]) * (_pop[i].Sex == Sex.Female ? 0.85 : 1.0) * (0.8 + 0.1 * (int)_pop[i].Education));
            }
            var workers = new List<int>();
            for (int k = 0; k < employees + self + business; k++)
            {
                int i = picker.Pick(rng);
                if (i < 0) break;
                picker.Set(i, 0);
                workers.Add(i);
            }
            // autonomi e imprenditori tra i lavoratori più grandi, gli altri dipendenti
            var arr = workers.ToArray();
            rng.Shuffle(arr);
            int selfLeft = self, businessLeft = business;
            foreach (var i in arr)
            {
                if (_age[i] >= 28 && businessLeft > 0 && rng.Chance(0.5)) { Set(i, Occupation.Entrepreneur, assigned); businessLeft--; }
                else if (_age[i] >= 25 && selfLeft > 0 && rng.Chance(0.5)) { Set(i, Occupation.SelfEmployed, assigned); selfLeft--; }
                else Set(i, Occupation.Employee, assigned);
                holders.Add(i);
            }

            for (int i = 0; i < n; i++)
            {
                if (assigned[i]) continue;
                bool homemaker = _pop[i].Sex == Sex.Female ? rng.Chance(_b.HomemakerShareFemale) : rng.Chance(0.08);
                Set(i, _age[i] >= _b.RetirementAge || homemaker ? Occupation.Homemaker : Occupation.Unemployed, assigned);
            }

            AssignIncomes(holders, rng);

            // auto: quasi sempre se in famiglia c'è un reddito, a volte anche senza (pensioni basse, ecc.)
            var rr = _b.Routine;
            foreach (var hh in _pop.Households)
            {
                int best = 0;
                foreach (var m in hh.Members) best = Math.Max(best, _pop[m].Income);
                double p = best >= rr.CarMinIncome ? rr.CarOwnershipWithIncome : rr.CarOwnershipWithoutIncome;
                hh.Cars = (byte)(rng.Chance(p) ? (best > 40000 && hh.Members.Count > 2 && rng.Chance(0.5) ? 2 : 1) : 0);
            }

            for (int i = 0; i < n; i++)
            {
                if (_age[i] < 18) continue;
                int income = _pop[i].Income;
                double months = _b.SavingsMonthsMean * Math.Exp(rng.Gaussian(0, _b.SavingsSpread) - _b.SavingsSpread * _b.SavingsSpread / 2);
                double baseIncome = income > 0 ? income : 4000;
                if (_age[i] >= 60) months *= 1.6;
                _pop[i].Savings = (int)Math.Min(int.MaxValue / 2, Math.Round(baseIncome / 12.0 * months / 10.0) * 10);
            }
        }

        private void Set(int i, Occupation o, bool[] assigned)
        {
            _pop[i].Occupation = o;
            assigned[i] = true;
        }

        /// <summary>
        /// Redditi con la distribuzione vera del comune (fasce MEF): i valori ordinati vanno alle persone ordinate
        /// per "potenziale" (tipo di lavoro, studio, età e una parte di caso).
        /// </summary>
        private void AssignIncomes(List<int> holders, Rng rng)
        {
            var values = new List<int>();
            foreach (var c in _d.IncomeClasses)
            {
                if (c.Count <= 0) continue;
                double avg = c.Average;
                // intervallo attorno alla media, dentro la fascia (senza ammucchiare valori sui confini)
                double lo = Math.Max(c.From ?? 0, avg * (c.To == null ? 0.6 : 0.7));
                double hi = Math.Min(c.To.HasValue ? c.To.Value - 1 : double.MaxValue, avg * (c.To == null ? 1.4 : 1.3));
                if (hi < lo) hi = lo;
                for (int k = 0; k < c.Count; k++)
                    values.Add((int)Math.Round(lo + (hi - lo) * rng.NextDouble()));
            }
            values.Sort();
            var score = new double[holders.Count];
            var idx = new int[holders.Count];
            // idx ordinato per punteggio crescente (a parità, per posizione: ordine identico su ogni piattaforma)
            for (int k = 0; k < holders.Count; k++)
            {
                int i = holders[k];
                ref var c = ref _pop[i];
                double kind = c.Occupation == Occupation.Entrepreneur ? 1.3 : c.Occupation == Occupation.SelfEmployed ? 1.25
                    : c.Occupation == Occupation.Retired ? 0.95 : 1.0;
                double ageF = c.Occupation == Occupation.Retired ? 1.0 : 0.7 + 0.3 * Math.Min(1.0, (_age[i] - 19) / 30.0);
                score[k] = kind * (1 + 0.15 * (int)c.Education) * ageF * Math.Exp(rng.Gaussian(0, 0.35));
                idx[k] = k;
            }
            Array.Sort(idx, (x, y) => score[x] != score[y] ? score[x].CompareTo(score[y]) : x.CompareTo(y));
            int offset = values.Count - holders.Count; // più valori che persone: si scartano i più bassi
            for (int r = 0; r < idx.Length; r++)
            {
                int vi = r + offset;
                int v = values.Count == 0 ? 0 : values[Math.Max(0, Math.Min(values.Count - 1, vi))];
                _pop[holders[idx[r]]].Income = Math.Max(0, v);
            }
        }

        private void AssignWork()
        {
            var rng = Rng.For(_seed, "population.jobs");
            var places = _pop.Workplaces;
            var workers = new List<int>();
            for (int i = 0; i < _pop.Count; i++)
                if (_pop[i].Works) workers.Add(i);
            var local = new List<int>();
            foreach (var w in workers)
            {
                if (places.Count > 0 && rng.Chance(_b.LocalWorkShare)) local.Add(w);
                else _pop[w].Work = Citizen.WorkOutside;
            }
            if (places.Count == 0) return;
            double jobs = 0;
            foreach (var p in places) jobs += p.Capacity;
            // posti per i residenti e per i pendolari che vengono da fuori (Chieti ha più posti che lavoratori residenti)
            double factor = jobs > 0 ? local.Count * Math.Max(1.05, _b.JobsPerLocalWorker) / jobs : 1;
            var picker = new WeightedPicker(places.Count);
            var free = new int[places.Count];
            for (int i = 0; i < places.Count; i++)
            {
                places[i].Capacity = Math.Max(1, (int)Math.Round(places[i].Capacity * factor));
                // conto iniziale: i posti pubblici sono pagati dallo Stato, le attività partono con un po' di cassa
                places[i].Public = PopulationBalance.InSet(_b.PublicCategories, places[i].Category);
                places[i].Cash = places[i].Public ? 0 : (long)Math.Round(places[i].Capacity * _b.InitialCashPerJob);
                if (PopulationBalance.InSet(_b.Routine.FoodCategories, places[i].Category)) places[i].Stock = places[i].Capacity * _b.Routine.StoreStockPerJob;
                free[i] = places[i].Capacity;
                picker.Set(i, free[i]);
            }
            var arr = local.ToArray();
            rng.Shuffle(arr);
            foreach (var w in arr)
            {
                int pick = -1;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int i = picker.Pick(rng);
                    if (i < 0) break;
                    if (pick < 0) pick = i;
                    if (places[i].MinEducation <= _pop[w].Education)
                    {
                        pick = i;
                        break;
                    }
                }
                if (pick < 0)
                {
                    _pop[w].Work = Citizen.WorkOutside;
                    continue;
                }
                _pop[w].Work = pick;
                free[pick]--;
                picker.Set(pick, Math.Max(0, free[pick]));
            }
        }

        private void AssignSchools()
        {
            var rng = Rng.For(_seed, "population.schools");
            var schools = _pop.Schools;
            if (schools.Count == 0) return;
            var free = new int[schools.Count];
            for (int i = 0; i < schools.Count; i++) free[i] = schools[i].Capacity;
            var uni = new WeightedPicker(schools.Count);
            for (int i = 0; i < schools.Count; i++)
                if (schools[i].Level == PlaceLevel.University) uni.Set(i, schools[i].Capacity);
            for (int i = 0; i < _pop.Count; i++)
            {
                ref var c = ref _pop[i];
                if (c.Occupation == Occupation.UniversityStudent)
                {
                    c.School = uni.Total > 0 && rng.Chance(0.75) ? uni.Pick(rng) : Citizen.None; // gli altri studiano fuori
                    continue;
                }
                if (c.Occupation != Occupation.Pupil) continue;
                var level = _age[i] < _b.SchoolFrom ? PlaceLevel.Kindergarten : PlaceLevel.School;
                c.School = Nearest(i, level, free);
                if (c.School < 0 && level == PlaceLevel.Kindergarten) c.School = Nearest(i, PlaceLevel.School, free);
                if (c.School >= 0) free[c.School]--;
            }
        }

        /// <summary>Scuola del livello più vicina a casa con posti liberi (o la più vicina se sono tutte piene).</summary>
        private int Nearest(int person, PlaceLevel level, int[] free)
        {
            int h = _pop[person].Household;
            float x = 0, y = 0;
            if (h >= 0 && _pop.Households[h].Dwelling >= 0)
            {
                var d = _pop.Dwellings[_pop.Households[h].Dwelling];
                x = d.X;
                y = d.Y;
            }
            int best = -1, bestFull = -1;
            double bd = double.MaxValue, bdFull = double.MaxValue;
            for (int i = 0; i < _pop.Schools.Count; i++)
            {
                var s = _pop.Schools[i];
                if (s.Level != level) continue;
                double dx = s.X - x, dy = s.Y - y, dist = dx * dx + dy * dy;
                if (free[i] > 0 && dist < bd) { bd = dist; best = i; }
                if (dist < bdFull) { bdFull = dist; bestFull = i; }
            }
            return best >= 0 ? best : bestFull;
        }

        // ------------------------------------------------------------------ personalità e aspetto

        private void Personalize()
        {
            var rng = Rng.For(_seed, "population.traits");
            int traitCount = _b.TraitKeys.Length;
            for (int i = 0; i < _pop.Count; i++)
            {
                ref var c = ref _pop[i];
                int a = _age[i];
                int want = a < 6 ? 2 : rng.Range(_b.TraitsMin, _b.TraitsMax);
                ulong traits = 0;
                for (int attempt = 0; attempt < want * 6 && traitCount > 0 && Bits(traits) < want; attempt++)
                {
                    int t = rng.Next(traitCount);
                    if ((traits & (1UL << t)) != 0 || Conflicts(traits, t)) continue;
                    traits |= 1UL << t;
                }
                c.Traits = traits;
                // aspetto: carnagione (3 bit), colore e taglio dei capelli (3+4), occhi (2), statura (8), corporatura (3)
                uint skin = (uint)Math.Max(0, rng.Pick(SkinWeights));
                uint hair = (uint)Math.Max(0, rng.Pick(HairWeights));
                if (a >= 60 && rng.Chance(Math.Min(0.9, (a - 50) / 35.0))) hair = 7; // grigi/bianchi
                uint style = (uint)rng.Next(16);
                uint eyes = (uint)Math.Max(0, rng.Pick(EyeWeights));
                double cm = c.Sex == Sex.Male ? rng.Gaussian(176, 7) : rng.Gaussian(163, 6.5);
                uint stature = (uint)Math.Max(0, Math.Min(255, Math.Round(cm - 100)));
                uint build = (uint)rng.Next(8);
                c.Appearance = skin | (hair << 3) | (style << 6) | (eyes << 10) | (stature << 12) | (build << 20);
                c.Tastes = (uint)rng.Next(16) | ((uint)rng.Next(16) << 4) | ((uint)rng.Next(16) << 8) | ((uint)rng.Next(16) << 12);
                c.Hunger = (byte)rng.Range(55, 95);
                c.Energy = (byte)rng.Range(55, 95);
                c.Hygiene = (byte)rng.Range(60, 95);
                c.Fun = (byte)rng.Range(45, 90);
                c.Social = (byte)rng.Range(45, 90);
                c.Comfort = (byte)rng.Range(55, 95);
                c.Health = (byte)Math.Max(20, Math.Min(100, 100 - Math.Max(0, a - 55) * 0.8 + rng.Gaussian(0, 6)));
                c.Mood = (byte)rng.Range(55, 85);
            }
        }

        private static readonly double[] SkinWeights = { 0.25, 0.35, 0.25, 0.1, 0.04, 0.01 };
        private static readonly double[] HairWeights = { 0.3, 0.3, 0.15, 0.08, 0.05, 0.02, 0.05, 0.05 };
        private static readonly double[] EyeWeights = { 0.55, 0.25, 0.12, 0.08 };

        private bool Conflicts(ulong traits, int t)
        {
            foreach (var (a, b) in _b.OppositeTraits)
                if ((t == a && (traits & (1UL << b)) != 0) || (t == b && (traits & (1UL << a)) != 0)) return true;
            return false;
        }

        private static int Bits(ulong v)
        {
            int n = 0;
            while (v != 0)
            {
                v &= v - 1;
                n++;
            }
            return n;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
