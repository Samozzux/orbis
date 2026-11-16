using System;
using System.Threading;
using System.Threading.Tasks;
using Orbis.Core.Device;
using Orbis.Core.Persistence;
using Orbis.Core.Settings;

// Servizi di piattaforma (docs/01 §10). Tutto ciò che è nativo passa da queste interfacce:
// nessuna chiamata nativa fuori dalla cartella Assets/Orbis/Platform.
namespace Orbis.Platform
{
    /// <summary>Insieme dei servizi disponibili sulla piattaforma corrente.</summary>
    public interface IPlatformServices
    {
        PlatformKind Kind { get; }
        ILocalLanguageModel LanguageModel { get; }
        IBiometricAuth Biometrics { get; }
        ISecureStore SecureStore { get; }
        ISpeech Speech { get; }
        IUserMusic UserMusic { get; }
        ISaveStore SaveStore { get; }
        IHaptics Haptics { get; }
        INotifications Notifications { get; }
        IDeviceProfile DeviceProfile { get; }
        IScreenOrientation ScreenOrientation { get; }
    }

    // --- IA sul dispositivo (docs/01 §9) ---

    public sealed class LanguageModelOptions
    {
        public float Temperature { get; set; } = 0.7f;
        public int MaxTokens { get; set; } = 512;
    }

    public sealed class LanguageModelResult
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public interface ILocalLanguageModel
    {
        /// <summary>Falso dove non c'è un modello locale: si usano template e regole.</summary>
        bool IsAvailable { get; }

        /// <summary>Genera testo; con <paramref name="jsonSchema"/> l'output è JSON guidato dallo schema.</summary>
        Task<LanguageModelResult> GenerateAsync(string prompt, string jsonSchema, LanguageModelOptions options, CancellationToken cancellationToken);
    }

    // --- Accesso biometrico ---

    public enum BiometricKind
    {
        None,
        FaceId,
        TouchId,
        AndroidBiometric,
        Passkey,
    }

    public interface IBiometricAuth
    {
        BiometricKind Kind { get; }
        bool IsAvailable { get; }

        /// <summary>Salva un segreto protetto dalla biometria (es. il token di rinnovo).</summary>
        Task<bool> StoreProtectedAsync(string key, string secret);

        /// <summary>Chiede la biometria e restituisce il segreto, o null se negata/annullata.</summary>
        Task<string> ReadProtectedAsync(string key, string reason);

        Task DeleteProtectedAsync(string key);
    }

    // --- Archivio sicuro (Keychain / Keystore / WebCrypto) ---

    public interface ISecureStore
    {
        /// <summary>Falso se i dati restano solo in memoria (ripiego temporaneo).</summary>
        bool IsPersistent { get; }
        Task SetAsync(string key, string value);
        Task<string> GetAsync(string key);
        Task DeleteAsync(string key);
    }

    // --- Sintesi vocale ---

    public interface ISpeech
    {
        bool IsAvailable { get; }
        bool IsSpeaking { get; }
        void Speak(string text, float rate = 1f);
        void Stop();
    }

    // --- "La mia musica" ---

    public enum UserMusicSource
    {
        None,
        AppleMusic,
        DeviceFiles,
    }

    public interface IUserMusic
    {
        UserMusicSource Source { get; }
        bool IsAvailable { get; }
        Task<bool> RequestAccessAsync();
    }

    // --- Vibrazione ---

    public enum HapticKind
    {
        Selection,
        Light,
        Medium,
        Heavy,
        Success,
        Warning,
    }

    public interface IHaptics
    {
        bool IsAvailable { get; }
        void Play(HapticKind kind);
    }

    // --- Notifiche locali ---

    public interface INotifications
    {
        bool IsAvailable { get; }
        Task<bool> RequestPermissionAsync();
        void Schedule(string id, string title, string body, TimeSpan delay);
        void Cancel(string id);
    }

    // --- Orientamento dello schermo ---

    public interface IScreenOrientation
    {
        /// <summary>Vero nelle app iOS/Android; falso su web, desktop ed editor (l'orientamento segue il dispositivo).</summary>
        bool CanControl { get; }

        /// <summary>Orientamento attuale dello schermo.</summary>
        FixedOrientation Current { get; }

        /// <summary>Applica rotazione automatica o blocco scelti nelle impostazioni.</summary>
        void Apply(DisplaySettings settings);
    }

    // --- Profilo del dispositivo ---

    public interface IDeviceProfile
    {
        /// <summary>Dati letti all'avvio; <see cref="DeviceInfo.PixelsPerPoint"/> si aggiorna con <see cref="Refresh"/>.</summary>
        DeviceInfo Info { get; }
        void Refresh();
    }
}
