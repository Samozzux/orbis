using System;
using System.Collections.Generic;
using Google.FlatBuffers;
using Orbis.Core.World.Compression;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;

namespace Orbis.Core.World.Tiles
{
    /// <summary>Legge i file del region pack (tile e indice) e li converte nel modello in memoria.</summary>
    public static class TileReader
    {
        /// <summary>Versione del formato supportata (deve coincidere con FORMAT_VERSION della pipeline).</summary>
        public const int FormatVersion = 1;

        /// <summary>Legge un file di tile compresso.</summary>
        /// <param name="scale">metri veri per metro Mercatore della regione (<see cref="RegionInfo.Scale"/>)</param>
        public static TileData ReadFile(byte[] file, double scale) => Read(TileFile.Unpack(file), scale);

        /// <summary>Legge un buffer FlatBuffers già decompresso.</summary>
        public static TileData Read(byte[] buffer, double scale)
        {
            var bb = new ByteBuffer(buffer);
            if (!Tile.TileBufferHasIdentifier(bb)) throw new CorruptDataException("non è un tile di Orbis");
            var t = Tile.GetRootAsTile(bb);
            if (t.Format != FormatVersion)
                throw new CorruptDataException("formato del tile " + t.Format + " non supportato (atteso " + FormatVersion + ")");

            var id = new TileId(t.Z, (int)t.X, (int)t.Y);
            float k = (float)(scale * 0.01); // cm Mercatore → metri veri
            var data = new TileData
            {
                Id = id,
                Scale = scale,
                Size = (float)(WebMercator.TileSize(id.Z) * scale),
            };

            data.Buildings = new BuildingInfo[t.BuildingsLength];
            for (int i = 0; i < data.Buildings.Length; i++)
            {
                var b = t.Buildings(i).Value;
                var info = new BuildingInfo
                {
                    OsmId = b.OsmId,
                    OsmType = b.OsmType,
                    Outer = Scale(b.GetOuterArray(), k),
                    Holes = ReadHoles(b.HolesLength, b.Holes, k),
                    Height = b.Height * 0.01f,
                    MinHeight = b.MinHeight * 0.01f,
                    RoofHeight = b.RoofHeight * 0.01f,
                    Levels = b.Levels,
                    Roof = b.RoofShape,
                    Use = b.Use,
                    Material = b.Material,
                    WallColor = b.WallColor,
                    RoofColor = b.RoofColor,
                    Flags = (BuildingFlag)b.Flags,
                    Year = b.Year,
                    Name = b.Name,
                    Address = b.Address,
                    Landmark = b.Landmark,
                };
                info.Bounds = Bounds2.Of(info.Outer);
                data.Buildings[i] = info;
            }

            data.Roads = new RoadInfo[t.RoadsLength];
            for (int i = 0; i < data.Roads.Length; i++)
            {
                var r = t.Roads(i).Value;
                data.Roads[i] = new RoadInfo
                {
                    OsmId = r.OsmId,
                    Points = Scale(r.GetPtsArray(), k),
                    Class = r.Cls,
                    Width = r.Width * 0.01f,
                    Lanes = r.Lanes,
                    Oneway = r.Oneway,
                    Layer = r.Layer,
                    Flags = (RoadFlag)r.Flags,
                    MaxSpeed = r.MaxSpeed,
                    Name = r.Name,
                    Ref = r.Ref,
                };
            }

            data.Lines = new LineInfo[t.LinesLength];
            for (int i = 0; i < data.Lines.Length; i++)
            {
                var l = t.Lines(i).Value;
                data.Lines[i] = new LineInfo
                {
                    OsmId = l.OsmId,
                    Kind = l.Kind,
                    Points = Scale(l.GetPtsArray(), k),
                    Width = l.Width * 0.01f,
                    Name = l.Name,
                };
            }

            data.Areas = new AreaInfo[t.AreasLength];
            for (int i = 0; i < data.Areas.Length; i++)
            {
                var a = t.Areas(i).Value;
                var info = new AreaInfo
                {
                    OsmId = a.OsmId,
                    OsmType = a.OsmType,
                    Kind = a.Kind,
                    Outer = Scale(a.GetOuterArray(), k),
                    Holes = ReadHoles(a.HolesLength, a.Holes, k),
                    Name = a.Name,
                    Landmark = a.Landmark,
                };
                info.Bounds = Bounds2.Of(info.Outer);
                data.Areas[i] = info;
            }

            data.Pois = new PoiInfo[t.PoisLength];
            for (int i = 0; i < data.Pois.Length; i++)
            {
                var p = t.Pois(i).Value;
                data.Pois[i] = new PoiInfo
                {
                    OsmId = p.OsmId,
                    OsmType = p.OsmType,
                    X = p.X * k,
                    Y = p.Y * k,
                    Category = p.Category,
                    Name = p.Name,
                    Address = p.Address,
                    Building = p.Building,
                };
            }

            var treePts = t.GetTreePtsArray();
            if (treePts != null && treePts.Length > 0)
            {
                int n = treePts.Length / 2;
                data.TreePoints = Scale(treePts, k);
                var kinds = t.GetTreeKindsArray();
                var heights = t.GetTreeHeightsArray();
                data.TreeKinds = new TreeKind[n];
                data.TreeHeights = new float[n];
                for (int i = 0; i < n; i++)
                {
                    data.TreeKinds[i] = kinds != null && i < kinds.Length ? kinds[i] : TreeKind.Broadleaf;
                    data.TreeHeights[i] = heights != null && i < heights.Length ? heights[i] * 0.1f : 0f;
                }
            }

            data.Lots = new LotInfo[t.LotsLength];
            for (int i = 0; i < data.Lots.Length; i++)
            {
                var l = t.Lots(i).Value;
                var info = new LotInfo
                {
                    Id = l.Id,
                    Kind = l.Kind,
                    Outer = Scale(l.GetOuterArray(), k),
                    Holes = ReadHoles(l.HolesLength, l.Holes, k),
                    GridOriginX = l.GridX * k,
                    GridOriginY = l.GridY * k,
                    GridAngle = (float)(l.GridAngle / 1000.0 * Math.PI / 180.0),
                    FrontRoad = l.FrontRoad,
                    Area = l.Area,
                };
                info.Bounds = Bounds2.Of(info.Outer);
                data.Lots[i] = info;
            }

            data.Terrain = ReadTerrain(t.Terrain, data.Size);
            return data;
        }

        private static TerrainInfo ReadTerrain(Terrain? terrain, float tileSize)
        {
            if (!terrain.HasValue || terrain.Value.Size < 2) return TerrainInfo.Flat(tileSize);
            var tr = terrain.Value;
            int size = tr.Size;
            var raw = tr.GetHeightsArray();
            if (raw == null || raw.Length != size * size) throw new CorruptDataException("terreno non valido");
            float baseM = tr.Base * 0.01f;
            float step = tr.Step * 0.001f;
            var h = new float[raw.Length];
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < raw.Length; i++)
            {
                float v = baseM + raw[i] * step;
                h[i] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return new TerrainInfo { Size = size, TileSize = tileSize, Heights = h, MinHeight = min, MaxHeight = max };
        }

        private static float[] Scale(int[] pts, float k)
        {
            if (pts == null) return Array.Empty<float>();
            var r = new float[pts.Length];
            for (int i = 0; i < pts.Length; i++) r[i] = pts[i] * k;
            return r;
        }

        private static float[][] ReadHoles(int count, Func<int, Ring?> get, float k)
        {
            if (count == 0) return Array.Empty<float[]>();
            var holes = new List<float[]>(count);
            for (int i = 0; i < count; i++)
            {
                var ring = get(i);
                if (!ring.HasValue) continue;
                var pts = ring.Value.GetPtsArray();
                if (pts != null && pts.Length >= 6) holes.Add(Scale(pts, k));
            }
            return holes.ToArray();
        }

        /// <summary>Legge l'indice del region pack (file index.bin).</summary>
        public static RegionInfo ReadIndexFile(byte[] file) => ReadIndex(TileFile.Unpack(file));

        public static RegionInfo ReadIndex(byte[] buffer)
        {
            var idx = RegionIndex.GetRootAsRegionIndex(new ByteBuffer(buffer));
            if (idx.Format != FormatVersion)
                throw new CorruptDataException("formato dell'indice " + idx.Format + " non supportato (atteso " + FormatVersion + ")");
            var info = new RegionInfo
            {
                Key = idx.Key,
                Name = idx.Name,
                Version = (int)idx.Version,
                Zoom = idx.Z,
                OriginLat = idx.OriginLat,
                OriginLon = idx.OriginLon,
                Boundary = idx.GetBoundaryArray() ?? Array.Empty<double>(),
                Attribution = idx.Attribution,
                Created = idx.Created,
                Source = idx.Source,
            };
            for (int i = 0; i < idx.TilesLength; i++)
            {
                var e = idx.Tiles(i).Value;
                var id = new TileId(info.Zoom, (int)e.X, (int)e.Y);
                info.Tiles[id] = new RegionTile(id, e.Size, e.Crc, (int)e.Buildings);
            }
            return info;
        }
    }

    public readonly struct RegionTile
    {
        public readonly TileId Id;
        public readonly uint Size;
        public readonly uint Crc;
        public readonly int Buildings;

        public RegionTile(TileId id, uint size, uint crc, int buildings)
        {
            Id = id;
            Size = size;
            Crc = crc;
            Buildings = buildings;
        }
    }

    /// <summary>Indice di un region pack: quali tile esistono e come convertire le coordinate.</summary>
    public sealed class RegionInfo
    {
        public string Key;
        public string Name;
        public int Version;
        public int Zoom;
        public double OriginLat;
        public double OriginLon;
        public double[] Boundary;
        public string Attribution;
        public string Created;
        public string Source;
        public readonly Dictionary<TileId, RegionTile> Tiles = new Dictionary<TileId, RegionTile>();

        /// <summary>Metri veri per metro Mercatore (scala unica per tutta la regione).</summary>
        public double Scale => WebMercator.ScaleAt(OriginLat);

        public WorldFrame CreateFrame() => new WorldFrame(OriginLon, OriginLat);
    }
}
