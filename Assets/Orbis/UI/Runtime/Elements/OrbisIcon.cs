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
        Home,
        Layers,
        Compass,
        Sun,
        Moon,
        Cloud,
        Rain,
        Fog,
        Pause,
        Fast,
        Building,
        Pin,
        Map,
        Cube,
        Coins,
        Undo,
        Redo,
        Rewind,
        Rotate,
        Check,
        Star,
        Lock,
        Trash,
        Upgrade,
        Palette,
        Move,
        Chart,
        Flag,
        Minus,
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

                case OrbisIconKind.Minus:
                    p.BeginPath();
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

                case OrbisIconKind.Home:
                    p.BeginPath();
                    p.MoveTo(P(0.14f, 0.48f)); p.LineTo(P(0.5f, 0.16f)); p.LineTo(P(0.86f, 0.48f));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(0.24f, 0.4f)); p.LineTo(P(0.24f, 0.84f)); p.LineTo(P(0.76f, 0.84f)); p.LineTo(P(0.76f, 0.4f));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(0.42f, 0.84f)); p.LineTo(P(0.42f, 0.6f)); p.LineTo(P(0.58f, 0.6f)); p.LineTo(P(0.58f, 0.84f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Layers:
                    for (var i = 0; i < 2; i++)
                    {
                        var dy = i * 0.2f;
                        p.BeginPath();
                        p.MoveTo(P(0.5f, 0.2f + dy)); p.LineTo(P(0.86f, 0.4f + dy)); p.LineTo(P(0.5f, 0.6f + dy)); p.LineTo(P(0.14f, 0.4f + dy));
                        p.ClosePath();
                        p.Stroke();
                    }
                    break;

                case OrbisIconKind.Compass:
                    Circle(p, P(0.5f, 0.5f), s * 0.38f);
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.18f)); p.LineTo(P(0.62f, 0.5f)); p.LineTo(P(0.38f, 0.5f));
                    p.ClosePath();
                    p.Fill();
                    break;

                case OrbisIconKind.Sun:
                    Circle(p, P(0.5f, 0.5f), s * 0.17f);
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * Mathf.PI / 4f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        p.BeginPath();
                        p.MoveTo(P(0.5f, 0.5f) + d * s * 0.29f); p.LineTo(P(0.5f, 0.5f) + d * s * 0.4f);
                        p.Stroke();
                    }
                    break;

                case OrbisIconKind.Moon:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.34f, Angle.Degrees(60f), Angle.Degrees(300f));
                    p.Arc(P(0.64f, 0.42f), s * 0.26f, Angle.Degrees(250f), Angle.Degrees(110f), ArcDirection.CounterClockwise);
                    p.ClosePath();
                    p.Fill();
                    break;

                case OrbisIconKind.Cloud:
                case OrbisIconKind.Rain:
                case OrbisIconKind.Fog:
                    p.BeginPath();
                    p.MoveTo(P(0.24f, 0.66f));
                    p.Arc(P(0.28f, 0.52f), s * 0.14f, Angle.Degrees(110f), Angle.Degrees(250f));
                    p.Arc(P(0.48f, 0.38f), s * 0.2f, Angle.Degrees(190f), Angle.Degrees(340f));
                    p.Arc(P(0.72f, 0.52f), s * 0.14f, Angle.Degrees(270f), Angle.Degrees(430f));
                    p.ClosePath();
                    if (_icon == OrbisIconKind.Cloud) p.Fill(); else p.Stroke();
                    if (_icon == OrbisIconKind.Rain)
                        for (var i = 0; i < 3; i++)
                        {
                            p.BeginPath();
                            p.MoveTo(P(0.34f + i * 0.16f, 0.76f)); p.LineTo(P(0.3f + i * 0.16f, 0.88f));
                            p.Stroke();
                        }
                    if (_icon == OrbisIconKind.Fog)
                        for (var i = 0; i < 2; i++)
                        {
                            p.BeginPath();
                            p.MoveTo(P(0.2f + i * 0.08f, 0.78f + i * 0.1f)); p.LineTo(P(0.8f - i * 0.08f, 0.78f + i * 0.1f));
                            p.Stroke();
                        }
                    break;

                case OrbisIconKind.Pause:
                    p.BeginPath();
                    p.MoveTo(P(0.36f, 0.24f)); p.LineTo(P(0.36f, 0.76f));
                    p.MoveTo(P(0.64f, 0.24f)); p.LineTo(P(0.64f, 0.76f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Fast:
                    for (var i = 0; i < 2; i++)
                    {
                        var dx = i * 0.3f;
                        p.BeginPath();
                        p.MoveTo(P(0.16f + dx, 0.26f)); p.LineTo(P(0.5f + dx, 0.5f)); p.LineTo(P(0.16f + dx, 0.74f));
                        p.ClosePath();
                        p.Fill();
                    }
                    break;

                case OrbisIconKind.Building:
                    RoundedRect(p, P(0.24f, 0.14f), P(0.76f, 0.86f), s * 0.05f);
                    for (var r = 0; r < 3; r++)
                    for (var c = 0; c < 2; c++)
                    {
                        p.BeginPath();
                        p.Arc(P(0.4f + c * 0.2f, 0.3f + r * 0.18f), s * 0.035f, Angle.Degrees(0f), Angle.Degrees(360f));
                        p.Fill();
                    }
                    break;

                case OrbisIconKind.Pin:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.4f), s * 0.24f, Angle.Degrees(150f), Angle.Degrees(390f));
                    p.LineTo(P(0.5f, 0.88f));
                    p.ClosePath();
                    p.Stroke();
                    Circle(p, P(0.5f, 0.4f), s * 0.08f);
                    break;

                case OrbisIconKind.Map:
                    p.BeginPath();
                    p.MoveTo(P(0.14f, 0.24f)); p.LineTo(P(0.38f, 0.16f)); p.LineTo(P(0.62f, 0.24f)); p.LineTo(P(0.86f, 0.16f));
                    p.LineTo(P(0.86f, 0.76f)); p.LineTo(P(0.62f, 0.84f)); p.LineTo(P(0.38f, 0.76f)); p.LineTo(P(0.14f, 0.84f));
                    p.ClosePath();
                    p.MoveTo(P(0.38f, 0.16f)); p.LineTo(P(0.38f, 0.76f));
                    p.MoveTo(P(0.62f, 0.24f)); p.LineTo(P(0.62f, 0.84f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Cube:
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.14f)); p.LineTo(P(0.84f, 0.32f)); p.LineTo(P(0.84f, 0.7f)); p.LineTo(P(0.5f, 0.88f));
                    p.LineTo(P(0.16f, 0.7f)); p.LineTo(P(0.16f, 0.32f)); p.ClosePath();
                    p.MoveTo(P(0.16f, 0.32f)); p.LineTo(P(0.5f, 0.5f)); p.LineTo(P(0.84f, 0.32f));
                    p.MoveTo(P(0.5f, 0.5f)); p.LineTo(P(0.5f, 0.88f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Coins:
                    Circle(p, P(0.42f, 0.42f), s * 0.24f);
                    Arc(p, P(0.6f, 0.6f), s * 0.24f, -60f, 150f);
                    p.BeginPath();
                    p.MoveTo(P(0.42f, 0.32f)); p.LineTo(P(0.42f, 0.52f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Undo:
                case OrbisIconKind.Redo:
                    {
                        bool undo = _icon == OrbisIconKind.Undo;
                        float sx = undo ? 1 : -1;
                        Vector2 Q(float x, float y) => P(0.5f + (x - 0.5f) * sx, y);
                        p.BeginPath();
                        p.MoveTo(Q(0.22f, 0.42f));
                        p.LineTo(Q(0.62f, 0.42f));
                        p.ArcTo(Q(0.84f, 0.42f), Q(0.84f, 0.64f), s * 0.2f);
                        p.ArcTo(Q(0.84f, 0.84f), Q(0.62f, 0.84f), s * 0.2f);
                        p.LineTo(Q(0.4f, 0.84f));
                        p.Stroke();
                        p.BeginPath();
                        p.MoveTo(Q(0.38f, 0.26f)); p.LineTo(Q(0.22f, 0.42f)); p.LineTo(Q(0.38f, 0.58f));
                        p.Stroke();
                        break;
                    }

                case OrbisIconKind.Rewind:
                    for (var i = 0; i < 2; i++)
                    {
                        var dx = i * 0.3f;
                        p.BeginPath();
                        p.MoveTo(P(0.84f - dx, 0.26f)); p.LineTo(P(0.5f - dx, 0.5f)); p.LineTo(P(0.84f - dx, 0.74f));
                        p.ClosePath();
                        p.Fill();
                    }
                    break;

                case OrbisIconKind.Rotate:
                    Arc(p, P(0.5f, 0.5f), s * 0.3f, 210f, 480f);
                    p.BeginPath();
                    p.MoveTo(P(0.24f, 0.3f)); p.LineTo(P(0.24f, 0.5f)); p.LineTo(P(0.44f, 0.5f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Check:
                    p.BeginPath();
                    p.MoveTo(P(0.2f, 0.52f)); p.LineTo(P(0.42f, 0.74f)); p.LineTo(P(0.82f, 0.28f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Star:
                    p.BeginPath();
                    for (var i = 0; i < 10; i++)
                    {
                        var a = -Mathf.PI / 2 + i * Mathf.PI / 5;
                        var r = i % 2 == 0 ? 0.4f : 0.17f;
                        var pt = P(0.5f + Mathf.Cos(a) * r, 0.53f + Mathf.Sin(a) * r);
                        if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                    }
                    p.ClosePath();
                    p.Fill();
                    break;

                case OrbisIconKind.Lock:
                    RoundedRect(p, P(0.24f, 0.46f), P(0.76f, 0.86f), s * 0.06f);
                    Arc(p, P(0.5f, 0.46f), s * 0.17f, 180f, 360f);
                    break;

                case OrbisIconKind.Trash:
                    p.BeginPath();
                    p.MoveTo(P(0.18f, 0.28f)); p.LineTo(P(0.82f, 0.28f));
                    p.MoveTo(P(0.4f, 0.28f)); p.LineTo(P(0.42f, 0.16f)); p.LineTo(P(0.58f, 0.16f)); p.LineTo(P(0.6f, 0.28f));
                    p.MoveTo(P(0.26f, 0.3f)); p.LineTo(P(0.3f, 0.86f)); p.LineTo(P(0.7f, 0.86f)); p.LineTo(P(0.74f, 0.3f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Upgrade:
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.16f)); p.LineTo(P(0.84f, 0.52f)); p.LineTo(P(0.62f, 0.52f)); p.LineTo(P(0.62f, 0.84f));
                    p.LineTo(P(0.38f, 0.84f)); p.LineTo(P(0.38f, 0.52f)); p.LineTo(P(0.16f, 0.52f));
                    p.ClosePath();
                    p.Fill();
                    break;

                case OrbisIconKind.Palette:
                    Circle(p, P(0.5f, 0.5f), s * 0.36f);
                    for (var i = 0; i < 4; i++)
                    {
                        var a = i * Mathf.PI / 2 + 0.6f;
                        p.BeginPath();
                        p.Arc(P(0.5f + Mathf.Cos(a) * 0.2f, 0.5f + Mathf.Sin(a) * 0.2f), s * 0.06f, Angle.Degrees(0f), Angle.Degrees(360f));
                        p.Fill();
                    }
                    break;

                case OrbisIconKind.Move:
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.14f)); p.LineTo(P(0.5f, 0.86f));
                    p.MoveTo(P(0.14f, 0.5f)); p.LineTo(P(0.86f, 0.5f));
                    p.MoveTo(P(0.4f, 0.24f)); p.LineTo(P(0.5f, 0.14f)); p.LineTo(P(0.6f, 0.24f));
                    p.MoveTo(P(0.4f, 0.76f)); p.LineTo(P(0.5f, 0.86f)); p.LineTo(P(0.6f, 0.76f));
                    p.MoveTo(P(0.24f, 0.4f)); p.LineTo(P(0.14f, 0.5f)); p.LineTo(P(0.24f, 0.6f));
                    p.MoveTo(P(0.76f, 0.4f)); p.LineTo(P(0.86f, 0.5f)); p.LineTo(P(0.76f, 0.6f));
                    p.Stroke();
                    break;

                case OrbisIconKind.Chart:
                    for (var i = 0; i < 3; i++)
                    {
                        var h = new[] { 0.3f, 0.5f, 0.68f }[i];
                        RoundedRect(p, P(0.18f + i * 0.24f, 0.84f - h), P(0.34f + i * 0.24f, 0.84f), s * 0.03f);
                    }
                    break;

                case OrbisIconKind.Flag:
                    p.BeginPath();
                    p.MoveTo(P(0.26f, 0.88f)); p.LineTo(P(0.26f, 0.14f));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(0.28f, 0.16f)); p.LineTo(P(0.78f, 0.28f)); p.LineTo(P(0.28f, 0.46f));
                    p.ClosePath();
                    p.Fill();
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
