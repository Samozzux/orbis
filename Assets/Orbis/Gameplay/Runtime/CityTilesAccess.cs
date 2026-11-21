using System.Collections.Generic;
using Orbis.Core.Game;
using Orbis.Core.World.Tiles;

namespace Orbis.Gameplay
{
    /// <summary>Tutti i tile del comune letti dal caricatore della città (anche lontani dalla camera), poi quelli dello streaming.</summary>
    public sealed class CityTilesAccess : IWorldAccess
    {
        private readonly Dictionary<long, TileData> _tiles = new Dictionary<long, TileData>();
        private readonly IWorldAccess _fallback;

        public CityTilesAccess(IEnumerable<(TileData Tile, double OriginX, double OriginY)> tiles, IWorldAccess fallback)
        {
            foreach (var t in tiles) _tiles[Key(t.Tile.Id.X, t.Tile.Id.Y)] = t.Tile;
            _fallback = fallback;
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public TileData Tile(int x, int y) => _tiles.TryGetValue(Key(x, y), out var t) ? t : _fallback?.Tile(x, y);
    }
}
