using System;
using System.Collections.Generic;

namespace Orbis.Core.Game
{
    public enum SimEventKind { ConstructionDone, LevelUp, MissionDone, DayClosed, Reaction }

    /// <summary>Evento della simulazione da mostrare al giocatore (avvisi, riepiloghi).</summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public DateTime Time;
        public int ObjectId;
        public string Key;
        public long Amount;
        /// <summary>Reazione dei cittadini (petizione, protesta, sciopero) per <see cref="SimEventKind.Reaction"/>.</summary>
        public Orbis.Core.Population.Reaction Reaction;
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
            /// <summary>Con la popolazione (M4): tasse di cittadini e attività e aiuti sociali, stimati sul giorno
            /// (li registra la vita dei cittadini, qui servono alla previsione).</summary>
            public double CitizensTax, BusinessTax, Welfare;
            /// <summary>Linee del comune: biglietti (li registra la vita dei cittadini; qui la stima è l'ultimo giorno) e
            /// costo del servizio (ore dei mezzi e fermate).</summary>
            public double TransitFares, TransitCosts;

            public double Net => ResidentialTax + CommercialTax + IndustrialTax + Rent + PublicLand + Production + EnergySales
                                 + CitizensTax + BusinessTax + TransitFares - Welfare - EnergyPurchase - Maintenance - Services - TransitCosts;
        }

        public static Figures Compute(GameContext ctx)
        {
            var s = ctx.State;
            var b = ctx.Balance;
            var f = new Figures();
            float mult = b.IncomeMultiplier(s.Config.Difficulty);
            // con la popolazione vera (M4) la città esistente non si stima: le tasse le pagano i cittadini e le attività
            var pop = ctx.Population;
            int residents = pop != null ? 0 : b.BasePopulation;
            int com = pop != null ? 0 : (int)(b.BaseJobs * b.CommercialJobShare);
            int ind = pop != null ? 0 : b.BaseJobs - com;
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
            f.ResidentialTax = residents * b.IncomePerResidentYear / 365.0 * s.ResidentialTax / 100.0 * mult;
            f.CommercialTax = com * b.RevenuePerJobYear / 365.0 * s.CommercialTax / 100.0 * mult;
            f.IndustrialTax = ind * b.RevenuePerJobYear / 365.0 * s.IndustrialTax / 100.0 * mult;
            if (pop != null && ctx.Life != null)
            {
                var sum = ctx.Life.Summarize(pop, ctx.TaxRates);
                residents += sum.Alive;
                com += sum.CommercialJobs;
                ind += sum.IndustrialJobs;
                f.CitizensTax = sum.ResidentialTaxDay;
                f.BusinessTax = sum.CommercialTaxDay + sum.IndustrialTaxDay;
                f.Welfare = sum.WelfareDay;
            }
            else if (pop != null) residents += pop.AliveCount;
            f.Population = residents;
            f.CommercialJobs = com;
            f.IndustrialJobs = ind;
            f.Rent = rent * mult;
            f.Production = production * mult;
            f.PublicLand = residents * b.PublicLandPerResidentYear / 365.0 * mult;
            f.PowerKw = (float)power;
            if (power > 0) f.EnergySales = power * 24 * b.EnergySellPerKwh;
            else f.EnergyPurchase = -power * 24 * b.EnergyBuyPerKwh;
            // strade e linee del sindaco
            foreach (var r in s.Roads.Values) maintenance += Infrastructure.RoadMaintenancePerDay(b.Traffic, r);
            foreach (var l in s.Lines.Values) f.TransitCosts += Infrastructure.LineCostPerDay(b.Transit, l);
            if (pop != null) f.TransitFares = pop.LastCityFares;
            f.Maintenance = maintenance;
            f.Services = b.BaseServicesPerDay + residents * b.ServiceCostPerResidentYear / 365.0 + s.ServicesAdjustPerDay;
            float taxPenalty = (s.ResidentialTax - b.ResidentialTax.Default) * b.HappinessPerTaxPoint
                               + (s.CommercialTax - b.CommercialTax.Default) * b.HappinessPerTaxPoint * 0.5f
                               + (s.IndustrialTax - b.IndustrialTax.Default) * b.HappinessPerTaxPoint * 0.3f;
            f.Happiness = Math.Max(0f, Math.Min(100f, b.HappinessBase - taxPenalty + (float)happiness * 0.5f));
            // con i cittadini veri (M4) la felicità è la loro media (tasse comprese), più gli abbellimenti del sindaco
            if (pop != null && pop.AverageHappiness > 0)
                f.Happiness = Math.Max(0f, Math.Min(100f, pop.AverageHappiness + (float)happiness * 0.5f));
            return f;
        }

        /// <summary>Chiude la giornata: registra entrate e uscite e aggiorna gli indicatori.</summary>
        /// <param name="life">giornata dei cittadini appena simulata: le sue tasse e i suoi aiuti sono già registrati,
        /// qui entrano solo nel saldo del giorno (al posto della stima)</param>
        public static Figures CloseDay(GameContext ctx, Orbis.Core.Population.LifeDay life = null)
        {
            var s = ctx.State;
            var f = Compute(ctx);
            if (life != null && !life.Skipped)
            {
                f.CitizensTax = life.ResidentialTax;
                f.BusinessTax = life.CommercialTax + life.IndustrialTax;
                f.Welfare = life.Welfare;
                f.TransitFares = life.TransitFares;
            }
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
            s.Transact(LedgerCategory.TransitCosts, -Round(f.TransitCosts));
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

        /// <summary>I passi della giornata dei cittadini che cadono in (from, to].</summary>
        private void RoutineSteps(GameContext ctx, DateTime from, DateTime to)
        {
            if (!Routines || ctx.Population == null || ctx.Routines == null) return;
            int step = ctx.Routines.StepMinutes;
            long stepTicks = TimeSpan.TicksPerMinute * step;
            long first = (from.Ticks / stepTicks + 1) * stepTicks;
            for (long t = first; t <= to.Ticks; t += stepTicks)
                ctx.Routines.Step(ctx.Population, new DateTime(t));
        }

        private static LedgerCategory Ledger(Orbis.Core.Population.CityFlow flow)
        {
            switch (flow)
            {
                case Orbis.Core.Population.CityFlow.ResidentialTax: return LedgerCategory.ResidentialTax;
                case Orbis.Core.Population.CityFlow.CommercialTax: return LedgerCategory.CommercialTax;
                case Orbis.Core.Population.CityFlow.IndustrialTax: return LedgerCategory.IndustrialTax;
                case Orbis.Core.Population.CityFlow.TransitFares: return LedgerCategory.TransitFares;
                default: return LedgerCategory.Services;
            }
        }

        /// <summary>Giornata dei cittadini a passi di 15 minuti (L1); spenta nel recupero del tempo ad app chiusa.</summary>
        public bool Routines = true;

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
                RoutineSteps(ctx, before, step);
                s.Now = step;
                foreach (var o in s.Objects.Values)
                    if (o.ReadyAt > before && o.ReadyAt <= s.Now)
                        events.Add(new SimEvent { Kind = SimEventKind.ConstructionDone, Time = o.ReadyAt, ObjectId = o.Id, Key = o.ItemKey });
                if (s.Now.Hour == 0 && s.Now.Minute == 0 && s.Now.Second == 0 && step == nextHour)
                {
                    // la giornata appena finita si registra con la sua data
                    var closing = s.Now;
                    s.Now = closing.AddTicks(-1);
                    // prima la giornata dei cittadini (stipendi, spese, tasse, nascite…), poi i conti del comune
                    Orbis.Core.Population.LifeDay life = null;
                    if (ctx.Life?.Services != null) ctx.Life.Services.Extra = ServiceSources.FromCatalog(ctx, ctx.Life.Services.Balance);
                    if (ctx.Life != null) ctx.Life.Reactions = s.Config.CitizenReactions;
                    if (ctx.Population != null && ctx.Life != null)
                        life = ctx.Life.Day(ctx.Population, closing.AddDays(-1), ctx.TaxRates, s.Config.LifeCycle, (flow, amount) => s.Transact(Ledger(flow), amount));
                    var f = Economy.CloseDay(ctx, life);
                    s.Now = closing;
                    // petizioni, proteste e scioperi diventano avvisi (i commenti restano nelle notizie)
                    if (life != null)
                        foreach (var r in life.Reactions)
                            if (r.Kind != Orbis.Core.Population.ReactionKind.Comment)
                                events.Add(new SimEvent { Kind = SimEventKind.Reaction, Time = closing, Key = r.Kind + "." + r.Factor, Amount = r.Count, Reaction = r });
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
