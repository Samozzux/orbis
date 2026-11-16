using Orbis.Core.World.Geo;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Terreno lontano (orizzonte): griglia a bassa risoluzione attorno alla regione, con un buco dove ci sono
    /// i tile del pack. Colori per quota e pendenza (pianura verde, colline, roccia, neve sulle vette).
    /// </summary>
    public static class FarTerrainView
    {
        public static GameObject Create(Transform parent, RegionInfo region, WorldFrame frame, Material material)
        {
            var far = region.Far;
            if (far == null) return null;
            var mb = new MeshBuffer();
            int cols = far.Cols, rows = far.Rows;
            float scale = (float)frame.Scale;
            float cell = (float)(far.Cell * scale);
            float ox = (float)((far.X0 - frame.OriginX) * scale), oy = (float)((far.Y0 - frame.OriginY) * scale);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float h = far.Heights[r * cols + c];
                float dx = (far.At(c + 1, r) - far.At(c - 1, r)) / (2 * cell);
                float dy = (far.At(c, r + 1) - far.At(c, r - 1)) / (2 * cell);
                float nl = Mathf.Sqrt(dx * dx + dy * dy + 1);
                uint color = ColorFor(h, 1f / nl);
                // sotto il livello del mare il fondale resta sotto il piano dell'acqua
                mb.Add(ox + c * cell, oy + r * cell, h > 0 ? h - 4f : h - 8f, -dx / nl, -dy / nl, 1 / nl, color, 0, 0, 0, 0, 0, 0, 0, 0);
            }
            int z = region.Zoom;
            for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                // cella coperta da un tile del pack: si salta
                double mx = far.X0 + (c + 0.5) * far.Cell, my = far.Y0 + (r + 0.5) * far.Cell;
                if (region.Tiles.ContainsKey(WebMercator.TileAt(z, mx, my))) continue;
                int v00 = r * cols + c, v10 = v00 + 1, v01 = v00 + cols, v11 = v01 + 1;
                mb.Triangle(v00, v10, v11);
                mb.Triangle(v00, v11, v01);
            }
            var go = new GameObject("Orizzonte");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshUpload.ToMesh(mb, "Orizzonte");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            WorldMaterials.Configure(mr, castShadows: false);
            return go;
        }

        private static uint ColorFor(float h, float up)
        {
            Color c;
            if (h < 2f) c = new Color32(205, 196, 160, 255);                      // spiaggia
            else if (h < 400f) c = Color.Lerp(new Color32(150, 170, 100, 255), new Color32(120, 150, 85, 255), h / 400f);
            else if (h < 1300f) c = Color.Lerp(new Color32(110, 140, 80, 255), new Color32(80, 115, 70, 255), (h - 400f) / 900f);
            else if (h < 2100f) c = Color.Lerp(new Color32(120, 125, 95, 255), new Color32(150, 140, 125, 255), (h - 1300f) / 800f);
            else c = Color.Lerp(new Color32(170, 165, 158, 255), new Color32(240, 242, 245, 255), Mathf.Clamp01((h - 2100f) / 400f));
            if (up < 0.8f) c = Color.Lerp(c, new Color32(140, 128, 112, 255), Mathf.InverseLerp(0.8f, 0.55f, up));
            var c32 = (Color32)c;
            return MeshBuffer.Rgba(c32.r, c32.g, c32.b);
        }
    }
}
