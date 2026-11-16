using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Gameplay;
using Orbis.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Game
{
    /// <summary>
    /// Nomi delle strade reali sulla mappa (docs/02: "si riconoscono le strade reali"): le vie con nome più vicine
    /// al centro della vista, un'etichetta per nome, senza sovrapposizioni, solo quando si è abbastanza vicini.
    /// </summary>
    public sealed class StreetLabels
    {
        private const int MaxLabels = 12;
        private const float MaxViewDistance = 1600f;

        private struct Candidate
        {
            public string Name;
            public Vector3 Position;
            public float Rank;
        }

        private readonly WorldSession _session;
        private readonly VisualElement _layer;
        private readonly List<Label> _pool = new List<Label>();
        private readonly List<Candidate> _chosen = new List<Candidate>();
        private readonly Dictionary<string, Candidate> _best = new Dictionary<string, Candidate>();
        private readonly List<Rect> _placed = new List<Rect>();
        private float _nextPick;

        /// <summary>Nomi delle vie accesi o spenti (scelta del giocatore).</summary>
        public bool Visible = true;

        public StreetLabels(VisualElement screen, WorldSession session)
        {
            _session = session;
            _layer = new VisualElement { name = "street-labels", pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("street-labels");
            screen.Insert(0, _layer); // sotto l'HUD
            for (int i = 0; i < MaxLabels; i++)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList("street-label");
                l.style.display = DisplayStyle.None;
                _layer.Add(l);
                _pool.Add(l);
            }
        }

        public void Update()
        {
            var rig = _session.CameraRig;
            bool show = Visible && _session.State == WorldSession.Phase.Playing && rig.enabled && rig.Distance < MaxViewDistance &&
                        (_session.Globe == null || !_session.Globe.Active);
            if (!show)
            {
                foreach (var l in _pool) l.style.display = DisplayStyle.None;
                return;
            }
            if (Time.unscaledTime >= _nextPick)
            {
                _nextPick = Time.unscaledTime + 0.4f;
                Pick(rig.Target, rig.Distance);
            }
            Place(rig.Cam);
        }

        private void Pick(Vector3 focus, float distance)
        {
            _best.Clear();
            float radius = Mathf.Clamp(distance * 0.7f, 120f, 700f);
            foreach (var view in _session.World.Tiles.Values)
            {
                if (view.Data == null || view.Lod != TileLod.Full) continue;
                foreach (var r in view.Data.Roads)
                {
                    if (string.IsNullOrEmpty(r.Name) || r.Class > RoadClass.Pedestrian || r.HasFlag(RoadFlag.Tunnel)) continue;
                    int n = r.Points.Length / 2;
                    if (n < 2) continue;
                    // punto del tracciato più vicino al centro della vista
                    float bestD = float.MaxValue;
                    Vector3 bestP = default;
                    for (int i = 0; i < n; i++)
                    {
                        var p = view.Origin + new Vector3(r.Points[2 * i], 0, r.Points[2 * i + 1]);
                        float d = (new Vector2(p.x - focus.x, p.z - focus.z)).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            bestP = p;
                        }
                    }
                    float dist = Mathf.Sqrt(bestD);
                    if (dist > radius) continue;
                    float rank = dist / (r.Class <= RoadClass.Secondary ? 1.6f : 1f);
                    if (_best.TryGetValue(r.Name, out var c) && c.Rank <= rank) continue;
                    float h = view.Data.Terrain.HeightAt(bestP.x - view.Origin.x, bestP.z - view.Origin.z);
                    _best[r.Name] = new Candidate { Name = r.Name, Position = new Vector3(bestP.x, h + 3f, bestP.z), Rank = rank };
                }
            }
            _chosen.Clear();
            _chosen.AddRange(_best.Values);
            _chosen.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        }

        private void Place(Camera cam)
        {
            var panel = _layer.panel;
            if (panel == null || cam == null) return;
            _placed.Clear();
            int used = 0;
            foreach (var c in _chosen)
            {
                if (used >= _pool.Count) break;
                var sp = cam.WorldToScreenPoint(c.Position);
                if (sp.z <= 0 || sp.x < 0 || sp.y < 0 || sp.x > Screen.width || sp.y > Screen.height) continue;
                // pannello → coordinate locali dello strato (safe area e margini dell'HUD)
                var pp = _layer.WorldToLocal(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y)));
                // rettangolo un po' più largo dell'etichetta: le vie restano ben separate
                float w = c.Name.Length * 6.6f + 40f, h = 34f;
                var rect = new Rect(pp.x - w / 2, pp.y - h / 2, w, h);
                bool overlaps = false;
                foreach (var r in _placed)
                    if (r.Overlaps(rect)) { overlaps = true; break; }
                if (overlaps) continue;
                _placed.Add(rect);
                var l = _pool[used++];
                if (l.text != c.Name) l.text = c.Name;
                l.style.left = rect.x;
                l.style.top = rect.y;
                l.style.display = DisplayStyle.Flex;
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].style.display = DisplayStyle.None;
        }
    }
}
