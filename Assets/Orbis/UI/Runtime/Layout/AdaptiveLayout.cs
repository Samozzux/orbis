using Orbis.Core.Layout;
using Orbis.Platform;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Layout
{
    /// <summary>
    /// Adatta l'interfaccia allo schermo: 1 unità UI = 1 punto logico, classe di layout
    /// (layout-compact / layout-medium / layout-wide), orientamento e safe area (Dynamic Island, notch).
    /// </summary>
    public sealed class AdaptiveLayout
    {
        private readonly PanelSettings _panelSettings;
        private readonly IDeviceProfile _device;
        private readonly VisualElement _root;
        private readonly VisualElement _safeArea;
        private int _lastWidth = -1;
        private int _lastHeight = -1;
        private Rect _lastSafeArea;

        public AdaptiveLayout(PanelSettings panelSettings, IDeviceProfile device, VisualElement root, VisualElement safeArea)
        {
            _panelSettings = panelSettings;
            _device = device;
            _root = root;
            _safeArea = safeArea;
        }

        public ScreenMetrics Metrics { get; private set; }

        /// <summary>Da chiamare ogni frame: lavora solo se dimensioni o safe area cambiano.</summary>
        public void Update(bool force = false)
        {
            var w = Screen.width;
            var h = Screen.height;
            var safe = Screen.safeArea;
            if (!force && w == _lastWidth && h == _lastHeight && safe == _lastSafeArea)
                return;
            _lastWidth = w;
            _lastHeight = h;
            _lastSafeArea = safe;

            _device.Refresh();
            Metrics = ScreenClassifier.Measure(w, h, _device.Info.PixelsPerPoint);
            var ppp = Metrics.PixelsPerPoint;
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.scale = ppp;

            _root.EnableInClassList("layout-compact", Metrics.Class == ScreenClass.Compact);
            _root.EnableInClassList("layout-medium", Metrics.Class == ScreenClass.Medium);
            _root.EnableInClassList("layout-wide", Metrics.Class == ScreenClass.Wide);
            _root.EnableInClassList("orientation-portrait", Metrics.Orientation == ScreenOrientationKind.Portrait);
            _root.EnableInClassList("input-touch", _device.Info.IsTouchPrimary);
            _root.EnableInClassList("input-pointer", !_device.Info.IsTouchPrimary);

            if (_safeArea != null && w > 0 && h > 0)
            {
                _safeArea.style.paddingLeft = safe.xMin / ppp;
                _safeArea.style.paddingRight = (w - safe.xMax) / ppp;
                _safeArea.style.paddingTop = (h - safe.yMax) / ppp;
                _safeArea.style.paddingBottom = safe.yMin / ppp;
            }
        }
    }
}
