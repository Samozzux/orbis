using System;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Lots
{
    /// <summary>Esito del controllo di piazzamento (docs/04 §4: anteprima verde/rossa con il motivo).</summary>
    public enum PlacementResult
    {
        Ok = 0,
        OutsideLot,
        Occupied,
    }

    /// <summary>
    /// Griglia di piazzamento di un lotto e mappa di occupazione (docs/04 §4).
    /// Celle quadrate di <see cref="CellSize"/> metri, allineate al lato principale del lotto
    /// (origine e angolo calcolati dalla pipeline). Una cella è "nel lotto" se è tutta dentro il perimetro;
    /// è occupata dall'elemento (edificio reale o piazzato) che la tocca. Ogni cella ha un solo proprietario.
    /// </summary>
    public sealed class LotGrid
    {
        public const int Free = -1;
        public const int Outside = -2;

        public readonly LotInfo Lot;
        public readonly float CellSize;
        public readonly Vec2 Origin;
        public readonly Vec2 AxisX;
        public readonly Vec2 AxisY;
        /// <summary>Prima cella della griglia (può essere negativa: la griglia copre tutto il lotto).</summary>
        public readonly int MinX, MinY;
        public readonly int Width, Height;
        private readonly int[] _cells;

        public LotGrid(LotInfo lot, float cellSize = 1f)
        {
            Lot = lot;
            CellSize = cellSize;
            Origin = new Vec2(lot.GridOriginX, lot.GridOriginY);
            AxisX = new Vec2((float)Math.Cos(lot.GridAngle), (float)Math.Sin(lot.GridAngle));
            AxisY = new Vec2(-AxisX.Y, AxisX.X);
            // estensione del lotto nel sistema della griglia
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            for (int i = 0; i + 1 < lot.Outer.Length; i += 2)
            {
                var p = new Vec2(lot.Outer[i], lot.Outer[i + 1]) - Origin;
                float u = Vec2.Dot(p, AxisX), v = Vec2.Dot(p, AxisY);
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }
            MinX = (int)Math.Floor(minU / cellSize);
            MinY = (int)Math.Floor(minV / cellSize);
            Width = Math.Max(1, (int)Math.Ceiling(maxU / cellSize) - MinX);
            Height = Math.Max(1, (int)Math.Ceiling(maxV / cellSize) - MinY);
            if ((long)Width * Height > 4_000_000) throw new ArgumentException("lotto troppo grande per una griglia unica");
            _cells = new int[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _cells[y * Width + x] = CellInside(MinX + x, MinY + y) ? Free : Outside;
        }

        /// <summary>Centro della cella in coordinate del tile.</summary>
        public Vec2 CellCenter(int cx, int cy) => CellPoint(cx + 0.5f, cy + 0.5f);

        public Vec2 CellPoint(float gx, float gy) => Origin + AxisX * (gx * CellSize) + AxisY * (gy * CellSize);

        /// <summary>Cella che contiene il punto del tile.</summary>
        public void CellAt(float x, float y, out int cx, out int cy)
        {
            var p = new Vec2(x, y) - Origin;
            cx = (int)Math.Floor(Vec2.Dot(p, AxisX) / CellSize);
            cy = (int)Math.Floor(Vec2.Dot(p, AxisY) / CellSize);
        }

        public int Get(int cx, int cy)
        {
            int x = cx - MinX, y = cy - MinY;
            if (x < 0 || y < 0 || x >= Width || y >= Height) return Outside;
            return _cells[y * Width + x];
        }

        public bool IsInside(int cx, int cy) => Get(cx, cy) != Outside;

        public int InsideCount
        {
            get
            {
                int n = 0;
                foreach (var c in _cells)
                    if (c != Outside) n++;
                return n;
            }
        }

        public int OccupiedCount
        {
            get
            {
                int n = 0;
                foreach (var c in _cells)
                    if (c >= 0) n++;
                return n;
            }
        }

        private static readonly float[] CornerU = { 0.5f, 0.02f, 0.98f, 0.98f, 0.02f };
        private static readonly float[] CornerV = { 0.5f, 0.02f, 0.02f, 0.98f, 0.98f };

        private bool CellInside(int cx, int cy)
        {
            // la cella è nel lotto se centro e quattro angoli (appena rientrati) sono dentro il perimetro
            for (int k = 0; k < 5; k++)
            {
                var p = CellPoint(cx + CornerU[k], cy + CornerV[k]);
                if (!Polygon2D.Contains(Lot.Outer, Lot.Holes, p.X, p.Y)) return false;
            }
            return true;
        }

        /// <summary>Segna le celle toccate dalla sagoma (anello [x,y…] nel tile) come occupate da <paramref name="owner"/>.</summary>
        public int Occupy(float[] footprint, int owner)
        {
            if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner));
            int count = 0;
            ForEachCellTouching(footprint, (x, y) =>
            {
                int i = y * Width + x;
                if (_cells[i] == Free || _cells[i] == Outside)
                {
                    // anche le celle fuori lotto toccate da un edificio reale restano "fuori", non diventano libere
                    if (_cells[i] == Free)
                    {
                        _cells[i] = owner;
                        count++;
                    }
                }
            });
            return count;
        }

        /// <summary>Libera le celle dell'elemento (demolizione o spostamento).</summary>
        public int Release(int owner)
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] == owner)
                {
                    _cells[i] = Free;
                    n++;
                }
            return n;
        }

        /// <summary>
        /// Controlla se un elemento rettangolare di w×h celle (dopo la rotazione a passi di 90°) sta con
        /// l'angolo in basso a sinistra nella cella (cx, cy).
        /// </summary>
        public PlacementResult CanPlace(int cx, int cy, int w, int h, int rotation90 = 0)
        {
            if ((rotation90 & 1) == 1)
            {
                int t = w;
                w = h;
                h = t;
            }
            var result = PlacementResult.Ok;
            for (int y = cy; y < cy + h; y++)
            for (int x = cx; x < cx + w; x++)
            {
                int c = Get(x, y);
                if (c == Outside) return PlacementResult.OutsideLot;
                if (c >= 0) result = PlacementResult.Occupied;
            }
            return result;
        }

        /// <summary>Occupa un rettangolo di celle (piazzamento confermato).</summary>
        public void Place(int cx, int cy, int w, int h, int rotation90, int owner)
        {
            if (CanPlace(cx, cy, w, h, rotation90) != PlacementResult.Ok) throw new InvalidOperationException("posizione non valida");
            if ((rotation90 & 1) == 1)
            {
                int t = w;
                w = h;
                h = t;
            }
            for (int y = cy; y < cy + h; y++)
            for (int x = cx; x < cx + w; x++)
                _cells[(y - MinY) * Width + (x - MinX)] = owner;
        }

        private void ForEachCellTouching(float[] ring, Action<int, int> action)
        {
            // riquadro della sagoma nel sistema della griglia
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            var local = new float[ring.Length];
            for (int i = 0; i + 1 < ring.Length; i += 2)
            {
                var p = new Vec2(ring[i], ring[i + 1]) - Origin;
                float u = Vec2.Dot(p, AxisX) / CellSize, v = Vec2.Dot(p, AxisY) / CellSize;
                local[i] = u;
                local[i + 1] = v;
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }
            int x0 = Math.Max(MinX, (int)Math.Floor(minU)), x1 = Math.Min(MinX + Width - 1, (int)Math.Floor(maxU));
            int y0 = Math.Max(MinY, (int)Math.Floor(minV)), y1 = Math.Min(MinY + Height - 1, (int)Math.Floor(maxV));
            for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                if (SquareTouchesPolygon(local, cx, cy)) action(cx - MinX, cy - MinY);
            }
        }

        /// <summary>Il quadrato [cx, cx+1]×[cy, cy+1] tocca il poligono (con area, non solo sul bordo)?</summary>
        private static bool SquareTouchesPolygon(float[] poly, int cx, int cy)
        {
            const float e = 0.02f; // tolleranza: una sagoma che sfiora il bordo della cella non la occupa
            if (Polygon2D.ContainsRing(poly, cx + 0.5f, cy + 0.5f)) return true;
            // un vertice del poligono dentro la cella
            for (int i = 0; i + 1 < poly.Length; i += 2)
                if (poly[i] > cx + e && poly[i] < cx + 1 - e && poly[i + 1] > cy + e && poly[i + 1] < cy + 1 - e) return true;
            // un angolo della cella dentro il poligono
            for (int k = 0; k < 4; k++)
            {
                float x = cx + ((k & 1) == 0 ? e : 1 - e), y = cy + ((k & 2) == 0 ? e : 1 - e);
                if (Polygon2D.ContainsRing(poly, x, y)) return true;
            }
            // un lato del poligono attraversa la cella
            int n = poly.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if (SegmentCrossesBox(poly[2 * j], poly[2 * j + 1], poly[2 * i], poly[2 * i + 1], cx + e, cy + e, cx + 1 - e, cy + 1 - e))
                    return true;
            return false;
        }

        private static bool SegmentCrossesBox(float ax, float ay, float bx, float by, float x0, float y0, float x1, float y1)
        {
            // Liang–Barsky
            float t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
            float[] p = { -dx, dx, -dy, dy };
            float[] q = { ax - x0, x1 - ax, ay - y0, y1 - ay };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-9f)
                {
                    if (q[i] < 0) return false;
                    continue;
                }
                float t = q[i] / p[i];
                if (p[i] < 0) t0 = Math.Max(t0, t);
                else t1 = Math.Min(t1, t);
                if (t0 > t1) return false;
            }
            return true;
        }

        /// <summary>Griglie dei lotti di un tile (stesso ordine di tile.Lots, null se troppo grandi) con gli edifici reali.</summary>
        public static LotGrid[] ForTile(TileData tile, float cellSize = 1f)
        {
            var grids = new LotGrid[tile.Lots.Length];
            for (int l = 0; l < tile.Lots.Length; l++)
            {
                var lot = tile.Lots[l];
                if (lot.Bounds.Width * lot.Bounds.Height / (cellSize * cellSize) > 1_000_000) continue;
                var g = new LotGrid(lot, cellSize);
                for (int b = 0; b < tile.Buildings.Length; b++)
                {
                    var bi = tile.Buildings[b];
                    if (bi.Bounds.Intersects(lot.Bounds)) g.Occupy(bi.Outer, b);
                }
                grids[l] = g;
            }
            return grids;
        }
    }
}
