using System;
using UnityEngine;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Riassume nella console, al massimo ogni 10 secondi, i fotogrammi lunghi (sopra 250 ms) con le raccolte della
    /// memoria e la memoria gestita: sul web il garbage collector non è incrementale e un lavoro lungo blocca la pagina.
    /// Con la scheda in secondo piano il browser rallenta i fotogrammi a uno al secondo: quelli non si contano.
    /// </summary>
    public sealed class FrameWatch : MonoBehaviour
    {
        private int _lastGc, _gcSinceReport, _long;
        private float _worst, _nextReport;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            int gc = GC.CollectionCount(0);
            _gcSinceReport += gc - _lastGc;
            _lastGc = gc;
            if (dt > 0.25f && Application.isFocused && Mathf.Abs(dt - 1f) > 0.05f)
            {
                _long++;
                _worst = Mathf.Max(_worst, dt);
            }
            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 10f;
            if (_long > 0)
                Debug.Log($"[Orbis] fotogrammi lunghi negli ultimi 10 s: {_long} (il più lungo {_worst * 1000:0} ms), " +
                          $"raccolte della memoria {_gcSinceReport}, memoria gestita {GC.GetTotalMemory(false) / 1048576} MB");
            _long = 0;
            _worst = 0;
            _gcSinceReport = 0;
        }
    }
}
