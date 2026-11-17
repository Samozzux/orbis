using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Indice a griglia dei tratti di strada di un tile (carrabili e pedonali, niente binari, piste e gallerie):
    /// dice quanto è lontano il bordo della strada più vicina, per capire quali facciate danno sulla via
    /// e quanto marciapiede c'è davanti (dehors, tende).
    /// </summary>
    public sealed class RoadIndex
    {
        private const float Cell = 24f;

        private struct Segment
        {
            public Vec2 A, B;
            public float HalfWidth;
        }

        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly List<Segment> _segments = new List<Segment>();

        public int SegmentCount => _segments.Count;

        public RoadIndex(IEnumerable<RoadInfo> roads)
        {
            foreach (var r in roads)
            {
                if (r.IsRail || r.IsAeroway || r.HasFlag(RoadFlag.Tunnel) || r.Points == null) continue;
                if (r.Class == RoadClass.Steps || r.Class == RoadClass.Path || r.Class == RoadClass.Track || r.Class == RoadClass.Bridleway) continue;
                float half = Math.Max(1.0f, (r.Width > 0 ? r.Width : DefaultWidth(r.Class)) * 0.5f);
                for (int i = 0; i + 3 < r.Points.Length; i += 2)
                {
                    var a = new Vec2(r.Points[i], r.Points[i + 1]);
                    var b = new Vec2(r.Points[i + 2], r.Points[i + 3]);
                    int id = _segments.Count;
                    _segments.Add(new Segment { A = a, B = b, HalfWidth = half });
                    int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / Cell), x1 = (int)Math.Floor(Math.Max(a.X, b.X) / Cell);
                    int y0 = (int)Math.Floor(Math.Min(a.Y, b.Y) / Cell), y1 = (int)Math.Floor(Math.Max(a.Y, b.Y) / Cell);
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long k = Key(x, y);
                        if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<int>();
                        list.Add(id);
                    }
                }
            }
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        private static float DefaultWidth(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway:
                case RoadClass.Trunk: return 14f;
                case RoadClass.Primary: return 10f;
                case RoadClass.Secondary: return 9f;
                case RoadClass.Tertiary: return 8f;
                case RoadClass.Footway:
                case RoadClass.Cycleway: return 2.5f;
                case RoadClass.Pedestrian: return 6f;
                default: return 6f;
            }
        }

        /// <summary>Distanza dal punto al bordo della strada più vicina (negativa se il punto è sulla carreggiata); ∞ oltre <paramref name="max"/>.</summary>
        public float DistanceToEdge(Vec2 p, float max = 30f)
        {
            float best = float.PositiveInfinity;
            int x0 = (int)Math.Floor((p.X - max) / Cell), x1 = (int)Math.Floor((p.X + max) / Cell);
            int y0 = (int)Math.Floor((p.Y - max) / Cell), y1 = (int)Math.Floor((p.Y + max) / Cell);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!_cells.TryGetValue(Key(x, y), out var list)) continue;
                foreach (var id in list)
                {
                    var s = _segments[id];
                    float d = DistanceToSegment(p, s.A, s.B) - s.HalfWidth;
                    if (d < best) best = d;
                }
            }
            return best <= max ? best : float.PositiveInfinity;
        }

        private static float DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            float l2 = Vec2.Dot(ab, ab);
            float t = l2 > 1e-6f ? Math.Max(0, Math.Min(1, Vec2.Dot(p - a, ab) / l2)) : 0;
            return (p - (a + ab * t)).Length;
        }
    }
}
