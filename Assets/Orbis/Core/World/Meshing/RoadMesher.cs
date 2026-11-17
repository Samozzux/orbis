using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Nastri delle strade e delle ferrovie appoggiati sul terreno (ponti in rettilineo tra le spalle),
    /// con marciapiedi per le vie urbane e "tappi" rotondi alle estremità per chiudere gli incroci.
    /// uv0 = (traverso 0..1, lungo la strada in m, larghezza m, corsie); uv1 = (tipo, senso unico, classe, segnaletica).
    /// </summary>
    public sealed class RoadMesher
    {
        public enum Kind { Road = 0, Sidewalk = 1, Paving = 2, Unpaved = 3, Footpath = 4, Rail = 5, Steps = 6, Runway = 7 }

        private const float MaxSegment = 6f;
        private const float SidewalkWidth = 1.6f;

        private readonly List<Vec2> _pts = new List<Vec2>(64);
        private readonly List<float> _dist = new List<float>(64);
        private readonly List<float> _z = new List<float>(64);

        public void Build(IReadOnlyList<RoadInfo> roads, GroundSampler ground, MeshBuffer mb)
        {
            foreach (var r in roads) Build(r, ground, mb);
        }

        /// <param name="zebraStart">strisce pedonali all'inizio (incrocio)</param>
        /// <param name="zebraEnd">strisce pedonali alla fine (incrocio)</param>
        public void Build(RoadInfo r, GroundSampler ground, MeshBuffer mb, bool zebraStart = false, bool zebraEnd = false)
        {
            if (r.HasFlag(RoadFlag.Tunnel) || r.Class == RoadClass.Abandoned || r.Points.Length < 4) return;
            var kind = KindOf(r);
            float width = Math.Max(r.Width, MinWidth(kind));
            float lift = 0.10f + Rank(r.Class) * 0.008f;
            if (!Resample(r.Points)) return;
            ComputeHeights(r, ground, lift);
            uint color = MeshBuffer.Rgb(ColorOf(r, kind));
            bool markings = kind == Kind.Road && r.Class <= RoadClass.Tertiary;
            float uv1z = (float)r.Class;
            // segnaletica: bit 0 righe di bordo e mezzeria, bit 1-2 strisce pedonali a inizio/fine, bit 3 solo mezzeria
            // (vie residenziali larghe); sopra, la lunghezza in quarti di metro
            bool zebra = kind == Kind.Road && width > 4f && r.Class <= RoadClass.LivingStreet && _dist[_dist.Count - 1] > 20f;
            bool centerOnly = !markings && kind == Kind.Road && width >= 5.5f && r.Class <= RoadClass.Residential && r.Oneway == 0;
            int flags = (markings ? 1 : 0) | (zebra && zebraStart ? 2 : 0) | (zebra && zebraEnd ? 4 : 0) | (centerOnly ? 8 : 0);
            float signage = flags + 16f * (float)Math.Round(_dist[_dist.Count - 1] * 4f);
            Ribbon(mb, width, 0, color, (float)kind, r.Oneway, uv1z, signage, r.Lanes);
            if (HasSidewalks(r, kind))
                Ribbon(mb, width + 2 * SidewalkWidth, -0.04f, MeshBuffer.Rgb(0xB7B1A6), (float)Kind.Sidewalk, 0, uv1z, 0, 0);
            if (kind != Kind.Rail && kind != Kind.Steps)
            {
                Cap(mb, 0, width, color, kind, uv1z);
                Cap(mb, _pts.Count - 1, width, color, kind, uv1z);
            }
        }

        public static Kind KindOf(RoadInfo r)
        {
            if (r.IsRail) return Kind.Rail;
            if (r.IsAeroway) return Kind.Runway;
            switch (r.Class)
            {
                case RoadClass.Pedestrian:
                case RoadClass.LivingStreet when r.HasFlag(RoadFlag.Area):
                    return Kind.Paving;
                case RoadClass.Footway:
                case RoadClass.Cycleway:
                case RoadClass.Path:
                case RoadClass.Bridleway:
                    return r.HasFlag(RoadFlag.Unpaved) ? Kind.Unpaved : Kind.Footpath;
                case RoadClass.Steps:
                    return Kind.Steps;
                case RoadClass.Track:
                    return Kind.Unpaved;
            }
            return r.HasFlag(RoadFlag.Unpaved) ? Kind.Unpaved : Kind.Road;
        }

        private static float MinWidth(Kind k)
        {
            switch (k)
            {
                case Kind.Footpath: return 1.4f;
                case Kind.Steps: return 1.8f;
                case Kind.Rail: return 2.8f;
                case Kind.Unpaved: return 2.5f;
                default: return 3f;
            }
        }

        private static int Rank(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway: return 12;
                case RoadClass.Trunk: return 11;
                case RoadClass.Primary: return 10;
                case RoadClass.Secondary: return 9;
                case RoadClass.Tertiary: return 8;
                case RoadClass.Unclassified: return 6;
                case RoadClass.Residential: return 7;
                case RoadClass.LivingStreet: return 6;
                case RoadClass.Pedestrian: return 5;
                case RoadClass.Service: return 4;
                case RoadClass.Track: return 2;
                default: return c >= RoadClass.Rail ? 3 : 1;
            }
        }

        private static uint ColorOf(RoadInfo r, Kind k)
        {
            switch (k)
            {
                case Kind.Paving: return 0xD5CBB8;
                case Kind.Unpaved: return 0xC3AB84;
                case Kind.Footpath: return 0xCFC6B4;
                case Kind.Steps: return 0xD9D1C2;
                case Kind.Rail: return 0x8D8279;
                case Kind.Runway: return 0x6E7072;
            }
            switch (r.Class)
            {
                case RoadClass.Motorway:
                case RoadClass.Trunk: return 0x4B4F54;
                case RoadClass.Primary:
                case RoadClass.Secondary: return 0x52565B;
                case RoadClass.Service: return 0x67696C;
                default: return 0x5B5E63;
            }
        }

        public static bool HasSidewalks(RoadInfo r, Kind k)
        {
            if (k != Kind.Road || r.HasFlag(RoadFlag.Bridge)) return false;
            if (r.HasFlag(RoadFlag.SidewalkLeft) || r.HasFlag(RoadFlag.SidewalkRight)) return true;
            switch (r.Class)
            {
                case RoadClass.Residential:
                case RoadClass.LivingStreet:
                case RoadClass.Tertiary:
                case RoadClass.Secondary:
                case RoadClass.Primary:
                    return !string.IsNullOrEmpty(r.Name) && string.IsNullOrEmpty(r.Ref);
            }
            return false;
        }

        private bool Resample(float[] flat)
        {
            _pts.Clear();
            _dist.Clear();
            float total = 0;
            var prev = new Vec2(flat[0], flat[1]);
            _pts.Add(prev);
            _dist.Add(0);
            for (int i = 2; i + 1 < flat.Length; i += 2)
            {
                var p = new Vec2(flat[i], flat[i + 1]);
                float len = (p - prev).Length;
                if (len < 0.05f) continue;
                int parts = Math.Max(1, (int)Math.Ceiling(len / MaxSegment));
                for (int k = 1; k <= parts; k++)
                {
                    _pts.Add(Vec2.Lerp(prev, p, k / (float)parts));
                    _dist.Add(total + len * k / parts);
                }
                total += len;
                prev = p;
            }
            return _pts.Count >= 2;
        }

        private void ComputeHeights(RoadInfo r, GroundSampler ground, float lift)
        {
            _z.Clear();
            for (int i = 0; i < _pts.Count; i++) _z.Add(ground(_pts[i].X, _pts[i].Y) + lift);
            if (!r.HasFlag(RoadFlag.Bridge)) return;
            // ponte: rettilineo tra le due estremità, mai sotto il terreno
            float z0 = _z[0], z1 = _z[_z.Count - 1], total = _dist[_dist.Count - 1];
            for (int i = 0; i < _z.Count; i++)
            {
                float t = total > 0 ? _dist[i] / total : 0;
                float straight = z0 + (z1 - z0) * t + 0.5f;
                _z[i] = Math.Max(straight, _z[i] + 0.4f);
            }
        }

        private void Ribbon(MeshBuffer mb, float width, float dz, uint color, float kind, int oneway, float cls, float markings, int lanes)
        {
            float half = width / 2;
            int n = _pts.Count;
            int first = mb.VertexCount;
            for (int i = 0; i < n; i++)
            {
                var dirIn = i > 0 ? (_pts[i] - _pts[i - 1]).Normalized : (_pts[1] - _pts[0]).Normalized;
                var dirOut = i < n - 1 ? (_pts[i + 1] - _pts[i]).Normalized : dirIn;
                var tangent = (dirIn + dirOut).Normalized;
                if (tangent.Length < 0.5f) tangent = dirOut;
                var left = new Vec2(-tangent.Y, tangent.X);
                // giunto a mitra, limitato nelle curve strette
                float cos = Vec2.Dot(left, new Vec2(-dirOut.Y, dirOut.X));
                float miter = half / Math.Max(0.5f, cos);
                var pl = _pts[i] + left * miter;
                var pr = _pts[i] - left * miter;
                float z = _z[i] + dz;
                mb.Add(pl.X, pl.Y, z, 0, 0, 1, color, 0, _dist[i], width, lanes, kind, oneway, cls, markings);
                mb.Add(pr.X, pr.Y, z, 0, 0, 1, color, 1, _dist[i], width, lanes, kind, oneway, cls, markings);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int l0 = first + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                // visto dall'alto: r0 → r1 → l1 → l0 è antiorario
                mb.Quad(r0, r1, l1, l0);
            }
        }

        private void Cap(MeshBuffer mb, int i, float width, uint color, Kind kind, float cls)
        {
            const int sides = 10;
            float half = width / 2;
            var c = _pts[i];
            float z = _z[i] - 0.002f;
            int center = mb.Add(c.X, c.Y, z, 0, 0, 1, color, 0.5f, _dist[i], width, 0, (float)kind, 0, cls, 0);
            for (int k = 0; k < sides; k++)
            {
                double a = 2 * Math.PI * k / sides;
                mb.Add(c.X + (float)Math.Cos(a) * half, c.Y + (float)Math.Sin(a) * half, z, 0, 0, 1, color,
                    0.5f, _dist[i], width, 0, (float)kind, 0, cls, 0);
            }
            for (int k = 0; k < sides; k++) mb.Triangle(center, center + 1 + k, center + 1 + (k + 1) % sides);
        }
    }
}
