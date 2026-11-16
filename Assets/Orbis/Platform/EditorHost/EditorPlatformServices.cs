using System.IO;
using Orbis.Core.Device;
using UnityEngine;

// Cartella "EditorHost" e non "Editor": in Unity il nome "Editor" ha un significato speciale.
namespace Orbis.Platform
{
    /// <summary>Editor Unity e build desktop: tutto con i ripieghi, salvataggi su file.</summary>
    public sealed class EditorPlatformServices : PlatformServicesBase
    {
        public EditorPlatformServices() : base(Application.isEditor ? PlatformKind.Editor : PlatformKind.Desktop)
        {
            SaveStore = new FileSaveStore(Path.Combine(Application.persistentDataPath, "store"));
            DeviceProfile = new UnityDeviceProfile(Kind, UnityDeviceProfile.DesktopPixelsPerPoint, () => false);
        }
    }
}
