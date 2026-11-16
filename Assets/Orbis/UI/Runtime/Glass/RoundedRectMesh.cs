using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Glass
{
    /// <summary>Geometria di rettangoli arrotondati per le mesh UI Toolkit (triangoli in senso orario, y verso il basso).</summary>
    internal static class RoundedRectMesh
    {
        /// <summary>Raggi in ordine: alto-sinistra, alto-destra, basso-destra, basso-sinistra.</summary>
        public static void Perimeter(List<Vector2> points, Rect rect, Vector4 radii, float inset, int segmentsPerCorner)
        {
            points.Clear();
            var x0 = rect.xMin + inset;
            var y0 = rect.yMin + inset;
            var x1 = rect.xMax - inset;
            var y1 = rect.yMax - inset;
            var maxRadius = Mathf.Max(0f, Mathf.Min(x1 - x0, y1 - y0) * 0.5f);
            float R(float r) => Mathf.Clamp(r - inset, 0f, maxRadius);

            AddArc(points, new Vector2(x0, y0), R(radii.x), 180f, 270f, segmentsPerCorner, +1, +1);
            AddArc(points, new Vector2(x1, y0), R(radii.y), 270f, 360f, segmentsPerCorner, -1, +1);
            AddArc(points, new Vector2(x1, y1), R(radii.z), 0f, 90f, segmentsPerCorner, -1, -1);
            AddArc(points, new Vector2(x0, y1), R(radii.w), 90f, 180f, segmentsPerCorner, +1, -1);
        }

        private static void AddArc(List<Vector2> points, Vector2 corner, float radius, float fromDeg, float toDeg, int segments, int sx, int sy)
        {
            var center = new Vector2(corner.x + sx * radius, corner.y + sy * radius);
            for (var i = 0; i <= segments; i++)
            {
                var a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, (float)i / segments);
                points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        public static int SegmentsFor(float radius) => Mathf.Clamp(Mathf.CeilToInt(radius / 3f), 2, 16);

        /// <summary>Riempimento a ventaglio dal centro (il bordo è convesso).</summary>
        public static void Fan(List<Vertex> vertices, List<ushort> indices, List<Vector2> perimeter, Vector2 center,
            System.Func<Vector2, Color32> color, System.Func<Vector2, Vector2> uv)
        {
            var start = (ushort)vertices.Count;
            vertices.Add(MakeVertex(center, color(center), uv(center)));
            foreach (var p in perimeter)
                vertices.Add(MakeVertex(p, color(p), uv(p)));
            var n = perimeter.Count;
            for (var i = 0; i < n; i++)
            {
                indices.Add(start);
                indices.Add((ushort)(start + 1 + i));
                indices.Add((ushort)(start + 1 + (i + 1) % n));
            }
        }

        /// <summary>Anello tra due perimetri con lo stesso numero di punti (bordo luminoso del vetro).</summary>
        public static void Ring(List<Vertex> vertices, List<ushort> indices, List<Vector2> outer, List<Vector2> inner,
            System.Func<Vector2, Color32> color)
        {
            var start = (ushort)vertices.Count;
            var n = outer.Count;
            for (var i = 0; i < n; i++)
            {
                vertices.Add(MakeVertex(outer[i], color(outer[i]), Vector2.zero));
                vertices.Add(MakeVertex(inner[i], color(inner[i]), Vector2.zero));
            }
            for (var i = 0; i < n; i++)
            {
                var o0 = (ushort)(start + i * 2);
                var i0 = (ushort)(o0 + 1);
                var o1 = (ushort)(start + (i + 1) % n * 2);
                var i1 = (ushort)(o1 + 1);
                indices.Add(o0); indices.Add(o1); indices.Add(i0);
                indices.Add(i0); indices.Add(o1); indices.Add(i1);
            }
        }

        public static Vertex MakeVertex(Vector2 p, Color32 tint, Vector2 uv) =>
            new Vertex { position = new Vector3(p.x, p.y, Vertex.nearZ), tint = tint, uv = uv };
    }
}
