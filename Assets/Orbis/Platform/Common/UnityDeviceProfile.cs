using System;
using Orbis.Core.Device;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>Profilo del dispositivo letto dalle API Unity (SystemInfo/Screen), valido su tutte le piattaforme.</summary>
    public sealed class UnityDeviceProfile : IDeviceProfile
    {
        private readonly Func<float> _pixelsPerPoint;
        private readonly Func<bool> _touchPrimary;

        public UnityDeviceProfile(PlatformKind kind, Func<float> pixelsPerPoint, Func<bool> touchPrimary)
        {
            _pixelsPerPoint = pixelsPerPoint ?? (() => 1f);
            _touchPrimary = touchPrimary ?? (() => false);
            Info = new DeviceInfo
            {
                Platform = kind,
                Model = SystemInfo.deviceModel ?? string.Empty,
                OperatingSystem = SystemInfo.operatingSystem ?? string.Empty,
                SystemMemoryMb = SystemInfo.systemMemorySize,
                GraphicsMemoryMb = SystemInfo.graphicsMemorySize,
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                GraphicsDevice = SystemInfo.graphicsDeviceName ?? string.Empty,
                ProcessorCount = SystemInfo.processorCount,
            };
            Refresh();
        }

        public DeviceInfo Info { get; }

        public void Refresh()
        {
            Info.PixelsPerPoint = Mathf.Max(1f, _pixelsPerPoint());
            Info.IsTouchPrimary = _touchPrimary();
        }

        // Densità di riferimento: 1 pt iOS ≈ 1/163", 1 dp Android = 1/160", 1 px CSS = 1/96".
        public static float IosPixelsPerPoint() => Screen.dpi > 0f ? Mathf.Max(1f, Mathf.Round(Screen.dpi / 163f)) : 2f;
        public static float AndroidPixelsPerPoint() => Screen.dpi > 0f ? Screen.dpi / 160f : 2f;
        public static float DesktopPixelsPerPoint() => Screen.dpi > 0f ? Mathf.Max(1f, Mathf.Round(Screen.dpi / 96f * 4f) / 4f) : 1f;
    }
}
