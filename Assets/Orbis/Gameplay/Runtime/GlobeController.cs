using System;
using Orbis.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Vista del globo (zoom continuo, docs/04 §2): allontanandosi oltre la distanza massima della città si passa
    /// al globo sopra lo stesso punto; trascinando si gira la Terra, avvicinandosi sotto i 55 km si torna a terra
    /// (nella regione caricata; fuori regione si torna su Chieti).
    /// </summary>
    public sealed class GlobeController : MonoBehaviour
    {
        public const float EnterAltitudeKm = 70f;
        public const float ExitAltitudeKm = 55f;
        public const float MaxAltitudeKm = 30000f;

        public GlobeView View { get; private set; }
        public bool Active { get; private set; }
        public double Lat { get; private set; }
        public double Lon { get; private set; }
        public float AltitudeKm { get; private set; } = EnterAltitudeKm;

        public event Action<bool> ActiveChanged;

        private WorldSession _session;
        private float _altitudeTarget = EnterAltitudeKm;
        private Vector2 _lastPointer;
        private bool _dragging;
        private float _prevPinch;
        private int _prevTouches;
        private CameraClearFlags _clear;
        private Color _background;
        private float _near, _far;
        private float _enteredAt;

        public static GlobeController Create(WorldSession session)
        {
            var c = session.gameObject.AddComponent<GlobeController>();
            c._session = session;
            c.View = GlobeView.Create(session.transform);
            var region = session.World.Region;
            c.View.SetPin(region.OriginLat, region.OriginLon);
            session.CameraRig.OverZoom += c.EnterFromCity;
            return c;
        }

        private void EnterFromCity()
        {
            if (Active) return;
            var t = _session.CameraRig.Target;
            _session.World.Frame.ToLonLat(t.x, t.z, out var lon, out var lat);
            Enter(lat, lon);
        }

        /// <summary>Entra nel globo sopra il punto (gradi), alla quota data in km.</summary>
        public void Enter(double lat, double lon, float altitudeKm = EnterAltitudeKm)
        {
            Active = true;
            _enteredAt = Time.unscaledTime;
            Lat = lat;
            Lon = lon;
            AltitudeKm = _altitudeTarget = altitudeKm;
            _session.Select(null);
            _session.World.SetVisible(false);
            _session.Sky.gameObject.SetActive(true);
            var cam = _session.CameraRig.Cam;
            _session.CameraRig.enabled = false;
            _clear = cam.clearFlags;
            _background = cam.backgroundColor;
            _near = cam.nearClipPlane;
            _far = cam.farClipPlane;
            cam.orthographic = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.015f, 0.04f);
            RenderSettings.fog = false;
            SetCloudsAndRain(false);
            View.gameObject.SetActive(true);
            ActiveChanged?.Invoke(true);
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
            View.gameObject.SetActive(false);
            var cam = _session.CameraRig.Cam;
            cam.clearFlags = _clear;
            cam.backgroundColor = _background;
            cam.nearClipPlane = _near;
            cam.farClipPlane = _far;
            RenderSettings.fog = true;
            SetCloudsAndRain(true);
            var frame = _session.World.Frame;
            frame.FromLonLat(Lon, Lat, out var x, out var z);
            // fuori dalla regione caricata si torna a Chieti
            if (Math.Sqrt(x * x + z * z) > 60000) x = z = 0;
            var rig = _session.CameraRig;
            rig.Target = new Vector3((float)x, rig.Target.y, (float)z);
            rig.Distance = CityCamera.MaxDistance * 0.9f;
            rig.Snap();
            rig.enabled = true;
            _session.World.SetVisible(true);
            ActiveChanged?.Invoke(false);
        }

        /// <summary>Nuvole e pioggia del mondo piatto: spente nel globo (la pioggia la riaccende il cielo se serve).</summary>
        private void SetCloudsAndRain(bool on)
        {
            foreach (Transform child in _session.Sky.transform)
            {
                if (child.name == "Nuvole") child.gameObject.SetActive(on);
                else if (child.name == "Pioggia" && !on) child.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!Active) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            HandleInput();
            AltitudeKm = Mathf.Exp(Mathf.Lerp(Mathf.Log(AltitudeKm), Mathf.Log(_altitudeTarget), 1 - Mathf.Exp(-dt * 8f)));
            Lat = Math.Max(-85, Math.Min(85, Lat));
            if (Lon > 180) Lon -= 360;
            if (Lon < -180) Lon += 360;
            var cam = _session.CameraRig.Cam;
            var pos = GlobeView.Position(Lat, Lon, GlobeView.Radius + AltitudeKm) + View.transform.position;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(View.transform.position - pos, Vector3.up));
            cam.nearClipPlane = Mathf.Max(0.5f, AltitudeKm * 0.3f);
            cam.farClipPlane = GlobeView.Radius * 3f + AltitudeKm * 2f;
            View.SetSun(SunDirection(_session.Clock.Now));
            // tornando sotto la quota di uscita si scende sulla città (non subito dopo l'ingresso)
            if (_altitudeTarget < ExitAltitudeKm && Time.unscaledTime - _enteredAt > 0.6f) Exit();
        }

        /// <summary>Direzione del sole dal punto sub-solare (dichinazione e ora UTC approssimate).</summary>
        private static Vector3 SunDirection(DateTime local)
        {
            double decl = 23.44 * Math.Sin(2 * Math.PI * (284 + local.DayOfYear) / 365.0);
            double utc = local.Hour + local.Minute / 60.0 - 1.0; // Europa centrale (senza ora legale: basta per il globo)
            double lon = -(utc - 12) * 15;
            return GlobeView.Position(decl, lon, 1f).normalized;
        }

        private void HandleInput()
        {
            float degPerPixel = AltitudeKm / 111f / Mathf.Max(200f, Screen.height) * 1.6f;
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                float pinch = (touches[0].screenPosition - touches[1].screenPosition).magnitude;
                if (_prevTouches >= 2 && _prevPinch > 1f) Zoom(_prevPinch / pinch);
                _prevPinch = pinch;
                _prevTouches = touches.Count;
                return;
            }
            if (touches.Count == 1)
            {
                var p = touches[0].screenPosition;
                if (_prevTouches == 1) Drag(p - _lastPointer, degPerPixel);
                _lastPointer = p;
                _prevTouches = 1;
                return;
            }
            _prevTouches = 0;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame) _dragging = !(_session.CameraRig.IsOverUi?.Invoke(p) ?? false);
                if (mouse.leftButton.isPressed && _dragging) Drag(p - _lastPointer, degPerPixel);
                if (!mouse.leftButton.isPressed) _dragging = false;
                _lastPointer = p;
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float steps = Mathf.Abs(scroll) < 10f ? Mathf.Clamp(scroll * 0.08f, -1f, 1f) : Mathf.Clamp(scroll / 120f, -3f, 3f);
                    Zoom(Mathf.Pow(0.8f, steps));
                }
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                float d = Time.unscaledDeltaTime * 30f * degPerPixel;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) Lat += d;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) Lat -= d;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) Lon += d;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) Lon -= d;
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) Zoom(Mathf.Exp(-1.5f * Time.unscaledDeltaTime));
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) Zoom(Mathf.Exp(1.5f * Time.unscaledDeltaTime));
            }
        }

        private void Drag(Vector2 delta, float degPerPixel)
        {
            Lon -= delta.x * degPerPixel / Math.Max(0.2, Math.Cos(Lat * Math.PI / 180));
            Lat -= delta.y * degPerPixel;
        }

        private void Zoom(float factor)
        {
            _altitudeTarget = Mathf.Clamp(_altitudeTarget * factor, ExitAltitudeKm * 0.9f, MaxAltitudeKm);
        }
    }
}
