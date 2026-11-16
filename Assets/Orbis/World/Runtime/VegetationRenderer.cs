using System.Collections.Generic;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    /// <summary>
    /// Disegna alberi, uliveti, filari di vite e cespugli con l'instancing (nessun GameObject per pianta).
    /// Le piante di ogni tile sono raggruppate per tipo in blocchi da 1023 matrici, con culling per tile e distanza.
    /// </summary>
    public sealed class VegetationRenderer
    {
        private const int Batch = 1023;

        private sealed class TilePlants
        {
            public Bounds Bounds;
            public readonly List<Matrix4x4[]>[] Batches = new List<Matrix4x4[]>[(int)PlantKind.Count];
            public readonly List<int>[] Counts = new List<int>[(int)PlantKind.Count];
            public int Total;
        }

        private readonly Dictionary<TileId, TilePlants> _tiles = new Dictionary<TileId, TilePlants>();
        private readonly Mesh[] _meshes;
        private readonly Material _material;
        private readonly Plane[] _planes = new Plane[6];

        /// <summary>Oltre questa distanza dalla telecamera le piante non si disegnano.</summary>
        public float MaxDistance = 1800f;
        public bool CastShadows = true;

        public int TotalInstances { get; private set; }

        public VegetationRenderer(Material material)
        {
            _material = material;
            _meshes = PlantMeshes.CreateAll();
        }

        public void Set(TileId id, Vector3 tileOrigin, List<PlantInstance> plants)
        {
            Remove(id);
            if (plants == null || plants.Count == 0) return;
            var tp = new TilePlants();
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int k = 0; k < tp.Batches.Length; k++)
            {
                tp.Batches[k] = new List<Matrix4x4[]>();
                tp.Counts[k] = new List<int>();
            }
            foreach (var p in plants)
            {
                int k = (int)p.Kind;
                var list = tp.Batches[k];
                var counts = tp.Counts[k];
                if (list.Count == 0 || counts[counts.Count - 1] == Batch)
                {
                    list.Add(new Matrix4x4[Batch]);
                    counts.Add(0);
                }
                var pos = tileOrigin + new Vector3(p.X, p.Z, p.Y);
                // rotazione attorno all'asse verticale (in Unity: antioraria vista dall'alto = -y)
                var rot = Quaternion.Euler(0, -p.Rotation * Mathf.Rad2Deg, 0);
                var scale = p.Kind == PlantKind.VineRow ? new Vector3(p.Length, 1, 1) : Vector3.one * p.Scale;
                list[list.Count - 1][counts[counts.Count - 1]] = Matrix4x4.TRS(pos, rot, scale);
                counts[counts.Count - 1]++;
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            tp.Bounds.SetMinMax(min - new Vector3(8, 1, 8), max + new Vector3(8, 16, 8));
            tp.Total = plants.Count;
            _tiles[id] = tp;
            TotalInstances += tp.Total;
        }

        public void Remove(TileId id)
        {
            if (_tiles.TryGetValue(id, out var tp))
            {
                TotalInstances -= tp.Total;
                _tiles.Remove(id);
            }
        }

        public void Draw(Camera camera)
        {
            if (_tiles.Count == 0 || camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            var camPos = camera.transform.position;
            var rp = new RenderParams(_material)
            {
                shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                layer = 0,
                lightProbeUsage = LightProbeUsage.Off,
            };
            foreach (var tp in _tiles.Values)
            {
                if (tp.Bounds.SqrDistance(camPos) > MaxDistance * MaxDistance) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, tp.Bounds)) continue;
                for (int k = 0; k < tp.Batches.Length; k++)
                {
                    var list = tp.Batches[k];
                    for (int b = 0; b < list.Count; b++)
                        Graphics.RenderMeshInstanced(rp, _meshes[k], 0, list[b], tp.Counts[k][b]);
                }
            }
        }

        public void Clear()
        {
            _tiles.Clear();
            TotalInstances = 0;
        }
    }
}
