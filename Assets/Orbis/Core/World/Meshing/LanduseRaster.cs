using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Disegna le aree del tile (campi, vigneti, uliveti, boschi, parchi, città, acqua…) in un'immagine
    /// RGBA che fa da texture del terreno. Le aree più specifiche coprono quelle generiche; i campi hanno
    /// colori diversi tra loro (mosaico di colture), sempre uguali per la stessa area OSM.
    /// </summary>
    public static class LanduseRaster
    {
        public const uint DefaultGround = 0xC4C68E;

        private static readonly uint[] FieldColors =
        {
            0xD8C077, 0xCDB56A, 0xA8B66A, 0xB79C6F, 0x9F8763, 0xC6C88B, 0xD3B784, 0xB4BE78, 0xC9A96E, 0xA2AE6E,
        };

        public static uint ColorOf(AreaKind k, long id)
        {
            switch (k)
            {
                case AreaKind.Residential: return 0xB4AE98;
                case AreaKind.Commercial:
                case AreaKind.Retail: return 0xBDB6A6;
                case AreaKind.Industrial: return 0xB2AFA7;
                case AreaKind.Construction: return 0xC7B49A;
                case AreaKind.Railway: return 0xB3AA9C;
                case AreaKind.Military: return 0xB4B99C;
                case AreaKind.Cemetery: return 0xA5BA8E;
                case AreaKind.Parking: return 0x9C9D9D;
                case AreaKind.Pedestrian: return 0xC9BEA8;
                case AreaKind.School:
                case AreaKind.University:
                case AreaKind.Hospital: return 0xBDB7A6;
                case AreaKind.Brownfield: return 0xBFAE8E;
                case AreaKind.Greenfield: return 0xB6C388;
                case AreaKind.Garages: return 0xACABA5;
                case AreaKind.Park: return 0x8BB669;
                case AreaKind.Garden: return 0x92BD6F;
                case AreaKind.Grass: return 0x9CC277;
                case AreaKind.Meadow: return 0xB2C67F;
                case AreaKind.Playground: return 0xC8B48D;
                case AreaKind.Pitch: return 0x6DAD56;
                case AreaKind.SportsCentre: return 0xA6B888;
                case AreaKind.Stadium: return 0x8DB26B;
                case AreaKind.GolfCourse: return 0x86BE64;
                case AreaKind.Track: return 0xB7694D;
                case AreaKind.Camping:
                case AreaKind.Zoo:
                case AreaKind.ThemePark: return 0xA6BB7D;
                case AreaKind.Forest: return 0x5C8949;
                case AreaKind.Scrub: return 0x8C9F5D;
                case AreaKind.Heath: return 0xA4A56B;
                case AreaKind.Grassland: return 0xBAC285;
                case AreaKind.Wetland: return 0x8DAD8B;
                case AreaKind.Beach: return 0xE7D8AD;
                case AreaKind.Sand: return 0xE0D0A0;
                case AreaKind.BareRock: return 0xB6AEA0;
                case AreaKind.Scree: return 0xC0B8AA;
                case AreaKind.Glacier: return 0xEFF3F5;
                case AreaKind.Cliff: return 0xA79E8F;
                case AreaKind.Farmland: return FieldColors[StylePalette.Hash(id, 31) % (uint)FieldColors.Length];
                case AreaKind.Vineyard: return 0xA29660;
                case AreaKind.Orchard: return 0x96AC66;
                case AreaKind.OliveGrove: return 0xB2AC80;
                case AreaKind.Allotments: return 0x9BAE69;
                case AreaKind.Greenhouse: return 0xD7DCD8;
                case AreaKind.Farmyard: return 0xC0B192;
                case AreaKind.PlantNursery: return 0x94B068;
                case AreaKind.Quarry: return 0xC6BCAE;
                case AreaKind.Landfill: return 0xA39C84;
                case AreaKind.Meadowland: return 0xB5C47C;
                case AreaKind.SwimmingPool: return 0x57B5D7;
                case AreaKind.Sea: return 0x2A5A86;
            }
            return IsWater(k) ? 0x6C9DC2 : DefaultGround;
        }

        public static bool IsWater(AreaKind k) => k >= AreaKind.Water;

        /// <summary>Ordine di disegno: un valore più alto copre i più bassi.</summary>
        public static int Priority(AreaKind k)
        {
            if (k == AreaKind.Sea) return 90;
            if (IsWater(k)) return 80;
            switch (k)
            {
                case AreaKind.Parking:
                case AreaKind.Pedestrian: return 60;
                case AreaKind.Pitch:
                case AreaKind.Track:
                case AreaKind.Playground: return 55;
                case AreaKind.Park:
                case AreaKind.Garden:
                case AreaKind.Grass:
                case AreaKind.Cemetery:
                case AreaKind.Stadium:
                case AreaKind.SportsCentre:
                case AreaKind.GolfCourse: return 50;
                case AreaKind.Beach:
                case AreaKind.Sand:
                case AreaKind.BareRock:
                case AreaKind.Scree:
                case AreaKind.Glacier:
                case AreaKind.Wetland: return 40;
                case AreaKind.Forest:
                case AreaKind.Scrub:
                case AreaKind.Heath:
                case AreaKind.Grassland: return 30;
                case AreaKind.Vineyard:
                case AreaKind.Orchard:
                case AreaKind.OliveGrove:
                case AreaKind.Allotments:
                case AreaKind.Greenhouse:
                case AreaKind.PlantNursery:
                case AreaKind.Farmyard: return 25;
                case AreaKind.Farmland:
                case AreaKind.Meadowland:
                case AreaKind.Meadow: return 20;
            }
            return 10; // aree urbane generiche
        }

        /// <summary>Indice dell'area (+1) per pixel, 0 = nessuna. Riga 0 = sud.</summary>
        public static ushort[] Rasterize(IReadOnlyList<AreaInfo> areas, float tileSize, int size)
        {
            var owner = new ushort[size * size];
            var order = new List<int>(areas.Count);
            for (int i = 0; i < areas.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int pa = Priority(areas[a].Kind), pb = Priority(areas[b].Kind);
                if (pa != pb) return pa.CompareTo(pb);
                // a parità, le aree più piccole sopra (dettagli dentro aree grandi)
                float sa = areas[a].Bounds.Width * areas[a].Bounds.Height;
                float sb = areas[b].Bounds.Width * areas[b].Bounds.Height;
                return sb.CompareTo(sa);
            });
            float px = tileSize / size;
            var xs = new List<float>(64);
            foreach (int ai in order)
            {
                if (ai >= ushort.MaxValue - 1) continue;
                var a = areas[ai];
                int r0 = Math.Max(0, (int)Math.Floor(a.Bounds.MinY / px));
                int r1 = Math.Min(size - 1, (int)Math.Ceiling(a.Bounds.MaxY / px));
                ushort id = (ushort)(ai + 1);
                for (int r = r0; r <= r1; r++)
                {
                    float y = (r + 0.5f) * px;
                    xs.Clear();
                    Crossings(a.Outer, y, xs);
                    if (a.Holes != null)
                        foreach (var h in a.Holes) Crossings(h, y, xs);
                    if (xs.Count < 2) continue;
                    xs.Sort();
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                    {
                        int c0 = Math.Max(0, (int)Math.Ceiling(xs[k] / px - 0.5f));
                        int c1 = Math.Min(size - 1, (int)Math.Floor(xs[k + 1] / px - 0.5f));
                        int row = r * size;
                        for (int c = c0; c <= c1; c++) owner[row + c] = id;
                    }
                }
            }
            return owner;
        }

        private static void Crossings(float[] ring, float y, List<float> xs)
        {
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float yi = ring[2 * i + 1], yj = ring[2 * j + 1];
                if ((yi > y) == (yj > y)) continue;
                float xi = ring[2 * i], xj = ring[2 * j];
                xs.Add(xi + (y - yi) * (xj - xi) / (yj - yi));
            }
        }

        /// <summary>Colori RGBA32 (per Texture2D.SetPixelData), con piccole variazioni di tono stabili.</summary>
        public static byte[] Colorize(IReadOnlyList<AreaInfo> areas, ushort[] owner, int size, int tileSeed)
        {
            var rgba = new byte[size * size * 4];
            var cache = new uint[areas.Count + 1];
            cache[0] = DefaultGround;
            for (int i = 0; i < areas.Count; i++) cache[i + 1] = ColorOf(areas[i].Kind, areas[i].OsmId);
            for (int p = 0; p < owner.Length; p++)
            {
                uint c = cache[owner[p]];
                int x = p % size, y = p / size;
                // rumore a due scale: macchie larghe e grana fine
                float n = (Noise(x >> 3, y >> 3, tileSeed) - 0.5f) * 0.10f + (Noise(x, y, tileSeed + 7) - 0.5f) * 0.06f;
                if (owner[p] != 0 && IsWater(areas[owner[p] - 1].Kind)) n *= 0.3f;
                float k = 1f + n;
                int o = p * 4;
                rgba[o] = Clamp(((c >> 16) & 0xFF) * k);
                rgba[o + 1] = Clamp(((c >> 8) & 0xFF) * k);
                rgba[o + 2] = Clamp((c & 0xFF) * k);
                rgba[o + 3] = owner[p] != 0 && IsWater(areas[owner[p] - 1].Kind) ? (byte)0 : (byte)255; // alfa 0 = acqua
            }
            return rgba;
        }

        private static float Noise(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static byte Clamp(float v) => v <= 0 ? (byte)0 : (v >= 255 ? (byte)255 : (byte)v);
    }
}
