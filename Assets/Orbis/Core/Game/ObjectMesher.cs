using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Game
{
    /// <summary>
    /// Modelli procedurali degli elementi del catalogo, per famiglia (docs/08: niente asset esterni in M2).
    /// Tutto resta dentro la sagoma esatta dell'elemento (docs/04 §4). Coordinate del tile (est, nord, quota);
    /// le piante (alberi, uliveti, vigneti…) escono come istanze da disegnare con i modelli delle piante.
    /// </summary>
    public sealed class ObjectMesher
    {
        private readonly BuildingMesher _building = new BuildingMesher();

        // telaio dell'oggetto: origine nell'angolo (0,0) della sagoma ruotata, assi U (larghezza) e V (profondità)
        private Vec2 _o, _u, _v;
        private float _w, _d;
        private GroundSampler _ground;
        private MeshBuffer _mb;
        // piano d'appoggio dell'elemento: quota più alta della sagoma (sui pendii niente parti sotto terra)
        private float _z0;
        private readonly HashSet<long> _plinths = new HashSet<long>();
        private List<PlantInstance> _plants;
        private long _seed;

        /// <summary>Costruisce il modello. <paramref name="corner"/> è l'angolo della sagoma, <paramref name="angle"/> la direzione di U.</summary>
        public void Build(CatalogItem item, int level, string variant, Vec2 corner, float angle, int rotation, GroundSampler ground,
            MeshBuffer mb, List<PlantInstance> plants, long seed)
        {
            bool swap = (rotation & 1) == 1;
            _w = swap ? item.Depth : item.Width;
            _d = swap ? item.Width : item.Depth;
            _u = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
            _v = new Vec2(-_u.Y, _u.X);
            _o = corner;
            _ground = ground;
            _mb = mb;
            _plants = plants;
            _seed = seed;
            _plinths.Clear();
            Range(0, 0, _w, _d, out _, out _z0);
            var v = item.Variant(variant);
            uint wall = v != null && v.WallColor != 0 ? v.WallColor : 0;
            uint roof = v != null && v.RoofColor != 0 ? v.RoofColor : 0;
            float lv = Math.Max(0, level - 1);
            switch (item.Family)
            {
                case "house": House(item, wall, roof, lv, false); break;
                case "shop": House(item, wall, roof, lv, true); break;
                case "civic": Civic(item, wall, roof); break;
                case "church": Church(item, wall); break;
                case "industrial": Industrial(item, wall, roof); break;
                case "farmhouse": Building(0, 0, _w, _d, item.Height, RoofShape.Gabled, wall != 0 ? wall : 0x1D8C7A8, 0x1B25A3C, 2, BuildingUse.Farm); break;
                case "barn": Building(0, 0, _w, _d, item.Height, RoofShape.Gabled, 0x1A0703F, 0x18E8A84, 1, BuildingUse.Barn); break;
                case "silo": Silos(item); break;
                case "greenhouse": Greenhouse(); break;
                case "wind": WindTurbine(_w / 2, _d / 2, item.Height, Math.Min(_w, _d) / 2); break;
                case "windfarm":
                    for (int k = 0; k < 4; k++) WindTurbine(_w * (0.25f + 0.5f * (k & 1)), _d * (0.25f + 0.5f * (k >> 1)), item.Height, Math.Min(_w, _d) / 4);
                    break;
                case "solar": SolarRows(0, 0, _w, _d, float.NaN); break;
                case "solarbuilding":
                    Building(0, 0, _w, _d, item.Height - 1.2f, RoofShape.Flat, 0x1E4DACB, 0x1A9A399, 4, BuildingUse.Apartments);
                    SolarRows(1, 1, _w - 1, _d - 1, _z0 + item.Height - 1.2f);
                    break;
                case "hydro": Hydro(item); break;
                case "thermal": Thermal(item); break;
                case "nuclear": Nuclear(item, lv); break;
                case "substation": Substation(); break;
                case "pylon": Pylon(item.Height); break;
                case "field": Field(item.Param("crop", "wheat")); break;
                case "pasture": Ground(0, 0, _w, _d, 0x19CC277, 0.1f); Fence(); break;
                case "vineyard": Ground(0, 0, _w, _d, 0x1A29660, 0.08f); Rows(PlantKind.VineRow, 2.6f, 0); break;
                case "olivegrove": Ground(0, 0, _w, _d, 0x1B2AC80, 0.08f); Grid(PlantKind.Olive, 7f); break;
                case "orchard": Ground(0, 0, _w, _d, 0x196AC66, 0.08f); Grid(PlantKind.Fruit, 5.5f); break;
                case "beehives": Ground(0, 0, _w, _d, 0x1B5C47C, 0.06f); Beehives(); break;
                case "pitch": Pitch(item.Param("sport", "football")); break;
                case "pool": Pool(); break;
                case "hall": Building(0, 0, _w, _d, item.Height, RoofShape.Round, wall != 0 ? wall : 0x1D9DCDD, 0x1B8BEC2, 2, BuildingUse.Sports); break;
                case "track": Track(); break;
                case "stadium": Stadium(item, lv); break;
                case "amphitheatre": Amphitheatre(); break;
                case "funfair": Funfair(item); break;
                case "parking": Ground(0, 0, _w, _d, 0x19C9D9D, 0.12f, 9); break;
                case "parkinggarage": Building(0, 0, _w, _d, item.Height, RoofShape.Flat, 0x1BDBAB3, 0x1A9A399, 4, BuildingUse.Parking); break;
                case "busstop": BusStop(); break;
                case "station": Building(0, 0, _w, _d, item.Height, RoofShape.Flat, 0x1E9E2D3, 0x18E8A84, 1, BuildingUse.TrainStation); break;
                case "path": Ground(0, 0, _w, _d, 0x1D5CBB8, 0.12f, 10); break;
                case "paving": Ground(0, 0, _w, _d, 0x1D5CBB8, 0.12f, 10); break;
                case "helipad":
                    {
                        float g = Base(0, 0, _w, _d);
                        Flat(0, 0, _w, _d, g + 0.12f, 0x1707275, 2);
                        Box(_w * 0.3f, _d * 0.45f, _w * 0.7f, _d * 0.55f, g + 0.12f, g + 0.16f, 0x1F2F2F2, 2);
                        break;
                    }
                case "tree": Plant(PlantKindOf(item.Param("kind", "broadleaf")), _w / 2, _d / 2, item.Height); break;
                case "grove": Ground(0, 0, _w, _d, 0x18BB669, 0.06f); Scatter(PlantKind.Broadleaf, 5f); break;
                case "hedge":
                    {
                        Range(0, 0, _w, _d, out var lo, out var hi);
                        Box(0, 0, _w, _d, lo - 0.2f, (lo + hi) / 2 + item.Height, 0x15C8949, 2);
                        break;
                    }
                case "flowerbed":
                    {
                        Range(0, 0, _w, _d, out var lo, out var hi);
                        Box(0, 0, _w, _d, lo - 0.1f, hi + 0.35f, 0x1B9A27A, 2);
                        Flowers(item.Param("color", "mixed"));
                        break;
                    }
                case "lawn": Ground(0, 0, _w, _d, 0x19CC277, 0.08f); break;
                case "shrubs": Scatter(PlantKind.Shrub, 1.4f); break;
                case "pond": Ground(0, 0, _w, _d, 0x1C9B87A, 0.06f); Ground(0.6f, 0.6f, _w - 0.6f, _d - 0.6f, 0x14F8FC0, 0.1f, 11); break;
                case "bench": Bench(); break;
                case "lamp": Lamp(item.Height); break;
                case "fountain": Fountain(item.Height); break;
                case "statue": Statue(item.Height); break;
                case "planter":
                    {
                        float g = Base(0, 0, _w, _d);
                        Box(0, 0, _w, _d, g, g + 0.5f, 0x1B7A58C, 0);
                        Box(0.1f, 0.1f, _w - 0.1f, _d - 0.1f, g + 0.5f, g + 0.8f, 0x16E9C4C, 2);
                        break;
                    }
                case "bin": Cylinder(_w / 2, _d / 2, 0.2f, Ground(_w / 2, _d / 2) - 0.05f, Ground(_w / 2, _d / 2) + 0.9f, 8, 0x1556070); break;
                case "gazebo": Gazebo(item.Height); break;
                case "kiosk": Building(0, 0, _w, _d, item.Height, RoofShape.Pyramidal, 0x12F7D4F, 0x1E8C48A, 1, BuildingUse.Kiosk); break;
                case "playground": Playground(); break;
                case "terrace": Terrace(); break;
                case "flag":
                    {
                        float g = Ground(0.2f, _d / 2);
                        Cylinder(0.2f, _d / 2, 0.06f, g, g + item.Height, 6, 0x1D0D4D8);
                        Box(0.26f, _d / 2 - 0.03f, _w - 0.05f, _d / 2 + 0.03f, g + item.Height - 1f, g + item.Height - 0.1f, 0x12F8F3F, 0);
                        break;
                    }
                case "bikerack":
                    {
                        float g = Base(0, 0, _w, _d);
                        for (float x = 0.3f; x < _w; x += 0.6f) Box(x, 0.2f, x + 0.05f, _d - 0.2f, g, g + 0.8f, 0x1708090, 0);
                        break;
                    }
                default:
                    {
                        float g = Base(0, 0, _w, _d);
                        Box(0, 0, _w, _d, g, g + item.Height, wall != 0 ? wall : 0x1D0C8B8, 0);
                        break;
                    }
            }
        }

        public static PlantKind PlantKindOf(string kind)
        {
            switch (kind)
            {
                case "olive": return PlantKind.Olive;
                case "stonepine": return PlantKind.StonePine;
                case "cypress": return PlantKind.Cypress;
                case "palm": return PlantKind.Palm;
                case "conifer": return PlantKind.Conifer;
                case "fruit": return PlantKind.Fruit;
                case "shrub": return PlantKind.Shrub;
                default: return PlantKind.Broadleaf;
            }
        }

        // ------------------------------------------------------------------ famiglie

        private void House(CatalogItem item, uint wall, uint roof, float lv, bool shops)
        {
            int floors = int.TryParse(item.Param("floors", "1"), out var f) ? f : 1;
            var shape = ParseRoof(item.Param("roof", "hipped"));
            var use = shops ? BuildingUse.Retail : (floors >= 4 ? BuildingUse.Apartments : BuildingUse.House);
            // potenziamento: un po' più alto ai livelli successivi (sempre dentro la sagoma)
            float h = item.Height * (1 + 0.06f * lv);
            Building(0, 0, _w, _d, h, shape, wall, roof, Math.Max(1, floors + (int)Math.Round(lv * 0.5f)), use, shops);
            if (shops)
            {
                // tenda sopra la vetrina e insegna sul fronte (lato v = 0)
                uint awning = Catalog.ParseColor(item.Param("awning", "#c8473b"));
                Box(0.4f, 0.0f, _w - 0.4f, 0.7f, _z0 + 2.9f, _z0 + 3.15f, awning != 0 ? awning : 0x1C8473B, 2);
            }
        }

        private void Civic(CatalogItem item, uint wall, uint roof)
        {
            int floors = int.TryParse(item.Param("floors", "2"), out var f) ? f : 2;
            Building(0, 0, _w, _d, item.Height, RoofShape.Hipped, wall != 0 ? wall : 0x1E2D3B3, roof != 0 ? roof : 0x1B25A3C, floors, BuildingUse.Civic);
            // portico a colonne sul fronte
            int n = Math.Max(4, (int)(_w / 4));
            for (int k = 0; k < n; k++) Cylinder(_w * (k + 0.5f) / n, 0.8f, 0.35f, _z0, _z0 + Math.Min(item.Height, 7f), 8, 0x1F2EDE2);
            float pole = _z0 + item.Height * 1.2f + 1.5f; // pennone appena sopra il tetto, dentro l'altezza dell'elemento
            Cylinder(1f, 1f, 0.06f, _z0, pole, 6, 0x1D0D4D8);
            Box(1f, 0.97f, 2.6f, 1.03f, pole - 1.1f, pole - 0.1f, 0x12F8F3F, 0);
        }

        private void Church(CatalogItem item, uint wall)
        {
            uint stone = wall != 0 ? wall : 0x1D9C9A8;
            float tower = Math.Min(_w, _d) * 0.3f;
            // l'altezza dell'elemento è quella del campanile; la navata è più bassa
            Building(0, 0, _w - tower, _d, item.Height * 0.55f, RoofShape.Gabled, stone, 0x1B8664A, 2, BuildingUse.Church);
            Building(_w - tower, 0, _w, tower, item.Height, RoofShape.Pyramidal, stone, 0x1B8664A, 6, BuildingUse.Tower);
        }

        private void Industrial(CatalogItem item, uint wall, uint roof)
        {
            string kind = item.Param("kind", "shed");
            uint w = wall != 0 ? wall : 0x1C8CCCE, r = roof != 0 ? roof : 0x19A9A96;
            switch (kind)
            {
                case "plant":
                    Building(0, 0, _w * 0.6f, _d, item.Height * 0.5f, RoofShape.Flat, w, r, 3, BuildingUse.Industrial);
                    Cylinder(_w * 0.8f, _d * 0.3f, Math.Min(_w, _d) * 0.12f, _z0 - 0.5f, _z0 + item.Height, 16, 0x1B5B0A6);
                    Cylinder(_w * 0.8f, _d * 0.75f, Math.Min(_w, _d) * 0.12f, _z0 - 0.5f, _z0 + item.Height * 0.8f, 16, 0x1B5B0A6);
                    break;
                case "winery":
                case "oilmill":
                    Building(0, 0, _w, _d * 0.6f, item.Height, RoofShape.Gabled, 0x1E0CDA8, 0x1B25A3C, 2, BuildingUse.Industrial);
                    for (int k = 0; k < 4; k++) Cylinder(_w * (k + 0.5f) / 4, _d * 0.82f, Math.Min(2f, _d * 0.12f), _z0 - 0.5f, _z0 + item.Height * 0.9f, 12, 0x1C9CED4);
                    break;
                default:
                    // fabbrica: la ciminiera è la parte più alta (= altezza dell'elemento), il capannone più basso
                    bool factory = kind == "factory";
                    Building(0, 0, _w, _d, factory ? item.Height * 0.55f : item.Height, kind == "workshop" ? RoofShape.Gabled : RoofShape.Flat, w, r, 2, BuildingUse.Industrial);
                    if (factory) Cylinder(_w * 0.9f, _d * 0.85f, 1.2f, _z0, _z0 + item.Height, 10, 0x1A0705A);
                    break;
            }
        }

        private void Silos(CatalogItem item)
        {
            float r = Math.Min(_w, _d) * 0.22f;
            float g = Base(0, 0, _w, _d);
            Cylinder(_w * 0.28f, _d * 0.28f, r, g, g + item.Height - 1.5f, 16, 0x1D9DCDD);
            Cylinder(_w * 0.72f, _d * 0.72f, r, g, g + item.Height * 0.85f - 1.5f, 16, 0x1D9DCDD);
            Cone(_w * 0.28f, _d * 0.28f, r, g + item.Height - 1.5f, 1.5f, 0x1B8BEC2);
            Cone(_w * 0.72f, _d * 0.72f, r, g + item.Height * 0.85f - 1.5f, 1.5f, 0x1B8BEC2);
        }

        private void Greenhouse()
        {
            float g = Base(0, 0, _w, _d);
            Box(0, 0, _w, _d, g - 0.3f, g + 2.4f, 0x1DDE8E4, 5 * 2);
            Building(0, 0, _w, _d, 3.6f, RoofShape.Gabled, 0x1DDE8E4, 0x1E6EFEC, 1, BuildingUse.Greenhouse);
        }

        /// <summary>Pala eolica: il rotore sta dentro la sagoma (raggio ≤ metà lato), la punta all'altezza dell'elemento.</summary>
        private void WindTurbine(float cu, float cv, float height, float halfSide)
        {
            float g = Ground(cu, cv);
            float blade = Math.Max(2f, Math.Min(height * 0.3f, halfSide - 1f));
            float hubZ = height - blade;
            Cylinder(cu, cv, 1.6f, g - 0.5f, g + hubZ, 12, 0x1F2F4F5, 1.0f, 0.55f);
            // navicella e tre pale fisse (la rotazione arriverà con l'animazione)
            Box(cu - 1.2f, cv - 3f, cu + 1.2f, cv + 2f, g + hubZ - 1.2f, g + hubZ + 1.2f, 0x1F2F4F5, 0);
            var hub = new Vec2(cu, cv - 3.2f);
            for (int k = 0; k < 3; k++)
            {
                double a = Math.PI / 2 + k * 2 * Math.PI / 3;
                var dir = new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
                // pala nel piano verticale U-quota davanti alla navicella
                Blade(hub, dir, g + hubZ, blade);
            }
        }

        private void Blade(Vec2 hub, Vec2 dir, float hz, float len)
        {
            uint c = MeshBuffer.Rgb(0xF5F6F7);
            var p0 = World(hub.X, hub.Y);
            var right = _u;
            // dir.X lungo U, dir.Y in quota
            var tip = p0 + right * (dir.X * len);
            float tipZ = hz + dir.Y * len;
            var side = right * (-dir.Y * 0.9f);
            float sideZ = dir.X * 0.9f;
            var n = _v * -1f;
            int a = _mb.Add(p0.X - side.X * 0.5f, p0.Y - side.Y * 0.5f, hz - sideZ * 0.5f, n.X, n.Y, 0, c, 0, 0, 1, 1, 2, 0, 1, 0);
            int b = _mb.Add(p0.X + side.X * 0.5f, p0.Y + side.Y * 0.5f, hz + sideZ * 0.5f, n.X, n.Y, 0, c, 0, 0, 1, 1, 2, 0, 1, 0);
            int t = _mb.Add(tip.X, tip.Y, tipZ, n.X, n.Y, 0, c, 0, 0, 1, 1, 2, 0, 1, 0);
            _mb.Triangle(a, b, t);
            _mb.Triangle(a, t, b);
        }

        /// <param name="z">quota del tetto su cui stanno i pannelli; NaN = a terra, seguendo il terreno</param>
        private void SolarRows(float u0, float v0, float u1, float v1, float z)
        {
            bool onGround = float.IsNaN(z);
            for (float v = v0 + 0.6f; v + 2.2f < v1; v += 3.4f)
            {
                float g = onGround ? Mid(u0, v, u1, v + 2.2f) : z;
                // pannello inclinato verso sud (v crescente = nord)
                SlopedPanel(u0 + 0.4f, v, u1 - 0.4f, v + 2.2f, g + 0.5f, g + 1.6f);
            }
        }

        private void SlopedPanel(float u0, float v0, float u1, float v1, float zLow, float zHigh)
        {
            uint c = MeshBuffer.Rgb(0x2B3A5A);
            var p00 = World(u0, v0);
            var p10 = World(u1, v0);
            var p11 = World(u1, v1);
            var p01 = World(u0, v1);
            float dz = zHigh - zLow, dv = v1 - v0;
            var nn = _v * (-dz) + new Vec2(0, 0);
            float nl = (float)Math.Sqrt(dz * dz + dv * dv);
            // tipo 12 = pannello solare (celle disegnate dallo shader)
            int a = _mb.Add(p00.X, p00.Y, zLow, nn.X / nl, nn.Y / nl, dv / nl, c, u0, v0, 0, 0, 12, 0.5f, 1, 0);
            int b = _mb.Add(p10.X, p10.Y, zLow, nn.X / nl, nn.Y / nl, dv / nl, c, u1, v0, 0, 0, 12, 0.5f, 1, 0);
            int d = _mb.Add(p11.X, p11.Y, zHigh, nn.X / nl, nn.Y / nl, dv / nl, c, u1, v1, 0, 0, 12, 0.5f, 1, 0);
            int e = _mb.Add(p01.X, p01.Y, zHigh, nn.X / nl, nn.Y / nl, dv / nl, c, u0, v1, 0, 0, 12, 0.5f, 1, 0);
            _mb.Quad(a, b, d, e);
            _mb.Quad(a, e, d, b); // retro
        }

        private void Hydro(CatalogItem item)
        {
            Building(0, 0, _w, _d * 0.5f, item.Height * 0.5f, RoofShape.Flat, 0x1C9C6BF, 0x1A9A399, 2, BuildingUse.Industrial);
            Box(0, _d * 0.5f, _w, _d, Base(0, _d * 0.5f, _w, _d) - 1, Base(0, _d * 0.5f, _w, _d) + item.Height, 0x1B9B4AA, 6 * 2);
            Ground(0, _d * 0.55f, _w, _d, 0x14F8FC0, 0.2f, 11);
        }

        private void Thermal(CatalogItem item)
        {
            int chimneys = int.TryParse(item.Param("chimneys", "1"), out var c) ? c : 1;
            Building(0, 0, _w * 0.65f, _d * 0.7f, item.Height * 0.4f, RoofShape.Flat, 0x1D1CBC0, 0x1A9A399, 3, BuildingUse.Industrial);
            for (int k = 0; k < chimneys; k++)
                Cylinder(_w * (0.78f + 0.12f * k), _d * 0.3f, 1.6f, _z0 - 0.5f, _z0 + item.Height, 12, 0x1C8C2BA, 1.0f, 0.75f);
            if (item.Param("steam", "false") == "true" || chimneys == 0)
                Cylinder(_w * 0.8f, _d * 0.75f, Math.Min(_w, _d) * 0.15f, _z0 - 0.5f, _z0 + item.Height * 0.6f, 20, 0x1D8DADC, 1.0f, 0.7f);
            Box(_w * 0.05f, _d * 0.75f, _w * 0.6f, _d * 0.95f, Base(0, _d * 0.75f, _w * 0.6f, _d * 0.95f), Base(0, _d * 0.75f, _w * 0.6f, _d * 0.95f) + 6, 0x1B8BEC2, 6 * 2);
        }

        /// <summary>
        /// Centrale nucleare come quelle vere: due torri di raffreddamento a iperboloide (vuote, con i pilastri alla base),
        /// edificio di contenimento del reattore con cupola, sala turbine, edificio ausiliario con il camino a bande
        /// bianche e rosse, stazione elettrica tra le torri, piazzale, strade e recinzione.
        /// </summary>
        private void Nuclear(CatalogItem item, float lv)
        {
            float H = item.Height;
            Ground(0, 0, _w, _d, 0x1BDB9B0, 0.08f, 13);
            // strade interne
            Ground(_w * 0.02f, _d * 0.31f, _w * 0.98f, _d * 0.37f, 0x1707274, 0.12f, 13);
            // torri di raffreddamento
            float r0 = Math.Min(_w * 0.19f, _d * 0.25f);
            float ht = Math.Min(H * 0.78f, r0 * 3.9f);
            CoolingTower(_w * 0.25f, _d * 0.65f, r0, ht);
            CoolingTower(_w * 0.75f, _d * 0.65f, r0, ht);
            // stazione elettrica tra le torri: trasformatori e portali
            float su0 = _w * 0.45f, su1 = _w * 0.55f;
            float sg = Base(su0, _d * 0.4f, su1, _d * 0.95f);
            for (int k = 0; k < 4; k++)
            {
                float v = _d * (0.45f + k * 0.12f);
                PaintedBox(su0 + 1.5f, v, su1 - 1.5f, v + 4f, sg, sg + 3.2f, 0x18E9196);
                Cylinder(su0 + 1f, v + 2f, 0.25f, sg, sg + 9f, 6, 0x1A9AEB4);
                Cylinder(su1 - 1f, v + 2f, 0.25f, sg, sg + 9f, 6, 0x1A9AEB4);
                PaintedBox(su0 + 0.8f, v + 1.8f, su1 - 0.8f, v + 2.2f, sg + 8.6f, sg + 9.2f, 0x1A9AEB4);
            }
            // edificio del reattore: cilindro di cemento con la cupola
            float rr = Math.Min(_w, _d) * 0.11f;
            float rg = Base(_w * 0.19f - rr, _d * 0.18f - rr, _w * 0.19f + rr, _d * 0.18f + rr);
            float rh = Math.Min(H * 0.24f, 36f);
            Cylinder(_w * 0.19f, _d * 0.18f, rr, rg - 0.5f, rg + rh, 32, 0x1DEDAD2);
            Cylinder(_w * 0.19f, _d * 0.18f, rr * 1.01f, rg + rh - 3f, rg + rh - 2.2f, 32, 0x1A5A29B);
            Dome(_w * 0.19f, _d * 0.18f, rr, rg + rh, 0x1E6E2DA, 13);
            // sala turbine (rivestimento metallico azzurro) e edificio ausiliario
            Building(_w * 0.3f, _d * 0.05f, _w * 0.72f, _d * 0.28f, Math.Min(26f, H * 0.18f), RoofShape.Flat, 0x1849BB2, 0x19A9A96, 3, BuildingUse.Industrial);
            Building(_w * 0.75f, _d * 0.05f, _w * 0.95f, _d * 0.26f, Math.Min(16f, H * 0.11f), RoofShape.Flat, 0x1CFCBC2, 0x19A9A96, 3, BuildingUse.Industrial);
            // camino di ventilazione: fusto chiaro, ultime bande bianche e rosse
            float cu = _w * 0.87f, cv = _d * 0.15f;
            float cg = Ground(cu, cv);
            float top = _z0 + H;
            float bands = Math.Min(20f, (top - cg) * 0.15f);
            Cylinder(cu, cv, 2.8f, cg - 0.5f, top - bands, 16, 0x1D6D3CC, 1f, 0.78f);
            for (int k = 0; k < 4; k++)
            {
                float z0 = top - bands + bands * k / 4, z1 = top - bands + bands * (k + 1) / 4;
                float s0 = 0.78f - 0.08f * k / 4, s1 = 0.78f - 0.08f * (k + 1) / 4;
                Cylinder(cu, cv, 2.8f, z0, z1, 16, k % 2 == 0 ? 0x1C8382Eu : 0x1F2F0EBu, s0, s1);
            }
            Fence();
        }

        /// <summary>Torre di raffreddamento a iperboloide (vuota): base larga con i pilastri, strozzatura, bocca larga in cima.</summary>
        private void CoolingTower(float cu, float cv, float r0, float height)
        {
            float g = Base(cu - r0, cv - r0, cu + r0, cv + r0);
            var c = World(cu, cv);
            const int sides = 32, rings = 12;
            float zt = height * 0.78f, rt = r0 * 0.6f;
            float k = zt / (float)Math.Sqrt((r0 / rt) * (r0 / rt) - 1);
            float R(float z) => rt * (float)Math.Sqrt(1 + ((z - zt) / k) * ((z - zt) / k));
            float legs = Math.Min(6f, height * 0.05f);
            uint shell = MeshBuffer.Rgb(0xD2CFC8), inner = MeshBuffer.Rgb(0x8D8A84), stain = MeshBuffer.Rgb(0xB4B0A8), dark = MeshBuffer.Rgb(0x3B3A37);
            for (int j = 0; j < rings; j++)
            {
                float z0 = legs + (height - legs) * j / rings, z1 = legs + (height - legs) * (j + 1) / rings;
                float ra = R(z0), rb = R(z1);
                float slope = (rb - ra) / (z1 - z0);
                uint col = j == rings - 1 ? stain : shell;
                for (int i = 0; i < sides; i++)
                {
                    double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                    var d0 = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
                    var d1 = new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1));
                    float nl = (float)Math.Sqrt(1 + slope * slope);
                    float nx = (float)Math.Cos(am) / nl, ny = (float)Math.Sin(am) / nl, nz = -slope / nl;
                    var p0 = c + d0 * ra;
                    var p1 = c + d1 * ra;
                    var p2 = c + d1 * rb;
                    var p3 = c + d0 * rb;
                    float len = (p1 - p0).Length;
                    int i0 = _mb.Add(p0.X, p0.Y, g + z0, nx, ny, nz, col, 0, 0, len, z1 - z0, 13, 0.5f, 0, 0);
                    int i1 = _mb.Add(p1.X, p1.Y, g + z0, nx, ny, nz, col, len, 0, len, z1 - z0, 13, 0.5f, 0, 0);
                    int i2 = _mb.Add(p2.X, p2.Y, g + z1, nx, ny, nz, col, len, z1 - z0, len, z1 - z0, 13, 0.5f, 0, 0);
                    int i3 = _mb.Add(p3.X, p3.Y, g + z1, nx, ny, nz, col, 0, z1 - z0, len, z1 - z0, 13, 0.5f, 0, 0);
                    _mb.Quad(i0, i1, i2, i3);
                    // interno (si vede dall'alto): stesso guscio, girato verso il centro
                    var q0 = c + d0 * (ra - 0.4f);
                    var q1 = c + d1 * (ra - 0.4f);
                    var q2 = c + d1 * (rb - 0.4f);
                    var q3 = c + d0 * (rb - 0.4f);
                    int j0 = _mb.Add(q0.X, q0.Y, g + z0, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    int j1 = _mb.Add(q1.X, q1.Y, g + z0, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    int j2 = _mb.Add(q2.X, q2.Y, g + z1, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    int j3 = _mb.Add(q3.X, q3.Y, g + z1, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    _mb.Quad(j0, j3, j2, j1);
                }
            }
            // bordo in cima e pilastri alla base (tra i pilastri il buio dell'ingresso dell'aria)
            float rtop = R(height);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                var d0 = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
                var d1 = new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1));
                var o0 = c + d0 * rtop;
                var o1 = c + d1 * rtop;
                var n0 = c + d0 * (rtop - 0.4f);
                var n1 = c + d1 * (rtop - 0.4f);
                int t0 = _mb.Add(o0.X, o0.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int t1 = _mb.Add(o1.X, o1.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int t2 = _mb.Add(n1.X, n1.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int t3 = _mb.Add(n0.X, n0.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                _mb.Quad(t0, t1, t2, t3);
                var b0 = c + d0 * r0;
                var b1 = c + d1 * r0;
                var l0 = c + d0 * R(legs);
                var l1 = c + d1 * R(legs);
                uint col = i % 2 == 0 ? shell : dark;
                float nx = (float)Math.Cos(am), ny = (float)Math.Sin(am);
                int k0 = _mb.Add(b0.X, b0.Y, g - 0.5f, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int k1 = _mb.Add(b1.X, b1.Y, g - 0.5f, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int k2 = _mb.Add(l1.X, l1.Y, g + legs, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int k3 = _mb.Add(l0.X, l0.Y, g + legs, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                _mb.Quad(k0, k1, k2, k3);
            }
            // fondo scuro della vasca (si vede dall'alto, dentro la torre)
            Flat(cu - R(legs) * 0.7f, cv - R(legs) * 0.7f, cu + R(legs) * 0.7f, cv + R(legs) * 0.7f, g + 0.3f, 0x14A5E66, 11);
        }

        private void Substation()
        {
            Ground(0, 0, _w, _d, 0x1A9A399, 0.1f, 10);
            for (int k = 0; k < 3; k++) Box(2 + k * (_w - 4) / 3, 3, 2 + k * (_w - 4) / 3 + 3, 7, Base(0, 0, _w, _d), Base(0, 0, _w, _d) + 4, 0x1B8BEC2, 0);
            Fence();
        }

        private void Pylon(float h)
        {
            float g = Base(0, 0, _w, _d);
            for (int k = 0; k < 4; k++)
            {
                float cu = (k & 1) == 0 ? 0.6f : _w - 0.6f, cv = (k & 2) == 0 ? 0.6f : _d - 0.6f;
                Box(cu - 0.2f, cv - 0.2f, cu + 0.2f, cv + 0.2f, g, g + h * 0.3f, 0x18A9098, 0);
            }
            Box(_w / 2 - 0.6f, _d / 2 - 0.6f, _w / 2 + 0.6f, _d / 2 + 0.6f, g + h * 0.3f, g + h, 0x18A9098, 0);
            Box(0, _d / 2 - 0.15f, _w, _d / 2 + 0.15f, g + h * 0.8f, g + h * 0.84f, 0x18A9098, 0);
        }

        private void Field(string crop)
        {
            uint soil = 0x1B59A6E, plant;
            float rowH = 0.6f;
            switch (crop)
            {
                case "corn": plant = 0x18AA84A; rowH = 1.8f; break;
                case "sunflower": plant = 0x1E8C030; rowH = 1.6f; break;
                case "vegetables": plant = 0x16E9C4C; rowH = 0.4f; break;
                case "lavender": plant = 0x19A86C8; rowH = 0.6f; break;
                case "saffron": plant = 0x1A98ACD; rowH = 0.2f; break;
                default: plant = 0x1D8C077; rowH = 0.8f; break;
            }
            Ground(0, 0, _w, _d, soil, 0.06f);
            for (float v = 0.6f; v + 0.5f < _d; v += 1.4f)
            {
                float g = Mid(0.3f, v, _w - 0.3f, v + 0.6f);
                Box(0.3f, v, _w - 0.3f, v + 0.6f, g - 0.3f, g + rowH, plant, 2);
            }
        }

        private void Fence()
        {
            // a tratti di ~3 m che seguono il terreno
            FenceSide(0, 0, _w, 0.1f, true);
            FenceSide(0, _d - 0.1f, _w, _d, true);
            FenceSide(0, 0, 0.1f, _d, false);
            FenceSide(_w - 0.1f, 0, _w, _d, false);
        }

        private void FenceSide(float u0, float v0, float u1, float v1, bool alongU)
        {
            float len = alongU ? u1 - u0 : v1 - v0;
            int n = Math.Max(1, (int)Math.Ceiling(len / 3f));
            for (int i = 0; i < n; i++)
            {
                float a = i * len / n, b = (i + 1) * len / n;
                float su0 = alongU ? u0 + a : u0, su1 = alongU ? u0 + b : u1, sv0 = alongU ? v0 : v0 + a, sv1 = alongU ? v1 : v0 + b;
                Range(su0, sv0, su1, sv1, out var lo, out var hi);
                Box(su0, sv0, su1, sv1, lo - 0.2f, (lo + hi) / 2 + 1.1f, 0x18B7458, 0);
            }
        }

        private void Beehives()
        {
            uint[] colors = { 0x1E8C030, 0x14F79B5, 0x1D9534A, 0x1F2EDE2 };
            int k = 0;
            for (float u = 0.6f; u + 0.6f < _w; u += 1.4f)
                for (float v = 0.6f; v + 0.6f < _d; v += 2.2f)
                {
                    float g = Mid(u, v, u + 0.6f, v + 0.6f);
                    Box(u, v, u + 0.6f, v + 0.6f, g - 0.1f, g + 0.7f, colors[k++ % colors.Length], 0);
                }
        }

        private void Pitch(string sport)
        {
            uint surface = sport == "tennis" ? 0x1C0663Fu : (sport == "basket" ? 0x1A65E3Cu : (sport == "skate" ? 0x1A9A9A9u : 0x15FA24Cu));
            float g = Base(0, 0, _w, _d);
            // campo: stesso tipo "campo con linee" dello stadio (uv0 in metri del rettangolo)
            Flat(0, 0, _w, _d, g + 0.12f, surface, 8);
            if (sport == "football" || sport == "football5")
            {
                Box(0.5f, _d / 2 - 2.4f, 0.7f, _d / 2 + 2.4f, g, g + 2.4f, 0x1F2F2F2, 0);
                Box(_w - 0.7f, _d / 2 - 2.4f, _w - 0.5f, _d / 2 + 2.4f, g, g + 2.4f, 0x1F2F2F2, 0);
            }
        }

        private void Pool()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.1f, 0x1D5CBB8, 10);
            Flat(3, 3, _w - 3, _d - 3, g + 0.14f, 0x157B5D7, 11);
        }

        private void Track()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.1f, 0x1B7694D, 2);
            Flat(_w * 0.12f, _d * 0.2f, _w * 0.88f, _d * 0.8f, g + 0.14f, 0x15FA24C, 8);
        }

        /// <summary>
        /// Stadio vero e proprio: campo regolamentare con le linee, quattro tribune a gradinata con i sedili colorati,
        /// copertura a sbalzo sulla tribuna principale (sulle altre ai livelli successivi), torri faro che la sera si
        /// accendono, cartelloni a bordo campo, panchine e tabellone.
        /// </summary>
        private void Stadium(CatalogItem item, float lv)
        {
            float H = item.Height;
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.06f, 0x1A9A69E, 10);
            // campo (105×68 m se c'è posto) con 3 m di bordo; il tipo 8 disegna strisce e linee
            float fl = Math.Min(111f, _w - 46f), fw = Math.Min(74f, _d - 44f);
            float fu0 = (_w - fl) / 2, fv0 = (_d - fw) / 2, fu1 = fu0 + fl, fv1 = fv0 + fw;
            Flat(fu0, fv0, fu1, fv1, g + 0.15f, 0x15FA24C, 8);
            PaintedBox(fu0 + 2.9f, _d / 2 - 3.66f, fu0 + 3.1f, _d / 2 + 3.66f, g + 0.15f, g + 2.44f, 0x1F2F2F2);
            PaintedBox(fu1 - 3.1f, _d / 2 - 3.66f, fu1 - 2.9f, _d / 2 + 3.66f, g + 0.15f, g + 2.44f, 0x1F2F2F2);
            // colori della squadra: sedili delle tribune lunghe e delle curve
            uint[] palette = { 0x2F6DB5, 0xB8433A, 0x3E8E4A, 0xE0A92E, 0x6B3FA0 };
            int pick = (int)(Frac(11) * palette.Length) % palette.Length;
            uint main = palette[pick], ends = palette[(pick + 2) % palette.Length];
            float zMain = Math.Min(H * 0.42f, 14f), zEnds = zMain * 0.7f;
            // tribune: principale a sud (v basse), distinti a nord, curve a ovest e a est
            float gap = 3f;
            SeatingStand(fu0 - 2, 2, fu1 + 2, fv0 - gap, 0, g, g + 1.3f, g + zMain, main);
            SeatingStand(fu0 - 2, fv1 + gap, fu1 + 2, _d - 2, 1, g, g + 1.3f, g + zMain * 0.85f, main);
            SeatingStand(2, fv0, fu0 - gap, fv1, 2, g, g + 1.3f, g + zEnds, ends);
            SeatingStand(fu1 + gap, fv0, _w - 2, fv1, 3, g, g + 1.3f, g + zEnds, ends);
            // coperture a sbalzo: principale sempre, distinti dal livello 2, curve al livello 3
            StandRoof(fu0 - 2, 2, fu1 + 2, fv0 - gap, 0, g, g + zMain + 4.5f);
            if (lv >= 1) StandRoof(fu0 - 2, fv1 + gap, fu1 + 2, _d - 2, 1, g, g + zMain * 0.85f + 4.5f);
            if (lv >= 2)
            {
                StandRoof(2, fv0, fu0 - gap, fv1, 2, g, g + zEnds + 4.5f);
                StandRoof(fu1 + gap, fv0, _w - 2, fv1, 3, g, g + zEnds + 4.5f);
            }
            // cartelloni pubblicitari attorno al campo e panchine davanti alla tribuna principale
            uint[] ads = { 0x1D9534A, 0x1F2F2F2, 0x12F6DB5, 0x1F2C230, 0x13E8E4A };
            for (float u = fu0 + 6; u + 7 < fu1 - 6; u += 8)
            {
                PaintedBox(u, fv0 - 1.4f, u + 7.6f, fv0 - 1.2f, g + 0.15f, g + 1.1f, ads[(int)(u / 8) % ads.Length]);
                PaintedBox(u, fv1 + 1.2f, u + 7.6f, fv1 + 1.4f, g + 0.15f, g + 1.1f, ads[(int)(u / 8 + 2) % ads.Length]);
            }
            for (int k = -1; k <= 1; k += 2)
            {
                float cu = _w / 2 + k * 9f;
                PaintedBox(cu - 4f, fv0 - 2.8f, cu + 4f, fv0 - 1.6f, g + 0.15f, g + 1.9f, 0x12A3A55);
            }
            // tabellone sopra la curva est
            float tu = _w - 2f;
            PaintedBox(tu - 1f, _d / 2 - 7f, tu, _d / 2 + 7f, g + zEnds, g + zEnds + 5.5f, 0x1262A30);
            Poly(new[] { tu - 1.02f, _d / 2 - 6.4f, g + zEnds + 0.6f, tu - 1.02f, _d / 2 + 6.4f, g + zEnds + 0.6f,
                tu - 1.02f, _d / 2 + 6.4f, g + zEnds + 5f, tu - 1.02f, _d / 2 - 6.4f, g + zEnds + 5f }, -1, 0, 0, 0x1203848, BuildingDecorator.KindLight, 0.5f);
            // torri faro agli angoli, fari rivolti verso il centro del campo
            for (int k = 0; k < 4; k++)
            {
                float mu = (k & 1) == 0 ? 4f : _w - 4f, mv = (k & 2) == 0 ? 4f : _d - 4f;
                float mg = Ground(mu, mv);
                float topZ = _z0 + H;
                Cylinder(mu, mv, 0.9f, mg - 0.3f, topZ - 4.5f, 10, 0x1A9AEB4, 1f, 0.55f);
                float du = _w / 2 - mu, dv = _d / 2 - mv;
                float dl = (float)Math.Sqrt(du * du + dv * dv);
                du /= dl;
                dv /= dl;
                float pu = mu + du * 1.2f, pv = mv + dv * 1.2f;
                float su = -dv * 3.2f, sv = du * 3.2f;
                PaintedBox(Math.Min(pu - su, pu + su) - 0.3f, Math.Min(pv - sv, pv + sv) - 0.3f, Math.Max(pu - su, pu + su) + 0.3f, Math.Max(pv - sv, pv + sv) + 0.3f,
                    topZ - 4.6f, topZ - 4.4f, 0x14A4F55);
                Poly(new[] { pu - su, pv - sv, topZ - 4.4f, pu + su, pv + sv, topZ - 4.4f, pu + su, pv + sv, topZ, pu - su, pv - sv, topZ },
                    du, dv, 0, 0x1FFF2D2, BuildingDecorator.KindLight, 1f);
            }
        }

        /// <summary>
        /// Tribuna a gradinata sul rettangolo; <paramref name="front"/> = lato verso il campo (0: v alte, 1: v basse,
        /// 2: u alte, 3: u basse). Sedili dello shader (tipo 18) sul piano inclinato, muri in cemento, scale ogni 18 m.
        /// </summary>
        private void SeatingStand(float u0, float v0, float u1, float v1, int front, float g, float zFront, float zBack, uint seat)
        {
            // angoli in ordine: fronte sinistra, fronte destra, retro destra, retro sinistra
            float[] fu, fv, bu, bv;
            switch (front)
            {
                case 0: fu = new[] { u0, u1 }; fv = new[] { v1, v1 }; bu = new[] { u0, u1 }; bv = new[] { v0, v0 }; break;
                case 1: fu = new[] { u0, u1 }; fv = new[] { v0, v0 }; bu = new[] { u0, u1 }; bv = new[] { v1, v1 }; break;
                case 2: fu = new[] { u1, u1 }; fv = new[] { v0, v1 }; bu = new[] { u0, u0 }; bv = new[] { v0, v1 }; break;
                default: fu = new[] { u0, u0 }; fv = new[] { v0, v1 }; bu = new[] { u1, u1 }; bv = new[] { v0, v1 }; break;
            }
            float zb = g - 0.5f;
            uint concrete = 0x1C6C2BA;
            // gradinata
            Poly(new[] { fu[0], fv[0], zFront, fu[1], fv[1], zFront, bu[1], bv[1], zBack, bu[0], bv[0], zBack }, 0, 0, 1, seat,
                BuildingDecorator.KindSeats, BuildingMesher.SeatRowHeight);
            // retro, parapetto sul fronte, fianchi
            float ou = (bu[0] - fu[0]), ov = (bv[0] - fv[0]);
            Poly(new[] { bu[0], bv[0], zb, bu[1], bv[1], zb, bu[1], bv[1], zBack, bu[0], bv[0], zBack }, ou, ov, 0, concrete, 0, 1, 12);
            Poly(new[] { fu[0], fv[0], zb, fu[1], fv[1], zb, fu[1], fv[1], zFront, fu[0], fv[0], zFront }, -ou, -ov, 0, concrete, 0, 1, 12);
            float lu = fu[0] - fu[1], lv = fv[0] - fv[1];
            Poly(new[] { fu[0], fv[0], zb, bu[0], bv[0], zb, bu[0], bv[0], zBack, fu[0], fv[0], zFront }, lu, lv, 0, concrete, 0, 1, 12);
            Poly(new[] { fu[1], fv[1], zb, bu[1], bv[1], zb, bu[1], bv[1], zBack, fu[1], fv[1], zFront }, -lu, -lv, 0, concrete, 0, 1, 12);
            // scale: strisce chiare sulla gradinata
            float len = (float)Math.Sqrt(lu * lu + lv * lv);
            int aisles = Math.Max(1, (int)(len / 18f));
            for (int k = 1; k <= aisles; k++)
            {
                float t = k / (float)(aisles + 1), w = 0.7f / len;
                float a = t - w, b = t + w;
                float Fu(float x) => fu[0] + (fu[1] - fu[0]) * x;
                float Fv(float x) => fv[0] + (fv[1] - fv[0]) * x;
                float Bu(float x) => bu[0] + (bu[1] - bu[0]) * x;
                float Bv(float x) => bv[0] + (bv[1] - bv[0]) * x;
                Poly(new[] { Fu(a), Fv(a), zFront + 0.05f, Fu(b), Fv(b), zFront + 0.05f, Bu(b), Bv(b), zBack + 0.05f, Bu(a), Bv(a), zBack + 0.05f },
                    0, 0, 1, 0x1D8D4CC, BuildingDecorator.KindProp, 0);
            }
        }

        /// <summary>Copertura a sbalzo sopra una tribuna: lastra leggermente inclinata verso il campo, pilastri sul retro.</summary>
        private void StandRoof(float u0, float v0, float u1, float v1, int front, float g, float z)
        {
            // la copertura parte dal retro e copre il 75% della profondità
            float ru0 = u0, rv0 = v0, ru1 = u1, rv1 = v1;
            switch (front)
            {
                case 0: rv1 = v0 + (v1 - v0) * 0.8f; break;
                case 1: rv0 = v1 - (v1 - v0) * 0.8f; break;
                case 2: ru1 = u0 + (u1 - u0) * 0.8f; break;
                default: ru0 = u1 - (u1 - u0) * 0.8f; break;
            }
            PaintedBox(ru0, rv0, ru1, rv1, z, z + 0.7f, 0x1E9ECEE);
            // bordo frontale colorato
            switch (front)
            {
                case 0: PaintedBox(ru0, rv1 - 0.4f, ru1, rv1, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
                case 1: PaintedBox(ru0, rv0, ru1, rv0 + 0.4f, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
                case 2: PaintedBox(ru1 - 0.4f, rv0, ru1, rv1, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
                default: PaintedBox(ru0, rv0, ru0 + 0.4f, rv1, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
            }
            bool alongU = front < 2;
            float len = alongU ? ru1 - ru0 : rv1 - rv0;
            int cols = Math.Max(2, (int)(len / 16f) + 1);
            for (int k = 0; k < cols; k++)
            {
                float t = k / (float)(cols - 1);
                float cu = alongU ? ru0 + 0.8f + (ru1 - ru0 - 1.6f) * t : (front == 2 ? u0 + 0.8f : u1 - 0.8f);
                float cv = alongU ? (front == 0 ? v0 + 0.8f : v1 - 0.8f) : rv0 + 0.8f + (rv1 - rv0 - 1.6f) * t;
                Cylinder(cu, cv, 0.45f, g - 0.3f, z, 8, 0x1D5D9DD);
            }
        }

        private void Amphitheatre()
        {
            float g = Base(0, 0, _w, _d);
            Flat(_w * 0.3f, 0, _w * 0.7f, _d * 0.25f, g + 0.4f, 0x1D5CBB8, 10);
            for (int s = 0; s < 8; s++)
                Box(_w * 0.05f, _d * (0.3f + s * 0.08f), _w * 0.95f, _d * (0.38f + s * 0.08f), g - 0.3f, g + 0.5f + s * 0.6f, 0x1D9D1C2, 6 * 2);
        }

        private void Funfair(CatalogItem item)
        {
            Ground(0, 0, _w, _d, 0x1C9C1B5, 0.1f, 10);
            // ruota panoramica (anello di colonnine) e qualche giostra colorata
            float r = Math.Min(Math.Min(_w, _d) * 0.28f, (item.Height - 3f) / 2f), cu = _w * 0.3f, cv = _d * 0.5f, g = Ground(cu, cv);
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8;
                float z = g + r + 2 + (float)Math.Sin(a) * r;
                float du = (float)Math.Cos(a) * r;
                Box(cu + du - 0.6f, cv - 0.6f, cu + du + 0.6f, cv + 0.6f, z - 0.6f, z + 0.6f, k % 2 == 0 ? 0x1D9534Au : 0x14F79B5u, 0);
            }
            Box(cu - 0.3f, cv - 2f, cu + 0.3f, cv + 2f, g, g + r + 2, 0x1F2EDE2, 0);
            Cylinder(_w * 0.72f, _d * 0.3f, 6, g, g + 3, 16, 0x1E8C030);
            Cone(_w * 0.72f, _d * 0.3f, 7, g + 3, 4, 0x1D9534A);
            Cylinder(_w * 0.72f, _d * 0.72f, 5, g, g + 2.5f, 16, 0x14F79B5);
        }

        private void BusStop()
        {
            float g = Base(0, 0, _w, _d);
            Box(0.2f, _d - 0.3f, _w - 0.2f, _d - 0.2f, g, g + 2.4f, 0x19FB6C4, 5 * 2);
            Box(0, 0, _w, _d, g + 2.4f, g + 2.6f, 0x1556070, 0);
            Box(0.3f, 0.2f, 0.4f, 0.3f, g, g + 2.4f, 0x1556070, 0);
            Box(_w - 0.4f, 0.2f, _w - 0.3f, 0.3f, g, g + 2.4f, 0x1556070, 0);
        }

        private void Bench()
        {
            float g = Base(0, 0, _w, _d);
            Box(0.05f, 0.05f, _w - 0.05f, _d - 0.05f, g + 0.4f, g + 0.48f, 0x19C7350, 0);
            Box(0.05f, _d - 0.12f, _w - 0.05f, _d - 0.05f, g + 0.48f, g + 0.85f, 0x19C7350, 0);
            Box(0.1f, 0.1f, 0.18f, _d - 0.1f, g, g + 0.4f, 0x1404850, 0);
            Box(_w - 0.18f, 0.1f, _w - 0.1f, _d - 0.1f, g, g + 0.4f, 0x1404850, 0);
        }

        private void Lamp(float h)
        {
            float g = Ground(_w / 2, _d / 2);
            Cylinder(_w / 2, _d / 2, 0.08f, g, g + h, 6, 0x1404850);
            Box(_w / 2 - 0.2f, _d / 2 - 0.2f, _w / 2 + 0.2f, _d / 2 + 0.2f, g + h - 0.4f, g + h, 0x1FFF2C8, 7 * 2);
        }

        private void Fountain(float h)
        {
            float g = Base(0, 0, _w, _d);
            float r = Math.Min(_w, _d) / 2;
            Cylinder(_w / 2, _d / 2, r, g - 0.2f, g + 0.6f, 20, 0x1D9D1C2);
            Cylinder(_w / 2, _d / 2, r * 0.85f, g + 0.6f, g + 0.62f, 20, 0x157B5D7);
            Cylinder(_w / 2, _d / 2, r * 0.15f, g, g + h, 10, 0x1D9D1C2);
            Cylinder(_w / 2, _d / 2, r * 0.4f, g + h * 0.6f, g + h * 0.68f, 14, 0x1D9D1C2);
        }

        private void Statue(float h)
        {
            float g = Base(0, 0, _w, _d);
            Box(_w * 0.15f, _d * 0.15f, _w * 0.85f, _d * 0.85f, g - 0.2f, g + h * 0.4f, 0x1D9D1C2, 0);
            Cylinder(_w / 2, _d / 2, Math.Min(_w, _d) * 0.16f, g + h * 0.4f, g + h * 0.85f, 10, 0x18A7A5A);
            Dome(_w / 2, _d / 2, Math.Min(_w, _d) * 0.12f, g + h * 0.85f, 0x18A7A5A);
        }

        private void Gazebo(float h)
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.15f, 0x1D5CBB8, 10);
            for (int k = 0; k < 4; k++)
                Cylinder((k & 1) == 0 ? 0.3f : _w - 0.3f, (k & 2) == 0 ? 0.3f : _d - 0.3f, 0.1f, g, g + h - 1, 6, 0x1F2EDE2);
            Building(0, 0, _w, _d, 1.2f, RoofShape.Pyramidal, 0x1F2EDE2, 0x13F6B4A, 1, BuildingUse.Roof, false, g + h - 1.2f);
        }

        private void Playground()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.1f, 0x1C9A27A, 2);
            Box(1, 1, 3, 3, g, g + 2.5f, 0x1D9534A, 0);
            Box(3, 1.5f, 6, 2.5f, g, g + 1.2f, 0x1E8C030, 0);
            Cylinder(_w - 3, _d - 3, 1.5f, g, g + 0.4f, 12, 0x14F79B5);
            Box(1, _d - 2, _w * 0.5f, _d - 1.6f, g, g + 2.2f, 0x1F2EDE2, 0);
        }

        private void Terrace()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.12f, 0x1C9BEA8, 10);
            for (float u = 1; u + 1 < _w; u += 2.2f)
                for (float v = 1; v + 1 < _d; v += 2.2f)
                {
                    Cylinder(u + 0.4f, v + 0.4f, 0.4f, g, g + 0.75f, 10, 0x1F2F2F2);
                    Cylinder(u + 0.4f, v + 0.4f, 0.05f, g + 0.75f, g + 2.3f, 4, 0x1F2F2F2);
                    Cone(u + 0.4f, v + 0.4f, 1.0f, g + 2.1f, 0.4f, 0x1C8473B);
                }
        }

        private void Flowers(string color)
        {
            uint[] c = color == "rose" ? new uint[] { 0x1D9534A, 0x1E8A0B0, 0x1C0304A } : new uint[] { 0x1E8C030, 0x1D9534A, 0x19A86C8, 0x1F2EDE2 };
            int k = (int)(_seed & 7);
            for (float u = 0.3f; u + 0.3f < _w; u += 0.5f)
                for (float v = 0.3f; v + 0.3f < _d; v += 0.5f)
                {
                    float g = Ground(u, v);
                    Box(u, v, u + 0.22f, v + 0.22f, g + 0.35f, g + 0.55f, c[k++ % c.Length], 0);
                }
        }

        // ------------------------------------------------------------------ piante

        private void Plant(PlantKind kind, float u, float v, float height)
        {
            var p = World(u, v);
            float scale = height / VegetationScatter.BaseHeight(kind);
            _plants?.Add(new PlantInstance { Kind = kind, X = p.X, Y = p.Y, Z = _ground(p.X, p.Y), Scale = scale, Rotation = (_seed % 628) / 100f });
        }

        private void Grid(PlantKind kind, float gap)
        {
            int k = 0;
            for (float u = gap / 2; u <= _w - gap / 2 + 0.01f; u += gap)
                for (float v = gap / 2; v <= _d - gap / 2 + 0.01f; v += gap)
                    Plant(kind, u, v, VegetationScatter.BaseHeight(kind) * (0.85f + 0.3f * Frac(++k)));
        }

        private void Scatter(PlantKind kind, float gap)
        {
            int k = 0;
            for (float u = gap / 2; u < _w; u += gap)
                for (float v = gap / 2; v < _d; v += gap)
                {
                    k++;
                    float du = (Frac(k * 3) - 0.5f) * gap * 0.5f, dv = (Frac(k * 7) - 0.5f) * gap * 0.5f;
                    Plant(kind, Math.Max(0.5f, Math.Min(_w - 0.5f, u + du)), Math.Max(0.5f, Math.Min(_d - 0.5f, v + dv)),
                        VegetationScatter.BaseHeight(kind) * (0.8f + 0.4f * Frac(k * 11)));
                }
        }

        /// <summary>Filari lungo U, distanziati di <paramref name="gap"/> lungo V.</summary>
        private void Rows(PlantKind kind, float gap, int unused)
        {
            float rot = (float)Math.Atan2(_u.Y, _u.X);
            for (float v = gap / 2; v <= _d - gap / 2 + 0.01f; v += gap)
            {
                for (float u0 = 0.5f; u0 < _w - 0.5f; u0 += 10f)
                {
                    float seg = Math.Min(10f, _w - 0.5f - u0);
                    var p = World(u0 + seg / 2, v);
                    _plants?.Add(new PlantInstance { Kind = kind, X = p.X, Y = p.Y, Z = _ground(p.X, p.Y), Scale = 1, Rotation = rot, Length = seg });
                }
            }
        }

        private float Frac(long k)
        {
            unchecked
            {
                uint h = (uint)(k * 2654435761u ^ _seed * 40503u);
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // ------------------------------------------------------------------ primitive (coordinate dell'oggetto)

        private Vec2 World(float u, float v) => _o + _u * u + _v * v;

        private float Ground(float u, float v)
        {
            var p = World(u, v);
            return _ground(p.X, p.Y);
        }

        /// <summary>Quota minima e massima del terreno sul rettangolo (angoli, centri dei lati e centro).</summary>
        private void Range(float u0, float v0, float u1, float v1, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;
            for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++)
            {
                float g = Ground(u0 + (u1 - u0) * i / 2, v0 + (v1 - v0) * j / 2);
                if (g < lo) lo = g;
                if (g > hi) hi = g;
            }
        }

        /// <summary>
        /// Piano d'appoggio di un volume: la quota più alta del rettangolo, così sui pendii niente entra nel terreno;
        /// sotto, verso valle, un basamento in pietra fino al terreno (come gli edifici in collina).
        /// </summary>
        private float Base(float u0, float v0, float u1, float v1)
        {
            Range(u0, v0, u1, v1, out var lo, out var hi);
            if (hi - lo > 0.12f)
            {
                long key = ((long)Math.Round(u0 * 20) * 73856093L) ^ ((long)Math.Round(v0 * 20) * 19349663L) ^ ((long)Math.Round(u1 * 20) * 83492791L) ^ (long)Math.Round(v1 * 20);
                if (_plinths.Add(key)) Box(u0, v0, u1, v1, lo - 0.3f, hi + 0.02f, 0x1B5AA96, 6 * 2);
            }
            return hi;
        }

        /// <summary>Quota media del rettangolo (oggetti bassi e lunghi che seguono il terreno: filari, pannelli a terra).</summary>
        private float Mid(float u0, float v0, float u1, float v1)
        {
            Range(u0, v0, u1, v1, out var lo, out var hi);
            return (lo + hi) / 2;
        }

        private static RoofShape ParseRoof(string s)
        {
            switch (s)
            {
                case "gabled": return RoofShape.Gabled;
                case "flat": return RoofShape.Flat;
                case "pyramidal": return RoofShape.Pyramidal;
                default: return RoofShape.Hipped;
            }
        }

        /// <summary>Edificio procedurale (muri con finestre, tetto) sul rettangolo [u0,u1]×[v0,v1].</summary>
        private void Building(float u0, float v0, float u1, float v1, float height, RoofShape roof, uint wall, uint roofColor, int levels,
            BuildingUse use, bool shops = false, float minZ = float.NaN)
        {
            var p0 = World(u0, v0);
            var p1 = World(u1, v0);
            var p2 = World(u1, v1);
            var p3 = World(u0, v1);
            var outer = new[] { p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y };
            if (Polygon2D.SignedArea(outer) < 0) outer = new[] { p0.X, p0.Y, p3.X, p3.Y, p2.X, p2.Y, p1.X, p1.Y };
            var b = new BuildingInfo
            {
                OsmId = _seed * 31 + (long)(u0 * 7 + v0 * 13), Outer = outer, Holes = Array.Empty<float[]>(), Height = height,
                Roof = roof, Levels = levels, Use = use, WallColor = wall, RoofColor = roofColor, Bounds = Bounds2.Of(outer),
                Flags = shops ? BuildingFlag.HasShops : 0,
            };
            // in piano alla quota più alta della sagoma (basamento verso valle), come gli altri volumi
            float top = Base(u0, v0, u1, v1);
            GroundSampler level = (x, y) => top;
            if (!float.IsNaN(minZ))
            {
                // volume sospeso (tetto di gazebo): base fissa sopra il piano
                b.MinHeight = Math.Max(0.2f, minZ - top);
                b.Height = b.MinHeight + height;
            }
            _building.Build(b, level, _mb, shops);
        }

        /// <summary>Parallelepipedo tra le quote assolute z0 e z1. kind = categoria*2 per i muri (12 = senza finestre).</summary>
        private void Box(float u0, float v0, float u1, float v1, float z0, float z1, uint color, int kind)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var p = new[] { World(u0, v0), World(u1, v0), World(u1, v1), World(u0, v1) };
            if (Polygon2D.SignedArea(new List<Vec2>(p)) < 0) Array.Reverse(p);
            float cat = kind == 0 ? 12 : kind;
            for (int i = 0; i < 4; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % 4];
                var e = b - a;
                float len = e.Length;
                if (len < 1e-4f) continue;
                var n = new Vec2(e.Y / len, -e.X / len);
                int i0 = _mb.Add(a.X, a.Y, z0, n.X, n.Y, 0, c, 0, 0, len, z1 - z0, 0, 0.5f, 1, cat);
                int i1 = _mb.Add(b.X, b.Y, z0, n.X, n.Y, 0, c, len, 0, len, z1 - z0, 0, 0.5f, 1, cat);
                int i2 = _mb.Add(b.X, b.Y, z1, n.X, n.Y, 0, c, len, z1 - z0, len, z1 - z0, 0, 0.5f, 1, cat);
                int i3 = _mb.Add(a.X, a.Y, z1, n.X, n.Y, 0, c, 0, z1 - z0, len, z1 - z0, 0, 0.5f, 1, cat);
                _mb.Quad(i0, i1, i2, i3);
            }
            Flat4(p, z1, c, 2);
        }

        private void Flat4(Vec2[] p, float z, uint c, float kind)
        {
            int t0 = _mb.Add(p[0].X, p[0].Y, z, 0, 0, 1, c, p[0].X, p[0].Y, 0, 0, kind, 0.5f, 1, 0);
            int t1 = _mb.Add(p[1].X, p[1].Y, z, 0, 0, 1, c, p[1].X, p[1].Y, 0, 0, kind, 0.5f, 1, 0);
            int t2 = _mb.Add(p[2].X, p[2].Y, z, 0, 0, 1, c, p[2].X, p[2].Y, 0, 0, kind, 0.5f, 1, 0);
            int t3 = _mb.Add(p[3].X, p[3].Y, z, 0, 0, 1, c, p[3].X, p[3].Y, 0, 0, kind, 0.5f, 1, 0);
            _mb.Quad(t0, t1, t2, t3);
        }

        /// <summary>Superficie orizzontale piana; kind 8 = campo con linee (uv0 in metri del rettangolo), 10 = pavimentazione, 11 = acqua.</summary>
        private void Flat(float u0, float v0, float u1, float v1, float z, uint color, float kind)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var corners = new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };
            var idx = new int[4];
            var pts = new Vec2[4];
            for (int i = 0; i < 4; i++) pts[i] = World(corners[i].Item1, corners[i].Item2);
            bool ccw = Polygon2D.SignedArea(new List<Vec2>(pts)) > 0;
            for (int i = 0; i < 4; i++)
            {
                var (u, v) = corners[i];
                idx[i] = _mb.Add(pts[i].X, pts[i].Y, z, 0, 0, 1, c, u - u0, v - v0, u1 - u0, v1 - v0, kind, 0.5f, 1, 0);
            }
            if (ccw) _mb.Quad(idx[0], idx[1], idx[2], idx[3]);
            else _mb.Quad(idx[0], idx[3], idx[2], idx[1]);
        }

        /// <summary>Copertura del terreno che segue le pendenze (griglia da ~4 m), poco sopra il suolo.</summary>
        private void Ground(float u0, float v0, float u1, float v1, uint color, float lift, float kind = 2)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            int nu = Math.Max(1, (int)Math.Ceiling((u1 - u0) / 4f)), nv = Math.Max(1, (int)Math.Ceiling((v1 - v0) / 4f));
            int start = _mb.VertexCount;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float u = u0 + (u1 - u0) * i / nu, v = v0 + (v1 - v0) * j / nv;
                    var p = World(u, v);
                    _mb.Add(p.X, p.Y, _ground(p.X, p.Y) + lift, 0, 0, 1, c, u - u0, v - v0, u1 - u0, v1 - v0, kind, 0.5f, 1, 0);
                }
            bool ccw = Vec2.Cross(_u, _v) > 0;
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = start + j * (nu + 1) + i, b = a + 1, d = a + nu + 1, e = d + 1;
                    if (ccw) _mb.Quad(a, b, e, d);
                    else _mb.Quad(a, d, e, b);
                }
        }

        private void Cylinder(float cu, float cv, float r, float z0, float z1, int sides, uint color, float bottomScale = 1f, float topScale = 1f)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                var d0 = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
                var d1 = new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1));
                var nm = new Vec2((float)Math.Cos(am), (float)Math.Sin(am));
                var b0 = center + d0 * (r * bottomScale);
                var b1 = center + d1 * (r * bottomScale);
                var t0 = center + d0 * (r * topScale);
                var t1 = center + d1 * (r * topScale);
                float len = (b1 - b0).Length;
                int i0 = _mb.Add(b0.X, b0.Y, z0, nm.X, nm.Y, 0, c, 0, 0, len, z1 - z0, 0, 0.5f, 1, 12);
                int i1 = _mb.Add(b1.X, b1.Y, z0, nm.X, nm.Y, 0, c, len, 0, len, z1 - z0, 0, 0.5f, 1, 12);
                int i2 = _mb.Add(t1.X, t1.Y, z1, nm.X, nm.Y, 0, c, len, z1 - z0, len, z1 - z0, 0, 0.5f, 1, 12);
                int i3 = _mb.Add(t0.X, t0.Y, z1, nm.X, nm.Y, 0, c, 0, z1 - z0, len, z1 - z0, 0, 0.5f, 1, 12);
                _mb.Quad(i0, i1, i2, i3);
                int tc = _mb.Add(center.X, center.Y, z1, 0, 0, 1, c, 0, 0, 0, 0, 2, 0.5f, 1, 0);
                int ta = _mb.Add(t0.X, t0.Y, z1, 0, 0, 1, c, 0, 0, 0, 0, 2, 0.5f, 1, 0);
                int tb = _mb.Add(t1.X, t1.Y, z1, 0, 0, 1, c, 0, 0, 0, 0, 2, 0.5f, 1, 0);
                _mb.Triangle(tc, ta, tb);
            }
        }

        /// <summary>
        /// Poligono (3 o 4 vertici: u, v, quota assoluta) orientato verso (ou, ov, oz) in coordinate dell'oggetto.
        /// kind 0 = muro (cat = categoria muro, 12 senza finestre); per gli altri tipi param va in uv1.z.
        /// </summary>
        private void Poly(float[] uvz, float ou, float ov, float oz, uint color, float kind, float param, float cat = 0)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            int n = uvz.Length / 3;
            var p = new (float x, float y, float z)[n];
            for (int i = 0; i < n; i++)
            {
                var w = World(uvz[3 * i], uvz[3 * i + 1]);
                p[i] = (w.X, w.Y, uvz[3 * i + 2]);
            }
            float ax = p[1].x - p[0].x, ay = p[1].y - p[0].y, az = p[1].z - p[0].z;
            float bx = p[2].x - p[0].x, by = p[2].y - p[0].y, bz = p[2].z - p[0].z;
            float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-6f) return;
            nx /= nl;
            ny /= nl;
            nz /= nl;
            // direzione desiderata nel mondo
            var o = _u * ou + _v * ov;
            if (nx * o.X + ny * o.Y + nz * oz < 0)
            {
                Array.Reverse(p);
                nx = -nx;
                ny = -ny;
                nz = -nz;
            }
            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var q in p)
            {
                zMin = Math.Min(zMin, q.z);
                zMax = Math.Max(zMax, q.z);
            }
            float total = 0;
            for (int i = 1; i < n; i++) total = Math.Max(total, (float)Math.Sqrt((p[i].x - p[0].x) * (p[i].x - p[0].x) + (p[i].y - p[0].y) * (p[i].y - p[0].y)));
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (kind < 0.5f)
                {
                    float along = (float)Math.Sqrt((p[i].x - p[0].x) * (p[i].x - p[0].x) + (p[i].y - p[0].y) * (p[i].y - p[0].y));
                    idx[i] = _mb.Add(p[i].x, p[i].y, p[i].z, nx, ny, nz, c, along, p[i].z - zMin, total, zMax - zMin, 0, 0.5f, 1, cat);
                }
                else idx[i] = _mb.Add(p[i].x, p[i].y, p[i].z, nx, ny, nz, c, p[i].x, p[i].y, 0, 0, kind, 0.5f, param, cat);
            }
            if (n == 3) _mb.Triangle(idx[0], idx[1], idx[2]);
            else _mb.Quad(idx[0], idx[1], idx[2], idx[3]);
        }

        /// <summary>Parallelepipedo verniciato (arredo, cartelloni, telai): tinta piena, senza finestre.</summary>
        private void PaintedBox(float u0, float v0, float u1, float v1, float z0, float z1, uint color)
        {
            Poly(new[] { u0, v0, z0, u1, v0, z0, u1, v0, z1, u0, v0, z1 }, 0, -1, 0, color, BuildingDecorator.KindProp, 0);
            Poly(new[] { u1, v0, z0, u1, v1, z0, u1, v1, z1, u1, v0, z1 }, 1, 0, 0, color, BuildingDecorator.KindProp, 0);
            Poly(new[] { u1, v1, z0, u0, v1, z0, u0, v1, z1, u1, v1, z1 }, 0, 1, 0, color, BuildingDecorator.KindProp, 0);
            Poly(new[] { u0, v1, z0, u0, v0, z0, u0, v0, z1, u0, v1, z1 }, -1, 0, 0, color, BuildingDecorator.KindProp, 0);
            Poly(new[] { u0, v0, z1, u1, v0, z1, u1, v1, z1, u0, v1, z1 }, 0, 0, 1, color, BuildingDecorator.KindProp, 0);
        }

        private void Cone(float cu, float cv, float r, float z0, float h, uint color)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            const int sides = 12;
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                var p0 = center + new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0)) * r;
                var p1 = center + new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1)) * r;
                float nx = (float)Math.Cos(am), ny = (float)Math.Sin(am), nz = r / h;
                float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                int i0 = _mb.Add(p0.X, p0.Y, z0, nx / nl, ny / nl, nz / nl, c, 0, 0, 0, 0, 1, 0.5f, 1, 0);
                int i1 = _mb.Add(p1.X, p1.Y, z0, nx / nl, ny / nl, nz / nl, c, 0, 0, 0, 0, 1, 0.5f, 1, 0);
                int t = _mb.Add(center.X, center.Y, z0 + h, nx / nl, ny / nl, nz / nl, c, 0, 0, 0, 0, 1, 0.5f, 1, 0);
                _mb.Triangle(i0, i1, t);
            }
        }

        private void Dome(float cu, float cv, float r, float z0, uint color, float kind = 2)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            const int sides = 16, rings = 5;
            for (int k = 0; k < rings; k++)
            {
                double b0 = Math.PI / 2 * k / rings, b1 = Math.PI / 2 * (k + 1) / rings;
                for (int i = 0; i < sides; i++)
                {
                    double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides;
                    Vec3 P(double a, double b) => new Vec3(center.X + (float)(Math.Cos(a) * Math.Cos(b) * r), center.Y + (float)(Math.Sin(a) * Math.Cos(b) * r), z0 + (float)(Math.Sin(b) * r));
                    var q0 = P(a0, b0);
                    var q1 = P(a1, b0);
                    var q2 = P(a1, b1);
                    var q3 = P(a0, b1);
                    int Add(Vec3 q) => _mb.Add(q.X, q.Y, q.Z, (q.X - center.X) / r, (q.Y - center.Y) / r, (q.Z - z0) / r, c, 0, 0, 0, 0, kind, 0.5f, kind > 12.5f ? 0 : 1, 0);
                    _mb.Quad(Add(q0), Add(q1), Add(q2), Add(q3));
                }
            }
        }

        private readonly struct Vec3
        {
            public readonly float X, Y, Z;

            public Vec3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }
    }
}
