using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Profilo di qualità del mondo (docs/08 §4, "Auto"): distanze di dettaglio, piante e ombre in base al
    /// dispositivo. Desktop pieno, iPhone/Android medio-alto, web più leggero (soprattutto sui telefoni).
    /// </summary>
    public sealed class WorldQuality
    {
        public string Name;
        public float FullRadius;
        public float MediumRadius;
        public float FarRadius;
        public int MaxTiles;
        public float VegetationDistance;
        public bool PlantShadows;
        public float MaxShadowDistance;
        public float FrameBudgetMs = 6f;
        public bool PostProcessing = true;

        public static WorldQuality Auto(bool touchPrimary, int systemMemoryMb)
        {
            var p = Application.platform;
            bool web = p == RuntimePlatform.WebGLPlayer;
            bool mobile = p == RuntimePlatform.IPhonePlayer || p == RuntimePlatform.Android;
            if (web && touchPrimary)
                return new WorldQuality { Name = "web-telefono", FullRadius = 550, MediumRadius = 1400, FarRadius = 4200, MaxTiles = 80, VegetationDistance = 450, PlantShadows = false, MaxShadowDistance = 400, FrameBudgetMs = 5, PostProcessing = false };
            if (web)
                return new WorldQuality { Name = "web", FullRadius = 750, MediumRadius = 2000, FarRadius = 6000, MaxTiles = 130, VegetationDistance = 750, PlantShadows = false, MaxShadowDistance = 700, FrameBudgetMs = 7 };
            if (mobile && systemMemoryMb > 0 && systemMemoryMb < 5000)
                return new WorldQuality { Name = "mobile-basso", FullRadius = 650, MediumRadius = 1800, FarRadius = 5000, MaxTiles = 110, VegetationDistance = 600, PlantShadows = false, MaxShadowDistance = 500, PostProcessing = false };
            if (mobile)
                return new WorldQuality { Name = "mobile", FullRadius = 900, MediumRadius = 2500, FarRadius = 7000, MaxTiles = 170, VegetationDistance = 1100, PlantShadows = true, MaxShadowDistance = 900 };
            return new WorldQuality { Name = "desktop", FullRadius = 1100, MediumRadius = 3200, FarRadius = 9000, MaxTiles = 260, VegetationDistance = 1800, PlantShadows = true, MaxShadowDistance = 1400 };
        }

        public void ApplyTo(WorldStreamer world)
        {
            world.FullRadius = FullRadius;
            world.MediumRadius = MediumRadius;
            world.FarRadius = FarRadius;
            world.MaxTiles = MaxTiles;
            world.Quality = this;
        }
    }
}
