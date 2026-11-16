using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Mare alla quota 0 m: un grande piano che segue la telecamera (a passi, per non far "scorrere" le onde).
    /// Il terreno sotto il livello del mare (batimetria dei Terrain Tiles) resta coperto dall'acqua.
    /// </summary>
    public sealed class SeaPlane : MonoBehaviour
    {
        public float Size = 120000f;
        public Transform Follow;

        public static SeaPlane Create(Transform parent, Material material)
        {
            var go = new GameObject("Mare");
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "Mare" };
            const int n = 16; // griglia per una nebbia più uniforme sui vertici lontani
            var v = new Vector3[(n + 1) * (n + 1)];
            var tris = new int[n * n * 6];
            for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
                v[y * (n + 1) + x] = new Vector3(x / (float)n - 0.5f, 0, y / (float)n - 0.5f);
            int t = 0;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * (n + 1) + x;
                tris[t++] = i;
                tris[t++] = i + n + 1;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n + 1;
                tris[t++] = i + n + 2;
            }
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1, 0.1f, 1));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            WorldMaterials.Configure(mr, false, true);
            var sea = go.AddComponent<SeaPlane>();
            go.transform.localScale = new Vector3(sea.Size, 1, sea.Size);
            return sea;
        }

        private void LateUpdate()
        {
            if (Follow == null) return;
            var p = Follow.position;
            const float snap = 500f;
            transform.position = new Vector3(Mathf.Round(p.x / snap) * snap, 0, Mathf.Round(p.z / snap) * snap);
        }
    }
}
