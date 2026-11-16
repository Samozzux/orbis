using System;
using System.Collections.Generic;
using Orbis.Core.Data;

namespace Orbis.Core.Game
{
    public enum CatalogCategory
    {
        Residential, Commercial, Industrial, Energy, Agriculture, Services, Sports, Culture, Transport, Nature, Decoration,
    }

    /// <summary>Un livello di potenziamento (1→5) di un elemento del catalogo.</summary>
    public sealed class CatalogLevel
    {
        public int Level;
        public long Cost;
        public float BuildHours;
        /// <summary>Abitanti (residenziale) o posti (servizi).</summary>
        public int Capacity;
        public int Jobs;
        public long RentPerMonth;
        public long MaintenancePerMonth;
        /// <summary>Energia: kW prodotti (positivo) o consumati (negativo).</summary>
        public float PowerKw;
        /// <summary>Ricavo mensile della produzione (campi, vigneti, fabbriche…).</summary>
        public long ProductionPerMonth;
        public int Xp;
        public string Model;
    }

    public sealed class CatalogVariant
    {
        public string Key;
        public string Name;
        public uint WallColor;
        public uint RoofColor;
        public float CostMultiplier = 1f;
    }

    /// <summary>
    /// Elemento del catalogo (docs/08 §2). Sagoma esatta in metri: <see cref="Width"/> lungo l'asse x della
    /// griglia del lotto, <see cref="Depth"/> lungo y; si piazza su celle da <see cref="CellSize"/> m (docs/04 §4).
    /// </summary>
    public sealed class CatalogItem
    {
        public string Key;
        public CatalogCategory Category;
        public string Name;
        public float Width;
        public float Depth;
        public float Height;
        public string Family;
        public Dictionary<string, string> ModelParams = new Dictionary<string, string>();
        public int UnlockLevel = 1;
        public List<CatalogLevel> Levels = new List<CatalogLevel>();
        public List<CatalogVariant> Variants = new List<CatalogVariant>();
        public float HappinessRadius;
        public float Happiness;
        public float LandValue;
        public float Pollution;
        public string[] Tags = Array.Empty<string>();

        /// <summary>Griglia: 0,5 m per decorazioni e piante, 1 m per il resto (docs/04 §4).</summary>
        public float CellSize => Category == CatalogCategory.Decoration || Category == CatalogCategory.Nature ? 0.5f : 1f;

        /// <summary>Celle occupate lungo x e y (rotazione 0).</summary>
        public int CellsX => Math.Max(1, (int)Math.Ceiling(Width / CellSize - 1e-4));
        public int CellsY => Math.Max(1, (int)Math.Ceiling(Depth / CellSize - 1e-4));

        public int MaxLevel => Levels.Count;

        public CatalogLevel LevelSpec(int level) => Levels[Math.Max(1, Math.Min(level, Levels.Count)) - 1];

        public CatalogVariant Variant(string key)
        {
            foreach (var v in Variants)
                if (v.Key == key) return v;
            return Variants.Count > 0 ? Variants[0] : null;
        }

        public string Param(string key, string fallback = null) => ModelParams.TryGetValue(key, out var v) ? v : fallback;

        public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;
    }

    /// <summary>Catalogo degli elementi piazzabili, caricato dai JSON di data/catalog.</summary>
    public sealed class Catalog
    {
        private readonly Dictionary<string, CatalogItem> _items = new Dictionary<string, CatalogItem>();
        private readonly List<CatalogItem> _ordered = new List<CatalogItem>();

        public IReadOnlyList<CatalogItem> Items => _ordered;
        public int Count => _ordered.Count;

        public CatalogItem Get(string key) => key != null && _items.TryGetValue(key, out var it) ? it : null;

        public bool TryGet(string key, out CatalogItem item) => _items.TryGetValue(key ?? "", out item);

        public IEnumerable<CatalogItem> InCategory(CatalogCategory c)
        {
            foreach (var it in _ordered)
                if (it.Category == c) yield return it;
        }

        /// <summary>Aggiunge gli elementi di un file JSON ({"items": [...]}); gli errori di validazione sono eccezioni.</summary>
        public void Load(string json)
        {
            var root = Json.Parse(json);
            foreach (var j in root["items"].Items) Add(ParseItem(j));
        }

        public void Add(CatalogItem item)
        {
            Validate(item);
            if (_items.ContainsKey(item.Key)) throw new FormatException("elemento del catalogo duplicato: " + item.Key);
            _items[item.Key] = item;
            _ordered.Add(item);
        }

        public static CatalogItem ParseItem(JsonValue j)
        {
            var it = new CatalogItem
            {
                Key = j["key"].AsString(),
                Name = j["name"].AsString(j["key"].AsString()),
                Width = j["footprint"]["w"].AsFloat(),
                Depth = j["footprint"]["d"].AsFloat(),
                Height = j["height"].AsFloat(3f),
                UnlockLevel = j["unlock"]["playerLevel"].AsInt(1),
                HappinessRadius = j["effects"]["happinessRadius"].AsFloat(),
                Happiness = j["effects"]["happiness"].AsFloat(),
                LandValue = j["effects"]["landValue"].AsFloat(),
                Pollution = j["effects"]["pollution"].AsFloat(),
            };
            if (!Enum.TryParse(Capitalize(j["category"].AsString("")), out it.Category))
                throw new FormatException("categoria non valida per " + it.Key + ": " + j["category"].AsString());
            var model = j["model"];
            it.Family = model["family"].AsString("building");
            foreach (var k in model.Keys)
                if (k != "family") it.ModelParams[k] = model[k].AsString(model[k].Type == JsonType.Bool ? (model[k].AsBool() ? "true" : "false") : null);
            int n = 1;
            foreach (var l in j["levels"].Items)
            {
                it.Levels.Add(new CatalogLevel
                {
                    Level = l["level"].AsInt(n),
                    Cost = l["cost"].AsLong(),
                    BuildHours = l["buildHours"].AsFloat(1f),
                    Capacity = l["capacity"].AsInt(),
                    Jobs = l["jobs"].AsInt(),
                    RentPerMonth = l["rentPerMonth"].AsLong(),
                    MaintenancePerMonth = l["maintenancePerMonth"].AsLong(),
                    PowerKw = l["powerKw"].AsFloat(),
                    ProductionPerMonth = l["productionPerMonth"].AsLong(),
                    Xp = l["xp"].AsInt(-1),
                    Model = l["model"].AsString(),
                });
                n++;
            }
            foreach (var v in j["variants"].Items)
            {
                it.Variants.Add(new CatalogVariant
                {
                    Key = v["key"].AsString(),
                    Name = v["name"].AsString(v["key"].AsString()),
                    WallColor = ParseColor(v["wall"].AsString()),
                    RoofColor = ParseColor(v["roof"].AsString()),
                    CostMultiplier = v["costMultiplier"].AsFloat(1f),
                });
            }
            var tags = new List<string>();
            foreach (var t in j["tags"].Items) tags.Add(t.AsString());
            it.Tags = tags.ToArray();
            return it;
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>"#d9a066" → 0x01D9A066 (bit 24 = colore indicato), 0 se assente.</summary>
        public static uint ParseColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '#' || s.Length != 7) return 0;
            return 0x1000000u | Convert.ToUInt32(s.Substring(1), 16);
        }

        private static void Validate(CatalogItem it)
        {
            if (string.IsNullOrEmpty(it.Key)) throw new FormatException("elemento del catalogo senza chiave");
            if (it.Width <= 0 || it.Depth <= 0) throw new FormatException("sagoma non valida per " + it.Key);
            if (it.Height <= 0) throw new FormatException("altezza non valida per " + it.Key);
            if (it.Levels.Count == 0 || it.Levels.Count > 5) throw new FormatException("livelli non validi per " + it.Key);
            for (int i = 0; i < it.Levels.Count; i++)
            {
                var l = it.Levels[i];
                if (l.Level != i + 1) throw new FormatException("livelli fuori ordine per " + it.Key);
                if (l.Cost < 0 || l.MaintenancePerMonth < 0 || l.RentPerMonth < 0) throw new FormatException("valori negativi per " + it.Key);
            }
            // la sagoma in celle deve coincidere con quella in metri (niente modelli che escono dall'impronta)
            if (Math.Abs(it.CellsX * it.CellSize - it.Width) > 0.01f || Math.Abs(it.CellsY * it.CellSize - it.Depth) > 0.01f)
                throw new FormatException("la sagoma di " + it.Key + " non è un multiplo della griglia da " + it.CellSize + " m");
        }
    }
}
