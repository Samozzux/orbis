using System;

namespace Orbis.Core.World.Geo
{
    /// <summary>
    /// Proiezione Web Mercator (EPSG:3857) e conversioni tile, come la pipeline Python
    /// (tools/world-pipeline/orbis_world/geo.py): le due implementazioni devono coincidere.
    /// </summary>
    public static class WebMercator
    {
        public const double EarthRadius = 6378137.0;
        public const double HalfCircumference = Math.PI * EarthRadius; // 20037508.34 m
        public const double MaxLatitude = 85.05112877980659;

        /// <summary>Longitudine/latitudine (gradi) → metri Mercatore.</summary>
        public static void ToMeters(double lon, double lat, out double x, out double y)
        {
            lat = Math.Max(-MaxLatitude, Math.Min(MaxLatitude, lat));
            x = lon * HalfCircumference / 180.0;
            y = Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) * EarthRadius;
        }

        /// <summary>Metri Mercatore → longitudine/latitudine (gradi).</summary>
        public static void ToLonLat(double x, double y, out double lon, out double lat)
        {
            lon = x / HalfCircumference * 180.0;
            lat = Math.Atan(Math.Exp(y / EarthRadius)) * 360.0 / Math.PI - 90.0;
        }

        /// <summary>Lato di un tile in metri Mercatore.</summary>
        public static double TileSize(int z) => 2.0 * HalfCircumference / (1 << z);

        /// <summary>Fattore metri veri / metri Mercatore alla latitudine data.</summary>
        public static double ScaleAt(double lat) => Math.Cos(lat * Math.PI / 180.0);

        /// <summary>Angolo sud-ovest del tile (x, y nello schema XYZ con y verso sud) in metri Mercatore.</summary>
        public static void TileOrigin(int z, int tx, int ty, out double x, out double y)
        {
            double size = TileSize(z);
            x = -HalfCircumference + tx * size;
            y = HalfCircumference - (ty + 1) * size;
        }

        /// <summary>Tile che contiene il punto in metri Mercatore.</summary>
        public static TileId TileAt(int z, double x, double y)
        {
            double size = TileSize(z);
            int n = 1 << z;
            int tx = (int)Math.Floor((x + HalfCircumference) / size);
            int ty = (int)Math.Floor((HalfCircumference - y) / size);
            tx = Math.Max(0, Math.Min(n - 1, tx));
            ty = Math.Max(0, Math.Min(n - 1, ty));
            return new TileId(z, tx, ty);
        }
    }
}
