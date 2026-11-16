using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>GameObject di un tile caricato: terreno, strade e blocchi di edifici.</summary>
    public sealed class TileView
    {
        public readonly TileId Id;
        public readonly GameObject Root;
        public TileData Data;
        public TileLod Lod;
        public Vector3 Origin;
        private Texture2D _landuse;
        private Material _terrainMaterial;

        public TileView(TileId id, Transform parent, Vector3 origin)
        {
            Id = id;
            Origin = origin;
            Root = new GameObject("Tile " + id);
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = origin;
        }

        /// <summary>Sostituisce il contenuto con il risultato di una costruzione (sul main thread).</summary>
        public void Apply(TileBuildResult r, WorldMaterials materials)
        {
            ClearChildren();
            Data = r.Data;
            Lod = r.Lod;
            _landuse = WorldMaterials.LanduseTexture(r.Landuse, r.LanduseSize);
            _terrainMaterial = materials.TerrainFor(_landuse);
            Add("Terreno", r.Terrain, _terrainMaterial, cast: false);
            if (r.Roads != null && !r.Roads.IsEmpty) Add("Strade", r.Roads, materials.Road, cast: false);
            for (int i = 0; i < r.Buildings.Length; i++)
            {
                var b = r.Buildings[i];
                if (b != null && !b.IsEmpty) Add("Edifici " + i, b, materials.Building, cast: true);
            }
        }

        private void Add(string name, Core.World.Meshing.MeshBuffer mb, Material material, bool cast)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshUpload.ToMesh(mb, Id + " " + name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            WorldMaterials.Configure(mr, cast);
        }

        private void ClearChildren()
        {
            for (int i = Root.transform.childCount - 1; i >= 0; i--)
            {
                var child = Root.transform.GetChild(i).gameObject;
                var mf = child.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
                Object.Destroy(child);
            }
            if (_landuse != null) Object.Destroy(_landuse);
            if (_terrainMaterial != null) Object.Destroy(_terrainMaterial);
        }

        public void Destroy()
        {
            ClearChildren();
            Object.Destroy(Root);
        }
    }
}
