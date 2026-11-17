using Orbis.Core.Game;
using UnityEngine;

namespace Orbis.Gameplay
{
    /// <summary>Dati del gioco caricati da Resources/OrbisContent (copiati da /data dal generatore).</summary>
    public sealed class GameContent
    {
        public Catalog Catalog;
        public Balance Balance;
        public Missions Missions;

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
            c.Balance ??= new Balance();
            c.Missions ??= new Missions();
            Debug.Log($"[Orbis] Contenuti: {c.Catalog.Count} elementi del catalogo, {c.Missions.All.Count} missioni");
            _cached = c;
            return c;
        }
    }
}
