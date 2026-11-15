using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Glass
{
    /// <summary>
    /// Superficie "liquid glass" in stile iOS 26: mostra lo sfondo sfocato che sta dietro di lei,
    /// con una tinta, un bordo luminoso che sfuma dall'alto al basso e un'ombra morbida.
    /// Si usa come primo figlio (classe USS "glass-fill") di bottoni, navbar e pannelli:
    /// forma (border-radius) e colori arrivano dall'USS, così gli stati :hover/:active/:focus restano nei fogli di stile.
    /// </summary>
    [UxmlElement]
    public partial class GlassSurface : VisualElement
    {
        private static readonly CustomStyleProperty<Color> TintTopProp = new CustomStyleProperty<Color>("--glass-tint-top");
        private static readonly CustomStyleProperty<Color> TintBottomProp = new CustomStyleProperty<Color>("--glass-tint-bottom");
        private static readonly CustomStyleProperty<Color> RimTopProp = new CustomStyleProperty<Color>("--glass-rim-top");
        private static readonly CustomStyleProperty<Color> RimBottomProp = new CustomStyleProperty<Color>("--glass-rim-bottom");
        private static readonly CustomStyleProperty<Color> ShadowProp = new CustomStyleProperty<Color>("--glass-shadow");
        private static readonly CustomStyleProperty<float> RimWidthProp = new CustomStyleProperty<float>("--glass-rim-width");
        private static readonly CustomStyleProperty<float> ShadowSpreadProp = new CustomStyleProperty<float>("--glass-shadow-spread");
        private static readonly CustomStyleProperty<float> ShadowOffsetProp = new CustomStyleProperty<float>("--glass-shadow-offset");

        private Color _tintTop = new Color(1f, 1f, 1f, 0.14f);
        private Color _tintBottom = new Color(1f, 1f, 1f, 0.05f);
        private Color _rimTop = new Color(1f, 1f, 1f, 0.55f);
        private Color _rimBottom = new Color(1f, 1f, 1f, 0.10f);
        private Color _shadow = new Color(0f, 0f, 0.05f, 0.28f);
        private float _rimWidth = 1.2f;
        private float _shadowSpread = 14f;
        private float _shadowOffset = 6f;

        private readonly List<Vector2> _outer = new List<Vector2>(80);
        private readonly List<Vector2> _inner = new List<Vector2>(80);
        private readonly List<Vertex> _vertices = new List<Vertex>(512);
        private readonly List<ushort> _indices = new List<ushort>(1536);
        private Matrix4x4 _lastTransform;
        private Rect _lastBound;

        public GlassSurface()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("glass-fill");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<AttachToPanelEvent>(_ => GlassBackdrop.Changed += MarkDirtyRepaint);
            RegisterCallback<DetachFromPanelEvent>(_ => GlassBackdrop.Changed -= MarkDirtyRepaint);
            // il vetro campiona lo sfondo in coordinate schermo: se l'elemento si sposta (animazioni) va ridisegnato
            schedule.Execute(CheckMoved).Every(16);
        }

        private void CheckMoved()
        {
            if (panel == null)
                return;
            var t = worldTransform;
            var b = worldBound;
            if (t != _lastTransform || b != _lastBound)
            {
                _lastTransform = t;
                _lastBound = b;
                MarkDirtyRepaint();
            }
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            var s = evt.customStyle;
            if (s.TryGetValue(TintTopProp, out var c)) _tintTop = c;
            if (s.TryGetValue(TintBottomProp, out c)) _tintBottom = c;
            if (s.TryGetValue(RimTopProp, out c)) _rimTop = c;
            if (s.TryGetValue(RimBottomProp, out c)) _rimBottom = c;
            if (s.TryGetValue(ShadowProp, out c)) _shadow = c;
            if (s.TryGetValue(RimWidthProp, out var f)) _rimWidth = f;
            if (s.TryGetValue(ShadowSpreadProp, out f)) _shadowSpread = f;
            if (s.TryGetValue(ShadowOffsetProp, out f)) _shadowOffset = f;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext mgc)
        {
            GlassBackdrop.EnsureTextures();
            var rect = new Rect(0f, 0f, layout.width, layout.height);
            if (rect.width < 1f || rect.height < 1f)
                return;

            var rs = resolvedStyle;
            var radii = new Vector4(rs.borderTopLeftRadius, rs.borderTopRightRadius, rs.borderBottomRightRadius, rs.borderBottomLeftRadius);
            var segments = RoundedRectMesh.SegmentsFor(Mathf.Max(Mathf.Max(radii.x, radii.y), Mathf.Max(radii.z, radii.w)));
            var center = rect.center;
            var h = rect.height;

            // 1) ombra morbida: anelli concentrici sempre più grandi e trasparenti, spostati verso il basso
            if (_shadow.a > 0.001f && _shadowSpread > 0.5f)
            {
                _vertices.Clear();
                _indices.Clear();
                const int layers = 6;
                for (var i = layers; i >= 1; i--)
                {
                    var grow = _shadowSpread * i / layers;
                    var shadowRect = new Rect(rect.x - grow, rect.y - grow + _shadowOffset, rect.width + grow * 2f, rect.height + grow * 2f);
                    var grown = new Vector4(radii.x + grow, radii.y + grow, radii.z + grow, radii.w + grow);
                    RoundedRectMesh.Perimeter(_outer, shadowRect, grown, 0f, segments);
                    var alpha = _shadow.a * (1f - (float)(i - 1) / layers) / layers * 1.6f;
                    var col = new Color(_shadow.r, _shadow.g, _shadow.b, alpha);
                    RoundedRectMesh.Fan(_vertices, _indices, _outer, shadowRect.center, _ => col, _ => Vector2.zero);
                }
                Submit(mgc, null);
            }

            // 2) lo sfondo sfocato visto "attraverso" il vetro
            RoundedRectMesh.Perimeter(_outer, rect, radii, 0f, segments);
            _vertices.Clear();
            _indices.Clear();
            var mesh = mgc.Allocate(_outer.Count + 1, _outer.Count * 3, GlassBackdrop.Blurred);
            RoundedRectMesh.Fan(_vertices, _indices, _outer, center, _ => Color.white, p => GlassBackdrop.UvAt(this.LocalToWorld(p)));
            mesh.SetAllVertices(_vertices.ToArray());
            mesh.SetAllIndices(_indices.ToArray());

            // 3) tinta del vetro, più chiara in alto
            _vertices.Clear();
            _indices.Clear();
            RoundedRectMesh.Fan(_vertices, _indices, _outer, center, p => Color.Lerp(_tintTop, _tintBottom, p.y / h), _ => Vector2.zero);
            Submit(mgc, null);

            // 4) bordo luminoso (riflesso) che sfuma verso il basso
            if (_rimWidth > 0.01f)
            {
                RoundedRectMesh.Perimeter(_inner, rect, radii, _rimWidth, segments);
                _vertices.Clear();
                _indices.Clear();
                RoundedRectMesh.Ring(_vertices, _indices, _outer, _inner, p => Color.Lerp(_rimTop, _rimBottom, Mathf.SmoothStep(0f, 1f, p.y / h)));
                Submit(mgc, null);
            }
        }

        private void Submit(MeshGenerationContext mgc, Texture texture)
        {
            if (_vertices.Count == 0)
                return;
            var mesh = mgc.Allocate(_vertices.Count, _indices.Count, texture);
            mesh.SetAllVertices(_vertices.ToArray());
            mesh.SetAllIndices(_indices.ToArray());
        }
    }
}
