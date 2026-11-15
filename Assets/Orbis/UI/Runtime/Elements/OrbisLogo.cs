using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Elements
{
    /// <summary>
    /// Logo di Orbis disegnato in vettoriale: un globo a fil di ferro che ruota lentamente,
    /// con un'orbita inclinata e un piccolo satellite. Colori da USS (--logo-color, --logo-accent).
    /// </summary>
    [UxmlElement]
    public partial class OrbisLogo : VisualElement
    {
        private static readonly CustomStyleProperty<Color> ColorProp = new CustomStyleProperty<Color>("--logo-color");
        private static readonly CustomStyleProperty<Color> AccentProp = new CustomStyleProperty<Color>("--logo-accent");

        private const float Tilt = 0.38f;       // inclinazione dell'asse (radianti)
        private const float SpinSpeed = 0.22f;  // radianti al secondo
        private const float OrbitSpeed = 0.55f;

        private Color _color = Color.white;
        private Color _accent = new Color(0.35f, 0.78f, 0.98f, 1f);
        private float _time;
        private readonly List<Vector3> _points = new List<Vector3>(96);

        [UxmlAttribute]
        public bool animate { get; set; } = true;

        public OrbisLogo()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(ColorProp, out var c)) _color = c;
                if (e.customStyle.TryGetValue(AccentProp, out var a)) _accent = a;
                MarkDirtyRepaint();
            });
            schedule.Execute(t =>
            {
                if (!animate || resolvedStyle.display == DisplayStyle.None || resolvedStyle.opacity <= 0.01f)
                    return;
                _time += t.deltaTime / 1000f;
                MarkDirtyRepaint();
            }).Every(33);
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var size = Mathf.Min(contentRect.width, contentRect.height);
            if (size < 4f)
                return;
            var p = mgc.painter2D;
            var c = contentRect.center;
            var radius = size * 0.36f;
            var line = Mathf.Max(1f, size * 0.022f);
            var spin = _time * SpinSpeed;

            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            // metà posteriore dell'orbita (dietro al globo)
            DrawOrbit(p, c, radius, line, back: true);

            // disco del globo: leggermente pieno per "coprire" l'orbita posteriore
            p.fillColor = new Color(_color.r, _color.g, _color.b, 0.07f);
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();

            // meridiani
            for (var k = 0; k < 6; k++)
            {
                var lon = spin + k * Mathf.PI / 6f;
                _points.Clear();
                for (var i = 0; i <= 32; i++)
                {
                    var lat = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, i / 32f);
                    _points.Add(Project(lat, lon));
                }
                StrokeVisible(p, c, radius, line * 0.75f, 0.55f);
                _points.Clear();
                for (var i = 0; i <= 32; i++)
                {
                    var lat = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, i / 32f);
                    _points.Add(Project(lat, lon + Mathf.PI));
                }
                StrokeVisible(p, c, radius, line * 0.75f, 0.55f);
            }

            // paralleli
            for (var k = -2; k <= 2; k++)
            {
                var lat = k * Mathf.PI / 6f;
                _points.Clear();
                for (var i = 0; i <= 48; i++)
                    _points.Add(Project(lat, i / 48f * Mathf.PI * 2f));
                StrokeVisible(p, c, radius, line * (k == 0 ? 0.9f : 0.75f), k == 0 ? 0.75f : 0.5f);
            }

            // contorno
            p.strokeColor = _color;
            p.lineWidth = line;
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();

            // metà anteriore dell'orbita e satellite
            DrawOrbit(p, c, radius, line, back: false);
        }

        private Vector3 Project(float lat, float lon)
        {
            var x = Mathf.Cos(lat) * Mathf.Sin(lon);
            var y = Mathf.Sin(lat);
            var z = Mathf.Cos(lat) * Mathf.Cos(lon);
            var y2 = y * Mathf.Cos(Tilt) - z * Mathf.Sin(Tilt);
            var z2 = y * Mathf.Sin(Tilt) + z * Mathf.Cos(Tilt);
            return new Vector3(x, y2, z2);
        }

        /// <summary>Traccia solo i tratti sul lato visibile (z &gt; 0), più tenui verso il bordo.</summary>
        private void StrokeVisible(Painter2D p, Vector2 c, float radius, float width, float alpha)
        {
            var i = 0;
            while (i < _points.Count)
            {
                while (i < _points.Count && _points[i].z <= 0f) i++;
                if (i >= _points.Count) break;
                var start = i;
                var depth = 0f;
                while (i < _points.Count && _points[i].z > 0f) { depth += _points[i].z; i++; }
                if (i - start < 2) continue;
                depth /= i - start;
                p.strokeColor = new Color(_color.r, _color.g, _color.b, alpha * (0.35f + 0.65f * depth));
                p.lineWidth = width;
                p.BeginPath();
                p.MoveTo(c + new Vector2(_points[start].x, -_points[start].y) * radius);
                for (var j = start + 1; j < i; j++)
                    p.LineTo(c + new Vector2(_points[j].x, -_points[j].y) * radius);
                p.Stroke();
            }
        }

        private void DrawOrbit(Painter2D p, Vector2 c, float radius, float line, bool back)
        {
            const float rot = -0.32f; // rotazione dell'ellisse sul piano
            var rx = radius * 1.42f;
            var ry = radius * 0.40f;
            var cos = Mathf.Cos(rot);
            var sin = Mathf.Sin(rot);
            Vector2 At(float a)
            {
                var e = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                return c + new Vector2(e.x * cos - e.y * sin, e.x * sin + e.y * cos);
            }

            // metà posteriore = parte alta dell'ellisse (sin < 0), anteriore = parte bassa
            var from = back ? Mathf.PI : 0f;
            p.strokeColor = new Color(_accent.r, _accent.g, _accent.b, back ? 0.35f : 0.95f);
            p.lineWidth = line;
            p.BeginPath();
            p.MoveTo(At(from));
            for (var i = 1; i <= 40; i++)
                p.LineTo(At(from + Mathf.PI * i / 40f));
            p.Stroke();

            var sat = Mathf.Repeat(_time * OrbitSpeed, Mathf.PI * 2f);
            var satInFront = sat < Mathf.PI;
            if (satInFront != back)
            {
                p.fillColor = back ? new Color(_accent.r, _accent.g, _accent.b, 0.45f) : _accent;
                p.BeginPath();
                p.Arc(At(sat), line * 2.1f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }
        }
    }
}
