using Orbis.Core.Game;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using Orbis.World;

namespace Orbis.Gameplay
{
    /// <summary>I tile caricati dallo streaming, per le regole di piazzamento del Core.</summary>
    public sealed class WorldAccess : IWorldAccess
    {
        private readonly WorldStreamer _world;

        public WorldAccess(WorldStreamer world) => _world = world;

        public TileData Tile(int x, int y)
        {
            if (_world.Region == null) return null;
            return _world.Tiles.TryGetValue(new TileId(_world.Region.Zoom, x, y), out var v) ? v.Data : null;
        }
    }
}
