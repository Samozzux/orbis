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
        /// <summary>Mappa dei materiali del terreno (stessa risoluzione di <see cref="Landuse"/>).</summary>
        public byte[] Detail;
        public int LanduseSize;
        public MeshBuffer Roads;
        public MeshBuffer[] Buildings = new MeshBuffer[Chunks * Chunks];
        /// <summary>Addobbi degli edifici (solo LOD pieno), separati per spegnerli da lontano.</summary>
        public MeshBuffer[] Decorations = new MeshBuffer[Chunks * Chunks];
        public int DecorationTriangles;
        /// <summary>Insegne con il nome vero (coordinate del tile), scritte dal motore solo da vicino.</summary>
        public List<SignInfo> Signs;
        public List<PlantInstance> Plants;
        public int BuildingCount;
    }

    /// <summary>Costruzione di un tile a passi (vedi <see cref="WorkRunner"/>): nessuna chiamata a Unity qui dentro.</summary>
    public static class TileBuild
    {
        /// <param name="hidden">edifici OSM da non disegnare (demoliti dal giocatore): chiavi GameState.OsmKey, copia per il thread</param>
        /// <param name="levels">livello degli edifici OSM potenziati (assente = 1), copia per il thread</param>
        /// <param name="decorDensity">densità degli addobbi (0 = nessuno)</param>
        public static IEnumerator<object> Run(Func<byte[]> file, TileData cached, double scale, TileLod lod, TileBuildResult r,
            HashSet<long> hidden = null, Dictionary<long, int> levels = null, float decorDensity = 1f, int landuseFull = 1024)
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

            // mappa delle colture: ~1,8 m per pixel nei tile vicini (prima 3,5 m: bordi a gradini)
            r.LanduseSize = lod == TileLod.Full ? landuseFull : (lod == TileLod.Medium ? 256 : 128);
            var owner = LanduseRaster.Rasterize(data.Areas, data.Size, r.LanduseSize);
            yield return null;
            r.Landuse = LanduseRaster.Colorize(data.Areas, owner, r.LanduseSize, data.Id.X * 31 + data.Id.Y);
            yield return null;
            r.Detail = LanduseRaster.Detail(data.Areas, owner, r.LanduseSize);
            yield return null;

            r.Roads = new MeshBuffer();
            var roadMesher = new RoadMesher();
            // incroci veri (almeno tre strade): lì lo shader disegna le strisce pedonali
            var crossings = lod == TileLod.Far ? null : StreetFurniture.Intersections(data.Roads);
            int n = 0;
            foreach (var road in data.Roads)
            {
                if (!Include(road, lod)) continue;
                bool zs = false, ze = false;
                if (crossings != null && road.Points.Length >= 4)
                {
                    int last = road.Points.Length - 2;
                    zs = crossings.Contains(StreetFurniture.Key(road.Points[0], road.Points[1]));
                    ze = crossings.Contains(StreetFurniture.Key(road.Points[last], road.Points[last + 1]));
                }
                roadMesher.Build(road, ground, r.Roads, zs, ze);
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
                // addobbi "da gioco" (docs/08 §3): solo vicino, con il POI principale di ogni edificio
                bool decorate = lod == TileLod.Full && decorDensity > 0.01f;
                BuildingDecorator decorator = null;
                RoadIndex roads = null;
                string[] poiCat = null, poiName = null;
                Orbis.Core.World.Geometry.Vec2?[] poiPos = null;
                List<PoiInfo> fuel = null;
                var scratch = new MeshBuffer();
                var decoPlants = new List<PlantInstance>();
                if (decorate)
                {
                    r.Signs = new List<SignInfo>();
                    decorator = new BuildingDecorator { Density = decorDensity, Signs = r.Signs };
                    roads = new RoadIndex(data.Roads);
                    poiCat = new string[data.Buildings.Length];
                    poiName = new string[data.Buildings.Length];
                    poiPos = new Orbis.Core.World.Geometry.Vec2?[data.Buildings.Length];
                    fuel = new List<PoiInfo>();
                    foreach (var p in data.Pois)
                    {
                        if (p.Category == "amenity/fuel") fuel.Add(p);
                        if (p.Building < 0 || p.Building >= poiCat.Length) continue;
                        // il primo POI "commerciale" vince (bar, negozi, farmacie…)
                        if (poiCat[p.Building] == null || (!IsShop(poiCat[p.Building]) && IsShop(p.Category)))
                        {
                            poiCat[p.Building] = p.Category;
                            poiName[p.Building] = p.Name;
                            poiPos[p.Building] = new Orbis.Core.World.Geometry.Vec2(p.X, p.Y);
                        }
                    }
                    yield return null;
                }
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
                    if (hidden != null && hidden.Contains(((long)b.OsmType << 60) | b.OsmId)) continue;
                    float cx = (b.Bounds.MinX + b.Bounds.MaxX) / 2, cy = (b.Bounds.MinY + b.Bounds.MaxY) / 2;
                    var mb = ChunkAt(cx, cy);
                    bool done = !string.IsNullOrEmpty(b.Landmark) && lod == TileLod.Full &&
                                landmarks.BuildBuilding(b, ground, mb, tallest.TryGetValue(b.Landmark, out var th) && b.Height >= th);
                    if (!done && decorate)
                    {
                        var kind = BuildingDecorator.KindOf(b, poiCat[i]);
                        if (b.Use == BuildingUse.Roof && kind == DressKind.Canopy && NearFuel(fuel, cx, cy)) kind = DressKind.FuelCanopy;
                        bool canopy = kind == DressKind.Canopy || kind == DressKind.FuelCanopy;
                        // le pensiline non sono blocchi pieni: si disegnano solo come addobbo (pilastri e copertura)
                        scratch.Clear();
                        if (mesher.Build(b, ground, canopy ? scratch : mb, shops[i], false))
                        {
                            r.BuildingCount++;
                            int level = levels != null && levels.TryGetValue(((long)b.OsmType << 60) | b.OsmId, out var lv) ? lv : 1;
                            var target = canopy ? mb : DecorAt(r, data, cx, cy);
                            string name = !string.IsNullOrEmpty(poiName[i]) ? poiName[i] : b.Name;
                            r.DecorationTriangles += decorator.Decorate(b, mesher, kind, level, poiPos[i], roads, ground, target, decoPlants, name);
                        }
                    }
                    else if (done || mesher.Build(b, ground, mb, shops[i], lod != TileLod.Full)) r.BuildingCount++;
                    if (i % 96 == 95) yield return null;
                }
                // lampioni sui marciapiedi delle vie urbane (con gli addobbi: si spengono da lontano; non sui profili leggeri)
                if (decorate && decorDensity >= 0.6f)
                {
                    int k = 0;
                    foreach (var road in data.Roads)
                    {
                        if (road.Points == null || road.Points.Length < 4) continue;
                        int mid = (road.Points.Length / 4) * 2;
                        var deco = DecorAt(r, data, road.Points[mid], road.Points[mid + 1]);
                        int start = deco.IndexCount;
                        StreetFurniture.Lamps(road, ground, deco);
                        r.DecorationTriangles += (deco.IndexCount - start) / 3;
                        if (++k % 300 == 0) yield return null;
                    }
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

        private static MeshBuffer DecorAt(TileBuildResult r, TileData data, float x, float y)
        {
            float chunk = data.Size / TileBuildResult.Chunks;
            int ix = Clamp((int)(x / chunk)), iy = Clamp((int)(y / chunk));
            int k = iy * TileBuildResult.Chunks + ix;
            return r.Decorations[k] ?? (r.Decorations[k] = new MeshBuffer());
        }

        private static bool NearFuel(List<PoiInfo> fuel, float x, float y)
        {
            if (fuel == null) return false;
            foreach (var p in fuel)
                if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < 40 * 40) return true;
            return false;
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
