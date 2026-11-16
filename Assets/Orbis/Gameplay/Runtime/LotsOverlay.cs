using System;
using System.Collections.Generic;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Lots;
using Orbis.Core.World.Meshing;
using Orbis.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Vista di controllo "Lotti": mostra attorno al centro della vista la griglia da 1 m dei lotti
    /// (isolati tra le strade reali) con le celle già occupate dagli edifici reali in rosso.
    /// </summary>
    public sealed class LotsOverlay : IDisposable
    {
        private const float Radius = 160f;
        private const float MaxViewDistance = 1500f;

        private readonly WorldSession _session;
        private readonly Dictionary<TileId, LotGrid[]> _grids = new Dictionary<TileId, LotGrid[]>();
        private readonly GameObject _root;
        private readonly Material _material;
        private readonly List<GameObject> _pieces = new List<GameObject>();
        private Vector3 _builtAt = new Vector3(float.MaxValue, 0, 0);
        private bool _visible;

        public int CellsShown { get; private set; }

        public LotsOverlay(WorldSession session)
        {
            _session = session;
            _root = new GameObject("Lotti");
            _root.transform.SetParent(session.transform, false);
            var shader = Resources.Load<Shader>("OrbisShaders/Overlay");
            _material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
            session.World.TileUnloaded += id => _grids.Remove(id);
            session.World.TileLoaded += v =>
            {
                _grids.Remove(v.Id);
                _builtAt = new Vector3(float.MaxValue, 0, 0);
            };
            _root.AddComponent<Updater>().Tick = Update;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            _root.SetActive(visible);
            _builtAt = new Vector3(float.MaxValue, 0, 0);
            if (!visible) Clear();
        }

        private void Update()
        {
            if (!_visible || _session.State != WorldSession.Phase.Playing) return;
            var focus = _session.CameraRig.Target;
            if (_session.CameraRig.Distance > MaxViewDistance)
            {
                if (_pieces.Count > 0) Clear();
                _builtAt = new Vector3(float.MaxValue, 0, 0);
                return;
            }
            if ((new Vector2(focus.x, focus.z) - new Vector2(_builtAt.x, _builtAt.z)).magnitude < 40f) return;
            _builtAt = focus;
            Rebuild(focus);
        }

        private void Rebuild(Vector3 focus)
        {
            Clear();
            CellsShown = 0;
            foreach (var view in _session.World.Tiles.Values)
            {
                if (view.Data == null || view.Data.Lots.Length == 0) continue;
                float lx = focus.x - view.Origin.x, lz = focus.z - view.Origin.z;
                float size = view.Data.Size;
                if (lx < -Radius || lz < -Radius || lx > size + Radius || lz > size + Radius) continue;
                if (!_grids.TryGetValue(view.Id, out var grids))
                {
                    grids = new LotGrid[view.Data.Lots.Length];
                    _grids[view.Id] = grids;
                }
                FillNear(view, grids, lx, lz);
                var mb = new MeshBuffer();
                CellsShown += LotOverlayMesher.Build(view.Data, grids, lx, lz, Radius, mb);
                if (mb.IsEmpty) continue;
                var go = new GameObject("Lotti " + view.Id);
                go.transform.SetParent(_root.transform, false);
                go.transform.position = view.Origin;
                go.AddComponent<MeshFilter>().sharedMesh = MeshUpload.ToMesh(mb, "Lotti");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _pieces.Add(go);
            }
        }

        /// <summary>Griglie (con gli edifici reali) solo per i lotti abbastanza vicini: i lotti di campagna enormi si saltano.</summary>
        private static void FillNear(TileView view, LotGrid[] grids, float lx, float lz)
        {
            var data = view.Data;
            float reach = Radius * 1.5f;
            for (int i = 0; i < data.Lots.Length; i++)
            {
                if (grids[i] != null) continue;
                var lot = data.Lots[i];
                var b = lot.Bounds;
                if (b.MaxX < lx - reach || b.MinX > lx + reach || b.MaxY < lz - reach || b.MinY > lz + reach) continue;
                if (b.Width * b.Height > 250000f) continue;
                var g = new LotGrid(lot);
                for (int k = 0; k < data.Buildings.Length; k++)
                    if (data.Buildings[k].Bounds.Intersects(b)) g.Occupy(data.Buildings[k].Outer, k);
                grids[i] = g;
            }
        }

        private void Clear()
        {
            foreach (var go in _pieces)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
                UnityEngine.Object.Destroy(go);
            }
            _pieces.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }

        private sealed class Updater : MonoBehaviour
        {
            public Action Tick;
            private void Update() => Tick?.Invoke();
        }
    }
}
