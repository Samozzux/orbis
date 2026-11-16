namespace Orbis.Core.World.Geo
{
    /// <summary>
    /// Sistema di coordinate del gioco: metri veri attorno a un'origine (lon/lat), x verso est e y verso nord
    /// (in Unity y diventa z). Usa una scala unica cos(lat origine) per tutta la regione, così i tile
    /// combaciano senza fessure; l'errore sulle distanze resta sotto lo 0,5% entro 50 km dall'origine.
    /// </summary>
    public sealed class WorldFrame
    {
        public readonly double OriginLon;
        public readonly double OriginLat;
        public readonly double OriginX;
        public readonly double OriginY;
        /// <summary>Metri veri per metro Mercatore.</summary>
        public readonly double Scale;

        public WorldFrame(double originLon, double originLat)
        {
            OriginLon = originLon;
            OriginLat = originLat;
            WebMercator.ToMeters(originLon, originLat, out OriginX, out OriginY);
            Scale = WebMercator.ScaleAt(originLat);
        }

        public void FromLonLat(double lon, double lat, out double x, out double y)
        {
            WebMercator.ToMeters(lon, lat, out var mx, out var my);
            x = (mx - OriginX) * Scale;
            y = (my - OriginY) * Scale;
        }

        public void ToLonLat(double x, double y, out double lon, out double lat)
        {
            WebMercator.ToLonLat(OriginX + x / Scale, OriginY + y / Scale, out lon, out lat);
        }

        /// <summary>Posizione (metri del gioco) dell'angolo sud-ovest del tile.</summary>
        public void TileOrigin(TileId id, out double x, out double y)
        {
            WebMercator.TileOrigin(id.Z, id.X, id.Y, out var mx, out var my);
            x = (mx - OriginX) * Scale;
            y = (my - OriginY) * Scale;
        }

        /// <summary>Lato del tile in metri del gioco.</summary>
        public double TileSize(int z) => WebMercator.TileSize(z) * Scale;

        /// <summary>Tile che contiene il punto (metri del gioco).</summary>
        public TileId TileAt(int z, double x, double y) => WebMercator.TileAt(z, OriginX + x / Scale, OriginY + y / Scale);
    }
}
