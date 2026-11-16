using System.Collections;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Globo base (docs/02 §2 "Globo base"): sfera con la texture della Terra del pack (globe.jpg),
    /// 1 unità = 1 km, centro nell'origine della scena del globo. Un segnaposto indica la regione.
    /// </summary>
    public sealed class GlobeView : MonoBehaviour
    {
        public const float Radius = 6371f;

        private Material _material;
        private Texture2D _texture;
        private Transform _pin;

        public bool HasTexture => _texture != null;

        public static GlobeView Create(Transform parent)
        {
            var go = new GameObject("Globo");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<GlobeView>();
            go.AddComponent<MeshFilter>().sharedMesh = Sphere(128, 64);
            var shader = Resources.Load<Shader>("OrbisShaders/Globe");
            view._material = new Material(shader != null && shader.isSupported ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = view._material;
            WorldMaterials.Configure(mr, false, false);
            go.SetActive(false);
            return view;
        }

        /// <summary>Carica la texture dal pack (se manca resta un globo azzurro).</summary>
        public IEnumerator Load(TileSource source)
        {
            byte[] data = null;
            yield return source.Fetch("globe.jpg", b => data = b);
            if (data == null) yield break;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = "Terra" };
            if (tex.LoadImage(data, true))
            {
                _texture = tex;
                _material.mainTexture = tex;
            }
        }

        /// <summary>Segnaposto sulla regione (lat/lon in gradi).</summary>
        public void SetPin(double lat, double lon)
        {
            if (_pin == null)
            {
                var pin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(pin.GetComponent<Collider>());
                pin.name = "Segnaposto";
                pin.transform.SetParent(transform, false);
                var shader = Resources.Load<Shader>("OrbisShaders/Highlight");
                var m = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_Color", new Color(1f, 0.45f, 0.1f));
                pin.GetComponent<MeshRenderer>().sharedMaterial = m;
                _pin = pin.transform;
            }
            _pin.localPosition = Position(lat, lon, Radius + 2f);
            _pin.localScale = Vector3.one * 40f;
        }

        public void SetSun(Vector3 dir) => _material.SetVector("_SunDir", dir);

        /// <summary>Punto sulla sfera (km) per latitudine e longitudine in gradi.</summary>
        public static Vector3 Position(double lat, double lon, float r)
        {
            float la = (float)(lat * Mathf.Deg2Rad), lo = (float)(lon * Mathf.Deg2Rad);
            return new Vector3(Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la), -Mathf.Cos(la) * Mathf.Cos(lo)) * r;
        }

        private static Mesh Sphere(int segments, int rings)
        {
            var v = new Vector3[(segments + 1) * (rings + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                double lat = 90.0 - 180.0 * r / rings;
                double lon = -180.0 + 360.0 * s / segments;
                int i = r * (segments + 1) + s;
                n[i] = Position(lat, lon, 1f);
                v[i] = n[i] * Radius;
                uv[i] = new Vector2((float)s / segments, 1f - (float)r / rings);
            }
            var tris = new int[segments * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            var mesh = new Mesh { name = "Globo", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = v;
            mesh.normals = n;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
