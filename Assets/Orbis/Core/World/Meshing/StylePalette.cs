using Orbis.Core.World.Fb;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.World.Meshing
{
    /// <summary>
    /// Profilo di stile architettonico "Abruzzo" (docs/02 §4): colori di facciate e tetti scelti in modo
    /// stabile dall'ID OSM, così lo stesso edificio ha sempre lo stesso aspetto. I colori indicati in OSM
    /// (building:colour, roof:colour) hanno la precedenza.
    /// </summary>
    public static class StylePalette
    {
        // intonaci caldi del centro storico
        private static readonly uint[] HistoricWalls =
        {
            0xEAD7B0, 0xE3C08A, 0xEFD9A6, 0xDDA98C, 0xE8BCA4, 0xF1E6CF, 0xD9C4A0, 0xE6B97F,
            0xCFAF8E, 0xEFCB95, 0xE2A98A, 0xF3E3C4, 0xD7B892, 0xC9A27C,
        };

        // condomini e case recenti: colori più chiari
        private static readonly uint[] ModernWalls =
        {
            0xF2EEE6, 0xE9E2D3, 0xEDE0C8, 0xF0E4CF, 0xE4DACB, 0xEAD4B8, 0xDCD6CC, 0xF4EBDB,
            0xE6CFAE, 0xD8CDBE,
        };

        private static readonly uint[] IndustrialWalls = { 0xD9DCDD, 0xC8CCCE, 0xE3E1DB, 0xB9C3C9, 0xD1CBC0 };
        private static readonly uint[] FarmWalls = { 0xD8C7A8, 0xCDB28E, 0xE0CDA8, 0xC4A882, 0xBFA58A };

        // coppi
        private static readonly uint[] RoofTiles = { 0xC0623F, 0xB25A3C, 0xCB7147, 0xA9533A, 0xBD6A4A, 0xC8784F, 0x9E4F38 };
        private static readonly uint[] FlatRoofs = { 0xBDB6AA, 0xA9A399, 0xC7C1B5, 0x9C978F, 0xB3ABA0 };

        public static uint Hash(long id, uint salt = 0)
        {
            unchecked
            {
                ulong x = (ulong)id * 0x9E3779B97F4A7C15UL ^ salt * 0xBF58476D1CE4E5B9UL;
                x ^= x >> 30;
                x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27;
                x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (uint)x;
            }
        }

        /// <summary>Numero in [0, 1) stabile per l'oggetto.</summary>
        public static float Random01(long id, uint salt = 0) => (Hash(id, salt) & 0xFFFFFF) / 16777216f;

        public static uint WallColor(BuildingInfo b)
        {
            if ((b.WallColor & 0x1000000) != 0) return b.WallColor & 0xFFFFFF;
            uint h = Hash(b.OsmId, 11);
            switch (b.Material)
            {
                case WallMaterial.Brick: return Pick(new uint[] { 0xB4704F, 0xA8654A, 0xBF7B58 }, h);
                case WallMaterial.Stone: return Pick(new uint[] { 0xCDBB9A, 0xC2AE8C, 0xD6C6A6 }, h);
                case WallMaterial.Concrete: return Pick(new uint[] { 0xC9C6BF, 0xBDBAB3 }, h);
                case WallMaterial.Glass: return 0x9FB6C4;
                case WallMaterial.Metal: return 0xB8BEC2;
                case WallMaterial.Marble: return 0xF1EEE8;
                case WallMaterial.Wood: return 0x9C7350;
            }
            switch (b.Use)
            {
                case BuildingUse.Industrial:
                case BuildingUse.Warehouse:
                case BuildingUse.Commercial:
                case BuildingUse.Retail:
                case BuildingUse.Service:
                case BuildingUse.Garages:
                case BuildingUse.Garage:
                case BuildingUse.Parking:
                    return Pick(IndustrialWalls, h);
                case BuildingUse.Farm:
                case BuildingUse.FarmAuxiliary:
                case BuildingUse.Barn:
                case BuildingUse.Stable:
                case BuildingUse.Shed:
                case BuildingUse.Hut:
                    return Pick(FarmWalls, h);
                case BuildingUse.Church:
                case BuildingUse.Cathedral:
                case BuildingUse.Chapel:
                case BuildingUse.Religious:
                case BuildingUse.Castle:
                case BuildingUse.Tower:
                    return Pick(new uint[] { 0xD9C9A8, 0xE2D3B3, 0xCDBA96 }, h);
                case BuildingUse.Greenhouse:
                    return 0xDDE8E4;
            }
            bool modern = b.Year >= 1950 || b.Levels >= 5 || b.Use == BuildingUse.Apartments || b.Use == BuildingUse.Office;
            if (b.Year > 0 && b.Year < 1940) modern = false;
            return modern ? Pick(ModernWalls, h) : Pick(HistoricWalls, h);
        }

        public static uint RoofColor(BuildingInfo b, bool flat)
        {
            if ((b.RoofColor & 0x1000000) != 0) return b.RoofColor & 0xFFFFFF;
            uint h = Hash(b.OsmId, 23);
            if (b.Use == BuildingUse.Greenhouse) return 0xE6EFEC;
            return flat ? Pick(FlatRoofs, h) : Pick(RoofTiles, h);
        }

        private static uint Pick(uint[] list, uint h) => list[h % (uint)list.Length];
    }
}
