using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>Risultato di un tocco/clic sul mondo.</summary>
    public struct WorldHit
    {
        public bool Hit;
        public Vector3 Point;
        public TileView Tile;
        /// <summary>Indice dell'edificio nel tile, -1 se si è toccato il terreno.</summary>
        public int Building;

        public BuildingInfo BuildingInfo => Tile != null && Building >= 0 ? Tile.Data.Buildings[Building] : null;
    }

    /// <summary>
    /// Selezione nel mondo senza collider: il raggio avanza a passi e si confronta con le sagome degli edifici
    /// (indice a griglia per tile) e con il terreno. Precisione: la sagoma reale, altezza dalla quota media.
    /// </summary>
    public sealed class WorldPicker
    {
        private const float Cell = 25f;
        private readonly WorldStreamer _world;
        private readonly Dictionary<TileId, Dictionary<long, List<int>>> _grids = new Dictionary<TileId, Dictionary<long, List<int>>>();
        private readonly Dictionary<(TileId, int), float> _ground = new Dictionary<(TileId, int), float>();

        public WorldPicker(WorldStreamer world)
        {
            _world = world;
            world.TileUnloaded += id =>
            {
                _grids.Remove(id);
            };
            world.TileLoaded += v => _grids.Remove(v.Id);
        }

        public WorldHit Raycast(Ray ray, float maxDistance = 20000f)
        {
            var hit = new WorldHit { Building = -1 };
            if (_world.State != WorldStreamer.Status.Ready) return hit;
            float t = 0;
            Vector3 prev = ray.origin;
            while (t < maxDistance)
            {
                float step = Mathf.Clamp(t * 0.004f, 0.4f, 12f);
                t += step;
                var p = ray.GetPoint(t);
                var id = _world.Frame.TileAt(_world.Region.Zoom, p.x, p.z);
                if (!_world.Tiles.TryGetValue(id, out var view) || view.Data == null)
                {
                    prev = p;
                    continue;
                }
                float lx = p.x - view.Origin.x, lz = p.z - view.Origin.z;
                if (view.Lod != TileLod.Far)
                {
                    int b = BuildingAt(view, lx, lz, p.y);
                    if (b >= 0)
                    {
                        hit.Hit = true;
                        hit.Point = p;
                        hit.Tile = view;
                        hit.Building = b;
                        return hit;
                    }
                }
                float ground = view.Data.Terrain.HeightAt(lx, lz);
                if (p.y <= ground)
                {
                    // affina il punto sul terreno tra prev e p
                    var a = prev;
                    var c = p;
                    for (int k = 0; k < 12; k++)
                    {
                        var m = (a + c) * 0.5f;
                        float g = _world.HeightAt(m.x, m.z);
                        if (float.IsNaN(g) || m.y <= g) c = m;
                        else a = m;
                    }
                    hit.Hit = true;
                    hit.Point = c;
                    hit.Tile = view;
                    return hit;
                }
                prev = p;
            }
            return hit;
        }

        private int BuildingAt(TileView view, float x, float z, float y)
        {
            var grid = GridFor(view);
            if (!grid.TryGetValue(Key(x, z), out var list)) return -1;
            var data = view.Data;
            foreach (int i in list)
            {
                var b = data.Buildings[i];
                if (!b.Bounds.Contains(x, z) || !Polygon2D.Contains(b.Outer, b.Holes, x, z) || _world.IsHidden(b)) continue;
                float g = GroundRef(view, i);
                float bottom = b.MinHeight > 0.1f ? g + b.MinHeight : g - 3f;
                if (y >= bottom && y <= g + Mathf.Max(2.5f, b.Height) + 0.2f) return i;
            }
            return -1;
        }

        /// <summary>Quota di riferimento dell'edificio (media dei vertici, come nel BuildingMesher).</summary>
        public float GroundRef(TileView view, int index)
        {
            if (_ground.TryGetValue((view.Id, index), out var g)) return g;
            var b = view.Data.Buildings[index];
            float sum = 0;
            int n = b.Outer.Length / 2;
            for (int i = 0; i < n; i++) sum += view.Data.Terrain.HeightAt(b.Outer[2 * i], b.Outer[2 * i + 1]);
            g = n > 0 ? sum / n : 0;
            _ground[(view.Id, index)] = g;
            return g;
        }

        private static long Key(float x, float z) => ((long)Mathf.FloorToInt(x / Cell) << 32) ^ (uint)Mathf.FloorToInt(z / Cell);

        private Dictionary<long, List<int>> GridFor(TileView view)
        {
            if (_grids.TryGetValue(view.Id, out var grid)) return grid;
            grid = new Dictionary<long, List<int>>();
            var bs = view.Data.Buildings;
            for (int i = 0; i < bs.Length; i++)
            {
                if (bs[i].HasFlag(BuildingFlag.Underground)) continue;
                var bb = bs[i].Bounds;
                int x0 = Mathf.FloorToInt(bb.MinX / Cell), x1 = Mathf.FloorToInt(bb.MaxX / Cell);
                int z0 = Mathf.FloorToInt(bb.MinY / Cell), z1 = Mathf.FloorToInt(bb.MaxY / Cell);
                for (int zz = z0; zz <= z1; zz++)
                for (int xx = x0; xx <= x1; xx++)
                {
                    long k = ((long)xx << 32) ^ (uint)zz;
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(4);
                    l.Add(i);
                }
            }
            _grids[view.Id] = grid;
            return grid;
        }
    }
}
