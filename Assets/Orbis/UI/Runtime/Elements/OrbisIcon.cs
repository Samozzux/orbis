using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Elements
{
    public enum OrbisIconKind
    {
        None,
        Play,
        Plus,
        Friends,
        Settings,
        Info,
        Globe,
        Close,
        PhonePortrait,
        PhoneLandscape,
    }

    /// <summary>Icone lineari disegnate in vettoriale (niente file esterni), nel colore del testo (USS "color").</summary>
    [UxmlElement]
    public partial class OrbisIcon : VisualElement
    {
        private OrbisIconKind _icon;

        [UxmlAttribute]
        public OrbisIconKind icon
        {
            get => _icon;
            set { _icon = value; MarkDirtyRepaint(); }
        }

        public OrbisIcon()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("orbis-icon");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var s = Mathf.Min(contentRect.width, contentRect.height);
            if (s < 2f || _icon == OrbisIconKind.None)
                return;
            var p = mgc.painter2D;
            var o = contentRect.center - new Vector2(s, s) * 0.5f;
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s; // coordinate 0..1

            var color = resolvedStyle.color;
            p.strokeColor = color;
            p.fillColor = color;
            p.lineWidth = Mathf.Max(1.2f, s * 0.085f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            switch (_icon)
            {
                case OrbisIconKind.Play:
                    p.BeginPath();
                    p.MoveTo(P(0.32f, 0.2f));
                    p.LineTo(P(0.8f, 0.5f));
                    p.LineTo(P(0.32f, 0.8f));
                    p.ClosePath();
                    p.Fill();
                    p.Stroke();
                    break;

                case OrbisIconKind.Plus:
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.18f)); p.LineTo(P(0.5f, 0.82f));
                    p.MoveTo(P(0.18f, 0.5f)); p.LineTo(P(0.82f, 0.5f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Friends:
                    Circle(p, P(0.38f, 0.33f), s * 0.14f);
                    Arc(p, P(0.38f, 0.86f), s * 0.27f, 200f, 340f);
                    Circle(p, P(0.7f, 0.38f), s * 0.11f);
                    Arc(p, P(0.72f, 0.84f), s * 0.2f, 225f, 330f);
                    break;

                case OrbisIconKind.Settings:
                    // tre cursori
                    for (var i = 0; i < 3; i++)
                    {
                        var y = 0.24f + i * 0.26f;
                        var knob = i == 1 ? 0.66f : (i == 0 ? 0.34f : 0.46f);
                        p.BeginPath();
                        p.MoveTo(P(0.14f, y)); p.LineTo(P(0.86f, y));
                        p.Stroke();
                        p.BeginPath();
                        p.Arc(P(knob, y), s * 0.09f, Angle.Degrees(0f), Angle.Degrees(360f));
                        p.Fill();
                    }
                    break;

                case OrbisIconKind.Info:
                    Circle(p, P(0.5f, 0.5f), s * 0.38f);
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.46f)); p.LineTo(P(0.5f, 0.68f));
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.32f), s * 0.05f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;

                case OrbisIconKind.Close:
                    p.BeginPath();
                    p.MoveTo(P(0.24f, 0.24f)); p.LineTo(P(0.76f, 0.76f));
                    p.MoveTo(P(0.76f, 0.24f)); p.LineTo(P(0.24f, 0.76f));
                    p.Stroke();
                    break;

                case OrbisIconKind.PhonePortrait:
                    RoundedRect(p, P(0.30f, 0.10f), P(0.70f, 0.90f), s * 0.09f);
                    p.BeginPath();
                    p.MoveTo(P(0.44f, 0.78f)); p.LineTo(P(0.56f, 0.78f));
                    p.Stroke();
                    break;

                case OrbisIconKind.PhoneLandscape:
                    RoundedRect(p, P(0.10f, 0.30f), P(0.90f, 0.70f), s * 0.09f);
                    p.BeginPath();
                    p.MoveTo(P(0.78f, 0.44f)); p.LineTo(P(0.78f, 0.56f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Globe:
                    Circle(p, P(0.5f, 0.5f), s * 0.38f);
                    p.BeginPath();
                    p.MoveTo(P(0.12f, 0.5f)); p.LineTo(P(0.88f, 0.5f));
                    p.Stroke();
                    Arc(p, P(0.5f, 0.5f), s * 0.38f, 0f, 0f, ellipseX: 0.45f);
                    break;
            }
        }

        private static void RoundedRect(Painter2D p, Vector2 min, Vector2 max, float r)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(min.x + r, min.y));
            p.LineTo(new Vector2(max.x - r, min.y));
            p.ArcTo(new Vector2(max.x, min.y), new Vector2(max.x, min.y + r), r);
            p.LineTo(new Vector2(max.x, max.y - r));
            p.ArcTo(new Vector2(max.x, max.y), new Vector2(max.x - r, max.y), r);
            p.LineTo(new Vector2(min.x + r, max.y));
            p.ArcTo(new Vector2(min.x, max.y), new Vector2(min.x, max.y - r), r);
            p.LineTo(new Vector2(min.x, min.y + r));
            p.ArcTo(new Vector2(min.x, min.y), new Vector2(min.x + r, min.y), r);
            p.ClosePath();
            p.Stroke();
        }

        private static void Circle(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        /// <summary>Arco di circonferenza (o ellisse completa schiacciata in x se ellipseX &lt; 1).</summary>
        private static void Arc(Painter2D p, Vector2 c, float r, float fromDeg, float toDeg, float ellipseX = 1f)
        {
            p.BeginPath();
            if (ellipseX < 1f)
            {
                for (var i = 0; i <= 32; i++)
                {
                    var a = i / 32f * Mathf.PI * 2f;
                    var pt = c + new Vector2(Mathf.Cos(a) * r * ellipseX, Mathf.Sin(a) * r);
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
            }
            else
            {
                p.Arc(c, r, Angle.Degrees(fromDeg), Angle.Degrees(toDeg));
            }
            p.Stroke();
        }
    }
}
