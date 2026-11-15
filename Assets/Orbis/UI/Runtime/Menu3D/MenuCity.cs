using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.UI.Menu3D
{
    /// <summary>Materiali della città del menu (indice nell'array <see cref="MenuCity"/>.materials).</summary>
    public enum MenuCityMaterial
    {
        Grass, GrassDark, FieldYellow, FieldGreen, Earth, Rock, Road,
        WallCream, WallOchre, WallSalmon, WallWhite, Roof, RoofDark, Stone,
        Tree, TreeLight, Trunk, Cloud, CarRed, CarBlue, CarYellow, Pitch, Stands,
    }

    /// <summary>
    /// Mini-città low-poly su una collina (ispirata a Chieti), generata da codice e sempre uguale (seed fisso):
    /// fa da sfondo vivo a logo, caricamento e menu. Le auto girano sulla strada ad anello, le nuvole passano.
    /// </summary>
    public sealed class MenuCity : MonoBehaviour
    {
        public const int MaterialCount = 23;

        private const float IslandRadius = 34f;
        private const float RingRoadRadius = 19.5f;
        private const float TownRadius = 17f;
        private const int Seed = 1907;

        [SerializeField] private Material[] materials = new Material[MaterialCount];

        private readonly List<Car> _cars = new List<Car>();
        private readonly List<Transform> _clouds = new List<Transform>();
        private System.Random _rng;

        private struct Car
        {
            public Transform Transform;
            public float Angle;
            public float Speed;
            public float Lane;
        }

        public bool IsBuilt { get; private set; }

        /// <summary>Altezza del terreno (la stessa funzione usata per la mesh).</summary>
        public static float HeightAt(float x, float z)
        {
            var r = Mathf.Sqrt(x * x + z * z);
            var hill = 7.5f * Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / 21f)), 1.15f);
            var waves = (Mathf.PerlinNoise(x * 0.07f + 13.1f, z * 0.07f + 7.7f) - 0.5f) * 1.4f;
            var plateau = Mathf.Lerp(0.6f, 0f, Mathf.Clamp01((r - 24f) / 10f));
            return hill + waves * Mathf.Clamp01(r / 8f) + plateau;
        }

        /// <summary>Costruisce la città a passi (per non bloccare un frame): <paramref name="progress"/> riceve 0–1.</summary>
        public IEnumerator Build(Action<float> progress)
        {
            if (IsBuilt)
                yield break;
            _rng = new System.Random(Seed);
            var b = new LowPolyMeshBuilder();

            BuildTerrain(b);
            progress?.Invoke(0.2f);
            yield return null;
            BuildRoad(b);
            BuildCathedral(b);
            BuildStadium(b);
            BuildTown(b);
            progress?.Invoke(0.5f);
            yield return null;
            BuildTrees(b);
            BuildOliveGroves(b);
            progress?.Invoke(0.7f);
            yield return null;

            CreateRenderer("Città", b, castShadows: true);
            BuildCars();
            BuildClouds();
            progress?.Invoke(1f);
            IsBuilt = true;
        }

        private void Update()
        {
            if (!IsBuilt)
                return;
            var dt = Time.deltaTime;
            for (var i = 0; i < _cars.Count; i++)
            {
                var car = _cars[i];
                car.Angle += car.Speed * dt;
                var r = RingRoadRadius + car.Lane;
                var x = Mathf.Cos(car.Angle) * r;
                var z = Mathf.Sin(car.Angle) * r;
                var t = car.Transform;
                t.localPosition = new Vector3(x, HeightAt(x, z) + 0.12f, z);
                // direzione di marcia tangente all'anello
                var tangent = new Vector3(-Mathf.Sin(car.Angle), 0f, Mathf.Cos(car.Angle)) * Mathf.Sign(car.Speed);
                t.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                _cars[i] = car;
            }
            var cloudSpin = Quaternion.Euler(0f, 1.6f * dt, 0f);
            foreach (var c in _clouds)
                c.localPosition = cloudSpin * c.localPosition;
        }

        // ---------- terreno ----------

        private void BuildTerrain(LowPolyMeshBuilder b)
        {
            const int rings = 15;
            const int segments = 56;
            var grid = new Vector3[rings + 1, segments];
            for (var i = 0; i <= rings; i++)
            for (var j = 0; j < segments; j++)
            {
                var r = IslandRadius * i / rings;
                // piccolo disordine per un aspetto più naturale (non sul centro e sul bordo)
                var jitter = i > 0 && i < rings ? ((float)_rng.NextDouble() - 0.5f) * 0.9f : 0f;
                var a = (j + (i % 2) * 0.5f) * Mathf.PI * 2f / segments;
                var x = Mathf.Cos(a) * (r + jitter);
                var z = Mathf.Sin(a) * (r + jitter);
                grid[i, j] = new Vector3(x, HeightAt(x, z), z);
            }

            var center = new Vector3(0f, HeightAt(0f, 0f), 0f);
            for (var j = 0; j < segments; j++)
                b.Triangle((int)MenuCityMaterial.Grass, center, grid[1, (j + 1) % segments], grid[1, j]);

            for (var i = 1; i < rings; i++)
            for (var j = 0; j < segments; j++)
            {
                var j1 = (j + 1) % segments;
                var a = grid[i, j]; var bb = grid[i, j1]; var c = grid[i + 1, j1]; var d = grid[i + 1, j];
                var mid = (a + c) * 0.5f;
                var mat = TerrainMaterial(mid);
                b.Triangle(mat, a, bb, c);
                b.Triangle(mat, a, c, d);
            }

            // bordo dell'isola che scende e roccia sotto
            var bottom = new Vector3(0f, -16f, 0f);
            for (var j = 0; j < segments; j++)
            {
                var j1 = (j + 1) % segments;
                var top0 = grid[rings, j];
                var top1 = grid[rings, j1];
                var low0 = new Vector3(top0.x * 0.94f, -3.2f + (float)_rng.NextDouble() * 0.6f, top0.z * 0.94f);
                var low1 = new Vector3(top1.x * 0.94f, -3.2f, top1.z * 0.94f);
                b.Quad((int)MenuCityMaterial.Earth, top0, top1, low1, low0);
                b.Triangle((int)MenuCityMaterial.Rock, low0, low1, bottom + new Vector3(0f, (float)_rng.NextDouble() * 3f, 0f));
            }
        }

        private int TerrainMaterial(Vector3 p)
        {
            var r = new Vector2(p.x, p.z).magnitude;
            if (r < TownRadius + 1f)
                return (int)MenuCityMaterial.Grass;
            // campi a toppe: settori angolari × anelli
            var sector = Mathf.FloorToInt((Mathf.Atan2(p.z, p.x) + Mathf.PI) / (Mathf.PI * 2f) * 11f);
            var ring = Mathf.FloorToInt(r / 5f);
            var h = (sector * 73856093) ^ (ring * 19349663);
            switch (Mathf.Abs(h) % 5)
            {
                case 0: return (int)MenuCityMaterial.FieldYellow;
                case 1: return (int)MenuCityMaterial.FieldGreen;
                case 2: return (int)MenuCityMaterial.GrassDark;
                default: return (int)MenuCityMaterial.Grass;
            }
        }

        // ---------- strada ad anello ----------

        private static void BuildRoad(LowPolyMeshBuilder b)
        {
            const int segments = 96;
            const float half = 0.95f;
            for (var j = 0; j < segments; j++)
            {
                var a0 = j * Mathf.PI * 2f / segments;
                var a1 = (j + 1) * Mathf.PI * 2f / segments;
                Vector3 P(float a, float r)
                {
                    var x = Mathf.Cos(a) * r;
                    var z = Mathf.Sin(a) * r;
                    return new Vector3(x, HeightAt(x, z) + 0.09f, z);
                }
                b.Quad((int)MenuCityMaterial.Road, P(a0, RingRoadRadius - half), P(a1, RingRoadRadius - half), P(a1, RingRoadRadius + half), P(a0, RingRoadRadius + half));
            }
        }

        // ---------- edifici ----------

        private static readonly int[] Walls =
        {
            (int)MenuCityMaterial.WallCream, (int)MenuCityMaterial.WallOchre,
            (int)MenuCityMaterial.WallSalmon, (int)MenuCityMaterial.WallWhite, (int)MenuCityMaterial.WallCream,
        };

        private void BuildTown(LowPolyMeshBuilder b)
        {
            const float cell = 3.1f;
            var n = Mathf.CeilToInt(TownRadius / cell);
            for (var gx = -n; gx <= n; gx++)
            for (var gz = -n; gz <= n; gz++)
            {
                var x = gx * cell + ((float)_rng.NextDouble() - 0.5f) * 0.8f;
                var z = gz * cell + ((float)_rng.NextDouble() - 0.5f) * 0.8f;
                var r = Mathf.Sqrt(x * x + z * z);
                if (r < 5.2f || r > TownRadius || _rng.NextDouble() > 0.86)
                    continue;
                // vicoli: due strade che attraversano il centro storico
                if (Mathf.Abs(x - z * 0.25f) < 1.1f || Mathf.Abs(z + x * 0.2f) < 1.0f)
                    continue;

                var w = 1.9f + (float)_rng.NextDouble() * 1.0f;
                var d = 1.9f + (float)_rng.NextDouble() * 1.0f;
                var floors = Mathf.Clamp(Mathf.RoundToInt(3.6f - r / 6f + (float)_rng.NextDouble() * 1.6f), 1, 5);
                var height = floors * 1.05f + 0.3f;
                var yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg + ((float)_rng.NextDouble() - 0.5f) * 14f;
                var ground = MinHeight(x, z, Mathf.Max(w, d) * 0.6f) - 0.5f;
                var wall = Walls[_rng.Next(Walls.Length)];
                b.Box(wall, new Vector3(x, ground, z), new Vector3(w, height + 0.5f, d), yaw);
                var roof = _rng.NextDouble() < 0.7 ? (int)MenuCityMaterial.Roof : (int)MenuCityMaterial.RoofDark;
                b.HipRoof(roof, new Vector3(x, ground + height + 0.5f, z), new Vector3(w, 0f, d), 0.75f + (float)_rng.NextDouble() * 0.35f, yaw);
            }
        }

        private static float MinHeight(float x, float z, float radius)
        {
            var h = HeightAt(x, z);
            h = Mathf.Min(h, HeightAt(x + radius, z));
            h = Mathf.Min(h, HeightAt(x - radius, z));
            h = Mathf.Min(h, HeightAt(x, z + radius));
            return Mathf.Min(h, HeightAt(x, z - radius));
        }

        /// <summary>Cattedrale con campanile in cima alla collina (omaggio a San Giustino).</summary>
        private static void BuildCathedral(LowPolyMeshBuilder b)
        {
            var g = MinHeight(0f, 0f, 5f) - 0.3f;
            b.Cylinder((int)MenuCityMaterial.Stone, new Vector3(0f, g - 0.2f, 0f), 5.4f, 5.4f, 0.5f, 18);
            var nave = new Vector3(0.6f, g + 0.3f, 0.4f);
            b.Box((int)MenuCityMaterial.Stone, nave, new Vector3(6.2f, 3.4f, 3.2f), 18f);
            b.HipRoof((int)MenuCityMaterial.Roof, nave + Vector3.up * 3.4f, new Vector3(6.2f, 0f, 3.2f), 1.5f, 18f, 0.2f);
            var towerBase = new Vector3(-3.0f, g + 0.3f, -1.4f);
            b.Box((int)MenuCityMaterial.Stone, towerBase, new Vector3(1.9f, 8.6f, 1.9f), 18f);
            b.Box((int)MenuCityMaterial.WallWhite, towerBase + Vector3.up * 8.6f, new Vector3(2.2f, 0.35f, 2.2f), 18f);
            b.Pyramid((int)MenuCityMaterial.RoofDark, towerBase + Vector3.up * 8.95f, 2.0f, 2.6f, 18f);
        }

        private static void BuildStadium(LowPolyMeshBuilder b)
        {
            var c = new Vector3(Mathf.Cos(0.75f) * 26f, 0f, Mathf.Sin(0.75f) * 26f);
            c.y = MinHeight(c.x, c.z, 5f) - 0.4f;
            const int parts = 18;
            for (var i = 0; i < parts; i++)
            {
                var a = i * Mathf.PI * 2f / parts;
                var p = c + new Vector3(Mathf.Cos(a) * 4.6f, 0f, Mathf.Sin(a) * 3.4f);
                var yaw = -a * Mathf.Rad2Deg + 90f;
                b.Box((int)MenuCityMaterial.Stands, p, new Vector3(2.0f, 1.5f, 1.1f), yaw);
            }
            b.Box((int)MenuCityMaterial.Pitch, c, new Vector3(5.6f, 0.75f, 3.4f), 0f);
        }

        // ---------- vegetazione ----------

        private void BuildTrees(LowPolyMeshBuilder b)
        {
            var placed = 0;
            var attempts = 0;
            while (placed < 150 && attempts++ < 2000)
            {
                var a = (float)_rng.NextDouble() * Mathf.PI * 2f;
                var r = 5.5f + (float)_rng.NextDouble() * (IslandRadius - 7f);
                var x = Mathf.Cos(a) * r;
                var z = Mathf.Sin(a) * r;
                if (Mathf.Abs(r - RingRoadRadius) < 1.6f) continue;               // non sulla strada
                if (r < TownRadius && _rng.NextDouble() < 0.75) continue;         // pochi alberi in città
                if (Vector2.Distance(new Vector2(x, z), new Vector2(Mathf.Cos(0.75f) * 26f, Mathf.Sin(0.75f) * 26f)) < 6.5f) continue;
                var y = HeightAt(x, z) - 0.1f;
                var s = 0.8f + (float)_rng.NextDouble() * 0.6f;
                b.Cylinder((int)MenuCityMaterial.Trunk, new Vector3(x, y, z), 0.13f * s, 0.1f * s, 0.7f * s, 5);
                if (_rng.NextDouble() < 0.45)
                {
                    b.Cylinder((int)MenuCityMaterial.Tree, new Vector3(x, y + 0.5f * s, z), 0.75f * s, 0f, 2.2f * s, 7, cap: false);
                }
                else
                {
                    var mat = _rng.NextDouble() < 0.5 ? (int)MenuCityMaterial.TreeLight : (int)MenuCityMaterial.Tree;
                    b.Blob(mat, new Vector3(x, y + 1.1f * s, z), new Vector3(0.85f, 0.75f, 0.85f) * s, _rng.Next());
                }
                placed++;
            }
        }

        /// <summary>Uliveti a filari in alcuni campi (paesaggio abruzzese).</summary>
        private void BuildOliveGroves(LowPolyMeshBuilder b)
        {
            float[] angles = { 2.2f, 3.9f, 5.3f };
            foreach (var baseAngle in angles)
            {
                var cx = Mathf.Cos(baseAngle) * 27f;
                var cz = Mathf.Sin(baseAngle) * 27f;
                var yaw = baseAngle * Mathf.Rad2Deg;
                var q = Quaternion.Euler(0f, yaw, 0f);
                for (var i = -2; i <= 2; i++)
                for (var j = -3; j <= 3; j++)
                {
                    var p = new Vector3(cx, 0f, cz) + q * new Vector3(i * 1.5f, 0f, j * 1.5f);
                    p.y = HeightAt(p.x, p.z) - 0.05f;
                    b.Cylinder((int)MenuCityMaterial.Trunk, p, 0.08f, 0.07f, 0.45f, 4);
                    b.Blob((int)MenuCityMaterial.GrassDark, p + Vector3.up * 0.7f, new Vector3(0.5f, 0.38f, 0.5f), _rng.Next());
                }
            }
        }

        // ---------- elementi animati ----------

        private void BuildCars()
        {
            int[] colors = { (int)MenuCityMaterial.CarRed, (int)MenuCityMaterial.CarBlue, (int)MenuCityMaterial.CarYellow };
            var meshes = new Mesh[colors.Length];
            var order = new int[colors.Length][];
            for (var i = 0; i < colors.Length; i++)
            {
                var b = new LowPolyMeshBuilder();
                b.Box(colors[i], new Vector3(0f, 0f, 0f), new Vector3(0.55f, 0.32f, 1.0f), 0f);
                b.Box((int)MenuCityMaterial.WallWhite, new Vector3(0f, 0.32f, -0.05f), new Vector3(0.48f, 0.24f, 0.55f), 0f);
                meshes[i] = b.Build("Auto", out order[i]);
            }
            for (var i = 0; i < 14; i++)
            {
                var c = i % colors.Length;
                var go = new GameObject("Auto");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[c];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = Pick(order[c]);
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var clockwise = i % 2 == 0;
                _cars.Add(new Car
                {
                    Transform = go.transform,
                    Angle = i * 0.45f + (float)_rng.NextDouble() * 0.3f,
                    Speed = (clockwise ? 1f : -1f) * (0.11f + (float)_rng.NextDouble() * 0.05f),
                    Lane = clockwise ? 0.42f : -0.42f,
                });
            }
        }

        private void BuildClouds()
        {
            for (var i = 0; i < 7; i++)
            {
                var b = new LowPolyMeshBuilder();
                var puffs = 3 + _rng.Next(3);
                for (var p = 0; p < puffs; p++)
                {
                    var offset = new Vector3(p * 1.7f - puffs * 0.8f, (float)_rng.NextDouble() * 0.6f, ((float)_rng.NextDouble() - 0.5f) * 1.5f);
                    var size = 1.3f + (float)_rng.NextDouble() * 1.0f;
                    b.Blob((int)MenuCityMaterial.Cloud, offset, new Vector3(size, size * 0.7f, size), _rng.Next());
                }
                var a = i * Mathf.PI * 2f / 7f + (float)_rng.NextDouble() * 0.4f;
                var r = 24f + (float)_rng.NextDouble() * 18f;
                var go = CreateRenderer("Nuvola", b, castShadows: false);
                go.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 15f + (float)_rng.NextDouble() * 7f, Mathf.Sin(a) * r);
                go.transform.localRotation = Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f);
                _clouds.Add(go.transform);
            }
        }

        // ---------- utilità ----------

        private GameObject CreateRenderer(string name, LowPolyMeshBuilder b, bool castShadows)
        {
            var mesh = b.Build(name, out var order);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = Pick(order);
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        private Material[] Pick(int[] order)
        {
            var result = new Material[order.Length];
            for (var i = 0; i < order.Length; i++)
                result[i] = order[i] < materials.Length ? materials[order[i]] : null;
            return result;
        }
    }
}
