using Orbis.UI.Glass;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Orbis.UI.Menu3D
{
    /// <summary>
    /// Fornisce al "liquid glass" la scena 3D sfocata: una seconda telecamera, figlia di quella principale,
    /// disegna la scena a bassa risoluzione (senza ombre); ogni frame la si sfoca con GlassBlur e la si passa a
    /// <see cref="GlassBackdrop"/>. Il vetro così mostra davvero ciò che c'è dietro.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GlassBlurCapture : MonoBehaviour
    {
        private static readonly int DirectionId = Shader.PropertyToID("_Direction");

        [SerializeField] private Material blurMaterial;
        [SerializeField, Range(2, 16)] private int downsample = 6;

        private Camera _main;
        private Camera _blurCamera;
        private RenderTexture _scene;
        private RenderTexture _temp;
        private RenderTexture _output;
        private bool _hasFrame;

        public bool IsActive => _blurCamera != null && _active;

        /// <summary>Ogni quanti frame si aggiorna lo sfondo del vetro (nel mondo: 3, per risparmiare GPU).</summary>
        public int Interval = 1;

        private bool _active;
        private int _frame;

        /// <summary>Avvia la cattura (dopo che la città è stata costruita).</summary>
        public void Activate()
        {
            if (blurMaterial == null)
            {
                Debug.LogWarning("[Orbis] Materiale GlassBlur mancante: il vetro resta sullo sfondo fisso.");
                return;
            }
            _main = GetComponent<Camera>();
            if (_blurCamera == null)
            {
                var go = new GameObject("Glass Blur Camera");
                go.transform.SetParent(transform, false);
                _blurCamera = go.AddComponent<Camera>();
                _blurCamera.CopyFrom(_main);
                _blurCamera.depth = _main.depth - 1;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
            }
            EnsureTargets();
            _blurCamera.enabled = true;
            _active = true;
        }

        private void EnsureTargets()
        {
            var w = Mathf.Max(32, Screen.width / downsample);
            var h = Mathf.Max(32, Screen.height / downsample);
            if (_scene != null && _scene.width == w && _scene.height == h)
                return;
            Release();
            _scene = Create("GlassScene", w, h, 16);
            _temp = Create("GlassTemp", w, h, 0);
            _output = Create("GlassBlurred", w, h, 0);
            _blurCamera.targetTexture = _scene;
            _hasFrame = false;
        }

        private static RenderTexture Create(string name, int w, int h, int depth)
        {
            var rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        private void LateUpdate()
        {
            if (!IsActive)
                return;
            _blurCamera.fieldOfView = _main.fieldOfView;
            _blurCamera.nearClipPlane = _main.nearClipPlane;
            _blurCamera.farClipPlane = _main.farClipPlane;
            _blurCamera.orthographic = _main.orthographic;
            _blurCamera.orthographicSize = _main.orthographicSize;
            // la telecamera di sfocatura disegna solo un frame ogni Interval: si sfoca solo dopo un frame disegnato
            bool rendered = _blurCamera.enabled;
            _frame++;
            _blurCamera.enabled = Interval <= 1 || _frame % Interval == 0;
            if (!rendered)
                return;
            EnsureTargets();
            if (!_hasFrame)
            {
                // il primo frame non è ancora stato disegnato: si sfoca dal prossimo
                _hasFrame = true;
                return;
            }
            // due iterazioni (orizzontale + verticale), la seconda più larga, l'ultima con la "vibrancy"
            blurMaterial.SetVector(DirectionId, new Vector4(1f, 0f, 0f, 0f));
            Graphics.Blit(_scene, _temp, blurMaterial, 0);
            blurMaterial.SetVector(DirectionId, new Vector4(0f, 1f, 0f, 0f));
            Graphics.Blit(_temp, _scene, blurMaterial, 0);
            blurMaterial.SetVector(DirectionId, new Vector4(2f, 0f, 0f, 0f));
            Graphics.Blit(_scene, _temp, blurMaterial, 0);
            blurMaterial.SetVector(DirectionId, new Vector4(0f, 2f, 0f, 0f));
            Graphics.Blit(_temp, _output, blurMaterial, 1);
            if (!GlassBackdrop.IsLive)
                GlassBackdrop.SetLive(_output);
        }

        private void OnDestroy()
        {
            GlassBackdrop.SetLive(null);
            Release();
        }

        private void Release()
        {
            foreach (var rt in new[] { _scene, _temp, _output })
            {
                if (rt == null) continue;
                rt.Release();
                Destroy(rt);
            }
            _scene = _temp = _output = null;
            if (GlassBackdrop.IsLive)
                GlassBackdrop.SetLive(null);
        }
    }
}
