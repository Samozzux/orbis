using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Orbis.Core.World.Compression;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    public class InflaterTests
    {
        private static byte[] Deflate(byte[] data, CompressionLevel level)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, level, true)) ds.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        [Theory]
        [InlineData(0, CompressionLevel.Optimal)]
        [InlineData(1, CompressionLevel.Optimal)]
        [InlineData(100, CompressionLevel.Fastest)]
        [InlineData(70000, CompressionLevel.NoCompression)]
        [InlineData(300000, CompressionLevel.Optimal)]
        [InlineData(300000, CompressionLevel.SmallestSize)]
        public void Decomprime_ComeDotNet(int size, CompressionLevel level)
        {
            var rnd = new Random(size);
            var data = new byte[size];
            // metà casuale e metà ripetitiva: usa blocchi dinamici, distanze lunghe e sovrapposte
            for (int i = 0; i < size; i++) data[i] = i < size / 2 ? (byte)rnd.Next(256) : (byte)("Chieti "[i % 7]);
            var packed = Deflate(data, level);
            Assert.Equal(data, Inflater.Decompress(packed, 0, packed.Length, size));
            Assert.Equal(data, Inflater.Decompress(packed)); // anche senza conoscere la dimensione
        }

        [Fact]
        public void DatiTroncatiOCorrotti_Eccezione()
        {
            var data = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7 % 13)).ToArray();
            var packed = Deflate(data, CompressionLevel.Optimal);
            Assert.Throws<CorruptDataException>(() => Inflater.Decompress(packed, 0, packed.Length / 2));
            Assert.Throws<CorruptDataException>(() => Inflater.Decompress(new byte[] { 0xFF, 0xFF, 0xFF }));
        }

        [Fact]
        public void Crc32_ComeZlib()
        {
            Assert.Equal(0xCBF43926u, Crc32.Compute(System.Text.Encoding.ASCII.GetBytes("123456789")));
        }
    }

    public class WebMercatorTests
    {
        [Fact]
        public void AndataERitorno()
        {
            WebMercator.ToMeters(14.166459, 42.3524445, out var x, out var y);
            WebMercator.ToLonLat(x, y, out var lon, out var lat);
            Assert.Equal(14.166459, lon, 9);
            Assert.Equal(42.3524445, lat, 9);
            Assert.Equal(2445.98490512564, WebMercator.TileSize(14), 6);
        }

        [Fact]
        public void TileDiChieti()
        {
            WebMercator.ToMeters(14.166459, 42.3524445, out var x, out var y);
            var t = WebMercator.TileAt(14, x, y);
            WebMercator.TileOrigin(14, t.X, t.Y, out var x0, out var y0);
            Assert.InRange(x - x0, 0, WebMercator.TileSize(14));
            Assert.InRange(y - y0, 0, WebMercator.TileSize(14));
            Assert.Equal("14/" + t.X + "/" + t.Y + ".bin", t.Path);
        }

        [Fact]
        public void WorldFrame_OrigineInZeroEScalaVera()
        {
            var f = new WorldFrame(14.166459, 42.3524445);
            f.FromLonLat(14.166459, 42.3524445, out var x0, out var y0);
            Assert.Equal(0, x0, 6);
            Assert.Equal(0, y0, 6);
            // 0,01° di latitudine ≈ 1112 m veri
            f.FromLonLat(14.166459, 42.3624445, out _, out var y1);
            Assert.InRange(y1, 1105, 1120);
            f.ToLonLat(500, -300, out var lon, out var lat);
            f.FromLonLat(lon, lat, out var bx, out var by);
            Assert.Equal(500, bx, 6);
            Assert.Equal(-300, by, 6);
        }
    }

    public class TileReaderTests
    {
        private static byte[] Fixture(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

        [Fact]
        public void LeggeIndiceETileScrittiDallaPipeline()
        {
            var region = TileReader.ReadIndexFile(Fixture("index_sample.bin"));
            Assert.Equal("test", region.Key);
            Assert.Equal(3, region.Version);
            Assert.Equal(14, region.Zoom);
            Assert.NotEmpty(region.Tiles);
            Assert.Contains("OpenStreetMap", region.Attribution);
            Assert.NotNull(region.Far);
            Assert.Equal(region.Far.Cols * region.Far.Rows, region.Far.Heights.Length);

            var frame = region.CreateFrame();
            var id = frame.TileAt(14, 0, 0);
            Assert.True(region.Tiles.ContainsKey(id));
            var file = Fixture("tile_sample.bin");
            Assert.Equal(region.Tiles[id].Size, (uint)file.Length);
            Assert.Equal(region.Tiles[id].Crc, Crc32.Compute(file));

            var tile = TileReader.ReadFile(file, region.Scale);
            Assert.Equal(id, tile.Id);
            Assert.Equal(WebMercator.TileSize(14) * region.Scale, tile.Size, 3);

            var apt = tile.Buildings.Single(b => b.Address == "Corso Marrucino 12");
            Assert.Equal(5, apt.Levels);
            Assert.Equal(BuildingUse.Apartments, apt.Use);
            Assert.Equal(0x1E8C48Au, apt.WallColor);
            Assert.Equal(30f, apt.Bounds.Width, 1);   // misure vere in metri
            Assert.Equal(20f, apt.Bounds.Height, 1);
            Assert.True(apt.HasFlag(BuildingFlag.HeightEstimated));

            var cath = tile.Buildings.Single(b => b.Name == "Cattedrale di San Giustino");
            Assert.Equal("cattedrale_san_giustino", cath.Landmark);
            Assert.Equal(2, tile.Buildings.Count(b => b.Name == "Palazzo de' Mayo" && b.HasFlag(BuildingFlag.Part)));

            Assert.Contains(tile.Areas, a => a.Kind == AreaKind.Vineyard);
            Assert.Contains(tile.Areas, a => a.Kind == AreaKind.OliveGrove);
            var road = tile.Roads.Single(r => r.Name == "Viale Amendola");
            Assert.Equal(RoadClass.Primary, road.Class);
            Assert.Equal("SS 5", road.Ref);

            var bar = tile.Pois.Single(p => p.Name == "Bar Centrale");
            Assert.Equal("amenity/bar", bar.Category);
            Assert.Same(apt, tile.Buildings[bar.Building]);
            Assert.Equal(2, tile.TreeCount);
            Assert.Contains(TreeKind.Olive, tile.TreeKinds);

            Assert.NotEmpty(tile.Lots);
            Assert.All(tile.Lots, l => Assert.True(l.Area >= 20f));
            Assert.Contains(tile.Lots, l => l.Kind == LotKind.Urban);

            Assert.Equal(65, tile.Terrain.Size);
            Assert.InRange(tile.Terrain.MinHeight, 150f, 400f);
        }

        [Fact]
        public void TerrenoInterpolatoSuiTriangoliDellaMesh()
        {
            var t = new TerrainInfo
            {
                Size = 2, TileSize = 10f,
                Heights = new[] { 0f, 10f, 20f, 40f }, // 00, 10 (sud), 01, 11 (nord)
            };
            Assert.Equal(0f, t.HeightAt(0, 0));
            Assert.Equal(10f, t.HeightAt(10, 0));
            Assert.Equal(20f, t.HeightAt(0, 10));
            Assert.Equal(40f, t.HeightAt(10, 10));
            Assert.Equal(20f, t.HeightAt(5, 5));          // sulla diagonale: media di 00 e 11
            Assert.Equal(5f + 15f, t.HeightAt(5, 5f), 4);
            Assert.Equal(10f + 15f, t.HeightAt(10, 5), 4); // bordo est: tra 10 e 40
            Assert.Equal(0f, t.HeightAt(-5, -5));          // fuori dal tile: bordo
        }

        [Fact]
        public void FileNonValido_Eccezione()
        {
            Assert.Throws<CorruptDataException>(() => TileReader.ReadFile(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 1));
        }
    }
}
