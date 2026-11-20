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
            var far = idx.Far;
            if (far.HasValue && far.Value.Cols > 1 && far.Value.Rows > 1)
            {
                var f = far.Value;
                var raw = f.GetHeightsArray();
                if (raw != null && raw.Length == f.Cols * f.Rows)
                {
                    var h = new float[raw.Length];
                    float baseM = f.Base * 0.01f, step = f.Step * 0.001f;
                    for (int i = 0; i < raw.Length; i++) h[i] = baseM + raw[i] * step;
                    info.Far = new FarTerrainInfo { X0 = f.X0, Y0 = f.Y0, Cell = f.Cell, Cols = f.Cols, Rows = f.Rows, Heights = h };
                }
            }
            for (int i = 0; i < idx.TilesLength; i++)
            {
                var e = idx.Tiles(i).Value;
                var id = new TileId(info.Zoom, (int)e.X, (int)e.Y);
                info.Tiles[id] = new RegionTile(id, e.Size, e.Crc, (int)e.Buildings);
            }
            ReadMunicipalities(idx, info);
            ReadTransit(idx, info);
            return info;
        }

        // pack v4: comuni (per sapere dove abita chi) e linee di trasporto pubblico; vuoti nei pack precedenti
        private static void ReadMunicipalities(RegionIndex idx, RegionInfo info)
        {
            for (int i = 0; i < idx.MunicipalitiesLength; i++)
            {
                var m = idx.Municipalities(i);
                if (!m.HasValue) continue;
                var rings = new List<double[]>(m.Value.RingsLength);
                for (int r = 0; r < m.Value.RingsLength; r++)
                {
                    var pts = m.Value.Rings(r)?.GetPtsArray();
                    if (pts != null && pts.Length >= 6 && pts.Length % 2 == 0) rings.Add(pts);
                }
                if (rings.Count > 0) info.Municipalities.Add(new MunicipalityInfo(m.Value.Code ?? "", m.Value.Name ?? "", rings.ToArray()));
            }
        }

        private static void ReadTransit(RegionIndex idx, RegionInfo info)
        {
            for (int i = 0; i < idx.TransitStopsLength; i++)
            {
                var s = idx.TransitStops(i).Value;
                info.TransitStops.Add(new TransitStopInfo { OsmId = s.OsmId, Lon = s.Lon, Lat = s.Lat, Name = s.Name ?? "" });
            }
            for (int i = 0; i < idx.TransitRoutesLength; i++)
            {
                var r = idx.TransitRoutes(i).Value;
                var stops = r.GetStopsArray() ?? Array.Empty<uint>();
                var list = new List<int>(stops.Length);
                foreach (var s in stops)
                    if (s < info.TransitStops.Count) list.Add((int)s);
                info.TransitRoutes.Add(new TransitRouteInfo
                {
                    OsmId = r.OsmId,
                    Mode = r.Mode,
                    Ref = r.Ref ?? "",
                    Name = r.Name ?? "",
                    From = r.From ?? "",
                    To = r.To ?? "",
                    Operator = r.Operator ?? "",
                    Network = r.Network ?? "",
                    Colour = r.Colour,
                    Ways = r.GetWaysArray() ?? Array.Empty<long>(),
                    Stops = list.ToArray(),
                });
            }
        }
    }

    /// <summary>Griglia di quote a bassa risoluzione attorno alla regione (coordinate in metri Mercatore assoluti).</summary>
    public sealed class FarTerrainInfo
    {
        public double X0, Y0, Cell;
        public int Cols, Rows;
        /// <summary>Quote in metri [riga * Cols + colonna], righe da sud a nord.</summary>
        public float[] Heights;

        public float At(int col, int row)
        {
            col = col < 0 ? 0 : (col >= Cols ? Cols - 1 : col);
            row = row < 0 ? 0 : (row >= Rows ? Rows - 1 : row);
            return Heights[row * Cols + col];
        }
    }

    /// <summary>Comune con il suo confine in gradi (anelli esterni e buchi, regola pari/dispari).</summary>
    public sealed class MunicipalityInfo
    {
        public readonly string Code;
        public readonly string Name;
        /// <summary>Anelli lon, lat, lon, lat… senza ripetere il primo punto.</summary>
        public readonly double[][] Rings;
        public readonly double MinLon, MinLat, MaxLon, MaxLat;

        public MunicipalityInfo(string code, string name, double[][] rings)
        {
            Code = code;
            Name = name;
            Rings = rings;
            MinLon = MinLat = double.MaxValue;
            MaxLon = MaxLat = double.MinValue;
            foreach (var r in rings)
                for (int i = 0; i + 1 < r.Length; i += 2)
                {
                    MinLon = Math.Min(MinLon, r[i]);
                    MaxLon = Math.Max(MaxLon, r[i]);
                    MinLat = Math.Min(MinLat, r[i + 1]);
                    MaxLat = Math.Max(MaxLat, r[i + 1]);
                }
        }

        public bool Contains(double lon, double lat)
        {
            if (lon < MinLon || lon > MaxLon || lat < MinLat || lat > MaxLat) return false;
            bool inside = false;
            foreach (var r in Rings)
            {
                int n = r.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = r[2 * i], yi = r[2 * i + 1], xj = r[2 * j], yj = r[2 * j + 1];
                    if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi) inside = !inside;
                }
            }
            return inside;
        }

        public override string ToString() => Name + " (" + Code + ")";
    }

    public sealed class TransitStopInfo
    {
        public long OsmId;
        public double Lon, Lat;
        public string Name;
    }

    /// <summary>Linea reale da OSM: le strade del percorso (Road.OsmId nei tile) e le fermate in ordine.</summary>
    public sealed class TransitRouteInfo
    {
        public long OsmId;
        public TransitMode Mode;
        public string Ref, Name, From, To, Operator, Network;
        /// <summary>0x01RRGGBB, 0 = nessun colore indicato.</summary>
        public uint Colour;
        public long[] Ways;
        /// <summary>Indici in <see cref="RegionInfo.TransitStops"/>.</summary>
        public int[] Stops;
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
        /// <summary>Terreno lontano (orizzonte), null se il pack non lo contiene.</summary>
        public FarTerrainInfo Far;
        /// <summary>Comuni della regione (pack v4), ordinati per codice ISTAT.</summary>
        public readonly List<MunicipalityInfo> Municipalities = new List<MunicipalityInfo>();
        /// <summary>Fermate usate dalle linee (pack v4).</summary>
        public readonly List<TransitStopInfo> TransitStops = new List<TransitStopInfo>();
        /// <summary>Linee reali di bus, filobus, tram e treni da OSM (pack v4).</summary>
        public readonly List<TransitRouteInfo> TransitRoutes = new List<TransitRouteInfo>();

        /// <summary>Comune che contiene il punto (gradi), null se fuori da tutti.</summary>
        public MunicipalityInfo MunicipalityAt(double lon, double lat)
        {
            foreach (var m in Municipalities)
                if (m.Contains(lon, lat)) return m;
            return null;
        }

        /// <summary>Comune con il codice ISTAT indicato, null se non c'è.</summary>
        public MunicipalityInfo Municipality(string code)
        {
            foreach (var m in Municipalities)
                if (m.Code == code) return m;
            return null;
        }

        /// <summary>Metri veri per metro Mercatore (scala unica per tutta la regione).</summary>
        public double Scale => WebMercator.ScaleAt(OriginLat);

        public WorldFrame CreateFrame() => new WorldFrame(OriginLon, OriginLat);
    }
}
