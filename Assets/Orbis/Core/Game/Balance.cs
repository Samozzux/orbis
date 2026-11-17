using System;
using Orbis.Core.Data;

namespace Orbis.Core.Game
{
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>Aliquota con limiti (cursore 0–5% ecc., docs/03 §4).</summary>
    public struct TaxRange
    {
        public float Default;
        public float Min;
        public float Max;
    }

    /// <summary>
    /// Parametri di bilanciamento (docs/03: mai fissi nel codice). Caricati da data/balance/economy.json;
    /// i valori predefiniti qui sotto servono solo se il file manca un campo.
    /// </summary>
    public sealed class Balance
    {
        public long StartCash = 150000;
        public float IncomeEasy = 1.4f, IncomeNormal = 1f, IncomeHard = 0.7f;

        // città esistente (Chieti reale) prima degli interventi del giocatore
        public int BasePopulation = 48000;
        public int BaseJobs = 21000;
        public float IncomePerResidentYear = 14000f;
        public float RevenuePerJobYear = 50000f;
        public float ServiceCostPerResidentYear = 120f;
        /// <summary>Costo fisso dei servizi della città esistente (uffici, scuole, strade…), al giorno.</summary>
        public float BaseServicesPerDay = 66000f;
        /// <summary>Quota dei posti di lavoro esistenti nel commercio (il resto è industria).</summary>
        public float CommercialJobShare = 0.7f;
        public float PublicLandPerResidentYear = 3f;

        public TaxRange ResidentialTax = new TaxRange { Default = 1.5f, Min = 0f, Max = 5f };
        public TaxRange CommercialTax = new TaxRange { Default = 2f, Min = 0f, Max = 8f };
        public TaxRange IndustrialTax = new TaxRange { Default = 2f, Min = 0f, Max = 8f };

        public float EnergySellPerKwh = 0.06f;
        public float EnergyBuyPerKwh = 0.14f;

        public float DemolishOsmCostPerM2 = 40f;
        /// <summary>Potenziare un edificio reale: costo per m² di piani a ogni passo (moltiplicato per il livello attuale).</summary>
        public float UpgradeOsmCostPerM2 = 25f;
        /// <summary>Affitto in più al mese per m² di piani e per livello sopra l'1 (rientro in circa 20 mesi di gioco).</summary>
        public float UpgradeOsmRentPerM2Month = 1.25f;
        public float DemolishRefundRatio = 0.2f;
        public float MoveCostRatio = 0.05f;
        public float VariantCostRatio = 0.1f;

        public int XpBase = 100;
        public float XpExponent = 1.45f;
        public long RewardPerLevel = 5000;
        public int MaxLevel = 60;
        public float XpPerCost = 0.004f;

        public float HappinessBase = 70f;
        public float HappinessPerTaxPoint = 4f;

        public int SnapshotsDaily = 30;
        public int SnapshotsWeeklyWeeks = 26;
        public float OfflineMaxRealHours = 12f;

        public float IncomeMultiplier(Difficulty d) => d == Difficulty.Easy ? IncomeEasy : (d == Difficulty.Hard ? IncomeHard : IncomeNormal);

        /// <summary>XP totali necessari per raggiungere il livello (livello 1 = 0).</summary>
        public long XpForLevel(int level)
        {
            if (level <= 1) return 0;
            long total = 0;
            for (int l = 1; l < level; l++) total += (long)Math.Round(XpBase * Math.Pow(l, XpExponent));
            return total;
        }

        public static Balance Parse(string json)
        {
            var j = Json.Parse(json);
            var b = new Balance();
            b.StartCash = j["startCash"].AsLong(b.StartCash);
            var d = j["difficulty"];
            b.IncomeEasy = d["easy"].AsFloat(b.IncomeEasy);
            b.IncomeNormal = d["normal"].AsFloat(b.IncomeNormal);
            b.IncomeHard = d["hard"].AsFloat(b.IncomeHard);
            var c = j["baseCity"];
            b.BasePopulation = c["population"].AsInt(b.BasePopulation);
            b.BaseJobs = c["jobs"].AsInt(b.BaseJobs);
            b.IncomePerResidentYear = c["incomePerResidentYear"].AsFloat(b.IncomePerResidentYear);
            b.RevenuePerJobYear = c["revenuePerJobYear"].AsFloat(b.RevenuePerJobYear);
            b.ServiceCostPerResidentYear = c["serviceCostPerResidentYear"].AsFloat(b.ServiceCostPerResidentYear);
            b.BaseServicesPerDay = c["baseServicesPerDay"].AsFloat(b.BaseServicesPerDay);
            b.CommercialJobShare = c["commercialJobShare"].AsFloat(b.CommercialJobShare);
            b.PublicLandPerResidentYear = c["publicLandPerResidentYear"].AsFloat(b.PublicLandPerResidentYear);
            b.ResidentialTax = Range(j["taxes"]["residential"], b.ResidentialTax);
            b.CommercialTax = Range(j["taxes"]["commercial"], b.CommercialTax);
            b.IndustrialTax = Range(j["taxes"]["industrial"], b.IndustrialTax);
            b.EnergySellPerKwh = j["energy"]["sellPerKwh"].AsFloat(b.EnergySellPerKwh);
            b.EnergyBuyPerKwh = j["energy"]["buyPerKwh"].AsFloat(b.EnergyBuyPerKwh);
            var a = j["actions"];
            b.DemolishOsmCostPerM2 = a["demolishOsmCostPerM2"].AsFloat(b.DemolishOsmCostPerM2);
            b.UpgradeOsmCostPerM2 = a["upgradeOsmCostPerM2"].AsFloat(b.UpgradeOsmCostPerM2);
            b.UpgradeOsmRentPerM2Month = a["upgradeOsmRentPerM2Month"].AsFloat(b.UpgradeOsmRentPerM2Month);
            b.DemolishRefundRatio = a["demolishRefundRatio"].AsFloat(b.DemolishRefundRatio);
            b.MoveCostRatio = a["moveCostRatio"].AsFloat(b.MoveCostRatio);
            b.VariantCostRatio = a["variantCostRatio"].AsFloat(b.VariantCostRatio);
            var l = j["levels"];
            b.XpBase = l["xpBase"].AsInt(b.XpBase);
            b.XpExponent = l["xpExponent"].AsFloat(b.XpExponent);
            b.RewardPerLevel = l["rewardPerLevel"].AsLong(b.RewardPerLevel);
            b.MaxLevel = l["max"].AsInt(b.MaxLevel);
            b.XpPerCost = l["xpPerCost"].AsFloat(b.XpPerCost);
            var h = j["happiness"];
            b.HappinessBase = h["base"].AsFloat(b.HappinessBase);
            b.HappinessPerTaxPoint = h["perTaxPoint"].AsFloat(b.HappinessPerTaxPoint);
            var s = j["snapshots"];
            b.SnapshotsDaily = s["daily"].AsInt(b.SnapshotsDaily);
            b.SnapshotsWeeklyWeeks = s["weeklyWeeks"].AsInt(b.SnapshotsWeeklyWeeks);
            b.OfflineMaxRealHours = j["offline"]["maxRealHours"].AsFloat(b.OfflineMaxRealHours);
            return b;
        }

        private static TaxRange Range(JsonValue j, TaxRange fallback) => j.IsNull
            ? fallback
            : new TaxRange { Default = j["default"].AsFloat(fallback.Default), Min = j["min"].AsFloat(fallback.Min), Max = j["max"].AsFloat(fallback.Max) };
    }
}
