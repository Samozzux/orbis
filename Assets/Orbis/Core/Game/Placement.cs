using System;
using System.Collections.Generic;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Lots;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Game
{
    /// <summary>Accesso ai tile caricati del mondo (Unity: WorldStreamer; test: tile finti).</summary>
    public interface IWorldAccess
    {
        /// <summary>Tile z14 caricato, null se non disponibile.</summary>
        TileData Tile(int x, int y);
    }

    /// <summary>Esito di un'azione di costruzione (docs/04 §4: anteprima rossa con il motivo).</summary>
    public enum BuildResult
    {
        Ok = 0,
        OutsideLot,
        Occupied,
        NotUnlocked,
        NotEnoughCash,
        LotNotLoaded,
        UnknownItem,
        UnknownObject,
        MaxLevel,
        NotReady,
    }

    /// <summary>Tutto ciò che serve a comandi e simulazione.</summary>
    public sealed class GameContext
    {
        public GameState State;
        public Catalog Catalog;
        public Balance Balance;
        public IWorldAccess World;
        public Missions Missions;

        public GameContext(GameState state, Catalog catalog, Balance balance, IWorldAccess world, Missions missions = null)
        {
            State = state;
            Catalog = catalog;
            Balance = balance;
            World = world;
            Missions = missions ?? new Missions();
        }
    }

    /// <summary>
    /// Regole di piazzamento sulla griglia dei lotti (docs/04 §4): la sagoma esatta deve stare tutta dentro il
    /// lotto e su celle libere (edifici OSM non demoliti ed elementi già piazzati occupano le loro celle).
    /// </summary>
    public static class Placement
    {
        public static LotInfo FindLot(TileData tile, uint lotId)
        {
            if (tile == null) return null;
            foreach (var l in tile.Lots)
                if (l.Id == lotId) return l;
            return null;
        }

        /// <summary>Estensione dell'oggetto sulla griglia in metri dopo la rotazione.</summary>
        public static void Extent(CatalogItem item, int rotation, out float sx, out float sy)
        {
            bool swap = (rotation & 1) == 1;
            sx = swap ? item.Depth : item.Width;
            sy = swap ? item.Width : item.Depth;
        }

        /// <summary>Sagoma dell'oggetto (rettangolo) in coordinate del tile, anello antiorario.</summary>
        public static float[] Footprint(LotInfo lot, CatalogItem item, int halfX, int halfY, int rotation)
        {
            Extent(item, rotation, out var sx, out var sy);
            var o = new Vec2(lot.GridOriginX, lot.GridOriginY);
            var ax = new Vec2((float)Math.Cos(lot.GridAngle), (float)Math.Sin(lot.GridAngle));
            var ay = new Vec2(-ax.Y, ax.X);
            float x0 = halfX * 0.5f, y0 = halfY * 0.5f;
            var p0 = o + ax * x0 + ay * y0;
            var p1 = o + ax * (x0 + sx) + ay * y0;
            var p2 = o + ax * (x0 + sx) + ay * (y0 + sy);
            var p3 = o + ax * x0 + ay * (y0 + sy);
            return new[] { p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y };
        }

        /// <summary>Centro dell'oggetto in coordinate del tile e angolo (radianti) della sua rotazione.</summary>
        public static Vec2 Center(LotInfo lot, CatalogItem item, int halfX, int halfY, int rotation, out float angle)
        {
            var f = Footprint(lot, item, halfX, halfY, rotation);
            angle = lot.GridAngle + rotation * (float)(Math.PI / 2);
            return new Vec2((f[0] + f[4]) / 2, (f[1] + f[5]) / 2);
        }

        /// <summary>Griglia del lotto con le celle già occupate (edifici OSM non demoliti ed elementi piazzati).</summary>
        public static LotGrid Occupancy(GameContext ctx, LotKey key, float cellSize, int ignoreObjectId = 0)
        {
            var tile = ctx.World?.Tile(key.TileX, key.TileY);
            var lot = FindLot(tile, key.LotId);
            if (lot == null) return null;
            var grid = new LotGrid(lot, cellSize);
            for (int b = 0; b < tile.Buildings.Length; b++)
            {
                var bi = tile.Buildings[b];
                if (!bi.Bounds.Intersects(lot.Bounds)) continue;
                if (ctx.State.DemolishedOsm.ContainsKey(GameState.OsmKey(bi.OsmType, bi.OsmId))) continue;
                grid.Occupy(bi.Outer, b);
            }
            foreach (var o in ctx.State.Objects.Values)
            {
                if (o.Id == ignoreObjectId || !o.Lot.Equals(key)) continue;
                var item = ctx.Catalog.Get(o.ItemKey);
                if (item == null) continue;
                grid.Occupy(Footprint(lot, item, o.HalfX, o.HalfY, o.Rotation), ObjectOwner(o.Id));
            }
            return grid;
        }

        /// <summary>Proprietario di una cella occupata da un elemento piazzato (gli edifici OSM usano il loro indice).</summary>
        public static int ObjectOwner(int objectId) => 10_000_000 + objectId;

        /// <summary>Controlla solo la geometria (lotto e celle libere).</summary>
        public static BuildResult CheckSpace(GameContext ctx, CatalogItem item, LotKey key, int halfX, int halfY, int rotation, int ignoreObjectId = 0)
        {
            var grid = Occupancy(ctx, key, item.CellSize, ignoreObjectId);
            if (grid == null) return BuildResult.LotNotLoaded;
            Extent(item, rotation, out var sx, out var sy);
            // posizione e misure in celle della griglia dell'oggetto
            float cells = 1f / item.CellSize;
            int cx = (int)Math.Floor(halfX * 0.5f * cells + 1e-4f), cy = (int)Math.Floor(halfY * 0.5f * cells + 1e-4f);
            int w = (int)Math.Round(sx * cells), h = (int)Math.Round(sy * cells);
            switch (grid.CanPlace(cx, cy, w, h))
            {
                case PlacementResult.OutsideLot: return BuildResult.OutsideLot;
                case PlacementResult.Occupied: return BuildResult.Occupied;
                default: return BuildResult.Ok;
            }
        }

        /// <summary>Controllo completo per un nuovo elemento: sblocco, cassa e spazio.</summary>
        public static BuildResult CanBuild(GameContext ctx, CatalogItem item, string variant, LotKey key, int halfX, int halfY, int rotation)
        {
            if (item == null) return BuildResult.UnknownItem;
            if (!ctx.State.Config.CreativeMode && ctx.State.Level < item.UnlockLevel) return BuildResult.NotUnlocked;
            if (!ctx.State.Config.CreativeMode && ctx.State.Cash < CostOf(item, variant)) return BuildResult.NotEnoughCash;
            return CheckSpace(ctx, item, key, halfX, halfY, rotation);
        }

        public static long CostOf(CatalogItem item, string variant, int level = 1)
        {
            var v = item.Variant(variant);
            return (long)Math.Round(item.LevelSpec(level).Cost * (v != null ? v.CostMultiplier : 1f));
        }

        /// <summary>Allinea una posizione in mezzi metri alla griglia dell'oggetto (1 m o 0,5 m).</summary>
        public static int Snap(CatalogItem item, float meters)
        {
            float step = item.CellSize;
            return (int)Math.Round(Math.Floor(meters / step + 1e-4) * step * 2f);
        }
    }
}
