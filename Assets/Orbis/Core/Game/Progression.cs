using System;
using System.Collections.Generic;
using Orbis.Core.Data;

namespace Orbis.Core.Game
{
    /// <summary>XP e livelli del giocatore (docs/00 §3): i livelli sbloccano elementi e danno monete.</summary>
    public static class Progression
    {
        public static void AddXp(GameContext ctx, long xp)
        {
            if (xp <= 0) return;
            var s = ctx.State;
            s.Xp += xp;
            var b = ctx.Balance;
            while (s.Level < b.MaxLevel && s.Xp >= b.XpForLevel(s.Level + 1))
            {
                s.Level++;
                s.Transact(LedgerCategory.LevelRewards, b.RewardPerLevel * s.Level);
            }
        }

        /// <summary>Avanzamento verso il livello successivo (0..1).</summary>
        public static float LevelProgress(GameState s, Balance b)
        {
            if (s.Level >= b.MaxLevel) return 1f;
            long from = b.XpForLevel(s.Level), to = b.XpForLevel(s.Level + 1);
            return to > from ? Math.Max(0f, Math.Min(1f, (float)(s.Xp - from) / (to - from))) : 1f;
        }
    }

    public sealed class MissionDef
    {
        public string Key;
        public string Title;
        public string Text;
        public string Type;
        public string Target;
        public long Count;
        public long RewardXp;
        public long RewardCash;
    }

    /// <summary>
    /// Missioni continue (docs/04: brevi, guidano verso nuove meccaniche): una alla volta, in ordine.
    /// Condizioni sullo stato attuale della partita; premi in XP e monete.
    /// </summary>
    public sealed class Missions
    {
        public readonly List<MissionDef> All = new List<MissionDef>();

        public static Missions Parse(string json)
        {
            var m = new Missions();
            foreach (var j in Json.Parse(json)["missions"].Items)
            {
                var c = j["condition"];
                m.All.Add(new MissionDef
                {
                    Key = j["key"].AsString(),
                    Title = j["title"].AsString(),
                    Text = j["text"].AsString(),
                    Type = c["type"].AsString(),
                    Target = c["target"].AsString(""),
                    Count = c["count"].AsLong(1),
                    RewardXp = j["reward"]["xp"].AsLong(),
                    RewardCash = j["reward"]["cash"].AsLong(),
                });
            }
            return m;
        }

        public MissionDef Current(GameState s) => s.MissionIndex >= 0 && s.MissionIndex < All.Count ? All[s.MissionIndex] : null;

        /// <summary>Valore attuale della condizione (per la barra di avanzamento).</summary>
        public static long Progress(GameState s, MissionDef m)
        {
            switch (m.Type)
            {
                case "placed": return string.IsNullOrEmpty(m.Target) ? s.Counter("placed") : s.Counter("placed:" + m.Target);
                case "tag": return s.Counter("tag:" + m.Target);
                case "upgraded": return s.Counter("upgraded");
                case "demolishedOsm": return s.Counter("demolishedOsm");
                case "moved": return s.Counter("moved");
                case "variants": return s.Counter("variants");
                case "taxChanged": return s.Counter("taxChanged");
                case "level": return s.Level;
                case "population": return s.Stats.Population;
                case "cash": return s.Cash;
                case "positiveDays": return s.Stats.PositiveDaysStreak;
                case "power": return (long)s.Stats.PowerBalanceKw;
                default: return 0;
            }
        }

        /// <summary>Completa la missione corrente (e le successive già soddisfatte) assegnando i premi.</summary>
        public int Evaluate(GameContext ctx)
        {
            var s = ctx.State;
            int done = 0;
            while (true)
            {
                var m = Current(s);
                if (m == null || Progress(s, m) < m.Count) return done;
                s.CompletedMissions.Add(m.Key);
                s.MissionIndex++;
                s.Transact(LedgerCategory.Missions, m.RewardCash);
                Progression.AddXp(ctx, m.RewardXp);
                done++;
            }
        }
    }
}
