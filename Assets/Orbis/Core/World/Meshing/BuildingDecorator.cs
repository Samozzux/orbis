using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>Che tipo di addobbi riceve un edificio (dall'uso OSM e dai POI che contiene).</summary>
    public enum DressKind
    {
        None, Residential, FoodDrink, Shop, Pharmacy, Bank, Hotel, Fuel, Canopy, FuelCanopy, School, Hospital, Civic, Office, Industrial, Farm,
    }

    /// <summary>Insegna con il nome vero del locale (il testo lo scrive il motore, solo da vicino).</summary>
    public struct SignInfo
    {
        /// <summary>Centro della faccia anteriore: metri del tile (X est, Y nord) e quota Z.</summary>
        public float X, Y, Z;
        /// <summary>Direzione verso cui guarda l'insegna (verso la strada).</summary>
        public float NormalX, NormalY;
        public float Width, Height;
        public string Text;
        /// <summary>Colore del fondo (RGB), per scegliere un testo chiaro o scuro.</summary>
        public uint Background;
    }

    /// <summary>
    /// Addobbi "da gioco" degli edifici reali (docs/08 §3): la struttura resta quella vera, sopra si aggiungono
    /// i dettagli tipici dell'uso (tende e insegne per bar e negozi, dehors con tavolini e ombrelloni, croce della
    /// farmacia, pensilina e pompe del benzinaio, balconi fioriti, fioriere, comignoli, parapetti e terrazze sui
    /// tetti piani, impianti e insegne per le industrie, balle di fieno nelle fattorie). Ogni livello 1→5
    /// aggiunge o cambia addobbi. Le posizioni seguono campate e piani dello shader, così tende e fioriere
    /// stanno sopra vetrine e sotto finestre vere. Sempre uguale per lo stesso edificio e livello.
    /// </summary>
    public sealed class BuildingDecorator
    {
        // tipi di superficie dello shader degli edifici (uv1.x)
        public const float KindProp = 13, KindCanvas = 14, KindSign = 15, KindLight = 16, KindPlants = 17, KindSolar = 12, KindWater = 11;

        private static readonly uint[] Awnings = { 0xC8372D, 0x2E7D4F, 0x1F5FA8, 0xE8A317, 0x2A9D8F, 0xD9622B, 0x7B2D5B };
        private static readonly uint[] SignColors = { 0x1E5631, 0x7A1F2B, 0x1C2E5A, 0x262626, 0xB3261E, 0x2A6F97 };
        private static readonly uint[] Planters = { 0xB5653F, 0x3B5A45, 0xE6E1D6, 0x8A5A3B };
        private static readonly uint[] Railings = { 0x2B2B2B, 0x354235, 0x3A2F2A };
        private const uint Foliage = 0x4E8A3A;
        private const uint Steel = 0xB7BCC0;
        private const uint Wood = 0x7A563C;

        private struct V3
        {
            public float X, Y, Z;

            public V3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public V3(Vec2 p, float z) : this(p.X, p.Y, z)
            {
            }

            public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        private struct Edge
        {
            public Vec2 P, Q, Dir, Normal;
            public float Len;
            public bool Street;
            public float Free; // spazio libero tra il muro e il bordo della strada (∞ se nessuna strada vicina)
            public float Ground; // quota del marciapiede davanti alla facciata (in collina può essere sopra la media)
            public int Bays;
            public float Bay;
        }

        private MeshBuffer _mb;
        private GroundSampler _ground;
        private List<PlantInstance> _plants;
        private long _id;
        private float _seed;
        private int _level;
        private string _name;
        private readonly List<Edge> _edges = new List<Edge>(32);

        /// <summary>Se impostata, riceve le insegne con il nome vero (bar, negozi, farmacie, scuole…).</summary>
        public List<SignInfo> Signs;

        /// <summary>Densità degli addobbi (1 = piena; i profili di qualità bassi usano meno).</summary>
        public float Density = 1f;

        /// <summary>Tipo di addobbi dall'uso dell'edificio e dalla categoria del POI principale (es. "amenity/bar").</summary>
        public static DressKind KindOf(BuildingInfo b, string poiCategory)
        {
            if (!string.IsNullOrEmpty(poiCategory))
            {
                switch (poiCategory)
                {
                    case "amenity/bar":
                    case "amenity/cafe":
                    case "amenity/restaurant":
                    case "amenity/fast_food":
                    case "amenity/ice_cream":
                    case "amenity/pub":
                    case "shop/bakery":
                    case "shop/pastry":
                        return DressKind.FoodDrink;
                    case "amenity/pharmacy":
                        return DressKind.Pharmacy;
                    case "amenity/bank":
                    case "amenity/post_office":
                        return DressKind.Bank;
                    case "amenity/fuel":
                        return b.Use == BuildingUse.Roof ? DressKind.FuelCanopy : DressKind.Fuel;
                    case "tourism/hotel":
                    case "tourism/guest_house":
                    case "tourism/hostel":
                        return DressKind.Hotel;
                }
                if (poiCategory.StartsWith("shop/", StringComparison.Ordinal)) return DressKind.Shop;
            }
            switch (b.Use)
            {
                case BuildingUse.House:
                case BuildingUse.Detached:
                case BuildingUse.Apartments:
                case BuildingUse.Residential:
                case BuildingUse.Terrace:
                case BuildingUse.Unknown:
                    return b.HasFlag(BuildingFlag.HasShops) ? DressKind.Shop : DressKind.Residential;
                case BuildingUse.Commercial:
                case BuildingUse.Retail:
                    return DressKind.Shop;
                case BuildingUse.Office:
                    return DressKind.Office;
                case BuildingUse.Hotel:
                    return DressKind.Hotel;
                case BuildingUse.School:
                case BuildingUse.University:
                case BuildingUse.Kindergarten:
                    return DressKind.School;
                case BuildingUse.Hospital:
                    return DressKind.Hospital;
                case BuildingUse.Civic:
                case BuildingUse.Government:
                case BuildingUse.TrainStation:
                    return DressKind.Civic;
                case BuildingUse.Industrial:
                case BuildingUse.Warehouse:
                case BuildingUse.Service:
                    return DressKind.Industrial;
                case BuildingUse.Farm:
                case BuildingUse.FarmAuxiliary:
                case BuildingUse.Barn:
                case BuildingUse.Stable:
                    return DressKind.Farm;
                case BuildingUse.Roof:
                    return DressKind.Canopy;
            }
            return DressKind.None;
        }

        /// <summary>
        /// Aggiunge gli addobbi dell'ultimo edificio costruito da <paramref name="m"/> (stessa facciata).
        /// Restituisce i triangoli aggiunti. <paramref name="entrance"/> = posizione del POI (ingresso), se c'è.
        /// </summary>
        public int Decorate(BuildingInfo b, BuildingMesher m, DressKind kind, int level, Vec2? entrance, RoadIndex roads,
            GroundSampler ground, MeshBuffer mb, List<PlantInstance> plants, string name = null)
        {
            _name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (kind == DressKind.None || m.LastOuter == null || m.LastOuter.Count < 3) return 0;
            _mb = mb;
            _ground = ground;
            _plants = plants;
            _id = b.OsmId;
            _seed = StylePalette.Random01(b.OsmId, 5);
            _level = Math.Max(1, Math.Min(5, level));
            int startTris = mb.IndexCount / 3;
            Edges(m, roads);
            if (_edges.Count == 0) return 0;

            switch (kind)
            {
                case DressKind.Residential:
                    Residential(m);
                    break;
                case DressKind.FoodDrink:
                    Storefront(m, entrance, true);
                    Dehors(m, entrance);
                    UpperFloors(m, 0.5f);
                    break;
                case DressKind.Shop:
                case DressKind.Bank:
                    Storefront(m, entrance, kind == DressKind.Shop);
                    UpperFloors(m, 0.6f);
                    break;
                case DressKind.Pharmacy:
                    Storefront(m, entrance, true, 0x2E8B3E);
                    PharmacyCross(m, entrance);
                    UpperFloors(m, 0.6f);
                    break;
                case DressKind.Hotel:
                    Hotel(m, entrance);
                    UpperFloors(m, 1f);
                    break;
                case DressKind.Fuel:
                    Storefront(m, entrance, false, 0x1F5FA8);
                    break;
                case DressKind.Canopy:
                case DressKind.FuelCanopy:
                    Canopy(b, m, kind == DressKind.FuelCanopy);
                    return mb.IndexCount / 3 - startTris;
                case DressKind.School:
                case DressKind.Civic:
                case DressKind.Hospital:
                    Public(m, entrance, kind);
                    break;
                case DressKind.Office:
                    Storefront(m, entrance, false);
                    break;
                case DressKind.Industrial:
                    Industrial(m);
                    break;
                case DressKind.Farm:
                    Farm(m);
                    break;
            }
            Roof(m, kind);
            return mb.IndexCount / 3 - startTris;
        }

        // ------------------------------------------------------------------ facciate

        private void Edges(BuildingMesher m, RoadIndex roads)
        {
            _edges.Clear();
            var ring = m.LastOuter;
            float bayW = BuildingMesher.BayWidth(m.LastCategory);
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                var d = q - p;
                float len = d.Length;
                if (len < 2.5f) continue;
                var dir = d * (1f / len);
                var e = new Edge { P = p, Q = q, Dir = dir, Len = len, Normal = new Vec2(dir.Y, -dir.X) };
                var mid = Vec2.Lerp(p, q, 0.5f);
                e.Free = roads != null ? roads.DistanceToEdge(mid + e.Normal * 0.3f, 25f) : float.PositiveInfinity;
                e.Ground = Math.Max(m.LastGroundRef, Ground(mid + e.Normal * 1.0f));
                e.Street = e.Free < 16f;
                e.Bays = Math.Max(1, (int)Math.Round(len / bayW));
                e.Bay = len / e.Bays;
                _edges.Add(e);
            }
            // nessuna strada nota: la facciata più lunga fa da fronte
            bool any = false;
            foreach (var e in _edges) any |= e.Street;
            if (!any && _edges.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < _edges.Count; i++)
                    if (_edges[i].Len > _edges[best].Len) best = i;
                var e = _edges[best];
                e.Street = true;
                _edges[best] = e;
            }
        }

        /// <summary>Lato d'ingresso: il più vicino al POI, altrimenti il lato su strada più lungo.</summary>
        private int FrontEdge(Vec2? entrance)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                float score;
                if (entrance.HasValue) score = DistanceToSegment(entrance.Value, e.P, e.Q) - (e.Street ? 2f : 0f);
                else score = (e.Street ? 0 : 1000) - e.Len;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private float R(int a, int b = 0, int c = 0, uint salt = 0)
        {
            long k = _id ^ ((long)a * 73856093L) ^ ((long)b * 19349663L) ^ ((long)c * 83492791L);
            return StylePalette.Random01(k, salt + 101);
        }

        private uint Pick(uint[] list, int a, uint salt) => list[(int)(R(a, 0, 0, salt) * list.Length) % list.Length];

        /// <summary>Probabilità per livello: L1 → base, L5 → piena.</summary>
        private float ByLevel(float l1, float l5) => (l1 + (l5 - l1) * (_level - 1) / 4f) * Density;

        // ------------------------------------------------------------------ case

        private void Residential(BuildingMesher m) => UpperFloors(m, 1f);

        /// <summary>Piani alti: fioriere sotto le finestre, balconi (con piante e, ai livelli alti, lucine e tende).</summary>
        private void UpperFloors(BuildingMesher m, float amount)
        {
            if (m.LastCategory != BuildingMesher.Category.Residential && m.LastCategory != BuildingMesher.Category.Commercial) return;
            int levels = m.LastLevels;
            float floorH = BuildingMesher.FloorHeight(m.LastWallHeight, levels);
            float w0 = m.LastCategory == BuildingMesher.Category.Residential ? 0.34f : 0.22f;
            float pFlower = ByLevel(0.06f, 0.8f) * amount;
            float pBalcony = levels >= 3 ? ByLevel(0.04f, 0.25f) * amount : 0;
            uint planter = Pick(Planters, 1, 3);
            uint rail = Pick(Railings, 2, 3);
            for (int ei = 0; ei < _edges.Count; ei++)
            {
                var e = _edges[ei];
                if ((!e.Street || e.Free > 10f) && _level < 3) continue;
                for (int fi = 1; fi < levels; fi++)
                {
                    float vTop = (fi + 0.80f) * floorH;
                    if (vTop > m.LastWallHeight - 0.5f) break;
                    for (int bi = 0; bi < e.Bays; bi++)
                    {
                        float r = R(ei, fi, bi, 7);
                        var center = e.P + e.Dir * ((bi + 0.5f) * e.Bay);
                        float winW = (1 - 2 * w0) * e.Bay;
                        float zFloor = m.LastGroundRef + fi * floorH;
                        if (zFloor < e.Ground + 1.2f) continue; // piano interrato dal lato della salita
                        if (r < pBalcony)
                        {
                            Balcony(center, e.Dir, e.Normal, winW + 0.9f, zFloor, rail, planter, ei * 31 + fi * 7 + bi);
                        }
                        else if (r < pBalcony + pFlower)
                        {
                            float z = m.LastGroundRef + (fi + 0.22f) * floorH;
                            FlowerBox(center, e.Dir, e.Normal, winW + 0.1f, z, planter, ei * 13 + fi * 5 + bi);
                        }
                    }
                }
            }
        }

        private void Balcony(Vec2 center, Vec2 dir, Vec2 n, float width, float z, uint rail, uint planter, int salt)
        {
            const float depth = 0.95f;
            // asse lungo la facciata: il lato "+traverso" è verso il muro, quindi niente retro
            var c = center + n * (depth / 2);
            Box(c, dir, width / 2, depth / 2, z - 0.16f, z, 0xD9D2C3, KindProp, 0, false, true);
            // ringhiera: un pannello a due facce (da lontano basta)
            var ra = center + n * depth - dir * (width / 2);
            var rb = center + n * depth + dir * (width / 2);
            Quad(new V3(ra, z), new V3(rb, z), new V3(rb, z + 1.0f), new V3(ra, z + 1.0f), new V3(n.X, n.Y, 0), rail, KindProp, 0, width, 1f, true);
            // piante sulla ringhiera dal livello 2, fiori che ricadono dal 3
            if (_level >= 2)
            {
                float k = R(salt, 2) - 0.5f;
                Box(center + n * 0.35f + dir * (k * width * 0.5f), dir, 0.3f, 0.25f, z, z + 0.5f + 0.1f * _level, Foliage, KindPlants, 0.2f + 0.1f * _level, false, true);
            }
            if (_level >= 3)
                Box(center + n * (depth + 0.08f), dir, width / 2 - 0.05f, 0.1f, z + 0.55f, z + 0.95f, Foliage, KindPlants, 0.25f + 0.12f * _level, false, true);
            if (_level >= 4 && R(salt, 3) < 0.6f)
                Awning(center - dir * (width / 2) + n * 0.05f, center + dir * (width / 2) + n * 0.05f, n, z + 2.55f, 1.0f, 0.45f, Pick(Awnings, salt, 9), R(salt, 4) < 0.6f);
            if (_level >= 5)
                StringLights(center + n * depth - dir * (width / 2) + new Vec2(0, 0), center + n * depth + dir * (width / 2), z + 1.05f, 0.08f, 5);
        }

        private void FlowerBox(Vec2 center, Vec2 dir, Vec2 n, float width, float z, uint planter, int salt)
        {
            // fioriera: solo le facce che si vedono (fronte del vaso, fronte e cima dei fiori)
            var a0 = center + n * 0.34f - dir * (width / 2);
            var b0 = center + n * 0.34f + dir * (width / 2);
            Quad(new V3(a0, z - 0.25f), new V3(b0, z - 0.25f), new V3(b0, z), new V3(a0, z), new V3(n.X, n.Y, 0), planter, KindProp, 0, width, 0.25f, false);
            float flowers = 0.25f + 0.15f * _level;
            float zf = z + 0.18f + 0.04f * _level;
            Quad(new V3(a0, z), new V3(b0, z), new V3(b0, zf), new V3(a0, zf), new V3(n.X, n.Y, 0), Foliage, KindPlants, flowers, width, zf - z, false);
            var a1 = center + n * 0.02f - dir * (width / 2);
            var b1 = center + n * 0.02f + dir * (width / 2);
            Quad(new V3(a0, zf), new V3(b0, zf), new V3(b1, zf), new V3(a1, zf), new V3(0, 0, 1), Foliage, KindPlants, flowers, width, 0.32f, false);
            var c = center + n * 0.18f;
            if (_level >= 3) // fiori che ricadono davanti al vaso
            {
                var a = c + n * 0.17f - dir * (width / 2 - 0.05f);
                var b = c + n * 0.17f + dir * (width / 2 - 0.05f);
                float z0 = z - 0.45f - 0.05f * _level;
                Quad(new V3(a, z0), new V3(b, z0), new V3(b, z - 0.05f), new V3(a, z - 0.05f), new V3(n.X, n.Y, 0), Foliage, KindPlants, flowers, width, 0.4f, false);
            }
        }

        private void PlantPot(Vec2 c, float z, uint color, float r, float h)
        {
            Box(c, new Vec2(1, 0), r, r, z, z + 0.4f, color, KindProp, 0, true, false);
            Box(c, new Vec2(1, 0), r * 1.25f, r * 1.25f, z + 0.4f, z + 0.4f + h, Foliage, KindPlants, 0.2f + 0.1f * _level);
        }

        // ------------------------------------------------------------------ negozi e locali

        /// <summary>Vetrine sul lato d'ingresso (e sugli altri lati su strada ai livelli alti): tende, insegna, fioriere, luci.</summary>
        private void Storefront(BuildingMesher m, Vec2? entrance, bool awnings, uint signColor = 0)
        {
            int front = FrontEdge(entrance);
            if (front < 0) return;
            float floorH = BuildingMesher.FloorHeight(m.LastWallHeight, m.LastLevels);
            uint awning = Pick(Awnings, 11, 5);
            bool stripes = R(12) < 0.55f;
            uint sign = signColor != 0 ? signColor : Pick(SignColors, 13, 5);
            uint planter = Pick(Planters, 14, 5);
            for (int ei = 0; ei < _edges.Count; ei++)
            {
                var e = _edges[ei];
                bool isFront = ei == front;
                if (!isFront && !(e.Street && _level >= 3 && e.Len > 5f)) continue;
                // in collina la strada può essere più alta della base dell'edificio: tende e insegne seguono il marciapiede
                float g = e.Ground;
                float top = Math.Min(0.80f * floorH + 0.25f, 3.1f);
                if (g + top + 0.3f > m.LastEave)
                {
                    // muro troppo basso sopra la strada (edificio incassato nel pendio): insegna a totem sul marciapiede
                    if (isFront && e.Free > 1.0f)
                    {
                        var t = e.P + e.Dir * Math.Min(1.2f, e.Len * 0.15f) + e.Normal * 0.6f;
                        float zt = Ground(t);
                        Box(t, e.Dir, 0.35f, 0.08f, zt + 0.2f, zt + 1.7f + 0.1f * _level, sign, KindSign, 1);
                        Box(t, e.Dir, 0.05f, 0.05f, zt, zt + 0.2f, 0x333333, KindProp, 0);
                    }
                    continue;
                }
                // tende sopra le vetrine (campate del piano terra), via via su tutte con il livello
                if (awnings)
                {
                    float share = isFront ? ByLevel(0.55f, 1f) / Math.Max(Density, 0.01f) : ByLevel(0.3f, 0.9f) / Math.Max(Density, 0.01f);
                    float depth = Math.Min(1.6f, Math.Max(0.9f, e.Free - 0.3f));
                    for (int bi = 0; bi < e.Bays; bi++)
                    {
                        if (R(ei, bi, 0, 21) > share) continue;
                        var a = e.P + e.Dir * ((bi + 0.08f) * e.Bay);
                        var b = e.P + e.Dir * ((bi + 0.92f) * e.Bay);
                        Awning(a + e.Normal * 0.04f, b + e.Normal * 0.04f, e.Normal, g + top, depth, 0.5f, awning, stripes);
                    }
                }
                // insegna sopra le vetrine (accesa la sera)
                if (isFront || _level >= 4)
                {
                    float w = Math.Min(e.Len * 0.7f, Math.Max(2.4f, e.Bay * (1.4f + 0.3f * _level)));
                    var c = Vec2.Lerp(e.P, e.Q, 0.5f) + e.Normal * 0.08f;
                    float z0 = g + top + 0.15f, z1 = z0 + 0.45f + 0.05f * _level;
                    if (z1 < m.LastEave - 0.15f)
                    {
                        // con il nome vero il pannello resta liscio: la scritta la aggiunge il motore da vicino
                        bool named = isFront && _name != null && Signs != null;
                        Box(c, e.Dir, w / 2, 0.08f, z0, z1, sign, KindSign, named ? 0 : 1);
                        if (named) AddSign(c + e.Normal * 0.08f, e.Normal, w, z0, z1, sign);
                    }
                }
                if (!isFront) continue;
                // fioriere ai lati dell'ingresso
                if (_level >= 2 && e.Free > 1.4f)
                {
                    var mid = Vec2.Lerp(e.P, e.Q, 0.5f);
                    float half = Math.Min(e.Len * 0.5f - 0.5f, e.Bay * 0.5f + 0.4f);
                    PlantPot(mid - e.Dir * half + e.Normal * 0.45f, Ground(mid - e.Dir * half + e.Normal * 0.45f), planter, 0.28f, 0.6f + 0.1f * _level);
                    PlantPot(mid + e.Dir * half + e.Normal * 0.45f, Ground(mid + e.Dir * half + e.Normal * 0.45f), planter, 0.28f, 0.6f + 0.1f * _level);
                }
                // lucine sopra le tende e bandierine colorate
                if (_level >= 4 && g + top + 1.2f < m.LastEave)
                    StringLights(e.P + e.Dir * 0.3f + e.Normal * 0.1f, e.Q - e.Dir * 0.3f + e.Normal * 0.1f, g + top + 1.05f, 0.25f, Math.Max(4, (int)(e.Len / 0.9f)));
                if (_level >= 5 && g + top + 1.6f < m.LastEave)
                    Bunting(e.P + e.Dir * 0.3f + e.Normal * 0.12f, e.Q - e.Dir * 0.3f + e.Normal * 0.12f, g + top + 1.45f, e.Normal);
            }
        }

        /// <summary>Dehors davanti al locale: tavolini, sedie, ombrelloni, fioriere di bordo (più numerosi col livello).</summary>
        private void Dehors(BuildingMesher m, Vec2? entrance)
        {
            int front = FrontEdge(entrance);
            if (front < 0) return;
            var e = _edges[front];
            if (e.Free < 2.3f) return; // marciapiede troppo stretto
            float depth = Math.Min(4.2f, e.Free - 0.4f);
            int rows = depth > 3.4f ? 2 : 1;
            int maxCols = Math.Max(1, (int)((e.Len - 1.2f) / 2.2f));
            int tables = Math.Min(maxCols * rows, Math.Max(1, (int)Math.Round((1 + _level * 1.4f) * Density)));
            int cols = Math.Min(maxCols, (tables + rows - 1) / rows);
            uint umbrella = Pick(Awnings, 31, 5);
            bool stripes = R(32) < 0.5f;
            uint tableColor = R(33) < 0.5f ? Wood : 0xD9D9D9;
            float start = (e.Len - (cols - 1) * 2.2f) / 2;
            for (int t = 0; t < tables; t++)
            {
                int col = t / rows, r = t % rows;
                if (col >= cols) break;
                var c = e.P + e.Dir * (start + col * 2.2f) + e.Normal * (1.3f + r * 1.7f);
                float z = Ground(c);
                Table(c, e.Dir, z, tableColor);
                if (_level >= 2) Umbrella(c, z, 1.15f, umbrella, stripes);
            }
            // bordo del dehors: fioriere basse (livello 3+), lampioncini (livello 5)
            if (_level >= 3)
            {
                float d = Math.Min(depth, 1.3f + (rows - 1) * 1.7f + 1.0f);
                var a = e.P + e.Dir * 0.4f + e.Normal * d;
                var b = e.Q - e.Dir * 0.4f + e.Normal * d;
                float len = (b - a).Length;
                for (float s = 0; s + 1.6f <= len; s += 2.4f)
                {
                    var c = a + e.Dir * (s + 0.8f);
                    float z = Ground(c);
                    Box(c, e.Dir, 0.8f, 0.22f, z, z + 0.45f, 0xE6E1D6, KindProp, 0);
                    Box(c, e.Dir, 0.75f, 0.2f, z + 0.45f, z + 0.85f, Foliage, KindPlants, 0.3f + 0.1f * _level);
                }
                if (_level >= 5)
                {
                    Lamp(a, Ground(a));
                    Lamp(b, Ground(b));
                }
            }
        }

        private void PharmacyCross(BuildingMesher m, Vec2? entrance)
        {
            int front = FrontEdge(entrance);
            if (front < 0) return;
            var e = _edges[front];
            float floorH = BuildingMesher.FloorHeight(m.LastWallHeight, m.LastLevels);
            float z = Math.Min(m.LastEave - 0.7f, e.Ground + Math.Max(3.2f, floorH * 1.25f));
            var c = e.P + e.Dir * Math.Min(1.2f, e.Len * 0.2f) + e.Normal * 0.65f;
            // croce verde "a bandiera", perpendicolare alla facciata, accesa
            Box(c, e.Normal, 0.5f, 0.07f, z - 0.17f, z + 0.17f, 0x22B14C, KindLight, 1);
            Box(c, e.Normal, 0.17f, 0.07f, z - 0.5f, z + 0.5f, 0x22B14C, KindLight, 1);
            Box(c - e.Normal * 0.55f, e.Normal, 0.08f, 0.04f, z - 0.05f, z + 0.05f, 0x444444, KindProp, 0);
        }

        private void Hotel(BuildingMesher m, Vec2? entrance)
        {
            int front = FrontEdge(entrance);
            if (front < 0) return;
            var e = _edges[front];
            float floorH = BuildingMesher.FloorHeight(m.LastWallHeight, m.LastLevels);
            float g = e.Ground;
            var mid = Vec2.Lerp(e.P, e.Q, 0.5f);
            // pensilina d'ingresso
            float w = Math.Min(e.Len * 0.5f, 3.5f + 0.4f * _level);
            float depth = Math.Min(3f, Math.Max(1.2f, e.Free - 0.3f));
            Box(mid + e.Normal * (depth / 2), e.Dir, w / 2, depth / 2, g + floorH * 0.85f, g + floorH * 0.85f + 0.3f, 0x2B2B2B, KindProp, 0);
            // insegna verticale accesa
            float top = m.LastEave - 0.4f;
            Box(e.P + e.Dir * Math.Min(1f, e.Len * 0.15f) + e.Normal * 0.45f, e.Normal, 0.35f, 0.08f, Math.Max(g + floorH, top - 2.5f - 0.3f * _level), top,
                Pick(SignColors, 41, 3), KindSign, 1);
            // bandiere (più numerose col livello)
            int flags = 1 + _level / 2;
            for (int i = 0; i < flags; i++)
            {
                float t = (i + 1f) / (flags + 1);
                var c = Vec2.Lerp(e.P, e.Q, t) + e.Normal * 0.1f;
                Flag(c, e.Normal, g + floorH + 0.3f, i);
            }
            Storefront(m, entrance, false);
        }

        private void Public(BuildingMesher m, Vec2? entrance, DressKind kind)
        {
            int front = FrontEdge(entrance);
            if (front < 0) return;
            var e = _edges[front];
            float floorH = BuildingMesher.FloorHeight(m.LastWallHeight, m.LastLevels);
            float g = e.Ground;
            var mid = Vec2.Lerp(e.P, e.Q, 0.5f);
            // bandiere italiana ed europea sulla facciata
            Flag(mid - e.Dir * 0.8f + e.Normal * 0.1f, e.Normal, g + floorH + 0.3f, 100);
            Flag(mid + e.Dir * 0.8f + e.Normal * 0.1f, e.Normal, g + floorH + 0.3f, 101);
            if (kind == DressKind.Hospital)
            {
                float z = Math.Min(m.LastEave - 0.8f, g + floorH * 1.6f);
                var c = e.P + e.Dir * Math.Min(2f, e.Len * 0.25f) + e.Normal * 0.1f;
                Box(c, e.Dir, 0.6f, 0.06f, z - 0.2f, z + 0.2f, 0xE53935, KindLight, 1);
                Box(c, e.Dir, 0.2f, 0.06f, z - 0.6f, z + 0.6f, 0xE53935, KindLight, 1);
            }
            else
            {
                float w = Math.Min(e.Len * 0.6f, 5f);
                float z0 = g + floorH * 0.86f + 0.2f, z1 = g + floorH * 0.86f + 0.7f;
                bool named = _name != null && Signs != null;
                Box(mid + e.Normal * 0.08f, e.Dir, w / 2, 0.07f, z0, z1, 0x1C2E5A, KindSign, named ? 0 : 1);
                if (named) AddSign(mid + e.Normal * 0.15f, e.Normal, w, z0, z1, 0x1C2E5A);
            }
            if (_level >= 2 && e.Free > 2f)
            {
                PlantPot(mid - e.Dir * 2.2f + e.Normal * 0.6f, Ground(mid - e.Dir * 2.2f + e.Normal * 0.6f), 0xE6E1D6, 0.35f, 0.9f);
                PlantPot(mid + e.Dir * 2.2f + e.Normal * 0.6f, Ground(mid + e.Dir * 2.2f + e.Normal * 0.6f), 0xE6E1D6, 0.35f, 0.9f);
            }
            if (_level >= 4 && e.Free > 2.5f)
            {
                Lamp(e.P + e.Dir * 0.8f + e.Normal * 1.2f, Ground(e.P + e.Dir * 0.8f + e.Normal * 1.2f));
                Lamp(e.Q - e.Dir * 0.8f + e.Normal * 1.2f, Ground(e.Q - e.Dir * 0.8f + e.Normal * 1.2f));
            }
            UpperFloors(m, 0.3f);
        }

        // ------------------------------------------------------------------ industria e campagna

        private void Industrial(BuildingMesher m)
        {
            int front = FrontEdge(null);
            if (front < 0) return;
            var e = _edges[front];
            float g = m.LastGroundRef;
            // portoni di carico sul lato lungo
            int doors = Math.Max(1, Math.Min(4, (int)(e.Len / 12f)));
            for (int i = 0; i < doors; i++)
            {
                var c = e.P + e.Dir * (e.Len * (i + 1f) / (doors + 1)) + e.Normal * 0.05f;
                float h = Math.Min(4.5f, m.LastWallHeight - 0.8f);
                if (h < 2.5f) break;
                Box(c, e.Dir, 2.0f, 0.06f, g, g + h, 0x8A9096, KindProp, 0);
                Box(c + e.Normal * 0.07f, e.Dir, 2.1f, 0.02f, g + h, g + h + 0.15f, 0xF2B705, KindProp, 0);
            }
            // fascia con l'insegna in alto
            if (m.LastWallHeight > 5f)
            {
                float w = Math.Min(e.Len * 0.5f, 12f);
                Box(Vec2.Lerp(e.P, e.Q, 0.5f) + e.Normal * 0.08f, e.Dir, w / 2, 0.07f, g + m.LastWallHeight - 1.6f, g + m.LastWallHeight - 0.5f,
                    Pick(SignColors, 51, 7), KindSign, 1);
            }
        }

        private void Farm(BuildingMesher m)
        {
            int front = FrontEdge(null);
            if (front < 0) return;
            var e = _edges[front];
            int bales = Math.Min(8, 1 + _level * 2);
            for (int i = 0; i < bales; i++)
            {
                float along = (i % 4) * 1.6f + 1f;
                if (along > e.Len - 1f) break;
                var c = e.P + e.Dir * along + e.Normal * (1.8f + (i / 4) * 1.5f);
                float z = Ground(c);
                Cylinder(c, 0.65f, z, z + 1.2f, 0xD9B44A, 8);
            }
            if (_level >= 3)
                PlantPot(e.Q - e.Dir * 1.2f + e.Normal * 0.8f, Ground(e.Q - e.Dir * 1.2f + e.Normal * 0.8f), 0x8A5A3B, 0.4f, 0.7f);
        }

        // ------------------------------------------------------------------ pensiline (benzinai, tettoie)

        private void Canopy(BuildingInfo b, BuildingMesher m, bool fuel)
        {
            var ring = m.LastOuter;
            var box = OrientedBox.Of(ring);
            float top = m.LastGroundRef + Math.Max(4.2f, Math.Min(7f, b.Height));
            var ax = box.U;
            var ay = box.V;
            float halfX = box.HalfU, halfY = box.HalfV;
            // pilastri
            float hx = Math.Max(0.5f, halfX - 1.2f), hy = Math.Max(0.5f, halfY - 1.2f);
            int nx = halfX > 9 ? 3 : 2;
            for (int i = 0; i < nx; i++)
            {
                float t = nx == 1 ? 0 : -hx + 2 * hx * i / (nx - 1);
                foreach (var s in new[] { -1f, 1f })
                {
                    var c = box.Center + ax * t + ay * (s * hy);
                    Box(c, ax, 0.22f, 0.22f, Ground(c), top - 0.7f, 0xE8E8E8, KindProp, 0);
                }
            }
            // fascia colorata del bordo della pensilina
            uint band = fuel ? Pick(new uint[] { 0x1F5FA8, 0xC8372D, 0x2E7D4F, 0xE8A317 }, 61, 3) : 0x9AA3A8;
            Box(box.Center, ax, halfX + 0.05f, halfY + 0.05f, top - 0.75f, top - 0.05f, band, KindProp, 0);
            if (!fuel) return;
            // isole con le pompe sotto la pensilina
            int islands = Math.Max(1, Math.Min(4, (int)(halfX * 2 / 6f)));
            for (int i = 0; i < islands; i++)
            {
                float t = islands == 1 ? 0 : -halfX * 0.6f + halfX * 1.2f * i / (islands - 1);
                var c = box.Center + ax * t;
                float z = Ground(c);
                Box(c, ay, 1.6f, 0.45f, z, z + 0.18f, 0xCFCFCF, KindProp, 0);
                foreach (var s in new[] { -0.8f, 0.8f })
                {
                    var p = c + ay * s;
                    Box(p, ay, 0.35f, 0.25f, z + 0.18f, z + 1.75f, 0xF2F2F2, KindProp, 0);
                    Box(p, ay, 0.36f, 0.26f, z + 1.45f, z + 1.75f, band, KindLight, 0.6f);
                }
            }
            // totem dei prezzi
            if (_level >= 2)
            {
                var c = box.Center + ax * (halfX + 2.5f) + ay * halfY;
                float z = Ground(c);
                Box(c, ax, 0.5f, 0.15f, z, z + 4f + 0.4f * _level, band, KindSign, 1);
            }
        }

        // ------------------------------------------------------------------ tetti

        private void Roof(BuildingMesher m, DressKind kind)
        {
            var ring = m.LastOuter;
            float eave = m.LastEave;
            bool dwelling = m.LastCategory == BuildingMesher.Category.Residential || m.LastCategory == BuildingMesher.Category.Commercial;
            // gronde e cornicioni solo sui profili di qualità pieni (sui telefoni via web restano i volumi semplici)
            bool relief = Density >= 0.6f;
            if (relief && dwelling && m.LastPitched) Eaves(m);
            if (relief && dwelling && m.LastFlatRoof && m.LastLevels >= 2) Cornice(m);
            if (m.LastFlatRoof)
            {
                // parapetto lungo il bordo: faccia esterna, cima e faccia interna (niente testate)
                uint wall = MeshBuffer.Rgb(Lighten(m.LastWallColor, 0.06f));
                for (int i = 0; i < ring.Count; i++)
                {
                    var p = ring[i];
                    var q = ring[(i + 1) % ring.Count];
                    var d = q - p;
                    float len = d.Length;
                    if (len < 0.3f) continue;
                    var dir = d * (1f / len);
                    var inward = new Vec2(-dir.Y, dir.X);
                    Side(p, q, eave, eave + 0.85f, wall, KindProp, 0);
                    Side(q + inward * 0.25f, p + inward * 0.25f, eave, eave + 0.85f, wall, KindProp, 0);
                    Quad(new V3(p, eave + 0.85f), new V3(q, eave + 0.85f), new V3(q + inward * 0.25f, eave + 0.85f), new V3(p + inward * 0.25f, eave + 0.85f),
                        new V3(0, 0, 1), m.LastWallColor, KindProp, 0, len, 0.25f, false);
                }
                float area = Math.Abs(Polygon2D.SignedArea(ring));
                if (area < 80) return;
                // impianti sul tetto: condizionatori, cisterne, antenne (pochi)
                int units = Math.Min(Density < 0.6f ? 1 : 3, 1 + (int)(area / 300f));
                for (int i = 0; i < units; i++)
                {
                    if (!InsidePoint(ring, i, 1.2f, out var c)) continue;
                    float r = R(i, 0, 0, 71);
                    if (r < 0.5f) Box(c, new Vec2(1, 0), 0.5f, 0.4f, eave, eave + 0.8f, 0xD5D8DA, KindProp, 0);
                    else if (r < 0.8f) Box(c, new Vec2(1, 0), 0.5f, 0.5f, eave, eave + 1.3f, 0xE8E8E8, KindProp, 0);
                    else Box(c, new Vec2(1, 0), 0.03f, 0.03f, eave, eave + 2.5f, 0x666666, KindProp, 0, true, false);
                }
                // terrazza: piante e pergolato ai livelli alti; pannelli solari sui tetti grandi
                if (kind == DressKind.Industrial)
                {
                    if (_level >= 2 && area > 300) SolarRows(ring, eave, area);
                    return;
                }
                int pots = (int)Math.Round(ByLevel(0, 8));
                for (int i = 0; i < pots; i++)
                    if (InsidePoint(ring, 20 + i, 0.8f, out var c)) PlantPot(c, eave, 0xB5653F, 0.3f, 0.6f);
                if (_level >= 4 && area > 60 && InsidePoint(ring, 40, 2.5f, out var pc)) Pergola(pc, eave);
            }
            else if (m.LastCategory == BuildingMesher.Category.Residential || m.LastCategory == BuildingMesher.Category.Rural)
            {
                // comignoli sulle falde
                float area = Math.Abs(Polygon2D.SignedArea(ring));
                if (area < 40) return;
                int chimneys = Math.Min(2, 1 + (int)(area / 200f));
                for (int i = 0; i < chimneys; i++)
                {
                    if (!InsidePoint(ring, 80 + i, 1.5f, out var c)) continue;
                    float z = m.RoofTopAt(c);
                    Box(c, new Vec2(1, 0), 0.32f, 0.32f, z - 0.4f, z + 1.15f, 0xC9B79A, KindProp, 0);
                }
            }
        }

        /// <summary>Gronda in coppi che sporge oltre i muri lungo i lati di gronda (non sui timpani), con il sottogronda.</summary>
        private void Eaves(BuildingMesher m)
        {
            var ring = m.LastOuter;
            float eave = m.LastEave;
            const float over = 0.5f, drop = 0.24f;
            uint under = Darken(Lighten(m.LastWallColor, 0.1f), 0.12f);
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                var d = q - p;
                float len = d.Length;
                if (len < 1.5f) continue;
                // lato di gronda: la falda è alla quota di gronda per tutto il lato (sui timpani sale)
                if (m.RoofTopAt(p) - eave > 0.08f || m.RoofTopAt(q) - eave > 0.08f || m.RoofTopAt(Vec2.Lerp(p, q, 0.5f)) - eave > 0.08f) continue;
                var dir = d * (1f / len);
                var n = new Vec2(dir.Y, -dir.X);
                var p1 = p + n * over - dir * 0.05f;
                var q1 = q + n * over + dir * 0.05f;
                // falda prolungata (coppi, tipo 1 dello shader) e sottogronda chiaro
                QuadKind(new V3(p, eave + 0.02f), new V3(q, eave + 0.02f), new V3(q1, eave - drop), new V3(p1, eave - drop),
                    new V3(n.X * 0.4f, n.Y * 0.4f, 1), m.LastRoofColor, 1, len, over);
                Quad(new V3(p, eave - 0.06f), new V3(q, eave - 0.06f), new V3(q1, eave - drop - 0.06f), new V3(p1, eave - drop - 0.06f),
                    new V3(0, 0, -1), under, KindProp, 0, len, over, false);
            }
        }

        /// <summary>Cornicione in rilievo sotto il parapetto dei tetti piani.</summary>
        private void Cornice(BuildingMesher m)
        {
            var ring = m.LastOuter;
            float eave = m.LastEave;
            uint color = Lighten(m.LastWallColor, 0.14f);
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                var d = q - p;
                float len = d.Length;
                if (len < 1.5f) continue;
                var dir = d * (1f / len);
                var n = new Vec2(dir.Y, -dir.X);
                var p1 = p + n * 0.22f;
                var q1 = q + n * 0.22f;
                // fronte, cima (inclinata verso fuori) e sotto
                Quad(new V3(p1, eave - 0.42f), new V3(q1, eave - 0.42f), new V3(q1, eave - 0.12f), new V3(p1, eave - 0.12f),
                    new V3(n.X, n.Y, 0), color, KindProp, 0, len, 0.3f, false);
                Quad(new V3(p1, eave - 0.12f), new V3(q1, eave - 0.12f), new V3(q, eave), new V3(p, eave),
                    new V3(n.X * 0.3f, n.Y * 0.3f, 1), color, KindProp, 0, len, 0.25f, false);
            }
        }

        private void SolarRows(List<Vec2> ring, float eave, float area)
        {
            int panels = Math.Min(16, (int)(area / 60f) * (_level - 1));
            for (int i = 0; i < panels; i++)
            {
                if (!InsidePoint(ring, 200 + i, 2f, out var c)) continue;
                Tilted(c, new Vec2(1, 0), 1.0f, 0.8f, eave + 0.4f, 0.5f, 0x1E2A44);
            }
        }

        private void Pergola(Vec2 c, float z)
        {
            var a = new Vec2(1, 0);
            var bAx = new Vec2(0, 1);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sy in new[] { -1f, 1f })
                Box(c + a * (1.4f * sx) + bAx * (1.4f * sy), a, 0.06f, 0.06f, z, z + 2.3f, Wood, KindProp, 0);
            for (int i = -2; i <= 2; i++)
                Box(c + a * (i * 0.65f), a, 0.04f, 1.6f, z + 2.3f, z + 2.42f, Wood, KindProp, 0);
            Box(c, a, 1.5f, 1.5f, z + 2.42f, z + 2.55f, Foliage, KindPlants, 0.5f);
        }

        // ------------------------------------------------------------------ oggetti

        private void Table(Vec2 c, Vec2 dir, float z, uint color)
        {
            Box(c, dir, 0.38f, 0.38f, z + 0.45f, z + 0.75f, color, KindProp, 0);
            Box(c - dir * 0.66f, dir, 0.2f, 0.2f, z, z + 0.9f, color, KindProp, 0);
            Box(c + dir * 0.66f, dir, 0.2f, 0.2f, z, z + 0.9f, color, KindProp, 0);
        }

        private void Umbrella(Vec2 c, float z, float r, uint color, bool stripes)
        {
            Box(c, new Vec2(1, 0), 0.025f, 0.025f, z, z + 2.3f, 0xEEEEEE, KindProp, 0);
            const int sides = 8;
            var apex = new V3(c, z + 2.55f);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides;
                var p0 = new V3(c.X + (float)Math.Cos(a0) * r, c.Y + (float)Math.Sin(a0) * r, z + 2.2f);
                var p1 = new V3(c.X + (float)Math.Cos(a1) * r, c.Y + (float)Math.Sin(a1) * r, z + 2.2f);
                uint col = stripes && (i & 1) == 1 ? 0xF4EEDF : color;
                Tri(p0, p1, apex, col, KindCanvas, 0, true);
            }
        }

        /// <summary>Tenda inclinata tra a e b (sul muro, quota zTop), sporge di depth e scende di drop; mantovana davanti.</summary>
        private void Awning(Vec2 a, Vec2 b, Vec2 n, float zTop, float depth, float drop, uint color, bool stripes)
        {
            float w = (b - a).Length;
            float k = stripes ? 1 : 0;
            var a0 = new V3(a, zTop);
            var b0 = new V3(b, zTop);
            var a1 = new V3(a + n * depth, zTop - drop);
            var b1 = new V3(b + n * depth, zTop - drop);
            Quad(a0, b0, b1, a1, new V3(n.X * 0.3f, n.Y * 0.3f, 1), color, KindCanvas, k, w, depth, true);
            // mantovana
            var a2 = new V3(a + n * depth, zTop - drop - 0.28f);
            var b2 = new V3(b + n * depth, zTop - drop - 0.28f);
            Quad(a2, b2, b1, a1, new V3(n.X, n.Y, 0), color, KindCanvas, k, w, 0.28f, true);
            // fianchi
            Tri(a0, a1, new V3(a + n * depth, zTop), color, KindCanvas, 0, true);
            Tri(b0, b1, new V3(b + n * depth, zTop), color, KindCanvas, 0, true);
        }

        private void StringLights(Vec2 a, Vec2 b, float z, float sag, int bulbs)
        {
            bulbs = Math.Max(2, Math.Min(24, bulbs));
            var dir = b - a;
            float len = dir.Length;
            if (len < 0.5f) return;
            var u = dir * (1f / len);
            for (int i = 0; i < bulbs; i++)
            {
                float t = (i + 0.5f) / bulbs;
                float zz = z - sag * 4 * t * (1 - t);
                Box(Vec2.Lerp(a, b, t), u, 0.06f, 0.06f, zz - 0.12f, zz, 0xFFE3A3, KindLight, 1);
            }
            Box(Vec2.Lerp(a, b, 0.5f), u, len / 2, 0.01f, z - sag, z - sag + 0.02f, 0x222222, KindProp, 0);
        }

        /// <summary>Festone di bandierine triangolari colorate.</summary>
        private void Bunting(Vec2 a, Vec2 b, float z, Vec2 n)
        {
            var dir = b - a;
            float len = dir.Length;
            if (len < 1) return;
            var u = dir * (1f / len);
            int count = Math.Min(30, (int)(len / 0.45f));
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                float zz = z - 0.35f * 4 * t * (1 - t);
                var c = Vec2.Lerp(a, b, t);
                uint col = Awnings[i % Awnings.Length];
                Tri(new V3(c - u * 0.17f, zz), new V3(c + u * 0.17f, zz), new V3(c, zz - 0.32f), col, KindCanvas, 0, true);
            }
        }

        private void Flag(Vec2 c, Vec2 n, float z, int which)
        {
            // asta inclinata che esce dalla facciata, telo appeso
            var tip = c + n * 1.4f;
            Box(Vec2.Lerp(c, tip, 0.5f), n, 0.7f, 0.025f, z + 0.6f, z + 0.65f, 0xDDDDDD, KindProp, 0);
            var side = new Vec2(-n.Y, n.X);
            float h = 1.1f;
            if (which == 100 || which == 101)
            {
                // italiana (verde, bianco, rosso) o europea (blu)
                uint[] cols = which == 100 ? new uint[] { 0x009246, 0xF1F2F1, 0xCE2B37 } : new uint[] { 0x003399, 0x003399, 0x003399 };
                for (int k = 0; k < 3; k++)
                {
                    var p0 = c + n * (0.3f + k * 0.35f);
                    var p1 = c + n * (0.65f + k * 0.35f);
                    Quad(new V3(p0, z + 0.62f - h), new V3(p1, z + 0.62f - h), new V3(p1, z + 0.62f), new V3(p0, z + 0.62f),
                        new V3(side.X, side.Y, 0), cols[k], KindCanvas, 0, 0.35f, h, true);
                }
                return;
            }
            var col = Awnings[(which + (int)(_seed * 7)) % Awnings.Length];
            Quad(new V3(c + n * 0.3f, z + 0.62f - h), new V3(tip, z + 0.62f - h), new V3(tip, z + 0.62f), new V3(c + n * 0.3f, z + 0.62f),
                new V3(side.X, side.Y, 0), col, KindCanvas, 0, 1.1f, h, true);
        }

        private void Lamp(Vec2 c, float z)
        {
            Box(c, new Vec2(1, 0), 0.05f, 0.05f, z, z + 3.2f, 0x2B2B2B, KindProp, 0);
            Box(c, new Vec2(1, 0), 0.16f, 0.16f, z + 3.2f, z + 3.6f, 0xFFE3A3, KindLight, 1);
            Box(c, new Vec2(1, 0), 0.2f, 0.2f, z + 3.6f, z + 3.7f, 0x2B2B2B, KindProp, 0);
        }

        private void AddSign(Vec2 front, Vec2 n, float width, float z0, float z1, uint background) =>
            Signs.Add(new SignInfo
            {
                X = front.X, Y = front.Y, Z = (z0 + z1) / 2, NormalX = n.X, NormalY = n.Y, Width = width, Height = z1 - z0,
                Text = _name, Background = background,
            });

        // ------------------------------------------------------------------ primitive

        private float Ground(Vec2 p) => _ground != null ? _ground(p.X, p.Y) : 0;

        /// <summary>Punto interno al poligono, ad almeno <paramref name="margin"/> m dal bordo (deterministico).</summary>
        private bool InsidePoint(List<Vec2> ring, int salt, float margin, out Vec2 p)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in ring)
            {
                minX = Math.Min(minX, v.X);
                minY = Math.Min(minY, v.Y);
                maxX = Math.Max(maxX, v.X);
                maxY = Math.Max(maxY, v.Y);
            }
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var c = new Vec2(minX + (maxX - minX) * R(salt, attempt, 1, 91), minY + (maxY - minY) * R(salt, attempt, 2, 91));
                if (!Contains(ring, c)) continue;
                bool ok = true;
                for (int i = 0; i < ring.Count && ok; i++)
                    if (DistanceToSegment(c, ring[i], ring[(i + 1) % ring.Count]) < margin) ok = false;
                if (!ok) continue;
                p = c;
                return true;
            }
            p = default;
            return false;
        }

        private static bool Contains(List<Vec2> ring, Vec2 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static float DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            float l2 = Vec2.Dot(ab, ab);
            float t = l2 > 1e-6f ? Math.Max(0, Math.Min(1, Vec2.Dot(p - a, ab) / l2)) : 0;
            return (p - (a + ab * t)).Length;
        }

        private static uint Lighten(uint rgb, float k)
        {
            uint r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            r = (uint)Math.Min(255, r + (255 - r) * k);
            g = (uint)Math.Min(255, g + (255 - g) * k);
            b = (uint)Math.Min(255, b + (255 - b) * k);
            return (r << 16) | (g << 8) | b;
        }

        /// <summary>Parallelepipedo orientato: centro, asse lungo (unitario), mezze misure lungo l'asse e di traverso, quote.</summary>
        private void Box(Vec2 c, Vec2 axis, float hu, float hv, float z0, float z1, uint color, float kind, float param) =>
            Box(c, axis, hu, hv, z0, z1, color, kind, param, true, true);

        /// <param name="back">faccia sul lato +traverso (contro il muro per gli oggetti appesi alla facciata)</param>
        private void Box(Vec2 c, Vec2 axis, float hu, float hv, float z0, float z1, uint color, float kind, float param, bool back, bool top)
        {
            var side = new Vec2(-axis.Y, axis.X);
            var p0 = c - axis * hu - side * hv;
            var p1 = c + axis * hu - side * hv;
            var p2 = c + axis * hu + side * hv;
            var p3 = c - axis * hu + side * hv;
            uint col = MeshBuffer.Rgb(color & 0xFFFFFF);
            Side(p0, p1, z0, z1, col, kind, param);
            Side(p1, p2, z0, z1, col, kind, param);
            if (back) Side(p2, p3, z0, z1, col, kind, param);
            Side(p3, p0, z0, z1, col, kind, param);
            if (!top) return;
            int t0 = _mb.Add(p0.X, p0.Y, z1, 0, 0, 1, col, 0, 0, 2 * hu, 2 * hv, kind, _seed, param, 0);
            int t1 = _mb.Add(p1.X, p1.Y, z1, 0, 0, 1, col, 2 * hu, 0, 2 * hu, 2 * hv, kind, _seed, param, 0);
            int t2 = _mb.Add(p2.X, p2.Y, z1, 0, 0, 1, col, 2 * hu, 2 * hv, 2 * hu, 2 * hv, kind, _seed, param, 0);
            int t3 = _mb.Add(p3.X, p3.Y, z1, 0, 0, 1, col, 0, 2 * hv, 2 * hu, 2 * hv, kind, _seed, param, 0);
            _mb.Quad(t0, t1, t2, t3);
        }

        /// <summary>Faccia verticale tra a e b (anello antiorario: normale a destra = esterno). uv0 = (u, v, larghezza, altezza).</summary>
        private void Side(Vec2 a, Vec2 b, float z0, float z1, uint col, float kind, float param)
        {
            var d = b - a;
            float len = d.Length;
            if (len < 1e-4f) return;
            var n = new Vec2(d.Y / len, -d.X / len);
            float h = z1 - z0;
            int i0 = _mb.Add(a.X, a.Y, z0, n.X, n.Y, 0, col, 0, 0, len, h, kind, _seed, param, 0);
            int i1 = _mb.Add(b.X, b.Y, z0, n.X, n.Y, 0, col, len, 0, len, h, kind, _seed, param, 0);
            int i2 = _mb.Add(b.X, b.Y, z1, n.X, n.Y, 0, col, len, h, len, h, kind, _seed, param, 0);
            int i3 = _mb.Add(a.X, a.Y, z1, n.X, n.Y, 0, col, 0, h, len, h, kind, _seed, param, 0);
            _mb.Quad(i0, i1, i2, i3);
        }

        private void Cylinder(Vec2 c, float r, float z0, float z1, uint color, int sides)
        {
            uint col = MeshBuffer.Rgb(color & 0xFFFFFF);
            int center = _mb.Add(c.X, c.Y, z1, 0, 0, 1, col, 0, 0, 0, 0, KindProp, _seed, 0, 0);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides;
                var p0 = c + new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0)) * r;
                var p1 = c + new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1)) * r;
                Side(p0, p1, z0, z1, col, KindProp, 0);
                int t0 = _mb.Add(p0.X, p0.Y, z1, 0, 0, 1, col, 0, 0, 0, 0, KindProp, _seed, 0, 0);
                int t1 = _mb.Add(p1.X, p1.Y, z1, 0, 0, 1, col, 0, 0, 0, 0, KindProp, _seed, 0, 0);
                _mb.Triangle(center, t0, t1);
            }
        }

        /// <summary>Pannello inclinato (solare) appoggiato su supporti.</summary>
        private void Tilted(Vec2 c, Vec2 axis, float hu, float hv, float z, float rise, uint color)
        {
            var side = new Vec2(-axis.Y, axis.X);
            var a = new V3(c - axis * hu - side * hv, z);
            var b = new V3(c + axis * hu - side * hv, z);
            var cc = new V3(c + axis * hu + side * hv, z + rise);
            var d = new V3(c - axis * hu + side * hv, z + rise);
            Quad(a, b, cc, d, new V3(0, -0.5f, 1), color, KindSolar, 0, 2 * hu, 2 * hv, false);
            Box(c + side * hv * 0.8f, axis, hu * 0.9f, 0.03f, z - 0.4f, z + rise, 0x9AA0A6, KindProp, 0);
        }

        private void Tri(V3 a, V3 b, V3 c, uint color, float kind, float param, bool twoSided)
        {
            uint col = MeshBuffer.Rgb(color & 0xFFFFFF);
            var n = V3.Cross(b - a, c - a);
            float l = (float)Math.Sqrt(V3.Dot(n, n));
            if (l < 1e-6f) return;
            n = new V3(n.X / l, n.Y / l, n.Z / l);
            if (n.Z < 0 && twoSided)
            {
                var t = b;
                b = c;
                c = t;
                n = new V3(-n.X, -n.Y, -n.Z);
            }
            int i0 = _mb.Add(a.X, a.Y, a.Z, n.X, n.Y, n.Z, col, 0, 0, 1, 1, kind, _seed, param, 0);
            int i1 = _mb.Add(b.X, b.Y, b.Z, n.X, n.Y, n.Z, col, 1, 0, 1, 1, kind, _seed, param, 0);
            int i2 = _mb.Add(c.X, c.Y, c.Z, n.X, n.Y, n.Z, col, 0.5f, 1, 1, 1, kind, _seed, param, 0);
            _mb.Triangle(i0, i1, i2);
            if (twoSided)
            {
                int j0 = _mb.Add(a.X, a.Y, a.Z, -n.X, -n.Y, -n.Z, col, 0, 0, 1, 1, kind, _seed, param, 0);
                int j1 = _mb.Add(b.X, b.Y, b.Z, -n.X, -n.Y, -n.Z, col, 1, 0, 1, 1, kind, _seed, param, 0);
                int j2 = _mb.Add(c.X, c.Y, c.Z, -n.X, -n.Y, -n.Z, col, 0.5f, 1, 1, 1, kind, _seed, param, 0);
                _mb.Triangle(j0, j2, j1);
            }
        }

        /// <summary>Quadrilatero a-b-c-d rivolto verso <paramref name="facing"/>; uv0 = (u lungo a→b, v lungo b→c, larghezza, altezza).</summary>
        private void Quad(V3 a, V3 b, V3 c, V3 d, V3 facing, uint color, float kind, float param, float w, float h, bool twoSided)
        {
            uint col = MeshBuffer.Rgb(color & 0xFFFFFF);
            var n = V3.Cross(b - a, c - a);
            float l = (float)Math.Sqrt(V3.Dot(n, n));
            if (l < 1e-6f) return;
            n = new V3(n.X / l, n.Y / l, n.Z / l);
            bool flip = V3.Dot(n, facing) < 0;
            if (flip) n = new V3(-n.X, -n.Y, -n.Z);
            int i0 = _mb.Add(a.X, a.Y, a.Z, n.X, n.Y, n.Z, col, 0, 0, w, h, kind, _seed, param, 0);
            int i1 = _mb.Add(b.X, b.Y, b.Z, n.X, n.Y, n.Z, col, w, 0, w, h, kind, _seed, param, 0);
            int i2 = _mb.Add(c.X, c.Y, c.Z, n.X, n.Y, n.Z, col, w, h, w, h, kind, _seed, param, 0);
            int i3 = _mb.Add(d.X, d.Y, d.Z, n.X, n.Y, n.Z, col, 0, h, w, h, kind, _seed, param, 0);
            if (flip) _mb.Quad(i0, i3, i2, i1);
            else _mb.Quad(i0, i1, i2, i3);
            if (!twoSided) return;
            // lato di sotto/dietro, un po' più scuro
            uint dark = MeshBuffer.Rgb(Darken(color & 0xFFFFFF, 0.25f));
            int j0 = _mb.Add(a.X, a.Y, a.Z, -n.X, -n.Y, -n.Z, dark, 0, 0, w, h, kind, _seed, param, 0);
            int j1 = _mb.Add(b.X, b.Y, b.Z, -n.X, -n.Y, -n.Z, dark, w, 0, w, h, kind, _seed, param, 0);
            int j2 = _mb.Add(c.X, c.Y, c.Z, -n.X, -n.Y, -n.Z, dark, w, h, w, h, kind, _seed, param, 0);
            int j3 = _mb.Add(d.X, d.Y, d.Z, -n.X, -n.Y, -n.Z, dark, 0, h, w, h, kind, _seed, param, 0);
            if (flip) _mb.Quad(j0, j1, j2, j3);
            else _mb.Quad(j0, j3, j2, j1);
        }

        /// <summary>Quadrilatero di un tipo di superficie qualsiasi con uv0 in metri (x lungo a→b, y lungo b→c).</summary>
        private void QuadKind(V3 a, V3 b, V3 c, V3 d, V3 facing, uint color, float kind, float w, float h)
        {
            uint col = MeshBuffer.Rgb(color & 0xFFFFFF);
            var n = V3.Cross(b - a, c - a);
            float l = (float)Math.Sqrt(V3.Dot(n, n));
            if (l < 1e-6f) return;
            n = new V3(n.X / l, n.Y / l, n.Z / l);
            bool flip = V3.Dot(n, facing) < 0;
            if (flip) n = new V3(-n.X, -n.Y, -n.Z);
            int i0 = _mb.Add(a.X, a.Y, a.Z, n.X, n.Y, n.Z, col, 0, 0, w, h, kind, _seed, 1, 0);
            int i1 = _mb.Add(b.X, b.Y, b.Z, n.X, n.Y, n.Z, col, w, 0, w, h, kind, _seed, 1, 0);
            int i2 = _mb.Add(c.X, c.Y, c.Z, n.X, n.Y, n.Z, col, w, h, w, h, kind, _seed, 1, 0);
            int i3 = _mb.Add(d.X, d.Y, d.Z, n.X, n.Y, n.Z, col, 0, h, w, h, kind, _seed, 1, 0);
            if (flip) _mb.Quad(i0, i3, i2, i1);
            else _mb.Quad(i0, i1, i2, i3);
        }

        private static uint Darken(uint rgb, float k)
        {
            uint r = (uint)(((rgb >> 16) & 0xFF) * (1 - k)), g = (uint)(((rgb >> 8) & 0xFF) * (1 - k)), b = (uint)((rgb & 0xFF) * (1 - k));
            return (r << 16) | (g << 8) | b;
        }
    }
}
