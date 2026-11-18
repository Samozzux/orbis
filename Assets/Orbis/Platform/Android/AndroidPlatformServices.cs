using System.IO;
using Orbis.Core.Device;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>
    /// Android. Keystore e BiometricPrompt col plugin Kotlin (M3); sintesi vocale, file audio e IA di
    /// sistema arrivano con le milestone che li usano (M7).
    /// </summary>
    public sealed class AndroidPlatformServices : PlatformServicesBase
    {
        public AndroidPlatformServices() : base(PlatformKind.Android)
        {
            SaveStore = new FileSaveStore(Path.Combine(Application.persistentDataPath, "store"));
            SecureStore = new AndroidKeystoreStore();
            Biometrics = new AndroidBiometricAuth();
            ScreenOrientation = new MobileScreenOrientation();
            DeviceProfile = new UnityDeviceProfile(Kind, UnityDeviceProfile.AndroidPixelsPerPoint, () => true);
        }
    }
}
