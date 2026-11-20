using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Core.Population;

namespace Orbis.Core.Game
{
    /// <summary>Istantanea giornaliera dello stato (docs/01 §5) per il riavvolgimento.</summary>
    public sealed class Snapshot
    {
        public DateTime GameTime;
        public long Cash;
        public int Level;
        public byte[] Data;
    }

    /// <summary>Istantanee: le ultime N giornaliere più una a settimana (lunedì) per le settimane indicate.</summary>
    public sealed class SnapshotStore
    {
        public readonly List<Snapshot> Items = new List<Snapshot>();

        public void Take(GameState s, int daily, int weeklyWeeks)
        {
            Items.Add(new Snapshot { GameTime = s.Now, Cash = s.Cash, Level = s.Level, Data = s.ToBytes() });
            Prune(daily, weeklyWeeks);
        }

        private void Prune(int daily, int weeklyWeeks)
        {
            if (Items.Count == 0) return;
            var latest = Items[Items.Count - 1].GameTime;
            var keep = new List<Snapshot>();
            for (int i = 0; i < Items.Count; i++)
            {
                var sn = Items[i];
                bool recent = i >= Items.Count - daily;
                bool weekly = sn.GameTime.DayOfWeek == DayOfWeek.Monday && (latest - sn.GameTime).TotalDays <= weeklyWeeks * 7;
                if (recent || weekly) keep.Add(sn);
            }
            Items.Clear();
            Items.AddRange(keep);
        }

        /// <summary>Toglie le istantanee successive a quella scelta (dopo un riavvolgimento).</summary>
        public void TruncateAfter(DateTime gameTime) => Items.RemoveAll(x => x.GameTime > gameTime);

        public void Write(BinaryWriter w)
        {
            w.Write(Items.Count);
            foreach (var s in Items)
            {
                w.Write(s.GameTime.Ticks);
                w.Write(s.Cash);
                w.Write(s.Level);
                w.Write(s.Data.Length);
                w.Write(s.Data);
            }
        }

        public void Read(BinaryReader r)
        {
            Items.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var s = new Snapshot { GameTime = new DateTime(r.ReadInt64()), Cash = r.ReadInt64(), Level = r.ReadInt32() };
                int len = r.ReadInt32();
                s.Data = r.ReadBytes(len);
                Items.Add(s);
            }
        }
    }

    /// <summary>Riepilogo del tempo passato ad app chiusa (docs/01 §4).</summary>
    public sealed class OfflineSummary
    {
        public TimeSpan RealElapsed;
        public TimeSpan GameElapsed;
        public long Income;
        public long Expenses;
        public int ConstructionsDone;
        public int MissionsDone;
        public int LevelsGained;
    }

    /// <summary>
    /// Partita in corso: stato, catalogo, bilanciamento, comandi con annulla/ripeti, simulazione, istantanee.
    /// Tutta logica pura: Unity la guida (tempo reale, input, salvataggio su disco) e la disegna.
    /// </summary>
    public sealed class GameSession
    {
        public const double GameSecondsPerRealSecond = 60.0; // 1 ora di gioco = 1 minuto reale

        public readonly GameContext Context;
        public readonly CommandStack Commands = new CommandStack();
        public readonly Simulation Simulation = new Simulation();
        public readonly SnapshotStore Snapshots = new SnapshotStore();

        public GameState State => Context.State;

        /// <summary>
        /// Istantanee dei cittadini per il riavvolgimento: solo in memoria e solo gli ultimi giorni (la popolazione
        /// pesa qualche MB). Riavvolgendo più indietro, i cittadini restano come sono adesso.
        /// </summary>
        public readonly List<(DateTime GameTime, byte[] Data)> PopulationSnapshots = new List<(DateTime, byte[])>();
        public const int PopulationSnapshotDays = 7;

        public CityPopulation Population => Context.Population;

        public GameSession(GameContext ctx)
        {
            Context = ctx;
            Simulation.Midnight += c =>
            {
                Snapshots.Take(c.State, c.Balance.SnapshotsDaily, c.Balance.SnapshotsWeeklyWeeks);
                TakePopulationSnapshot();
            };
        }

        private void TakePopulationSnapshot()
        {
            if (Context.Population == null) return;
            PopulationSnapshots.Add((State.Now, PopulationSerializer.ToBytes(Context.Population)));
            while (PopulationSnapshots.Count > PopulationSnapshotDays) PopulationSnapshots.RemoveAt(0);
        }

        /// <summary>Collega i cittadini alla partita (nuova partita o salvataggio di prima di M4).</summary>
        /// <param name="keepBalance">la città vera prende il posto di quella stimata con lo stesso saldo giornaliero
        /// (si aggiusta una volta il costo dei servizi); false quando si ricarica un salvataggio</param>
        public void AttachPopulation(CityPopulation population, LifeInputs life, bool keepBalance = false)
        {
            double before = keepBalance ? Economy.Compute(Context).Net : 0;
            Context.Population = population;
            Context.Life = population != null && life != null ? new CityLife(life) : null;
            if (keepBalance && population != null) State.ServicesAdjustPerDay += Economy.Compute(Context).Net - before;
            if (population != null && population.LastDay == 0) population.LastDay = CityLife.DayIndex(State.Now) - 1;
            PopulationSnapshots.Clear();
            TakePopulationSnapshot();
        }

        /// <summary>Nuova partita: inizia alla data reale (docs/03 §1) con la cassa e le aliquote predefinite.</summary>
        public static GameSession New(string name, WorldConfig config, DateTime startGameTime, DateTime nowUtc, int seed,
            Catalog catalog, Balance balance, IWorldAccess world, Missions missions)
        {
            var s = new GameState
            {
                Name = name, Config = config ?? new WorldConfig(), Seed = seed, CreatedUtc = nowUtc, LastPlayedUtc = nowUtc,
                Now = startGameTime,
                ResidentialTax = balance.ResidentialTax.Default,
                CommercialTax = balance.CommercialTax.Default,
                IndustrialTax = balance.IndustrialTax.Default,
            };
            s.StartCash = balance.StartCash;
            s.Cash = balance.StartCash;
            var session = new GameSession(new GameContext(s, catalog, balance, world, missions));
            var f = Economy.Compute(session.Context);
            s.Stats.Population = f.Population;
            s.Stats.Jobs = f.CommercialJobs + f.IndustrialJobs;
            s.Stats.Happiness = f.Happiness;
            session.Snapshots.Take(s, balance.SnapshotsDaily, balance.SnapshotsWeeklyWeeks);
            return session;
        }

        public BuildResult Do(IGameCommand cmd) => Commands.Do(Context, cmd);
        public bool Undo() => Commands.Undo(Context);
        public BuildResult Redo() => Commands.Redo(Context);

        public List<SimEvent> Advance(TimeSpan gameTime) => Simulation.Advance(Context, gameTime);

        /// <summary>Riavvolge all'istantanea: tutto ciò che è successo dopo si perde (con conferma nella UI).</summary>
        public void Rewind(Snapshot snapshot)
        {
            var restored = GameState.FromBytes(snapshot.Data);
            restored.LastPlayedUtc = State.LastPlayedUtc;
            Context.State = restored;
            if (Context.Population != null)
            {
                int i = PopulationSnapshots.FindIndex(p => p.GameTime == snapshot.GameTime);
                if (i >= 0)
                {
                    Context.Population = PopulationSerializer.FromBytes(PopulationSnapshots[i].Data);
                    PopulationSnapshots.RemoveRange(i + 1, PopulationSnapshots.Count - i - 1);
                }
                else
                {
                    // istantanea troppo vecchia: i cittadini restano come sono e riprendono a vivere dal giorno riavvolto
                    Context.Population.LastDay = CityLife.DayIndex(restored.Now) - 1;
                    PopulationSnapshots.Clear();
                }
            }
            Commands.Clear();
            Snapshots.TruncateAfter(snapshot.GameTime);
        }

        /// <summary>Simulazione riassuntiva del tempo passato ad app chiusa (fino al limite configurato).</summary>
        public OfflineSummary CatchUp(DateTime nowUtc)
        {
            var s = State;
            if (!s.Config.OfflineTime || s.LastPlayedUtc == default || nowUtc <= s.LastPlayedUtc) return null;
            var real = nowUtc - s.LastPlayedUtc;
            var max = TimeSpan.FromHours(Context.Balance.OfflineMaxRealHours);
            if (real > max) real = max;
            if (real.TotalMinutes < 1) return null;
            var game = TimeSpan.FromSeconds(real.TotalSeconds * GameSecondsPerRealSecond);
            int levelBefore = s.Level;
            long cashBefore = s.Cash;
            var startDay = s.Now.Date;
            var events = Advance(game);
            var summary = new OfflineSummary { RealElapsed = real, GameElapsed = game, LevelsGained = s.Level - levelBefore };
            foreach (var d in s.Ledger)
            {
                if (d.Date < startDay) continue;
                summary.Income += d.Income;
                summary.Expenses += d.Expenses;
            }
            foreach (var e in events)
            {
                if (e.Kind == SimEventKind.ConstructionDone) summary.ConstructionsDone++;
                if (e.Kind == SimEventKind.MissionDone) summary.MissionsDone++;
            }
            s.LastPlayedUtc = nowUtc;
            return summary;
        }

        // ------------------------------------------------------------------ salvataggio

        private static readonly byte[] Magic = { (byte)'O', (byte)'S', (byte)'A', (byte)'V' };
        public const int SaveVersion = 2; // 2: cittadini (M4)

        public byte[] Save(DateTime nowUtc)
        {
            State.LastPlayedUtc = nowUtc;
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(Magic);
                    w.Write(SaveVersion);
                    State.Write(w);
                    Commands.Write(w);
                    Snapshots.Write(w);
                    w.Write(Context.Population != null);
                    if (Context.Population != null) PopulationSerializer.Write(w, Context.Population);
                }
                return ms.ToArray();
            }
        }

        /// <param name="life">dati per far vivere i cittadini del salvataggio (null = restano fermi)</param>
        public static GameSession Load(byte[] data, Catalog catalog, Balance balance, IWorldAccess world, Missions missions,
            LifeInputs life = null)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                var magic = r.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                    throw new InvalidDataException("non è un salvataggio di Orbis");
                int version = r.ReadInt32();
                if (version < 1 || version > SaveVersion) throw new InvalidDataException("versione del salvataggio non supportata: " + version);
                var state = GameState.Read(r);
                var session = new GameSession(new GameContext(state, catalog, balance, world, missions));
                session.Commands.Read(r);
                session.Snapshots.Read(r);
                if (version >= 2 && r.ReadBoolean()) session.AttachPopulation(PopulationSerializer.Read(r), life);
                return session;
            }
        }
    }
}
