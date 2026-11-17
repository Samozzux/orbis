using System;
using System.Collections.Generic;

namespace Orbis.Core.Game
{
    public enum SimEventKind { ConstructionDone, LevelUp, MissionDone, DayClosed }

    /// <summary>Evento della simulazione da mostrare al giocatore (avvisi, riepiloghi).</summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public DateTime Time;
        public int ObjectId;
        public string Key;
        public long Amount;
    }

    /// <summary>
    /// Economia del proprietario (docs/03 §4), calcolata a fine giornata: tasse, affitti, suolo pubblico,
    /// produzione, energia, manutenzione e servizi. Tutti i parametri vengono da <see cref="Balance"/>.
    /// </summary>
    public static class Economy
    {
        public struct Figures
        {
            public int Population, CommercialJobs, IndustrialJobs;
            public double ResidentialTax, CommercialTax, IndustrialTax, Rent, PublicLand, Production;
            public double EnergySales, EnergyPurchase, Maintenance, Services;
            public float PowerKw, Happiness;

            public double Net => ResidentialTax + CommercialTax + IndustrialTax + Rent + PublicLand + Production + EnergySales
                                 - EnergyPurchase - Maintenance - Services;
        }

        public static Figures Compute(GameContext ctx)
        {
            var s = ctx.State;
            var b = ctx.Balance;
            var f = new Figures();
            float mult = b.IncomeMultiplier(s.Config.Difficulty);
            int residents = b.BasePopulation, com = (int)(b.BaseJobs * b.CommercialJobShare), ind = b.BaseJobs - com;
            double rent = 0, production = 0, maintenance = 0, power = 0, happiness = 0;
            foreach (var o in s.Objects.Values)
            {
                var item = ctx.Catalog.Get(o.ItemKey);
                if (item == null) continue;
                var spec = item.LevelSpec(o.Level);
                maintenance += spec.MaintenancePerMonth / 30.0;
                if (!o.IsReady(s.Now)) continue;
                if (item.Category == CatalogCategory.Residential) residents += spec.Capacity;
                if (item.Category == CatalogCategory.Industrial || item.Category == CatalogCategory.Energy || item.Category == CatalogCategory.Agriculture)
                    ind += spec.Jobs;
                else com += spec.Jobs;
                rent += spec.RentPerMonth / 30.0;
                production += spec.ProductionPerMonth / 30.0;
                power += spec.PowerKw;
                happiness += item.Happiness;
            }
            // edifici reali potenziati: affitti in più e un po' di felicità per gli addobbi
            foreach (var kv in s.OsmUpgrades)
            {
                if (kv.Value.Level <= 1 || s.DemolishedOsm.ContainsKey(kv.Key)) continue;
                rent += kv.Value.FloorArea * b.UpgradeOsmRentPerM2Month * (kv.Value.Level - 1) / 30.0;
                happiness += 0.1 * (kv.Value.Level - 1);
            }
            f.Population = residents;
            f.CommercialJobs = com;
            f.IndustrialJobs = ind;
            f.ResidentialTax = residents * b.IncomePerResidentYear / 365.0 * s.ResidentialTax / 100.0 * mult;
            f.CommercialTax = com * b.RevenuePerJobYear / 365.0 * s.CommercialTax / 100.0 * mult;
            f.IndustrialTax = ind * b.RevenuePerJobYear / 365.0 * s.IndustrialTax / 100.0 * mult;
            f.Rent = rent * mult;
            f.Production = production * mult;
            f.PublicLand = residents * b.PublicLandPerResidentYear / 365.0 * mult;
            f.PowerKw = (float)power;
            if (power > 0) f.EnergySales = power * 24 * b.EnergySellPerKwh;
            else f.EnergyPurchase = -power * 24 * b.EnergyBuyPerKwh;
            f.Maintenance = maintenance;
            f.Services = b.BaseServicesPerDay + residents * b.ServiceCostPerResidentYear / 365.0;
            float taxPenalty = (s.ResidentialTax - b.ResidentialTax.Default) * b.HappinessPerTaxPoint
                               + (s.CommercialTax - b.CommercialTax.Default) * b.HappinessPerTaxPoint * 0.5f
                               + (s.IndustrialTax - b.IndustrialTax.Default) * b.HappinessPerTaxPoint * 0.3f;
            f.Happiness = Math.Max(0f, Math.Min(100f, b.HappinessBase - taxPenalty + (float)happiness * 0.5f));
            return f;
        }

        /// <summary>Chiude la giornata: registra entrate e uscite e aggiorna gli indicatori.</summary>
        public static Figures CloseDay(GameContext ctx)
        {
            var s = ctx.State;
            var f = Compute(ctx);
            s.Transact(LedgerCategory.ResidentialTax, Round(f.ResidentialTax));
            s.Transact(LedgerCategory.CommercialTax, Round(f.CommercialTax));
            s.Transact(LedgerCategory.IndustrialTax, Round(f.IndustrialTax));
            s.Transact(LedgerCategory.Rent, Round(f.Rent));
            s.Transact(LedgerCategory.PublicLand, Round(f.PublicLand));
            s.Transact(LedgerCategory.Production, Round(f.Production));
            s.Transact(LedgerCategory.EnergySales, Round(f.EnergySales));
            s.Transact(LedgerCategory.EnergyPurchase, -Round(f.EnergyPurchase));
            s.Transact(LedgerCategory.Maintenance, -Round(f.Maintenance));
            s.Transact(LedgerCategory.Services, -Round(f.Services));
            s.Stats.Population = f.Population;
            s.Stats.Jobs = f.CommercialJobs + f.IndustrialJobs;
            s.Stats.PowerBalanceKw = f.PowerKw;
            s.Stats.Happiness = f.Happiness;
            s.Stats.LastDayNet = Round(f.Net);
            s.Stats.PositiveDaysStreak = s.Stats.LastDayNet > 0 ? s.Stats.PositiveDaysStreak + 1 : 0;
            return f;
        }

        private static long Round(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : (long)Math.Round(v);
    }

    /// <summary>
    /// Avanzamento del tempo di gioco a passi di un'ora (docs/01 §4): fine lavori, chiusura della giornata a
    /// mezzanotte (economia, istantanea), missioni. Deterministico: niente orologio di sistema qui dentro.
    /// </summary>
    public sealed class Simulation
    {
        /// <summary>Chiamato a ogni mezzanotte di gioco, dopo l'economia (per le istantanee).</summary>
        public event Action<GameContext> Midnight;

        public List<SimEvent> Advance(GameContext ctx, TimeSpan span, List<SimEvent> events = null)
        {
            events = events ?? new List<SimEvent>();
            var s = ctx.State;
            var end = s.Now + span;
            while (s.Now < end)
            {
                var nextHour = new DateTime(s.Now.Year, s.Now.Month, s.Now.Day, s.Now.Hour, 0, 0).AddHours(1);
                var step = nextHour <= end ? nextHour : end;
                var before = s.Now;
                s.Now = step;
                foreach (var o in s.Objects.Values)
                    if (o.ReadyAt > before && o.ReadyAt <= s.Now)
                        events.Add(new SimEvent { Kind = SimEventKind.ConstructionDone, Time = o.ReadyAt, ObjectId = o.Id, Key = o.ItemKey });
                if (s.Now.Hour == 0 && s.Now.Minute == 0 && s.Now.Second == 0 && step == nextHour)
                {
                    // la giornata appena finita si registra con la sua data
                    var closing = s.Now;
                    s.Now = closing.AddTicks(-1);
                    var f = Economy.CloseDay(ctx);
                    s.Now = closing;
                    events.Add(new SimEvent { Kind = SimEventKind.DayClosed, Time = closing, Amount = s.Stats.LastDayNet });
                    Midnight?.Invoke(ctx);
                }
                int level = s.Level;
                int before2 = s.MissionIndex;
                ctx.Missions?.Evaluate(ctx);
                for (int i = before2; i < s.MissionIndex; i++)
                    events.Add(new SimEvent { Kind = SimEventKind.MissionDone, Time = s.Now, Key = ctx.Missions.All[i].Key, Amount = ctx.Missions.All[i].RewardCash });
                if (s.Level > level) events.Add(new SimEvent { Kind = SimEventKind.LevelUp, Time = s.Now, Amount = s.Level });
            }
            return events;
        }
    }
}
