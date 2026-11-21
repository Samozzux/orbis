using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Core.World.Fb;

namespace Orbis.Core.Population
{
    /// <summary>
    /// Salvataggio compatto della popolazione (dentro il salvataggio della partita). Si salva tutto, non solo le
    /// differenze: i cittadini cambiano di continuo (bisogni, soldi, lavoro), e così la città non dipende da come
    /// sarebbe stata rigenerata su un'altra piattaforma o con un pack diverso.
    /// </summary>
    public static class PopulationSerializer
    {
        public const int Version = 6; // 2: stato dei cittadini, conti delle attività, contatori della vita; 3: giornata, frigo, scorte; 4: auto e trasporto pubblico; 5: biglietti delle linee del comune; 6: felicità
        private const uint Magic = 0x504F5043; // "CPOP"

        public static void Write(BinaryWriter w, CityPopulation p)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(p.MunicipalityCode ?? "");
            w.Write(p.MunicipalityName ?? "");
            w.Write(p.Seed);
            w.Write(p.Date.Ticks);
            w.Write(p.ExternalIn);
            w.Write(p.ExternalOut);
            w.Write(p.Births);
            w.Write(p.Deaths);
            w.Write(p.Arrived);
            w.Write(p.Left);
            w.Write(p.LastDay);
            w.Write(p.LastWelfare);
            w.Write(p.LastCityFares);
            w.Write(p.AverageHappiness);
            w.Write(p.UnhappyCount);
            w.Write(p.HappinessFactors.Length);
            foreach (var f in p.HappinessFactors) w.Write(f);
            Strings(w, p.TraitKeys);
            Strings(w, p.FirstNames);
            Strings(w, p.Surnames);

            w.Write(p.Dwellings.Count);
            foreach (var d in p.Dwellings)
            {
                w.Write(d.OsmId);
                w.Write(d.TileX);
                w.Write(d.TileY);
                w.Write(d.X);
                w.Write(d.Y);
                w.Write(d.FloorArea);
                w.Write(d.Units);
                w.Write(d.Capacity);
                w.Write((byte)d.Use);
                w.Write(d.Address ?? "");
            }
            Places(w, p.Workplaces);
            Places(w, p.Schools);

            w.Write(p.Households.Count);
            foreach (var h in p.Households)
            {
                w.Write((byte)h.Type);
                w.Write(h.Dwelling);
                w.Write(h.Unit);
                w.Write((byte)h.Tenure);
                w.Write(h.Rent);
                w.Write(h.Fridge);
                w.Write(h.FoodSpentToday);
                w.Write(h.Cars);
                w.Write(h.TransitCents);
                w.Write(h.CityTransitCents);
                w.Write(h.Members.Count);
                foreach (var m in h.Members) w.Write(m);
            }

            w.Write(p.Count);
            for (int i = 0; i < p.Count; i++)
            {
                ref var c = ref p.Citizens[i];
                w.Write(c.Household);
                w.Write(c.Partner);
                w.Write(c.Work);
                w.Write(c.School);
                w.Write(c.BirthDay);
                w.Write(c.Income);
                w.Write(c.Savings);
                w.Write(c.FirstName);
                w.Write(c.Surname);
                w.Write((byte)c.Sex);
                w.Write((byte)c.Education);
                w.Write((byte)c.Occupation);
                w.Write((byte)c.Role);
                w.Write(c.Traits);
                w.Write(c.Appearance);
                w.Write(c.Tastes);
                w.Write(c.Hunger);
                w.Write(c.Energy);
                w.Write(c.Hygiene);
                w.Write(c.Fun);
                w.Write(c.Social);
                w.Write(c.Comfort);
                w.Write(c.Health);
                w.Write(c.Mood);
                w.Write((byte)c.Flags);
                w.Write((byte)c.Activity);
                w.Write((byte)c.Location);
                w.Write((byte)c.PreviousLocation);
                w.Write(c.LocationIndex);
                w.Write(c.PreviousIndex);
                w.Write(c.ActivityEnd);
                w.Write(c.TravelStart);
                w.Write(c.TravelEnd);
                w.Write((byte)c.Mode);
                w.Write(c.TransitLine);
                w.Write(c.Happiness);
                w.Write(c.Commute);
            }
        }

        public static CityPopulation Read(BinaryReader r)
        {
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("popolazione: intestazione non valida");
            int version = r.ReadInt32();
            if (version < 1 || version > Version) throw new InvalidDataException("popolazione: versione " + version + " non supportata");
            var p = new CityPopulation
            {
                MunicipalityCode = r.ReadString(),
                MunicipalityName = r.ReadString(),
                Seed = r.ReadInt32(),
                Date = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            };
            if (version >= 2)
            {
                p.ExternalIn = r.ReadInt64();
                p.ExternalOut = r.ReadInt64();
                p.Births = r.ReadInt32();
                p.Deaths = r.ReadInt32();
                p.Arrived = r.ReadInt32();
                p.Left = r.ReadInt32();
                p.LastDay = r.ReadInt32();
                p.LastWelfare = r.ReadInt64();
            }
            if (version >= 5) p.LastCityFares = r.ReadInt64();
            if (version >= 6)
            {
                p.AverageHappiness = r.ReadSingle();
                p.UnhappyCount = r.ReadInt32();
                int factors = r.ReadInt32();
                for (int k = 0; k < factors; k++)
                {
                    float f = r.ReadSingle();
                    if (k < p.HappinessFactors.Length) p.HappinessFactors[k] = f;
                }
            }
            p.TraitKeys = ReadStrings(r).ToArray();
            p.FirstNames.AddRange(ReadStrings(r));
            p.Surnames.AddRange(ReadStrings(r));

            int dwellings = Count(r);
            for (int i = 0; i < dwellings; i++)
                p.Dwellings.Add(new Dwelling
                {
                    OsmId = r.ReadInt64(),
                    TileX = r.ReadInt32(),
                    TileY = r.ReadInt32(),
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    FloorArea = r.ReadSingle(),
                    Units = r.ReadInt32(),
                    Capacity = r.ReadInt32(),
                    Use = (BuildingUse)r.ReadByte(),
                    Address = r.ReadString(),
                });
            ReadPlaces(r, p.Workplaces, version);
            ReadPlaces(r, p.Schools, version);

            int households = Count(r);
            for (int i = 0; i < households; i++)
            {
                var h = new Household
                {
                    Type = (HouseholdType)r.ReadByte(),
                    Dwelling = r.ReadInt32(),
                    Unit = r.ReadInt32(),
                    Tenure = (Tenure)r.ReadByte(),
                    Rent = r.ReadInt32(),
                };
                if (version >= 3)
                {
                    h.Fridge = r.ReadInt32();
                    h.FoodSpentToday = r.ReadInt32();
                }
                if (version >= 4)
                {
                    h.Cars = r.ReadByte();
                    h.TransitCents = r.ReadInt32();
                }
                if (version >= 5) h.CityTransitCents = r.ReadInt32();
                int members = Count(r);
                for (int k = 0; k < members; k++) h.Members.Add(r.ReadInt32());
                p.Households.Add(h);
            }

            int count = Count(r);
            p.Citizens = new Citizen[Math.Max(64, count)];
            for (int i = 0; i < count; i++)
            {
                var c = new Citizen
                {
                    Household = r.ReadInt32(),
                    Partner = r.ReadInt32(),
                    Work = r.ReadInt32(),
                    School = r.ReadInt32(),
                    BirthDay = r.ReadInt32(),
                    Income = r.ReadInt32(),
                    Savings = r.ReadInt32(),
                    FirstName = r.ReadUInt16(),
                    Surname = r.ReadUInt16(),
                    Sex = (Sex)r.ReadByte(),
                    Education = (Education)r.ReadByte(),
                    Occupation = (Occupation)r.ReadByte(),
                    Role = (FamilyRole)r.ReadByte(),
                    Traits = r.ReadUInt64(),
                    Appearance = r.ReadUInt32(),
                    Tastes = r.ReadUInt32(),
                    Hunger = r.ReadByte(),
                    Energy = r.ReadByte(),
                    Hygiene = r.ReadByte(),
                    Fun = r.ReadByte(),
                    Social = r.ReadByte(),
                    Comfort = r.ReadByte(),
                    Health = r.ReadByte(),
                    Mood = r.ReadByte(),
                };
                if (version >= 2) c.Flags = (CitizenFlags)r.ReadByte();
                if (version >= 3)
                {
                    c.Activity = (CitizenActivity)r.ReadByte();
                    c.Location = (LocationKind)r.ReadByte();
                    c.PreviousLocation = (LocationKind)r.ReadByte();
                    c.LocationIndex = r.ReadInt32();
                    c.PreviousIndex = r.ReadInt32();
                    c.ActivityEnd = r.ReadInt32();
                    c.TravelStart = r.ReadInt32();
                    c.TravelEnd = r.ReadInt32();
                }
                if (version >= 4)
                {
                    c.Mode = (TravelMode)r.ReadByte();
                    c.TransitLine = r.ReadInt32();
                }
                else c.TransitLine = -1;
                if (version >= 6)
                {
                    c.Happiness = r.ReadByte();
                    c.Commute = r.ReadByte();
                }
                p.Add(c);
            }
            Validate(p);
            return p;
        }

        public static byte[] ToBytes(CityPopulation p)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Write(w, p);
                return ms.ToArray();
            }
        }

        public static CityPopulation FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms, System.Text.Encoding.UTF8))
                return Read(r);
        }

        /// <summary>Riferimenti dentro i limiti: un file rovinato non deve far cadere il gioco più avanti.</summary>
        private static void Validate(CityPopulation p)
        {
            for (int i = 0; i < p.Count; i++)
            {
                ref var c = ref p.Citizens[i];
                if (c.Household < -1 || c.Household >= p.Households.Count || c.Partner < -1 || c.Partner >= p.Count
                    || c.Work < Citizen.WorkOutside || c.Work >= p.Workplaces.Count || c.School < -1 || c.School >= p.Schools.Count
                    || c.FirstName >= Math.Max(1, p.FirstNames.Count) || c.Surname >= Math.Max(1, p.Surnames.Count))
                    throw new InvalidDataException("popolazione: riferimento non valido nel cittadino " + i);
            }
            foreach (var h in p.Households)
            {
                if (h.Dwelling < -1 || h.Dwelling >= p.Dwellings.Count) throw new InvalidDataException("popolazione: casa non valida");
                foreach (var m in h.Members)
                    if (m < 0 || m >= p.Count) throw new InvalidDataException("popolazione: membro non valido");
            }
        }

        private static int Count(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 10_000_000) throw new InvalidDataException("popolazione: numero di elementi non valido");
            return n;
        }

        private static void Places(BinaryWriter w, List<Place> list)
        {
            w.Write(list.Count);
            foreach (var p in list)
            {
                w.Write(p.OsmId);
                w.Write(p.TileX);
                w.Write(p.TileY);
                w.Write(p.X);
                w.Write(p.Y);
                w.Write((byte)p.Level);
                w.Write(p.Category ?? "");
                w.Write(p.Name ?? "");
                w.Write(p.Capacity);
                w.Write((byte)p.MinEducation);
                w.Write(p.Public);
                w.Write(p.Closed);
                w.Write(p.Cash);
                w.Write(p.RevenueDay);
                w.Write(p.CostsDay);
                w.Write(p.BadMonths);
                w.Write(p.Stock);
                w.Write(p.SalesToday);
            }
        }

        private static void ReadPlaces(BinaryReader r, List<Place> list, int version)
        {
            int n = Count(r);
            for (int i = 0; i < n; i++)
            {
                var place = new Place
                {
                    OsmId = r.ReadInt64(),
                    TileX = r.ReadInt32(),
                    TileY = r.ReadInt32(),
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Level = (PlaceLevel)r.ReadByte(),
                    Category = r.ReadString(),
                    Name = r.ReadString(),
                    Capacity = r.ReadInt32(),
                    MinEducation = (Education)r.ReadByte(),
                };
                if (version >= 2)
                {
                    place.Public = r.ReadBoolean();
                    place.Closed = r.ReadBoolean();
                    place.Cash = r.ReadInt64();
                    place.RevenueDay = r.ReadInt64();
                    place.CostsDay = r.ReadInt64();
                    place.BadMonths = r.ReadByte();
                }
                if (version >= 3)
                {
                    place.Stock = r.ReadInt32();
                    place.SalesToday = r.ReadInt64();
                }
                list.Add(place);
            }
        }

        private static void Strings(BinaryWriter w, IReadOnlyList<string> list)
        {
            w.Write(list.Count);
            foreach (var s in list) w.Write(s ?? "");
        }

        private static List<string> ReadStrings(BinaryReader r)
        {
            int n = Count(r);
            var list = new List<string>(n);
            for (int i = 0; i < n; i++) list.Add(r.ReadString());
            return list;
        }
    }
}
