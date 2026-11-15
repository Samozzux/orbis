using System.IO;
using Orbis.Core.Device;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>
    /// iOS. In M0 solo ripieghi; i plugin Swift (Foundation Models, Face ID/Keychain, MediaPlayer,
    /// sintesi vocale) arrivano con le milestone che li usano (M3, M7).
    /// </summary>
    public sealed class IosPlatformServices : PlatformServicesBase
    {
        public IosPlatformServices() : base(PlatformKind.Ios)
        {
            SaveStore = new FileSaveStore(Path.Combine(Application.persistentDataPath, "store"));
            DeviceProfile = new UnityDeviceProfile(Kind, UnityDeviceProfile.IosPixelsPerPoint, () => true);
        }
    }
}
