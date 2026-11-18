using System.IO;
using Orbis.Core.Device;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Orbis.Platform
{
    /// <summary>
    /// Web (browser). Salvataggi su file in persistentDataPath, che Unity tiene in IndexedDB (sincronizzato
    /// dopo ogni scrittura); densità dello schermo e tipo di puntatore letti dal browser (OrbisWeb.jslib).
    /// </summary>
    public sealed class WebPlatformServices : PlatformServicesBase
    {
        public WebPlatformServices() : base(PlatformKind.Web)
        {
            DeviceProfile = new UnityDeviceProfile(Kind, PixelsPerPoint, IsTouchPrimary);
            SaveStore = new FileSaveStore(Path.Combine(Application.persistentDataPath, "store"), SyncFs);
            // refresh token nella memoria del sito (IndexedDB): ripiego della specifica finché non arrivano le passkey
            SecureStore = new FileSecureStore(Path.Combine(Application.persistentDataPath, "secure"), SyncFs);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern float OrbisWeb_GetPixelsPerPoint();
        [DllImport("__Internal")] private static extern int OrbisWeb_IsTouchPrimary();
        [DllImport("__Internal")] private static extern int OrbisWeb_HasWebGpu();
        [DllImport("__Internal")] private static extern void OrbisWeb_SyncFs();

        private static void SyncFs() => OrbisWeb_SyncFs();

        private static float PixelsPerPoint() => OrbisWeb_GetPixelsPerPoint();
        private static bool IsTouchPrimary() => OrbisWeb_IsTouchPrimary() != 0;

        /// <summary>Vero se il browser espone WebGPU (navigator.gpu).</summary>
        public static bool HasWebGpu => OrbisWeb_HasWebGpu() != 0;
#else
        private static void SyncFs() { }
        private static float PixelsPerPoint() => 1f;
        private static bool IsTouchPrimary() => false;
        public static bool HasWebGpu => false;
#endif
    }
}
