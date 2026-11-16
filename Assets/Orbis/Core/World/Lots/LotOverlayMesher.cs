using System;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Lots
{
    /// <summary>
    /// Vista di controllo dei lotti (docs/04 §4): le celle da 1 m della griglia di ogni lotto vicino, appoggiate
    /// sul terreno, verdi se libere e rosse se occupate da un edificio. uv0 = posizione nella cella (0..1).
    /// </summary>
    public static class LotOverlayMesher
    {
        public const int MaxCells = 90000;
        private static readonly uint FreeColor = MeshBuffer.Rgba(70, 210, 120, 95);
        private static readonly uint TakenColor = MeshBuffer.Rgba(235, 75, 60, 150);

        /// <returns>numero di celle disegnate</returns>
        public static int Build(TileData tile, LotGrid[] grids, float cx, float cy, float radius, MeshBuffer mb, float lift = 0.35f)
        {
            int cells = 0;
            float r2 = radius * radius;
            for (int l = 0; l < grids.Length; l++)
            {
                var g = grids[l];
                if (g == null) continue;
                var b = g.Lot.Bounds;
                float dx = Math.Max(0, Math.Max(b.MinX - cx, cx - b.MaxX));
                float dy = Math.Max(0, Math.Max(b.MinY - cy, cy - b.MaxY));
                if (dx * dx + dy * dy > r2) continue;
                for (int y = g.MinY; y < g.MinY + g.Height; y++)
                for (int x = g.MinX; x < g.MinX + g.Width; x++)
                {
                    int owner = g.Get(x, y);
                    if (owner == LotGrid.Outside) continue;
                    var c = g.CellCenter(x, y);
                    float ex = c.X - cx, ey = c.Y - cy;
                    if (ex * ex + ey * ey > r2) continue;
                    if (cells >= MaxCells) return cells;
                    uint color = owner >= 0 ? TakenColor : FreeColor;
                    var p0 = g.CellPoint(x, y);
                    var p1 = g.CellPoint(x + 1, y);
                    var p2 = g.CellPoint(x + 1, y + 1);
                    var p3 = g.CellPoint(x, y + 1);
                    var t = tile.Terrain;
                    int i0 = mb.Add(p0.X, p0.Y, t.HeightAt(p0.X, p0.Y) + lift, 0, 0, 1, color, 0, 0, (float)g.Lot.Kind, 0, 0, 0, 0, 0);
                    int i1 = mb.Add(p1.X, p1.Y, t.HeightAt(p1.X, p1.Y) + lift, 0, 0, 1, color, 1, 0, (float)g.Lot.Kind, 0, 0, 0, 0, 0);
                    int i2 = mb.Add(p2.X, p2.Y, t.HeightAt(p2.X, p2.Y) + lift, 0, 0, 1, color, 1, 1, (float)g.Lot.Kind, 0, 0, 0, 0, 0);
                    int i3 = mb.Add(p3.X, p3.Y, t.HeightAt(p3.X, p3.Y) + lift, 0, 0, 1, color, 0, 1, (float)g.Lot.Kind, 0, 0, 0, 0, 0);
                    mb.Quad(i0, i1, i2, i3);
                    cells++;
                }
            }
            return cells;
        }
    }
}
