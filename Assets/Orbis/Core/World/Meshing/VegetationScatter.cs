using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>Tipi di pianta da disegnare con l'instancing.</summary>
    public enum PlantKind
    {
        Broadleaf = 0, Conifer, Palm, Olive, Cypress, StonePine, Fruit, Shrub, VineRow, Count,
    }

    public struct PlantInstance
    {
        public float X, Y, Z;
        public float Rotation; // radianti
        public float Scale;
        public float Length;   // per i filari di vite: lunghezza in metri
        public PlantKind Kind;
    }

    /// <summary>
    /// Distribuisce alberi e piante: alberi singoli OSM, boschi, macchia, uliveti e frutteti a sesto regolare,
    /// vigneti a filari allineati al campo, parchi. Le piante evitano edifici e strade. Sempre uguale per lo
    /// stesso tile (semi dagli ID OSM).
    /// </summary>
    public sealed class VegetationScatter
    {
        public int MaxInstances = 30000;

        private readonly SpatialIndex _blockers = new SpatialIndex(24f);

        public List<PlantInstance> Scatter(TileData tile, GroundSampler ground, bool areas = true)
        {
            var result = new List<PlantInstance>(1024);
            _blockers.Clear();
            for (int i = 0; i < tile.Buildings.Length; i++) _blockers.AddPolygon(tile.Buildings[i].Outer, 1.0f);
            foreach (var r in tile.Roads)
            {
                if (r.HasFlag(RoadFlag.Tunnel)) continue;
                _blockers.AddLine(r.Points, r.Width / 2 + 1.5f);
            }

            // alberi singoli OSM
            for (int i = 0; i < tile.TreeCount; i++)
            {
                float x = tile.TreePoints[2 * i], y = tile.TreePoints[2 * i + 1];
                var kind = FromOsm(tile.TreeKinds[i]);
                float h = tile.TreeHeights[i];
                float scale = h > 0 ? h / BaseHeight(kind) : 0.85f + 0.3f * Hash01(i, 3, tile.Id.X);
                Add(result, kind, x, y, ground, scale, Hash01(i, 5, tile.Id.Y) * 6.283f);
            }
            if (!areas) return result;

            foreach (var a in tile.Areas)
            {
                if (result.Count >= MaxInstances) break;
                switch (a.Kind)
                {
                    case AreaKind.Forest:
                        Random(result, a, ground, 8.5f, 0.9f, PlantKind.Broadleaf, PlantKind.Conifer, 0.25f);
                        break;
                    case AreaKind.Scrub:
                    case AreaKind.Heath:
                        Random(result, a, ground, 10f, 0.6f, PlantKind.Shrub, PlantKind.Broadleaf, 0.15f);
                        break;
                    case AreaKind.Park:
                    case AreaKind.Garden:
                    case AreaKind.Cemetery:
                        Random(result, a, ground, 13f, 0.45f, PlantKind.Broadleaf,
                            a.Kind == AreaKind.Cemetery ? PlantKind.Cypress : PlantKind.StonePine, 0.3f);
                        break;
                    case AreaKind.OliveGrove:
                        Rows(result, a, ground, 7f, 7f, PlantKind.Olive);
                        break;
                    case AreaKind.Orchard:
                        Rows(result, a, ground, 5.5f, 5f, PlantKind.Fruit);
                        break;
                    case AreaKind.Vineyard:
                        Vines(result, a, ground);
                        break;
                }
            }
            return result;
        }

        private static PlantKind FromOsm(TreeKind k)
        {
            switch (k)
            {
                case TreeKind.Conifer: return PlantKind.Conifer;
                case TreeKind.Palm: return PlantKind.Palm;
                case TreeKind.Olive: return PlantKind.Olive;
                case TreeKind.Cypress: return PlantKind.Cypress;
                case TreeKind.StonePine: return PlantKind.StonePine;
                case TreeKind.Fruit: return PlantKind.Fruit;
                case TreeKind.Shrub: return PlantKind.Shrub;
                default: return PlantKind.Broadleaf;
            }
        }

        /// <summary>Altezza in metri dei modelli a scala 1.</summary>
        public static float BaseHeight(PlantKind k)
        {
            switch (k)
            {
                case PlantKind.Conifer: return 12f;
                case PlantKind.Palm: return 9f;
                case PlantKind.Olive: return 5f;
                case PlantKind.Cypress: return 13f;
                case PlantKind.StonePine: return 13f;
                case PlantKind.Fruit: return 4.5f;
                case PlantKind.Shrub: return 2.2f;
                case PlantKind.VineRow: return 1.6f;
                default: return 10f;
            }
        }

        private bool Add(List<PlantInstance> list, PlantKind kind, float x, float y, GroundSampler ground, float scale, float rot, float length = 0)
        {
            if (list.Count >= MaxInstances) return false;
            list.Add(new PlantInstance { Kind = kind, X = x, Y = y, Z = ground(x, y), Scale = scale, Rotation = rot, Length = length });
            return true;
        }

        private void Random(List<PlantInstance> list, AreaInfo a, GroundSampler ground, float spacing, float fill,
            PlantKind main, PlantKind other, float otherShare)
        {
            var b = a.Bounds;
            int seed = (int)(a.OsmId & 0x7FFFFFFF);
            int nx = (int)(b.Width / spacing) + 1, ny = (int)(b.Height / spacing) + 1;
            if ((long)nx * ny > 400000) return; // aree enormi: solo nei tile vicini e già tagliate dalla pipeline
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                if (Hash01(i, j, seed) > fill) continue;
                float x = b.MinX + (i + Hash01(i, j, seed + 1)) * spacing;
                float y = b.MinY + (j + Hash01(i, j, seed + 2)) * spacing;
                if (!Polygon2D.Contains(a.Outer, a.Holes, x, y) || _blockers.Blocks(x, y)) continue;
                var kind = Hash01(i, j, seed + 3) < otherShare ? other : main;
                if (!Add(list, kind, x, y, ground, 0.75f + 0.5f * Hash01(i, j, seed + 4), Hash01(i, j, seed + 5) * 6.283f)) return;
            }
        }

        /// <summary>Sesto d'impianto regolare allineato al lato lungo del campo.</summary>
        private void Rows(List<PlantInstance> list, AreaInfo a, GroundSampler ground, float rowGap, float plantGap, PlantKind kind)
        {
            var pts = Polygon2D.ToPoints(a.Outer);
            if (pts.Length < 3) return;
            var box = OrientedBox.Of(pts);
            int seed = (int)(a.OsmId & 0x7FFFFFFF);
            int nu = (int)(2 * box.HalfU / plantGap), nv = (int)(2 * box.HalfV / rowGap);
            if ((long)nu * nv > 200000) return;
            for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
            {
                float u = -box.HalfU + plantGap * (i + 0.5f), v = -box.HalfV + rowGap * (j + 0.5f);
                var p = box.Center + box.U * u + box.V * v;
                if (!Polygon2D.Contains(a.Outer, a.Holes, p.X, p.Y) || _blockers.Blocks(p.X, p.Y)) continue;
                if (!Add(list, kind, p.X, p.Y, ground, 0.8f + 0.35f * Hash01(i, j, seed), Hash01(i, j, seed + 9) * 6.283f)) return;
            }
        }

        /// <summary>Filari di vite: segmenti continui lungo il lato lungo, interrotti da strade ed edifici.</summary>
        private void Vines(List<PlantInstance> list, AreaInfo a, GroundSampler ground)
        {
            const float rowGap = 2.6f, step = 2f;
            var pts = Polygon2D.ToPoints(a.Outer);
            if (pts.Length < 3) return;
            var box = OrientedBox.Of(pts);
            int nv = (int)(2 * box.HalfV / rowGap);
            int nu = (int)(2 * box.HalfU / step);
            if ((long)nu * nv > 300000) return;
            float rot = (float)Math.Atan2(box.U.Y, box.U.X);
            for (int j = 0; j <= nv; j++)
            {
                float v = -box.HalfV + rowGap * (j + 0.5f);
                int runStart = -1;
                for (int i = 0; i <= nu + 1; i++)
                {
                    bool ok = false;
                    Vec2 p = default;
                    if (i <= nu)
                    {
                        p = box.Center + box.U * (-box.HalfU + step * (i + 0.5f)) + box.V * v;
                        ok = Polygon2D.Contains(a.Outer, a.Holes, p.X, p.Y) && !_blockers.Blocks(p.X, p.Y);
                    }
                    if (ok && runStart < 0) runStart = i;
                    if ((!ok || i == nu + 1) && runStart >= 0)
                    {
                        // filare da runStart a i-1: un'istanza ogni ~10 m per seguire il terreno
                        int count = i - runStart;
                        const int chunk = 5;
                        for (int s = 0; s < count; s += chunk)
                        {
                            int len = Math.Min(chunk, count - s);
                            float uMid = -box.HalfU + step * (runStart + s + len / 2f);
                            var c = box.Center + box.U * uMid + box.V * v;
                            if (!Add(list, PlantKind.VineRow, c.X, c.Y, ground, 1f, rot, len * step)) return;
                        }
                        runStart = -1;
                    }
                }
            }
        }

        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ seed * 83492791);
                h ^= h >> 15;
                h *= 2246822519;
                h ^= h >> 13;
                h *= 3266489917;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>Griglia uniforme di ostacoli (sagome ingrandite e segmenti di strada con raggio).</summary>
    public sealed class SpatialIndex
    {
        private readonly float _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly List<float[]> _polys = new List<float[]>();
        private readonly List<float> _margins = new List<float>();
        private readonly List<(float ax, float ay, float bx, float by, float r)> _segs = new List<(float, float, float, float, float)>();

        public SpatialIndex(float cell) => _cell = cell;

        public void Clear()
        {
            _cells.Clear();
            _polys.Clear();
            _margins.Clear();
            _segs.Clear();
        }

        private long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        private void Insert(float minX, float minY, float maxX, float maxY, int id)
        {
            int x0 = (int)Math.Floor(minX / _cell), x1 = (int)Math.Floor(maxX / _cell);
            int y0 = (int)Math.Floor(minY / _cell), y1 = (int)Math.Floor(maxY / _cell);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 4096) return;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var k = Key(x, y);
                if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>(4);
                l.Add(id);
            }
        }

        public void AddPolygon(float[] ring, float margin)
        {
            var b = Bounds2.Of(ring);
            _polys.Add(ring);
            _margins.Add(margin);
            Insert(b.MinX - margin, b.MinY - margin, b.MaxX + margin, b.MaxY + margin, _polys.Count - 1);
        }

        public void AddLine(float[] pts, float radius)
        {
            for (int i = 0; i + 3 < pts.Length; i += 2)
            {
                float ax = pts[i], ay = pts[i + 1], bx = pts[i + 2], by = pts[i + 3];
                _segs.Add((ax, ay, bx, by, radius));
                Insert(Math.Min(ax, bx) - radius, Math.Min(ay, by) - radius, Math.Max(ax, bx) + radius, Math.Max(ay, by) + radius,
                    -_segs.Count);
            }
        }

        public bool Blocks(float x, float y)
        {
            if (!_cells.TryGetValue(Key((int)Math.Floor(x / _cell), (int)Math.Floor(y / _cell)), out var l)) return false;
            foreach (int id in l)
            {
                if (id >= 0)
                {
                    var ring = _polys[id];
                    if (Polygon2D.ContainsRing(ring, x, y)) return true;
                    if (_margins[id] > 0 && NearRing(ring, x, y, _margins[id])) return true;
                }
                else
                {
                    var s = _segs[-id - 1];
                    if (SegDist2(x, y, s.ax, s.ay, s.bx, s.by) < s.r * s.r) return true;
                }
            }
            return false;
        }

        private static bool NearRing(float[] ring, float x, float y, float r)
        {
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if (SegDist2(x, y, ring[2 * j], ring[2 * j + 1], ring[2 * i], ring[2 * i + 1]) < r * r) return true;
            return false;
        }

        public static float SegDist2(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float l2 = dx * dx + dy * dy;
            float t = l2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            float qx = ax + t * dx - px, qy = ay + t * dy - py;
            return qx * qx + qy * qy;
        }
    }
}
