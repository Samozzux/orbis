using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Glass
{
    /// <summary>Disegna lo sfondo nitido a tutto schermo e comunica la sua area alle superfici di vetro.</summary>
    [UxmlElement]
    public partial class GlassBackdropElement : VisualElement
    {
        private readonly List<Vertex> _vertices = new List<Vertex>(4);
        private readonly List<ushort> _indices = new List<ushort>(6);

        public GlassBackdropElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                GlassBackdrop.SetArea(worldBound);
                MarkDirtyRepaint();
            });
        }

        private void Draw(MeshGenerationContext mgc)
        {
            GlassBackdrop.EnsureTextures();
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f)
                return;

            _vertices.Clear();
            _indices.Clear();
            var corners = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            var mesh = mgc.Allocate(4, 6, GlassBackdrop.Sharp);
            // le UV sono quelle della texture intera: Unity le rimappa da solo se la texture finisce in un atlante
            foreach (var c in corners)
                _vertices.Add(RoundedRectMesh.MakeVertex(c, Color.white, GlassBackdrop.UvAt(this.LocalToWorld(c))));
            _indices.AddRange(new ushort[] { 0, 1, 2, 0, 2, 3 });
            mesh.SetAllVertices(_vertices.ToArray());
            mesh.SetAllIndices(_indices.ToArray());
        }
    }
}
