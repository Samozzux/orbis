using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Lots;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    public class TerrainAndRoadMesherTests
    {
        [Fact]
        public void Terreno_VerticiSullaSuperficieEGonne()
        {
            var t = new TerrainInfo { Size = 5, TileSize = 100f, Heights = Enumerable.Range(0, 25).Select(i => (float)i).ToArray() };
            var mb = new MeshBuffer();
            TerrainMesher.Build(t, mb, 1, 10f);
            // 25 vertici + 4 gonne da 5×2
            Assert.Equal(25 + 40, mb.VertexCount);
            Assert.Equal((16 * 2 + 4 * 4 * 2) * 3, mb.IndexCount);
            for (int i = 0; i < 25; i++)
                Assert.Equal(t.HeightAt(mb.GetFloat(i, 0), mb.GetFloat(i, 2)), mb.GetFloat(i, 1), 4);
            // normale verso l'alto
            Assert.True(mb.GetFloat(12, 4) > 0.5f);
            // LOD: un campione su due
            mb.Clear();
            TerrainMesher.Build(t, mb, 2, 0);
            Assert.Equal(9, mb.VertexCount);
        }

        [Fact]
        public void Strada_NastroLargoQuantoLaStradaESulTerreno()
        {
            var road = new RoadInfo { Points = new float[] { 0, 0, 30, 0 }, Class = RoadClass.Residential, Width = 6, Name = "Via Pollione" };
            var mb = new MeshBuffer();
            new RoadMesher().Build(road, (x, y) => 50f, mb);
            Assert.True(mb.VertexCount > 0);
            float minN = float.MaxValue, maxN = float.MinValue;
            for (int i = 0; i < mb.VertexCount; i++)
            {
                Assert.InRange(mb.GetFloat(i, 1), 50f, 50.3f);
                if (mb.GetFloat(i, 11) == (float)RoadMesher.Kind.Road)
                {
                    minN = Math.Min(minN, mb.GetFloat(i, 2));
                    maxN = Math.Max(maxN, mb.GetFloat(i, 2));
                }
            }
            Assert.Equal(6f, maxN - minN, 2);
            // via urbana con nome: anche il marciapiede
            Assert.Contains(Enumerable.Range(0, mb.VertexCount), i => mb.GetFloat(i, 11) == (float)RoadMesher.Kind.Sidewalk);
        }

        [Fact]
        public void Galleria_NonSiDisegna_Ponte_SopraIlTerreno()
        {
            var mb = new MeshBuffer();
            var rm = new RoadMesher();
            rm.Build(new RoadInfo { Points = new float[] { 0, 0, 30, 0 }, Class = RoadClass.Primary, Width = 8, Flags = RoadFlag.Tunnel }, (x, y) => 0, mb);
            Assert.Equal(0, mb.VertexCount);
            rm.Build(new RoadInfo { Points = new float[] { 0, 0, 60, 0 }, Class = RoadClass.Primary, Width = 8, Flags = RoadFlag.Bridge },
                (x, y) => x > 10 && x < 50 ? -30f : 0f, mb);
            for (int i = 0; i < mb.VertexCount; i++) Assert.True(mb.GetFloat(i, 1) >= 0.4f);
        }
    }

    public class LanduseAndVegetationTests
    {
        private static AreaInfo Area(AreaKind k, long id, params float[] outer) =>
            new AreaInfo { Kind = k, OsmId = id, Outer = outer, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(outer) };

        [Fact]
        public void Raster_PrioritaEBuchi()
        {
            var areas = new List<AreaInfo>
            {
                Area(AreaKind.Vineyard, 2, 20, 20, 40, 20, 40, 40, 20, 40),
                Area(AreaKind.Farmland, 1, 0, 0, 100, 0, 100, 100, 0, 100),
            };
            var owner = LanduseRaster.Rasterize(areas, 100f, 100);
            Assert.Equal(2, owner[10 * 100 + 10]); // campo (indice 1 + 1)
            Assert.Equal(1, owner[30 * 100 + 30]); // vigneto sopra il campo
            var rgba = LanduseRaster.Colorize(areas, owner, 100, 1);
            Assert.Equal(255, rgba[(30 * 100 + 30) * 4 + 3]);
            // stesso campo → stesso colore di base
            Assert.Equal(LanduseRaster.ColorOf(AreaKind.Farmland, 1), LanduseRaster.ColorOf(AreaKind.Farmland, 1));
        }

        [Fact]
        public void Uliveto_ASestoRegolareEFuoriDalleStrade()
        {
            var tile = new TileData
            {
                Areas = new[] { Area(AreaKind.OliveGrove, 5, 0, 0, 70, 0, 70, 35, 0, 35) },
                Roads = new[] { new RoadInfo { Points = new float[] { 0, 17.5f, 70, 17.5f }, Width = 4, Class = RoadClass.Track } },
            };
            var plants = new VegetationScatter().Scatter(tile, (x, y) => 10f);
            Assert.NotEmpty(plants);
            Assert.All(plants, p => Assert.Equal(PlantKind.Olive, p.Kind));
            Assert.All(plants, p => Assert.True(Math.Abs(p.Y - 17.5f) > 3.4f, "pianta sulla strada"));
            Assert.All(plants, p => Assert.Equal(10f, p.Z));
            // 10 per fila × 4-5 file meno quelle sulla strada
            Assert.InRange(plants.Count, 30, 50);
        }

        [Fact]
        public void Vigneto_FilariLunghiQuantoIlCampo()
        {
            var tile = new TileData { Areas = new[] { Area(AreaKind.Vineyard, 9, 0, 0, 100, 0, 100, 26, 0, 26) } };
            var plants = new VegetationScatter().Scatter(tile, (x, y) => 0);
            Assert.All(plants, p => Assert.Equal(PlantKind.VineRow, p.Kind));
            float total = plants.Sum(p => p.Length);
            Assert.InRange(total, 9 * 95f, 10 * 101f); // ~10 filari da 100 m
        }
    }

    public class LotGridTests
    {
        private static LotInfo Lot(float w, float h, float angleDeg = 0)
        {
            var outer = new float[] { 0, 0, w, 0, w, h, 0, h };
            return new LotInfo { Id = 1, Outer = outer, Holes = Array.Empty<float[]>(), GridAngle = (float)(angleDeg * Math.PI / 180), Bounds = Bounds2.Of(outer) };
        }

        [Fact]
        public void LottoRettangolare_CelleEsatte()
        {
            var g = new LotGrid(Lot(20, 10));
            Assert.Equal(200, g.InsideCount);
            Assert.Equal(PlacementResult.Ok, g.CanPlace(0, 0, 20, 10));
            Assert.Equal(PlacementResult.OutsideLot, g.CanPlace(15, 0, 6, 4));
            Assert.Equal(PlacementResult.Ok, g.CanPlace(5, 5, 4, 6, 1));          // 4×6 ruotato = 6×4: entra
            Assert.Equal(PlacementResult.OutsideLot, g.CanPlace(5, 5, 6, 4, 1));  // 6×4 ruotato = 4×6: sporge in alto
            Assert.Equal(PlacementResult.Ok, g.CanPlace(5, 0, 10, 4, 1));         // 10×4 ruotato = 4×10: alto quanto il lotto
        }

        [Fact]
        public void EdificioReale_OccupaSoloLeSueCelle()
        {
            var g = new LotGrid(Lot(20, 10));
            int n = g.Occupy(new float[] { 2, 2, 6, 2, 6, 5, 2, 5 }, 7);
            Assert.Equal(12, n); // 4×3 celle esatte
            Assert.Equal(7, g.Get(2, 2));
            Assert.Equal(LotGrid.Free, g.Get(6, 2));
            Assert.Equal(PlacementResult.Occupied, g.CanPlace(5, 4, 3, 3));
            Assert.Equal(PlacementResult.Ok, g.CanPlace(6, 2, 3, 3));
            g.Place(6, 2, 3, 3, 0, 8);
            Assert.Equal(PlacementResult.Occupied, g.CanPlace(8, 4, 2, 2));
            Assert.Equal(9, g.Release(8));
            Assert.Equal(12, g.Release(7));
            Assert.Equal(0, g.OccupiedCount);
        }

        [Fact]
        public void GrigliaRuotata_SegueIlLatoSuStrada()
        {
            // lotto ruotato di 30°: la griglia allineata deve contenere 200 celle intere
            double a = Math.PI / 6;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            var outer = new float[] { 0, 0, 20 * c, 20 * s, 20 * c - 10 * s, 20 * s + 10 * c, -10 * s, 10 * c };
            var lot = new LotInfo { Outer = outer, Holes = Array.Empty<float[]>(), GridAngle = (float)a, Bounds = Bounds2.Of(outer) };
            var g = new LotGrid(lot);
            Assert.Equal(200, g.InsideCount);
            var p = g.CellCenter(0, 0);
            Assert.Equal(0.5f * c - 0.5f * s, p.X, 4);
        }
    }
}
