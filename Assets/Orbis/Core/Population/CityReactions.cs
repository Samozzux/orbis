using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Core.Data;

namespace Orbis.Core.Population
{
    /// <summary>Tipi di reazione dei cittadini (docs/03 §6).</summary>
    public enum ReactionKind : byte { Comment = 0, Petition, PetitionAccepted, PetitionIgnored, Protest, Strike }

    /// <summary>Livello delle reazioni scelto nella configurazione del mondo (WorldConfig.CitizenReactions).</summary>
    public static class ReactionLevel
    {
        public const int Off = 0, Light = 1, Realistic = 2;
    }

    /// <summary>Parametri delle reazioni (sezione "reactions" di data/balance/services.json).</summary>
    public sealed class ReactionSpec
    {
        public int CommentsPerDay = 6, FeedSize = 60;
        public double ProductivityBase = 70, ProductivityPerPoint = 0.002;
        /// <summary>Sotto questa felicità un cittadino preoccupato firma e protesta.</summary>
        public int UnhappyBelow = 55;
        public double PetitionShare = 0.03, PetitionSignRate = 0.25, PetitionSolvedShare = 0.015;
        public int PetitionDays = 30;
        public double ProtestAverage = 55, ProtestIgnoredShare = 0.02, ProtestShare = 0.1;
        public int ProtestCooldownDays = 7;
        public double StrikeWork = 45, StrikeLoss = 0.3;
        public int StrikeDays = 3;
        public double EmigrationBase = 70, EmigrationScale = 15, EmigrationMin = 0.4, EmigrationMax = 4;
        public double ArrivalsScale = 20, ArrivalsMin = 0.5, ArrivalsMax = 2;

        public static ReactionSpec Parse(JsonValue j)
        {
            var s = new ReactionSpec();
            s.CommentsPerDay = j["commentsPerDay"].AsInt(s.CommentsPerDay);
            s.FeedSize = Math.Max(1, j["feedSize"].AsInt(s.FeedSize));
            s.ProductivityBase = j["productivityBase"].AsDouble(s.ProductivityBase);
            s.ProductivityPerPoint = j["productivityPerPoint"].AsDouble(s.ProductivityPerPoint);
            s.UnhappyBelow = j["unhappyBelow"].AsInt(s.UnhappyBelow);
            s.PetitionShare = j["petitionShare"].AsDouble(s.PetitionShare);
            s.PetitionSignRate = j["petitionSignRate"].AsDouble(s.PetitionSignRate);
            s.PetitionDays = j["petitionDays"].AsInt(s.PetitionDays);
            s.PetitionSolvedShare = j["petitionSolvedShare"].AsDouble(s.PetitionSolvedShare);
            s.ProtestAverage = j["protestAverage"].AsDouble(s.ProtestAverage);
            s.ProtestIgnoredShare = j["protestIgnoredShare"].AsDouble(s.ProtestIgnoredShare);
            s.ProtestShare = j["protestShare"].AsDouble(s.ProtestShare);
            s.ProtestCooldownDays = j["protestCooldownDays"].AsInt(s.ProtestCooldownDays);
            s.StrikeWork = j["strikeWork"].AsDouble(s.StrikeWork);
            s.StrikeDays = j["strikeDays"].AsInt(s.StrikeDays);
            s.StrikeLoss = j["strikeLoss"].AsDouble(s.StrikeLoss);
            s.EmigrationBase = j["emigrationBase"].AsDouble(s.EmigrationBase);
            s.EmigrationScale = j["emigrationScale"].AsDouble(s.EmigrationScale);
            s.EmigrationMin = j["emigrationMin"].AsDouble(s.EmigrationMin);
            s.EmigrationMax = j["emigrationMax"].AsDouble(s.EmigrationMax);
            s.ArrivalsScale = j["arrivalsScale"].AsDouble(s.ArrivalsScale);
            s.ArrivalsMin = j["arrivalsMin"].AsDouble(s.ArrivalsMin);
            s.ArrivalsMax = j["arrivalsMax"].AsDouble(s.ArrivalsMax);
            return s;
        }

        /// <summary>Moltiplicatore della produttività delle attività con la felicità media (reazioni leggere e realistiche).</summary>
        public double Productivity(double happiness) =>
            Math.Max(0.8, Math.Min(1.2, 1 + (happiness - ProductivityBase) * ProductivityPerPoint));

        /// <summary>Moltiplicatore delle partenze di una famiglia con la sua felicità media (reazioni realistiche).</summary>
        public double Emigration(double happiness) =>
            Math.Max(EmigrationMin, Math.Min(EmigrationMax, Math.Exp((EmigrationBase - happiness) / EmigrationScale)));

        /// <summary>Moltiplicatore degli arrivi con la felicità media della città (reazioni realistiche).</summary>
        public double Arrivals(double happiness) =>
            Math.Max(ArrivalsMin, Math.Min(ArrivalsMax, Math.Exp((happiness - EmigrationBase) / ArrivalsScale)));
    }

    /// <summary>Una reazione per il feed delle notizie e gli avvisi: chi, su cosa, quanti, dove.</summary>
    public sealed class Reaction
    {
        public ReactionKind Kind;
        /// <summary>Giorno (giorni dall'1/1/1900).</summary>
        public int Day;
        public HappinessFactor Factor;
        /// <summary>Cittadino che commenta (-1 per le reazioni collettive).</summary>
        public int Citizen = -1;
        /// <summary>Firme, partecipanti o persone coinvolte.</summary>
        public int Count;
        /// <summary>Commento a favore (cittadino contento) o contro.</summary>
        public bool Positive;
        /// <summary>Luogo in metri della regione (piazza della protesta, zona dei firmatari).</summary>
        public float X, Y;

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Kind);
            w.Write(Day);
            w.Write((byte)Factor);
            w.Write(Citizen);
            w.Write(Count);
            w.Write(Positive);
            w.Write(X);
            w.Write(Y);
        }

        public static Reaction Read(BinaryReader r) => new Reaction
        {
            Kind = (ReactionKind)r.ReadByte(), Day = r.ReadInt32(), Factor = (HappinessFactor)r.ReadByte(), Citizen = r.ReadInt32(),
            Count = r.ReadInt32(), Positive = r.ReadBoolean(), X = r.ReadSingle(), Y = r.ReadSingle(),
        };
    }

    /// <summary>Petizione aperta: firme raccolte tra i cittadini preoccupati dallo stesso problema.</summary>
    public sealed class Petition
    {
        public HappinessFactor Factor;
        public int StartDay;
        public int Signatures;
        /// <summary>Centro delle case dei firmatari (metri della regione).</summary>
        public float X, Y;

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Factor);
            w.Write(StartDay);
            w.Write(Signatures);
            w.Write(X);
            w.Write(Y);
        }

        public static Petition Read(BinaryReader r) => new Petition
        {
            Factor = (HappinessFactor)r.ReadByte(), StartDay = r.ReadInt32(), Signatures = r.ReadInt32(), X = r.ReadSingle(), Y = r.ReadSingle(),
        };
    }

    /// <summary>
    /// Reazioni dei cittadini a fine giornata (docs/03 §6), secondo il livello scelto:
    /// leggere = commenti sul social; realistiche = anche petizioni, proteste in piazza e scioperi.
    /// Gli effetti su produttività, partenze e arrivi li applica <see cref="CityLife"/>.
    /// </summary>
    public static class CityReactions
    {
        /// <summary>Problemi su cui i cittadini firmano petizioni (non l'umore del giorno).</summary>
        public static bool IsCivic(HappinessFactor f) => f > HappinessFactor.Needs && f < HappinessFactor.Count;

        public static List<Reaction> Day(CityPopulation pop, int day, int level, ReactionSpec s, Rng rng, DateTime date)
        {
            var news = new List<Reaction>();
            if (level <= ReactionLevel.Off) return news;
            int factors = (int)HappinessFactor.Count;
            // preoccupati e scontenti per problema, con il centro delle loro case
            var worried = new int[factors];
            var sx = new double[factors];
            var sy = new double[factors];
            int adults = 0, unhappy = 0;
            var candidates = new List<int>();
            for (int i = 0; i < pop.Count; i++)
            {
                ref var c = ref pop.Citizens[i];
                if (!c.Alive || c.Household < 0 || c.AgeOn(date) < 16) continue;
                adults++;
                candidates.Add(i);
                if (c.Happiness == 0 || c.Happiness >= s.UnhappyBelow) continue;
                unhappy++;
                if (!IsCivic((HappinessFactor)c.Worry)) continue;
                worried[c.Worry]++;
                var dw = pop.Households[c.Household].Dwelling;
                if (dw < 0) continue;
                sx[c.Worry] += pop.Dwellings[dw].X;
                sy[c.Worry] += pop.Dwellings[dw].Y;
            }
            pop.Approval = adults > 0 ? (float)(100.0 * (adults - unhappy) / adults) : 0;
            if (adults == 0) return news;

            // commenti sul social: cittadini a caso, sul loro problema o su ciò che va meglio
            for (int k = 0; k < s.CommentsPerDay && candidates.Count > 0; k++)
            {
                int i = candidates[rng.Next(candidates.Count)];
                ref var c = ref pop.Citizens[i];
                bool positive = c.Happiness >= 70 || !IsCivic((HappinessFactor)c.Worry);
                var factor = positive ? BestCityFactor(pop, rng) : (HappinessFactor)c.Worry;
                Add(pop, news, s, new Reaction { Kind = ReactionKind.Comment, Day = day, Factor = factor, Citizen = i, Positive = positive });
            }
            if (level < ReactionLevel.Realistic) return news;

            // petizioni: si aprono quando abbastanza adulti sono preoccupati dallo stesso problema, crescono, si chiudono
            for (int f = (int)HappinessFactor.Home; f < factors; f++)
            {
                var factor = (HappinessFactor)f;
                var open = pop.Petitions.Find(p => p.Factor == factor);
                double share = worried[f] / (double)adults;
                if (open == null)
                {
                    if (share < s.PetitionShare) continue;
                    open = new Petition { Factor = factor, StartDay = day, X = (float)(sx[f] / worried[f]), Y = (float)(sy[f] / worried[f]) };
                    open.Signatures = (int)Math.Round(worried[f] * s.PetitionSignRate);
                    pop.Petitions.Add(open);
                    Add(pop, news, s, new Reaction { Kind = ReactionKind.Petition, Day = day, Factor = factor, Count = open.Signatures, X = open.X, Y = open.Y });
                    continue;
                }
                if (share <= s.PetitionSolvedShare)
                {
                    pop.Petitions.Remove(open);
                    Add(pop, news, s, new Reaction { Kind = ReactionKind.PetitionAccepted, Day = day, Factor = factor, Count = open.Signatures, X = open.X, Y = open.Y });
                    continue;
                }
                // firmano un po' alla volta, al massimo tutti i preoccupati
                open.Signatures = Math.Max(open.Signatures, Math.Min(worried[f], open.Signatures + (int)Math.Round(worried[f] * s.PetitionSignRate)));
                if (day - open.StartDay >= s.PetitionDays)
                {
                    pop.Petitions.Remove(open);
                    Add(pop, news, s, new Reaction { Kind = ReactionKind.PetitionIgnored, Day = day, Factor = factor, Count = open.Signatures, X = open.X, Y = open.Y });
                    if (open.Signatures >= adults * s.ProtestIgnoredShare) pop.ProtestPending = (byte)(f + 1);
                }
            }

            // protesta in piazza: città scontenta o petizione ignorata con molte firme (non più di una a settimana)
            int top = TopWorry(worried);
            bool angry = pop.AverageHappiness > 0 && pop.AverageHappiness < s.ProtestAverage;
            if ((angry || pop.ProtestPending > 0) && day - pop.LastProtestDay >= s.ProtestCooldownDays && (top >= 0 || pop.ProtestPending > 0))
            {
                var factor = pop.ProtestPending > 0 ? (HappinessFactor)(pop.ProtestPending - 1) : (HappinessFactor)top;
                int people = Math.Max(1, (int)Math.Round(Math.Max(unhappy, worried[(int)factor]) * s.ProtestShare));
                Square(pop, out var px, out var py);
                Add(pop, news, s, new Reaction { Kind = ReactionKind.Protest, Day = day, Factor = factor, Count = people, X = px, Y = py });
                pop.LastProtestDay = day;
                pop.ProtestPending = 0;
            }

            // sciopero: se il lavoro preoccupa a lungo, domani le attività lavorano meno
            if (pop.HappinessFactors[(int)HappinessFactor.Work] < s.StrikeWork) pop.LowWorkDays++;
            else pop.LowWorkDays = 0;
            if (pop.LowWorkDays >= s.StrikeDays)
            {
                pop.LowWorkDays = 0;
                pop.StrikeDay = day + 1;
                int workers = 0;
                for (int i = 0; i < pop.Count; i++)
                    if (pop.Citizens[i].Alive && pop.Citizens[i].Works && pop.Citizens[i].Work >= 0) workers++;
                Add(pop, news, s, new Reaction { Kind = ReactionKind.Strike, Day = day + 1, Factor = HappinessFactor.Work, Count = workers });
            }
            return news;
        }

        private static int TopWorry(int[] worried)
        {
            int top = -1;
            for (int f = (int)HappinessFactor.Home; f < worried.Length; f++)
                if (worried[f] > 0 && (top < 0 || worried[f] > worried[top])) top = f;
            return top;
        }

        private static HappinessFactor BestCityFactor(CityPopulation pop, Rng rng)
        {
            // il fattore che va meglio in città, con un po' di varietà
            int best = (int)HappinessFactor.Home;
            double bv = double.MinValue;
            for (int f = (int)HappinessFactor.Home; f < (int)HappinessFactor.Count; f++)
            {
                double v = pop.HappinessFactors[f] + rng.NextDouble() * 15;
                if (v > bv)
                {
                    bv = v;
                    best = f;
                }
            }
            return (HappinessFactor)best;
        }

        /// <summary>La piazza della protesta: il municipio, se c'è tra i luoghi, altrimenti il centro delle case.</summary>
        private static void Square(CityPopulation pop, out float x, out float y)
        {
            foreach (var p in pop.Workplaces)
                if (p.Category == "amenity/townhall")
                {
                    x = p.X;
                    y = p.Y;
                    return;
                }
            double ax = 0, ay = 0;
            foreach (var d in pop.Dwellings)
            {
                ax += d.X;
                ay += d.Y;
            }
            int n = Math.Max(1, pop.Dwellings.Count);
            x = (float)(ax / n);
            y = (float)(ay / n);
        }

        private static void Add(CityPopulation pop, List<Reaction> news, ReactionSpec s, Reaction r)
        {
            news.Add(r);
            pop.Feed.Add(r);
            if (pop.Feed.Count > s.FeedSize) pop.Feed.RemoveRange(0, pop.Feed.Count - s.FeedSize);
        }
    }
}
