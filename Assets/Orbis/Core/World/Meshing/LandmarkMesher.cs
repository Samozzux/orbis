using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Generatori dedicati ai luoghi famosi (docs/02 §5, data/landmarks): partono dalla sagoma reale OSM
    /// e aggiungono gli elementi che li rendono riconoscibili, senza uscire dall'impronta.
    ///  - cathedral: pietra chiara, tetto a capanna, campanile con cella campanaria e cuspide;
    ///  - theatre: facciata neoclassica con portico a colonne sul lato verso la strada più larga;
    ///  - stadium: (area leisure=stadium) campo con le linee, tribune a gradoni, copertura, torri faro.
    /// I muri usano lo shader degli edifici (stesse finestre e luci notturne).
    /// </summary>
    public sealed class LandmarkMesher
    {
        private readonly BuildingMesher _building = new BuildingMesher();

        public static string GeneratorFor(string landmarkKey)
        {
            if (string.IsNullOrEmpty(landmarkKey)) return null;
            if (landmarkKey.StartsWith("cattedrale", StringComparison.Ordinal) || landmarkKey.StartsWith("chiesa", StringComparison.Ordinal)) return "cathedral";
            if (landmarkKey.StartsWith("teatro", StringComparison.Ordinal)) return "theatre";
            if (landmarkKey.StartsWith("stadio", StringComparison.Ordinal)) return "stadium";
            return null;
        }

        /// <summary>Edificio-landmark: true se generato (altrimenti si usa il mesher normale).</summary>
        public bool BuildBuilding(BuildingInfo b, GroundSampler ground, MeshBuffer mb, bool isTallestPart)
        {
            switch (GeneratorFor(b.Landmark))
            {
                case "cathedral": return Cathedral(b, ground, mb, isTallestPart);
                case "theatre": return Theatre(b, ground, mb);
                default: return false;
            }
        }

        // ------------------------------------------------------------------ cattedrale

        private bool Cathedral(BuildingInfo b, GroundSampler ground, MeshBuffer mb, bool isTallestPart)
        {
            var outer = Polygon2D.Clean(b.Outer, true);
            if (outer.Count < 3) return false;
            var box = OrientedBox.Of(outer);
            var stone = b;
            if ((b.WallColor & 0x1000000) == 0) stone = Copy(b, 0x1E3D6B8);
            if ((stone.RoofColor & 0x1000000) == 0) stone.RoofColor = 0x1B8664A;
            bool tower = b.HasFlag(BuildingFlag.Part) ? isTallestPart && box.HalfU < 9f : false;
            if (tower)
            {
                // la parte più alta è il campanile: fusto, cella campanaria e cuspide a piramide
                stone.Roof = RoofShape.Pyramidal;
                stone.Use = BuildingUse.Tower;
                stone.RoofHeight = Math.Min(box.HalfV * 2.2f, b.Height * 0.3f);
                return _building.Build(stone, ground, mb, false);
            }
            if (!b.HasFlag(BuildingFlag.Part) && box.HalfU >= 10f && box.HalfV >= 6f)
            {
                // un solo contorno: navata a capanna sulla sagoma + campanile quadrato dentro l'impronta,
                // all'estremità del lato lungo (la cattedrale di Chieti ha il campanile sulla facciata)
                stone.Roof = RoofShape.Gabled;
                stone.Use = BuildingUse.Cathedral;
                stone.Height = Math.Max(b.Height, 22f);
                if (!_building.Build(stone, ground, mb, false)) return false;
                float side = Math.Min(Math.Min(box.HalfV * 1.1f, 9f), box.HalfU * 0.5f);
                var c = box.Center + box.U * (box.HalfU - side / 2 - 0.2f) + box.V * (box.HalfV - side / 2 - 0.2f);
                var t = Square(c, box.U, box.V, side / 2);
                if (!AllInside(t, b)) c = box.Center + box.U * (box.HalfU - side / 2 - 0.2f);
                t = Square(c, box.U, box.V, side / 2);
                if (!AllInside(t, b)) return true;
                var campanile = Copy(stone, stone.WallColor);
                campanile.OsmId = b.OsmId ^ 0x5A5A;
                campanile.Outer = t;
                campanile.Holes = Array.Empty<float[]>();
                campanile.Height = Math.Max(stone.Height * 1.9f, 42f);
                campanile.Roof = RoofShape.Pyramidal;
                campanile.RoofHeight = side * 1.2f;
                campanile.Use = BuildingUse.Tower;
                campanile.Levels = 8;
                campanile.Bounds = Bounds2.Of(t);
                _building.Build(campanile, ground, mb, false);
                return true;
            }
            stone.Use = BuildingUse.Cathedral;
            return _building.Build(stone, ground, mb, false);
        }

        // ------------------------------------------------------------------ teatro

        private bool Theatre(BuildingInfo b, GroundSampler ground, MeshBuffer mb)
        {
            var t = Copy(b, (b.WallColor & 0x1000000) != 0 ? b.WallColor : 0x1E2B48A);
            t.Use = BuildingUse.Theatre;
            if (t.Roof == RoofShape.Unknown || t.Roof == RoofShape.Flat) t.Roof = RoofShape.Hipped;
            t.Height = Math.Max(b.Height, 18f);
            if (!_building.Build(t, ground, mb, false)) return false;
            // portico: colonne lungo il lato più lungo, appena dentro la sagoma
            var outer = Polygon2D.Clean(b.Outer, true);
            int best = -1;
            float bestLen = 0;
            for (int i = 0; i < outer.Count; i++)
            {
                float len = (outer[(i + 1) % outer.Count] - outer[i]).Length;
                if (len > bestLen)
                {
                    bestLen = len;
                    best = i;
                }
            }
            if (best < 0 || bestLen < 10f) return true;
            var a = outer[best];
            var d = (outer[(best + 1) % outer.Count] - a).Normalized;
            var inward = new Vec2(-d.Y, d.X);
            int columns = Math.Max(4, (int)(bestLen / 3.2f));
            float gRef = ground(a.X + d.X * bestLen / 2, a.Y + d.Y * bestLen / 2);
            uint white = MeshBuffer.Rgb(0xF2EDE2);
            for (int k = 0; k < columns; k++)
            {
                float u = bestLen * (k + 0.5f) / columns;
                var p = a + d * u + inward * 0.9f;
                Column(mb, p, gRef - 0.3f, gRef + 9.5f, 0.42f, white);
            }
            return true;
        }

        // ------------------------------------------------------------------ stadio

        /// <summary>Stadio dall'area OSM (leisure=stadium): tutto dentro il rettangolo orientato dell'area.</summary>
        /// <param name="stands">false se in OSM le tribune sono già mappate come edifici</param>
        public bool Stadium(AreaInfo area, AreaInfo pitch, GroundSampler ground, MeshBuffer mb, bool stands = true)
        {
            var outer = Polygon2D.Clean(area.Outer, true);
            if (outer.Count < 3) return false;
            var box = OrientedBox.Of(outer);
            if (box.HalfU < 30f || box.HalfV < 20f) return false;
            // campo: dall'area leisure=pitch se c'è, altrimenti 105×68 al centro (ridotto se serve)
            OrientedBox field;
            if (pitch != null)
            {
                field = OrientedBox.Of(Polygon2D.Clean(pitch.Outer, true));
            }
            else
            {
                field = box;
                field.HalfU = Math.Min(52.5f, box.HalfU - 10f);
                field.HalfV = Math.Min(34f, box.HalfV - 10f);
            }
            float g = ground(box.Center.X, box.Center.Y);
            // prato con le linee (tipo "campo" del materiale strade: uv1.x = 8)
            Quad(mb, field.Center, field.U, field.V, field.HalfU + 3f, field.HalfV + 3f, g + 0.15f, MeshBuffer.Rgb(0x5FA24C), 8f);

            // tribune: quattro lati tra il bordo del campo (+ pista) e il bordo dell'area
            uint seats = MeshBuffer.Rgb(0xC9CED8), concrete = MeshBuffer.Rgb(0xB9B4AA);
            float gap = 6f;
            for (int side = 0; side < 4 && stands; side++)
            {
                bool longSide = side < 2;
                var axis = longSide ? box.V : box.U;
                var along = longSide ? box.U : box.V;
                float sign = side % 2 == 0 ? 1 : -1;
                float inner = (longSide ? field.HalfV : field.HalfU) + gap;
                float outerEdge = (longSide ? box.HalfV : box.HalfU) - 1f;
                float depth = outerEdge - inner;
                if (depth < 6f) continue;
                float halfLen = (longSide ? field.HalfU : field.HalfV) + (longSide ? 2f : 0f);
                int steps = Math.Min(24, (int)(depth / 0.85f));
                float rise = longSide ? 0.5f : 0.42f;
                for (int s = 0; s < steps; s++)
                {
                    float d0 = inner + depth * s / steps, d1 = inner + depth * (s + 1) / steps;
                    var c = box.Center + axis * (sign * (d0 + d1) / 2);
                    float top = g + 1.2f + rise * (s + 1);
                    Block(mb, c, along, axis, halfLen, (d1 - d0) / 2, g - 1f, top, s % 2 == 0 ? seats : concrete);
                }
                // copertura sulla tribuna principale (primo lato lungo)
                if (side == 0)
                {
                    float roofZ = g + 1.2f + rise * steps + 5f;
                    var rc = box.Center + axis * (sign * (inner + depth * 0.55f));
                    Block(mb, rc, along, axis, halfLen, depth * 0.5f, roofZ, roofZ + 0.6f, MeshBuffer.Rgb(0xE8ECEF));
                    for (int k = -2; k <= 2; k++)
                    {
                        var pc = box.Center + axis * (sign * (outerEdge - 1f)) + along * (halfLen * k / 2.5f);
                        Column(mb, pc, g, roofZ, 0.35f, MeshBuffer.Rgb(0xD5D9DD));
                    }
                }
            }
            // torri faro agli angoli
            for (int k = 0; k < 4; k++)
            {
                var corner = box.Center + box.U * ((k & 1) == 0 ? box.HalfU - 3f : -box.HalfU + 3f) + box.V * ((k & 2) == 0 ? box.HalfV - 3f : -box.HalfV + 3f);
                Column(mb, corner, g, g + 34f, 0.55f, MeshBuffer.Rgb(0xA9AEB4));
                var towardCenter = (box.Center - corner).Normalized;
                Block(mb, corner + towardCenter * 0.8f, new Vec2(-towardCenter.Y, towardCenter.X), towardCenter, 2.4f, 0.3f, g + 33f, g + 36f,
                    MeshBuffer.Rgb(0xFFF6D8));
            }
            return true;
        }

        // ------------------------------------------------------------------ primitive

        private static BuildingInfo Copy(BuildingInfo b, uint wall)
        {
            return new BuildingInfo
            {
                OsmId = b.OsmId, OsmType = b.OsmType, Outer = b.Outer, Holes = b.Holes, Height = b.Height,
                MinHeight = b.MinHeight, RoofHeight = b.RoofHeight, Levels = b.Levels, Roof = b.Roof, Use = b.Use,
                Material = b.Material, WallColor = wall, RoofColor = b.RoofColor, Flags = b.Flags, Year = b.Year,
                Name = b.Name, Address = b.Address, Landmark = b.Landmark, Bounds = b.Bounds,
            };
        }

        private static float[] Square(Vec2 c, Vec2 u, Vec2 v, float h)
        {
            var p0 = c - u * h - v * h;
            var p1 = c + u * h - v * h;
            var p2 = c + u * h + v * h;
            var p3 = c - u * h + v * h;
            var ring = new[] { p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y };
            if (Polygon2D.SignedArea(ring) < 0)
                ring = new[] { p0.X, p0.Y, p3.X, p3.Y, p2.X, p2.Y, p1.X, p1.Y };
            return ring;
        }

        private static bool AllInside(float[] ring, BuildingInfo b)
        {
            for (int i = 0; i + 1 < ring.Length; i += 2)
                if (!Polygon2D.Contains(b.Outer, b.Holes, ring[i], ring[i + 1])) return false;
            return true;
        }

        /// <summary>Parallelepipedo orientato (lati 2·hu lungo u, 2·hv lungo v) tra z0 e z1. Muri senza finestre.</summary>
        private static void Block(MeshBuffer mb, Vec2 c, Vec2 u, Vec2 v, float hu, float hv, float z0, float z1, uint color)
        {
            var p = new[] { c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv };
            if (Polygon2D.SignedArea(new List<Vec2>(p)) < 0) Array.Reverse(p);
            for (int i = 0; i < 4; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % 4];
                var d = (b - a).Normalized;
                var n = new Vec2(d.Y, -d.X);
                float len = (b - a).Length;
                int i0 = mb.Add(a.X, a.Y, z0, n.X, n.Y, 0, color, 0, 0, len, z1 - z0, 0, 0.5f, 1, 12);
                int i1 = mb.Add(b.X, b.Y, z0, n.X, n.Y, 0, color, len, 0, len, z1 - z0, 0, 0.5f, 1, 12);
                int i2 = mb.Add(b.X, b.Y, z1, n.X, n.Y, 0, color, len, z1 - z0, len, z1 - z0, 0, 0.5f, 1, 12);
                int i3 = mb.Add(a.X, a.Y, z1, n.X, n.Y, 0, color, 0, z1 - z0, len, z1 - z0, 0, 0.5f, 1, 12);
                mb.Quad(i0, i1, i2, i3);
            }
            int t0 = mb.Add(p[0].X, p[0].Y, z1, 0, 0, 1, color, 0, 0, 0, 0, 2, 0.5f, 1, 0);
            int t1 = mb.Add(p[1].X, p[1].Y, z1, 0, 0, 1, color, 0, 0, 0, 0, 2, 0.5f, 1, 0);
            int t2 = mb.Add(p[2].X, p[2].Y, z1, 0, 0, 1, color, 0, 0, 0, 0, 2, 0.5f, 1, 0);
            int t3 = mb.Add(p[3].X, p[3].Y, z1, 0, 0, 1, color, 0, 0, 0, 0, 2, 0.5f, 1, 0);
            mb.Quad(t0, t1, t2, t3);
        }

        private static void Column(MeshBuffer mb, Vec2 c, float z0, float z1, float r, uint color)
        {
            const int sides = 8;
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                float nx = (float)Math.Cos(am), ny = (float)Math.Sin(am);
                var p0 = c + new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0)) * r;
                var p1 = c + new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1)) * r;
                int i0 = mb.Add(p0.X, p0.Y, z0, nx, ny, 0, color, 0, 0, 1, z1 - z0, 0, 0.5f, 1, 12);
                int i1 = mb.Add(p1.X, p1.Y, z0, nx, ny, 0, color, 0, 0, 1, z1 - z0, 0, 0.5f, 1, 12);
                int i2 = mb.Add(p1.X, p1.Y, z1, nx, ny, 0, color, 0, z1 - z0, 1, z1 - z0, 0, 0.5f, 1, 12);
                int i3 = mb.Add(p0.X, p0.Y, z1, nx, ny, 0, color, 0, z1 - z0, 1, z1 - z0, 0, 0.5f, 1, 12);
                mb.Quad(i0, i1, i2, i3);
            }
        }

        /// <summary>Rettangolo orizzontale (tipo superficie: uv1.x).</summary>
        private static void Quad(MeshBuffer mb, Vec2 c, Vec2 u, Vec2 v, float hu, float hv, float z, uint color, float kind)
        {
            var p = new[] { c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv };
            if (Polygon2D.SignedArea(new List<Vec2>(p)) < 0) Array.Reverse(p);
            var idx = new int[4];
            for (int i = 0; i < 4; i++)
            {
                float lu = Vec2.Dot(p[i] - c, u) + hu, lv = Vec2.Dot(p[i] - c, v) + hv;
                idx[i] = mb.Add(p[i].X, p[i].Y, z, 0, 0, 1, color, lu, lv, hu * 2, hv * 2, kind, 0.5f, 1, 0);
            }
            mb.Quad(idx[0], idx[1], idx[2], idx[3]);
        }
    }
}
