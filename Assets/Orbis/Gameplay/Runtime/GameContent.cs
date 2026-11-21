using System;
using Orbis.Core.Game;
using Orbis.Core.Population;
using UnityEngine;

namespace Orbis.Gameplay
{
    /// <summary>Dati del gioco caricati da Resources/OrbisContent (copiati da /data dal generatore).</summary>
    public sealed class GameContent
    {
        public Catalog Catalog;
        public Balance Balance;
        public Missions Missions;
        /// <summary>Dati della popolazione reale (M4); null se mancano: la città resta quella stimata.</summary>
        public LifeInputs Life;
        /// <summary>Parametri del traffico (M4); null se mancano: tempi di viaggio in linea d'aria.</summary>
        public Orbis.Core.Traffic.TrafficBalance Traffic;
        /// <summary>Parametri del trasporto pubblico (M4); null se mancano: niente linee.</summary>
        public Orbis.Core.Traffic.TransitBalance Transit;

        private static GameContent _cached;

        public static GameContent Load()
        {
            if (_cached != null) return _cached;
            var c = new GameContent { Catalog = new Catalog() };
            foreach (var t in Resources.LoadAll<TextAsset>("OrbisContent"))
            {
                if (t.name.StartsWith("catalog_")) c.Catalog.Load(t.text);
                else if (t.name == "economy") c.Balance = Balance.Parse(t.text);
                else if (t.name == "missions") c.Missions = Missions.Parse(t.text);
            }
            c.Life = LoadLife();
            var traffic = Resources.Load<TextAsset>("OrbisContent/traffic_balance");
            if (traffic != null) c.Traffic = Orbis.Core.Traffic.TrafficBalance.Parse(traffic.text);
            var transit = Resources.Load<TextAsset>("OrbisContent/transit_balance");
            if (transit != null) c.Transit = Orbis.Core.Traffic.TransitBalance.Parse(transit.text);
            c.Balance ??= new Balance();
            c.Missions ??= new Missions();
            // strade e linee del sindaco: costi e regole stanno nei bilanciamenti del traffico e del trasporto pubblico
            c.Balance.Traffic = c.Traffic;
            c.Balance.Transit = c.Transit;
            Debug.Log($"[Orbis] Contenuti: {c.Catalog.Count} elementi del catalogo, {c.Missions.All.Count} missioni" +
                      (c.Life != null ? $", popolazione di {c.Life.Data.MunicipalityName} ({c.Life.Data.Total} abitanti)" : ""));
            _cached = c;
            return c;
        }

        private static LifeInputs LoadLife()
        {
            var data = Resources.Load<TextAsset>("OrbisContent/population_chieti");
            var names = Resources.Load<TextAsset>("OrbisContent/population_names-it");
            var balance = Resources.Load<TextAsset>("OrbisContent/population_balance");
            var services = Resources.Load<TextAsset>("OrbisContent/services_balance");
            if (data == null || names == null || balance == null) return null;
            try
            {
                return new LifeInputs
                {
                    Data = PopulationData.Parse(data.text),
                    Names = NameData.Parse(names.text),
                    Balance = PopulationBalance.Parse(balance.text),
                    // servizi e felicità (M4 blocco 5): senza il file la felicità resta quella stimata
                    Services = services != null ? ServicesBalance.Parse(services.text) : null,
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] dati della popolazione illeggibili: " + e.Message);
                return null;
            }
        }
    }
}
