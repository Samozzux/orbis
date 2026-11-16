using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;

namespace Orbis.World
{
    /// <summary>Livello di dettaglio di un tile in base alla distanza dalla telecamera.</summary>
    public enum TileLod
    {
        /// <summary>Vicino: tutto (tetti a falde, marciapiedi, alberi, filari).</summary>
        Full = 0,
        /// <summary>Medio: edifici semplificati, strade principali e secondarie, niente piante.</summary>
        Medium = 1,
        /// <summary>Lontano: terreno con le aree e strade principali.</summary>
        Far = 2,
    }

    /// <summary>Risultato della costruzione di un tile (prodotto fuori dal main thread).</summary>
    public sealed class TileBuildResult
    {
        public const int Chunks = 4; // edifici divisi in 4×4 blocchi per il frustum culling

        public TileData Data;
        public TileLod Lod;
        public MeshBuffer Terrain;
        public byte[] Landuse;
        public int LanduseSize;
        public MeshBuffer Roads;
        public MeshBuffer[] Buildings = new MeshBuffer[Chunks * Chunks];
        public List<PlantInstance> Plants;
        public int BuildingCount;
    }

    /// <summary>Costruzione di un tile a passi (vedi <see cref="WorkRunner"/>): nessuna chiamata a Unity qui dentro.</summary>
    public static class TileBuild
    {
        public static IEnumerator<object> Run(Func<byte[]> file, TileData cached, double scale, TileLod lod, TileBuildResult r)
        {
            var data = cached;
            if (data == null)
            {
                data = TileReader.ReadFile(file(), scale);
                yield return null;
            }
            r.Data = data;
            r.Lod = lod;
            GroundSampler ground = data.Terrain.HeightAt;

            r.Terrain = new MeshBuffer();
            TerrainMesher.Build(data.Terrain, r.Terrain, lod == TileLod.Far ? 2 : 1, lod == TileLod.Full ? 15f : 40f);
            yield return null;

            r.LanduseSize = lod == TileLod.Full ? 512 : (lod == TileLod.Medium ? 256 : 128);
            var owner = LanduseRaster.Rasterize(data.Areas, data.Size, r.LanduseSize);
            yield return null;
            r.Landuse = LanduseRaster.Colorize(data.Areas, owner, r.LanduseSize, data.Id.X * 31 + data.Id.Y);
            yield return null;

            r.Roads = new MeshBuffer();
            var roadMesher = new RoadMesher();
            int n = 0;
            foreach (var road in data.Roads)
            {
                if (!Include(road, lod)) continue;
                roadMesher.Build(road, ground, r.Roads);
                if (++n % 200 == 0) yield return null;
            }
            yield return null;

            if (lod != TileLod.Far)
            {
                var shops = new bool[data.Buildings.Length];
                foreach (var p in data.Pois)
                    if (p.Building >= 0 && p.Building < shops.Length && IsShop(p.Category)) shops[p.Building] = true;
                var mesher = new BuildingMesher();
                var landmarks = new LandmarkMesher();
                // per i landmark fatti di più parti: la parte più alta (es. il campanile)
                var tallest = new Dictionary<string, float>();
                foreach (var b in data.Buildings)
                    if (!string.IsNullOrEmpty(b.Landmark) && (!tallest.TryGetValue(b.Landmark, out var h) || b.Height > h))
                        tallest[b.Landmark] = b.Height;
                float chunk = data.Size / TileBuildResult.Chunks;
                MeshBuffer ChunkAt(float x, float y)
                {
                    int ix = Clamp((int)(x / chunk)), iy = Clamp((int)(y / chunk));
                    return r.Buildings[iy * TileBuildResult.Chunks + ix] ?? (r.Buildings[iy * TileBuildResult.Chunks + ix] = new MeshBuffer());
                }
                for (int i = 0; i < data.Buildings.Length; i++)
                {
                    var b = data.Buildings[i];
                    if (b.HasFlag(BuildingFlag.Underground)) continue;
                    var mb = ChunkAt((b.Bounds.MinX + b.Bounds.MaxX) / 2, (b.Bounds.MinY + b.Bounds.MaxY) / 2);
                    bool done = !string.IsNullOrEmpty(b.Landmark) && lod == TileLod.Full &&
                                landmarks.BuildBuilding(b, ground, mb, tallest.TryGetValue(b.Landmark, out var th) && b.Height >= th);
                    if (done || mesher.Build(b, ground, mb, shops[i], lod != TileLod.Full)) r.BuildingCount++;
                    if (i % 96 == 95) yield return null;
                }
                // stadi (aree con landmark): campo, tribune, copertura, torri faro
                foreach (var a in data.Areas)
                {
                    if (a.Kind != AreaKind.Stadium || LandmarkMesher.GeneratorFor(a.Landmark) != "stadium") continue;
                    AreaInfo pitch = null;
                    foreach (var p in data.Areas)
                        if (p.Kind == AreaKind.Pitch && a.Bounds.Contains((p.Bounds.MinX + p.Bounds.MaxX) / 2, (p.Bounds.MinY + p.Bounds.MaxY) / 2))
                            pitch = p;
                    bool mappedStands = false;
                    foreach (var b in data.Buildings)
                        if (a.Bounds.Contains((b.Bounds.MinX + b.Bounds.MaxX) / 2, (b.Bounds.MinY + b.Bounds.MaxY) / 2) &&
                            Orbis.Core.World.Geometry.Polygon2D.Contains(a.Outer, a.Holes, (b.Bounds.MinX + b.Bounds.MaxX) / 2, (b.Bounds.MinY + b.Bounds.MaxY) / 2))
                            mappedStands = true;
                    landmarks.Stadium(a, pitch, ground, ChunkAt((a.Bounds.MinX + a.Bounds.MaxX) / 2, (a.Bounds.MinY + a.Bounds.MaxY) / 2), !mappedStands);
                }
            }

            if (lod == TileLod.Full)
            {
                r.Plants = new VegetationScatter().Scatter(data, ground);
                yield return null;
            }
        }

        private static int Clamp(int i) => i < 0 ? 0 : (i >= TileBuildResult.Chunks ? TileBuildResult.Chunks - 1 : i);

        private static bool Include(RoadInfo r, TileLod lod)
        {
            if (lod == TileLod.Full) return true;
            if (lod == TileLod.Medium)
                return r.Class <= RoadClass.Service || r.Class == RoadClass.Pedestrian || r.IsRail || r.IsAeroway;
            return r.Class <= RoadClass.Tertiary || r.Class == RoadClass.Rail || r.IsAeroway;
        }

        private static bool IsShop(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            if (category.StartsWith("shop/", StringComparison.Ordinal)) return true;
            switch (category)
            {
                case "amenity/bar":
                case "amenity/cafe":
                case "amenity/restaurant":
                case "amenity/pharmacy":
                case "amenity/bank":
                case "amenity/fast_food":
                case "amenity/ice_cream":
                case "amenity/pub":
                    return true;
            }
            return false;
        }
    }
}
