using System.Collections.Generic;
using Orbis.Core.World.Meshing;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Scrive il nome vero dei locali sulle insegne (docs/08 §3): testo 3D col font del gioco, solo sulle insegne
    /// più vicine alla telecamera (al massimo <see cref="MaxLabels"/>, oggetti riusati). Da lontano resta il pannello.
    /// </summary>
    public sealed class SignLabels
    {
        public const int MaxLabels = 48;

        private sealed class Label
        {
            public GameObject Go;
            public TextMesh Text;
            public MeshRenderer Renderer;
            public object Key;
            public float Width, Height;
            public bool Fitted;
        }

        private readonly Transform _parent;
        private readonly Font _font;
        private readonly List<Label> _labels = new List<Label>();
        private readonly List<(float d2, TileView view, int index)> _near = new List<(float, TileView, int)>();
        private readonly HashSet<object> _wanted = new HashSet<object>();

        public float MaxDistance = 140f;

        public SignLabels(Transform parent)
        {
            _parent = parent;
            _font = Resources.Load<Font>("OrbisFonts/LilitaOne-Regular");
        }

        public int Visible
        {
            get
            {
                int n = 0;
                foreach (var l in _labels)
                    if (l.Go.activeSelf) n++;
                return n;
            }
        }

        public void Update(IEnumerable<TileView> views, Vector3 camera)
        {
            if (_font == null) return;
            _near.Clear();
            float max2 = MaxDistance * MaxDistance;
            foreach (var v in views)
            {
                if (v.Signs == null || v.Lod != TileLod.Full) continue;
                for (int i = 0; i < v.Signs.Count; i++)
                {
                    var s = v.Signs[i];
                    float dx = v.Origin.x + s.X - camera.x, dy = s.Z - camera.y, dz = v.Origin.z + s.Y - camera.z;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 < max2) _near.Add((d2, v, i));
                }
            }
            _near.Sort((a, b) => a.d2.CompareTo(b.d2));
            if (_near.Count > MaxLabels) _near.RemoveRange(MaxLabels, _near.Count - MaxLabels);

            // le etichette già assegnate restano dove sono; le altre si liberano
            _wanted.Clear();
            foreach (var n in _near) _wanted.Add(Key(n.view, n.index));
            foreach (var l in _labels)
                if (l.Key != null && !_wanted.Contains(l.Key))
                {
                    l.Key = null;
                    l.Go.SetActive(false);
                }
            foreach (var n in _near)
            {
                var key = Key(n.view, n.index);
                Label free = null;
                bool shown = false;
                foreach (var l in _labels)
                {
                    if (Equals(l.Key, key))
                    {
                        shown = true;
                        if (!l.Fitted) Fit(l);
                        break;
                    }
                    if (free == null && l.Key == null) free = l;
                }
                if (shown) continue;
                if (free == null)
                {
                    if (_labels.Count >= MaxLabels) continue;
                    free = Create();
                }
                Assign(free, key, n.view, n.view.Signs[n.index]);
            }
        }

        private static object Key(TileView v, int index) => (v.Id, index);

        private Label Create()
        {
            var go = new GameObject("Insegna");
            go.transform.SetParent(_parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.fontSize = 64;
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var l = new Label { Go = go, Text = tm, Renderer = mr };
            _labels.Add(l);
            return l;
        }

        private static void Assign(Label l, object key, TileView view, SignInfo s)
        {
            l.Key = key;
            l.Width = s.Width * 0.88f;
            l.Height = s.Height * 0.62f;
            l.Fitted = false;
            l.Text.text = s.Text;
            // testo chiaro sui fondi scuri, scuro su quelli chiari
            float lum = ((s.Background >> 16) & 0xFF) * 0.299f + ((s.Background >> 8) & 0xFF) * 0.587f + (s.Background & 0xFF) * 0.114f;
            l.Text.color = lum < 140 ? new Color(1f, 0.96f, 0.86f) : new Color(0.12f, 0.12f, 0.14f);
            var t = l.Go.transform;
            t.position = view.Origin + new Vector3(s.X + s.NormalX * 0.02f, s.Z, s.Y + s.NormalY * 0.02f);
            // si legge guardando la facciata da fuori: l'asse avanti del testo entra nel muro
            t.rotation = Quaternion.LookRotation(new Vector3(-s.NormalX, 0, -s.NormalY), Vector3.up);
            t.localScale = Vector3.one;
            // si mostra solo dopo averla misurata e scalata (al giro successivo, a scritta generata)
            l.Renderer.enabled = false;
            l.Go.SetActive(true);
        }

        /// <summary>Scala il testo per stare nel pannello (si misura dopo che il motore ha generato la scritta).</summary>
        private static void Fit(Label l)
        {
            var size = l.Renderer.localBounds.size;
            if (size.x < 1e-4f || size.y < 1e-4f) return;
            float k = Mathf.Min(l.Width / size.x, l.Height / size.y);
            l.Go.transform.localScale = new Vector3(k, k, k);
            l.Renderer.enabled = true;
            l.Fitted = true;
        }

        public void Dispose()
        {
            foreach (var l in _labels)
                if (l.Go != null) Object.Destroy(l.Go);
            _labels.Clear();
        }
    }
}
