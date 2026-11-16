using System;
using Orbis.Core.World.Sky;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    /// <summary>
    /// Cielo, sole, luce ambiente, nebbia, nuvole e pioggia del mondo (stile Classico, docs/08 §3).
    /// Riceve ora del giorno e meteo e aggiorna tutto in modo morbido.
    /// </summary>
    public sealed class SkyController : MonoBehaviour
    {
        public Light Sun;
        public Transform Follow;
        public double Latitude = 42.35;
        public double Longitude = 14.17;

        private Material _skybox;
        private Material _clouds;
        private Material _rain;
        private Transform _cloudLayer;
        private GameObject _rainObject;
        private WeatherState _weather = WeatherState.For(WeatherKind.Clear);
        private WeatherState _shown = WeatherState.For(WeatherKind.Clear);
        private float _nextAmbient;

        public float SunElevation { get; private set; }
        public float Night { get; private set; }

        public void Init(Light sun)
        {
            Sun = sun;
            Sun.shadows = LightShadows.Soft;
            var proc = Shader.Find("Skybox/Procedural");
            if (RenderSettings.skybox != null && RenderSettings.skybox.shader == proc) _skybox = new Material(RenderSettings.skybox);
            else if (proc != null) _skybox = new Material(proc);
            if (_skybox != null) RenderSettings.skybox = _skybox;
            RenderSettings.sun = Sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Custom;

            var cloudShader = Resources.Load<Shader>("OrbisShaders/Clouds");
            if (cloudShader != null && cloudShader.isSupported)
            {
                _clouds = new Material(cloudShader);
                var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
                go.name = "Nuvole";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(9000, 1, 9000); // piano 90 km
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _clouds;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _cloudLayer = go.transform;
            }
            var rainShader = Resources.Load<Shader>("OrbisShaders/Rain");
            if (rainShader != null && rainShader.isSupported)
            {
                _rain = new Material(rainShader);
                _rainObject = new GameObject("Pioggia");
                _rainObject.transform.SetParent(transform, false);
                _rainObject.AddComponent<MeshFilter>().sharedMesh = RainMesh(6000);
                var mr = _rainObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _rain;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _rainObject.SetActive(false);
            }
        }

        public void SetWeather(WeatherState w) => _weather = w;

        /// <summary>Aggiorna sole e cielo per l'ora civile data e per la distanza di vista (nebbia).</summary>
        public void Apply(DateTime localTime, float viewDistance)
        {
            if (Sun == null) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 0.6f);
            _shown.Clouds = Mathf.Lerp(_shown.Clouds, _weather.Clouds, k);
            _shown.Rain = Mathf.Lerp(_shown.Rain, _weather.Rain, k);
            _shown.Fog = Mathf.Lerp(_shown.Fog, _weather.Fog, k);
            _shown.Wetness = Mathf.Lerp(_shown.Wetness, _weather.Wetness, k * 0.3f);

            bool dst = IsDaylightSaving(localTime);
            double solar = SunPosition.SolarHour(localTime, Longitude, dst);
            SunPosition.Compute(Latitude, localTime.DayOfYear, solar, out var el, out var az);
            SunElevation = (float)el;
            float elevation = (float)el * Mathf.Deg2Rad, azimuth = (float)az * Mathf.Deg2Rad;
            var toSun = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(azimuth) * Mathf.Cos(elevation));

            float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-6f, 8f, (float)el));
            Night = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-8f, 2f, (float)el));
            float clouds = _shown.Clouds;
            float dim = 1f - clouds * 0.55f - _shown.Fog * 0.2f;

            if (el > -4)
            {
                // sole: caldo vicino all'orizzonte
                Sun.transform.rotation = Quaternion.LookRotation(-toSun);
                float warm = Mathf.InverseLerp(25f, 0f, (float)el);
                Sun.color = Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(1f, 0.62f, 0.35f), warm);
                Sun.intensity = Mathf.Lerp(0f, 1.25f, day) * dim;
            }
            else
            {
                // luna: luce fredda da una direzione fissa alta
                Sun.transform.rotation = Quaternion.Euler(55f, 200f, 0);
                Sun.color = new Color(0.6f, 0.7f, 0.95f);
                Sun.intensity = 0.16f * (1 - clouds * 0.7f);
            }
            Sun.shadowStrength = Mathf.Lerp(0.35f, 0.85f, day) * (1 - clouds * 0.6f);

            // cielo e ambiente
            var skyDay = Color.Lerp(new Color(0.42f, 0.62f, 0.92f), new Color(0.6f, 0.64f, 0.7f), clouds);
            var skyDusk = new Color(0.95f, 0.6f, 0.45f);
            var skyNight = new Color(0.03f, 0.05f, 0.11f);
            float dusk = Mathf.Clamp01(1f - Mathf.Abs((float)el - 2f) / 10f) * (1 - clouds * 0.7f);
            var sky = Color.Lerp(Color.Lerp(skyNight, skyDay, day), skyDusk, dusk * 0.6f);
            var horizon = Color.Lerp(sky, new Color(0.85f, 0.88f, 0.92f) * Mathf.Max(0.12f, day), 0.45f);
            if (_skybox != null)
            {
                _skybox.SetFloat("_Exposure", Mathf.Lerp(0.12f, 1.25f, day) * (1 - clouds * 0.25f));
                _skybox.SetColor("_SkyTint", Color.Lerp(new Color(0.45f, 0.62f, 0.95f), new Color(0.62f, 0.66f, 0.72f), clouds));
                _skybox.SetColor("_GroundColor", horizon);
                _skybox.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.8f, 1.4f, clouds));
                _skybox.SetFloat("_SunSize", 0.035f * (1 - clouds * 0.8f));
            }
            if (Time.unscaledTime >= _nextAmbient)
            {
                _nextAmbient = Time.unscaledTime + 0.2f;
                var sh = new SphericalHarmonicsL2();
                var ambientSky = Color.Lerp(new Color(0.05f, 0.06f, 0.1f), sky * 0.95f, Mathf.Max(day, 0.15f));
                var ground = Color.Lerp(new Color(0.03f, 0.03f, 0.03f), new Color(0.45f, 0.42f, 0.36f), day);
                sh.AddAmbientLight(Color.Lerp(ambientSky, ground, 0.35f) * 0.9f);
                sh.AddDirectionalLight(Vector3.up, ambientSky * 0.55f, 1f);
                sh.AddDirectionalLight(Vector3.down, ground * 0.35f, 1f);
                RenderSettings.ambientProbe = sh;
            }

            // nebbia: lontano di base (orizzonte), vicina con nebbia e pioggia
            float far = Mathf.Max(9000f, viewDistance * 14f);
            float near = Mathf.Max(1500f, viewDistance * 3f);
            float fog = _shown.Fog;
            RenderSettings.fogStartDistance = Mathf.Lerp(near, 30f, fog);
            RenderSettings.fogEndDistance = Mathf.Lerp(far, 900f + viewDistance * 2f, fog);
            RenderSettings.fogColor = Color.Lerp(horizon, new Color(0.72f, 0.74f, 0.76f) * Mathf.Max(0.1f, day), fog * 0.7f);

            WorldMaterials.SetNight(Night);
            WorldMaterials.SetWet(_shown.Wetness);

            if (_clouds != null)
            {
                _clouds.SetFloat("_Coverage", Mathf.Lerp(0.1f, 0.95f, clouds));
                if (Follow != null) _cloudLayer.position = new Vector3(Follow.position.x, 2200f, Follow.position.z);
            }
            if (_rainObject != null)
            {
                _rainObject.SetActive(_shown.Rain > 0.02f);
                _rain.SetFloat("_Intensity", _shown.Rain);
            }
        }

        private static bool IsDaylightSaving(DateTime t)
        {
            // ora legale europea: dall'ultima domenica di marzo all'ultima di ottobre
            DateTime LastSunday(int month)
            {
                var d = new DateTime(t.Year, month, DateTime.DaysInMonth(t.Year, month));
                while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(-1);
                return d;
            }
            return t >= LastSunday(3).AddHours(2) && t < LastSunday(10).AddHours(3);
        }

        private static Mesh RainMesh(int drops)
        {
            var rnd = new System.Random(7);
            var v = new Vector3[drops * 4];
            var uv = new Vector4[drops * 4];
            var idx = new int[drops * 6];
            for (int i = 0; i < drops; i++)
            {
                var start = new Vector4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), 0);
                for (int k = 0; k < 4; k++)
                {
                    v[i * 4 + k] = new Vector3(k % 2 == 0 ? -1 : 1, 0, 0);
                    var u = start;
                    u.w = k < 2 ? 0 : 1;
                    uv[i * 4 + k] = u;
                }
                idx[i * 6] = i * 4;
                idx[i * 6 + 1] = i * 4 + 2;
                idx[i * 6 + 2] = i * 4 + 1;
                idx[i * 6 + 3] = i * 4 + 1;
                idx[i * 6 + 4] = i * 4 + 2;
                idx[i * 6 + 5] = i * 4 + 3;
            }
            var mesh = new Mesh { name = "Pioggia", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v;
            mesh.SetUVs(0, uv);
            mesh.triangles = idx;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f); // posizioni calcolate nello shader
            return mesh;
        }
    }
}
