using UnityEngine;

namespace Orbis.UI.Menu3D
{
    /// <summary>
    /// Telecamera del menu: gira lentamente attorno alla città e si allontana quanto basta
    /// perché il centro storico stia nello schermo, sia in orizzontale sia in verticale.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MenuCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 target = new Vector3(0f, 3.2f, 0f);
        [SerializeField] private float fitRadius = 19f;
        [SerializeField] private float pitch = 23f;
        [SerializeField] private float degreesPerSecond = 3.5f;
        [SerializeField] private float startYaw = 205f;

        private Camera _camera;
        private float _yaw;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _yaw = startYaw;
            Apply();
        }

        private void LateUpdate()
        {
            _yaw += degreesPerSecond * Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            var aspect = Mathf.Max(0.1f, _camera.aspect);
            // in verticale si allarga il campo visivo, altrimenti la città diventa minuscola
            _camera.fieldOfView = aspect < 1f ? 50f : 34f;
            var halfV = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            var distance = fitRadius / Mathf.Sin(Mathf.Min(halfV, halfH));
            var bob = Mathf.Sin(Time.time * 0.25f) * 0.6f;
            var rotation = Quaternion.Euler(pitch, _yaw, 0f);
            transform.position = target + Vector3.up * bob - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
            _camera.farClipPlane = distance + 120f;
        }
    }
}
