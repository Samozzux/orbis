using System;
using Orbis.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Telecamera della città (docs/04 §2): orbita attorno a un punto sul terreno, vista 3D o 2D dall'alto.
    /// Touch: un dito sposta, pizzico zoom, due dita ruotano e (in verticale) inclinano, doppio tocco centra.
    /// Mouse: sinistro sposta, destro ruota/inclina, rotella zoom verso il puntatore, doppio clic centra.
    /// Tastiera: WASD/frecce, Q-E inclina, Z-X ruota, +/- zoom, M passa 2D/3D.
    /// Movimenti con inerzia e smorzamento. Funziona uguale in verticale e in orizzontale.
    /// </summary>
    public sealed class CityCamera : MonoBehaviour
    {
        public const float MinDistance = 25f;
        public const float MaxDistance = 45000f;
        public const float MinPitch = 12f;
        public const float MaxPitch = 89f;

        public Camera Cam;
        public WorldStreamer World;
        /// <summary>Il punto dello schermo (pixel, y in basso) è sopra un elemento dell'interfaccia?</summary>
        public Func<Vector2, bool> IsOverUi = _ => false;

        /// <summary>Tastiera occupata dall'interfaccia (es. si scrive in un campo di testo): niente WASD/QE.</summary>
        public Func<bool> KeyboardBlocked = () => false;

        /// <summary>Tocco o clic breve su un punto (pixel dello schermo).</summary>
        public event Action<Vector2> Tapped;
        /// <summary>Si continua ad allontanarsi oltre la distanza massima (→ globo).</summary>
        public event Action OverZoom;
        /// <summary>Passaggio tra vista 3D e 2D.</summary>
        public event Action<bool> ModeChanged;

        public Vector3 Target;
        public float Yaw = 25f;
        public float Pitch = 48f;
        public float Distance = 650f;
        public bool TopDown { get; private set; }

        private Vector3 _target;
        private float _yaw, _pitch, _dist, _groundY;
        private Vector3 _panVelocity;
        private bool _dragging;
        private Vector2 _lastPointer;
        private Vector2 _downPos;
        private float _downTime;
        private bool _downOverUi;
        private bool _moved;
        private float _lastTapTime;
        private Vector2 _lastTapPos;
        private float _prevPinch, _prevAngle;
        private Vector2 _prevMid;
        private int _prevTouchCount;
        private bool _flyTo;
        private Vector3 _flyTarget;

        private float Dpi => Screen.dpi > 0 ? Screen.dpi : 160f;

        /// <summary>Posiziona subito la telecamera (senza smorzamento), es. uscendo dal globo.</summary>
        public void Snap()
        {
            _target = Target;
            _yaw = Yaw;
            _pitch = Pitch;
            _dist = Distance;
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            _target = Target;
            _yaw = Yaw;
            _pitch = Pitch;
            _dist = Distance;
            _groundY = Target.y;
        }

        public void SetTopDown(bool topDown)
        {
            if (TopDown == topDown) return;
            TopDown = topDown;
            ModeChanged?.Invoke(topDown);
        }

        /// <summary>Riporta il nord in alto.</summary>
        public void ResetNorth() => Yaw = 0;

        /// <summary>Sposta la vista con un volo morbido.</summary>
        public void FlyTo(Vector3 point, float distance = -1)
        {
            _flyTarget = point;
            _flyTo = true;
            if (distance > 0) Distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        }

        private void Update()
        {
            if (Cam == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (Touch.activeTouches.Count > 0 || _prevTouchCount > 0) HandleTouches(dt);
            else
            {
                HandleMouse(dt);
                if (!KeyboardBlocked()) HandleKeyboard(dt);
            }

            // inerzia dello spostamento
            if (!_dragging && _panVelocity.sqrMagnitude > 0.01f)
            {
                Target += _panVelocity * dt;
                _panVelocity *= Mathf.Exp(-dt * 4.5f);
            }
            if (_flyTo)
            {
                Target = Vector3.Lerp(Target, _flyTarget, 1 - Mathf.Exp(-dt * 4f));
                if ((Target - _flyTarget).sqrMagnitude < 1f) _flyTo = false;
            }

            Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
            Target.x = Mathf.Clamp(Target.x, -60000, 60000);
            Target.z = Mathf.Clamp(Target.z, -60000, 60000);

            // quota del terreno sotto il punto osservato
            if (World != null)
            {
                float h = World.HeightAt(Target.x, Target.z);
                if (!float.IsNaN(h)) _groundY = Mathf.Lerp(_groundY, h, 1 - Mathf.Exp(-dt * 6f));
            }
            Target.y = _groundY;

            float k = 1 - Mathf.Exp(-dt * 12f);
            _target = Vector3.Lerp(_target, Target, k);
            _yaw = Mathf.LerpAngle(_yaw, Yaw, k);
            _pitch = Mathf.Lerp(_pitch, TopDown ? 89.9f : Pitch, k);
            _dist = Mathf.Exp(Mathf.Lerp(Mathf.Log(_dist), Mathf.Log(Distance), k));
            Place();
        }

        private void Place()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            var pos = _target - rot * Vector3.forward * _dist;
            // mai sotto il terreno
            if (World != null)
            {
                float h = World.HeightAt(pos.x, pos.z);
                if (!float.IsNaN(h) && pos.y < h + 4f) pos.y = h + 4f;
            }
            if (pos.y < 3f) pos.y = 3f; // mare
            Cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_target - pos, rot * Vector3.up));
            Cam.orthographic = TopDown && _pitch > 85f;
            Cam.orthographicSize = _dist * 0.55f;
            Cam.nearClipPlane = Mathf.Clamp(_dist * 0.02f, 0.5f, 400f);
            Cam.farClipPlane = Mathf.Clamp(_dist * 30f + 25000f, 25000f, 160000f);
            if (World != null)
            {
                World.Focus = _target;
                World.ViewDistance = _dist;
            }
        }

        // ---------------------------------------------------------------- input

        /// <summary>Punto sul piano orizzontale del punto osservato sotto il pixel dato.</summary>
        private bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, _target.y, 0));
            if (plane.Raycast(ray, out var t) && t < _dist * 25f)
            {
                point = ray.GetPoint(t);
                return true;
            }
            point = default;
            return false;
        }

        private void PanByScreen(Vector2 from, Vector2 to, float dt)
        {
            if (!GroundPoint(from, out var a) || !GroundPoint(to, out var b)) return;
            var delta = a - b;
            delta.y = 0;
            // limite per i raggi quasi orizzontali
            float max = _dist * 0.5f;
            if (delta.magnitude > max) delta = delta.normalized * max;
            Target += delta;
            _flyTo = false;
            if (dt > 0) _panVelocity = Vector3.Lerp(_panVelocity, delta / dt, 0.5f);
        }

        private void ZoomAt(Vector2 screen, float factor)
        {
            if (factor > 1f && Distance >= MaxDistance * 0.98f)
            {
                OverZoom?.Invoke();
                return;
            }
            float before = Distance;
            Distance = Mathf.Clamp(Distance * factor, MinDistance, MaxDistance);
            if (GroundPoint(screen, out var p))
            {
                // il punto sotto il puntatore resta fermo
                var t = p + (Target - p) * (Distance / before);
                Target = new Vector3(t.x, Target.y, t.z);
            }
        }

        private void HandleMouse(float dt)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var pos = mouse.position.ReadValue();
            bool overUi = IsOverUi(pos);

            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
            {
                _downOverUi = overUi;
                _downPos = pos;
                _downTime = Time.unscaledTime;
                _moved = false;
                _lastPointer = pos;
                _panVelocity = Vector3.zero;
            }
            bool left = mouse.leftButton.isPressed, right = mouse.rightButton.isPressed, middle = mouse.middleButton.isPressed;
            if (!_downOverUi && (left || right || middle))
            {
                var delta = pos - _lastPointer;
                if ((pos - _downPos).magnitude > 6f) _moved = true;
                if (_moved)
                {
                    if (left || middle)
                    {
                        _dragging = true;
                        PanByScreen(_lastPointer, pos, dt);
                    }
                    else
                    {
                        float s = 160f / Dpi;
                        Yaw += delta.x * 0.25f * s;
                        if (!TopDown) Pitch -= delta.y * 0.2f * s;
                    }
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _dragging = false;
                if (!_downOverUi && !_moved && Time.unscaledTime - _downTime < 0.35f) Tap(pos);
            }
            if (!left && !middle) _dragging = false;
            _lastPointer = pos;

            float scroll = mouse.scroll.ReadValue().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f)
            {
                // le rotelle danno ±120 per scatto (Windows) o valori piccoli e continui (trackpad, web)
                float steps = Mathf.Clamp(scroll / 120f, -3f, 3f);
                if (Mathf.Abs(scroll) < 10f) steps = Mathf.Clamp(scroll * 0.08f, -1f, 1f);
                ZoomAt(pos, Mathf.Pow(0.85f, steps));
            }
        }

        private void HandleKeyboard(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            var move = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
            if (move != Vector2.zero)
            {
                var fwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
                var right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
                Target += (fwd * move.y + right * move.x) * (Distance * 0.9f * dt);
                _flyTo = false;
            }
            if (kb.qKey.isPressed) Pitch += 50f * dt;
            if (kb.eKey.isPressed) Pitch -= 50f * dt;
            if (kb.zKey.isPressed) Yaw += 70f * dt;
            if (kb.xKey.isPressed) Yaw -= 70f * dt;
            if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) Distance *= Mathf.Exp(-1.5f * dt);
            if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed)
            {
                if (Distance >= MaxDistance * 0.98f) OverZoom?.Invoke();
                Distance *= Mathf.Exp(1.5f * dt);
            }
            if (kb.mKey.wasPressedThisFrame) SetTopDown(!TopDown);
        }

        private void HandleTouches(float dt)
        {
            var touches = Touch.activeTouches;
            int count = touches.Count;
            if (count == 0)
            {
                _prevTouchCount = 0;
                _dragging = false;
                return;
            }
            if (count == 1)
            {
                var t = touches[0];
                var pos = t.screenPosition;
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began || _prevTouchCount != 1)
                {
                    _downOverUi = IsOverUi(pos);
                    _downPos = pos;
                    _downTime = Time.unscaledTime;
                    _moved = false;
                    _lastPointer = pos;
                    _panVelocity = Vector3.zero;
                    if (_prevTouchCount > 1) _moved = true; // fine di un gesto a due dita: niente tocco
                }
                else if (!_downOverUi)
                {
                    if ((pos - _downPos).magnitude > 10f * Dpi / 160f) _moved = true;
                    if (_moved)
                    {
                        _dragging = true;
                        PanByScreen(_lastPointer, pos, dt);
                    }
                }
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    _dragging = false;
                    if (!_downOverUi && !_moved && Time.unscaledTime - _downTime < 0.35f) Tap(pos);
                }
                _lastPointer = pos;
            }
            else
            {
                var a = touches[0].screenPosition;
                var b = touches[1].screenPosition;
                var mid = (a + b) * 0.5f;
                float pinch = (a - b).magnitude;
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                if (_prevTouchCount < 2)
                {
                    _downOverUi = IsOverUi(mid);
                }
                else if (!_downOverUi && pinch > 1f && _prevPinch > 1f)
                {
                    float zoom = _prevPinch / pinch;
                    ZoomAt(mid, zoom);
                    // come su Mappe: la città gira insieme alle dita (dita in senso orario → città in senso orario)
                    Yaw += Mathf.DeltaAngle(_prevAngle, angle);
                    var dm = mid - _prevMid;
                    // due dita che salgono o scendono insieme: inclinazione
                    if (Mathf.Abs(zoom - 1f) < 0.02f && Mathf.Abs(dm.y) > Mathf.Abs(dm.x) * 1.5f && !TopDown)
                        Pitch -= dm.y * 0.18f * 160f / Dpi;
                    else
                        PanByScreen(_prevMid, mid, dt);
                }
                _prevPinch = pinch;
                _prevAngle = angle;
                _prevMid = mid;
                _moved = true;
            }
            _prevTouchCount = count;
        }

        private void Tap(Vector2 pos)
        {
            bool doubleTap = Time.unscaledTime - _lastTapTime < 0.32f && (pos - _lastTapPos).magnitude < 30f * Dpi / 160f;
            _lastTapTime = Time.unscaledTime;
            _lastTapPos = pos;
            if (doubleTap)
            {
                // doppio tocco: centra e avvicina
                if (GroundPoint(pos, out var p))
                {
                    FlyTo(p, Distance * 0.6f);
                }
                return;
            }
            Tapped?.Invoke(pos);
        }
    }
}
