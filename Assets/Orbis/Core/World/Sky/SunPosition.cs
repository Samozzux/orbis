using System;

namespace Orbis.Core.World.Sky
{
    /// <summary>
    /// Posizione apparente del sole (formula semplificata NOAA, precisione ~1°): basta per luci, ombre e alba/tramonto.
    /// </summary>
    public static class SunPosition
    {
        /// <summary>Elevazione e azimut (gradi; azimut da nord in senso orario) per ora solare locale.</summary>
        /// <param name="dayOfYear">1..366</param>
        /// <param name="solarHour">ora solare locale (12 = mezzogiorno), 0..24</param>
        public static void Compute(double latitude, int dayOfYear, double solarHour, out double elevation, out double azimuth)
        {
            double g = 2 * Math.PI / 365.0 * (dayOfYear - 1 + (solarHour - 12) / 24.0);
            double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
                          + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
            double lat = latitude * Math.PI / 180;
            double ha = (solarHour - 12) * 15 * Math.PI / 180;
            double cosZen = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha);
            cosZen = Math.Max(-1, Math.Min(1, cosZen));
            double zen = Math.Acos(cosZen);
            elevation = 90 - zen * 180 / Math.PI;
            double sinZen = Math.Sin(zen);
            double az;
            if (sinZen < 1e-6) az = 180;
            else
            {
                double cosAz = (Math.Sin(decl) - Math.Sin(lat) * cosZen) / (Math.Cos(lat) * sinZen);
                cosAz = Math.Max(-1, Math.Min(1, cosAz));
                az = Math.Acos(cosAz) * 180 / Math.PI; // da nord verso est al mattino
                if (ha > 0) az = 360 - az;              // pomeriggio: verso ovest
            }
            azimuth = az;
        }

        /// <summary>Ora solare locale dall'ora civile (Europa centrale, con ora legale) e dalla longitudine.</summary>
        public static double SolarHour(DateTime local, double longitude, bool daylightSaving)
        {
            double zone = 15.0 * (daylightSaving ? 2 : 1); // meridiano del fuso
            double h = local.Hour + local.Minute / 60.0 + local.Second / 3600.0;
            return h + (longitude - zone) / 15.0;
        }
    }
}
