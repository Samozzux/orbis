using System;

namespace Orbis.Core.World.Geo
{
    /// <summary>Tile nello schema XYZ (Web Mercator, y verso sud).</summary>
    public readonly struct TileId : IEquatable<TileId>
    {
        public readonly int Z;
        public readonly int X;
        public readonly int Y;

        public TileId(int z, int x, int y)
        {
            if (z < 0 || z > 24) throw new ArgumentOutOfRangeException(nameof(z));
            Z = z;
            X = x;
            Y = y;
        }

        /// <summary>Tile vicino: dx verso est, dy verso nord.</summary>
        public TileId Offset(int dx, int dyNorth) => new TileId(Z, X + dx, Y - dyNorth);

        /// <summary>Percorso relativo del file del tile nel pack: "14/8834/6064.bin".</summary>
        public string Path => Z + "/" + X + "/" + Y + ".bin";

        public bool Equals(TileId other) => Z == other.Z && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is TileId t && Equals(t);
        public override int GetHashCode() => (Z * 397 ^ X) * 397 ^ Y;
        public static bool operator ==(TileId a, TileId b) => a.Equals(b);
        public static bool operator !=(TileId a, TileId b) => !a.Equals(b);
        public override string ToString() => Z + "/" + X + "/" + Y;
    }
}
