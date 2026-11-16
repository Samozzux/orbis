using System.IO;
using Orbis.Core.Device;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>
    /// Android. In M0 solo ripieghi; i plugin Kotlin (BiometricPrompt/Keystore, sintesi vocale,
    /// file audio, IA di sistema) arrivano con le milestone che li usano (M3, M7).
    /// </summary>
    public sealed class AndroidPlatformServices : PlatformServicesBase
    {
        public AndroidPlatformServices() : base(PlatformKind.Android)
        {
            SaveStore = new FileSaveStore(Path.Combine(Application.persistentDataPath, "store"));
            ScreenOrientation = new MobileScreenOrientation();
            DeviceProfile = new UnityDeviceProfile(Kind, UnityDeviceProfile.AndroidPixelsPerPoint, () => true);
        }
    }
}
