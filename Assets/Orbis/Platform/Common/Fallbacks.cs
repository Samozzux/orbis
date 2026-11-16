using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Ripieghi usati quando la piattaforma non offre (ancora) un servizio (docs/01 §10, colonna "Ripiego").
namespace Orbis.Platform
{
    public sealed class UnavailableLanguageModel : ILocalLanguageModel
    {
        public bool IsAvailable => false;

        public Task<LanguageModelResult> GenerateAsync(string prompt, string jsonSchema, LanguageModelOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(new LanguageModelResult { Success = false, Error = "unavailable" });
    }

    /// <summary>Nessuna biometria: si resta sulla sessione lunga con token di rinnovo.</summary>
    public sealed class UnavailableBiometricAuth : IBiometricAuth
    {
        public BiometricKind Kind => BiometricKind.None;
        public bool IsAvailable => false;
        public Task<bool> StoreProtectedAsync(string key, string secret) => Task.FromResult(false);
        public Task<string> ReadProtectedAsync(string key, string reason) => Task.FromResult<string>(null);
        public Task DeleteProtectedAsync(string key) => Task.CompletedTask;
    }

    /// <summary>Archivio "sicuro" solo in memoria: ripiego temporaneo finché non arrivano Keychain/Keystore/WebCrypto (M3).</summary>
    public sealed class MemorySecureStore : ISecureStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool IsPersistent => false;

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string> GetAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

        public Task DeleteAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>Nessuna voce: il testo resta solo a schermo.</summary>
    public sealed class SilentSpeech : ISpeech
    {
        public bool IsAvailable => false;
        public bool IsSpeaking => false;
        public void Speak(string text, float rate = 1f) { }
        public void Stop() { }
    }

    /// <summary>Solo libreria musicale inclusa.</summary>
    public sealed class NoUserMusic : IUserMusic
    {
        public UserMusicSource Source => UserMusicSource.None;
        public bool IsAvailable => false;
        public Task<bool> RequestAccessAsync() => Task.FromResult(false);
    }

    public sealed class NoHaptics : IHaptics
    {
        public bool IsAvailable => false;
        public void Play(HapticKind kind) { }
    }

    public sealed class NoNotifications : INotifications
    {
        public bool IsAvailable => false;
        public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
        public void Schedule(string id, string title, string body, TimeSpan delay) { }
        public void Cancel(string id) { }
    }
}
