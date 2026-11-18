using System.IO;
using Orbis.Core.Device;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>
    /// iOS. Keychain e Face ID col plugin Swift (M3); Foundation Models, MediaPlayer e sintesi vocale
    /// arrivano con le milestone che li usano (M7).
    /// </summary>
    public sealed class IosPlatformServices : PlatformServicesBase
    {
        public IosPlatformServices() : base(PlatformKind.Ios)
        {
            SaveStore = new FileSaveStore(Path.Combine(Application.persistentDataPath, "store"));
            SecureStore = new IosKeychainStore();
            Biometrics = new IosBiometricAuth();
            ScreenOrientation = new MobileScreenOrientation();
            DeviceProfile = new UnityDeviceProfile(Kind, UnityDeviceProfile.IosPixelsPerPoint, () => true);
        }
    }
}
