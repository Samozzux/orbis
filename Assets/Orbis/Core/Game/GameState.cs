using System;
using System.Collections.Generic;
using System.IO;

namespace Orbis.Core.Game
{
    /// <summary>Lotto del mondo: tile z14 (x, y) e ID del lotto dato dalla pipeline.</summary>
    public readonly struct LotKey : IEquatable<LotKey>
    {
        public readonly int TileX, TileY;
        public readonly uint LotId;

        public LotKey(int tileX, int tileY, uint lotId)
        {
            TileX = tileX;
            TileY = tileY;
            LotId = lotId;
        }

        public bool Equals(LotKey o) => TileX == o.TileX && TileY == o.TileY && LotId == o.LotId;
        public override bool Equals(object obj) => obj is LotKey k && Equals(k);
        public override int GetHashCode() => (TileX * 73856093) ^ (TileY * 19349663) ^ (int)LotId;
        public override string ToString() => TileX + "/" + TileY + "#" + LotId;
    }

    /// <summary>
    /// Elemento piazzato dal giocatore. La posizione è sulla griglia del lotto in mezzi metri
    /// (indipendente dalla dimensione della cella: 1 m per gli edifici, 0,5 m per le decorazioni).
    /// </summary>
    public sealed class PlacedObject
    {
        public int Id;
        public string ItemKey;
        public int Level = 1;
        public string Variant;
        public LotKey Lot;
        /// <summary>Angolo in basso a sinistra sulla griglia del lotto, in mezzi metri.</summary>
        public int HalfX, HalfY;
        /// <summary>Rotazione a passi di 90° (0–3).</summary>
        public int Rotation;
        public DateTime BuiltAt;
        /// <summary>Fine dei lavori (costruzione o potenziamento): prima di allora non rende.</summary>
        public DateTime ReadyAt;

        public bool IsReady(DateTime now) => now >= ReadyAt;

        public PlacedObject Clone() => (PlacedObject)MemberwiseClone();
    }

    /// <summary>Configurazione del mondo (docs/00 §14); in M2 contano difficoltà, tempo ad app chiusa e modalità creativa.</summary>
    public sealed class WorldConfig
    {
        public Difficulty Difficulty = Difficulty.Normal;
        public bool OfflineTime = true;
        public bool CreativeMode;
        public int CitizenReactions = 1; // 0 disattivate, 1 leggere, 2 realistiche
        public bool LifeCycle = true;
        public int Crime = 1;
        public int Disasters;
        public int RandomEvents = 1;
        public bool RealSportNames = true;
        public bool RealWeather = true;
    }

    public enum LedgerCategory
    {
        ResidentialTax, CommercialTax, IndustrialTax, Rent, PublicLand, Production, EnergySales, Missions, LevelRewards,
        Construction, Upgrades, Demolition, Moves, Variants, Maintenance, Services, EnergyPurchase,
        /// <summary>Biglietti delle linee del comune (M4).</summary>
        TransitFares,
        /// <summary>Servizio delle linee del comune: ore dei mezzi e fermate (M4).</summary>
        TransitCosts,
        Count,
    }

    /// <summary>Edificio reale (OSM) potenziato: livello 2→5, superficie dei piani e lotto (per ridisegnare il tile).</summary>
    public sealed class OsmUpgrade
    {
        public int Level = 1;
        public float FloorArea;
        public LotKey Lot;
    }

    /// <summary>Movimenti di cassa di un giorno di gioco, per categoria (docs/03 §4).</summary>
    public sealed class LedgerDay
    {
        public DateTime Date;
        public readonly long[] Amounts = new long[(int)LedgerCategory.Count];

        public long Income
        {
            get
            {
                long s = 0;
                foreach (var a in Amounts) if (a > 0) s += a;
                return s;
            }
        }

        public long Expenses
        {
            get
            {
                long s = 0;
                foreach (var a in Amounts) if (a < 0) s += a;
                return s;
            }
        }

        public long Net
        {
            get
            {
                long s = 0;
                foreach (var a in Amounts) s += a;
                return s;
            }
        }
    }

    /// <summary>Indicatori calcolati a fine giornata (per HUD, finanze e missioni).</summary>
    public sealed class CityStats
    {
        public int Population;
        public int Jobs;
        public float PowerBalanceKw;
        public float Happiness;
        public long LastDayNet;
        public int PositiveDaysStreak;
    }

    /// <summary>
    /// Stato completo di una partita (docs/01 §7): si salvano solo le differenze rispetto al mondo reale
    /// (elementi piazzati, edifici OSM demoliti) più economia, progressi e configurazione.
    /// </summary>
    public sealed class GameState
    {
        public const int FormatVersion = 4; // 2: edifici reali potenziati; 3: aggiustamento dei servizi (M4); 4: strade e linee del sindaco

        public string Name = "Chieti";
        public string Region = "chieti";
        public int Seed;
        public DateTime CreatedUtc;
        public DateTime LastPlayedUtc;
        public WorldConfig Config = new WorldConfig();

        public DateTime Now;
        public long Cash;
        public long StartCash;
        public long Xp;
        public int Level = 1;
        public float ResidentialTax, CommercialTax, IndustrialTax;
        /// <summary>
        /// Euro al giorno aggiunti al costo dei servizi della città esistente: fissati quando arrivano i cittadini veri
        /// (M4), così la partita mantiene il saldo che aveva con la città stimata.
        /// </summary>
        public double ServicesAdjustPerDay;

        public readonly SortedDictionary<int, PlacedObject> Objects = new SortedDictionary<int, PlacedObject>();
        public int NextObjectId = 1;
        /// <summary>Edifici OSM demoliti: chiave (tipo &lt;&lt; 60 | id) → lotto, per liberarne la sagoma.</summary>
        public readonly Dictionary<long, LotKey> DemolishedOsm = new Dictionary<long, LotKey>();

        /// <summary>Edifici reali potenziati (chiave come <see cref="DemolishedOsm"/>): gli addobbi cambiano col livello.</summary>
        public readonly Dictionary<long, OsmUpgrade> OsmUpgrades = new Dictionary<long, OsmUpgrade>();

        /// <summary>Livello di un edificio reale (1 se mai potenziato).</summary>
        public int OsmLevel(long key) => OsmUpgrades.TryGetValue(key, out var u) ? u.Level : 1;

        /// <summary>Strade disegnate dal sindaco (M4).</summary>
        public readonly SortedDictionary<int, CityRoad> Roads = new SortedDictionary<int, CityRoad>();
        /// <summary>Linee del trasporto pubblico del comune (M4).</summary>
        public readonly SortedDictionary<int, CityLine> Lines = new SortedDictionary<int, CityLine>();
        /// <summary>Prossimo ID per strade e linee.</summary>
        public int NextInfraId = 1;

        public readonly List<LedgerDay> Ledger = new List<LedgerDay>();
        public readonly CityStats Stats = new CityStats();

        public int MissionIndex;
        public readonly HashSet<string> CompletedMissions = new HashSet<string>();
        /// <summary>Contatori per le missioni (piazzati per categoria, potenziamenti, demolizioni…).</summary>
        public readonly Dictionary<string, long> Counters = new Dictionary<string, long>();

        public static long OsmKey(Orbis.Core.World.Fb.OsmType type, long id) => ((long)type << 60) | id;

        public long Counter(string key) => Counters.TryGetValue(key, out var v) ? v : 0;

        public void AddCounter(string key, long delta)
        {
            Counters[key] = Counter(key) + delta;
        }

        /// <summary>Unico punto in cui cambia la cassa: ogni movimento finisce nel registro del giorno.</summary>
        public void Transact(LedgerCategory category, long amount)
        {
            if (amount == 0) return;
            Cash += amount;
            Today().Amounts[(int)category] += amount;
        }

        public LedgerDay Today()
        {
            var date = Now.Date;
            if (Ledger.Count > 0 && Ledger[Ledger.Count - 1].Date == date) return Ledger[Ledger.Count - 1];
            var d = new LedgerDay { Date = date };
            Ledger.Add(d);
            if (Ledger.Count > 400) Ledger.RemoveAt(0);
            return d;
        }

        /// <summary>Somma di tutti i movimenti registrati (per i test di conservazione del denaro).</summary>
        public long LedgerTotal
        {
            get
            {
                long s = 0;
                foreach (var d in Ledger) s += d.Net;
                return s;
            }
        }

        // ------------------------------------------------------------------ serializzazione

        public void Write(BinaryWriter w)
        {
            w.Write(FormatVersion);
            w.Write(Name ?? "");
            w.Write(Region ?? "");
            w.Write(Seed);
            w.Write(CreatedUtc.Ticks);
            w.Write(LastPlayedUtc.Ticks);
            w.Write((int)Config.Difficulty);
            w.Write(Config.OfflineTime);
            w.Write(Config.CreativeMode);
            w.Write(Config.CitizenReactions);
            w.Write(Config.LifeCycle);
            w.Write(Config.Crime);
            w.Write(Config.Disasters);
            w.Write(Config.RandomEvents);
            w.Write(Config.RealSportNames);
            w.Write(Config.RealWeather);
            w.Write(Now.Ticks);
            w.Write(Cash);
            w.Write(StartCash);
            w.Write(Xp);
            w.Write(Level);
            w.Write(ResidentialTax);
            w.Write(CommercialTax);
            w.Write(IndustrialTax);
            w.Write(NextObjectId);
            w.Write(Objects.Count);
            foreach (var o in Objects.Values) WriteObject(w, o);
            w.Write(DemolishedOsm.Count);
            foreach (var kv in DemolishedOsm)
            {
                w.Write(kv.Key);
                WriteLot(w, kv.Value);
            }
            w.Write(OsmUpgrades.Count);
            foreach (var kv in OsmUpgrades)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Level);
                w.Write(kv.Value.FloorArea);
                WriteLot(w, kv.Value.Lot);
            }
            w.Write(Ledger.Count);
            foreach (var d in Ledger)
            {
                w.Write(d.Date.Ticks);
                w.Write(d.Amounts.Length);
                foreach (var a in d.Amounts) w.Write(a);
            }
            w.Write(Stats.Population);
            w.Write(Stats.Jobs);
            w.Write(Stats.PowerBalanceKw);
            w.Write(Stats.Happiness);
            w.Write(Stats.LastDayNet);
            w.Write(Stats.PositiveDaysStreak);
            w.Write(ServicesAdjustPerDay);
            w.Write(MissionIndex);
            w.Write(CompletedMissions.Count);
            foreach (var m in CompletedMissions) w.Write(m);
            w.Write(Counters.Count);
            foreach (var kv in Counters)
            {
                w.Write(kv.Key);
                w.Write(kv.Value);
            }
            w.Write(NextInfraId);
            w.Write(Roads.Count);
            foreach (var r in Roads.Values) r.Write(w);
            w.Write(Lines.Count);
            foreach (var l in Lines.Values) l.Write(w);
        }

        public static GameState Read(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version < 1 || version > FormatVersion) throw new InvalidDataException("versione del salvataggio non supportata: " + version);
            var s = new GameState
            {
                Name = r.ReadString(),
                Region = r.ReadString(),
                Seed = r.ReadInt32(),
                CreatedUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
                LastPlayedUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            };
            s.Config.Difficulty = (Difficulty)r.ReadInt32();
            s.Config.OfflineTime = r.ReadBoolean();
            s.Config.CreativeMode = r.ReadBoolean();
            s.Config.CitizenReactions = r.ReadInt32();
            s.Config.LifeCycle = r.ReadBoolean();
            s.Config.Crime = r.ReadInt32();
            s.Config.Disasters = r.ReadInt32();
            s.Config.RandomEvents = r.ReadInt32();
            s.Config.RealSportNames = r.ReadBoolean();
            s.Config.RealWeather = r.ReadBoolean();
            s.Now = new DateTime(r.ReadInt64());
            s.Cash = r.ReadInt64();
            s.StartCash = r.ReadInt64();
            s.Xp = r.ReadInt64();
            s.Level = r.ReadInt32();
            s.ResidentialTax = r.ReadSingle();
            s.CommercialTax = r.ReadSingle();
            s.IndustrialTax = r.ReadSingle();
            s.NextObjectId = r.ReadInt32();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var o = ReadObject(r);
                s.Objects[o.Id] = o;
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long k = r.ReadInt64();
                s.DemolishedOsm[k] = ReadLot(r);
            }
            if (version >= 2)
            {
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    long k = r.ReadInt64();
                    s.OsmUpgrades[k] = new OsmUpgrade { Level = r.ReadInt32(), FloorArea = r.ReadSingle(), Lot = ReadLot(r) };
                }
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var d = new LedgerDay { Date = new DateTime(r.ReadInt64()) };
                int m = r.ReadInt32();
                for (int k = 0; k < m; k++)
                {
                    long a = r.ReadInt64();
                    if (k < d.Amounts.Length) d.Amounts[k] = a;
                }
                s.Ledger.Add(d);
            }
            s.Stats.Population = r.ReadInt32();
            s.Stats.Jobs = r.ReadInt32();
            s.Stats.PowerBalanceKw = r.ReadSingle();
            s.Stats.Happiness = r.ReadSingle();
            s.Stats.LastDayNet = r.ReadInt64();
            s.Stats.PositiveDaysStreak = r.ReadInt32();
            if (version >= 3) s.ServicesAdjustPerDay = r.ReadDouble();
            s.MissionIndex = r.ReadInt32();
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) s.CompletedMissions.Add(r.ReadString());
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) s.Counters[r.ReadString()] = r.ReadInt64();
            if (version >= 4)
            {
                s.NextInfraId = r.ReadInt32();
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var road = CityRoad.Read(r);
                    s.Roads[road.Id] = road;
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var line = CityLine.Read(r);
                    s.Lines[line.Id] = line;
                }
            }
            return s;
        }

        public static void WriteObject(BinaryWriter w, PlacedObject o)
        {
            w.Write(o.Id);
            w.Write(o.ItemKey ?? "");
            w.Write(o.Level);
            w.Write(o.Variant ?? "");
            WriteLot(w, o.Lot);
            w.Write(o.HalfX);
            w.Write(o.HalfY);
            w.Write(o.Rotation);
            w.Write(o.BuiltAt.Ticks);
            w.Write(o.ReadyAt.Ticks);
        }

        public static PlacedObject ReadObject(BinaryReader r) => new PlacedObject
        {
            Id = r.ReadInt32(),
            ItemKey = r.ReadString(),
            Level = r.ReadInt32(),
            Variant = r.ReadString(),
            Lot = ReadLot(r),
            HalfX = r.ReadInt32(),
            HalfY = r.ReadInt32(),
            Rotation = r.ReadInt32(),
            BuiltAt = new DateTime(r.ReadInt64()),
            ReadyAt = new DateTime(r.ReadInt64()),
        };

        public static void WriteLot(BinaryWriter w, LotKey k)
        {
            w.Write(k.TileX);
            w.Write(k.TileY);
            w.Write(k.LotId);
        }

        public static LotKey ReadLot(BinaryReader r) => new LotKey(r.ReadInt32(), r.ReadInt32(), r.ReadUInt32());

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms)) Write(w);
                return ms.ToArray();
            }
        }

        public static GameState FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
                return Read(r);
        }
    }
}
