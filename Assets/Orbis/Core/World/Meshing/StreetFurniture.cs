using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Arredo delle strade (docs/08 §3): lampioni sui marciapiedi delle vie urbane, accesi la sera, e incroci
    /// (dove si incontrano almeno tre strade) per le strisce pedonali disegnate dallo shader delle strade.
    /// </summary>
    public static class StreetFurniture
    {
        public const float LampSpacing = 32f;

        /// <summary>Punti dove si incontrano almeno tre tratti di strada carrabile (estremi e punti interni).</summary>
        public static HashSet<long> Intersections(IReadOnlyList<RoadInfo> roads)
        {
            var count = new Dictionary<long, int>();
            foreach (var r in roads)
            {
                if (r.Points == null || r.Points.Length < 4 || RoadMesher.KindOf(r) != RoadMesher.Kind.Road || r.HasFlag(RoadFlag.Tunnel)) continue;
                int n = r.Points.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    long k = Key(r.Points[2 * i], r.Points[2 * i + 1]);
                    // un estremo vale un ramo, un punto interno due (la strada prosegue dai due lati)
                    int add = i == 0 || i == n - 1 ? 1 : 2;
                    count[k] = count.TryGetValue(k, out var c) ? c + add : add;
                }
            }
            var set = new HashSet<long>();
            foreach (var kv in count)
                if (kv.Value >= 3) set.Add(kv.Key);
            return set;
        }

        /// <summary>Chiave di un punto arrotondato a mezzo metro.</summary>
        public static long Key(float x, float y) => ((long)Math.Round(x * 2) << 32) ^ (uint)(int)Math.Round(y * 2);

        /// <summary>Lampioni lungo una via con marciapiedi, a lati alterni; restituisce quanti ne ha messi.</summary>
        public static int Lamps(RoadInfo r, GroundSampler ground, MeshBuffer mb)
        {
            var kind = RoadMesher.KindOf(r);
            if (r.Points == null || r.Points.Length < 4) return 0;
            // vie pedonali con nome (centri storici): lampioni ai bordi del lastricato
            bool pedestrian = kind == RoadMesher.Kind.Paving && !string.IsNullOrEmpty(r.Name);
            if (!pedestrian && !RoadMesher.HasSidewalks(r, kind)) return 0;
            float width = Math.Max(r.Width, 3f);
            float offset = pedestrian ? Math.Max(0.8f, width / 2 - 0.6f) : width / 2 + 0.55f;
            float seed = (StylePalette.Hash(r.OsmId, 61) & 0xFF) / 255f;
            float next = LampSpacing * (0.3f + 0.5f * seed); // non tutte le vie iniziano con un lampione
            float walked = 0;
            int placed = 0;
            for (int i = 0; i + 3 < r.Points.Length; i += 2)
            {
                var a = new Vec2(r.Points[i], r.Points[i + 1]);
                var b = new Vec2(r.Points[i + 2], r.Points[i + 3]);
                var d = b - a;
                float len = d.Length;
                if (len < 0.01f) continue;
                var dir = d * (1f / len);
                var left = new Vec2(-dir.Y, dir.X);
                while (next <= walked + len)
                {
                    float t = (next - walked) / len;
                    float side = (placed & 1) == 0 ? 1 : -1;
                    var p = Vec2.Lerp(a, b, t) + left * (offset * side);
                    Lamp(p, left * -side, ground(p.X, p.Y), mb);
                    placed++;
                    next += LampSpacing;
                }
                walked += len;
            }
            return placed;
        }

        /// <summary>Lampione: palo scuro e lanterna in cima che di notte si accende (18 triangoli).</summary>
        private static void Lamp(Vec2 p, Vec2 towardRoad, float z, MeshBuffer mb)
        {
            uint pole = MeshBuffer.Rgb(0x2F3A33);
            Box(mb, p, towardRoad, 0.07f, 0.07f, z, z + 4.6f, pole, BuildingDecorator.KindProp, 0, false);
            Box(mb, p, towardRoad, 0.2f, 0.2f, z + 4.6f, z + 5.05f, MeshBuffer.Rgb(0xFFE3A3), BuildingDecorator.KindLight, 1, true);
        }

        private static void Box(MeshBuffer mb, Vec2 c, Vec2 axis, float hu, float hv, float z0, float z1, uint col, float kind, float param, bool top)
        {
            var side = new Vec2(-axis.Y, axis.X);
            var p0 = c - axis * hu - side * hv;
            var p1 = c + axis * hu - side * hv;
            var p2 = c + axis * hu + side * hv;
            var p3 = c - axis * hu + side * hv;
            Side(mb, p0, p1, z0, z1, col, kind, param);
            Side(mb, p1, p2, z0, z1, col, kind, param);
            Side(mb, p2, p3, z0, z1, col, kind, param);
            Side(mb, p3, p0, z0, z1, col, kind, param);
            if (!top) return;
            int t0 = mb.Add(p0.X, p0.Y, z1, 0, 0, 1, col, 0, 0, 2 * hu, 2 * hv, kind, 0.5f, param, 0);
            int t1 = mb.Add(p1.X, p1.Y, z1, 0, 0, 1, col, 2 * hu, 0, 2 * hu, 2 * hv, kind, 0.5f, param, 0);
            int t2 = mb.Add(p2.X, p2.Y, z1, 0, 0, 1, col, 2 * hu, 2 * hv, 2 * hu, 2 * hv, kind, 0.5f, param, 0);
            int t3 = mb.Add(p3.X, p3.Y, z1, 0, 0, 1, col, 0, 2 * hv, 2 * hu, 2 * hv, kind, 0.5f, param, 0);
            mb.Quad(t0, t1, t2, t3);
        }

        private static void Side(MeshBuffer mb, Vec2 a, Vec2 b, float z0, float z1, uint col, float kind, float param)
        {
            var d = b - a;
            float len = d.Length;
            if (len < 1e-4f) return;
            var n = new Vec2(d.Y / len, -d.X / len);
            float h = z1 - z0;
            int i0 = mb.Add(a.X, a.Y, z0, n.X, n.Y, 0, col, 0, 0, len, h, kind, 0.5f, param, 0);
            int i1 = mb.Add(b.X, b.Y, z0, n.X, n.Y, 0, col, len, 0, len, h, kind, 0.5f, param, 0);
            int i2 = mb.Add(b.X, b.Y, z1, n.X, n.Y, 0, col, len, h, len, h, kind, 0.5f, param, 0);
            int i3 = mb.Add(a.X, a.Y, z1, n.X, n.Y, 0, col, 0, h, len, h, kind, 0.5f, param, 0);
            mb.Quad(i0, i1, i2, i3);
        }
    }
}
