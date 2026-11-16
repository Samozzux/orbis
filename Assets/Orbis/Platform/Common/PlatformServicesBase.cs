using Orbis.Core.Device;
using Orbis.Core.Persistence;

namespace Orbis.Platform
{
    /// <summary>Base comune: ogni piattaforma parte dai ripieghi e sostituisce ciò che sa fare.</summary>
    public abstract class PlatformServicesBase : IPlatformServices
    {
        protected PlatformServicesBase(PlatformKind kind)
        {
            Kind = kind;
        }

        public PlatformKind Kind { get; }
        public ILocalLanguageModel LanguageModel { get; protected set; } = new UnavailableLanguageModel();
        public IBiometricAuth Biometrics { get; protected set; } = new UnavailableBiometricAuth();
        public ISecureStore SecureStore { get; protected set; } = new MemorySecureStore();
        public ISpeech Speech { get; protected set; } = new SilentSpeech();
        public IUserMusic UserMusic { get; protected set; } = new NoUserMusic();
        public ISaveStore SaveStore { get; protected set; } = new MemorySaveStore();
        public IHaptics Haptics { get; protected set; } = new NoHaptics();
        public INotifications Notifications { get; protected set; } = new NoNotifications();
        public IDeviceProfile DeviceProfile { get; protected set; }
    }
}
