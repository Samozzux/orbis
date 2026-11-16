using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    public class TriangulatorTests
    {
        private static float TriArea(List<Vec2> v, List<int> t)
        {
            float a = 0;
            for (int i = 0; i < t.Count; i += 3)
                a += Vec2.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]) / 2;
            return a;
        }

        [Fact]
        public void PoligonoConcavo_AreaEsattaETriangoliAntiorari()
        {
            // a forma di L
            var l = Polygon2D.Clean(new float[] { 0, 0, 20, 0, 20, 8, 8, 8, 8, 20, 0, 20 }, true);
            var v = new List<Vec2>();
            var t = new List<int>();
            Assert.True(Triangulator.Triangulate(l, null, v, t));
            Assert.Equal(4, t.Count / 3);
            Assert.Equal(20 * 8 + 8 * 12, TriArea(v, t), 2);
            for (int i = 0; i < t.Count; i += 3)
                Assert.True(Vec2.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]) > 0);
        }

        [Fact]
        public void PoligonoConCortile()
        {
            var outer = Polygon2D.Clean(new float[] { 0, 0, 30, 0, 30, 30, 0, 30 }, true);
            var hole = Polygon2D.Clean(new float[] { 10, 10, 20, 10, 20, 20, 10, 20 }, false);
            var hole2 = Polygon2D.Clean(new float[] { 22, 22, 26, 22, 26, 26, 22, 26 }, false);
            var v = new List<Vec2>();
            var t = new List<int>();
            Assert.True(Triangulator.Triangulate(outer, new List<List<Vec2>> { hole, hole2 }, v, t));
            Assert.Equal(900 - 100 - 16, TriArea(v, t), 1);
        }
    }

    public class BuildingMesherTests
    {
        private static BuildingInfo Rect(float w, float d, float height, RoofShape roof, long id = 42)
        {
            var outer = new float[] { 0, 0, w, 0, w, d, 0, d };
            return new BuildingInfo
            {
                OsmId = id, Outer = outer, Holes = Array.Empty<float[]>(), Height = height, Roof = roof,
                Levels = 0, Use = BuildingUse.House, Bounds = Bounds2.Of(outer),
            };
        }

        private static IEnumerable<(float e, float h, float n)> Positions(MeshBuffer mb)
        {
            for (int i = 0; i < mb.VertexCount; i++) yield return (mb.GetFloat(i, 0), mb.GetFloat(i, 1), mb.GetFloat(i, 2));
        }

        private static float RoofProjectedArea(MeshBuffer mb)
        {
            float a = 0;
            for (int i = 0; i < mb.IndexCount; i += 3)
            {
                int i0 = mb.Indices[i], i1 = mb.Indices[i + 1], i2 = mb.Indices[i + 2];
                if (mb.GetFloat(i0, 11) == 0) continue; // muri
                var p0 = new Vec2(mb.GetFloat(i0, 0), mb.GetFloat(i0, 2));
                var p1 = new Vec2(mb.GetFloat(i1, 0), mb.GetFloat(i1, 2));
                var p2 = new Vec2(mb.GetFloat(i2, 0), mb.GetFloat(i2, 2));
                // indici in ordine Unity (orario): l'area con segno risulta negativa
                a -= Vec2.Cross(p1 - p0, p2 - p0) / 2;
            }
            return a;
        }

        [Theory]
        [InlineData(RoofShape.Flat)]
        [InlineData(RoofShape.Hipped)]
        [InlineData(RoofShape.Gabled)]
        [InlineData(RoofShape.Pyramidal)]
        [InlineData(RoofShape.Skillion)]
        public void TettoDentroLaSagomaEAltezzaGiusta(RoofShape shape)
        {
            var mesher = new BuildingMesher();
            var mb = new MeshBuffer();
            Assert.True(mesher.Build(Rect(20, 10, 9, shape), (x, y) => 100f, mb, false));
            float maxH = float.MinValue;
            foreach (var (e, h, n) in Positions(mb))
            {
                Assert.InRange(e, -0.001f, 20.001f); // sagoma esatta
                Assert.InRange(n, -0.001f, 10.001f);
                maxH = Math.Max(maxH, h);
            }
            Assert.Equal(109f, maxH, 3); // cima del tetto = terreno + altezza OSM
            Assert.Equal(200f, RoofProjectedArea(mb), 1); // il tetto copre tutta la sagoma, una volta sola
            Assert.Equal(shape == RoofShape.Flat ? 0f : 1.8f, mesher.LastRoofHeight, 3);
        }

        [Fact]
        public void TerrenoInPendenza_BaseAlPuntoPiuBasso()
        {
            var mesher = new BuildingMesher();
            var mb = new MeshBuffer();
            mesher.Build(Rect(10, 10, 7, RoofShape.Flat), (x, y) => 200f + x, mb, false); // 10 m di dislivello
            Assert.Equal(200f - 0.6f, mesher.LastBase, 3);
            Assert.Equal(205f + 7f, mesher.LastEave, 3);
        }

        [Fact]
        public void SagomaConvessaIrregolare_PadiglioneEsatto()
        {
            var outer = new float[] { 0, 0, 16, 0, 20, 6, 12, 14, 0, 10 };
            var b = new BuildingInfo
            {
                OsmId = 1, Outer = outer, Holes = Array.Empty<float[]>(), Height = 10, Roof = RoofShape.Hipped,
                Bounds = Bounds2.Of(outer),
            };
            var mesher = new BuildingMesher();
            var mb = new MeshBuffer();
            Assert.True(mesher.Build(b, (x, y) => 0, mb, false));
            Assert.Equal(RoofShape.Hipped, mesher.LastRoof);
            float area = Math.Abs(Polygon2D.SignedArea(Polygon2D.ToPoints(outer)));
            Assert.Equal(area, RoofProjectedArea(mb), 1);
            foreach (var (e, h, n) in Positions(mb)) Assert.True(h <= 10.001f);
        }

        [Fact]
        public void CortileOSagomaAdL_TettoPiano()
        {
            var b = Rect(30, 30, 12, RoofShape.Hipped);
            b.Holes = new[] { new float[] { 10, 10, 10, 20, 20, 20, 20, 10 } };
            var mesher = new BuildingMesher();
            var mb = new MeshBuffer();
            Assert.True(mesher.Build(b, (x, y) => 0, mb, true));
            Assert.Equal(RoofShape.Flat, mesher.LastRoof);
            Assert.Equal(800f, RoofProjectedArea(mb), 1);

            var l = new BuildingInfo
            {
                OsmId = 3, Outer = new float[] { 0, 0, 30, 0, 30, 6, 6, 6, 6, 30, 0, 30 },
                Holes = Array.Empty<float[]>(), Height = 9, Roof = RoofShape.Gabled,
            };
            mb.Clear();
            Assert.True(mesher.Build(l, (x, y) => 0, mb, false));
            Assert.Equal(RoofShape.Flat, mesher.LastRoof);
        }

        [Fact]
        public void StessoEdificio_StessiColori()
        {
            var a = StylePalette.WallColor(Rect(5, 5, 5, RoofShape.Flat, 777));
            var b = StylePalette.WallColor(Rect(9, 9, 9, RoofShape.Flat, 777));
            Assert.Equal(a, b);
            var tagged = Rect(5, 5, 5, RoofShape.Flat);
            tagged.WallColor = 0x1ABCDEF;
            Assert.Equal(0xABCDEFu, StylePalette.WallColor(tagged));
        }
    }
}
