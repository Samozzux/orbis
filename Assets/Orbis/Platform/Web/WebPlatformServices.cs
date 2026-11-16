using Orbis.Core.Device;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Orbis.Platform
{
    /// <summary>
    /// Web (browser). Salvataggi in memoria finché in M2 non arriva l'archivio IndexedDB;
    /// densità dello schermo e tipo di puntatore letti dal browser (Assets/Plugins/WebGL/OrbisWeb.jslib).
    /// </summary>
    public sealed class WebPlatformServices : PlatformServicesBase
    {
        public WebPlatformServices() : base(PlatformKind.Web)
        {
            DeviceProfile = new UnityDeviceProfile(Kind, PixelsPerPoint, IsTouchPrimary);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern float OrbisWeb_GetPixelsPerPoint();
        [DllImport("__Internal")] private static extern int OrbisWeb_IsTouchPrimary();
        [DllImport("__Internal")] private static extern int OrbisWeb_HasWebGpu();

        private static float PixelsPerPoint() => OrbisWeb_GetPixelsPerPoint();
        private static bool IsTouchPrimary() => OrbisWeb_IsTouchPrimary() != 0;

        /// <summary>Vero se il browser espone WebGPU (navigator.gpu).</summary>
        public static bool HasWebGpu => OrbisWeb_HasWebGpu() != 0;
#else
        private static float PixelsPerPoint() => 1f;
        private static bool IsTouchPrimary() => false;
        public static bool HasWebGpu => false;
#endif
    }
}
