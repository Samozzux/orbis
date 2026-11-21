using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Orbis.Core.Game;
using Orbis.Core.Traffic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using Orbis.World;
using UnityEngine;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Disegna le strade del sindaco con lo stesso nastro delle strade vere (<see cref="RoadMesher"/>), appoggiate sul
    /// terreno dei tile caricati, un po' più in alto per coprire gli incroci. Si ricostruisce solo ciò che cambia.
    /// </summary>
    public sealed class CityRoadsView : IDisposable
    {
        private sealed class Entry
        {
            public GameObject Root;
            public Mesh Mesh;
            public string Signature;
        }

        private readonly WorldStreamer _world;
        private readonly TrafficBalance _balance;
        private readonly Transform _parent;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly RoadMesher _mesher = new RoadMesher();
        private readonly MeshBuffer _mb = new MeshBuffer();

        public int Count => _entries.Count;

        public CityRoadsView(WorldStreamer world, TrafficBalance balance, Transform parent)
        {
            _world = world;
            _balance = balance;
            _parent = parent;
        }

        public GameObject RootOf(int roadId) => _entries.TryGetValue(roadId, out var e) ? e.Root : null;

        public void Sync(GameState state)
        {
            var seen = new HashSet<int>();
            foreach (var r in state.Roads.Values)
            {
                seen.Add(r.Id);
                string sig = Signature(r);
                if (_entries.TryGetValue(r.Id, out var e) && e.Signature == sig && e.Root != null) continue;
                var built = Build(r, sig);
                if (built == null) continue; // terreno non ancora caricato: si riprova al prossimo Sync
                if (e != null) Destroy(e);
                _entries[r.Id] = built;
            }
            var gone = new List<int>();
            foreach (var kv in _entries)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                Destroy(_entries[id]);
                _entries.Remove(id);
            }
        }

        private static string Signature(CityRoad r)
        {
            var sb = new StringBuilder();
            sb.Append((int)r.Class).Append(r.Oneway).Append(r.Bridge ? 'p' : '-');
            foreach (var v in r.Points) sb.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private Entry Build(CityRoad r, string sig)
        {
            if (_world.Frame == null || r.Points.Length < 4) return null;
            // coordinate relative al primo punto: numeri piccoli nella mesh
            float ox = r.Points[0], oy = r.Points[1];
            for (int i = 0; i + 1 < r.Points.Length; i += 2)
                if (float.IsNaN(_world.HeightAt(r.Points[i], r.Points[i + 1]))) return null;
            var local = new float[r.Points.Length];
            for (int i = 0; i + 1 < local.Length; i += 2)
            {
                local[i] = r.Points[i] - ox;
                local[i + 1] = r.Points[i + 1] - oy;
            }
            var info = new RoadInfo
            {
                OsmId = -r.Id,
                Class = r.Class,
                Points = local,
                Width = Infrastructure.RoadWidth(_balance, r.Class),
                Lanes = _balance != null && _balance.Classes.TryGetValue(r.Class, out var spec) ? spec.Lanes * (r.Oneway > 0 ? 1 : 2) : 2,
                Oneway = r.Oneway,
                Flags = r.Bridge ? RoadFlag.Bridge : 0,
                // un nome fa disegnare i marciapiedi alle vie urbane, come quelli che il Core conta nello spazio occupato
                Name = Infrastructure.HasSidewalks(r.Class, r.Bridge) ? "sindaco" : "",
            };
            _mb.Clear();
            GroundSampler ground = (x, y) =>
            {
                float h = _world.HeightAt(x + ox, y + oy);
                return float.IsNaN(h) ? 0f : h + 0.03f; // sopra le strade vere che attraversa
            };
            _mesher.Build(info, ground, _mb);
            if (_mb.IsEmpty) return null;
            var root = new GameObject("Strada del sindaco " + r.Id);
            root.transform.SetParent(_parent, false);
            root.transform.position = new Vector3(ox, 0, oy);
            var mesh = MeshUpload.ToMesh(_mb, "Strada " + r.Id);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _world.Materials.Road;
            WorldMaterials.Configure(mr, false);
            return new Entry { Root = root, Mesh = mesh, Signature = sig };
        }

        private static void Destroy(Entry e)
        {
            if (e.Mesh != null) UnityEngine.Object.Destroy(e.Mesh);
            if (e.Root != null) UnityEngine.Object.Destroy(e.Root);
        }

        public void Dispose()
        {
            foreach (var e in _entries.Values) Destroy(e);
            _entries.Clear();
        }
    }
}
