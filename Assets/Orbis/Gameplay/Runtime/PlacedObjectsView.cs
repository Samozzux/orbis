using System;
using System.Collections.Generic;
using Orbis.Core.Game;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Meshing;
using Orbis.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Disegna gli elementi piazzati dal giocatore (modelli procedurali di <see cref="ObjectMesher"/>).
    /// Durante i lavori l'edificio cresce dal suolo (cantiere); si ricostruisce solo quando cambia qualcosa.
    /// </summary>
    public sealed class PlacedObjectsView : IDisposable
    {
        private sealed class Entry
        {
            public GameObject Root;
            public string Signature;
            public Mesh Mesh;
        }

        private readonly WorldStreamer _world;
        private readonly Catalog _catalog;
        private readonly Transform _parent;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly ObjectMesher _mesher = new ObjectMesher();
        private readonly List<PlantInstance> _plants = new List<PlantInstance>();
        private readonly MeshBuffer _mb = new MeshBuffer();

        public int Count => _entries.Count;

        public PlacedObjectsView(WorldStreamer world, Catalog catalog, Transform parent)
        {
            _world = world;
            _catalog = catalog;
            _parent = parent;
        }

        public GameObject RootOf(int objectId) => _entries.TryGetValue(objectId, out var e) ? e.Root : null;

        /// <summary>Allinea i GameObject allo stato della partita.</summary>
        public void Sync(GameState state)
        {
            var seen = new HashSet<int>();
            foreach (var o in state.Objects.Values)
            {
                seen.Add(o.Id);
                string sig = o.ItemKey + "|" + o.Level + "|" + o.Variant + "|" + o.Lot + "|" + o.HalfX + "|" + o.HalfY + "|" + o.Rotation;
                if (!_entries.TryGetValue(o.Id, out var e) || e.Signature != sig || e.Root == null)
                {
                    if (e != null) Destroy(e);
                    e = Build(o, sig);
                    if (e == null) continue; // tile non ancora caricato: si riprova al prossimo Sync
                    _entries[o.Id] = e;
                }
                UpdateConstruction(e, o, state.Now);
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

        /// <summary>Aggiorna solo l'avanzamento dei cantieri (ogni frame, economico).</summary>
        public void UpdateConstructions(GameState state)
        {
            foreach (var kv in _entries)
                if (state.Objects.TryGetValue(kv.Key, out var o)) UpdateConstruction(kv.Value, o, state.Now);
        }

        private static void UpdateConstruction(Entry e, PlacedObject o, DateTime now)
        {
            float progress = 1f;
            if (now < o.ReadyAt && o.ReadyAt > o.BuiltAt)
                progress = (float)((now - o.BuiltAt).TotalSeconds / (o.ReadyAt - o.BuiltAt).TotalSeconds);
            float y = Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(progress));
            var s = e.Root.transform.localScale;
            if (Math.Abs(s.y - y) > 1e-4f) e.Root.transform.localScale = new Vector3(1, y, 1);
        }

        private Entry Build(PlacedObject o, string sig)
        {
            var item = _catalog.Get(o.ItemKey);
            if (item == null || _world.Region == null) return null;
            var id = new TileId(_world.Region.Zoom, o.Lot.TileX, o.Lot.TileY);
            if (!_world.Tiles.TryGetValue(id, out var view) || view.Data == null) return null;
            var lot = Placement.FindLot(view.Data, o.Lot.LotId);
            if (lot == null) return null;
            var f = Placement.Footprint(lot, item, o.HalfX, o.HalfY, o.Rotation);
            _mb.Clear();
            _plants.Clear();
            var corner = new Orbis.Core.World.Geometry.Vec2(f[0], f[1]);
            _mesher.Build(item, o.Level, o.Variant, corner, lot.GridAngle, o.Rotation, view.Data.Terrain.HeightAt, _mb, _plants, o.Id);
            float ground = view.Data.Terrain.HeightAt((f[0] + f[4]) / 2, (f[1] + f[5]) / 2);

            // perno sul terreno: la crescita del cantiere parte dal suolo
            var root = new GameObject("Elemento " + o.Id + " " + item.Key);
            root.transform.SetParent(_parent, false);
            root.transform.position = new Vector3(view.Origin.x, ground, view.Origin.z);
            var e = new Entry { Root = root, Signature = sig };
            if (!_mb.IsEmpty)
            {
                var go = new GameObject("Modello");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0, -ground, 0);
                e.Mesh = MeshUpload.ToMesh(_mb, item.Key);
                go.AddComponent<MeshFilter>().sharedMesh = e.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _world.Materials.Building;
                WorldMaterials.Configure(mr, true);
            }
            foreach (var p in _plants)
            {
                var go = new GameObject("Pianta");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(p.X, p.Z - ground, p.Y);
                go.transform.localRotation = Quaternion.Euler(0, -p.Rotation * Mathf.Rad2Deg, 0);
                go.transform.localScale = p.Kind == PlantKind.VineRow ? new Vector3(p.Length, 1, 1) : Vector3.one * p.Scale;
                go.AddComponent<MeshFilter>().sharedMesh = _world.Vegetation.MeshOf(p.Kind);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _world.Vegetation.Material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.lightProbeUsage = LightProbeUsage.Off;
            }
            return e;
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
