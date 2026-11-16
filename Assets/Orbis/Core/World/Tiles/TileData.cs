using System;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;

namespace Orbis.Core.World.Tiles
{
    // Modello in memoria di un tile, già convertito in metri veri relativi all'angolo
    // sud-ovest del tile (x verso est, y verso nord). Lo produce TileReader.

    public sealed class BuildingInfo
    {
        public long OsmId;
        public OsmType OsmType;
        /// <summary>Anello esterno [x0, y0, x1, y1…] in metri, antiorario.</summary>
        public float[] Outer;
        /// <summary>Buchi (cortili), orari. Mai null.</summary>
        public float[][] Holes;
        /// <summary>Altezza dalla base alla cima del tetto, in metri.</summary>
        public float Height;
        public float MinHeight;
        /// <summary>Altezza del tetto in metri; 0 = da stimare.</summary>
        public float RoofHeight;
        public int Levels;
        public RoofShape Roof;
        public BuildingUse Use;
        public WallMaterial Material;
        /// <summary>0x01RRGGBB se indicato in OSM, 0 = colore dal profilo di stile.</summary>
        public uint WallColor;
        public uint RoofColor;
        public BuildingFlag Flags;
        public int Year;
        public string Name;
        public string Address;
        public string Landmark;
        public Bounds2 Bounds;

        public bool HasFlag(BuildingFlag f) => (Flags & f) != 0;
    }

    public sealed class RoadInfo
    {
        public long OsmId;
        public float[] Points;
        public RoadClass Class;
        /// <summary>Larghezza della carreggiata in metri.</summary>
        public float Width;
        public int Lanes;
        public int Oneway;
        public int Layer;
        public RoadFlag Flags;
        public int MaxSpeed;
        public string Name;
        public string Ref;

        public bool HasFlag(RoadFlag f) => (Flags & f) != 0;
        public bool IsRail => Class >= RoadClass.Rail && Class < RoadClass.Runway;
        public bool IsAeroway => Class >= RoadClass.Runway;
    }

    public sealed class LineInfo
    {
        public long OsmId;
        public LineKind Kind;
        public float[] Points;
        public float Width;
        public string Name;
    }

    public sealed class AreaInfo
    {
        public long OsmId;
        public OsmType OsmType;
        public AreaKind Kind;
        public float[] Outer;
        public float[][] Holes;
        public string Name;
        public string Landmark;
        public Bounds2 Bounds;
    }

    public sealed class PoiInfo
    {
        public long OsmId;
        public OsmType OsmType;
        public float X;
        public float Y;
        public string Category;
        public string Name;
        public string Address;
        public int Building;
    }

    public sealed class LotInfo
    {
        public uint Id;
        public LotKind Kind;
        public float[] Outer;
        public float[][] Holes;
        public float GridOriginX;
        public float GridOriginY;
        /// <summary>Angolo dell'asse x della griglia, radianti da est in senso antiorario.</summary>
        public float GridAngle;
        public long FrontRoad;
        public float Area;
        public Bounds2 Bounds;
    }

    public struct Bounds2
    {
        public float MinX, MinY, MaxX, MaxY;

        public static Bounds2 Of(float[] pts)
        {
            var b = new Bounds2 { MinX = float.MaxValue, MinY = float.MaxValue, MaxX = float.MinValue, MaxY = float.MinValue };
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                if (pts[i] < b.MinX) b.MinX = pts[i];
                if (pts[i] > b.MaxX) b.MaxX = pts[i];
                if (pts[i + 1] < b.MinY) b.MinY = pts[i + 1];
                if (pts[i + 1] > b.MaxY) b.MaxY = pts[i + 1];
            }
            return b;
        }

        public bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        public bool Intersects(Bounds2 o) => MinX <= o.MaxX && MaxX >= o.MinX && MinY <= o.MaxY && MaxY >= o.MinY;
        public float Width => MaxX - MinX;
        public float Height => MaxY - MinY;
    }

    /// <summary>
    /// Terreno del tile: griglia Size×Size di quote in metri; il primo e l'ultimo campione cadono sui bordi,
    /// così i tile vicini combaciano. Ogni cella è divisa in due triangoli lungo la diagonale
    /// sud-ovest → nord-est: <see cref="HeightAt"/> segue esattamente la stessa superficie della mesh.
    /// </summary>
    public sealed class TerrainInfo
    {
        public int Size;
        public float TileSize;
        /// <summary>Quote [riga * Size + colonna], righe da sud a nord.</summary>
        public float[] Heights;
        public float MinHeight;
        public float MaxHeight;

        public float CellSize => TileSize / (Size - 1);

        public float At(int col, int row)
        {
            col = col < 0 ? 0 : (col >= Size ? Size - 1 : col);
            row = row < 0 ? 0 : (row >= Size ? Size - 1 : row);
            return Heights[row * Size + col];
        }

        public float HeightAt(float x, float y)
        {
            float cell = CellSize;
            float fx = x / cell, fy = y / cell;
            int c = (int)Math.Floor(fx), r = (int)Math.Floor(fy);
            if (c < 0) { c = 0; fx = 0; }
            if (r < 0) { r = 0; fy = 0; }
            if (c > Size - 2) { c = Size - 2; fx = Size - 1; }
            if (r > Size - 2) { r = Size - 2; fy = Size - 1; }
            float u = fx - c, v = fy - r;
            float h00 = Heights[r * Size + c];
            float h10 = Heights[r * Size + c + 1];
            float h01 = Heights[(r + 1) * Size + c];
            float h11 = Heights[(r + 1) * Size + c + 1];
            // triangoli (00, 10, 11) sotto la diagonale e (00, 11, 01) sopra
            if (u >= v) return h00 + (h10 - h00) * u + (h11 - h10) * v;
            return h00 + (h11 - h01) * u + (h01 - h00) * v;
        }

        public static TerrainInfo Flat(float tileSize, float height = 0f, int size = 2)
        {
            var h = new float[size * size];
            for (int i = 0; i < h.Length; i++) h[i] = height;
            return new TerrainInfo { Size = size, TileSize = tileSize, Heights = h, MinHeight = height, MaxHeight = height };
        }
    }

    public sealed class TileData
    {
        public TileId Id;
        /// <summary>Lato del tile in metri veri (scala della regione).</summary>
        public float Size;
        /// <summary>Metri veri per metro Mercatore usati per convertire le coordinate.</summary>
        public double Scale;
        public BuildingInfo[] Buildings = Array.Empty<BuildingInfo>();
        public RoadInfo[] Roads = Array.Empty<RoadInfo>();
        public LineInfo[] Lines = Array.Empty<LineInfo>();
        public AreaInfo[] Areas = Array.Empty<AreaInfo>();
        public PoiInfo[] Pois = Array.Empty<PoiInfo>();
        public float[] TreePoints = Array.Empty<float>();
        public TreeKind[] TreeKinds = Array.Empty<TreeKind>();
        public float[] TreeHeights = Array.Empty<float>();
        public LotInfo[] Lots = Array.Empty<LotInfo>();
        public TerrainInfo Terrain;

        public int TreeCount => TreeKinds.Length;
    }
}
