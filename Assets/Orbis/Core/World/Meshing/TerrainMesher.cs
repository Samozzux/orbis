using System;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Mesh del terreno di un tile: griglia di quote con la stessa diagonale di <see cref="TerrainInfo.HeightAt"/>
    /// e "gonne" verticali sui bordi per nascondere le fessure tra tile con dettaglio diverso.
    /// uv0 = (u, v del tile 0..1 per la texture delle colture, x, y in metri per i dettagli).
    /// </summary>
    public static class TerrainMesher
    {
        /// <param name="step">1 = tutti i campioni, 2 o 4 per i tile lontani</param>
        public static void Build(TerrainInfo t, MeshBuffer mb, int step = 1, float skirt = 25f)
        {
            int size = t.Size;
            if (step < 1 || (size - 1) % step != 0) step = 1;
            int n = (size - 1) / step + 1;
            float cell = t.CellSize;
            float tile = t.TileSize;
            uint white = MeshBuffer.Rgba(255, 255, 255);
            int start = mb.VertexCount;
            for (int r = 0; r < n; r++)
            {
                int row = r * step;
                for (int c = 0; c < n; c++)
                {
                    int col = c * step;
                    float x = col * cell, y = row * cell;
                    float h = t.Heights[row * size + col];
                    // normale dalle differenze centrali (sul bordo si usa il campione interno)
                    float hl = t.At(col - step, row), hr = t.At(col + step, row);
                    float hd = t.At(col, row - step), hu = t.At(col, row + step);
                    float dx = (hr - hl) / ((Math.Min(col + step, size - 1) - Math.Max(col - step, 0)) * cell);
                    float dy = (hu - hd) / ((Math.Min(row + step, size - 1) - Math.Max(row - step, 0)) * cell);
                    float nl = (float)Math.Sqrt(dx * dx + dy * dy + 1);
                    mb.Add(x, y, h, -dx / nl, -dy / nl, 1 / nl, white, x / tile, y / tile, x, y, 0, 0, 0, 0);
                }
            }
            for (int r = 0; r < n - 1; r++)
            for (int c = 0; c < n - 1; c++)
            {
                int v00 = start + r * n + c;
                int v10 = v00 + 1;
                int v01 = v00 + n;
                int v11 = v01 + 1;
                mb.Triangle(v00, v10, v11);
                mb.Triangle(v00, v11, v01);
            }
            if (skirt <= 0) return;
            // gonne: sud (r = 0), est (c = n-1), nord (r = n-1), ovest (c = 0), in senso antiorario
            AddSkirt(mb, start, n, skirt, white, tile, (i) => i, 0, -1);                    // sud
            AddSkirt(mb, start, n, skirt, white, tile, (i) => i * n + (n - 1), 1, 0);       // est
            AddSkirt(mb, start, n, skirt, white, tile, (i) => (n - 1) * n + (n - 1 - i), 0, 1); // nord
            AddSkirt(mb, start, n, skirt, white, tile, (i) => (n - 1 - i) * n, -1, 0);      // ovest
        }

        private static void AddSkirt(MeshBuffer mb, int start, int n, float depth, uint color, float tile,
            Func<int, int> index, float nx, float ny)
        {
            int first = mb.VertexCount;
            for (int i = 0; i < n; i++)
            {
                int v = start + index(i);
                float x = mb.GetFloat(v, 0), h = mb.GetFloat(v, 1), y = mb.GetFloat(v, 2);
                mb.Add(x, y, h, nx, ny, 0, color, x / tile, y / tile, x, y, 0, 0, 0, 0);
                mb.Add(x, y, h - depth, nx, ny, 0, color, x / tile, y / tile, x, y, 0, 0, 0, 0);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int top0 = first + i * 2, bot0 = top0 + 1, top1 = top0 + 2, bot1 = top0 + 3;
                // visto da fuori i punti vanno da sinistra a destra → (bot0, bot1, top1, top0)
                mb.Quad(bot0, bot1, top1, top0);
            }
        }
    }
}
