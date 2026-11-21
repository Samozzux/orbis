using System.Collections.Generic;
using Orbis.Core.Population;
using Orbis.Core.World.Geo;

namespace Orbis.Core.Game
{
    /// <summary>Sedi dei servizi tra gli edifici del catalogo piazzati dal sindaco (caserme, ospedali, scuole…).</summary>
    public static class ServiceSources
    {
        /// <summary>
        /// Edifici del catalogo finiti che offrono un servizio, con la posizione in metri della regione. Servono i tile
        /// dei lotti (<see cref="GameContext.CityWorld"/> o quelli caricati) e il sistema di coordinate.
        /// </summary>
        public static List<ServiceSource> FromCatalog(GameContext ctx, ServicesBalance b)
        {
            var list = new List<ServiceSource>();
            var world = ctx.CityWorld ?? ctx.World;
            if (b == null || world == null || ctx.Frame == null) return list;
            foreach (var o in ctx.State.Objects.Values)
            {
                var kind = b.KindOfCatalog(o.ItemKey);
                if (kind == ServiceKind.Count || !o.IsReady(ctx.State.Now)) continue;
                var item = ctx.Catalog.Get(o.ItemKey);
                var tile = world.Tile(o.Lot.TileX, o.Lot.TileY);
                var lot = Placement.FindLot(tile, o.Lot.LotId);
                if (item == null || lot == null) continue;
                var c = Placement.Center(lot, item, o.HalfX, o.HalfY, o.Rotation, out _);
                ctx.Frame.TileOrigin(new TileId(ctx.TileZoom, o.Lot.TileX, o.Lot.TileY), out var ox, out var oy);
                list.Add(new ServiceSource { Kind = kind, X = (float)(ox + c.X), Y = (float)(oy + c.Y), Name = item.Name });
            }
            return list;
        }
    }
}
