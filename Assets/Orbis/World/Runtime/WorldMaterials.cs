using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    /// <summary>Materiali del mondo, creati dagli shader in Resources/OrbisShaders (inclusi così in tutte le build).</summary>
    public sealed class WorldMaterials
    {
        public static readonly int NightId = Shader.PropertyToID("_OrbisNight");
        public static readonly int WetId = Shader.PropertyToID("_OrbisWet");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int DetailId = Shader.PropertyToID("_Detail");
        private static readonly int DetailSizeId = Shader.PropertyToID("_DetailSize");

        public readonly Material Building;
        public readonly Material Road;
        public readonly Material Water;
        public readonly Material Plant;
        public readonly Material Highlight;
        private readonly Shader _terrain;

        public WorldMaterials()
        {
            Building = Create("Building");
            Road = Create("Road");
            Water = Create("Water");
            Plant = Create("Plant");
            Plant.enableInstancing = true;
            _terrain = Load("Terrain");
            Highlight = Create("Building");
            Highlight.name = "Orbis Highlight";
            Highlight.SetColor("_Glass", new Color(0.3f, 0.6f, 1f));
        }

        private static Shader Load(string name)
        {
            var s = Resources.Load<Shader>("OrbisShaders/" + name);
            if (s == null || !s.isSupported)
            {
                Debug.LogWarning("[Orbis] Shader " + name + " non disponibile: uso Simple Lit");
                s = Shader.Find("Universal Render Pipeline/Simple Lit");
            }
            return s;
        }

        private static Material Create(string name) => new Material(Load(name)) { name = "Orbis " + name };

        /// <summary>Materiale del terreno di un tile con la sua texture delle aree.</summary>
        public Material TerrainFor(Texture2D landuse, Texture2D detail)
        {
            var m = new Material(_terrain) { name = "Orbis Terrain" };
            m.SetTexture(MainTexId, landuse);
            if (detail != null)
            {
                m.SetTexture(DetailId, detail);
                m.SetFloat(DetailSizeId, detail.width);
            }
            return m;
        }

        /// <summary>Mappa dei materiali del terreno (suolo, direzione e fase delle file): letta senza filtro né mipmap.</summary>
        public static Texture2D DetailTexture(byte[] rgba, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                name = "Suolo",
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(false, true);
            return tex;
        }

        public static void SetNight(float night) => Shader.SetGlobalFloat(NightId, Mathf.Clamp01(night));
        public static void SetWet(float wet) => Shader.SetGlobalFloat(WetId, Mathf.Clamp01(wet));

        public static Texture2D LanduseTexture(byte[] rgba, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                name = "Landuse",
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true); // mipmap e niente copia in RAM
            return tex;
        }

        public static void Configure(MeshRenderer r, bool castShadows, bool receiveShadows = true)
        {
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = receiveShadows;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
        }
    }
}
