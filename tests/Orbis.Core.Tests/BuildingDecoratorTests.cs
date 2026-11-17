using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Addobbi "da gioco" degli edifici reali: tipo dall'uso/POI, più addobbi a ogni livello, sempre vicini all'edificio.</summary>
    public class BuildingDecoratorTests
    {
        private static readonly GroundSampler Flat = (x, y) => 100f;

        /// <summary>Edificio 20×12 m tra (0,0) e (20,12); strada lungo il lato sud a <paramref name="roadGap"/> m dal muro.</summary>
        private static (BuildingInfo b, RoadIndex roads) Building(BuildingUse use, int levels, RoofShape roof, float roadGap, long id = 4242)
        {
            var outer = new float[] { 0, 0, 20, 0, 20, 12, 0, 12 };
            var b = new BuildingInfo
            {
                OsmId = id, OsmType = OsmType.Way, Outer = outer, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(outer),
                Height = levels * 3.2f + (roof == RoofShape.Flat ? 0 : 2.5f), Levels = levels, Roof = roof, Use = use,
            };
            // strada larga 6 m: il bordo è a roadGap dal muro sud (y = 0)
            var road = new RoadInfo { Points = new float[] { -30, -roadGap - 3, 50, -roadGap - 3 }, Class = RoadClass.Residential, Width = 6 };
            return (b, new RoadIndex(new[] { road }));
        }

        private static MeshBuffer Decorate(BuildingInfo b, RoadIndex roads, DressKind kind, int level, Vec2? entrance, out int tris)
        {
            var m = new BuildingMesher();
            Assert.True(m.Build(b, Flat, new MeshBuffer(), kind == DressKind.Shop || kind == DressKind.FoodDrink));
            var mb = new MeshBuffer();
            tris = new BuildingDecorator().Decorate(b, m, kind, level, entrance, roads, Flat, mb, new List<PlantInstance>());
            Assert.Equal(mb.IndexCount / 3, tris);
            return mb;
        }

        [Fact]
        public void TipoDallUsoEDaiPoi()
        {
            var house = new BuildingInfo { Use = BuildingUse.House };
            Assert.Equal(DressKind.Residential, BuildingDecorator.KindOf(house, null));
            Assert.Equal(DressKind.FoodDrink, BuildingDecorator.KindOf(house, "amenity/bar"));
            Assert.Equal(DressKind.Pharmacy, BuildingDecorator.KindOf(house, "amenity/pharmacy"));
            Assert.Equal(DressKind.Shop, BuildingDecorator.KindOf(house, "shop/clothes"));
            Assert.Equal(DressKind.FuelCanopy, BuildingDecorator.KindOf(new BuildingInfo { Use = BuildingUse.Roof }, "amenity/fuel"));
            Assert.Equal(DressKind.Canopy, BuildingDecorator.KindOf(new BuildingInfo { Use = BuildingUse.Roof }, null));
            Assert.Equal(DressKind.Industrial, BuildingDecorator.KindOf(new BuildingInfo { Use = BuildingUse.Warehouse }, null));
            Assert.Equal(DressKind.None, BuildingDecorator.KindOf(new BuildingInfo { Use = BuildingUse.Church }, null));
        }

        [Theory]
        [InlineData(DressKind.Residential)]
        [InlineData(DressKind.FoodDrink)]
        [InlineData(DressKind.Shop)]
        [InlineData(DressKind.Pharmacy)]
        [InlineData(DressKind.Hotel)]
        [InlineData(DressKind.Civic)]
        [InlineData(DressKind.Industrial)]
        public void OgniLivelloAggiungeAddobbi_SempreVicinoAllEdificio(DressKind kind)
        {
            var (b, roads) = Building(BuildingUse.Apartments, 4, RoofShape.Flat, 4.5f);
            int last = -1;
            for (int level = 1; level <= 5; level++)
            {
                var mb = Decorate(b, roads, kind, level, new Vec2(10, -0.5f), out var tris);
                Assert.True(tris > 0, $"{kind} livello {level}: nessun addobbo");
                Assert.True(tris >= last, $"{kind}: il livello {level} ha meno addobbi del precedente ({tris} < {last})");
                Assert.True(tris < 8000, $"{kind} livello {level}: troppi triangoli ({tris})");
                last = tris;
                // tutto entro pochi metri dalla sagoma (dehors e tende sul marciapiede davanti, a sud)
                Assert.True(mb.MinX > -3f && mb.MaxX < 23f, $"{kind}: x fuori ({mb.MinX}..{mb.MaxX})");
                Assert.True(mb.MinZ > -4.6f && mb.MaxZ < 15f, $"{kind}: y fuori ({mb.MinZ}..{mb.MaxZ})");
                Assert.True(mb.MinY > 99f && mb.MaxY < 100 + b.Height + 4f, $"{kind}: quota fuori ({mb.MinY}..{mb.MaxY})");
            }
        }

        [Fact]
        public void IlLivelloCinqueEPiuRiccoDelPrimo()
        {
            var (b, roads) = Building(BuildingUse.Apartments, 5, RoofShape.Flat, 4.5f);
            Decorate(b, roads, DressKind.FoodDrink, 1, new Vec2(10, -0.5f), out var t1);
            Decorate(b, roads, DressKind.FoodDrink, 5, new Vec2(10, -0.5f), out var t5);
            Assert.True(t5 > t1 * 2, $"livello 5 ({t5}) non abbastanza più ricco del livello 1 ({t1})");
        }

        [Fact]
        public void DehorsSoloSeIlMarciapiedeEAbbastanzaLargo()
        {
            var (wide, roadsWide) = Building(BuildingUse.Residential, 3, RoofShape.Hipped, 4.5f);
            var mbWide = Decorate(wide, roadsWide, DressKind.FoodDrink, 3, new Vec2(10, -0.5f), out _);
            Assert.True(mbWide.MinZ < -2.2f, "con 4,5 m di marciapiede ci devono essere tavolini davanti al bar");

            var (narrow, roadsNarrow) = Building(BuildingUse.Residential, 3, RoofShape.Hipped, 1.2f);
            var mbNarrow = Decorate(narrow, roadsNarrow, DressKind.FoodDrink, 3, new Vec2(10, -0.5f), out _);
            Assert.True(mbNarrow.MinZ > -1.7f, $"con 1,2 m di marciapiede niente dehors in strada (minimo y {mbNarrow.MinZ})");
        }

        [Fact]
        public void InCollina_TendeSopraIlMarciapiedeAMonte()
        {
            // terreno in salita verso sud: la facciata su strada (sud) è 6 m più in alto del retro
            GroundSampler slope = (x, y) => 100f - y * 0.5f;
            var (b, roads) = Building(BuildingUse.Residential, 4, RoofShape.Flat, 4.5f);
            var m = new BuildingMesher();
            Assert.True(m.Build(b, slope, new MeshBuffer(), true));
            var mb = new MeshBuffer();
            new BuildingDecorator().Decorate(b, m, DressKind.FoodDrink, 3, new Vec2(10, -0.5f), roads, slope, mb, null);
            int canvas = 0;
            for (int v = 0; v < mb.VertexCount; v++)
            {
                if (Math.Abs(mb.GetFloat(v, 11) - BuildingDecorator.KindCanvas) > 0.1f) continue;
                if (mb.GetFloat(v, 2) > -0.5f) continue; // solo tende sulla facciata sud (non gli ombrelloni, più lontani)
                canvas++;
                Assert.True(mb.GetFloat(v, 1) > 100.5f + 1.9f, $"tenda sotto il livello della strada: {mb.GetFloat(v, 1)}");
            }
            Assert.True(canvas > 0, "nessuna tenda sulla facciata");
        }

        [Fact]
        public void SempreUgualePerLoStessoEdificio()
        {
            var (b, roads) = Building(BuildingUse.Apartments, 4, RoofShape.Flat, 4f);
            var a = Decorate(b, roads, DressKind.Shop, 3, null, out var ta);
            var c = Decorate(b, roads, DressKind.Shop, 3, null, out var tc);
            Assert.Equal(ta, tc);
            for (int i = 0; i < a.VertexCount * MeshBuffer.Stride; i++) Assert.Equal(a.Data[i], c.Data[i]);
            // un altro edificio identico ma con un altro id ha addobbi diversi (colori, posizioni)
            var (b2, _) = Building(BuildingUse.Apartments, 4, RoofShape.Flat, 4f, 777);
            var d = Decorate(b2, roads, DressKind.Shop, 3, null, out _);
            bool differs = d.VertexCount != a.VertexCount;
            for (int i = 0; !differs && i < a.VertexCount * MeshBuffer.Stride; i++) differs = a.Data[i] != d.Data[i];
            Assert.True(differs);
        }

        [Fact]
        public void PensilinaDelBenzinaioConPompe()
        {
            var outer = new float[] { 0, 0, 16, 0, 16, 10, 0, 10 };
            var b = new BuildingInfo
            {
                OsmId = 9, OsmType = OsmType.Way, Outer = outer, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(outer), Height = 5.5f, Roof = RoofShape.Flat,
                Use = BuildingUse.Roof,
            };
            var m = new BuildingMesher();
            Assert.True(m.Build(b, Flat, new MeshBuffer(), false));
            var fuel = new MeshBuffer();
            var plain = new MeshBuffer();
            int tf = new BuildingDecorator().Decorate(b, m, DressKind.FuelCanopy, 2, null, null, Flat, fuel, null);
            int tp = new BuildingDecorator().Decorate(b, m, DressKind.Canopy, 2, null, null, Flat, plain, null);
            Assert.True(tf > tp, "il benzinaio deve avere anche le pompe e il totem");
            Assert.True(fuel.MaxY <= 100 + 7f + 4f + 0.8f + 0.1f, $"troppo alto ({fuel.MaxY})");
        }
    }
}
