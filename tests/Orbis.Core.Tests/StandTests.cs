using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Tribune degli stadi (building=grandstand): gradinate che salgono allontanandosi dal campo, non palazzi.</summary>
    public class StandTests
    {
        private static readonly GroundSampler Flat = (x, y) => 100f;

        private static BuildingInfo Stand(float[] outer, float[][] holes, float height = 12f)
        {
            return new BuildingInfo
            {
                OsmId = 555, OsmType = OsmType.Way, Outer = outer, Holes = holes ?? Array.Empty<float[]>(), Bounds = Bounds2.Of(outer),
                Height = height, Use = BuildingUse.Stadium, Roof = RoofShape.Flat,
            };
        }

        private static List<int> Seats(MeshBuffer mb)
        {
            var list = new List<int>();
            for (int v = 0; v < mb.VertexCount; v++)
                if (Math.Abs(mb.GetFloat(v, 11) - BuildingDecorator.KindSeats) < 0.1f) list.Add(v);
            return list;
        }

        [Fact]
        public void TribunaDritta_BassaVersoIlCampoAltaDietro()
        {
            // tribuna 60×15 m a sud del campo (il campo è a nord, y = 60)
            var b = Stand(new float[] { 0, 0, 60, 0, 60, 15, 0, 15 }, null, 10f);
            var m = new BuildingMesher { FieldCenter = new Vec2(30, 60) };
            var mb = new MeshBuffer();
            Assert.True(m.Build(b, Flat, mb, false));
            var seats = Seats(mb);
            Assert.NotEmpty(seats);
            foreach (int v in seats)
            {
                float y = mb.GetFloat(v, 2), z = mb.GetFloat(v, 1);
                if (Math.Abs(y - 15) < 0.01f) Assert.InRange(z, 100.9f, 101.3f);   // fronte: parapetto
                if (Math.Abs(y) < 0.01f) Assert.InRange(z, 109.9f, 110.1f);         // retro: in cima
                Assert.True(mb.GetFloat(v, 4) > 0.3f, "la gradinata deve guardare in alto");
                Assert.True(mb.GetFloat(v, 5) > 0.1f, "la gradinata deve guardare verso il campo (nord)");
                Assert.Equal(BuildingMesher.SeatRowHeight, mb.GetFloat(v, 13), 3);
            }
            // nessun muro con le finestre
            for (int v = 0; v < mb.VertexCount; v++)
                if (mb.GetFloat(v, 11) < 0.5f) Assert.Equal(12f, mb.GetFloat(v, 14));
        }

        [Fact]
        public void TribunaAdAnello_DentroBassaFuoriAlta()
        {
            // anello attorno al campo (raggio interno 50 m, esterno 70 m), come le curve di uno stadio con la pista
            const int n = 32;
            var outer = new float[n * 2];
            var hole = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                outer[2 * i] = (float)Math.Cos(a) * 70;
                outer[2 * i + 1] = (float)Math.Sin(a) * 70;
                // buco in senso orario
                double ah = -2 * Math.PI * i / n;
                hole[2 * i] = (float)Math.Cos(ah) * 50;
                hole[2 * i + 1] = (float)Math.Sin(ah) * 50;
            }
            var b = Stand(outer, new[] { hole }, 14);
            var m = new BuildingMesher { FieldCenter = new Vec2(0, 0) };
            var mb = new MeshBuffer();
            Assert.True(m.Build(b, Flat, mb, false));
            var seats = Seats(mb);
            Assert.NotEmpty(seats);
            foreach (int v in seats)
            {
                float x = mb.GetFloat(v, 0), y = mb.GetFloat(v, 2), z = mb.GetFloat(v, 1);
                float r = (float)Math.Sqrt(x * x + y * y);
                if (Math.Abs(r - 50) < 0.5f) Assert.InRange(z, 100.9f, 101.3f);
                if (Math.Abs(r - 70) < 0.5f) Assert.InRange(z, 113.9f, 114.1f);
            }
        }

        [Fact]
        public void TribunaStrettaEAlta_PendenzaDaGradinataVera()
        {
            // 4 m di profondità e 15 m di altezza: niente "pinna", la gradinata resta sotto i ~35°
            var b = Stand(new float[] { 0, 0, 60, 0, 60, 4, 0, 4 }, null, 15f);
            var m = new BuildingMesher { FieldCenter = new Vec2(30, 60) };
            var mb = new MeshBuffer();
            Assert.True(m.Build(b, Flat, mb, false));
            foreach (int v in Seats(mb))
                Assert.True(mb.GetFloat(v, 1) <= 101.1f + 4 * 0.7f + 0.01f, $"gradinata troppo ripida: {mb.GetFloat(v, 1)}");
        }

        [Fact]
        public void EdificioDiPuntaVersoIlCampo_NonDiventaUnaTribuna()
        {
            // 8×40 m con il lato corto verso il campo: la "gradinata" salirebbe per 40 m in lunghezza
            var b = Stand(new float[] { 26, 0, 34, 0, 34, 40, 26, 40 }, null, 13f);
            var m = new BuildingMesher { FieldCenter = new Vec2(30, 90) };
            var mb = new MeshBuffer();
            Assert.True(m.Build(b, Flat, mb, false));
            Assert.Empty(Seats(mb));
        }

        [Fact]
        public void SenzaCampoVicino_ResTaUnEdificio()
        {
            var b = Stand(new float[] { 0, 0, 60, 0, 60, 15, 0, 15 }, null);
            var mb = new MeshBuffer();
            Assert.True(new BuildingMesher().Build(b, Flat, mb, false));
            Assert.Empty(Seats(mb));
        }

        [Fact]
        public void LeTribuneNonHannoAddobbiDaNegozio()
        {
            var b = Stand(new float[] { 0, 0, 60, 0, 60, 15, 0, 15 }, null);
            Assert.Equal(DressKind.None, BuildingDecorator.KindOf(b, "amenity/bar"));
        }

        [Theory]
        [InlineData(178f, 118f)]
        [InlineData(118f, 178f)]
        public void PistaDAtletica_AnelloConLeCorsieDentroLArea(float sx, float sy)
        {
            var track = new AreaInfo { Kind = AreaKind.Track, Outer = new float[] { 0, 0, sx, 0, sx, sy, 0, sy }, Holes = Array.Empty<float[]>() };
            var mb = new MeshBuffer();
            LandmarkMesher.AthleticsTrack(track, Flat, mb);
            Assert.True(mb.IndexCount > 0);
            int tartan = 0;
            for (int v = 0; v < mb.VertexCount; v++)
            {
                float x = mb.GetFloat(v, 0), y = mb.GetFloat(v, 2);
                Assert.InRange(x, -0.5f, sx + 0.5f);
                Assert.InRange(y, -0.5f, sy + 0.5f);
                Assert.True(mb.GetFloat(v, 4) > 0.99f, "pista e prato guardano in alto");
                if (Math.Abs(mb.GetFloat(v, 11) - BuildingDecorator.KindTrack) < 0.1f) tartan++;
            }
            Assert.True(tartan >= 60, "anello della pista");
            // triangoli rivolti verso l'alto (antiorari visti dall'alto)
            for (int t = 0; t < mb.IndexCount; t += 3)
            {
                // il buffer salva i triangoli già girati per Unity (a, c, b)
                int a = mb.Indices[t], c = mb.Indices[t + 1], b = mb.Indices[t + 2];
                float ax = mb.GetFloat(a, 0), ay = mb.GetFloat(a, 2);
                float cross = (mb.GetFloat(b, 0) - ax) * (mb.GetFloat(c, 2) - ay) - (mb.GetFloat(b, 2) - ay) * (mb.GetFloat(c, 0) - ax);
                Assert.True(cross > -1e-3f, "triangolo girato verso il basso");
            }
        }
    }
}