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
        public const int Version = 1;
        private const uint Magic = 0x504F5043; // "CPOP"

        public static void Write(BinaryWriter w, CityPopulation p)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(p.MunicipalityCode ?? "");
            w.Write(p.MunicipalityName ?? "");
            w.Write(p.Seed);
            w.Write(p.Date.Ticks);
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
            ReadPlaces(r, p.Workplaces);
            ReadPlaces(r, p.Schools);

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
            }
        }

        private static void ReadPlaces(BinaryReader r, List<Place> list)
        {
            int n = Count(r);
            for (int i = 0; i < n; i++)
                list.Add(new Place
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
                });
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
