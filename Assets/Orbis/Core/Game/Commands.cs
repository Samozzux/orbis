using System;
using System.Collections.Generic;
using System.IO;

namespace Orbis.Core.Game
{
    /// <summary>
    /// Azione del giocatore (docs/01 §5): esegue, annulla e si salva. L'annullamento rimborsa i costi diretti;
    /// XP, premi di livello e missioni ottenuti con l'azione li toglie <see cref="CommandStack"/>.
    /// </summary>
    public interface IGameCommand
    {
        byte Tag { get; }
        BuildResult Execute(GameContext ctx);
        void Undo(GameContext ctx);
        void Write(BinaryWriter w);
    }

    public sealed class PlaceCommand : IGameCommand
    {
        public string Item;
        public string Variant;
        public LotKey Lot;
        public int HalfX, HalfY, Rotation;
        private int _id;
        private long _cost;
        private int _prevNextId;

        public byte Tag => 1;

        public BuildResult Execute(GameContext ctx)
        {
            var item = ctx.Catalog.Get(Item);
            var r = Placement.CanBuild(ctx, item, Variant, Lot, HalfX, HalfY, Rotation);
            if (r != BuildResult.Ok) return r;
            var s = ctx.State;
            _prevNextId = s.NextObjectId;
            if (_id == 0) _id = s.NextObjectId;
            s.NextObjectId = Math.Max(s.NextObjectId, _id + 1);
            var spec = item.LevelSpec(1);
            s.Objects[_id] = new PlacedObject
            {
                Id = _id, ItemKey = Item, Level = 1, Variant = Variant ?? (item.Variants.Count > 0 ? item.Variants[0].Key : ""),
                Lot = Lot, HalfX = HalfX, HalfY = HalfY, Rotation = Rotation & 3,
                BuiltAt = s.Now, ReadyAt = s.Now.AddHours(s.Config.CreativeMode ? 0 : spec.BuildHours),
            };
            _cost = s.Config.CreativeMode ? 0 : Placement.CostOf(item, Variant);
            s.Transact(LedgerCategory.Construction, -_cost);
            Progression.AddXp(ctx, spec.Xp >= 0 ? spec.Xp : (long)Math.Round(_cost * ctx.Balance.XpPerCost) + 5);
            s.AddCounter("placed", 1);
            s.AddCounter("placed:" + item.Category, 1);
            s.AddCounter("placed:" + item.Key, 1);
            foreach (var t in item.Tags) s.AddCounter("tag:" + t, 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            var s = ctx.State;
            var item = ctx.Catalog.Get(Item);
            s.Objects.Remove(_id);
            s.NextObjectId = _prevNextId;
            s.Transact(LedgerCategory.Construction, _cost);
            s.AddCounter("placed", -1);
            if (item != null)
            {
                s.AddCounter("placed:" + item.Category, -1);
                s.AddCounter("placed:" + item.Key, -1);
                foreach (var t in item.Tags) s.AddCounter("tag:" + t, -1);
            }
        }

        /// <summary>ID dell'elemento creato (dopo Execute).</summary>
        public int ObjectId => _id;

        public void Write(BinaryWriter w)
        {
            w.Write(Item ?? "");
            w.Write(Variant ?? "");
            GameState.WriteLot(w, Lot);
            w.Write(HalfX);
            w.Write(HalfY);
            w.Write(Rotation);
            w.Write(_id);
            w.Write(_cost);
            w.Write(_prevNextId);
        }

        public static PlaceCommand Read(BinaryReader r) => new PlaceCommand
        {
            Item = r.ReadString(), Variant = r.ReadString(), Lot = GameState.ReadLot(r), HalfX = r.ReadInt32(), HalfY = r.ReadInt32(),
            Rotation = r.ReadInt32(), _id = r.ReadInt32(), _cost = r.ReadInt64(), _prevNextId = r.ReadInt32(),
        };
    }

    /// <summary>Sposta e/o ruota un elemento (stesso lotto o un altro).</summary>
    public sealed class MoveCommand : IGameCommand
    {
        public int ObjectId;
        public LotKey Lot;
        public int HalfX, HalfY, Rotation;
        private LotKey _oldLot;
        private int _oldX, _oldY, _oldRot;
        private long _cost;

        public byte Tag => 2;

        public BuildResult Execute(GameContext ctx)
        {
            var s = ctx.State;
            if (!s.Objects.TryGetValue(ObjectId, out var o)) return BuildResult.UnknownObject;
            var item = ctx.Catalog.Get(o.ItemKey);
            if (item == null) return BuildResult.UnknownItem;
            _cost = s.Config.CreativeMode ? 0 : (long)Math.Round(Placement.CostOf(item, o.Variant) * ctx.Balance.MoveCostRatio);
            if (s.Cash < _cost) return BuildResult.NotEnoughCash;
            var r = Placement.CheckSpace(ctx, item, Lot, HalfX, HalfY, Rotation, ObjectId);
            if (r != BuildResult.Ok) return r;
            _oldLot = o.Lot;
            _oldX = o.HalfX;
            _oldY = o.HalfY;
            _oldRot = o.Rotation;
            o.Lot = Lot;
            o.HalfX = HalfX;
            o.HalfY = HalfY;
            o.Rotation = Rotation & 3;
            s.Transact(LedgerCategory.Moves, -_cost);
            s.AddCounter("moved", 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            if (!ctx.State.Objects.TryGetValue(ObjectId, out var o)) return;
            o.Lot = _oldLot;
            o.HalfX = _oldX;
            o.HalfY = _oldY;
            o.Rotation = _oldRot;
            ctx.State.Transact(LedgerCategory.Moves, _cost);
            ctx.State.AddCounter("moved", -1);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(ObjectId);
            GameState.WriteLot(w, Lot);
            w.Write(HalfX);
            w.Write(HalfY);
            w.Write(Rotation);
            GameState.WriteLot(w, _oldLot);
            w.Write(_oldX);
            w.Write(_oldY);
            w.Write(_oldRot);
            w.Write(_cost);
        }

        public static MoveCommand Read(BinaryReader r) => new MoveCommand
        {
            ObjectId = r.ReadInt32(), Lot = GameState.ReadLot(r), HalfX = r.ReadInt32(), HalfY = r.ReadInt32(), Rotation = r.ReadInt32(),
            _oldLot = GameState.ReadLot(r), _oldX = r.ReadInt32(), _oldY = r.ReadInt32(), _oldRot = r.ReadInt32(), _cost = r.ReadInt64(),
        };
    }

    /// <summary>Demolisce un elemento piazzato (rimborso parziale).</summary>
    public sealed class DemolishCommand : IGameCommand
    {
        public int ObjectId;
        private PlacedObject _removed;
        private long _refund;

        public byte Tag => 3;

        public BuildResult Execute(GameContext ctx)
        {
            var s = ctx.State;
            if (!s.Objects.TryGetValue(ObjectId, out var o)) return BuildResult.UnknownObject;
            var item = ctx.Catalog.Get(o.ItemKey);
            _removed = o.Clone();
            _refund = item == null || s.Config.CreativeMode ? 0 : (long)Math.Round(Placement.CostOf(item, o.Variant) * ctx.Balance.DemolishRefundRatio);
            s.Objects.Remove(ObjectId);
            s.Transact(LedgerCategory.Demolition, _refund);
            s.AddCounter("demolished", 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            ctx.State.Objects[_removed.Id] = _removed.Clone();
            ctx.State.Transact(LedgerCategory.Demolition, -_refund);
            ctx.State.AddCounter("demolished", -1);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(ObjectId);
            w.Write(_removed != null);
            if (_removed != null) GameState.WriteObject(w, _removed);
            w.Write(_refund);
        }

        public static DemolishCommand Read(BinaryReader r)
        {
            var c = new DemolishCommand { ObjectId = r.ReadInt32() };
            if (r.ReadBoolean()) c._removed = GameState.ReadObject(r);
            c._refund = r.ReadInt64();
            return c;
        }
    }

    /// <summary>Demolisce un edificio reale di OpenStreetMap: ne libera esattamente la sagoma (docs/04 §4).</summary>
    public sealed class DemolishOsmCommand : IGameCommand
    {
        public long OsmKey;
        public LotKey Lot;
        public float AreaM2;
        private long _cost;

        public byte Tag => 4;

        public BuildResult Execute(GameContext ctx)
        {
            var s = ctx.State;
            if (s.DemolishedOsm.ContainsKey(OsmKey)) return BuildResult.UnknownObject;
            _cost = s.Config.CreativeMode ? 0 : (long)Math.Round(AreaM2 * ctx.Balance.DemolishOsmCostPerM2);
            if (s.Cash < _cost) return BuildResult.NotEnoughCash;
            s.DemolishedOsm[OsmKey] = Lot;
            s.Transact(LedgerCategory.Demolition, -_cost);
            s.AddCounter("demolishedOsm", 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            ctx.State.DemolishedOsm.Remove(OsmKey);
            ctx.State.Transact(LedgerCategory.Demolition, _cost);
            ctx.State.AddCounter("demolishedOsm", -1);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(OsmKey);
            GameState.WriteLot(w, Lot);
            w.Write(AreaM2);
            w.Write(_cost);
        }

        public static DemolishOsmCommand Read(BinaryReader r) => new DemolishOsmCommand
        {
            OsmKey = r.ReadInt64(), Lot = GameState.ReadLot(r), AreaM2 = r.ReadSingle(), _cost = r.ReadInt64(),
        };
    }

    /// <summary>Potenzia un elemento al livello successivo (1→5).</summary>
    public sealed class UpgradeCommand : IGameCommand
    {
        public int ObjectId;
        private long _cost;
        private DateTime _oldReady;

        public byte Tag => 5;

        public BuildResult Execute(GameContext ctx)
        {
            var s = ctx.State;
            if (!s.Objects.TryGetValue(ObjectId, out var o)) return BuildResult.UnknownObject;
            var item = ctx.Catalog.Get(o.ItemKey);
            if (item == null) return BuildResult.UnknownItem;
            if (o.Level >= item.MaxLevel) return BuildResult.MaxLevel;
            if (!o.IsReady(s.Now) && !s.Config.CreativeMode) return BuildResult.NotReady;
            var next = item.LevelSpec(o.Level + 1);
            _cost = s.Config.CreativeMode ? 0 : Placement.CostOf(item, o.Variant, o.Level + 1);
            if (s.Cash < _cost) return BuildResult.NotEnoughCash;
            _oldReady = o.ReadyAt;
            o.Level++;
            o.ReadyAt = s.Now.AddHours(s.Config.CreativeMode ? 0 : next.BuildHours);
            s.Transact(LedgerCategory.Upgrades, -_cost);
            Progression.AddXp(ctx, next.Xp >= 0 ? next.Xp : (long)Math.Round(_cost * ctx.Balance.XpPerCost) + 5);
            s.AddCounter("upgraded", 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            if (!ctx.State.Objects.TryGetValue(ObjectId, out var o)) return;
            o.Level--;
            o.ReadyAt = _oldReady;
            ctx.State.Transact(LedgerCategory.Upgrades, _cost);
            ctx.State.AddCounter("upgraded", -1);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(ObjectId);
            w.Write(_cost);
            w.Write(_oldReady.Ticks);
        }

        public static UpgradeCommand Read(BinaryReader r) => new UpgradeCommand
        {
            ObjectId = r.ReadInt32(), _cost = r.ReadInt64(), _oldReady = new DateTime(r.ReadInt64()),
        };
    }

    /// <summary>Cambia variante (colori, materiali) di un elemento.</summary>
    public sealed class VariantCommand : IGameCommand
    {
        public int ObjectId;
        public string Variant;
        private string _old;
        private long _cost;

        public byte Tag => 6;

        public BuildResult Execute(GameContext ctx)
        {
            var s = ctx.State;
            if (!s.Objects.TryGetValue(ObjectId, out var o)) return BuildResult.UnknownObject;
            var item = ctx.Catalog.Get(o.ItemKey);
            if (item == null || item.Variant(Variant) == null) return BuildResult.UnknownItem;
            _cost = s.Config.CreativeMode ? 0 : (long)Math.Round(Placement.CostOf(item, Variant) * ctx.Balance.VariantCostRatio);
            if (s.Cash < _cost) return BuildResult.NotEnoughCash;
            _old = o.Variant;
            o.Variant = Variant;
            s.Transact(LedgerCategory.Variants, -_cost);
            s.AddCounter("variants", 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            if (!ctx.State.Objects.TryGetValue(ObjectId, out var o)) return;
            o.Variant = _old;
            ctx.State.Transact(LedgerCategory.Variants, _cost);
            ctx.State.AddCounter("variants", -1);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(ObjectId);
            w.Write(Variant ?? "");
            w.Write(_old ?? "");
            w.Write(_cost);
        }

        public static VariantCommand Read(BinaryReader r) => new VariantCommand
        {
            ObjectId = r.ReadInt32(), Variant = r.ReadString(), _old = r.ReadString(), _cost = r.ReadInt64(),
        };
    }

    public enum TaxKind { Residential = 0, Commercial = 1, Industrial = 2 }

    /// <summary>Cambia un'aliquota (entro i limiti del bilanciamento).</summary>
    public sealed class SetTaxCommand : IGameCommand
    {
        public TaxKind Kind;
        public float Rate;
        private float _old;

        public byte Tag => 7;

        public BuildResult Execute(GameContext ctx)
        {
            var s = ctx.State;
            var range = Kind == TaxKind.Residential ? ctx.Balance.ResidentialTax : (Kind == TaxKind.Commercial ? ctx.Balance.CommercialTax : ctx.Balance.IndustrialTax);
            float v = Math.Max(range.Min, Math.Min(range.Max, Rate));
            _old = Get(s);
            Set(s, v);
            s.AddCounter("taxChanged", 1);
            return BuildResult.Ok;
        }

        public void Undo(GameContext ctx)
        {
            Set(ctx.State, _old);
            ctx.State.AddCounter("taxChanged", -1);
        }

        private float Get(GameState s) => Kind == TaxKind.Residential ? s.ResidentialTax : (Kind == TaxKind.Commercial ? s.CommercialTax : s.IndustrialTax);

        private void Set(GameState s, float v)
        {
            if (Kind == TaxKind.Residential) s.ResidentialTax = v;
            else if (Kind == TaxKind.Commercial) s.CommercialTax = v;
            else s.IndustrialTax = v;
        }

        public void Write(BinaryWriter w)
        {
            w.Write((int)Kind);
            w.Write(Rate);
            w.Write(_old);
        }

        public static SetTaxCommand Read(BinaryReader r) => new SetTaxCommand { Kind = (TaxKind)r.ReadInt32(), Rate = r.ReadSingle(), _old = r.ReadSingle() };
    }

    /// <summary>
    /// Pila annulla/ripeti illimitata e persistente (docs/01 §5). Registra anche gli effetti indiretti
    /// dell'azione (XP, livelli, premi, missioni) per toglierli all'annullamento: niente guadagni gratis.
    /// </summary>
    public sealed class CommandStack
    {
        private sealed class Entry
        {
            public IGameCommand Command;
            public long Xp;
            public int LevelBefore;
            public long Rewards; // premi di livello e missioni ottenuti
            public long MissionRewards;
            public int MissionIndexBefore;
            public List<string> Missions = new List<string>();
        }

        private readonly List<Entry> _undo = new List<Entry>();
        private readonly List<Entry> _redo = new List<Entry>();

        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public IGameCommand LastDone => _undo.Count > 0 ? _undo[_undo.Count - 1].Command : null;

        public event Action Changed;

        public BuildResult Do(GameContext ctx, IGameCommand cmd)
        {
            var e = Run(ctx, cmd);
            if (e == null) return _lastResult;
            _undo.Add(e);
            _redo.Clear();
            Changed?.Invoke();
            return BuildResult.Ok;
        }

        private BuildResult _lastResult;

        private Entry Run(GameContext ctx, IGameCommand cmd)
        {
            var s = ctx.State;
            long xp = s.Xp;
            int level = s.Level;
            int missionIndex = s.MissionIndex;
            long levelRewards = s.Today().Amounts[(int)LedgerCategory.LevelRewards];
            long missionRewards = s.Today().Amounts[(int)LedgerCategory.Missions];
            var completedBefore = new HashSet<string>(s.CompletedMissions);
            _lastResult = cmd.Execute(ctx);
            if (_lastResult != BuildResult.Ok) return null;
            ctx.Missions?.Evaluate(ctx);
            var e = new Entry
            {
                Command = cmd, Xp = s.Xp - xp, LevelBefore = level, MissionIndexBefore = missionIndex,
                Rewards = s.Today().Amounts[(int)LedgerCategory.LevelRewards] - levelRewards,
                MissionRewards = s.Today().Amounts[(int)LedgerCategory.Missions] - missionRewards,
            };
            foreach (var m in s.CompletedMissions)
                if (!completedBefore.Contains(m)) e.Missions.Add(m);
            return e;
        }

        public bool Undo(GameContext ctx)
        {
            if (_undo.Count == 0) return false;
            var e = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            var s = ctx.State;
            // prima gli effetti indiretti, poi l'azione
            s.Xp -= e.Xp;
            s.Level = e.LevelBefore;
            s.Transact(LedgerCategory.LevelRewards, -e.Rewards);
            s.Transact(LedgerCategory.Missions, -e.MissionRewards);
            foreach (var m in e.Missions) s.CompletedMissions.Remove(m);
            s.MissionIndex = e.MissionIndexBefore;
            e.Command.Undo(ctx);
            _redo.Add(e);
            Changed?.Invoke();
            return true;
        }

        public BuildResult Redo(GameContext ctx)
        {
            if (_redo.Count == 0) return BuildResult.UnknownObject;
            var cmd = _redo[_redo.Count - 1].Command;
            var e = Run(ctx, cmd);
            if (e == null) return _lastResult;
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(e);
            Changed?.Invoke();
            return BuildResult.Ok;
        }

        /// <summary>Dopo un riavvolgimento la storia ricomincia dall'istantanea.</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ salvataggio

        public void Write(BinaryWriter w)
        {
            WriteList(w, _undo);
            WriteList(w, _redo);
        }

        private static void WriteList(BinaryWriter w, List<Entry> list)
        {
            w.Write(list.Count);
            foreach (var e in list)
            {
                w.Write(e.Command.Tag);
                e.Command.Write(w);
                w.Write(e.Xp);
                w.Write(e.LevelBefore);
                w.Write(e.Rewards);
                w.Write(e.MissionRewards);
                w.Write(e.MissionIndexBefore);
                w.Write(e.Missions.Count);
                foreach (var m in e.Missions) w.Write(m);
            }
        }

        public void Read(BinaryReader r)
        {
            _undo.Clear();
            _redo.Clear();
            ReadList(r, _undo);
            ReadList(r, _redo);
        }

        private static void ReadList(BinaryReader r, List<Entry> list)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var e = new Entry { Command = ReadCommand(r) };
                e.Xp = r.ReadInt64();
                e.LevelBefore = r.ReadInt32();
                e.Rewards = r.ReadInt64();
                e.MissionRewards = r.ReadInt64();
                e.MissionIndexBefore = r.ReadInt32();
                int m = r.ReadInt32();
                for (int k = 0; k < m; k++) e.Missions.Add(r.ReadString());
                list.Add(e);
            }
        }

        public static IGameCommand ReadCommand(BinaryReader r)
        {
            byte tag = r.ReadByte();
            switch (tag)
            {
                case 1: return PlaceCommand.Read(r);
                case 2: return MoveCommand.Read(r);
                case 3: return DemolishCommand.Read(r);
                case 4: return DemolishOsmCommand.Read(r);
                case 5: return UpgradeCommand.Read(r);
                case 6: return VariantCommand.Read(r);
                case 7: return SetTaxCommand.Read(r);
                default: throw new InvalidDataException("comando sconosciuto nel salvataggio: " + tag);
            }
        }
    }
}
